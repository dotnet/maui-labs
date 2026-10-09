using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class WebViewLayoutDiagnosticsTests
{
    private const string Snapshot = """
        {
          "viewport":{"width":100,"height":100,"devicePixelRatio":2,"visualScale":1},
          "nodes":[
            {"index":0,"parentIndex":-1,"tag":"div","id":"clip","visible":true,
             "rect":{"x":10,"y":5,"width":40,"height":20},
             "clientWidth":40,"clientHeight":20,"scrollWidth":80,"scrollHeight":20,
             "overflowX":"hidden","overflowY":"visible"},
            {"index":1,"parentIndex":0,"tag":"button","id":"child","visible":true,"interactive":true,
             "rect":{"x":10,"y":5,"width":80,"height":20},
             "ancestorClips":[{"clipperIndex":0,"kind":"ancestor-layout-clip",
                              "rect":{"x":10,"y":5,"width":40,"height":20}}],
             "directText":{"truncated":true,"kind":"horizontal-hard-clip","length":8,
                           "contentWidth":80,"availableWidth":40,"contentHeight":20,"availableHeight":20},
             "blockedByIndex":0,"blockedSamples":81,"sampleCount":81}
          ],
          "totalElementCount":2,"crossOriginFrames":0
        }
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LayoutScope_CoreAndPortableClient_UseOnlyCanonicalProperty(bool include)
    {
        var core = new LayoutInspectionScope { IncludeWebViewElements = include };
        var client = new Driver.LayoutInspectionScope { IncludeWebViewElements = include };
        foreach (var json in new[] { AgentJson.Serialize(core), JsonSerializer.Serialize(client) })
        {
            using var document = JsonDocument.Parse(json);
            Assert.Equal(include, document.RootElement.GetProperty("includeWebViewElements").GetBoolean());
            Assert.False(document.RootElement.TryGetProperty("includeBlazorElements", out _));
        }
        Assert.True(JsonSerializer.Deserialize<LayoutInspectionScope>("{}")!.IncludeWebViewElements);
        Assert.True(JsonSerializer.Deserialize<Driver.LayoutInspectionScope>("{}")!.IncludeWebViewElements);
        Assert.Equal(include, JsonSerializer.Deserialize<LayoutInspectionScope>(
            JsonSerializer.Serialize(client))!.IncludeWebViewElements);
    }

    [Theory]
    [InlineData("webview", "WebView")]
    [InlineData("hybrid", "HybridWebView")]
    [InlineData("blazor", "BlazorWebView")]
    [InlineData("custom", "CustomWebView")]
    public void AppendNodes_AllHosts_PreserveCoordinatesClipsOcclusionAndGenericMetadata(string kind, string type)
    {
        var owner = new object();
        var capture = Capture(Host("native-host", owner, type: type));
        using var document = JsonDocument.Parse(Snapshot);
        MauiDevFlowAgentService.AppendBlazorLayoutNodes(capture, Context(owner, kind: kind),
            document.RootElement, new LayoutInspectionRequest());

        Assert.False(capture.Nodes[0].IsCoverageOpaque);
        var parent = capture.Nodes[1];
        var child = capture.Nodes[2];
        Assert.Equal("native-host", parent.Element.ParentId);
        Assert.Equal("web-3-0", child.Element.ParentId);
        foreach (var node in capture.Nodes.Skip(1))
        {
            Assert.Equal("webview", node.Element.Framework);
            Assert.Equal("WebView.DOM.Element", node.Element.FullType);
            Assert.Equal(120, node.Element.WindowBounds!.X);
            Assert.Equal(215, node.Element.WindowBounds.Y);
            Assert.Equal(60, node.Element.WindowBounds.Height);
            Assert.Equal("window-1", node.WindowId);
            Assert.Equal(2, node.WindowScale);
        }
        Assert.Equal(80, parent.FullRegion.Bounds.Width);
        Assert.Equal(160, child.FullRegion.Bounds.Width);
        Assert.Equal(80 * 60, child.VisibleRegion.Area);
        Assert.Equal("web-3-0", Assert.Single(child.ClipChain).ClipperElementId);
        Assert.Equal("web-3-0", child.InteractionOccluderId);
        Assert.Equal(81, child.InteractionSampleCount);
        Assert.True(child.InteractionBlockedLowerBound > 0.8);
        Assert.True(child.Text!.IsTruncated);
        Assert.Null(child.Text.TextLength);
        Assert.Null(child.Text.Text);
    }

    [Fact]
    public void FindHost_DuplicateIdsAndStaleElementId_UseOnlyLiveOwnerIdentity()
    {
        var first = new object();
        var second = new object();
        var capture = Capture(Host("first", first), Host("second", second));
        var context = Context(second);
        context.ElementId = "first";
        Assert.Same(capture.Nodes[1], MauiDevFlowAgentService.FindLayoutWebViewHost(capture, context));
        context.Owner = new WeakReference<object>(null!);
        Assert.Null(MauiDevFlowAgentService.FindLayoutWebViewHost(capture, context));
        context.Owner = new WeakReference<object>(new object());
        Assert.Null(MauiDevFlowAgentService.FindLayoutWebViewHost(capture, context));
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("ancestor-hidden")]
    [InlineData("clipped-away")]
    public void FindHost_NotVisible_DoesNotEnrich(string condition)
    {
        var owner = new object();
        var host = Host("host", owner);
        if (condition == "hidden") host.Element.IsVisible = false;
        if (condition == "ancestor-hidden") host.IsRendered = false;
        if (condition == "clipped-away") host.VisibleRegion = LayoutRegionMath.Empty();
        var capture = Capture(host);
        using var document = JsonDocument.Parse(Snapshot);
        MauiDevFlowAgentService.AppendBlazorLayoutNodes(capture, Context(owner), document.RootElement, new());
        Assert.Single(capture.Nodes);
        Assert.Contains(capture.IncompleteReasons, reason => reason.Contains("visible native"));
    }

    [Fact]
    public void FindHost_OwnerlessFallback_RequiresUnambiguousNativeHost()
    {
        var capture = Capture(Host("first", new object()), Host("second", new object()));
        var context = new CdpWebViewInfo { AutomationId = "duplicate" };
        Assert.Null(MauiDevFlowAgentService.FindLayoutWebViewHost(capture, context));
        context.ElementId = "second";
        Assert.Same(capture.Nodes[1], MauiDevFlowAgentService.FindLayoutWebViewHost(capture, context));
        context.ElementId = null;
        capture.Nodes.RemoveAt(0);
        Assert.Same(capture.Nodes[0], MauiDevFlowAgentService.FindLayoutWebViewHost(capture, context));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Enrichment_ScopeActuallyControlsDomDispatchAndNativeNodes(bool include)
    {
        var owner = new object();
        using var service = new ActiveService();
        var calls = 0;
        var index = service.RegisterCdpWebView(command =>
        {
            calls++;
            Assert.Contains("JSON.stringify", command);
            return Task.FromResult(Response());
        }, () => true, owner: owner, hostKind: "hybrid");
        service.Active.Add($"webview-{index}");
        var capture = Capture(Host("host", owner));
        await Enrich(service, capture, new() { Scope = new() { IncludeWebViewElements = include } });
        Assert.Equal(include ? 1 : 0, calls);
        Assert.Equal(include ? 3 : 1, capture.Nodes.Count);
        Assert.Equal("host", capture.Nodes[0].Element.Id);
        Assert.Empty(capture.IncompleteReasons);
    }

    [Fact]
    public async Task Enrichment_InactiveUnreadyUnregisteredHosts_NeverDispatchOrBorrowAnotherOwner()
    {
        var active = new object();
        var retained = new object();
        var unready = new object();
        using var service = new ActiveService();
        var calls = 0;
        var index = service.RegisterCdpWebView(_ => { calls++; return Task.FromResult(Response()); },
            () => true, owner: active);
        service.Active.Add($"webview-{index}");
        service.RegisterCdpWebView(_ => throw new InvalidOperationException("retained host"), () => true, owner: retained);
        var unreadyIndex = service.RegisterCdpWebView(_ => throw new InvalidOperationException("unready host"),
            () => false, owner: unready);
        service.Active.Add($"webview-{unreadyIndex}");
        var capture = Capture(Host("active", active), Host("retained", retained), Host("unready", unready),
            Host("unregistered", new object()));
        await Enrich(service, capture, new());
        Assert.Equal(1, calls);
        Assert.All(capture.Nodes.Where(node => node.Element.Framework == "webview"),
            node => Assert.StartsWith($"web-{index}-", node.Element.Id));
        Assert.False(capture.Nodes[0].IsCoverageOpaque);
        Assert.All(capture.Nodes.Skip(1).Take(3), node => Assert.True(node.IsCoverageOpaque));
        Assert.Contains(capture.IncompleteReasons, reason => reason.Contains("not ready"));
        Assert.Equal(3, capture.IncompleteReasons.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"result":{"result":{"value":""}}}""")]
    [InlineData("""{"result":{"result":{"value":null}}}""")]
    [InlineData("""{"result":{"result":{"value":{"viewport":{"width":0,"height":100},"nodes":[]}}}}""")]
    [InlineData("""{"error":{"message":"unsupported"}}""")]
    public async Task Enrichment_InvalidResponse_LeavesHostOpaqueAndIncomplete(string response)
    {
        var owner = new object();
        using var service = new ActiveService();
        var index = service.RegisterCdpWebView(_ => Task.FromResult(response), () => true, owner: owner);
        service.Active.Add($"webview-{index}");
        var capture = Capture(Host("host", owner));
        capture.Nodes[0].IsCoverageOpaque = false;
        await Enrich(service, capture, new());
        Assert.Single(capture.Nodes);
        Assert.True(capture.Nodes[0].IsCoverageOpaque);
        Assert.NotEmpty(capture.IncompleteReasons);
    }

    [Fact]
    public async Task Enrichment_ExpiredDeadline_DoesNotStartProbe()
    {
        var owner = new object();
        using var service = new ActiveService();
        var index = service.RegisterCdpWebView(_ => throw new InvalidOperationException("Expired probe dispatched"),
            () => true, owner: owner);
        service.Active.Add($"webview-{index}");
        var capture = Capture(Host("host", owner));
        await Enrich(service, capture, new(), timeoutMs: -1);
        Assert.Single(capture.Nodes);
        Assert.True(capture.Nodes[0].IsCoverageOpaque);
        Assert.Contains(capture.IncompleteReasons, reason => reason.Contains("could not start"));
    }

    [Fact]
    public async Task Enrichment_NoRegisteredBridge_ReportsVisibleHostIncomplete()
    {
        using var service = new ActiveService();
        var capture = Capture(Host("host", new object()));
        await Enrich(service, capture, new());
        Assert.Single(capture.Nodes);
        Assert.True(capture.Nodes[0].IsCoverageOpaque);
        Assert.Contains(capture.IncompleteReasons, reason => reason.Contains("No WebView CDP bridge"));
    }

    [Fact]
    public async Task Enrichment_Timeout_DoesNotReplayOutstandingProbe()
    {
        var owner = new object();
        using var service = new ActiveService();
        var pending = new TaskCompletionSource<string>();
        var calls = 0;
        var index = service.RegisterCdpWebView(_ => { calls++; return pending.Task; }, () => true, owner: owner);
        service.Active.Add($"webview-{index}");
        var capture = Capture(Host("host", owner));
        await Enrich(service, capture, new(), timeoutMs: 30);
        Assert.True(capture.Nodes[0].IsCoverageOpaque);
        Assert.Contains(capture.IncompleteReasons, reason => reason.Contains("deadline"));
        var second = Capture(Host("host", owner));
        await Enrich(service, second, new());
        Assert.Equal(1, calls);
        Assert.Contains(second.IncompleteReasons, reason => reason.Contains("still running"));
        pending.SetResult(Response());
    }

    [Fact]
    public void AppendNodes_LimitsAndCrossOriginFrames_RemainExplicitlyIncomplete()
    {
        using var document = JsonDocument.Parse(Snapshot.Replace(
            "\"totalElementCount\":2,\"crossOriginFrames\":0", "\"totalElementCount\":501,\"crossOriginFrames\":1"));
        var owner = new object();
        var capture = Capture(Host("host", owner));
        MauiDevFlowAgentService.AppendBlazorLayoutNodes(capture, Context(owner), document.RootElement, new());
        Assert.Contains(capture.IncompleteReasons, reason => reason.Contains("cross-origin"));
        Assert.Contains(capture.IncompleteReasons, reason => reason.Contains("limited to the first 500"));
    }

    private static LayoutNodeSnapshot Host(string id, object owner, string type = "WebView") => new()
    {
        Element = new ElementInfo { Id = id, Type = type, AutomationId = "duplicate", IsVisible = true },
        WebViewOwner = new WeakReference<object>(owner),
        FullRegion = LayoutRegionMath.FromRect(100, 200, 200, 300),
        VisibleRegion = LayoutRegionMath.FromRect(100, 200, 200, 300),
        WindowId = "window-1",
        WindowScale = 2,
        IsCoverageOpaque = true
    };

    private static LayoutCaptureSnapshot Capture(params LayoutNodeSnapshot[] hosts)
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.AddRange(hosts);
        return capture;
    }

    private static CdpWebViewInfo Context(object owner, string kind = "webview") => new()
    {
        Index = 3, Owner = new WeakReference<object>(owner), AutomationId = "duplicate", HostKind = kind
    };

    private static string Response() => JsonSerializer.Serialize(new { result = new { result = new { value = Snapshot } } });

    private static Task Enrich(ActiveService service, LayoutCaptureSnapshot capture, LayoutInspectionRequest request,
        int timeoutMs = 1000) => service.EnrichLayoutCaptureWithBlazorAsync(capture, request, new(), new(),
            DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs), CancellationToken.None);

    private sealed class ActiveService : MauiDevFlowAgentService
    {
        public HashSet<string> Active { get; } = [];
        protected override bool SupportsActiveCdpWebViewResolution => true;
        protected override Task<HashSet<string>> GetActiveWebViewAutomationIdsAsync() => Task.FromResult(Active);
    }
}
