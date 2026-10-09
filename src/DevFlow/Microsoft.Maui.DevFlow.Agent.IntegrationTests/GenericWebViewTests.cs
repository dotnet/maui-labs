using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using SkiaSharp;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "WebView")]
[Trait(TestFramework.Trait, TestFramework.Maui)]
public class GenericWebViewTests(AppFixture app, ITestOutputHelper output)
    : IntegrationTestBase(app, output), IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        Assert.True(await Client.NavigateAsync("//native"));
        App.InvalidateBlazorReady();
    }

    [Theory]
    [InlineData("webview", "StandardWebView", "Standard WebView fixture")]
    [InlineData("hybrid", "HybridWebView", "Hybrid WebView fixture")]
    public async Task Context_GenericHost_IsCanonicalReadyAndCorrelated(string kind, string id, string title)
    {
        var context = await EnsureHostAsync(kind, title);
        Assert.Equal(id, context.GetProperty("automationId").GetString());
        AssertContext(context);
        var element = await FindElementAsync(id);
        Assert.Equal(element.Id, context.GetProperty("elementId").GetString());
        Assert.Equal(title, (await EvaluateAsync("document.title", ContextId(context))).GetString());
        Assert.Contains(title, await Client.GetCdpSourceAsync(ContextId(context)));
        var status = await Client.GetStatusAsync();
        Assert.NotNull(status);
        Assert.NotNull(status.Capabilities);
        Assert.True(status.Capabilities.WebView);
    }

    [Theory]
    [InlineData("webview", "Standard WebView fixture")]
    [InlineData("hybrid", "Hybrid WebView fixture")]
    public async Task Input_GenericHost_DeliversEventsAndClicksExactlyOnce(string kind, string title)
    {
        var id = ContextId(await EnsureHostAsync(kind, title));
        await AssertInputAndClickAsync(id);
    }

    [Theory]
    [InlineData("webview", "Standard WebView fixture")]
    [InlineData("hybrid", "Hybrid WebView fixture")]
    [InlineData("blazor", "SampleMauiApp")]
    public async Task Failure_SelectedHost_PreservesJavaScriptDetailsAndNeverReplays(string kind, string title)
    {
        JsonElement context;
        if (kind == "blazor")
        {
            await App.EnsureBlazorReadyAsync();
            context = Assert.Single(Contexts(await Client.GetCdpWebViewsAsync()), c => c.GetProperty("active").GetBoolean());
        }
        else
        {
            context = await EnsureHostAsync(kind, title);
        }
        var id = ContextId(context);
        await EvaluateAsync("window.unit5Attempts = 0", id);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Client.SendCdpCommandAsync(
            "Runtime.evaluate",
            new JsonObject { ["expression"] = "window.unit5Attempts++; throw new Error('unit5-exact-js-failure')" }, id));
        Assert.Contains("unit5-exact-js-failure", error.Message);
        Assert.Equal(1, (await EvaluateAsync("window.unit5Attempts", id)).GetInt32());
        var missing = await Assert.ThrowsAsync<HttpRequestException>(
            () => Client.ClickWebViewAsync("#unit5-missing-element", id));
        Assert.Contains("No element matches selector", missing.Message);
        Assert.Contains("#unit5-missing-element", missing.Message);
    }

    [Theory]
    [InlineData("webview", "Standard WebView fixture", 251, 228, 228)]
    [InlineData("hybrid", "Hybrid WebView fixture", 226, 244, 231)]
    public async Task Screenshot_GenericHost_IsDecodedHostSizedAndHostColored(
        string kind, string title, byte red, byte green, byte blue)
    {
        var context = await EnsureHostAsync(kind, title);
        await AssertScreenshotAsync(context, new SKColor(red, green, blue));
    }

    [Theory]
    [InlineData("webview", "Standard WebView fixture")]
    [InlineData("hybrid", "Hybrid WebView fixture")]
    public async Task Reload_GenericHost_ReinjectsAndPreservesNativeInfrastructure(string kind, string title)
    {
        var context = await EnsureHostAsync(kind, title);
        var id = ContextId(context);
        await using var fixture = kind == "webview" ? new WebViewHttpFixture() : null;
        var reloadTitle = fixture is null ? title : "HTTP WebView fixture";
        try
        {
            if (fixture is not null)
            {
                Assert.True(await Client.NavigateWebViewAsync(fixture.GetUrl(Platform), id));
                await WaitForDocumentAsync(kind, reloadTitle);
            }
            Assert.True(await Client.ClickWebViewAsync("#fixture-button", id));
            Assert.Equal(1, (await EvaluateAsync("count", id)).GetInt32());
            await Client.SendCdpCommandAsync("Page.reload", contextId: id);
            context = await WaitForDocumentAsync(kind, reloadTitle, expectedCount: 0);
            Assert.Equal(id, ContextId(context));
            await AssertInputAndClickAsync(id);
            if (kind == "hybrid")
                await AssertHybridMessagingAsync(id, "HybridMessageStatus", "HybridSendMessageButton");
            else
            {
                Assert.StartsWith("Success:", (await FindElementAsync("StandardNavigationStatus")).Text);
                Assert.True(fixture!.RequestPaths.Count(p => p == "/index.html") >= 2);
            }
        }
        finally
        {
            if (fixture is not null)
            {
                Assert.True(await Client.TapAsync((await FindElementAsync("ResetStandardWebViewButton")).Id));
                await WaitForDocumentAsync(kind, title);
            }
        }
    }

    [Theory]
    [InlineData("webview", "Standard WebView fixture", "RecreateStandardWebViewButton")]
    [InlineData("hybrid", "Hybrid WebView fixture", "ResetHybridWebViewButton")]
    public async Task Recreate_GenericHost_RemovesOldRegistrationAndState(string kind, string title, string buttonId)
    {
        var previous = await EnsureHostAsync(kind, title);
        Assert.True(await Client.ClickWebViewAsync("#fixture-button", ContextId(previous)));
        Assert.True(await Client.TapAsync((await FindElementAsync(buttonId)).Id));
        var current = await WaitForDocumentAsync(kind, title, ContextId(previous));
        Assert.NotEqual(ContextId(previous), ContextId(current));
        Assert.Equal((await FindElementAsync(current.GetProperty("automationId").GetString()!)).Id,
            current.GetProperty("elementId").GetString());
        Assert.Equal(0, (await EvaluateAsync("count", ContextId(current))).GetInt32());
        var contexts = await Client.GetCdpWebViewsAsync();
        Assert.DoesNotContain(Contexts(contexts), c => ContextId(c) == ContextId(previous));
        using var stale = await GetRawAsync($"/api/v1/webview/source?contextId={ContextId(previous)}");
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        await AssertInputAndClickAsync(ContextId(current));
    }

    [Theory]
    [InlineData("webview", "Standard WebView fixture")]
    [InlineData("hybrid", "Hybrid WebView fixture")]
    public async Task Layout_GenericHost_ReportsMeasuredDomDefectsAndHonestCapabilities(string kind, string title)
    {
        var context = await EnsureHostAsync(kind, title);
        var capabilities = (await Client.GetCapabilitiesAsync()).GetProperty("capabilities");
        var layout = capabilities.GetProperty("ui.layoutDiagnostics");
        Assert.True(layout.GetProperty("webview").GetProperty("supported").GetBoolean());
        Assert.False(layout.TryGetProperty("blazor", out _));
        Assert.Contains(layout.GetProperty("features").EnumerateArray(), f => f.GetString() == "webview-dom");
        Assert.DoesNotContain(capabilities.GetProperty("webview").GetProperty("features").EnumerateArray(),
            f => f.GetString() == "network");
        using var network = await GetRawAsync("/api/v1/webview/network");
        Assert.Equal(HttpStatusCode.NotImplemented, network.StatusCode);
        var result = await AnalyzeAsync(includeDom: true);
        var text = Assert.Single(result.Findings, f => f.Element.AutomationId == "fixture-overflow"
            && f.RuleId == LayoutDiagnosticRules.TextNotFullyRendered);
        Assert.StartsWith($"web-{context.GetProperty("index").GetInt32()}-", text.Element.Id);
        Assert.Equal(context.GetProperty("elementId").GetString(), text.Element.ParentId);
        var clip = Assert.Single(result.Findings, f => f.Element.AutomationId == "fixture-clipped"
            && f.RuleId == LayoutDiagnosticRules.ElementClipped);
        Assert.NotNull(clip.Evidence?.LostAreaRatio);
        Assert.InRange(clip.Evidence!.LostAreaRatio!.Value, 0.74, 0.76);
        Assert.Contains(clip.Evidence.ClipChain, c => c.Kind == "ancestor-layout-clip");
        var nativeOnly = await AnalyzeAsync(includeDom: false);
        Assert.DoesNotContain(nativeOnly.Findings, f => f.Element.Id.StartsWith("web-", StringComparison.Ordinal));
        Assert.Equal(result.Snapshot.TreeRevision, nativeOnly.Snapshot.TreeRevision);
        Assert.True(nativeOnly.Snapshot.NodeCount < result.Snapshot.NodeCount);
    }

    [Fact]
    public async Task HybridMessaging_OriginalReadyAndRoundTripSurviveNavigationReloadAndRecreation()
    {
        var context = await EnsureHostAsync("hybrid", "Hybrid WebView fixture");
        await WaitForAsync(async () => (await FindElementAsync("HybridMessageStatus")).Text == "ready: Hybrid WebView fixture");
        await AssertHybridMessagingAsync(ContextId(context), "HybridMessageStatus", "HybridSendMessageButton");
        Assert.True(await Client.ClickWebViewAsync("#fixture-link", ContextId(context)));
        context = await WaitForDocumentAsync("hybrid", "Second WebView document");
        await WaitForAsync(async () => (await FindElementAsync("HybridMessageStatus")).Text == "ready: Second WebView document");
        Assert.EndsWith("/second.html", (await EvaluateAsync("location.pathname", ContextId(context))).GetString());
        Assert.Equal("Second document loaded",
            (await EvaluateAsync("document.querySelector('#second-document').textContent", ContextId(context))).GetString());
        await AssertHybridMessagingAsync(ContextId(context), "HybridMessageStatus", "HybridSendMessageButton");
        await Client.SendCdpCommandAsync("Page.reload", contextId: ContextId(context));
        context = await WaitForDocumentAsync("hybrid", "Second WebView document", expectedNativeMessage: "none");
        await AssertHybridMessagingAsync(ContextId(context), "HybridMessageStatus", "HybridSendMessageButton");
        Assert.True(await Client.NavigateWebViewAsync("index.html", ContextId(context)));
        context = await WaitForDocumentAsync("hybrid", "Hybrid WebView fixture");
        await AssertInputAndClickAsync(ContextId(context));
        var previousId = ContextId(context);
        Assert.True(await Client.TapAsync((await FindElementAsync("ResetHybridWebViewButton")).Id));
        context = await WaitForDocumentAsync("hybrid", "Hybrid WebView fixture", previousId);
        Assert.NotEqual(previousId, ContextId(context));
        await AssertHybridMessagingAsync(ContextId(context), "HybridMessageStatus", "HybridSendMessageButton");
    }

    [Fact]
    public async Task Navigate_StandardHttp_RootRelativeDocumentReloadAndNativeRestoration()
    {
        var context = await EnsureHostAsync("webview", "Standard WebView fixture");
        await using var fixture = new WebViewHttpFixture();
        using var http = new HttpClient();
        using var readiness = await http.GetAsync(fixture.GetUrl("maccatalyst"));
        readiness.EnsureSuccessStatusCode();
        try
        {
            Assert.True(await Client.NavigateWebViewAsync(fixture.GetUrl(Platform), ContextId(context)));
            context = await WaitForDocumentAsync("webview", "HTTP WebView fixture");
            await AssertInputAndClickAsync(ContextId(context));
            Assert.True(await Client.NavigateWebViewAsync("/second.html", ContextId(context)));
            context = await WaitForDocumentAsync("webview", "Second WebView document");
            Assert.Equal("/second.html", (await EvaluateAsync("location.pathname", ContextId(context))).GetString());
            Assert.Equal("Second document loaded",
                (await EvaluateAsync("document.querySelector('#second-document').textContent", ContextId(context))).GetString());
            await WaitForAsync(async () => (await FindElementAsync("StandardNavigationStatus")).Text ==
                $"Success: {fixture.GetUrl(Platform).Replace("/index.html", "/second.html")}");
            await Client.SendCdpCommandAsync("Page.reload", contextId: ContextId(context));
            context = await WaitForDocumentAsync("webview", "Second WebView document");
            Assert.True(await Client.ClickWebViewAsync("#return-link", ContextId(context)));
            context = await WaitForDocumentAsync("webview", "HTTP WebView fixture");
            Assert.Equal(0, (await EvaluateAsync("count", ContextId(context))).GetInt32());
            Assert.Contains("/index.html", fixture.RequestPaths);
            Assert.True(fixture.RequestPaths.Count(p => p == "/second.html") >= 2);
        }
        finally
        {
            Assert.True(await Client.TapAsync((await FindElementAsync("ResetStandardWebViewButton")).Id));
            await WaitForDocumentAsync("webview", "Standard WebView fixture");
        }
    }

    [Theory]
    [InlineData("webview", "Standard WebView fixture")]
    [InlineData("hybrid", "Hybrid WebView fixture")]
    public async Task Console_GenericHost_DeliversOriginalBrowserEvent(string kind, string title)
    {
        var context = await EnsureHostAsync(kind, title);
        var path = $"/api/v1/webview/console?contextId={ContextId(context)}";
        var before = await GetJsonAsync(path);
        Assert.True(await Client.ClickWebViewAsync("#fixture-log", ContextId(context)));
        var marker = (await EvaluateAsync("document.querySelector('#fixture-log').dataset.lastMarker", ContextId(context))).GetString();
        Assert.NotNull(marker);
        Assert.StartsWith("devflow-generic-webview-console-marker-", marker);
        Assert.DoesNotContain(before.EnumerateArray(), log => log.GetProperty("m").GetString() == marker);
        await WaitForAsync(async () =>
        {
            var console = await GetJsonAsync(path);
            return console.EnumerateArray().Any(log => log.GetProperty("m").GetString() == marker);
        }, timeoutMs: 10000);
    }

    [Fact]
    public async Task HttpFixture_ServesRealResourcesRejectsUnknownPathsAndStopsOwnedListener()
    {
        var fixture = new WebViewHttpFixture();
        var port = fixture.Port;
        try
        {
            using var idleConnection = new System.Net.Sockets.TcpClient();
            await idleConnection.ConnectAsync("127.0.0.1", port);
            using (var abandoned = new System.Net.Sockets.TcpClient())
            {
                await abandoned.ConnectAsync("127.0.0.1", port);
                await WaitForAsync(() => Task.FromResult(fixture.AcceptedConnectionCount >= 2));
                abandoned.Client.LingerState = new System.Net.Sockets.LingerOption(true, 0);
            }
            await WaitForAsync(() => Task.FromResult(!fixture.ClientDisconnects.IsEmpty));
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var html = await http.GetStringAsync(fixture.GetUrl("maccatalyst"));
            Assert.Contains("HTTP WebView fixture", html);
            Assert.Contains("fixture-button", html);
            using var missing = await http.GetAsync($"http://127.0.0.1:{port}/missing.html");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
        using var socket = new System.Net.Sockets.TcpClient();
        await Assert.ThrowsAsync<System.Net.Sockets.SocketException>(() => socket.ConnectAsync("127.0.0.1", port));
    }

    [Fact]
    public async Task MixedHosts_UnnamedAndDuplicateIds_RouteStateSourceAndNativePngIndependently()
    {
        var active = await EnsureMixedAsync();
        Assert.Equal(3, active.Select(ContextId).Distinct().Count());
        Assert.Equal(3, active.Select(c => c.GetProperty("elementId").GetString()).Distinct().Count());
        foreach (var context in active)
            AssertContext(context);
        var standard = Assert.Single(active, c => HostKind(c) == "webview");
        var hybrid = Assert.Single(active, c => HostKind(c) == "hybrid");
        var blazor = Assert.Single(active, c => HostKind(c) == "blazor");
        Assert.Equal(JsonValueKind.Null, standard.GetProperty("automationId").ValueKind);
        Assert.Equal(hybrid.GetProperty("automationId").GetString(), blazor.GetProperty("automationId").GetString());
        await AssertInputAndClickAsync(ContextId(standard));
        Assert.Equal(0, (await EvaluateAsync("count", ContextId(hybrid))).GetInt32());
        await AssertInputAndClickAsync(ContextId(hybrid));
        Assert.Contains("Mixed standard WebView fixture", await Client.GetCdpSourceAsync(ContextId(standard)));
        Assert.Contains("Hybrid WebView fixture", await Client.GetCdpSourceAsync(ContextId(hybrid)));
        Assert.Contains("todo-container", await Client.GetCdpSourceAsync(ContextId(blazor)));
        await AssertHybridMessagingAsync(ContextId(hybrid), "MixedHybridMessageStatus", "MixedHybridSendMessageButton");
        await AssertBlazorCounterAsync(ContextId(blazor));
        Assert.Equal(1, (await EvaluateAsync("count", ContextId(standard))).GetInt32());
        Assert.Equal(1, (await EvaluateAsync("count", ContextId(hybrid))).GetInt32());
        await AssertScreenshotAsync(standard, new SKColor(251, 228, 228));
        await AssertScreenshotAsync(hybrid, new SKColor(226, 244, 231));
        using var colors = JsonDocument.Parse((await EvaluateAsync(
            """JSON.stringify(getComputedStyle(document.querySelector('.todo-container')).backgroundColor.match(/\d+/g).slice(0, 3).map(Number))""",
            ContextId(blazor))).GetString()!);
        var rgb = colors.RootElement;
        await AssertScreenshotAsync(blazor, new SKColor(rgb[0].GetByte(), rgb[1].GetByte(), rgb[2].GetByte()));
    }

    [Fact]
    public async Task MixedHosts_LayoutScopesKeepNativeDefectsAndRevisionWithoutHiddenDom()
    {
        var active = await EnsureMixedAsync();
        var result = await AnalyzeAsync(includeDom: true);
        foreach (var context in active.Where(c => HostKind(c) != "blazor"))
        {
            var prefix = $"web-{context.GetProperty("index").GetInt32()}-";
            var finding = Assert.Single(result.Findings, f => f.Element.Id.StartsWith(prefix, StringComparison.Ordinal)
                && f.Element.AutomationId == "fixture-overflow" && f.RuleId == LayoutDiagnosticRules.TextNotFullyRendered);
            Assert.Equal(context.GetProperty("elementId").GetString(), finding.Element.ParentId);
            var host = Assert.Single(Flatten(await Client.GetTreeAsync()), e => e.Id == finding.Element.ParentId);
            Assert.NotNull(host.WindowBounds);
            Assert.NotNull(finding.Evidence?.FullRegion);
            Assert.True(finding.Evidence!.FullRegion!.Bounds.X >= host.WindowBounds!.X);
            Assert.True(finding.Evidence.FullRegion.Bounds.Y >= host.WindowBounds.Y);
        }
        var native = Assert.Single(result.Findings, f => f.Element.AutomationId == "WebViewNativeClipped"
            && f.RuleId == LayoutDiagnosticRules.ElementClipped);
        Assert.InRange(Assert.IsType<double>(native.Evidence?.LostAreaRatio), 0.74, 0.76);
        var nativeOnly = await AnalyzeAsync(includeDom: false);
        Assert.Equal(result.Snapshot.TreeRevision, nativeOnly.Snapshot.TreeRevision);
        Assert.DoesNotContain(nativeOnly.Findings, f => f.Element.Id.StartsWith("web-", StringComparison.Ordinal));
        var preserved = Assert.Single(nativeOnly.Findings, f => f.Element.AutomationId == "WebViewNativeClipped"
            && f.RuleId == LayoutDiagnosticRules.ElementClipped);
        Assert.Equal(native.Evidence!.LostAreaRatio, preserved.Evidence!.LostAreaRatio);
        Assert.True(await Client.NavigateAsync("//native"));
        var hidden = await AnalyzeAsync(includeDom: true);
        Assert.DoesNotContain(hidden.Findings, f => f.Element.Id.StartsWith("web-", StringComparison.Ordinal));
        Assert.DoesNotContain(Contexts(await Client.GetCdpWebViewsAsync()), c => c.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task MixedHosts_RecreateAndRetainedPagesNeverCrossMapOldContexts()
    {
        var previous = await EnsureMixedAsync();
        var previousIds = previous.Select(ContextId).ToArray();
        Assert.True(await Client.TapAsync((await FindElementAsync("ResetMixedWebViewsButton")).Id));
        await WaitForAsync(async () => !Contexts(await Client.GetCdpWebViewsAsync())
            .Any(c => previousIds.Contains(ContextId(c))), timeoutMs: 10000);
        var current = await WaitForMixedAsync();
        Assert.DoesNotContain(current, c => previousIds.Contains(ContextId(c)));
        Assert.DoesNotContain(Contexts(await Client.GetCdpWebViewsAsync()), c => previousIds.Contains(ContextId(c)));
        foreach (var id in previousIds)
        {
            using var stale = await GetRawAsync($"/api/v1/webview/screenshot?contextId={id}");
            Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        }
        Assert.True(await Client.NavigateAsync("//native"));
        Assert.DoesNotContain(Contexts(await Client.GetCdpWebViewsAsync()), c => c.GetProperty("active").GetBoolean());
        Assert.True(await Client.NavigateAsync("//mixedwebviews"));
        var retained = await WaitForMixedAsync();
        Assert.Equal(current.Select(ContextId).Order(), retained.Select(ContextId).Order());
        Assert.Equal("Mixed standard WebView fixture",
            (await EvaluateAsync("document.title", ContextId(Assert.Single(retained, c => HostKind(c) == "webview")))).GetString());
    }

    [Fact]
    public async Task Blazor_RealComponentEventAndRoutingWorkAlongsideNativeAutomation()
    {
        await App.EnsureBlazorReadyAsync();
        var context = Assert.Single(Contexts(await Client.GetCdpWebViewsAsync()), c => c.GetProperty("active").GetBoolean());
        await AssertBlazorCounterAsync(ContextId(context));
        Assert.True(await Client.NavigateAsync("//native"));
        var button = await FindElementAsync("AddButton");
        Assert.True(button.IsVisible);
        Assert.NotNull(await Client.ScreenshotAsync(elementId: button.Id));
        App.InvalidateBlazorReady();
    }

    async Task AssertBlazorCounterAsync(string id)
    {
        Assert.True(await Client.NavigateWebViewAsync("/counter", id));
        await WaitForAsync(async () =>
            (await EvaluateAsync("document.querySelector('[role=status]')?.textContent", id)).GetString() == "Current count: 0",
            timeoutMs: 30000);
        Assert.EndsWith("/counter", (await EvaluateAsync("location.pathname", id)).GetString());
        Assert.True(await Client.ClickWebViewAsync("button.btn-primary", id));
        await WaitForAsync(async () =>
            (await EvaluateAsync("document.querySelector('[role=status]').textContent", id)).GetString() == "Current count: 1");
        Assert.True(await Client.NavigateWebViewAsync("/", id));
        await WaitForAsync(async () => (await EvaluateAsync("!!document.querySelector('.todo-container')", id)).GetBoolean());
        Assert.DoesNotContain("Current count:", await Client.GetCdpSourceAsync(id));
    }

    async Task AssertInputAndClickAsync(string id)
    {
        Assert.Equal(0, (await EvaluateAsync("count", id)).GetInt32());
        Assert.True(await Client.FillWebViewAsync("#fixture-input", "generic input", id));
        Assert.Equal("generic input", (await EvaluateAsync("document.querySelector('#fixture-input').value", id)).GetString());
        Assert.Equal("generic input", (await EvaluateAsync("document.querySelector('#fixture-input-value').textContent", id)).GetString());
        Assert.True(await Client.InsertWebViewTextAsync(" appended", id));
        Assert.Equal("generic input appended", (await EvaluateAsync("document.querySelector('#fixture-input').value", id)).GetString());
        Assert.Equal("generic input appended", (await EvaluateAsync("document.querySelector('#fixture-input-value').textContent", id)).GetString());
        Assert.True(await Client.ClickWebViewAsync("#fixture-button", id));
        Assert.Equal(1, (await EvaluateAsync("count", id)).GetInt32());
        Assert.Equal("1", (await EvaluateAsync("document.querySelector('#fixture-count').textContent", id)).GetString());
    }

    async Task AssertHybridMessagingAsync(string id, string statusId, string buttonId)
    {
        Assert.True(await Client.TapAsync((await FindElementAsync(buttonId)).Id));
        await WaitForAsync(async () => (await FindElementAsync(statusId)).Text == "js-ack: native-to-js-ok");
        Assert.Equal("native-to-js-ok",
            (await EvaluateAsync("document.querySelector('#native-message').textContent", id)).GetString());
    }

    async Task AssertScreenshotAsync(JsonElement context, SKColor? expectedColor = null)
    {
        var bytes = await Client.GetWebViewScreenshotAsync(ContextId(context));
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.Take(8));
        using var image = SKBitmap.Decode(bytes);
        Assert.NotNull(image);
        var element = Assert.Single(Flatten(await Client.GetTreeAsync()), e => e.Id == context.GetProperty("elementId").GetString());
        Assert.NotNull(element.Bounds);
        var density = (await GetJsonAsync("/api/v1/agent/status")).GetProperty("device").GetProperty("displayDensity").GetDouble();
        Assert.InRange(image.Width, (int)Math.Floor(element.Bounds!.Width * density) - 2,
            (int)Math.Ceiling(element.Bounds.Width * density) + 2);
        Assert.InRange(image.Height, (int)Math.Floor(element.Bounds.Height * density) - 2,
            (int)Math.Ceiling(element.Bounds.Height * density) + 2);
        Assert.True(image.Width > 50 && image.Height > 50);
        var samples = new List<SKColor>();
        for (var y = 0; y < image.Height; y += 8)
            for (var x = 0; x < image.Width; x += 8)
                samples.Add(image.GetPixel(x, y));
        Assert.True(samples.Distinct().Count() > 10, "Expected actual rendered content, not a blank PNG.");
        if (expectedColor is { } color)
        {
            var ratio = (double)samples.Count(p => Math.Abs(p.Red - color.Red) < 5
                && Math.Abs(p.Green - color.Green) < 5 && Math.Abs(p.Blue - color.Blue) < 5) / samples.Count;
            Assert.True(ratio > 0.35, $"Expected host-specific background coverage >35%, got {ratio:P1}.");
        }
        Output.WriteLine($"{HostKind(context)} {ContextId(context)} PNG {image.Width}x{image.Height}, {bytes.Length} bytes.");
    }

    async Task<JsonElement> EnsureHostAsync(string kind, string title)
    {
        var route = kind == "webview" ? "//webview" : "//hybrid";
        var reset = kind == "webview" ? "RecreateStandardWebViewButton" : "ResetHybridWebViewButton";
        Assert.True(await Client.NavigateAsync(route));
        var previous = await WaitForDocumentAsync(kind, title);
        Assert.True(await Client.TapAsync((await FindElementAsync(reset)).Id));
        return await WaitForDocumentAsync(kind, title, ContextId(previous));
    }

    async Task<JsonElement[]> EnsureMixedAsync()
    {
        Assert.True(await Client.NavigateAsync("//mixedwebviews"));
        var previous = await WaitForMixedAsync();
        Assert.True(await Client.TapAsync((await FindElementAsync("ResetMixedWebViewsButton")).Id));
        await WaitForAsync(async () => !Contexts(await Client.GetCdpWebViewsAsync())
            .Any(c => previous.Any(p => ContextId(p) == ContextId(c))), timeoutMs: 10000);
        return await WaitForMixedAsync();
    }

    async Task<JsonElement[]> WaitForMixedAsync()
    {
        foreach (var (kind, title) in new[] { ("webview", "Mixed standard WebView fixture"), ("hybrid", "Hybrid WebView fixture") })
            await WaitForDocumentAsync(kind, title);
        JsonElement[] active = [];
        await WaitForAsync(async () =>
        {
            active = Contexts(await Client.GetCdpWebViewsAsync()).Where(c => c.GetProperty("active").GetBoolean()).ToArray();
            if (active.Length != 3 || active.Any(c => !c.GetProperty("ready").GetBoolean()))
                return false;
            var blazor = Assert.Single(active, c => HostKind(c) == "blazor");
            return (await EvaluateAsync("!!document.querySelector('.todo-container')", ContextId(blazor))).GetBoolean();
        }, timeoutMs: 60000);
        return active;
    }

    async Task<JsonElement> WaitForDocumentAsync(
        string kind, string title, string? previousId = null, int? expectedCount = null, string? expectedNativeMessage = null)
    {
        JsonElement selected = default;
        await WaitForAsync(async () =>
        {
            var active = Contexts(await Client.GetCdpWebViewsAsync()).Where(c =>
                c.GetProperty("active").GetBoolean() && c.GetProperty("ready").GetBoolean() && HostKind(c) == kind
                && ContextId(c) != previousId).ToArray();
            if (active.Length != 1)
                return false;
            selected = active[0];
            try
            {
                return (await EvaluateAsync("document.title", ContextId(selected))).GetString() == title
                    && (expectedCount is null || (await EvaluateAsync("count", ContextId(selected))).GetInt32() == expectedCount)
                    && (expectedNativeMessage is null || (await EvaluateAsync(
                        "document.querySelector('#native-message').textContent", ContextId(selected))).GetString() == expectedNativeMessage);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not ready", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }, timeoutMs: 60000);
        return selected;
    }

    async Task<JsonElement> EvaluateAsync(string expression, string id)
    {
        var response = await Client.SendCdpCommandAsync("Runtime.evaluate",
            new JsonObject { ["expression"] = expression, ["returnByValue"] = true }, id);
        return response.GetProperty("result").GetProperty("result").GetProperty("value").Clone();
    }

    async Task<LayoutInspectionResult> AnalyzeAsync(bool includeDom)
        => Assert.IsType<LayoutInspectionResult>(await Client.AnalyzeLayoutAsync(new LayoutInspectionRequest
        {
            Profile = "strict",
            MinimumSeverity = "info",
            Scope = new LayoutInspectionScope { IncludeWebViewElements = includeDom, IncludeNativeElements = true },
            Stability = new LayoutStabilityOptions { Mode = "immediate" }
        }));

    static IEnumerable<JsonElement> Contexts(JsonElement json) => json.GetProperty("webviews").EnumerateArray();
    static string ContextId(JsonElement context) => context.GetProperty("id").GetString()!;
    static string HostKind(JsonElement context) => context.GetProperty("hostKind").GetString()!;

    static void AssertContext(JsonElement context)
    {
        Assert.True(context.GetProperty("active").GetBoolean());
        Assert.True(context.GetProperty("ready").GetBoolean());
        Assert.False(context.TryGetProperty("isReady", out _));
        Assert.Equal($"webview-{context.GetProperty("index").GetInt32()}", ContextId(context));
        Assert.False(string.IsNullOrEmpty(context.GetProperty("elementId").GetString()));
    }

    static IEnumerable<ElementInfo> Flatten(IEnumerable<ElementInfo> elements)
    {
        foreach (var element in elements)
        {
            yield return element;
            foreach (var child in Flatten(element.Children ?? []))
                yield return child;
        }
    }
}
