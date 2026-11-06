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
        await layoutTab.FocusAsync();
        Volatile.Write(ref capabilities, NoLayoutCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));
        Assert.Equal("df-tab-logs", await _page.EvaluateAsync<string>("document.activeElement.id"));

        await _page.Locator("#df-toggle-tree").ClickAsync();
        await Expect(_page.Locator("#df-tree-pane")).ToBeVisibleAsync();
        var bounds = _page.Locator("#df-toggle-bounds");
        if (!await bounds.IsVisibleAsync())
            await more.ClickAsync();
        await bounds.ClickAsync();
        Assert.True(await _page.Locator("body").EvaluateAsync<bool>("body => body.classList.contains('df-show-all')"));

        await _page.Locator("#df-mode-inspect").ClickAsync();
        Volatile.Write(ref capabilities, ActionsOnlyCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));
        await Expect(_page.Locator("#df-mode-inspect")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-mode-interact")).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(_page.Locator("#df-mode-interact")).ToHaveAttributeAsync("tabindex", "0");
        await Expect(_page.Locator("#df-tree-pane")).ToBeHiddenAsync();
        Assert.False(await _page.Locator("body").EvaluateAsync<bool>("body => body.classList.contains('df-show-all')"));

        Volatile.Write(ref capabilities, ScreenshotOnlyCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));

        await Expect(_page.Locator("#df-toolbar > .df-modes")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-toolbar > .df-sep")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-toggle-fit")).ToBeVisibleAsync();

        await more.FocusAsync();
        await _page.SetViewportSizeAsync(1440, 820);
        await _page.WaitForTimeoutAsync(100);
        await Expect(more).ToBeHiddenAsync();
        Assert.NotEqual("BODY", await _page.EvaluateAsync<string>("document.activeElement.tagName"));
    }

    [InspectorBrowserFact]
    public async Task CommandPolicy_ExplainsSelectionReadOnlyAndDisconnectedStates()
    {
        var writer = 1;
        var capabilities = FullCapabilityPayload;
        await PrepareLayoutWorkspaceAsync(
            capabilityResponse: () => Volatile.Read(ref capabilities),
            controlResponse: () => Volatile.Read(ref writer) == 1
                ? """{"youAreWriter":true,"heldByOther":false,"authority":"agent"}"""
                : """{"youAreWriter":false,"heldByOther":true,"label":"VS Code Inspector","authority":"agent"}""");

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
        await Expect(record).ToHaveAttributeAsync("aria-label", "Rec (0)");
        Volatile.Write(ref capabilities, ActionsUnsupportedCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));
        await Expect(record).ToBeVisibleAsync();
        await Expect(record).ToHaveAttributeAsync("aria-disabled", "false");
        await Expect(_page.Locator("#df-record-cancel")).ToBeEnabledAsync();

        Volatile.Write(ref capabilities, FullCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));
        await record.ClickAsync();
        await Expect(_page.Locator("#df-workflow-replay")).ToBeEnabledAsync();
        Assert.Equal(1, recordRequests);

        Volatile.Write(ref capabilities, ActionsUnsupportedCapabilityPayload);
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/capabilities", StringComparison.Ordinal));
        await Expect(_page.Locator("#df-toggle-replay")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-workflow-replay")).ToBeDisabledAsync();
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
        await record.EvaluateAsync("button => button.click()");
        Assert.Equal(1, recordRequests);

        await _page.EvaluateAsync("() => window.dispatchEvent(new Event('offline'))");
        await Expect(record).ToHaveAttributeAsync("data-df-availability", "disconnected");
        await Expect(record).ToHaveAttributeAsync("data-df-reason", "app-disconnected");
        await Expect(_page.Locator("#df-take-control")).ToHaveAttributeAsync("aria-disabled", "true");

        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/state", StringComparison.Ordinal));
        await Expect(record).ToHaveAttributeAsync("data-df-availability", "readOnly");
    }
}
