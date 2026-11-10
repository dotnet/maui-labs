using Microsoft.Playwright;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

public partial class InspectorLayoutWorkspaceTests
{
    private const string FullCapabilityPayload = """
        {
          "agent":{"framework":"maui"},
          "capabilities":{
            "ui.tree":{"supported":true,"features":["css-selector"]},
            "ui.actions":{"supported":true,"features":["tap","navigate","properties"]},
            "ui.screenshot":{"supported":true,"features":["fullscreen"]},
            "ui.layoutDiagnostics":{"features":["clipping","coverage"]}
          }
        }
        """;

    private const string NoLayoutCapabilityPayload = """
        {
          "agent":{"framework":"maui"},
          "capabilities":{
            "ui.tree":{"supported":true,"features":["css-selector"]},
            "ui.actions":{"supported":true,"features":["tap","navigate","properties"]},
            "ui.screenshot":{"supported":true,"features":["fullscreen"]}
          }
        }
        """;

    private const string ScreenshotOnlyCapabilityPayload = """
        {
          "agent":{"framework":"headless"},
          "capabilities":{
            "ui.tree":{"supported":false,"features":[],"reason":"Tree inspection is unavailable."},
            "ui.actions":{"supported":false,"features":[],"reason":"UI actions are unavailable."},
            "ui.screenshot":{"supported":true,"features":["fullscreen"]}
          }
        }
        """;

    private const string ActionsOnlyCapabilityPayload = """
        {
          "agent":{"framework":"maui"},
          "capabilities":{
            "ui.tree":{"supported":false,"features":[],"reason":"Tree inspection is unavailable."},
            "ui.actions":{"supported":true,"features":["tap","navigate","properties"]},
            "ui.screenshot":{"supported":true,"features":["fullscreen"]}
          }
        }
        """;

    private const string ActionsUnsupportedCapabilityPayload = """
        {
          "agent":{"framework":"maui"},
          "capabilities":{
            "ui.tree":{"supported":true,"features":["css-selector"]},
            "ui.actions":{"supported":false,"features":[],"reason":"UI actions are unavailable."},
            "ui.screenshot":{"supported":true,"features":["fullscreen"]}
          }
        }
        """;

    [InspectorBrowserFact]
    public async Task CommandPolicy_LegacyCapabilitiesKeepExistingToolbarDiscoverable()
    {
        await PrepareLayoutWorkspaceAsync(capabilityResponse: () =>
            """{"capabilities":{}}""");

        await Expect(_page.Locator("#df-mode-interact")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-mode-inspect")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-toggle-diagnostics")).ToBeVisibleAsync();
        Assert.False(await _page.Locator("#df-tab-layout").EvaluateAsync<bool>("tab => tab.hidden"));
        await Expect(_page.Locator("#df-take-control")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-toggle-record")).ToHaveAttributeAsync("data-df-availability", "available");
    }

    [InspectorBrowserFact]
    public async Task CommandPolicy_HidesUnsupportedCommandsAndPreservesOverflowFocus()
    {
        var capabilities = FullCapabilityPayload;
        var previewMutations = 0;
        _page.Request += (_, request) =>
        {
            if (request.Url.EndsWith("/api/tap", StringComparison.Ordinal) ||
                request.Url.EndsWith("/api/scroll", StringComparison.Ordinal) ||
                request.Url.EndsWith("/api/gesture", StringComparison.Ordinal))
                Interlocked.Increment(ref previewMutations);
        };
        await _page.SetViewportSizeAsync(260, 820);
        await PrepareLayoutWorkspaceAsync(capabilityResponse: () => Volatile.Read(ref capabilities));

        var more = _page.Locator("#df-more");
        await Expect(more).ToBeVisibleAsync();
        await more.ClickAsync();
        var layout = _page.Locator("#df-toggle-diagnostics");
        Assert.Equal("df-toolbar-overflow", await layout.EvaluateAsync<string>("button => button.parentElement.id"));
        await layout.FocusAsync();

        Volatile.Write(ref capabilities, NoLayoutCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));

