using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "WebView")]
// Requires the MAUI sample: BlazorWebView is a MAUI control. Native runs filter this out.
[Trait(TestFramework.Trait, TestFramework.Maui)]
public class WebViewTests : IntegrationTestBase
{
    public WebViewTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    static bool HasWebViewContexts(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
            return json.GetArrayLength() > 0;

        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("webviews", out var webviews) &&
            webviews.ValueKind == JsonValueKind.Array)
        {
            return webviews.GetArrayLength() > 0;
        }

        return false;
    }

    static int CountWebViewContexts(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
            return json.GetArrayLength();

        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("webviews", out var webviews) &&
            webviews.ValueKind == JsonValueKind.Array)
        {
            return webviews.GetArrayLength();
        }

        return 0;
    }

    static bool AnyReadyContext(JsonElement json)
    {
        JsonElement array;
        if (json.ValueKind == JsonValueKind.Array)
            array = json;
        else if (json.ValueKind == JsonValueKind.Object &&
                 json.TryGetProperty("webviews", out var webviews) &&
                 webviews.ValueKind == JsonValueKind.Array)
            array = webviews;
        else
            return false;

        foreach (var ctx in array.EnumerateArray())
        {
            if (ctx.ValueKind != JsonValueKind.Object) continue;
            if (ctx.TryGetProperty("isReady", out var r1) && r1.ValueKind == JsonValueKind.True) return true;
            if (ctx.TryGetProperty("ready", out var r2) && r2.ValueKind == JsonValueKind.True) return true;
        }

        return false;
    }

    Task EnsureOnBlazorPageAsync() => App.EnsureBlazorReadyAsync();

    async Task<string> EvaluateStringAsync(string expression, string contextId)
    {
        var result = await Client.SendCdpCommandAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression,
            ["returnByValue"] = true,
        }, contextId);
        Assert.False(result.TryGetProperty("error", out _), $"CDP evaluation failed: {result}");
        var evaluation = result.GetProperty("result");
        Assert.False(evaluation.TryGetProperty("exceptionDetails", out _), $"JavaScript evaluation failed: {result}");
        return evaluation.GetProperty("result").GetProperty("value").GetString()!;
    }

    static IEnumerable<JsonElement> EnumerateContexts(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
        {
            foreach (var ctx in json.EnumerateArray())
                yield return ctx;
            yield break;
        }

        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("webviews", out var webviews) &&
            webviews.ValueKind == JsonValueKind.Array)
        {
            foreach (var ctx in webviews.EnumerateArray())
                yield return ctx;
        }
    }

    static bool IsReadyContext(JsonElement ctx)
        => (ctx.TryGetProperty("isReady", out var r1) && r1.ValueKind == JsonValueKind.True)
           || (ctx.TryGetProperty("ready", out var r2) && r2.ValueKind == JsonValueKind.True);

    async Task<string> GetActiveContextIdAsync(int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        JsonElement? lastJson = null;

        while (DateTime.UtcNow < deadline)
        {
            var json = await Client.GetCdpWebViewsAsync();
            lastJson = json;
            var contexts = EnumerateContexts(json).ToList();
            if (contexts.Count == 0)
            {
                await Task.Delay(250);
                continue;
            }

            JsonElement? pick =
                contexts.LastOrDefault(c =>
                    c.TryGetProperty("automationId", out var automationId) &&
                    automationId.ValueKind == JsonValueKind.String &&
                    automationId.GetString() == "BlazorWebView" &&
                    IsReadyContext(c));

            if (pick == null || pick.Value.ValueKind == JsonValueKind.Undefined)
                pick = contexts.LastOrDefault(IsReadyContext);

            if (pick == null || pick.Value.ValueKind == JsonValueKind.Undefined)
                pick = contexts.LastOrDefault(c =>
                    c.TryGetProperty("automationId", out var automationId) &&
                    automationId.ValueKind == JsonValueKind.String &&
                    automationId.GetString() == "BlazorWebView");

            var selected = pick ?? contexts[^1];
            if (selected.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                return id.GetString()!;
            if (selected.TryGetProperty("index", out var index) && index.ValueKind == JsonValueKind.Number)
                return index.GetInt32().ToString();

            return "0";
        }

        Assert.Fail($"Expected at least one WebView context within {timeoutMs}ms. Last payload: {lastJson}");
        return "0";
    }

    /// <summary>
    /// Returns true if the response body indicates the CDP bridge is transiently
    /// not-ready (i.e. chobitsu is re-injecting after a page navigation). Android's
    /// OnPageFinished can reset the bridge mid-test on internal Blazor router navigations.
    /// </summary>
    static bool IsBridgeNotReady(string body)
        => body.Contains("CDP not ready", StringComparison.OrdinalIgnoreCase)
            || body.Contains("WebView not ready", StringComparison.OrdinalIgnoreCase)
            || body.Contains("WebView not initialized", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Executes an HTTP call and retries up to <paramref name="timeoutMs"/> when the
    /// response body signals the CDP bridge is transiently re-injecting. Any other
    /// error path (non-bridge 4xx/5xx, exception) returns immediately.
    /// </summary>
    async Task<HttpResponseMessage> WithBridgeRetryAsync(
        Func<Task<HttpResponseMessage>> call,
        int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        HttpResponseMessage? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last?.Dispose();
            last = await call();
            if (last.IsSuccessStatusCode) return last;

            var body = await last.Content.ReadAsStringAsync();
            if (!IsBridgeNotReady(body))
                return last;

            await Task.Delay(250);
            // Re-create the response wrapper with the body we already read so callers
            // can still inspect it if we hit the deadline.
            if (DateTime.UtcNow >= deadline)
            {
                var replay = new HttpResponseMessage(last.StatusCode)
                {
                    Content = new StringContent(body),
                    RequestMessage = last.RequestMessage,
                };
                last.Dispose();
                return replay;
            }
        }
        return last!;
    }

    Task<HttpResponseMessage> GetWithBridgeRetryAsync(string path, int timeoutMs = 15000)
        => WithBridgeRetryAsync(() => GetRawAsync(path), timeoutMs);

    Task<HttpResponseMessage> PostWithBridgeRetryAsync(string path, object? body, int timeoutMs = 15000)
        => WithBridgeRetryAsync(() => PostRawAsync(path, body), timeoutMs);

    /// <summary>
    /// Asserts the CDP bridge is actually live by sending a trivial Runtime.evaluate probe.
    /// Retries briefly when the bridge is mid-reinject (Android OnPageFinished racing with
    /// a test call). Protects against false-positive test passes when chobitsu failed to
    /// load and the bridge is returning error payloads.
    /// </summary>
    async Task AssertCdpResponsiveAsync(int timeoutMs = 15000)
    {
        var effectiveTimeoutMs = Platform == "windows"
            ? Math.Max(timeoutMs, 30000)
            : timeoutMs;
        var ok = await App.WaitForCdpResponsiveAsync(effectiveTimeoutMs);
        if (!ok)
        {
            App.InvalidateBlazorReady();
            await App.EnsureBlazorReadyAsync();
            ok = await App.WaitForCdpResponsiveAsync(effectiveTimeoutMs);
        }
        Assert.True(ok,
            $"CDP bridge did not become responsive within {effectiveTimeoutMs}ms (expected a live bridge answering Runtime.evaluate).");
    }

    [Fact]
    public async Task Contexts_ReturnsAtLeastOneWebView()
    {
        await EnsureOnBlazorPageAsync();
        var json = await Client.GetCdpWebViewsAsync();

        Assert.True(HasWebViewContexts(json), "Expected at least one WebView context.");
    }

    [Fact]
    public async Task Contexts_WebViewIsReady()
    {
        await EnsureOnBlazorPageAsync();
        var json = await Client.GetCdpWebViewsAsync();
        Assert.True(HasWebViewContexts(json), "Expected at least one WebView context.");
        Assert.True(AnyReadyContext(json),
            $"Expected at least one WebView context with isReady=true. Got: {json}");
    }

    [Fact]
    public async Task Evaluate_DocumentTitle_ReturnsResult()
    {
        await EnsureOnBlazorPageAsync();

        var paramsJson = JsonNode.Parse("""{"expression": "document.title"}""");
        var result = await Client.SendCdpCommandAsync("Runtime.evaluate", paramsJson);

        var text = result.ToString();
        Assert.False(text.Contains("\"error\"", StringComparison.OrdinalIgnoreCase),
            $"Runtime.evaluate returned an error payload: {text}");
        // Response should be a Runtime.evaluate result object with a result.value (string title).
        Assert.Contains("\"result\"", text);
        Assert.Contains("\"value\"", text);
    }

    [Fact]
    public async Task Evaluate_SimpleExpression_ReturnsResult()
    {
        await EnsureOnBlazorPageAsync();

        var paramsJson = JsonNode.Parse("""{"expression": "1 + 1"}""");
        var result = await Client.SendCdpCommandAsync("Runtime.evaluate", paramsJson);

        var text = result.ToString();
        Assert.False(text.Contains("\"error\"", StringComparison.OrdinalIgnoreCase),
            $"Runtime.evaluate returned an error payload: {text}");
        Assert.Contains("\"value\":2", text);
    }

    [Fact]
    public async Task LayoutDiagnostics_DetectsBlazorTextOverflow()
    {
        await EnsureOnBlazorPageAsync();
        const string fixtureId = "DevFlowBlazorOverflowFixture";
        var createFixture = JsonNode.Parse(
            $$"""
            {
              "expression": "(() => { const old = document.getElementById('{{fixtureId}}'); if (old) old.remove(); const element = document.createElement('div'); element.id = '{{fixtureId}}'; element.textContent = 'This text is deliberately too wide for the fixture'; element.style.cssText = 'width:60px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;position:fixed;left:10px;top:10px;'; document.body.appendChild(element); return true; })()"
            }
            """);
        _ = await Client.SendCdpCommandAsync("Runtime.evaluate", createFixture);

        try
        {
            var result = await Client.AnalyzeLayoutAsync(new LayoutInspectionRequest
            {
                Profile = "strict",
                MinimumSeverity = "info",
                Scope = new LayoutInspectionScope
                {
                    IncludeBlazorElements = true,
                    IncludeNativeElements = false
                },
                Stability = new LayoutStabilityOptions { Mode = "immediate" }
            });

            Assert.NotNull(result);
            Assert.Contains(result!.Findings, finding =>
                finding.RuleId == LayoutDiagnosticRules.TextNotFullyRendered
                && finding.Element.AutomationId == fixtureId);
        }
        finally
        {
            var removeFixture = JsonNode.Parse(
                $$"""{"expression":"document.getElementById('{{fixtureId}}')?.remove()"}""");
            _ = await Client.SendCdpCommandAsync("Runtime.evaluate", removeFixture);
        }
    }

    [Fact]
    public async Task Source_ReturnsHtmlContent()
    {
        await EnsureOnBlazorPageAsync();

        var source = await Client.GetCdpSourceAsync();
        Assert.NotNull(source);
        Assert.NotEmpty(source);
        Assert.Contains("<", source);
    }

    [Fact]
    public async Task Dom_ReturnsOuterHtml()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();

        var response = await GetWithBridgeRetryAsync("/api/v1/webview/dom");
        Assert.True(response.IsSuccessStatusCode,
            $"/api/v1/webview/dom returned {(int)response.StatusCode}");

        var html = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(html);
        Assert.Contains("<", html);
    }

    [Fact]
    public async Task DomQuery_ReturnsElements()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();
        var contextId = await GetActiveContextIdAsync();

        var response = await PostWithBridgeRetryAsync("/api/v1/webview/dom/query", new
        {
            selector = ".add-btn",
            contextId,
        });

        Assert.True(response.IsSuccessStatusCode,
            $"/api/v1/webview/dom/query returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);
    }

    [Fact]
    public async Task WebViewNavigate_Succeeds()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();
        var contextId = await GetActiveContextIdAsync();

        var response = await PostWithBridgeRetryAsync("/api/v1/webview/navigate", new
        {
            url = "/counter",
            contextId,
        });

        Assert.True(response.IsSuccessStatusCode,
            $"WebView navigate failed with status {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        await AssertWebViewPathAsync(contextId, "/counter");
        contextId = await GetActiveContextIdAsync();

        var backResponse = await PostWithBridgeRetryAsync("/api/v1/webview/navigate", new
        {
            url = "/",
            contextId,
        });
        Assert.True(backResponse.IsSuccessStatusCode,
            $"WebView navigate back failed with status {(int)backResponse.StatusCode}: {await backResponse.Content.ReadAsStringAsync()}");
        await AssertWebViewPathAsync(contextId, "/");
        App.InvalidateBlazorReady();
    }

    async Task AssertWebViewPathAsync(string contextId, string expectedPath)
    {
        var result = await Client.SendCdpCommandAsync(
            "Runtime.evaluate",
            new JsonObject
            {
                ["expression"] = "location.pathname",
                ["returnByValue"] = true
            },
            contextId);
        Assert.Equal(expectedPath,
            result.GetProperty("result").GetProperty("result").GetProperty("value").GetString());
    }

    [Fact]
    public async Task InputClick_Button_Succeeds()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();
        var contextId = await GetActiveContextIdAsync();

        // Use the always-present Add button. On slow renderers (Windows WebView2 on
        // hosted runners) the component DOM can lag behind the CDP "ready" state, so
        // poll until the element appears or a generous timeout expires.
        HttpResponseMessage response = null!;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            response = await PostWithBridgeRetryAsync("/api/v1/webview/input/click", new
            {
                selector = ".add-btn",
                contextId,
            });

            if (response.IsSuccessStatusCode)
                break;

            var body = await response.Content.ReadAsStringAsync();
            if (!body.Contains("No element matches", StringComparison.OrdinalIgnoreCase))
                break; // Non-selector error, don't retry

            await Task.Delay(1000);
        }

        Assert.True(response.IsSuccessStatusCode,
            $"/api/v1/webview/input/click returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    }

    [Fact]
    public async Task InputFill_TextInput_Succeeds()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();
        var contextId = await GetActiveContextIdAsync();

        var response = await PostWithBridgeRetryAsync("/api/v1/webview/input/fill", new
        {
            selector = "input",
            text = "Blazor fill test",
            contextId,
        });

        // Some platforms may not ship an input element on /counter; a 404 from the selector
        // is acceptable, but anything above 500 or a bridge error is not.
        Assert.True(
            response.IsSuccessStatusCode || (int)response.StatusCode == 404,
            $"/api/v1/webview/input/fill returned unexpected status {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    }

    [Theory]
    [InlineData("click")]
    [InlineData("fill")]
    [InlineData("query")]
    public async Task TypedAction_ExecutesExactlyOnce_AndPreservesStructuredResult(string action)
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();
        var contextId = await GetActiveContextIdAsync();
        const string fixtureId = "DevFlowExactlyOnceFixture";
        var fixtureIdJson = JsonSerializer.Serialize(fixtureId);

        try
        {
            var ready = await EvaluateStringAsync(
                $$"""
                (() => {
                    const id = {{fixtureIdJson}};
                    const old = document.getElementById(id);
                    if (old) old.remove();
                    const root = document.createElement('div');
                    root.id = id;
                    root.style.cssText = 'position:fixed;top:10px;left:10px;z-index:99999;background:white;padding:10px;';
                    root.dataset.clicks = '0';
                    root.dataset.inputs = '0';
                    root.dataset.changes = '0';
                    const button = document.createElement('button');
                    button.type = 'button';
                    button.textContent = 'Exactly once';
                    button.addEventListener('click', () => root.dataset.clicks = String(Number(root.dataset.clicks) + 1));
                    const input = document.createElement('input');
                    input.type = 'text';
                    input.addEventListener('input', () => root.dataset.inputs = String(Number(root.dataset.inputs) + 1));
                    input.addEventListener('change', () => root.dataset.changes = String(Number(root.dataset.changes) + 1));
                    root.appendChild(button);
                    root.appendChild(input);
                    document.body.appendChild(root);
                    return 'ready';
                })()
                """, contextId);
            Assert.Equal("ready", ready);

            var selector = $"#{fixtureId} {(action == "fill" ? "input" : "button")}";
            var path = action == "query"
                ? "/api/v1/webview/dom/query"
                : $"/api/v1/webview/input/{action}";
            var lease = await Client.ControlMutationLeaseAsync("claim");
            Assert.True(lease.YouHold, "The regression must hold the mutation lease.");
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add("X-DevFlow-Lease", Client.MutationLeaseId);
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                selector,
                text = "Exactly once value",
                contextId,
            }), System.Text.Encoding.UTF8, "application/json");

            // Send exactly one mutation; transport/bridge retries would obscure its event count.
            using var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"Typed {action} failed: {(int)response.StatusCode}: {body}");
            using var result = JsonDocument.Parse(body);
            if (action == "query")
            {
                Assert.Equal(JsonValueKind.Array, result.RootElement.ValueKind);
                var match = Assert.Single(result.RootElement.EnumerateArray());
                Assert.Equal("button", match.GetProperty("tagName").GetString());
                Assert.Equal("Exactly once", match.GetProperty("text").GetString());
            }
            else
            {
                Assert.Equal(JsonValueKind.Object, result.RootElement.ValueKind);
                Assert.True(result.RootElement.GetProperty("success").GetBoolean());
                if (action == "click")
                    Assert.Equal("button", result.RootElement.GetProperty("tagName").GetString());
                else
                    Assert.Equal("Exactly once value".Length, result.RootElement.GetProperty("textLength").GetInt32());
            }

            var stateJson = await EvaluateStringAsync(
                $$"""
                (() => {
                    const root = document.getElementById({{fixtureIdJson}});
                    return JSON.stringify({
                        clicks: Number(root.dataset.clicks),
                        inputs: Number(root.dataset.inputs),
                        changes: Number(root.dataset.changes),
                        value: root.querySelector('input').value
                    });
                })()
                """, contextId);
            using var state = JsonDocument.Parse(stateJson);
            Assert.Equal(action == "click" ? 1 : 0, state.RootElement.GetProperty("clicks").GetInt32());
            Assert.Equal(action == "fill" ? 1 : 0, state.RootElement.GetProperty("inputs").GetInt32());
            Assert.Equal(action == "fill" ? 1 : 0, state.RootElement.GetProperty("changes").GetInt32());
            Assert.Equal(action == "fill" ? "Exactly once value" : "", state.RootElement.GetProperty("value").GetString());
            Output.WriteLine($"Verified {action} result and exact DOM event counts on {Platform}: {stateJson}");
        }
        finally
        {
            await EvaluateStringAsync(
                $"(() => {{ const root = document.getElementById({fixtureIdJson}); if (root) root.remove(); return 'removed'; }})()", contextId);
        }
    }

    const string CliFixtureId = "DevFlowCliOperationsFixture";
    const string CliFixtureButtonText = "Exactly once \"quoted\" \\ button";
    const string CliFillText = "Hello \"quoted\"\n'world'\\ \u2603";

    async Task CreateCliDomFixtureAsync(string contextId)
    {
        var ready = await EvaluateStringAsync(
            $$"""
            (() => {
                const id = {{JsonSerializer.Serialize(CliFixtureId)}};
                document.getElementById(id)?.remove();
                const root = document.createElement('div');
                root.id = id;
                root.dataset.name = {{JsonSerializer.Serialize("a'b\\c")}};
                root.style.cssText = 'position:fixed;top:10px;left:10px;z-index:99999;background:white;padding:10px;';
                root.dataset.clicks = '0';
                root.dataset.inputs = '0';
                root.dataset.changes = '0';
                const button = document.createElement('button');
                button.type = 'button';
                button.textContent = {{JsonSerializer.Serialize(CliFixtureButtonText)}};
                button.addEventListener('click', () => root.dataset.clicks = String(Number(root.dataset.clicks) + 1));
                root.appendChild(button);
                for (const tag of ['textarea', 'div']) {
                    const field = document.createElement(tag);
                    if (tag === 'div') field.contentEditable = 'true';
                    field.addEventListener('input', () => root.dataset.inputs = String(Number(root.dataset.inputs) + 1));
                    field.addEventListener('change', () => root.dataset.changes = String(Number(root.dataset.changes) + 1));
                    root.appendChild(field);
                }
                document.body.appendChild(root);
                return 'ready';
            })()
            """, contextId);
        Assert.Equal("ready", ready);
    }

    Task RemoveCliDomFixtureAsync(string contextId)
        => EvaluateStringAsync(
            $"(() => {{ document.getElementById({JsonSerializer.Serialize(CliFixtureId)})?.remove(); return 'removed'; }})()", contextId);

    async Task<JsonDocument> ReadCliDomStateAsync(string contextId)
        => JsonDocument.Parse(await EvaluateStringAsync(
            $$"""
            (() => {
                const root = document.getElementById({{JsonSerializer.Serialize(CliFixtureId)}});
                return JSON.stringify({
                    clicks: Number(root.dataset.clicks),
                    inputs: Number(root.dataset.inputs),
                    changes: Number(root.dataset.changes),
                    value: root.querySelector('textarea').value,
                    editable: root.querySelector('[contenteditable]').textContent
                });
            })()
            """, contextId));

    [Fact]
    public async Task CliOperations_QuerySnapshotAndCommand_ReturnRealSelectedDom()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();
        var contextId = await GetActiveContextIdAsync();
        var operations = new WebViewOperations(Client);
        try
        {
            await CreateCliDomFixtureAsync(contextId);
            var selector = $"#{CliFixtureId}[data-name=\"a'b\\\\c\"] button";
            var queryResult = await operations.DomQuerySelectorAsync(selector, contextId);
            Assert.False(queryResult.StartsWith("Error:", StringComparison.Ordinal), $"CLI DOM query failed: {queryResult}");
            using var query = JsonDocument.Parse(queryResult);
            Assert.Equal("button", query.RootElement.GetProperty("tagName").GetString());
            Assert.Equal(CliFixtureButtonText, query.RootElement.GetProperty("textContent").GetString());
            Assert.Equal(JsonValueKind.Null, query.RootElement.GetProperty("id").ValueKind);

            using var queryAll = JsonDocument.Parse(await operations.DomQuerySelectorAllAsync($"#{CliFixtureId} > *", contextId));
            Assert.Equal(new[] { "button", "textarea", "div" },
                queryAll.RootElement.EnumerateArray().Select(e => e.GetProperty("tagName").GetString()));
            Assert.Equal(new[] { 0, 1, 2 },
                queryAll.RootElement.EnumerateArray().Select(e => e.GetProperty("index").GetInt32()));
            Assert.Equal("null", await operations.DomQuerySelectorAsync($"#{CliFixtureId} .missing", contextId));
            Assert.Equal("[]", await operations.DomQuerySelectorAllAsync($"#{CliFixtureId} .missing", contextId));
            Assert.Contains("<button type=\"button\">", await operations.DomGetOuterHtmlAsync(selector, contextId));
            var snapshot = await operations.SnapshotAsync(contextId);
            Assert.Contains($"<div id=\"{CliFixtureId}\">", snapshot);
            Assert.Contains($"<button [clickable]> {CliFixtureButtonText}", snapshot);
            Assert.Contains("<textarea [input]>", snapshot);

            // Runtime.evaluate takes a mutation lease even for a DOM read. Hand it to the
            // real CLI, then release only that CLI's lease and restore the fixture's owner.
            var released = await Client.ControlMutationLeaseAsync("release");
            Assert.True(released.Ok, released.Error);
            try
            {
                var (exitCode, stdout, stderr) = await InvokeWebViewCommandAsync(
                    "DOM", "querySelector", selector, "--webview", contextId);
                Assert.Equal(0, exitCode);
                Assert.DoesNotContain("Error:", stderr);
                using var commandQuery = JsonDocument.Parse(stdout);
                Assert.Equal(query.RootElement.ToString(), commandQuery.RootElement.ToString());
            }
            finally
            {
                var cliLeaseId = (string)typeof(DevFlowCommands)
                    .GetField("s_cliMutationLeaseId", BindingFlags.Static | BindingFlags.NonPublic)!
                    .GetValue(null)!;
                var cliReleased = await Client.ControlMutationLeaseAsync("release", leaseId: cliLeaseId);
                Assert.True(cliReleased.Ok, cliReleased.Error);
                var restored = await Client.ControlMutationLeaseAsync("claim");
                Assert.True(restored.YouHold, restored.Error);
            }
        }
        finally
        {
            await RemoveCliDomFixtureAsync(contextId);
        }
    }

    Task<(int ExitCode, string Stdout, string Stderr)> InvokeWebViewCommandAsync(params string[] args) =>
        InvokeCliCommandAsync(["webview", .. args]);

    [Theory]
    [InlineData("click", "button")]
    [InlineData("fill", "textarea")]
    [InlineData("fill", "[contenteditable]")]
    public async Task CliOperations_Mutations_ExecuteExactlyOnce(string action, string field)
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();
        var contextId = await GetActiveContextIdAsync();
        var operations = new WebViewOperations(Client);
        var retryMutations = Client.RetryMutatingRequests;
        try
        {
            await CreateCliDomFixtureAsync(contextId);
            Client.RetryMutatingRequests = false;
            var selector = $"#{CliFixtureId} {field}";
            var result = action == "click"
                ? await operations.InputDispatchClickAsync(selector, contextId)
                : await operations.InputFillAsync(selector, CliFillText, contextId);
            Assert.Equal(action == "click" ? "Clicked: button"
                : $"Filled: {(field == "textarea" ? "textarea" : "div")} with {CliFillText.Length} chars", result);

            using var state = await ReadCliDomStateAsync(contextId);
            Assert.Equal(action == "click" ? 1 : 0, state.RootElement.GetProperty("clicks").GetInt32());
            Assert.Equal(action == "fill" ? 1 : 0, state.RootElement.GetProperty("inputs").GetInt32());
            Assert.Equal(action == "fill" ? 1 : 0, state.RootElement.GetProperty("changes").GetInt32());
            Assert.Equal(action == "fill" && field == "textarea" ? CliFillText : "", state.RootElement.GetProperty("value").GetString());
            Assert.Equal(action == "fill" && field == "[contenteditable]" ? CliFillText : "", state.RootElement.GetProperty("editable").GetString());
            Output.WriteLine($"Verified CLI {action} on {field}, including exact DOM event counts on {Platform}: {state.RootElement}");
        }
        finally
        {
            Client.RetryMutatingRequests = retryMutations;
            await RemoveCliDomFixtureAsync(contextId);
        }
    }

    [Fact]
    public async Task CliOperations_SelectedContext_LeavesOtherContextUnchanged()
    {
        App.InvalidateBlazorReady();
        var retryMutations = Client.RetryMutatingRequests;
        var leftFixtureCreated = false;
        var rightFixtureCreated = false;
        try
        {
            await NavigateToPageAsync("//multiblazor", "BlazorLeft");
            var deadline = DateTime.UtcNow.AddSeconds(Platform is "android" or "windows" ? 90 : 60);
            JsonElement contexts = default;
            while (DateTime.UtcNow < deadline)
            {
                contexts = await Client.GetCdpWebViewsAsync();
                if (new[] { "BlazorLeft", "BlazorRight" }.All(id => EnumerateContexts(contexts).Any(c =>
                    c.TryGetProperty("automationId", out var automationId) && automationId.GetString() == id && IsReadyContext(c))))
                    break;
                await Task.Delay(500);
            }
            Assert.True(new[] { "BlazorLeft", "BlazorRight" }.All(id => EnumerateContexts(contexts).Any(c =>
                c.TryGetProperty("automationId", out var automationId) && automationId.GetString() == id && IsReadyContext(c))),
                $"Expected both existing multi-Blazor contexts to be ready: {contexts}");

            await CreateCliDomFixtureAsync("BlazorLeft");
            leftFixtureCreated = true;
            await CreateCliDomFixtureAsync("BlazorRight");
            rightFixtureCreated = true;
            Client.RetryMutatingRequests = false;
            var operations = new WebViewOperations(Client);
            Assert.Equal("Clicked: button", await operations.InputDispatchClickAsync($"#{CliFixtureId} button", "BlazorRight"));
            Assert.Equal($"Filled: textarea with {CliFillText.Length} chars",
                await operations.InputFillAsync($"#{CliFixtureId} textarea", CliFillText, "BlazorRight"));
            using var left = await ReadCliDomStateAsync("BlazorLeft");
            using var right = await ReadCliDomStateAsync("BlazorRight");
            Assert.Equal(0, left.RootElement.GetProperty("clicks").GetInt32());
            Assert.Equal(0, left.RootElement.GetProperty("inputs").GetInt32());
            Assert.Equal(0, left.RootElement.GetProperty("changes").GetInt32());
            Assert.Equal("", left.RootElement.GetProperty("value").GetString());
            Assert.Equal(1, right.RootElement.GetProperty("clicks").GetInt32());
            Assert.Equal(1, right.RootElement.GetProperty("inputs").GetInt32());
            Assert.Equal(1, right.RootElement.GetProperty("changes").GetInt32());
            Assert.Equal(CliFillText, right.RootElement.GetProperty("value").GetString());
        }
        finally
        {
            Client.RetryMutatingRequests = retryMutations;
            try
            {
                try
                {
                    if (leftFixtureCreated)
                        await RemoveCliDomFixtureAsync("BlazorLeft");
                }
                finally
                {
                    if (rightFixtureCreated)
                        await RemoveCliDomFixtureAsync("BlazorRight");
                }
            }
            finally
            {
                await NavigateToMainPageAsync();
                App.InvalidateBlazorReady();
            }
        }
    }

    [Fact]
    public async Task InputText_InsertsText()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();
        var contextId = await GetActiveContextIdAsync();

        var response = await PostWithBridgeRetryAsync("/api/v1/webview/input/text", new
        {
            text = "Hello from test",
            contextId,
        });

        Assert.True(response.IsSuccessStatusCode,
            $"/api/v1/webview/input/text returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Screenshot_WebView_ReturnsImage()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();
        var contextId = await GetActiveContextIdAsync();

        var response = await GetWithBridgeRetryAsync($"/api/v1/webview/screenshot?contextId={Uri.EscapeDataString(contextId)}");

        // WebView screenshots rely on either CDP Page.captureScreenshot or native
        // element capture. iOS simulators on CI runners sometimes lack the graphics
        // surface needed for either path, so accept a 400 there as a known limitation.
        if (!response.IsSuccessStatusCode && Platform == "ios")
        {
            Output.WriteLine("WebView screenshot not supported on this iOS runner (known limitation).");
            return;
        }

        Assert.True(response.IsSuccessStatusCode,
            $"/api/v1/webview/screenshot returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task Evaluate_WithDomAccess_Works()
    {
        await EnsureOnBlazorPageAsync();

        var paramsJson = JsonNode.Parse("""{"expression": "document.querySelectorAll('*').length"}""");
        var result = await Client.SendCdpCommandAsync("Runtime.evaluate", paramsJson);

        var text = result.ToString();
        Assert.False(text.Contains("\"error\"", StringComparison.OrdinalIgnoreCase),
            $"Runtime.evaluate returned an error payload: {text}");
        Assert.Contains("\"result\"", text);
        Assert.Contains("\"value\"", text);
    }

    [Fact]
    public async Task MultiBlazorPage_HasMultipleContexts()
    {
        // This test intentionally leaves the Blazor single-WebView page.
        App.InvalidateBlazorReady();
        try
        {
            await NavigateToPageAsync("//multiblazor", "BlazorLeft");

            var timeoutMs = Platform switch
            {
                "ios" => 60000,
                "android" or "windows" => 90000,
                _ => 45000,
            };

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            var contextCount = 0;
            var anyReady = false;
            JsonElement lastJson = default;

            while (DateTime.UtcNow < deadline)
            {
                lastJson = await Client.GetCdpWebViewsAsync();
                contextCount = CountWebViewContexts(lastJson);
                anyReady = AnyReadyContext(lastJson);
                if (contextCount >= 2 && anyReady)
                    break;
                await Task.Delay(500);
            }

            Assert.True(contextCount >= 2,
                $"Expected at least 2 WebView contexts on the multi-Blazor page, got {contextCount}. Last response: {lastJson}");
            Assert.True(anyReady,
                $"Expected at least one ready WebView context on the multi-Blazor page. Last response: {lastJson}");
        }
        finally
        {
            await NavigateToMainPageAsync();
            App.InvalidateBlazorReady();
        }
    }

    [Fact]
    public async Task DefaultContext_FollowsActiveShellPage_AfterMultiBlazor()
    {
        App.InvalidateBlazorReady();
        try
        {
            await NavigateToPageAsync("//multiblazor", "BlazorLeft");

            var timeoutMs = Platform switch
            {
                "ios" => 60000,
                "android" or "windows" => 90000,
                _ => 45000,
            };
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            JsonElement contexts = default;
            while (DateTime.UtcNow < deadline)
            {
                contexts = await Client.GetCdpWebViewsAsync();
                var items = EnumerateContexts(contexts).ToList();
                if (items.Count >= 2 && items.Count(IsReadyContext) >= 2)
                    break;
                await Task.Delay(500);
            }

            Assert.True(
                EnumerateContexts(contexts).Count(IsReadyContext) >= 2,
                $"Expected both multi-Blazor contexts to be ready. Last response: {contexts}");

            await Client.SendCdpCommandAsync(
                "Runtime.evaluate",
                JsonNode.Parse("""{"expression":"window.__devflowContext = 'right'"}"""),
                "BlazorRight");

            await Client.NavigateAsync("//blazor");
            App.InvalidateBlazorReady();
            await EnsureOnBlazorPageAsync();

            await Client.SendCdpCommandAsync(
                "Runtime.evaluate",
                JsonNode.Parse("""{"expression":"window.__devflowContext = 'main'"}"""),
                "BlazorWebView");
            var defaultResult = await Client.SendCdpCommandAsync(
                "Runtime.evaluate",
                JsonNode.Parse("""{"expression":"window.__devflowContext"}"""));

            Assert.Contains("\"value\":\"main\"", defaultResult.ToString());

            contexts = await Client.GetCdpWebViewsAsync();
            var active = EnumerateContexts(contexts)
                .Where(context =>
                    context.TryGetProperty("active", out var activeProperty) &&
                    activeProperty.ValueKind == JsonValueKind.True)
                .ToList();
            var activeContext = Assert.Single(active);
            Assert.Equal(
                "BlazorWebView",
                activeContext.GetProperty("automationId").GetString());
        }
        finally
        {
            await NavigateToMainPageAsync();
            App.InvalidateBlazorReady();
        }
    }
}