        await Expect(layout).ToBeHiddenAsync();
        Assert.True(await _page.Locator("#df-tab-layout").EvaluateAsync<bool>("tab => tab.hidden"));
        await Expect(_page.Locator("#df-toolbar-overflow")).ToBeVisibleAsync();
        Assert.Equal("df-assert", await _page.EvaluateAsync<string>("document.activeElement.id"));

        Volatile.Write(ref capabilities, "{");
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));
        await Expect(layout).ToBeHiddenAsync();

        Volatile.Write(ref capabilities, FullCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));
        var data = _page.Locator("#df-toggle-dock");
        if (!await data.IsVisibleAsync())
            await more.ClickAsync();
        await data.ClickAsync();
        var layoutTab = _page.Locator("#df-tab-layout");
        await layoutTab.ClickAsync();
        await Expect(_page.Locator("#layout-coverage")).ToHaveTextAsync(
            new System.Text.RegularExpressions.Regex("Partial coverage|Unstable snapshot"));
        await layoutTab.FocusAsync();
        Volatile.Write(ref capabilities, NoLayoutCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));
        await Expect(layoutTab).ToBeHiddenAsync();
        Assert.Equal("df-tab-logs", await _page.EvaluateAsync<string>("document.activeElement.id"));

        Volatile.Write(ref capabilities, FullCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));
        await _page.Locator("#df-tab-layout").ClickAsync();
        await Expect(_page.Locator("#layout-coverage")).ToHaveTextAsync("Stale snapshot");

        await _page.Locator("#df-toggle-tree").ClickAsync();
        await Expect(_page.Locator("#df-tree-pane")).ToBeVisibleAsync();
        var bounds = _page.Locator("#df-toggle-bounds");
        if (!await bounds.IsVisibleAsync())
            await more.ClickAsync();
        await bounds.ClickAsync();
        Assert.True(await _page.Locator("body").EvaluateAsync<bool>("body => body.classList.contains('df-show-all')"));

        await _page.Locator("#df-mode-inspect").ClickAsync();
        await _page.Locator(".df-tree-node").First.FocusAsync();
        await _page.Locator("#df-status").EvaluateAsync("status => status.textContent = 'Recording continues.'");
        Volatile.Write(ref capabilities, ActionsOnlyCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));
        await Expect(_page.Locator("#df-mode-inspect")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-mode-interact")).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(_page.Locator("#df-mode-interact")).ToHaveAttributeAsync("tabindex", "0");
        await Expect(_page.Locator("#df-tree-pane")).ToBeHiddenAsync();
        Assert.NotEqual("BODY", await _page.EvaluateAsync<string>("document.activeElement.tagName"));
        await Expect(_page.Locator("#df-status")).ToHaveTextAsync("Recording continues.");
        Assert.False(await _page.Locator("body").EvaluateAsync<bool>("body => body.classList.contains('df-show-all')"));

        Volatile.Write(ref capabilities, ScreenshotOnlyCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));

        await Expect(_page.Locator("#df-toolbar > .df-modes")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-toolbar > .df-sep")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-toggle-fit")).ToBeVisibleAsync();
        await _page.Locator("#df-viewport-wrap").ClickAsync(new()
        {
            Position = new() { X = 100, Y = 100 }
        });
        await _page.Mouse.WheelAsync(0, 100);
        Assert.Equal(0, previewMutations);

        await more.FocusAsync();
        await _page.SetViewportSizeAsync(1440, 820);
        await _page.WaitForTimeoutAsync(100);
        await Expect(more).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-tree-pane")).ToBeHiddenAsync();
        Assert.NotEqual("BODY", await _page.EvaluateAsync<string>("document.activeElement.tagName"));
    }

    [InspectorBrowserFact]
    public async Task CommandPolicy_RevokedActionsDiscardQueuedInputAndPreserveDraft()
    {
        var capabilities = FullCapabilityPayload;
        var scrollRequests = 0;
        var fillRequests = 0;
        _page.Request += (_, request) =>
        {
            if (request.Url.EndsWith("/api/scroll", StringComparison.Ordinal))
                Interlocked.Increment(ref scrollRequests);
            if (request.Url.EndsWith("/api/fill", StringComparison.Ordinal))
                Interlocked.Increment(ref fillRequests);
        };
        await PrepareLayoutWorkspaceAsync(
            capabilityResponse: () => Volatile.Read(ref capabilities),
            elements: """
                <div class="devflow-element" data-id="layout-root" data-type="Window" data-automationId="InputRoot" data-isVisible="true" data-isEnabled="true" style="position:absolute;left:0px;top:0px;width:320px;height:600px;"></div>
                <div class="devflow-element" data-id="layout-target" data-parentId="layout-root" data-type="Entry" data-automationId="Input" data-text="before" data-capabilities="set-value" data-isVisible="true" data-isEnabled="true" style="position:absolute;left:20px;top:40px;width:180px;height:40px;"></div>
                """);

        await _page.Locator(".devflow-element[data-id='layout-target']").ClickAsync();
        var editor = _page.Locator("#app-viewport > input[data-element-id='layout-target']");
        await Expect(editor).ToBeVisibleAsync();
        await editor.FillAsync("preserved draft");
        await _page.EvaluateAsync("""
            () => document.getElementById('app-viewport').dispatchEvent(
              new WheelEvent('wheel', {
                bubbles: true,
                cancelable: true,
                clientX: 120,
                clientY: 120,
                deltaY: 100,
              }))
            """);

        Volatile.Write(ref capabilities, ActionsUnsupportedCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));
        await _page.Clock.RunForAsync(150);
        await editor.PressAsync("Enter");

        Assert.Equal(0, scrollRequests);
        Assert.Equal(0, fillRequests);
        await Expect(editor).ToHaveValueAsync("preserved draft");
        await Expect(editor).ToHaveAttributeAsync("aria-invalid", "true");
    }

    [InspectorBrowserFact]
    public async Task CommandPolicy_ExplainsSelectionReadOnlyAndDisconnectedStates()
    {
        var writer = 1;
        var holderLabel = "VS Code Inspector";
        var capabilities = FullCapabilityPayload;
        await PrepareLayoutWorkspaceAsync(
            capabilityResponse: () => Volatile.Read(ref capabilities),
            controlResponse: () => Volatile.Read(ref writer) == 1
                ? """{"youAreWriter":true,"heldByOther":false,"authority":"agent"}"""
                : System.Text.Json.JsonSerializer.Serialize(new
                {
                    youAreWriter = false,
                    heldByOther = true,
                    label = Volatile.Read(ref holderLabel)
                }));

        var source = _page.Locator("#df-open-source");
        await Expect(source).ToHaveAttributeAsync("aria-disabled", "true");
        await Expect(source).ToHaveAttributeAsync("data-df-reason", "source-required");
        await Expect(source).ToHaveAttributeAsync("title", new System.Text.RegularExpressions.Regex("Select an element with XAML source mapping"));
        Assert.True(await _page.Locator("#df-open-source-availability").EvaluateAsync<bool>("node => node.hidden"));

        await _page.Locator(".df-tree-node[data-tree-id='layout-target']").ClickAsync();
        await Expect(source).ToHaveAttributeAsync("aria-disabled", "false");

        var recordRequests = 0;
        var replayRequests = 0;
        await _page.RouteAsync("**/api/flows/record/start", route =>
        {
            Interlocked.Increment(ref recordRequests);
            return route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = """{"ok":true,"recordingId":"fixture","name":"fixture"}"""
            });
        });
        await _page.RouteAsync("**/api/flows/record/stop", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """{"ok":true,"recording":false,"name":"fixture","steps":1,"markdown":"# fixture"}"""
        }));
        await _page.RouteAsync("**/api/flows/replay", route =>
        {
            Interlocked.Increment(ref replayRequests);
            return route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = """{"ok":true,"passed":1,"failed":0,"total":1,"results":[]}"""
            });
        });

        var record = _page.Locator("#df-toggle-record");
        await record.ClickAsync();
        await Expect(record).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(record).ToHaveAttributeAsync("aria-label", "Record workflow");
        Volatile.Write(ref capabilities, ActionsUnsupportedCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));
        await Expect(record).ToBeVisibleAsync();
        await Expect(record).ToHaveAttributeAsync("aria-disabled", "false");
        await Expect(_page.Locator("#df-record-cancel")).ToBeEnabledAsync();
        await _page.EvaluateAsync("() => window.dispatchEvent(new Event('offline'))");
        await Expect(record).ToHaveAttributeAsync("aria-disabled", "false");
        await Expect(_page.Locator("#df-record-cancel")).ToBeEnabledAsync();
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => IsStateUrl(response.Url));

        Volatile.Write(ref capabilities, FullCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));
        await record.ClickAsync();
        var workflowReplay = _page.Locator("#df-workflow-replay");
        await Expect(workflowReplay).ToBeEnabledAsync();
        await workflowReplay.FocusAsync();
        Assert.Equal(1, recordRequests);

        Volatile.Write(ref capabilities, ActionsUnsupportedCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));
        await Expect(_page.Locator("#df-toggle-replay")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-workflow-replay")).ToBeHiddenAsync();
        Assert.NotEqual("BODY", await _page.EvaluateAsync<string>("document.activeElement.tagName"));
        await _page.Locator("#df-workflow-replay").EvaluateAsync("button => button.click()");
        Assert.Equal(0, replayRequests);

        Volatile.Write(ref capabilities, FullCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));

        Volatile.Write(ref writer, 0);
        await _page.RunAndWaitForResponseAsync(
            () => _page.Clock.RunForAsync(4100),
            response => response.Url.EndsWith("/api/control", StringComparison.Ordinal));

        await Expect(record).ToHaveAttributeAsync("aria-disabled", "true");
        await Expect(record).ToHaveAttributeAsync("data-df-availability", "readOnly");
        await Expect(record).ToHaveAttributeAsync("title", new System.Text.RegularExpressions.Regex("VS Code Inspector is driving"));
        await Expect(_page.Locator("#df-take-control")).ToHaveAttributeAsync(
            "title", new System.Text.RegularExpressions.Regex("VS Code Inspector is driving"));
        await Expect(_page.Locator("#df-take-control")).ToBeVisibleAsync();
        await Expect(workflowReplay).ToHaveAttributeAsync("aria-disabled", "true");
        Assert.False(await workflowReplay.EvaluateAsync<bool>("button => button.disabled"));
        await Expect(workflowReplay).ToHaveAttributeAsync("aria-describedby", "df-toggle-replay-availability");
        await record.EvaluateAsync("button => button.click()");
        Assert.Equal(1, recordRequests);

        Volatile.Write(ref holderLabel, "Canvas Inspector");
        await _page.RunAndWaitForResponseAsync(
            () => _page.Clock.RunForAsync(4100),
            response => response.Url.EndsWith("/api/control", StringComparison.Ordinal));
        await Expect(record).ToHaveAttributeAsync(
            "title", new System.Text.RegularExpressions.Regex("Canvas Inspector is driving"));

        await _page.EvaluateAsync("() => window.dispatchEvent(new Event('offline'))");
        await Expect(record).ToHaveAttributeAsync("data-df-availability", "disconnected");
        await Expect(record).ToHaveAttributeAsync("data-df-reason", "app-disconnected");
        await Expect(_page.Locator("#df-take-control")).ToHaveAttributeAsync("aria-disabled", "true");

        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => IsStateUrl(response.Url));
        await Expect(record).ToHaveAttributeAsync("data-df-availability", "readOnly");
    }
}
