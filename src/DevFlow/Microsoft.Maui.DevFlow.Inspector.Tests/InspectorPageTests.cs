using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.DevFlow.Testing;
using Microsoft.Playwright;
using Xunit;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

/// <summary>
/// Playwright integration tests for the DevFlow Web Inspector.
/// Requires the broker running with a connected MAUI app.
/// The inspector is available at http://localhost:19223/inspector/.
/// Set INSPECTOR_URL environment variable to override the default URL.
///
/// The default URL points to the broker's explicit single-agent convenience route.
///
/// These tests are categorized as "Integration" and excluded from normal CI runs
/// (which cannot spin up a broker + connected agent). Run them locally with:
///   dotnet test --filter "Category=Integration"
/// </summary>
[Collection("Inspector")]
[Trait("Category", "Integration")]
public class InspectorPageTests : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IBrowserContext _context = null!;
    private IPage _page = null!;

    private Uri BaseUri => new(Environment.GetEnvironmentVariable("INSPECTOR_URL") ?? "http://localhost:19223/inspector/default/");

    private string BaseUrl => BaseUri.ToString();

    private string ResolveUrl(string relativePath) => new Uri(BaseUri, relativePath).ToString();

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
        _context = await _browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
        });
        _page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            await ResetInspectorStateAsync();
        }
        finally
        {
            await _context.DisposeAsync();
            await _browser.DisposeAsync();
            _playwright.Dispose();
        }
    }

    [LiveInspectorFact]
    public async Task ViewportUsesWindowDimensionsFromAgent()
    {
        await _page.GotoAsync(BaseUrl);
        var viewport = _page.Locator("#app-viewport");
        await Expect(viewport).ToBeVisibleAsync();

        var width = await viewport.GetAttributeAsync("data-width");
        var height = await viewport.GetAttributeAsync("data-height");

        var w = double.Parse(width!, CultureInfo.InvariantCulture);
        var h = double.Parse(height!, CultureInfo.InvariantCulture);
        Assert.True(w > 0, "Viewport width should be positive");
        Assert.True(h > 0, "Viewport height should be positive");

        // Correlate with the live /api/state values to prove the DOM is wired to the
        // agent-reported window dimensions rather than a hardcoded fallback. Using a
        // hardcoded "not iPhone size" assertion would false-positive on a real iPhone.
        var stateResponse = await _page.APIRequest.GetAsync(ResolveUrl("api/state"));
        Assert.True(stateResponse.Ok);
        var stateJson = System.Text.Json.JsonDocument.Parse(await stateResponse.TextAsync());
        var apiW = stateJson.RootElement.GetProperty("viewportWidth").GetDouble();
        var apiH = stateJson.RootElement.GetProperty("viewportHeight").GetDouble();

        // Allow 1px slack for fractional rounding between layout passes.
        Assert.True(Math.Abs(apiW - w) <= 1.0,
            $"DOM data-width {w} should match /api/state viewportWidth {apiW}");
        Assert.True(Math.Abs(apiH - h) <= 1.0,
            $"DOM data-height {h} should match /api/state viewportHeight {apiH}");
    }

    [LiveInspectorFact]
    public async Task ViewportHasFixedDimensions()
    {
        await _page.GotoAsync(BaseUrl);
        var viewport = _page.Locator("#app-viewport");

        // Viewport should have explicit width/height style
        var style = await viewport.GetAttributeAsync("style");
        Assert.NotNull(style);
        Assert.Contains("width:", style);
        Assert.Contains("height:", style);
    }

    [LiveInspectorFact]
    public async Task ScreenshotImageIsPresent()
    {
        await _page.GotoAsync(BaseUrl);
        var screenshot = _page.Locator("#screenshot");
        await Expect(screenshot).ToBeVisibleAsync();

        var src = await screenshot.GetAttributeAsync("src");
        Assert.Contains("screenshot.png", src);
    }

    [LiveInspectorFact]
    public async Task NoInspectorChromeRendered()
    {
        await _page.GotoAsync(BaseUrl);

        // No toolbar, no connection status — the host inspector tool provides its own chrome
        await Expect(_page.Locator("#devflow-toolbar")).ToHaveCountAsync(0);
        await Expect(_page.Locator("#btn-back")).ToHaveCountAsync(0);
        await Expect(_page.Locator("#connection-status")).ToHaveCountAsync(0);
    }

    [LiveInspectorFact]
    public async Task ElementsRenderedAsPositionedDivs()
    {
        await _page.GotoAsync(BaseUrl);
        var elements = _page.Locator(".devflow-element");
        var count = await elements.CountAsync();
        Assert.True(count > 0, "Should have at least one element div");

        // First element should have required data attributes
        var first = elements.First;
        var id = await first.GetAttributeAsync("data-id");
        var type = await first.GetAttributeAsync("data-type");
        Assert.NotNull(id);
        Assert.NotNull(type);
    }

    [LiveInspectorFact]
    public async Task ElementPositionsMatchAppCoordinates()
    {
        await _page.GotoAsync(BaseUrl);

        // Find an element with bounds
        var positioned = _page.Locator(".devflow-element[style*='left:']");
        var count = await positioned.CountAsync();
        Assert.True(count > 0, "Should have positioned elements");

        var style = await positioned.First.GetAttributeAsync("style");
        Assert.NotNull(style);
        Assert.Contains("position:absolute", style);
        Assert.Matches(@"left:\d", style);
        Assert.Matches(@"top:\d", style);
    }

    [LiveInspectorFact]
    public async Task ElementTreeIsFlatNotNested()
    {
        await _page.GotoAsync(BaseUrl);

        // All elements should be direct children of viewport (flat rendering)
        var directChildren = _page.Locator("#app-viewport > .devflow-element");
        var nested = _page.Locator(".devflow-element > .devflow-element");

        Assert.True(await directChildren.CountAsync() > 0, "Should have flat element divs");
        Assert.Equal(0, await nested.CountAsync());
    }

    [LiveInspectorFact]
    public async Task DataAttributesUseCamelCase()
    {
        // CSS attribute selectors like [data-isVisible] are case-insensitive in HTML,
        // so a Locator-based assertion would also match the lowercased serialization
        // that we are guarding against. Fetch the raw HTML and assert the byte-level
        // serialization preserves camelCase as advertised.
        var response = await _page.APIRequest.GetAsync(BaseUrl);
        Assert.True(response.Ok);
        var html = await response.TextAsync();

        // DevFlow properties use camelCase: isVisible, isEnabled, fullType
        Assert.Contains("data-isVisible", html);
        Assert.Contains("data-isEnabled", html);
        Assert.DoesNotContain("data-isvisible", html);
        Assert.DoesNotContain("data-isenabled", html);
    }

    [LiveInspectorFact]
    public async Task CssServedSeparately()
    {
        var response = await _page.APIRequest.GetAsync(ResolveUrl("devflow.css"));
        Assert.True(response.Ok);
        var text = await response.TextAsync();
        Assert.Contains("#app-viewport", text);
        Assert.Contains(".devflow-element", text);
        // The shared inspector is a full interactive tool (toolbar, tree, docked properties,
        // timeline), so it ships its own hover/selection chrome — hover styles are expected here.

        var workbenchResponse = await _page.APIRequest.GetAsync(ResolveUrl("inspector-workbench.css"));
        Assert.True(workbenchResponse.Ok);
        Assert.Contains("text/css", workbenchResponse.Headers["content-type"]);
        Assert.Contains("#df-workbench", await workbenchResponse.TextAsync());
    }

    [LiveInspectorFact]
    public async Task InspectorModulesAreServedAsJavaScript()
    {
        foreach (var (file, exportedSymbol) in new[]
        {
            ("inspector-api.js", "export function createInspectorApi"),
            ("inspector-dialog.js", "export function confirmModal"),
            ("inspector-data-context.js", "export function createDataSnapshot"),
            ("inspector-data-controller.js", "export function createDataDockController"),
            ("inspector-data-ui.js", "export function createDataUi"),
            ("inspector-diagnostics.js", "export function createLayoutDataPayload"),
            ("inspector-evidence.js", "export function createEvidenceController"),
            ("inspector-study.js", "export function createPrototypeStudyJournal"),
            ("inspector-host-bridge.js", "export function createInspectorHostBridge"),
            ("inspector-workbench.js", "export function createInspectorWorkbench"),
            ("inspector-plan.js", "export function renderPlanPanel"),
            ("inspector-steps.js", "export function renderStepsPanel"),
            ("inspector-run.js", "export function renderRunPanel"),
            ("inspector-trace.js", "export function renderTracePanel"),
            ("inspector-repair.js", "export function renderRepairPanel"),
            ("inspector-improve.js", "export function renderImprovePanel"),
            ("inspector-source.js", "export function renderSourceProposalPanel"),
            ("inspector-properties.js", "export function createPropertyGridController"),
            ("inspector-tree.js", "export function createElementTreeController"),
        })
        {
            var response = await _page.APIRequest.GetAsync(ResolveUrl(file));
            Assert.True(response.Ok);
            Assert.Contains("application/javascript", response.Headers["content-type"]);
            Assert.Equal("no-store", response.Headers["cache-control"]);
            Assert.Equal("nosniff", response.Headers["x-content-type-options"]);
            Assert.Contains(exportedSymbol, await response.TextAsync());
        }
    }

    [LiveInspectorFact]
    public async Task TestWorkbenchToolbar_SeparatesJourneyFromContextualTools()
    {
        await _page.GotoAsync(BaseUrl);

        await _page.EvaluateAsync("""
            () => {
                window.__lastKeydown = null;
                window.addEventListener("keydown", event => {
                    window.__lastKeydown = { key: event.key, code: event.code, ctrlKey: event.ctrlKey, altKey: event.altKey, metaKey: event.metaKey, shiftKey: event.shiftKey, target: event.target && event.target.id };
                }, true);
            }
            """);

        var button = _page.Locator("#df-toggle-workbench");
        await Expect(button).ToHaveTextAsync("Tests");
        await button.ClickAsync();

        var workbench = _page.Locator("#df-workbench");
        await Expect(workbench).ToBeVisibleAsync();
        await Expect(workbench).ToHaveAttributeAsync("aria-label", "Tests");
        foreach (var stage in new[] { "goal", "record", "review", "run", "results" })
            await Expect(_page.Locator($"#df-workbench-stage-{stage}")).ToBeVisibleAsync();

        await Expect(_page.Locator("#df-workbench-tabs")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-workbench-advanced-tools")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-workbench-tabs [role=tab]")).ToHaveCountAsync(9);
        await Expect(_page.Locator("#df-workbench-tabs [role=tablist]")).ToHaveCountAsync(2);
        await Expect(_page.Locator(".df-workbench-stage-list [role=tab]")).ToHaveCountAsync(5);
        await Expect(_page.Locator(".df-workbench-tool-list [role=tab]")).ToHaveCountAsync(4);
        foreach (var tab in new[] { "requests", "repair", "improve", "source" })
            await Expect(_page.Locator($"#df-workbench-tab-{tab}")).ToBeHiddenAsync();

        var selectedTabColors = await _page.Locator("#df-workbench-stage-goal").EvaluateAsync<string[]>(
            """
            element => {
              const probe = document.createElement('span');
              probe.style.color = 'var(--df-fg)';
              element.append(probe);
              const expected = getComputedStyle(probe).color;
              probe.remove();
              return [
                getComputedStyle(element).color,
                expected,
                element.dataset.state || ''
              ];
            }
            """);
        Assert.True(
            selectedTabColors[0] == selectedTabColors[1],
            $"Selected tab color {selectedTabColors[0]} did not match {selectedTabColors[1]}; " +
            $"state={selectedTabColors[2]}.");

        var tabsBox = await _page.Locator("#df-workbench-tabs").BoundingBoxAsync();
        var contentBox = await _page.Locator("#df-workbench-content").BoundingBoxAsync();
        Assert.NotNull(tabsBox);
        Assert.NotNull(contentBox);
        Assert.True(contentBox.Y >= tabsBox.Y + tabsBox.Height - 1,
            "Tabbed content must start below the Tests tab row rather than stacking over it.");

        foreach (var selector in new[]
        {
            "#df-workbench-stage-record", "#df-workbench-stage-review", "#df-workbench-stage-run",
        })
            await Expect(_page.Locator(selector)).ToBeDisabledAsync();

        await _page.Locator("#df-workbench-stage-results").ClickAsync();
        await Expect(_page.Locator("#df-workbench-panel-trace")).ToBeVisibleAsync();
        Assert.Contains("df-active", await _page.Locator("#df-workbench-stage-results").GetAttributeAsync("class"));
        await Expect(_page.Locator("#df-timeline")).ToHaveCountAsync(1);
    }

    [LiveInspectorFact]
    public async Task TestWorkbenchPendingAgentRequest_BadgesWithoutStealingActiveTab()
    {
        await _page.RouteAsync("**/api/workbench/agent-requests", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """
                {
                  "ok": true,
                  "appName": "Test app",
                  "platform": "WinUI",
                  "requests": [
                    {
                      "approvalRequestId": "approval_layout_test",
                      "kind": "run",
                      "state": "pending",
                      "intent": "Run this test once",
                      "expiresAt": "2099-01-01T00:00:00Z",
                      "requestedScope": {
                        "allowedActions": ["run"],
                        "allowedSelectors": [],
                        "allowedRoutes": [],
                        "allowedSideEffectClasses": [],
                        "maxActionCount": 1,
                        "maxValueBytes": 0
                      }
                    }
                  ]
                }
                """,
        }));

        await _page.GotoAsync(BaseUrl);
        await _page.Locator("#df-toggle-workbench").ClickAsync();

        await Expect(_page.Locator("#df-workbench-stage-goal")).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("df-active"));
        await Expect(_page.Locator("#df-test-agent-request-badge")).ToHaveTextAsync("1");
        await Expect(_page.Locator("#df-agent-requests-badge")).ToHaveTextAsync("1");
        await Expect(_page.Locator("#df-agent-requests")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-workbench-tab-requests")).ToBeEnabledAsync();

        await _page.Locator("#df-workbench-tab-requests").ClickAsync();
        await Expect(_page.Locator("#df-agent-requests")).ToBeVisibleAsync();
        var request = _page.Locator("[data-approval-request-id=approval_layout_test]");
        await Expect(request).ToBeVisibleAsync();
        var review = request.Locator(".df-agent-request-details");
        await Expect(review).ToBeVisibleAsync();
        Assert.True(await review.EvaluateAsync<bool>("details => details.open"));
        await Expect(request).ToContainTextAsync("Your agent would like to run this test once");
        await Expect(request.GetByRole(AriaRole.Button, new() { Name = "Allow one run", Exact = true })).ToBeVisibleAsync();
        await Expect(request.GetByRole(AriaRole.Button, new() { Name = "Reject", Exact = true })).ToBeVisibleAsync();
    }

    [LiveInspectorFact]
    public async Task TestWorkbenchToolTabs_StayHiddenUntilUseful()
    {
        await _page.RouteAsync("**/api/workbench/agent-requests", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"appName":"Test app","platform":"WinUI","requests":[]}""",
        }));

        await _page.GotoAsync(BaseUrl);
        await _page.Locator("#df-toggle-workbench").ClickAsync();

        foreach (var (selector, title) in new[]
        {
            ("#df-workbench-tab-requests", "Agent requests appear here after your agent prepares a test or asks to run it."),
            ("#df-workbench-tab-repair", "Open a failed local result to unlock Repair."),
            ("#df-workbench-tab-improve", "Record or open a test to unlock Improve."),
            ("#df-workbench-tab-source", "Select a source-mapped control to unlock Source."),
        })
        {
            await Expect(_page.Locator(selector)).ToBeHiddenAsync();
            await Expect(_page.Locator(selector)).ToBeDisabledAsync();
            await Expect(_page.Locator(selector)).ToHaveAttributeAsync("title", title);
        }
    }

    [LiveInspectorFact]
    public async Task TestWorkbenchSource_SelectedControlShowsOnlyCheckSource()
    {
        await _page.GotoAsync(BaseUrl);
        await _page.Locator("#df-mode-inspect").ClickAsync();
        var mappedId = await _page.EvaluateAsync<string>(
            """
            () => {
              for (const element of document.querySelectorAll('.devflow-element[data-hassource=true]')) {
                const id = element.getAttribute('data-id');
                const row = id && document.querySelector(`.df-tree-node[data-tree-id='${id}']`);
                if (row) {
                  row.click();
                  return id;
                }
              }
              return '';
            }
            """);
        if (string.IsNullOrWhiteSpace(mappedId))
        {
            await _page.Locator("#df-toggle-workbench").ClickAsync();
            await Expect(_page.Locator("#df-workbench-tab-source")).ToBeHiddenAsync();
            await Expect(_page.Locator("#df-workbench-tab-source")).ToBeDisabledAsync();
            await Expect(_page.Locator("#df-workbench-tab-source"))
                .ToHaveAttributeAsync("title", "Select a source-mapped control to unlock Source.");
            return;
        }

        await _page.Locator("#df-toggle-workbench").ClickAsync();
        await Expect(_page.Locator("#df-workbench-tab-source")).ToBeEnabledAsync();
        await _page.Locator("#df-workbench-tab-source").ClickAsync();
        var source = _page.Locator("#df-workbench-panel-source");
        var automationId = source.GetByLabel("New AutomationId");
        await Expect(automationId).ToBeVisibleAsync();
        await automationId.FillAsync("ProgressiveSourceId");
        await Expect(automationId).ToHaveValueAsync("ProgressiveSourceId");
        await Expect(source.GetByRole(AriaRole.Button, new() { Name = "Check source", Exact = true })).ToBeEnabledAsync();
        await Expect(source.GetByRole(AriaRole.Button, new() { Name = "Create source proposal", Exact = true })).ToHaveCountAsync(0);
    }

    [LiveInspectorFact]
    public async Task TestWorkbenchKeyboard_RestoresToolbarFocusAndDoesNotStartRun()
    {
        await _page.GotoAsync(BaseUrl);

        var button = _page.Locator("#df-toggle-workbench");
        await button.FocusAsync();
        await button.ClickAsync();
        await Expect(_page.Locator("#df-workbench")).ToBeVisibleAsync();
        await button.FocusAsync();
        await _page.EvaluateAsync("""
            () => {
                const target = document.getElementById('df-toggle-workbench');
                target.dispatchEvent(new KeyboardEvent('keydown', { key: '4', code: 'Digit4', ctrlKey: true, altKey: true, bubbles: true, cancelable: true }));
            }
            """);
        Assert.DoesNotContain("df-active", await _page.Locator("#df-workbench-stage-run").GetAttributeAsync("class"));
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("Save a reviewed test with an expected result to unlock Run.");
        await _page.Keyboard.PressAsync("Escape");

        var focusedId = await _page.EvaluateAsync<string>("() => document.activeElement && document.activeElement.id");
        Assert.Equal("df-toggle-workbench", focusedId);
    }

    [LiveInspectorFact]
    public async Task TestWorkbenchGoal_IsRequiredAndRecordingRecoversFocus()
    {
        await _page.GotoAsync(BaseUrl);
        await _page.Locator("#df-toggle-workbench").ClickAsync();

        var goal = _page.Locator("#df-goal-input");
        await Expect(goal).ToBeVisibleAsync();
        await _page.WaitForTimeoutAsync(50);
        Assert.True(await goal.EvaluateAsync<bool>("element => element === document.activeElement"));
        Assert.Equal("true", await goal.GetAttributeAsync("required"));
        Assert.Equal("true", await goal.GetAttributeAsync("aria-invalid"));
        Assert.Contains("df-goal-help", await goal.GetAttributeAsync("aria-describedby"));
        await Expect(_page.Locator(".df-test-detail-group")).ToHaveCountAsync(4);
        foreach (var group in await _page.Locator(".df-test-detail-group").AllAsync())
            Assert.False(await group.EvaluateAsync<bool>("details => details.open"));
        await Expect(_page.Locator("#df-test-name-input")).ToBeHiddenAsync();
        await Expect(_page.Locator(".df-plan-more")).ToHaveCountAsync(0);
        await Expect(_page.Locator(".df-advanced-quick-record")).ToHaveCountAsync(0);
        Assert.DoesNotContain(
            "does not support save test bundles",
            await _page.Locator("#df-workbench-panel-plan").TextContentAsync(),
            StringComparison.OrdinalIgnoreCase);
        await Expect(_page.Locator("#df-workbench-panel-plan")).ToContainTextAsync("Create your first test");
        await Expect(_page.Locator("#df-workbench-panel-plan").GetByRole(
            AriaRole.Button,
            new() { Name = "Create this test with your agent", Exact = true })).ToBeVisibleAsync();

        await Expect(_page.Locator("#df-workbench-stage-record")).ToBeDisabledAsync();
        await Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Record steps", Exact = true })).ToBeDisabledAsync();
    }

    [LiveInspectorFact]
    public async Task TestWorkbenchGoal_PreservesFocusAndExpandersDuringBackgroundRefresh()
    {
        await _page.GotoAsync(BaseUrl);
        await _page.Locator("#df-toggle-workbench").ClickAsync();

        var goal = _page.Locator("#df-goal-input");
        await goal.FillAsync("Keep authoring controls stable during broker refresh.");
        await goal.FocusAsync();
        await _page.WaitForTimeoutAsync(3_500);
        Assert.True(await goal.EvaluateAsync<bool>("element => element === document.activeElement"));

        var agentAction = _page.Locator("#df-workbench-panel-plan .df-agent-action");
        await agentAction.FocusAsync();
        await _page.WaitForTimeoutAsync(3_500);
        Assert.True(await agentAction.EvaluateAsync<bool>("element => element === document.activeElement"));
    }

    [LiveInspectorFact]
    public async Task TestWorkbenchGoal_FocusesRequiredFieldOnFirstOpenAndRestoresToggleOnClose()
    {
        await _page.GotoAsync(BaseUrl);
        await _page.Locator("#df-toggle-workbench").ClickAsync();

        var goal = _page.Locator("#df-goal-input");
        await Expect(goal).ToBeVisibleAsync();
        await _page.WaitForTimeoutAsync(50);
        Assert.True(await goal.EvaluateAsync<bool>("element => element === document.activeElement"));

        await CloseWorkbenchAsync();
        await _page.WaitForTimeoutAsync(50);
        Assert.Equal("df-toggle-workbench", await _page.EvaluateAsync<string>("() => document.activeElement && document.activeElement.id"));
    }

    [LiveInspectorFact]
    public async Task TestWorkbenchProgressiveDisclosure_ShowsOnlyCurrentActions()
    {
        await _page.GotoAsync(BaseUrl);
        await _page.Locator("#df-toggle-workbench").ClickAsync();

        await Expect(_page.Locator(".df-test-detail-group")).ToHaveCountAsync(4);
        foreach (var group in await _page.Locator(".df-test-detail-group").AllAsync())
            Assert.False(await group.EvaluateAsync<bool>("details => details.open"));

        await Expect(_page.Locator("#df-workbench-stage-record")).ToBeDisabledAsync();
        await Expect(_page.Locator("#df-workbench-stage-review")).ToBeDisabledAsync();
        await Expect(_page.Locator("#df-workbench-stage-run")).ToBeDisabledAsync();
        await Expect(_page.Locator("#df-workbench-tab-improve")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-workbench-stage-results")).ToBeEnabledAsync();
        await _page.Locator("#df-workbench-stage-results").ClickAsync();
        await Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Go to Goal", Exact = true })).ToBeVisibleAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Go to Goal", Exact = true }).ClickAsync();
        await _page.Locator("#df-workbench-stage-goal").FocusAsync();
        await _page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("df-workbench-stage-results",
            await _page.EvaluateAsync<string>("() => document.activeElement && document.activeElement.id"));

        await _page.Locator("#df-workbench-stage-goal").ClickAsync();
        await _page.Locator("#df-goal-input").FillAsync("Show only actions that are currently usable.");
        await Expect(_page.Locator("#df-workbench-stage-record")).ToBeEnabledAsync();
        await Expect(_page.Locator("#df-workbench-stage-review")).ToBeDisabledAsync();
        await Expect(_page.Locator("#df-workbench-stage-run")).ToBeDisabledAsync();
        await _page.Locator("#df-workbench-stage-record").ClickAsync();
        await Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Start recording", Exact = true })).ToBeVisibleAsync();
        await Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Save test", Exact = true })).ToHaveCountAsync(0);

        await Expect(_page.Locator("#df-workbench-stage-review")).ToBeDisabledAsync();
        await Expect(_page.Locator("#df-workbench-stage-run")).ToBeDisabledAsync();

        await _page.Locator("#df-workbench-stage-results").ClickAsync();
        await Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Go to Steps", Exact = true })).ToBeVisibleAsync();
        await Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Go to Run", Exact = true })).ToHaveCountAsync(0);
        var import = _page.Locator("#df-workbench-panel-trace .df-trace-import");
        await Expect(import).ToBeVisibleAsync();
        Assert.False(await import.EvaluateAsync<bool>("details => details.open"));
        await Expect(_page.Locator("#df-workbench-panel-trace .df-trace-details")).ToHaveCountAsync(0);
    }

    [LiveInspectorFact]
    public async Task TestWorkbenchReview_AmbiguousSelectorGuidesResolutionThenCheckDiffSave()
    {
        string? commitRequest = null;
        const string firstStableKey = "sha256:1111111111111111111111111111111111111111111111111111111111111111";
        const string secondStableKey = "sha256:2222222222222222222222222222222222222222222222222222222222222222";
        const string markdown = """
            # Repeated item

            ```json maui-test
            {
              "schema": 2,
              "name": "repeated-item",
              "steps": [
                {
                  "seq": 1,
                  "action": "tap",
                  "target": {
                    "automationId": "TodoCheckBox",
                    "matchCount": 2,
                    "quality": "ambiguous"
                  }
                },
                {
                  "seq": 2,
                  "action": "fill",
                  "target": {
                    "automationId": "NewTodoEntry",
                    "matchCount": 1,
                    "quality": "durable"
                  },
                  "args": {
                    "text": "new item"
                  },
                  "asserts": [
                    {
                      "kind": "exists",
                      "selector": {
                        "automationId": "NewTodoEntry",
                        "matchCount": 1,
                        "quality": "durable"
                      },
                      "verify": true
                    }
                  ]
                }
              ]
            }
            ```
            """;

        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"youAreWriter":true,"heldByOther":false}""",
        }));
        await _page.RouteAsync("**/api/flows/record/start", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"recordingId":"review-resolution","name":"repeated-item"}""",
        }));
        await _page.RouteAsync("**/api/flows/record/status", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"recording":true,"recordingId":"review-resolution","name":"repeated-item","steps":2}""",
        }));
        await _page.RouteAsync("**/api/flows/record/stop", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new { ok = true, name = "repeated-item", steps = 2, markdown }),
        }));
        await _page.RouteAsync("**/api/flows/validate", async route =>
        {
            var hasScopedItem = route.Request.PostData?.Contains(secondStableKey, StringComparison.Ordinal) == true;
            await route.FulfillAsync(new()
            {
                Status = hasScopedItem ? 200 : 400,
                ContentType = "application/json",
                Body = hasScopedItem
                    ? """{"ok":true,"errors":[],"warnings":[],"issues":[]}"""
                    : """
                        {
                          "ok": false,
                          "error": "Validation found issues.",
                          "errors": [
                            "step 1: selector must resolve exactly one element; it currently reports 2 matches.",
                            "step 1: ambiguous selectors cannot be saved."
                          ],
                          "warnings": [],
                          "issues": [
                            {
                              "severity": "error",
                              "blocking": true,
                              "code": "selector-match-count",
                              "stepSequence": 1,
                              "message": "step 1: selector must resolve exactly one element; it currently reports 2 matches.",
                              "remediation": "resolve-selector"
                            }
                          ]
                        }
                        """
            });
        });
        await _page.RouteAsync("**/api/flows/selector/verify", async route =>
        {
            var scoped = route.Request.PostData?.Contains(secondStableKey, StringComparison.Ordinal) == true;
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = scoped
                    ? $$"""
                        {
                          "ok": true,
                          "matchCount": 1,
                          "quality": "stable-item-key",
                          "element": {
                            "id": "todo-2-checkbox",
                            "type": "CheckBox",
                            "automationId": "TodoCheckBox",
                            "stableItemKey": "{{secondStableKey}}",
                            "collectionScope": "TodoList"
                          }
                        }
                        """
                    : $$"""
                        {
                          "ok": false,
                          "matchCount": 2,
                          "totalCount": 2,
                          "truncated": false,
                          "error": "AutomationId selector is ambiguous.",
                          "matches": [
                            {
                              "id": "todo-1-checkbox",
                              "type": "CheckBox",
                              "automationId": "TodoCheckBox",
                              "stableItemKey": "{{firstStableKey}}",
                              "collectionScope": "TodoList",
                              "isVisible": true,
                              "isEnabled": true
                            },
                            {
                              "id": "todo-2-checkbox",
                              "type": "CheckBox",
                              "automationId": "TodoCheckBox",
                              "stableItemKey": "{{secondStableKey}}",
                              "collectionScope": "TodoList",
                              "isVisible": true,
                              "isEnabled": true
                            }
                          ]
                        }
                        """
            });
        });
        await _page.RouteAsync("**/api/flows/diff", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"errors":[],"warnings":[],"issues":[],"diff":"--- saved\n+++ draft\n+ scoped item selector"}""",
        }));
        await _page.RouteAsync("**/api/flows/commit", async route =>
        {
            commitRequest = route.Request.PostData;
            using var request = JsonDocument.Parse(commitRequest!);
            var planJson = request.RootElement.GetProperty("planJson").GetString()!;
            using var plan = JsonDocument.Parse(planJson);
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    ok = true,
                    supported = true,
                    errors = Array.Empty<string>(),
                    warnings = Array.Empty<string>(),
                    issues = Array.Empty<object>(),
                    flow = new
                    {
                        name = "repeated-item.md",
                        markdown = request.RootElement.GetProperty("markdown").GetString(),
                        document = (object?)null,
                        digest = new string('a', 64),
                    },
                    plan = new
                    {
                        json = planJson,
                        document = plan.RootElement.Clone(),
                        digest = new string('b', 64),
                        revision = 1,
                    },
                }),
            });
        });

        await _page.GotoAsync(BaseUrl);
        await StartManagedRecordingAsync("Tap one repeated todo, then add a new item.");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Stop recording", Exact = true }).ClickAsync();

        var review = _page.Locator("#df-workbench-panel-review");
        await Expect(review.GetByRole(AriaRole.Button, new() { Name = "Check test", Exact = true })).ToBeVisibleAsync();
        await review.GetByRole(AriaRole.Button, new() { Name = "Check test", Exact = true }).ClickAsync();
        await Expect(review).ToContainTextAsync("Step 1 needs attention");
        await Expect(review.GetByRole(AriaRole.Button, new() { Name = "Resolve step 1", Exact = true })).ToBeVisibleAsync();
        await Expect(review.GetByRole(AriaRole.Button, new() { Name = "Save test", Exact = true })).ToHaveCountAsync(0);

        await review.GetByRole(AriaRole.Button, new() { Name = "Resolve step 1", Exact = true }).ClickAsync();
        await Expect(review).ToContainTextAsync("Step 1 needs a stable control");
        await review.GetByRole(AriaRole.Button, new() { Name = "Check matching controls", Exact = true }).ClickAsync();
        await Expect(review).ToContainTextAsync("2 controls match");
        var useButtons = review.GetByRole(AriaRole.Button, new() { Name = "Use this control", Exact = true });
        await Expect(useButtons).ToHaveCountAsync(2);
        await useButtons.Nth(1).ClickAsync();

        await Expect(review.GetByRole(AriaRole.Button, new() { Name = "Check test", Exact = true })).ToBeVisibleAsync();
        await review.GetByRole(AriaRole.Button, new() { Name = "Check test", Exact = true }).ClickAsync();
        await Expect(review).Not.ToContainTextAsync("Saving test…", new() { Timeout = 15_000 });
        var reviewChanges = review.GetByRole(AriaRole.Button, new() { Name = "Review changes", Exact = true });
        Assert.True(
            await reviewChanges.IsVisibleAsync(),
            $"{await review.InnerTextAsync()}\nSTATUS: {await _page.Locator("#df-status").InnerTextAsync()}");
        await review.GetByRole(AriaRole.Button, new() { Name = "Review changes", Exact = true }).ClickAsync();
        await Expect(review).ToContainTextAsync("Changes to save");
        var save = review.GetByRole(AriaRole.Button, new() { Name = "Save test", Exact = true });
        await Expect(save).ToBeVisibleAsync();
        await save.ClickAsync();
        await Expect(review.GetByRole(AriaRole.Button, new() { Name = "Continue to Run", Exact = true })).ToBeVisibleAsync();
        Assert.NotNull(commitRequest);
        using var committed = JsonDocument.Parse(commitRequest);
        Assert.Equal(JsonValueKind.Null, committed.RootElement.GetProperty("expectedPlanRevision").ValueKind);
        Assert.Equal(JsonValueKind.Null, committed.RootElement.GetProperty("expectedPlanDigest").ValueKind);
        Assert.Equal(JsonValueKind.Null, committed.RootElement.GetProperty("expectedFlowDigest").ValueKind);
    }

    [LiveInspectorFact]
    public async Task TestWorkbenchRecording_RepeatedItemUsesScopedStableSelector()
    {
        await _page.GotoAsync(BaseUrl);
        var takeControl = _page.Locator("#df-take-control");
        if (await takeControl.IsVisibleAsync())
        {
            await takeControl.ClickAsync();
            await _page.GetByRole(AriaRole.Dialog)
                .GetByRole(AriaRole.Button, new() { Name = "Take control", Exact = true })
                .ClickAsync();
        }
        await StartManagedRecordingAsync("Toggle the first seeded todo and verify the target remains identifiable.");

        await _page.Locator("[data-automationId='TodoCheckBox']").First.ClickAsync(new() { Force = true });
        await _page.GetByRole(AriaRole.Button, new() { Name = "Stop recording", Exact = true }).ClickAsync();

        var review = _page.Locator("#df-workbench-panel-review");
        await Expect(review).ToBeVisibleAsync();
        await Expect(review).ToContainTextAsync("stable item in TodoList");
        await Expect(review).Not.ToContainTextAsync("needs a stable control");

        var expectedResult = review.Locator(".df-expected-result-editor");
        if (!await expectedResult.EvaluateAsync<bool>("details => details.open"))
            await expectedResult.Locator("summary").ClickAsync();
        await expectedResult.Locator(".df-authoring-section")
            .GetByRole(AriaRole.Button, new() { Name = "Add expected result", Exact = true })
            .ClickAsync();

        await Expect(review.GetByRole(AriaRole.Button, new() { Name = "Check test", Exact = true })).ToBeVisibleAsync();
        await review.GetByRole(AriaRole.Button, new() { Name = "Check test", Exact = true }).ClickAsync();
        await Expect(review).Not.ToContainTextAsync("Saving test…", new() { Timeout = 15_000 });
        var reviewChanges = review.GetByRole(AriaRole.Button, new() { Name = "Review changes", Exact = true });
        Assert.True(
            await reviewChanges.IsVisibleAsync(),
            $"{await review.InnerTextAsync()}\nSTATUS: {await _page.Locator("#df-status").InnerTextAsync()}");
    }

    [LiveInspectorFact]
    public async Task TestWorkbenchNarrowLayout_ScrollsTabsAndExplainsTracePickerFallback()
    {
        await _page.SetViewportSizeAsync(320, 700);
        await _page.GotoAsync(BaseUrl);
        await _page.Locator("#df-toggle-workbench").ClickAsync();

        var workbench = _page.Locator("#df-workbench");
        await Expect(workbench).ToBeVisibleAsync();
        Assert.Equal("true", await workbench.GetAttributeAsync("aria-modal"));
        var tabList = _page.Locator("#df-workbench-tabs .df-workbench-stage-list");
        var scrollWidth = await tabList.EvaluateAsync<double>("element => element.scrollWidth");
        var clientWidth = await tabList.EvaluateAsync<double>("element => element.clientWidth");
        Assert.True(scrollWidth >= clientWidth);
        await _page.Locator("#df-workbench-stage-results").ClickAsync();
        var tabListBox = await tabList.BoundingBoxAsync();
        var resultsTabBox = await _page.Locator("#df-workbench-stage-results").BoundingBoxAsync();
        Assert.NotNull(tabListBox);
        Assert.NotNull(resultsTabBox);
        Assert.True(
            resultsTabBox.X >= tabListBox.X - 1 &&
            resultsTabBox.X + resultsTabBox.Width <= tabListBox.X + tabListBox.Width + 1,
            "The focused Results tab should scroll fully into view.");
        await _page.Locator("#df-workbench-panel-trace .df-trace-import summary").ClickAsync();
        await Expect(_page.Locator("#df-workbench-panel-trace")).ToContainTextAsync("This host cannot pick trace artifacts. The Inspector will use its own file picker instead.");
        await Expect(_page.Locator("#df-workbench-panel-trace")).ToContainTextAsync("Choose result file");
    }

    [LiveInspectorFact]
    public async Task TestWorkbenchCanonicalPlanBinding_DoesNotReportFalseStale()
    {
        const string name = "recording-2026-08-04T07-33-09.md";
        await _page.GotoAsync(BaseUrl);
        await OpenSavedTestAsync(name);
        await Expect(_page.Locator("#df-workbench-strip")).ToContainTextAsync(name);
        var planLoad = await _page.EvaluateAsync<string>(
            """
            async name => {
              const token = document.querySelector('meta[name="devflow-inspector-token"]')?.content || '';
              const response = await fetch('api/plans/load', {
                method: 'POST',
                headers: {
                  'Content-Type': 'application/json',
                  'X-DevFlow-Inspector-Token': token
                },
                body: JSON.stringify({ name })
              });
              return await response.text();
            }
            """,
            name);
        using var planDocument = JsonDocument.Parse(planLoad);
        var flowDigest = planDocument.RootElement.GetProperty("flow").GetProperty("digest").GetString();
        var planFlowDigest = planDocument.RootElement
            .GetProperty("plan")
            .GetProperty("document")
            .GetProperty("flow")
            .GetProperty("digest")
            .GetString();
        Assert.Equal(flowDigest, planFlowDigest);

        await Expect(_page.Locator("#df-workbench-stage-review")).ToBeEnabledAsync();
        await Expect(_page.Locator("#df-workbench-stage-run")).ToBeEnabledAsync();
        await _page.Locator("#df-workbench-stage-run").ClickAsync();
        var run = _page.Locator("#df-workbench-panel-run");
        await Expect(run).ToBeVisibleAsync();
        await Expect(run).Not.ToContainTextAsync("The saved plan no longer matches the recorded steps.");
        var checkRun = run.GetByRole(AriaRole.Button, new() { Name = "Check run", Exact = true });
        if (await checkRun.IsVisibleAsync())
            await checkRun.ClickAsync();
        await Expect(run.GetByRole(AriaRole.Button, new() { Name = "Review and start", Exact = true })).ToBeVisibleAsync();
        await Expect(run).ToContainTextAsync("Verification notes");
    }

    [LiveInspectorFact]
    public async Task TestWorkbenchFailedResult_CopiesExactRestrictedAgentHandoff()
    {
        const string name = "recording-2026-08-04T07-33-09.md";
        await CaptureClipboardWritesAsync();
        string? handoffRequest = null;
        await _page.RouteAsync("**/api/workbench/run/journal*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body =
                """
                {
                  "ok": true,
                  "restored": false,
                  "pending": false,
                  "run": {
                    "runId": "run-copy-handoff",
                    "state": "failed",
                    "terminal": true,
                    "totalSteps": 1,
                    "completedSteps": 1,
                    "firstDivergence": 1,
                    "report": {
                      "runId": "run-copy-handoff",
                      "outcome": { "status": "failed", "terminal": true },
                      "failure": {
                        "class": "drive-failed",
                        "code": "drive-failed",
                        "category": "action",
                        "phase": "dispatch",
                        "stepId": "1"
                      },
                      "steps": [
                        {
                          "stepId": "1",
                          "sequence": 1,
                          "action": "tap",
                          "failureClass": "drive-failed"
                        }
                      ]
                    }
                  }
                }
                """,
        }));
        await _page.RouteAsync("**/api/workbench/agent-handoff", async route =>
        {
            handoffRequest = route.Request.PostData;
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body =
                    """
                    {
                      "ok": true,
                      "context": {
                        "testName": "recording-2026-08-04T07-33-09.md",
                        "runId": "run-copy-handoff",
                        "failureRequest": {
                          "runId": "run-copy-handoff",
                          "runCapabilityToken": "run-read-capability",
                          "envelope": {
                            "requestId": "handoff-failure",
                            "correlation": {
                              "authoringSessionId": "author-handoff",
                              "runId": "run-copy-handoff"
                            },
                            "readCapabilityId": "read-handoff"
                          }
                        },
                        "improvementsEnvelope": {
                          "requestId": "handoff-improvements",
                          "correlation": { "authoringSessionId": "author-handoff" },
                          "readCapabilityId": "read-handoff"
                        },
                        "patchEnvelope": {
                          "requestId": "handoff-patch",
                          "correlation": { "authoringSessionId": "author-handoff" },
                          "readCapabilityId": "read-handoff"
                        }
                      }
                    }
                    """,
            });
        });

        await _page.GotoAsync(BaseUrl);
        await OpenSavedTestAsync(name);
        await _page.Locator("#df-workbench-stage-results").ClickAsync();
        var agentAction = _page.Locator("#df-workbench-panel-trace .df-agent-action");
        await Expect(agentAction).ToHaveTextAsync("Diagnose this failure with your agent");
        await agentAction.ClickAsync();

        await Expect(_page.Locator("#df-workbench-status")).ToContainTextAsync(
            "Copied the agent request");
        var copied = await _page.EvaluateAsync<string>("() => window.__copiedDevFlowData");
        Assert.Contains("Call maui_test_failure exactly once", copied, StringComparison.Ordinal);
        Assert.Contains("run-copy-handoff", copied, StringComparison.Ordinal);
        Assert.DoesNotContain("latest failed local", copied, StringComparison.OrdinalIgnoreCase);

        Assert.False(string.IsNullOrWhiteSpace(handoffRequest));
        using var request = JsonDocument.Parse(handoffRequest);
        Assert.Equal("run-copy-handoff", request.RootElement.GetProperty("runId").GetString());
        Assert.True(request.RootElement.TryGetProperty("markdown", out var markdown));
        Assert.Contains("json maui-test", markdown.GetString(), StringComparison.Ordinal);
    }

    [LiveInspectorFact]
    public async Task ClickSendsTapToAgent()
    {
        await _page.GotoAsync(BaseUrl);

        // Get the viewport bounding box
        var viewport = _page.Locator("#app-viewport");
        var box = await viewport.BoundingBoxAsync();
        Assert.NotNull(box);

        // Click in the middle of the viewport
        await viewport.ClickAsync(new() { Position = new() { X = (float)box.Width / 2, Y = (float)box.Height / 2 } });

        // Wait for AJAX refresh (devflow.js refreshes after tap via /api/state)
        await _page.WaitForTimeoutAsync(1000);

        // The screenshot src should identify the immutable frame captured after the action.
        var screenshotAfter = await _page.Locator("#screenshot").GetAttributeAsync("src");
        Assert.Contains("?frame=", screenshotAfter);
    }

    [LiveInspectorFact]
    public async Task ClickOnElementSendsTapAtCorrectCoordinates()
    {
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));

        // Set up request interception to capture tap coordinates
        var tapRequests = new List<string>();
        await _page.RouteAsync("**/api/tap", async route =>
        {
            var body = route.Request.PostData;
            tapRequests.Add(body ?? "");
            string? elementId = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var request = JsonDocument.Parse(body);
                if (request.RootElement.TryGetProperty("elementId", out var id) &&
                    id.ValueKind == JsonValueKind.String)
                {
                    elementId = id.GetString();
                }
            }
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new { ok = true, elementId }),
            });
        });
        await _page.GotoAsync(BaseUrl);

        // Find an actionable element with positive width and height in style (not -1 or 0).
        var allPositioned = _page.Locator(
            ".devflow-element[data-interactable='true'][data-traits*='interactive'][style*='width:']");
        var count = await allPositioned.CountAsync();
        ILocator? target = null;

        for (int i = 0; i < count; i++)
        {
            var style = await allPositioned.Nth(i).GetAttributeAsync("style") ?? "";
            // Parse width value — skip elements with -1 or 0 width
            var widthMatch = System.Text.RegularExpressions.Regex.Match(style, @"width:(\d+)px");
            var heightMatch = System.Text.RegularExpressions.Regex.Match(style, @"height:(\d+)px");
            if (widthMatch.Success && heightMatch.Success)
            {
                var w = int.Parse(widthMatch.Groups[1].Value);
                var h = int.Parse(heightMatch.Groups[1].Value);
                if (w > 10 && h > 10)
                {
                    target = allPositioned.Nth(i);
                    break;
                }
            }
        }

        if (target == null)
        {
            // No suitable element found — skip
            return;
        }

        // Click with force (the div is transparent overlay, not visually rendered)
        await target.ClickAsync(new() { Force = true, Timeout = 5000 });
        await _page.WaitForTimeoutAsync(300);

        // Verify a tap request was sent with valid coordinates
        Assert.NotEmpty(tapRequests);
        var json = System.Text.Json.JsonDocument.Parse(tapRequests[0]);
        var x = json.RootElement.GetProperty("x").GetDouble();
        var y = json.RootElement.GetProperty("y").GetDouble();
        Assert.True(x >= 0, $"Tap x should be non-negative, got {x}");
        Assert.True(y >= 0, $"Tap y should be non-negative, got {y}");
        Assert.True(
            json.RootElement.TryGetProperty("elementId", out var elementId),
            $"Tap request did not include the highlighted element id: {tapRequests[0]}");
        Assert.Equal(await target.GetAttributeAsync("data-id"), elementId.GetString());
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("Tap sent to");
    }

    [LiveInspectorFact]
    public async Task InspectClickUsesAuthoritativeHitTestAndOffersCandidates()
    {
        await _page.GotoAsync(BaseUrl);
        var header = _page.Locator("[data-automationId='HeaderLabel']");
        var modalButton = _page.Locator("[data-automationId='ShowModalButton']");
        var headerId = await header.GetAttributeAsync("data-id");
        var modalId = await modalButton.GetAttributeAsync("data-id");
        Assert.False(string.IsNullOrEmpty(headerId));
        Assert.False(string.IsNullOrEmpty(modalId));

        await _page.RouteAsync("**/api/hitTest", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = System.Text.Json.JsonSerializer.Serialize(new
            {
                ok = true,
                elementId = headerId,
                candidates = new[]
                {
                    new { id = headerId, type = "Label", automationId = "HeaderLabel", text = "My Todos" },
                    new { id = modalId, type = "Button", automationId = "ShowModalButton", text = "Show Modal" },
                }
            })
        }));

        await _page.Locator("#df-mode-inspect").ClickAsync();
        await modalButton.ClickAsync(new() { Force = true });

        await Expect(_page.Locator($".df-tree-node[data-tree-id='{headerId}']"))
            .ToHaveAttributeAsync("aria-selected", "true");
        var candidates = _page.Locator("#df-hit-candidates button");
        await Expect(candidates).ToHaveCountAsync(2);
        await Expect(candidates.First).ToContainTextAsync("HeaderLabel");

        await candidates.Nth(1).ClickAsync();
        await Expect(_page.Locator($".df-tree-node[data-tree-id='{modalId}']"))
            .ToHaveAttributeAsync("aria-selected", "true");
        await Expect(_page.Locator("#df-hit-candidates")).ToBeHiddenAsync();
    }

    [LiveInspectorFact]
    public async Task ScreenshotEndpointReturnsPng()
    {
        var response = await _page.APIRequest.GetAsync(ResolveUrl("screenshot.png"));
        Assert.True(response.Ok);
        var body = await response.BodyAsync();

        // PNG magic bytes
        Assert.Equal(0x89, body[0]);
        Assert.Equal(0x50, body[1]); // P
        Assert.Equal(0x4E, body[2]); // N
        Assert.Equal(0x47, body[3]); // G
    }

    [LiveInspectorFact]
    public async Task StateEndpointReturnsJsonWithElements()
    {
        var response = await _page.APIRequest.GetAsync(ResolveUrl("api/state"));
        Assert.True(response.Ok);
        var text = await response.TextAsync();
        var json = System.Text.Json.JsonDocument.Parse(text);

        Assert.True(json.RootElement.TryGetProperty("screenshotUrl", out var url));
        Assert.Contains("screenshot.png", url.GetString());

        Assert.True(json.RootElement.TryGetProperty("elements", out var elements));
        Assert.Contains("devflow-element", elements.GetString());

        Assert.True(json.RootElement.TryGetProperty("viewportWidth", out var vw));
        Assert.True(vw.GetDouble() > 0);

        Assert.True(json.RootElement.TryGetProperty("viewportHeight", out var vh));
        Assert.True(vh.GetDouble() > 0);
    }

    [LiveInspectorFact]
    public async Task AjaxRefreshUpdatesElementsWithoutReload()
    {
        await _page.GotoAsync(BaseUrl);

        // Count initial elements
        var initialCount = await _page.Locator(".devflow-element").CountAsync();
        Assert.True(initialCount > 0);

        // Trigger a refresh via JS (simulating what the polling does)
        await _page.EvaluateAsync(@"async () => {
            const basePath = location.pathname.replace(/\/$/, '');
            const resp = await fetch(basePath + '/api/state');
            return resp.ok;
        }");

        // Elements should still exist (page was not reloaded)
        var afterCount = await _page.Locator(".devflow-element").CountAsync();
        Assert.True(afterCount > 0);
    }

    [LiveInspectorFact]
    public async Task StateFailureDisablesActionsAndRecoversAutomatically()
    {
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));

        await _page.GotoAsync(BaseUrl);
        await OpenWorkbenchAsync();
        await _page.Locator("#df-goal-input").FillAsync("Verify recording follows connection state.");
        var record = _page.GetByRole(AriaRole.Button, new() { Name = "Record steps", Exact = true });
        await Expect(record).ToBeEnabledAsync();
        try
        {
            await _context.SetOfflineAsync(true);
            await Expect(_page.Locator("#df-status")).ToContainTextAsync("App disconnected", new() { Timeout = 10_000 });
            await Expect(_page.Locator("#df-presence")).ToContainTextAsync("Disconnected");
            await Expect(_page.Locator("#df-disconnected-overlay")).ToBeVisibleAsync();
            await Expect(_page.Locator("#df-disconnected-overlay")).ToContainTextAsync("Showing the last captured frame");
            Assert.True(await _page.Locator("body").EvaluateAsync<bool>(
                "body => body.classList.contains('df-disconnected')"));
            await Expect(record).ToBeDisabledAsync();
            var entry = _page.Locator("[data-automationId='NewTodoEntry']");
            await Expect(entry).ToBeAttachedAsync();
            var bounds = await entry.BoundingBoxAsync();
            Assert.NotNull(bounds);
            await _page.Mouse.ClickAsync(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            await Expect(_page.Locator("#app-viewport > input, #app-viewport > textarea")).ToHaveCountAsync(0);

            await _context.SetOfflineAsync(false);
            await Expect(record).ToBeEnabledAsync(new() { Timeout = 15_000 });
            await Expect(_page.Locator("#df-status")).ToBeEmptyAsync();
            await Expect(_page.Locator("#df-presence")).ToContainTextAsync("Driving");
            await Expect(_page.Locator("#df-disconnected-overlay")).ToBeHiddenAsync();
            Assert.False(await _page.Locator("body").EvaluateAsync<bool>(
                "body => body.classList.contains('df-disconnected')"));
        }
        finally
        {
            await _context.SetOfflineAsync(false);
        }
    }

    [LiveInspectorFact]
    public async Task ReadOnlyInteractionPromptsForControlWithoutSendingTap()
    {
        var tapRequests = 0;
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":false,\"heldByOther\":true}",
        }));
        await _page.RouteAsync("**/api/tap", route =>
        {
            tapRequests++;
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = "{\"ok\":true}",
            });
        });

        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator("#df-presence")).ToContainTextAsync("Read-only");
        var target = _page.Locator("[data-automationId='ShowModalButton']");
        await Expect(target).ToBeAttachedAsync();

        await target.ClickAsync(new() { Force = true });

        await Expect(_page.Locator("#df-status")).ToContainTextAsync("Take control to interact");
        Assert.Equal(0, tapRequests);
    }

    [LiveInspectorFact]
    public async Task EmptyRecordingStopClosesTimeline()
    {
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));
        await _page.RouteAsync("**/api/flows/record/start", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"ok\":true,\"recordingId\":\"empty-recording\",\"name\":\"empty\"}",
        }));
        await _page.RouteAsync("**/api/flows/record/status", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"ok\":true,\"recording\":true,\"recordingId\":\"empty-recording\",\"name\":\"empty\",\"steps\":0}",
        }));
        await _page.RouteAsync("**/api/flows/record/stop", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"ok\":true,\"empty\":true,\"steps\":0}",
        }));

        await _page.GotoAsync(BaseUrl);
        await StartManagedRecordingAsync("Stop an empty recording without creating a replayable test.");
        await Expect(_page.Locator("#df-timeline")).ToBeVisibleAsync();

        await _page.GetByRole(AriaRole.Button, new() { Name = "Stop recording", Exact = true }).ClickAsync();

        await Expect(_page.Locator("#df-status")).ToContainTextAsync("no replayable steps");
        await Expect(_page.Locator("#df-timeline")).ToBeHiddenAsync();
        Assert.False(await _page.Locator("body").EvaluateAsync<bool>(
            "body => body.classList.contains('df-timeline-open')"));
    }

    [LiveInspectorFact]
    public async Task RecordingBecomesPassiveOnLeaseLossAndResumesAfterTakeover()
    {
        var recordingStarted = 0;
        await _page.RouteAsync("**/api/control", route =>
        {
            using var body = System.Text.Json.JsonDocument.Parse(route.Request.PostData ?? "{}");
            var force = body.RootElement.TryGetProperty("force", out var forceValue) && forceValue.GetBoolean();
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = force
                    ? "{\"youAreWriter\":true,\"heldByOther\":false}"
                    : "{\"youAreWriter\":true,\"heldByOther\":false}",
            });
        });
        await _page.RouteAsync("**/api/flows/record/start", route =>
        {
            Interlocked.Exchange(ref recordingStarted, 1);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = "{\"ok\":true,\"recordingId\":\"handoff-recording\",\"name\":\"handoff\"}",
            });
        });
        await _page.RouteAsync("**/api/flows/record/status", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = Volatile.Read(ref recordingStarted) == 1
                ? "{\"ok\":true,\"recording\":true,\"recordingId\":\"handoff-recording\",\"name\":\"handoff\",\"steps\":0}"
                : "{\"ok\":true,\"recording\":false,\"steps\":0}",
        }));
        await _page.RouteAsync("**/api/tap", route => route.FulfillAsync(new()
        {
            Status = 409,
            ContentType = "application/json",
            Body = "{\"ok\":false,\"reason\":\"writer\",\"label\":\"Canvas Inspector\"}",
        }));
        await _page.GotoAsync(BaseUrl);
        await StartManagedRecordingAsync("Observe recording across a writer lease handoff.");
        var stop = _page.GetByRole(AriaRole.Button, new() { Name = "Stop recording", Exact = true });

        await _page.Locator("[data-automationId='ShowModalButton']").ClickAsync(new() { Force = true });
        await Expect(_page.Locator("#df-presence")).ToContainTextAsync("Canvas Inspector");
        await Expect(stop).ToBeDisabledAsync();
        await Expect(_page.Locator("#df-timeline")).ToBeVisibleAsync();

        await _page.Locator("#df-take-control").ClickAsync();
        await _page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Take control" }).ClickAsync();
        await Expect(stop).ToBeEnabledAsync();
        await Expect(_page.Locator("#df-timeline")).ToBeVisibleAsync();
    }

    [LiveInspectorFact]
    public async Task ActiveRecordingCanBeDiscardedWithoutSaving()
    {
        var cancelRequests = 0;
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));
        await _page.RouteAsync("**/api/flows/record/status", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"ok\":true,\"recording\":false,\"steps\":0}",
        }));
        await _page.RouteAsync("**/api/flows/record/start", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"ok\":true,\"recordingId\":\"discard-recording\",\"name\":\"discard\"}",
        }));
        await _page.RouteAsync("**/api/flows/record/cancel", route =>
        {
            cancelRequests++;
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = "{\"ok\":true,\"recording\":false}",
            });
        });
        await _page.GotoAsync(BaseUrl);
        await StartManagedRecordingAsync("Discard an unfinished managed recording.");

        await _page.Locator("#df-record-cancel").ClickAsync();
        var dialog = _page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToContainTextAsync("Discard this recording");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Discard" }).ClickAsync();

        Assert.Equal(1, cancelRequests);
        await Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Stop recording", Exact = true })).ToHaveCountAsync(0);
        await Expect(_page.Locator("#df-timeline")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("Recording discarded");
    }

    [LiveInspectorFact]
    public async Task AdaptiveLayoutTracksHostViewport()
    {
        await _page.SetViewportSizeAsync(1440, 820);
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator(".devflow-element").First).ToBeAttachedAsync();

        Assert.Equal("wide", await _page.Locator("body").GetAttributeAsync("data-layout-width"));
        Assert.Equal("tall", await _page.Locator("body").GetAttributeAsync("data-layout-height"));
        await Expect(_page.Locator("#df-toolbar-secondary")).ToBeVisibleAsync();
        await _page.Locator(".df-tree-node").Last.ClickAsync();
        await Expect(_page.Locator("#df-props-pane")).ToBeVisibleAsync();
        await _page.Keyboard.PressAsync("Escape");
        await Expect(_page.Locator("#df-props-pane")).ToBeHiddenAsync();

        await _page.SetViewportSizeAsync(1200, 820);
        await _page.WaitForTimeoutAsync(100);
        Assert.Equal("compact", await _page.Locator("body").GetAttributeAsync("data-layout-width"));
        await _page.Locator(".df-tree-node").Last.ClickAsync();
        await Expect(_page.Locator("#df-props-pane")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-tree-pane")).ToBeVisibleAsync();
        Assert.False(await _page.Locator("body").EvaluateAsync<bool>("body => body.classList.contains('df-tree-hidden')"));
        var compactTree = await _page.Locator("#df-tree-pane").BoundingBoxAsync();
        var compactScrim = await _page.Locator("#df-pane-scrim").BoundingBoxAsync();
        Assert.NotNull(compactTree);
        Assert.NotNull(compactScrim);
        Assert.True(compactScrim.X >= compactTree.X + compactTree.Width - 1,
            $"Expected compact properties scrim to preserve the tree, tree right={compactTree.X + compactTree.Width}, scrim left={compactScrim.X}");
        await _page.Keyboard.PressAsync("Escape");

        await _page.SetViewportSizeAsync(700, 450);
        await _page.WaitForTimeoutAsync(100);
        Assert.Equal("narrow", await _page.Locator("body").GetAttributeAsync("data-layout-width"));
        Assert.Equal("short", await _page.Locator("body").GetAttributeAsync("data-layout-height"));
        Assert.True(await _page.Locator("body").EvaluateAsync<bool>("body => body.classList.contains('df-tree-hidden')"));
    }

    /// <summary>
    /// Width and height are independent axes. A wide but short window has ample horizontal room, so
    /// it must keep its tree docked and give up only vertical chrome.
    /// </summary>
    [LiveInspectorFact]
    public async Task ShortViewportKeepsTreeDockedWhenThereIsHorizontalRoom()
    {
        await _page.SetViewportSizeAsync(1920, 500);
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator(".devflow-element").First).ToBeAttachedAsync();

        Assert.Equal("wide", await _page.Locator("body").GetAttributeAsync("data-layout-width"));
        Assert.Equal("short", await _page.Locator("body").GetAttributeAsync("data-layout-height"));
        Assert.False(
            await _page.Locator("body").EvaluateAsync<bool>("body => body.classList.contains('df-tree-hidden')"),
            "A wide-but-short viewport has room for a docked tree and must not collapse it to a drawer.");
        await Expect(_page.Locator("#df-tree-pane")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Layout is a pure function of geometry. The same viewport must produce the same layout no
    /// matter which surface is embedding the Inspector.
    /// </summary>
    [LiveInspectorFact]
    public async Task LayoutIsIdenticalAcrossHostIdentitiesAtTheSameGeometry()
    {
        await _page.SetViewportSizeAsync(800, 820);
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator(".devflow-element").First).ToBeAttachedAsync();

        var baseline = await _page.Locator("body").GetAttributeAsync("data-layout-width");
        Assert.Equal("compact", baseline);

        foreach (var (hostId, surface) in new[]
        {
            ("vscode", "editor"),
            ("canvas", "side-panel"),
            ("browser", "browser"),
        })
        {
            await _page.EvaluateAsync(
                @"([hostId, surface]) => window.postMessage({
                    type: 'devflow:host',
                    v: 1,
                    bridgeId: null,
                    hostId,
                    hostLabel: hostId,
                    capabilities: [],
                    profile: { surface },
                }, '*')",
                new[] { hostId, surface });
            await _page.WaitForTimeoutAsync(60);

            Assert.Equal(baseline, await _page.Locator("body").GetAttributeAsync("data-layout-width"));
        }
    }

    [LiveInspectorFact]
    public async Task ToolbarActionsNeverOverlapAtResponsiveBreakpoints()
    {
        var writer = 1;
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = Volatile.Read(ref writer) == 1
                ? "{\"youAreWriter\":true,\"heldByOther\":false}"
                : "{\"youAreWriter\":false,\"heldByOther\":true,\"label\":\"Another Inspector\"}",
        }));
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator(".devflow-element").First).ToBeAttachedAsync();

        foreach (var width in new[] { 260, 300, 420, 1200, 1380, 1400, 1440 })
        {
            await _page.SetViewportSizeAsync(width, 820);
            await _page.WaitForTimeoutAsync(100);

            var toolbar = await _page.Locator("#df-toolbar").BoundingBoxAsync();
            var inspect = await _page.Locator("#df-mode-inspect").BoundingBoxAsync();
            var tree = await _page.Locator("#df-toggle-tree").BoundingBoxAsync();
            var presence = await _page.Locator("#df-presence").BoundingBoxAsync();
            Assert.NotNull(toolbar);
            Assert.NotNull(inspect);
            Assert.NotNull(tree);
            Assert.NotNull(presence);
            Assert.True(inspect.X + inspect.Width <= tree.X + 1,
                $"Inspect overlapped Tree at {width}px: inspect right={inspect.X + inspect.Width}, tree left={tree.X}");
            Assert.True(presence.X + presence.Width <= toolbar.X + toolbar.Width + 1,
                $"Presence escaped the toolbar at {width}px: presence right={presence.X + presence.Width}, toolbar right={toolbar.X + toolbar.Width}");
            Assert.True(await _page.Locator("#df-toolbar").EvaluateAsync<bool>(
                "toolbar => toolbar.scrollWidth <= toolbar.clientWidth + 1"),
                $"Toolbar overflowed horizontally at {width}px.");
        }

        await _page.SetViewportSizeAsync(260, 820);
        await Expect(_page.Locator(".df-presence-label")).ToHaveTextAsync("Driving");
        await _page.SetViewportSizeAsync(420, 820);
        await Expect(_page.Locator(".df-presence-label")).ToBeVisibleAsync();

        Volatile.Write(ref writer, 0);
        await _page.SetViewportSizeAsync(260, 820);
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator("#df-take-control")).ToBeVisibleAsync();
        await Expect(_page.Locator(".df-presence-label")).ToHaveTextAsync("Read-only · Another Inspector");
        Assert.True(await _page.Locator("#df-toolbar").EvaluateAsync<bool>(
            "toolbar => toolbar.scrollWidth <= toolbar.clientWidth + 1"),
            "Read-only toolbar overflowed horizontally at 260px.");
    }

    [LiveInspectorFact]
    public async Task ToolbarMovesOnlyActionsThatDoNotFitIntoAnchoredMoreMenu()
    {
        await _page.SetViewportSizeAsync(1800, 820);
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator(".devflow-element").First).ToBeAttachedAsync();
        var total = await _page.Locator("#df-toolbar-secondary > button, #df-toolbar-overflow > button").CountAsync();
        var wideInline = await _page.Locator("#df-toolbar-secondary > button").CountAsync();
        var focusedInline = _page.Locator("#df-toggle-dock");
        Assert.Equal("df-toolbar-secondary", await focusedInline.EvaluateAsync<string>(
            "button => button.parentElement?.id"));
        await focusedInline.FocusAsync();
        await _page.EvaluateAsync("() => window.dispatchEvent(new Event('resize'))");
        await _page.WaitForTimeoutAsync(100);
        Assert.Equal("df-toolbar-secondary", await focusedInline.EvaluateAsync<string>(
            "button => button.parentElement?.id"));
        Assert.True(await focusedInline.EvaluateAsync<bool>("button => button === document.activeElement"),
            "Toolbar reflow should preserve focus for an action that remains inline.");

        var compactInline = 0;
        var compactOverflow = 0;
        foreach (var width in new[] { 1400, 1300, 1200, 1100, 1000, 900, 800 })
        {
            await _page.SetViewportSizeAsync(width, 820);
            await _page.WaitForTimeoutAsync(150);
            compactInline = await _page.Locator("#df-toolbar-secondary > button").CountAsync();
            compactOverflow = await _page.Locator("#df-toolbar-overflow > button").CountAsync();
            if (compactInline > 0 && compactOverflow > 0 && compactInline < wideInline)
                break;
        }
        Assert.True(compactInline > 0, "Expected actions that still fit to remain inline.");
        Assert.True(compactOverflow > 0, "Expected only non-fitting actions in More.");
        Assert.Equal(total, compactInline + compactOverflow);
        Assert.True(compactInline < wideInline, "Expected fewer inline actions at the constrained width.");

        var more = _page.Locator("#df-more");
        await more.ClickAsync();
        var moreBounds = await more.BoundingBoxAsync();
        var menuBounds = await _page.Locator("#df-toolbar-overflow").BoundingBoxAsync();
        Assert.NotNull(moreBounds);
        Assert.NotNull(menuBounds);
        Assert.True(menuBounds.Y >= moreBounds.Y + moreBounds.Height - 1,
            $"Expected More menu below its button, button bottom={moreBounds.Y + moreBounds.Height}, menu top={menuBounds.Y}");
    }

    [LiveInspectorFact]
    public async Task ToolbarCompactIconButtonsKeepStableAccessibleNames()
    {
        await _page.SetViewportSizeAsync(260, 820);
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator("body")).ToHaveAttributeAsync("data-layout-width", "narrow");

        foreach (var (selector, label) in new[]
        {
            ("#df-toggle-workbench", "Open Tests"),
            ("#df-toggle-tree", "Tree"),
            ("#df-toggle-fit", "Fit"),
            ("#df-toggle-bounds", "Bounds"),
            ("#df-open-source", "Source"),
            ("#df-send-copilot", "Add to Copilot"),
            ("#df-evidence", "Evidence"),
            ("#df-toggle-dock", "Open Data"),
            ("#df-more", "More inspector actions"),
        })
        {
            await Expect(_page.Locator(selector)).ToHaveAttributeAsync("aria-label", label);
        }

        Assert.Equal("Open Tests", await _page.GetByRole(AriaRole.Button, new() { Name = "Open Tests", Exact = true }).GetAttributeAsync("aria-label"));
        Assert.Equal("Tree", await _page.GetByRole(AriaRole.Button, new() { Name = "Tree", Exact = true }).GetAttributeAsync("aria-label"));
    }

    [LiveInspectorFact]
    public async Task LayoutDataCheckStartsOnceAndReopensCachedResults()
    {
        await _page.SetViewportSizeAsync(1440, 900);
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));
        await _page.GotoAsync(BaseUrl);
        var header = _page.Locator("[data-automationId='HeaderLabel']");
        await Expect(header).ToBeAttachedAsync();
        var elementId = await header.GetAttributeAsync("data-id");
        Assert.False(string.IsNullOrWhiteSpace(elementId));

        var treeRow = _page.Locator($".df-tree-node[data-tree-id='{elementId}']");
        await treeRow.ClickAsync();
        var propertyLayout = _page.Locator(".df-prop-diagnostics");
        await Expect(propertyLayout).ToHaveCountAsync(1);
        await Expect(propertyLayout).ToHaveAttributeAsync("data-layout-state", "idle");
        await Expect(propertyLayout).ToContainTextAsync("Start");

        var scanRequests = 0;
        var firstScanStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstScan = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var response = JsonSerializer.Serialize(new
        {
            ok = true,
            report = new
            {
                schemaVersion = "2.0",
                ruleSetVersion = "2.0",
                capturedUtc = "2026-08-10T08:00:00Z",
                platform = "Test",
                snapshot = new
                {
                    id = "layout-toolbar",
                    capturedAt = "2026-08-10T08:00:00Z",
                    platform = "Test",
                    treeRevision = "tree-1",
                    diagnosticsRevision = "diagnostics-1",
                    stable = true,
                },
                scope = new
                {
                    rootElementId = elementId,
                    elementsExamined = 1,
                    maxElements = 2000,
                    truncated = false,
                },
                coverage = new
                {
                    overall = "partial",
                    rules = new[]
                    {
                        new
                        {
                            ruleId = "layout.visible-zero-area",
                            support = "full",
                            confidence = "high",
                            evaluated = 1,
                            skipped = 0,
                            limitations = Array.Empty<string>(),
                        },
                    },
                    limitations = Array.Empty<string>(),
                    neverCaptured = new[] { "Element Text/Value content" },
                },
                summary = new
                {
                    violations = 1,
                    observations = 0,
                    incomplete = 0,
                    passes = 0,
                    notApplicable = 0,
                    suppressed = 0,
                },
                findings = new[]
                {
                    new
                    {
                        id = $"layout.visible-zero-area:{elementId}:area",
                        ruleId = "layout.visible-zero-area",
                        outcome = "violation",
                        severity = "serious",
                        confidence = "high",
                        actionability = "fix",
                        message = "The selected label has no visible area.",
                        explanation = "A realized element with no area cannot draw.",
                        element = new
                        {
                            id = elementId,
                            type = "Label",
                            automationId = "HeaderLabel",
                        },
                        relatedElements = Array.Empty<object>(),
                        limitations = Array.Empty<string>(),
                        fixCategories = new[] { "adjust-layout-constraints" },
                    },
                },
            },
        });
        await _page.RouteAsync("**/api/diagnostics/layout", async route =>
        {
            var requestNumber = Interlocked.Increment(ref scanRequests);
            if (requestNumber == 1)
            {
                firstScanStarted.TrySetResult(true);
                await releaseFirstScan.Task;
            }
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = response,
            });
        });

        var dataEntry = _page.Locator("#df-toggle-dock");
        await dataEntry.ClickAsync();
        await _page.Locator("#df-tab-layout").ClickAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Start check", Exact = true }).ClickAsync();
        await firstScanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Expect(_page.Locator("#df-data-status")).ToHaveTextAsync("…");
        await Expect(dataEntry).ToHaveAttributeAsync("aria-busy", "true");
        releaseFirstScan.TrySetResult(true);

        await Expect(_page.Locator("#df-dock")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-tab-layout")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(_page.Locator(".df-layout-root")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-data-status")).ToHaveTextAsync("1");
        await Expect(dataEntry).ToHaveAttributeAsync("aria-busy", "false");
        await Expect(dataEntry).ToHaveAttributeAsync("aria-label", "Close Data; layout has 1 issue");
        await Expect(_page.Locator("#df-tab-layout")).ToHaveTextAsync("Layout (1)");
        await Expect(treeRow.Locator(".df-tree-diagnostic")).ToHaveTextAsync("1");
        await Expect(propertyLayout).ToHaveAttributeAsync("data-layout-state", "ready");
        await Expect(propertyLayout).ToContainTextAsync("1 issue");
        Assert.Equal(1, scanRequests);

        var otherElementId = await _page
            .Locator(".devflow-element:not([data-automationId='HeaderLabel'])")
            .First
            .GetAttributeAsync("data-id");
        Assert.False(string.IsNullOrWhiteSpace(otherElementId));
        await _page.Locator($".df-tree-node[data-tree-id='{otherElementId}']").ClickAsync();
        await Expect(propertyLayout).ToHaveCountAsync(1);
        await Expect(propertyLayout).ToHaveAttributeAsync("data-layout-state", "ready");
        await Expect(propertyLayout).ToContainTextAsync("View Layout");
        await treeRow.ClickAsync();
        var selectedLayoutState = await propertyLayout.GetAttributeAsync("data-layout-state");
        var staleReason = await _page.Locator("body").GetAttributeAsync("data-layout-stale-reason");
        Assert.True(
            string.Equals(selectedLayoutState, "ready", StringComparison.Ordinal),
            $"Selecting another Inspector element must not stale Layout results. State={selectedLayoutState}, reason={staleReason}");

        await _page.Locator("#df-dock-close").ClickAsync();
        await dataEntry.ClickAsync();
        await Expect(_page.Locator(".df-layout-root")).ToBeVisibleAsync();
        Assert.Equal(1, scanRequests);

        await _page.Locator("#df-dock-close").ClickAsync();
        await treeRow.ClickAsync();
        await propertyLayout.GetByRole(
            AriaRole.Button,
            new() { Name = "View all in Layout", Exact = true }).ClickAsync();
        await Expect(_page.Locator(".df-layout-focus")).ToContainTextAsync("HeaderLabel");
        Assert.Equal(1, scanRequests);

        await _page.RouteAsync("**/api/setProperty", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"ok\":true}",
        }));
        var opacity = _page
            .Locator(".df-prop-row")
            .Filter(new() { HasText = "Opacity" })
            .Locator("input[type='number']");
        await opacity.FillAsync("0.9");
        await opacity.DispatchEventAsync("change");
        await Expect(_page.Locator("#df-data-status")).ToHaveTextAsync("1");
        await Expect(dataEntry).ToHaveAttributeAsync(
            "aria-label",
            new Regex("results may be outdated", RegexOptions.IgnoreCase));
        await Expect(propertyLayout).ToHaveAttributeAsync("data-layout-state", "stale");
        await Expect(_page.Locator("#df-dock-action-strip")).ToContainTextAsync("Stale");

        await propertyLayout.GetByRole(
            AriaRole.Button,
            new() { Name = "Rescan", Exact = true }).ClickAsync();
        await Expect(_page.Locator("#df-data-status")).ToHaveTextAsync("1");
        await Expect(propertyLayout).ToHaveAttributeAsync("data-layout-state", "ready");
        Assert.Equal(2, scanRequests);
    }

    [LiveInspectorFact]
    public async Task CopilotSubmenuKeepsOverflowMenuOpen()
    {
        await _page.SetViewportSizeAsync(420, 820);
        await _page.GotoAsync(BaseUrl);
        var elementId = await _page.Locator("[data-automationId='HeaderLabel']").GetAttributeAsync("data-id");
        await _page.Locator("#df-toggle-tree").ClickAsync();
        await _page.Locator($".df-tree-node[data-tree-id='{elementId}']").ClickAsync();
        foreach (var width in new[] { 420, 360, 300, 260 })
        {
            await _page.SetViewportSizeAsync(width, 820);
            await _page.WaitForTimeoutAsync(100);
            if (await _page.Locator("#df-send-copilot").EvaluateAsync<string>(
                    "button => button.parentElement.id") == "df-toolbar-overflow")
                break;
        }
        Assert.Equal("df-toolbar-overflow", await _page.Locator("#df-send-copilot").EvaluateAsync<string>(
            "button => button.parentElement.id"));

        await _page.Locator("#df-more").ClickAsync();
        await _page.Locator("#df-send-copilot").ClickAsync();
        await Expect(_page.Locator("#df-toolbar-overflow")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-copilot-menu")).ToBeVisibleAsync();
    }

    [LiveInspectorFact]
    public async Task NarrowLayoutCoordinatesTreeAndPropertiesDrawers()
    {
        await _page.SetViewportSizeAsync(420, 820);
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator(".devflow-element").First).ToBeAttachedAsync();

        var toolbar = await _page.Locator("#df-toolbar").BoundingBoxAsync();
        Assert.NotNull(toolbar);
        Assert.True(toolbar.Height <= 40, $"Expected a single-row toolbar, got {toolbar.Height}px");
        Assert.True(await _page.Locator("body").EvaluateAsync<bool>("body => body.classList.contains('df-tree-hidden')"));

        await _page.Locator("#df-toggle-tree").ClickAsync();
        await Expect(_page.Locator("#df-tree-pane")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-pane-scrim")).ToBeVisibleAsync();

        await _page.Locator(".df-tree-node").Last.ClickAsync();
        await Expect(_page.Locator("#df-props-pane")).ToBeVisibleAsync();
        Assert.True(await _page.Locator("body").EvaluateAsync<bool>("body => body.classList.contains('df-tree-hidden')"));
        await _page.Locator("#df-status").EvaluateAsync("status => status.textContent = 'Property update failed.'");
        await Expect(_page.Locator("#df-status")).ToBeVisibleAsync();

        var props = await _page.Locator("#df-props-pane").BoundingBoxAsync();
        Assert.NotNull(props);
        Assert.True(Math.Abs(props.Width - 420) <= 1, $"Expected a full-width narrow drawer, got {props.Width}px");
    }

    [LiveInspectorFact]
    public async Task ConstrainedDataSheetPreservesScreenshotBudget()
    {
        await _page.SetViewportSizeAsync(420, 500);
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator(".devflow-element").First).ToBeAttachedAsync();

        var bodyBefore = await _page.Locator("#df-body").BoundingBoxAsync();
        Assert.NotNull(bodyBefore);

        await _page.Locator("#df-more").ClickAsync();
        await _page.Locator("#df-toggle-dock").ClickAsync();
        await Expect(_page.Locator("#df-dock")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-pane-scrim")).ToBeVisibleAsync();

        var bodyAfter = await _page.Locator("#df-body").BoundingBoxAsync();
        Assert.NotNull(bodyAfter);
        Assert.True(Math.Abs(bodyBefore.Height - bodyAfter.Height) <= 1,
            $"Opening the constrained data sheet changed body height {bodyBefore.Height} -> {bodyAfter.Height}");

        await _page.Locator("#df-dock-collapse").ClickAsync();
        Assert.True(await _page.Locator("body").EvaluateAsync<bool>("body => body.classList.contains('df-dock-collapsed')"));
    }

    [LiveInspectorFact]
    public async Task DataContentStartsDirectlyBelowTabs()
    {
        await _page.SetViewportSizeAsync(420, 500);
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator(".devflow-element").First).ToBeAttachedAsync();

        await _page.Locator("#df-more").ClickAsync();
        await _page.Locator("#df-toggle-dock").ClickAsync();
        await Expect(_page.Locator("#df-dock-body > *").First).ToBeVisibleAsync();

        var tabs = await _page.Locator("#df-dock-tabs").BoundingBoxAsync();
        var content = await _page.Locator("#df-dock-body > *").First.BoundingBoxAsync();
        Assert.NotNull(tabs);
        Assert.NotNull(content);
        var gap = content.Y - (tabs.Y + tabs.Height);
        Assert.InRange(gap, 4, 8);
    }

    [LiveInspectorFact]
    public async Task PassiveDataTabsHideActionStrip()
    {
        await _page.GotoAsync(BaseUrl);
        await OpenDataDockAsync();

        foreach (var tab in new[] { "problems", "logs", "network", "preferences", "device" })
        {
            await _page.Locator($"[data-tab='{tab}']").ClickAsync();
            await Expect(_page.Locator("#df-dock-action-strip")).ToBeHiddenAsync();
        }
    }

    [LiveInspectorFact]
    public async Task SelectingLayoutTabDoesNotStartCheck()
    {
        var scanRequests = 0;
        await _page.RouteAsync("**/api/diagnostics/layout", route =>
        {
            Interlocked.Increment(ref scanRequests);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"ok":false,"error":"Unexpected scan"}""",
            });
        });

        await _page.GotoAsync(BaseUrl);
        await OpenDataDockAsync();
        await _page.Locator("[data-tab='layout']").ClickAsync();

        await Expect(_page.Locator(".df-data-state")).ToContainTextAsync("Layout not checked");
        Assert.Equal("0px", await _page.Locator(".df-data-state").EvaluateAsync<string>(
            "state => getComputedStyle(state).borderLeftWidth"));
        Assert.Equal(0, scanRequests);
    }

    [LiveInspectorFact]
    public async Task LayoutTabUsesCompactDockHierarchyAcrossResponsiveWidths()
    {
        await _page.SetViewportSizeAsync(1440, 900);
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator(".devflow-element").First).ToBeAttachedAsync();
        await _page.Locator("#df-toggle-dock").ClickAsync();
        await _page.Locator("[data-tab='layout']").ClickAsync();

        await Expect(_page.Locator(".df-data-state")).ToContainTextAsync("Layout not checked");
        await Expect(_page.Locator("#df-dock-action-strip")).ToBeVisibleAsync();
        var startBox = await _page.Locator("#df-dock-action-strip")
            .GetByRole(AriaRole.Button, new() { Name = "Start check", Exact = true })
            .BoundingBoxAsync();
        var contextBox = await _page.Locator("#df-dock-action-strip .df-data-action-context").BoundingBoxAsync();
        Assert.NotNull(startBox);
        Assert.NotNull(contextBox);
        Assert.True(startBox.X < contextBox.X, "Primary Data actions should lead on the left.");
        await _page.Locator("#df-dock-action-strip").GetByRole(AriaRole.Button, new() { Name = "Start check", Exact = true }).ClickAsync();
        var root = _page.Locator(".df-layout-root");
        await Expect(root).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-dock-action-strip")).ToBeVisibleAsync();
        await Expect(root.Locator(".df-layout-summary")).ToHaveCountAsync(0);
        await Expect(root.Locator(".df-layout-header")).ToHaveCountAsync(0);
        await Expect(_page.Locator("#df-dock-action-strip .df-layout-coverage-button")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-dock-action-strip").GetByRole(AriaRole.Button, new() { Name = "Rescan", Exact = true })).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-dock-action-strip .df-layout-live-toggle")).ToBeVisibleAsync();

        var firstFinding = root.Locator(".df-layout-finding-row").First;
        if (await firstFinding.CountAsync() > 0)
        {
            await Expect(firstFinding.Locator(".df-status-pill")).ToBeVisibleAsync();
            await firstFinding.ClickAsync();
            await Expect(_page.Locator(".df-layout-subview")).ToBeVisibleAsync();
            await Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Findings", Exact = true })).ToBeVisibleAsync();
            await _page.GetByRole(AriaRole.Button, new() { Name = "Findings", Exact = true }).ClickAsync();
        }

        await _page.Locator("#df-dock-action-strip .df-layout-coverage-button").ClickAsync();
        await Expect(_page.Locator(".df-layout-compact-table")).ToBeVisibleAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Findings", Exact = true }).ClickAsync();

        await _page.Locator("#df-dock").EvaluateAsync("dock => dock.style.height = '180px'");
        var rowsVisibleAtMinimumHeight = await root.Locator(".df-layout-finding-row").EvaluateAllAsync<int>(
            "rows => { const body = document.querySelector('#df-dock-body').getBoundingClientRect(); return rows.filter(row => { const r = row.getBoundingClientRect(); return r.bottom > body.top && r.top < body.bottom; }).length; }");
        Assert.True(rowsVisibleAtMinimumHeight >= Math.Min(2, await root.Locator(".df-layout-finding-row").CountAsync()));
        await _page.Locator("#df-dock").EvaluateAsync("dock => dock.style.height = ''");

        await _page.SetViewportSizeAsync(900, 700);
        await _page.WaitForTimeoutAsync(100);
        await Expect(_page.Locator("body")).ToHaveAttributeAsync("data-layout-width", "compact");

        await _page.SetViewportSizeAsync(420, 820);
        await _page.WaitForTimeoutAsync(100);
        await Expect(_page.Locator("body")).ToHaveAttributeAsync("data-layout-width", "narrow");
        Assert.True(await root.EvaluateAsync<bool>(
            "root => root.scrollWidth <= root.clientWidth + 1"),
            "The narrow Layout surface must not introduce horizontal overflow.");
    }

    [LiveInspectorFact]
    public async Task DataControlsUseThemedSvgIcons()
    {
        await _page.RouteAsync("**/api/sensors", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"sensors":[]}""",
        }));
        await _page.RouteAsync("**/api/files/roots", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """
                {"ok":true,"roots":{"roots":[{"id":"appData","displayName":"App data","kind":"appData","isReadOnly":false,"isPersistent":true,"isUserVisible":false}]}}
                """,
        }));
        await _page.RouteAsync("**/api/files/list", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """
                {"ok":true,"files":{"root":"appData","path":"","entries":[{"name":"logs","type":"directory"},{"name":"settings.json","type":"file","size":42}]}}
                """,
        }));

        await OpenDataDockAsync();

        await _page.Locator("[data-tab='sensors']").ClickAsync();
        await Expect(_page.Locator("#df-dock-action-strip button use[href='#i-location']")).ToHaveCountAsync(1);
        Assert.DoesNotContain("📍", await _page.Locator("#df-dock-panel").InnerTextAsync());

        await _page.Locator("[data-tab='files']").ClickAsync();
        await Expect(_page.Locator("#df-dock-action-strip #df-files-root")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-dock-body use[href='#i-folder']")).ToHaveCountAsync(1);
        await Expect(_page.Locator("#df-dock-body use[href='#i-file']")).ToHaveCountAsync(1);
        var filesText = await _page.Locator("#df-dock-body").InnerTextAsync();
        Assert.DoesNotContain("📁", filesText);
        Assert.DoesNotContain("📄", filesText);
    }

    [LiveInspectorFact]
    public async Task SensorsLocationUsesSharedNavigationStripAndDoesNotChangeSnapshotScope()
    {
        await _page.RouteAsync("**/api/sensors", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"sensors":[{"sensor":"accelerometer","supported":true,"active":false,"subscribers":0}]}""",
        }));
        await _page.RouteAsync("**/api/geolocation", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"location":{"latitude":47.6,"longitude":-122.3,"accuracy":5}}""",
        }));

        await OpenDataDockAsync();
        await _page.Locator("[data-tab='sensors']").ClickAsync();
        var strip = _page.Locator("#df-dock-action-strip");
        await Expect(strip).ToContainTextAsync("1 sensor");
        await strip.GetByRole(AriaRole.Button, new() { Name = "Read location", Exact = true }).ClickAsync();

        await Expect(strip).ToContainTextAsync("5 m accuracy");
        await Expect(_page.Locator("#df-dock-body")).ToContainTextAsync("47.6");
        await Expect(strip.GetByRole(AriaRole.Button, new() { Name = "Sensors", Exact = true })).ToBeVisibleAsync();
        await strip.GetByRole(AriaRole.Button, new() { Name = "Sensors", Exact = true }).ClickAsync();
        await Expect(_page.Locator("#df-dock-body")).ToContainTextAsync("accelerometer");
        await Expect(_page.Locator("#df-attach-data")).ToBeEnabledAsync();

        await _page.Locator("#df-dock-collapse").ClickAsync();
        await Expect(_page.Locator("#df-dock-panel")).ToBeHiddenAsync();
        await Expect(strip).ToBeHiddenAsync();
        await _page.Locator("#df-dock-collapse").ClickAsync();
        await Expect(strip).ToBeVisibleAsync();
    }

    [LiveInspectorFact]
    public async Task ToolbarToggleStateIsExposedToAssistiveTechnology()
    {
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator(".devflow-element").First).ToBeAttachedAsync();

        await Expect(_page.Locator("#df-mode-interact")).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(_page.Locator("#df-mode-inspect")).ToHaveAttributeAsync("aria-checked", "false");
        await _page.Locator("#df-mode-inspect").ClickAsync();
        await Expect(_page.Locator("#df-mode-interact")).ToHaveAttributeAsync("aria-checked", "false");
        await Expect(_page.Locator("#df-mode-inspect")).ToHaveAttributeAsync("aria-checked", "true");

        await ExpectToolbarToggleStateAsync("#df-toggle-bounds", false);
        await ClickToolbarActionAsync("#df-toggle-bounds");
        await ExpectToolbarToggleStateAsync("#df-toggle-bounds", true);

        await ExpectToolbarToggleStateAsync("#df-toggle-dock", false);
        await ClickToolbarActionAsync("#df-toggle-dock");
        await ExpectToolbarToggleStateAsync("#df-toggle-dock", true);
        await _page.Locator("#df-dock-close").ClickAsync();
        await ExpectToolbarToggleStateAsync("#df-toggle-dock", false);
    }

    [LiveInspectorFact]
    public async Task ToolbarOverflow_PreservesActiveToggleAccent()
    {
        await _page.SetViewportSizeAsync(320, 700);
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator(".devflow-element").First).ToBeAttachedAsync();

        await ClickToolbarActionAsync("#df-toggle-bounds");
        await _page.Locator("#df-more").ClickAsync();
        var bounds = _page.Locator("#df-toolbar-overflow #df-toggle-bounds");
        await Expect(bounds).ToBeVisibleAsync();
        var colors = await bounds.EvaluateAsync<string[]>(
            """
            element => {
              const probe = document.createElement('span');
              probe.style.backgroundColor = 'var(--df-accent)';
              probe.style.color = 'var(--df-accent-fg)';
              element.append(probe);
              const expected = getComputedStyle(probe);
              const actual = getComputedStyle(element);
              const result = [
                actual.backgroundColor,
                expected.backgroundColor,
                actual.color,
                expected.color
              ];
              probe.remove();
              return result;
            }
            """);
        Assert.Equal(colors[1], colors[0]);
        Assert.Equal(colors[3], colors[2]);
    }

    [LiveInspectorFact]
    public async Task HoverUsesTheSameVisualTargetInAllModes()
    {
        await _page.GotoAsync(BaseUrl);
        var viewport = _page.Locator("#app-viewport");
        await Expect(viewport).ToBeVisibleAsync();
        await viewport.EvaluateAsync("""
            viewport => {
                for (const existing of viewport.querySelectorAll('.devflow-element'))
                    existing.style.pointerEvents = 'none';
                const add = (id, left, traits, enabled = true) => {
                    const element = document.createElement('div');
                    element.className = 'devflow-element';
                    element.dataset.id = id;
                    element.dataset.type = 'Button';
                    element.dataset.isvisible = 'true';
                    element.dataset.isenabled = String(enabled);
                    if (traits) element.dataset.traits = traits;
                    element.dataset.interactable = String(enabled && ['interactive', 'scrollable']
                        .some(trait => (traits || '').split(',').includes(trait)));
                    element.style.cssText = `position:absolute;left:${left}px;top:4px;width:24px;height:24px;pointer-events:auto;`;
                    viewport.appendChild(element);
                };
                add('hover-static', 4, '');
                add('hover-disabled', 36, 'interactive', false);
                add('hover-interactive', 68, 'interactive');
                add('hover-scrollable', 100, 'scrollable');
                add('hover-under-interactive', 132, 'interactive');
                add('hover-over-static', 132, '');
            }
            """);

        var staticElement = _page.Locator("[data-id='hover-static']");
        var disabledElement = _page.Locator("[data-id='hover-disabled']");
        var interactiveElement = _page.Locator("[data-id='hover-interactive']");
        var scrollableElement = _page.Locator("[data-id='hover-scrollable']");
        var underlyingInteractive = _page.Locator("[data-id='hover-under-interactive']");
        var overlappingStatic = _page.Locator("[data-id='hover-over-static']");

        await interactiveElement.HoverAsync(new() { Force = true });
        await Expect(interactiveElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));
        await Expect(interactiveElement).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover-noninteractive(\\s|$)"));
        await Expect(_page.Locator("#df-badge")).ToContainTextAsync("Interactive element");

        await scrollableElement.HoverAsync(new() { Force = true });
        await Expect(scrollableElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));
        await Expect(_page.Locator("#df-badge")).ToContainTextAsync("Scrollable element");

        await staticElement.HoverAsync(new() { Force = true });
        await Expect(staticElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));
        await Expect(staticElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover-noninteractive(\\s|$)"));
        Assert.Equal("dashed", await staticElement.EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));
        Assert.Equal("rgba(0, 0, 0, 0)", await staticElement.EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
        await Expect(_page.Locator("#df-badge")).ToContainTextAsync("Visual element");

        await disabledElement.HoverAsync(new() { Force = true });
        await Expect(disabledElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));
        await Expect(disabledElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover-noninteractive(\\s|$)"));

        await overlappingStatic.HoverAsync(new() { Force = true });
        await Expect(overlappingStatic).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));
        await Expect(underlyingInteractive).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));
        await Expect(_page.Locator("#df-badge")).ToContainTextAsync("Visual element");

        var treeStaticElement = _page.Locator(".devflow-element[data-id='HeaderLabel']");
        await Expect(treeStaticElement).ToHaveAttributeAsync("data-interactable", "false");
        var treeStaticRow = _page.Locator(".df-tree-node[data-tree-id='HeaderLabel']");
        if (!await treeStaticRow.IsVisibleAsync())
            await _page.Locator("#df-toggle-tree").ClickAsync();
        await treeStaticRow.HoverAsync();
        await Expect(treeStaticElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));
        await Expect(treeStaticElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover-noninteractive(\\s|$)"));

        await _page.Locator("#df-mode-inspect").ClickAsync();
        await staticElement.HoverAsync(new() { Force = true });
        await Expect(staticElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));
        await Expect(staticElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover-noninteractive(\\s|$)"));
        await Expect(_page.Locator("#df-badge")).ToContainTextAsync("Visual element");

        await overlappingStatic.HoverAsync(new() { Force = true });
        await Expect(overlappingStatic).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));
        await Expect(underlyingInteractive).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));
        await Expect(_page.Locator("#df-badge")).ToContainTextAsync("Visual element");

        await interactiveElement.HoverAsync(new() { Force = true });
        await Expect(interactiveElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));
        await Expect(interactiveElement).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover-noninteractive(\\s|$)"));
        await Expect(_page.Locator("#df-badge")).ToContainTextAsync("Interactive element");
        Assert.Equal("solid", await interactiveElement.EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));
        Assert.NotEqual("rgba(0, 0, 0, 0)", await interactiveElement.EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
    }

    [LiveInspectorFact]
    public async Task InspectorExposesAccessibleDocumentStructure()
    {
        await _page.SetViewportSizeAsync(420, 820);
        await _page.GotoAsync(BaseUrl);

        await Expect(_page.Locator("html")).ToHaveAttributeAsync("lang", "en");
        await Expect(_page.Locator("#df-toolbar")).ToHaveAttributeAsync("role", "toolbar");
        await Expect(_page.Locator("#df-toolbar")).ToHaveAttributeAsync("aria-label", "Inspector controls");
        await Expect(_page.Locator("#df-view-pane")).ToHaveAttributeAsync("role", "main");
        await Expect(_page.Locator("#df-tree-pane")).ToHaveAttributeAsync("aria-label", "Visual tree");
        await Expect(_page.Locator("#df-props-pane")).ToHaveAttributeAsync("aria-label", "Properties");
        await Expect(_page.Locator("#df-presence")).ToHaveAttributeAsync("role", "status");
        await Expect(_page.Locator("#df-pane-scrim")).ToHaveAttributeAsync("tabindex", "-1");
        await Expect(_page.Locator(".df-modes")).ToHaveAttributeAsync("role", "radiogroup");
        await Expect(_page.Locator("#df-mode-interact")).ToHaveAttributeAsync("role", "radio");
        await Expect(_page.Locator("#df-mode-interact")).ToHaveAttributeAsync("aria-label", "Interact");
        await Expect(_page.Locator("#df-mode-inspect")).ToHaveAttributeAsync("aria-label", "Inspect");
        await Expect(_page.Locator("#df-mode-interact")).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(_page.Locator("#df-mode-inspect")).ToHaveAttributeAsync("aria-checked", "false");
        await Expect(_page.Locator("#df-toolbar-overflow")).ToHaveAttributeAsync("role", "menu");
        await Expect(_page.Locator("#df-toolbar-overflow")).ToHaveAttributeAsync("aria-label", "More inspector actions");
        await _page.Locator("#df-more").ClickAsync();
        await Expect(_page.Locator("#df-toolbar-overflow")).ToBeVisibleAsync();
        var firstOverflowAction = _page.Locator("#df-toolbar-overflow > button").First;
        var overflowRole = await firstOverflowAction.GetAttributeAsync("role");
        Assert.True(overflowRole is "menuitem" or "menuitemcheckbox",
            $"Expected a menu action role, but was '{overflowRole}'.");
        if (overflowRole == "menuitemcheckbox")
        {
            var expectedChecked = await firstOverflowAction.EvaluateAsync<bool>(
                "button => button.classList.contains('df-active')");
            await Expect(firstOverflowAction).ToHaveAttributeAsync(
                "aria-checked",
                expectedChecked ? "true" : "false");
        }
        await Expect(_page.Locator("#df-toggle-workbench")).ToHaveAttributeAsync("aria-controls", "df-workbench");
        await Expect(_page.Locator(".df-dock-tab-list")).ToHaveAttributeAsync("aria-label", "App data");
        await Expect(_page.Locator("#df-viewport-wrap")).ToHaveAttributeAsync("role", "region");
        await Expect(_page.Locator("#df-viewport-wrap")).ToHaveAttributeAsync("aria-label", "Live app viewport");
    }

    [LiveInspectorFact]
    public async Task TransientInspectorChromeRestoresKeyboardFocus()
    {
        await _page.SetViewportSizeAsync(420, 820);
        await _page.GotoAsync(BaseUrl);
        var more = _page.Locator("#df-more");
        await Expect(more).ToBeVisibleAsync();

        await more.FocusAsync();
        await _page.Keyboard.PressAsync("ArrowDown");
        await Expect(_page.Locator("#df-toolbar-overflow")).ToBeVisibleAsync();
        Assert.True(await _page.Locator("#df-toolbar-overflow > button").First.EvaluateAsync<bool>(
            "button => button === document.activeElement"));
        await _page.Keyboard.PressAsync("Escape");
        Assert.True(await more.EvaluateAsync<bool>("button => button === document.activeElement"));

        await _page.SetViewportSizeAsync(420, 820);
        await _page.WaitForTimeoutAsync(100);
        var treeToggle = _page.Locator("#df-toggle-tree");
        await treeToggle.ClickAsync();
        var firstTreeItem = _page.Locator(".df-tree-node").First;
        await firstTreeItem.FocusAsync();
        await _page.Keyboard.PressAsync("Escape");
        await Expect(_page.Locator("#df-tree-pane")).ToBeHiddenAsync();
        Assert.True(await treeToggle.EvaluateAsync<bool>("button => button === document.activeElement"));

        await treeToggle.ClickAsync();
        firstTreeItem = _page.Locator(".df-tree-node").First;
        await firstTreeItem.FocusAsync();
        await _page.Keyboard.PressAsync("Enter");
        await Expect(_page.Locator("#df-props-pane")).ToBeVisibleAsync();
        await _page.Keyboard.PressAsync("Escape");
        await Expect(_page.Locator("#df-props-pane")).ToBeHiddenAsync();
        Assert.True(await treeToggle.EvaluateAsync<bool>("button => button === document.activeElement"));
    }

    [LiveInspectorFact]
    public async Task DataTabSelectionSurvivesRefreshAndCollapse()
    {
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator(".devflow-element").First).ToBeAttachedAsync();
        await _page.Locator("#df-toggle-dock").ClickAsync();

        var network = _page.Locator("[data-tab='network']");
        await network.ClickAsync();
        await Expect(network).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(_page.Locator("[data-tab='logs']")).ToHaveAttributeAsync("aria-selected", "false");

        await _page.Locator("#df-dock-refresh").ClickAsync();
        await Expect(network).ToHaveAttributeAsync("aria-selected", "true");
        await _page.Locator("#df-dock-collapse").ClickAsync();
        await Expect(network).ToHaveAttributeAsync("aria-selected", "true");
        await _page.Locator("#df-dock-collapse").ClickAsync();
        await Expect(network).ToHaveAttributeAsync("aria-selected", "true");
    }

    [LiveInspectorFact]
    public async Task ElementTreeSupportsRovingKeyboardNavigation()
    {
        await _page.GotoAsync(BaseUrl);
        var tree = _page.Locator("#df-tree");
        var rows = tree.Locator(".df-tree-node");
        await Expect(tree).ToHaveAttributeAsync("role", "tree");
        await Expect(rows.First).ToHaveAttributeAsync("role", "treeitem");
        await Expect(rows.First).ToHaveAttributeAsync("tabindex", "0");

        await rows.First.FocusAsync();
        await _page.Keyboard.PressAsync("ArrowDown");
        Assert.True(await rows.Nth(1).EvaluateAsync<bool>("row => row === document.activeElement"));

        await _page.Keyboard.PressAsync("End");
        Assert.True(await rows.Last.EvaluateAsync<bool>("row => row === document.activeElement"));
        await _page.Keyboard.PressAsync("Home");
        Assert.True(await rows.First.EvaluateAsync<bool>("row => row === document.activeElement"));
    }

    [LiveInspectorFact]
    public async Task RepeatedTemplateNodesIncludeRuntimeItemContext()
    {
        await _page.GotoAsync(BaseUrl);
        var titleRows = _page.Locator(".df-tree-node").Filter(new() { HasText = "Label TodoTitle" });
        await Expect(titleRows.First).ToBeVisibleAsync();
        Assert.True(await titleRows.CountAsync() >= 3);
        var titleLabels = await titleRows.AllInnerTextsAsync();
        Assert.Contains(titleLabels, label => label.Contains("Buy groceries", StringComparison.Ordinal));
        Assert.Contains(titleLabels, label => label.Contains("Walk the dog", StringComparison.Ordinal));
        Assert.Contains(titleLabels, label => label.Contains("Finish Microsoft.Maui.DevFlow project", StringComparison.Ordinal));

        var checkRows = _page.Locator(".df-tree-node").Filter(new() { HasText = "CheckBox TodoCheckBox" });
        await Expect(checkRows.First).ToBeVisibleAsync();
        Assert.True(await checkRows.CountAsync() >= 3);
        var checkLabels = await checkRows.AllInnerTextsAsync();
        Assert.Contains(checkLabels, label => label.Contains("Buy groceries", StringComparison.Ordinal));
        Assert.Contains(checkLabels, label => label.Contains("Walk the dog", StringComparison.Ordinal));

        await titleRows.First.ClickAsync();
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("Buy groceries");
    }

    [LiveInspectorFact]
    public async Task HiddenInspectorRefreshesWhenVisible()
    {
        await _page.GotoAsync(BaseUrl);
        var header = _page.Locator("[data-automationId='HeaderLabel']");
        await Expect(header).ToBeAttachedAsync();
        var initialClaimed = await _page.EvaluateAsync<bool>("""
            async () => {
                const base = location.pathname.replace(/\/$/, '');
                const response = await fetch(base + '/api/control', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ action: 'claim', force: true }),
                });
                return response.ok;
            }
            """);
        Assert.True(initialClaimed);
        await Expect(_page.Locator("#df-presence")).ToContainTextAsync("Driving");
        var elementId = await header.GetAttributeAsync("data-id");
        var originalText = await header.GetAttributeAsync("data-text");
        Assert.NotNull(elementId);
        Assert.NotNull(originalText);

        var other = await _context.NewPageAsync();
        var cdp = await _context.NewCDPSessionAsync(_page);
        try
        {
            await other.GotoAsync(BaseUrl);
            await Expect(other.Locator("[data-automationId='HeaderLabel']")).ToBeAttachedAsync();
            await cdp.SendAsync("Page.setWebLifecycleState", new Dictionary<string, object> { ["state"] = "frozen" });

            var claimed = await other.EvaluateAsync<bool>("""
                async () => {
                    const base = location.pathname.replace(/\/$/, '');
                    const response = await fetch(base + '/api/control', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ action: 'claim', force: true }),
                    });
                    return response.ok;
                }
                """);
            Assert.True(claimed);

            var status = await other.EvaluateAsync<int>("""
                async ({ elementId }) => {
                    const base = location.pathname.replace(/\/$/, '');
                    const response = await fetch(base + '/api/setProperty', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ elementId, name: 'Text', value: 'Hidden refresh probe' }),
                    });
                    return response.status;
                }
                """, new { elementId });
            Assert.Equal(200, status);

            await cdp.SendAsync("Page.setWebLifecycleState", new Dictionary<string, object> { ["state"] = "active" });
            await _page.BringToFrontAsync();
            await Expect(header).ToHaveAttributeAsync("data-text", "Hidden refresh probe", new() { Timeout = 10_000 });
        }
        finally
        {
            await cdp.SendAsync("Page.setWebLifecycleState", new Dictionary<string, object> { ["state"] = "active" });
            var restoreStatus = await other.EvaluateAsync<int>("""
                async ({ elementId, originalText }) => {
                    const base = location.pathname.replace(/\/$/, '');
                    const response = await fetch(base + '/api/setProperty', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ elementId, name: 'Text', value: originalText }),
                    });
                    return response.status;
                }
                """, new { elementId, originalText });
            Assert.Equal(200, restoreStatus);
            await cdp.DetachAsync();
            await other.CloseAsync();
        }
    }

    [LiveInspectorFact]
    public async Task DataTabsSupportArrowKeyNavigation()
    {
        await _page.GotoAsync(BaseUrl);
        await OpenDataDockAsync();
        var logs = _page.Locator("[data-tab='logs']");
        var network = _page.Locator("[data-tab='network']");

        await logs.FocusAsync();
        await _page.Keyboard.PressAsync("ArrowRight");

        await Expect(network).ToHaveAttributeAsync("aria-selected", "true");
        Assert.True(await network.EvaluateAsync<bool>("tab => tab === document.activeElement"));
        await Expect(_page.Locator("#df-dock-panel")).ToHaveAttributeAsync("role", "tabpanel");
        await Expect(_page.Locator("#df-dock-panel")).ToHaveAttributeAsync("aria-labelledby", "df-tab-network");
        await Expect(_page.Locator("#df-dock-body")).ToHaveAttributeAsync("role", "region");
    }

    [LiveInspectorFact]
    public async Task FreeLeaseOffersTakeControlAfterInitialClaimFails()
    {
        var claimCount = 0;
        await _page.RouteAsync("**/api/control", route =>
        {
            using var body = System.Text.Json.JsonDocument.Parse(route.Request.PostData ?? "{}");
            var action = body.RootElement.TryGetProperty("action", out var value) ? value.GetString() : null;
            var ownsLease = string.Equals(action, "claim", StringComparison.Ordinal) &&
                Interlocked.Increment(ref claimCount) > 1;
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = $"{{\"youAreWriter\":{ownsLease.ToString().ToLowerInvariant()},\"heldByOther\":false}}",
            });
        });
        await _page.RouteAsync("**/api/state", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{}",
        }));

        await _page.GotoAsync(BaseUrl);

        var takeControl = _page.Locator("#df-take-control");
        await Expect(takeControl).ToBeVisibleAsync();
        await takeControl.ClickAsync();
        await OpenWorkbenchAsync();
        await _page.Locator("#df-goal-input").FillAsync("Verify takeover enables recording.");
        await Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Record steps", Exact = true })).ToBeEnabledAsync();
        Assert.True(claimCount >= 2);
    }

    [LiveInspectorFact]
    public async Task ReadOnlyPollingDoesNotAutomaticallyReclaimLease()
    {
        var initialClaims = 0;
        var statusPolls = 0;
        await _page.RouteAsync("**/api/control", route =>
        {
            using var body = System.Text.Json.JsonDocument.Parse(route.Request.PostData ?? "{}");
            var action = body.RootElement.TryGetProperty("action", out var value) ? value.GetString() : null;
            if (string.Equals(action, "claim", StringComparison.Ordinal))
                Interlocked.Increment(ref initialClaims);
            if (string.Equals(action, "status", StringComparison.Ordinal))
                Interlocked.Increment(ref statusPolls);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = "{\"youAreWriter\":false,\"heldByOther\":true,\"label\":\"Other Inspector\",\"expiresInMs\":10000}",
            });
        });

        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator("#df-presence")).ToContainTextAsync("Other Inspector");
        for (var attempt = 0; attempt < 80 && Volatile.Read(ref statusPolls) < 1; attempt++)
            await _page.WaitForTimeoutAsync(100);

        Assert.Equal(1, initialClaims);
        Assert.True(statusPolls >= 1, $"Expected status-only reconciliation, got {statusPolls} status poll(s).");
        await OpenWorkbenchAsync();
        await _page.Locator("#df-goal-input").FillAsync("Verify read-only recording state.");
        await Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Record steps", Exact = true })).ToBeDisabledAsync();
        await Expect(_page.Locator("#df-take-control")).ToBeVisibleAsync();
    }

    [LiveInspectorFact]
    public async Task ReadOnlyPresenceNamesOwnerAndConfirmsForcedTakeover()
    {
        var forcedClaims = 0;
        await _page.RouteAsync("**/api/control", route =>
        {
            using var body = System.Text.Json.JsonDocument.Parse(route.Request.PostData ?? "{}");
            var force = body.RootElement.TryGetProperty("force", out var forceValue) && forceValue.GetBoolean();
            if (force) Interlocked.Increment(ref forcedClaims);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = force
                    ? "{\"youAreWriter\":true,\"heldByOther\":false}"
                    : "{\"youAreWriter\":false,\"heldByOther\":true,\"label\":\"VS Code Inspector\",\"expiresInMs\":12000}",
            });
        });

        await _page.GotoAsync(BaseUrl);
        var presence = _page.Locator("#df-presence");
        await Expect(presence).ToContainTextAsync("VS Code Inspector");
        await Expect(presence).ToHaveAttributeAsync("title", new System.Text.RegularExpressions.Regex("12s"));

        await _page.Locator("#df-take-control").ClickAsync();
        var dialog = _page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToContainTextAsync("VS Code Inspector");
        Assert.Equal(0, forcedClaims);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Take control" }).ClickAsync();
        await Expect(presence).ToContainTextAsync("Driving");
        Assert.Equal(1, forcedClaims);
    }

    [LiveInspectorFact]
    public async Task ReplayDialogEnterOnCancelDoesNotReplay()
    {
        var replayRequests = 0;
        const string recordedMarkdown = "# Keyboard test\n\n```json maui-test\n{\"schemaVersion\":1,\"name\":\"keyboard-test\",\"steps\":[]}\n```";
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));
        await _page.RouteAsync("**/api/flows/record/start", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"ok\":true,\"recordingId\":\"test-recording\",\"name\":\"keyboard-test\"}",
        }));
        await _page.RouteAsync("**/api/flows/record/stop", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                ok = true,
                name = "keyboard-test",
                steps = 1,
                markdown = recordedMarkdown,
            }),
        }));
        await _page.RouteAsync("**/api/flows/replay", route =>
        {
            replayRequests++;
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = "{\"ok\":true,\"total\":0,\"passed\":0,\"failed\":0}",
            });
        });

        await _page.GotoAsync(BaseUrl);
        const string savedFlow = "recording-2026-08-04T07-33-09.md";
        await OpenSavedTestAsync(savedFlow);
        await OpenLegacyQuickReplayAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToBeVisibleAsync();
        await _page.Keyboard.PressAsync("Shift+Tab");
        var cancel = dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" });
        Assert.True(await cancel.EvaluateAsync<bool>("button => button === document.activeElement"));
        await _page.Keyboard.PressAsync("Enter");

        await Expect(dialog).ToHaveCountAsync(0);
        Assert.Equal(0, replayRequests);
    }

    [LiveInspectorFact]
    public async Task ImproveNoTestCtaGoesDirectlyToGoal()
    {
        await _page.GotoAsync(BaseUrl);
        await OpenWorkbenchAsync();
        await _page.EvaluateAsync("() => window.dispatchEvent(new CustomEvent('devflow:workbench-state', { detail: { selectedTab: 'improve' } }))");

        var improve = _page.Locator("#df-workbench-panel-improve");
        await Expect(improve).ToContainTextAsync("No test to scan");
        await Expect(improve.GetByRole(AriaRole.Button, new() { Name = "Go to Goal", Exact = true })).ToBeVisibleAsync();

        await improve.GetByRole(AriaRole.Button, new() { Name = "Go to Goal", Exact = true }).ClickAsync();
        await Expect(_page.Locator("#df-workbench-stage-goal")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("df-active"));
        await Expect(_page.Locator("#df-goal-input")).ToBeVisibleAsync();
        await _page.WaitForTimeoutAsync(50);
        Assert.True(await _page.Locator("#df-goal-input").EvaluateAsync<bool>("element => element === document.activeElement"));
    }

    [LiveInspectorFact]
    public async Task WorkflowPanelLoadsProjectAndLocalFilesAndShowsReplayResultsInline()
    {
        const string projectMarkdown = "# Project workflow\n\n```json maui-test\n{\"schemaVersion\":1,\"name\":\"project\",\"steps\":[{\"seq\":1,\"stepId\":\"1\",\"action\":\"tap\",\"label\":\"Tap AddButton\",\"target\":{\"automationId\":\"AddButton\"},\"asserts\":[{\"kind\":\"exists\",\"selector\":{\"automationId\":\"AddButton\"},\"verify\":true}]}]}\n```";
        const string localMarkdown = "# Local workflow\n\n```json maui-test\n{\"schemaVersion\":1,\"name\":\"local\",\"steps\":[]}\n```";
        const string appendedMarkdown = "# Appended workflow\n\n```json maui-test\n{\"schemaVersion\":1,\"name\":\"appended\",\"steps\":[{\"seq\":1,\"stepId\":\"2\",\"action\":\"tap\",\"label\":\"Tap AnotherButton\",\"target\":{\"automationId\":\"AnotherButton\"}}]}\n```";
        var replayRequests = 0;
        var preflightRequests = 0;
        var startRequests = 0;
        var journalRequests = 0;
        var statusRequests = 0;
        var writer = true;
        string? startedIdempotencyKey = null;
        var recoveryKeyMatched = false;
        var recoveryJournalAttempts = 0;
        var preflightKeys = new List<string>();
        using var flowDocumentJson = JsonDocument.Parse("""
            {
              "schemaVersion": 1,
              "name": "project",
              "steps": [
                {
                  "seq": 1,
                  "stepId": "1",
                  "action": "tap",
                  "label": "Tap AddButton",
                  "target": { "automationId": "AddButton" },
                  "asserts": [
                    {
                      "kind": "exists",
                      "selector": { "automationId": "AddButton" },
                      "verify": true
                    }
                  ]
                }
              ]
            }
            """);
        using var planDocumentJson = JsonDocument.Parse("""
            {
              "schema": 1,
              "planId": "plan-project",
              "revision": 1,
              "flow": { "path": "saved.md", "digest": "" },
              "goal": "Verify the saved workflow through broker preflight.",
              "sideEffectPolicy": "none",
              "scenarios": [],
              "acceptanceCriteria": [],
              "businessOracles": [],
              "independentBusinessOracles": []
            }
            """);
        var flowDocument = flowDocumentJson.RootElement.Clone();
        var planDocument = planDocumentJson.RootElement.Clone();
        await _page.RouteAsync("**/api/control", async route =>
        {
            using var body = JsonDocument.Parse(route.Request.PostData ?? "{}");
            var action = body.RootElement.TryGetProperty("action", out var value)
                ? value.GetString()
                : "status";
            if (action == "release") writer = false;
            else if (action == "claim") writer = true;
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    youAreWriter = writer,
                    heldByOther = false,
                }),
            });
        });
        await _page.RouteAsync("**/api/flows/record/start", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"ok\":true,\"recordingId\":\"aaaaaaaaaaaaaaaaaaaaaaaa\",\"name\":\"appended\",\"steps\":0}",
        }));
        await _page.RouteAsync("**/api/flows/record/stop", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                ok = true,
                name = "appended",
                steps = 1,
                markdown = appendedMarkdown,
            }),
        }));
        await _page.RouteAsync("**/api/flows/files/list", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"ok\":true,\"supported\":true,\"tests\":[{\"name\":\"saved.md\",\"size\":100}]}",
        }));
        await _page.RouteAsync("**/api/flows/files/load", async route =>
        {
            using var body = JsonDocument.Parse(route.Request.PostData ?? "{}");
            Assert.Equal("saved.md", body.RootElement.GetProperty("name").GetString());
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    ok = true,
                    name = "saved.md",
                    markdown = projectMarkdown,
                    steps = 1,
                }),
            });
        });
        await _page.RouteAsync("**/api/plans/load", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                ok = true,
                flow = new
                {
                    name = "saved.md",
                    markdown = projectMarkdown,
                    document = flowDocument,
                    digest = "saved-flow-digest",
                },
                plan = new
                {
                    json = planDocument.GetRawText(),
                    document = planDocument,
                    digest = "saved-plan-digest",
                    revision = 1,
                },
            }),
        }));
        await _page.RouteAsync("**/api/workbench/target", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """
                {
                  "ok": true,
                  "target": {
                    "agentId": "sample-agent",
                    "agentInstanceId": "sample-instance",
                    "appName": "Sample",
                    "platform": "android"
                  },
                  "broker": {}
                }
                """,
        }));
        await _page.RouteAsync("**/api/workbench/run/preflight", async route =>
        {
            Interlocked.Increment(ref preflightRequests);
            using var body = JsonDocument.Parse(route.Request.PostData ?? "{}");
            preflightKeys.Add(body.RootElement
                .GetProperty("run")
                .GetProperty("idempotencyKey")
                .GetString()!);
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = """
                    {
                      "ok": true,
                      "flowDigest": "saved-flow-digest",
                      "admission": {
                        "ordinaryReplayAllowed": true,
                        "runVerificationAllowed": false,
                        "reasons": [
                          {
                            "code": "independent-oracle-absent",
                            "message": "No required independent business oracle is declared, so the run or repair cannot be verified.",
                            "blocking": false,
                            "scope": "verification"
                          }
                        ]
                      }
                    }
                    """,
            });
        });
        await _page.RouteAsync("**/api/workbench/run/start", async route =>
        {
            Interlocked.Increment(ref startRequests);
            using var body = JsonDocument.Parse(route.Request.PostData ?? "{}");
            startedIdempotencyKey = body.RootElement
                .GetProperty("run")
                .GetProperty("idempotencyKey")
                .GetString();
            await route.AbortAsync("failed");
        });
        await _page.RouteAsync("**/api/workbench/run/journal*", route =>
        {
            Interlocked.Increment(ref journalRequests);
            if (startRequests > 0 && !string.IsNullOrWhiteSpace(startedIdempotencyKey))
            {
                Interlocked.Increment(ref recoveryJournalAttempts);
                recoveryKeyMatched = new Uri(route.Request.Url).Query.Contains(
                    Uri.EscapeDataString(startedIdempotencyKey),
                    StringComparison.Ordinal);
            }
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = startRequests == 0 || recoveryJournalAttempts == 1
                    ? """{"ok":true}"""
                    : recoveryJournalAttempts == 2
                        ? """{"ok":true,"pending":true}"""
                    : JsonSerializer.Serialize(new
                    {
                        ok = true,
                        run = new
                        {
                            runId = "recovered-run",
                            state = "running",
                            terminal = false,
                            flowDigest = "saved-flow-digest",
                            createdAt = DateTimeOffset.UtcNow,
                            totalSteps = 1,
                            completedSteps = 0,
                            events = Array.Empty<object>(),
                        },
                    }),
            });
        });
        await _page.RouteAsync("**/api/workbench/run/recovered-run/status", route =>
        {
            Interlocked.Increment(ref statusRequests);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = """
                    {
                      "ok": true,
                      "run": {
                        "runId": "recovered-run",
                        "state": "passed",
                        "terminal": true,
                        "flowDigest": "saved-flow-digest",
                        "totalSteps": 1,
                        "completedSteps": 1,
                        "events": [],
                        "report": {
                          "outcome": { "status": "passed", "summary": "Flow replay passed.", "terminal": true },
                          "steps": [{ "stepId": "1", "sequence": 1, "action": "tap", "intent": "Tap AddButton" }]
                        }
                      }
                    }
                    """,
            });
        });
        await _page.RouteAsync("**/api/flows/replay", async route =>
        {
            using var body = JsonDocument.Parse(route.Request.PostData ?? "{}");
            var replayMarkdown = body.RootElement.GetProperty("markdown").GetString();
            Assert.False(string.IsNullOrWhiteSpace(replayMarkdown));
            Assert.DoesNotContain("schemaVersion", replayMarkdown, StringComparison.Ordinal);
            var parsed = FlowMarkdown.Parse(replayMarkdown!);
            Assert.True(parsed.Ok, parsed.Error);
            Assert.Equal(1, parsed.Flow!.Schema);
            Assert.Equal("project", parsed.Flow.Name);
            var replayStep = Assert.Single(parsed.Flow.Steps);
            Assert.Equal("Tap AddButton", replayStep.Label);
            Assert.Equal("AddButton", replayStep.Target!.AutomationId);
            Assert.DoesNotContain("AnotherButton", replayMarkdown, StringComparison.Ordinal);
            Assert.DoesNotContain("\"name\":\"local\"", replayMarkdown, StringComparison.Ordinal);
            Interlocked.Increment(ref replayRequests);
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = """
                    {
                      "ok": true,
                      "passed": 1,
                      "failed": 0,
                      "total": 1,
                      "results": [{ "seq": 1, "action": "tap", "label": "Tap AddButton", "ok": true }]
                    }
                    """,
            });
        });

        await _page.GotoAsync(BaseUrl);
        await OpenSavedTestAsync("saved.md");
        await Expect(_page.Locator("#df-workbench-strip")).ToContainTextAsync("saved.md");
        await Expect(_page.Locator("#df-workbench-stage-record")).ToBeEnabledAsync();
        await Expect(_page.Locator("#df-workbench-stage-review")).ToBeEnabledAsync();
        await Expect(_page.Locator("#df-workbench-stage-run")).ToBeEnabledAsync();
        await Expect(_page.Locator("#df-workbench-tab-improve")).ToBeEnabledAsync();
        await Expect(_page.Locator("#df-workbench-tab-repair")).ToBeDisabledAsync();
        await Expect(_page.Locator("#df-workbench-tab-source")).ToBeDisabledAsync();
        await _page.Locator("#df-workbench-stage-review").ClickAsync();
        var review = _page.Locator("#df-workbench-panel-review");
        await Expect(review.Locator(".df-review-step-list")).ToHaveAttributeAsync("role", "list");
        await Expect(review.Locator("[role='listitem']")).ToHaveCountAsync(1);
        Assert.Equal("BUTTON", await review.Locator(".df-review-step-row").EvaluateAsync<string>("row => row.tagName"));
        Assert.Null(await review.Locator(".df-review-step-row").GetAttributeAsync("role"));
        await Expect(review.Locator(".df-review-step-row")).ToHaveCountAsync(1);
        await Expect(review.Locator(".df-review-step-editor")).ToContainTextAsync("Step 1");
        foreach (var action in new[] { "Move up", "Move down", "Remove step", "Record more steps" })
            await Expect(review.GetByRole(AriaRole.Button, new() { Name = action, Exact = true })).ToBeVisibleAsync();
        await review.Locator(".df-review-step-details > summary").ClickAsync();
        await Expect(review.GetByRole(AriaRole.Button, new() { Name = "Save step", Exact = true })).ToBeVisibleAsync();
        var expectedResult = review.Locator(".df-expected-result-editor");
        await Expect(expectedResult).ToBeVisibleAsync();
        Assert.False(await expectedResult.EvaluateAsync<bool>("details => details.open"));
        Assert.DoesNotContain(
            "hard outcome check",
            await review.TextContentAsync(),
            StringComparison.OrdinalIgnoreCase);
        await _page.Locator("#df-workbench-tab-improve").ClickAsync();
        var improve = _page.Locator("#df-workbench-panel-improve");
        await Expect(improve.GetByRole(AriaRole.Button, new() { Name = "Scan test", Exact = true })).ToBeVisibleAsync();
        var improveAgentAction = improve.Locator(".df-agent-action");
        await Expect(improveAgentAction).ToContainTextAsync("Improve this test with your agent");
        Assert.Equal("BUTTON", await improveAgentAction.EvaluateAsync<string>("element => element.tagName"));
        var scanOptions = improve.Locator(".df-tool-details");
        await Expect(scanOptions).ToHaveCountAsync(1);
        await _page.Locator("#df-workbench-stage-run").ClickAsync();
        var runPanel = _page.Locator("#df-workbench-panel-run");
        await Expect(runPanel).ToBeVisibleAsync();
        await runPanel.GetByRole(AriaRole.Button, new() { Name = "Check run", Exact = true }).ClickAsync();
        await Expect(runPanel).ToContainTextAsync("Ready to run; verification limited");
        await Expect(runPanel).ToContainTextAsync("Verification notes");
        await Expect(runPanel).ToContainTextAsync("can run, but the result will not be marked independently verified");
        Assert.Equal(1, preflightRequests);
        Assert.Equal(0, replayRequests);

        await _page.GetByRole(AriaRole.Button, new() { Name = "Review and start", Exact = true }).ClickAsync();
        await _page.GetByRole(AriaRole.Dialog).GetByRole(
            AriaRole.Button,
            new() { Name = "Run test", Exact = true }).ClickAsync();
        await Expect(_page.Locator("#df-workbench-stage-results")).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("df-active"));
        Assert.Equal(1, startRequests);
        Assert.True(journalRequests >= 1);
        Assert.True(statusRequests >= 1);
        Assert.True(recoveryKeyMatched);
        Assert.True(recoveryJournalAttempts >= 3);

        await _page.GetByRole(AriaRole.Button, new() { Name = "Run again", Exact = true }).ClickAsync();
        await Expect(_page.Locator("#df-workbench-panel-run")).ToContainTextAsync("Ready to run; verification limited");
        Assert.True(preflightKeys.Count >= 2);
        Assert.NotEqual(preflightKeys[0], preflightKeys[^1]);
        await OpenLegacyQuickReplayAsync();
        await _page.GetByRole(AriaRole.Dialog).GetByRole(
            AriaRole.Button,
            new() { Name = "Legacy quick replay" }).ClickAsync();
        await Expect(_page.Locator("#df-timeline-title-text")).ToContainTextAsync("Replay passed");
        await Expect(_page.Locator("#df-timeline .df-step-passed")).ToHaveCountAsync(1);
        await Expect(_page.Locator("#df-timeline")).ToContainTextAsync("Tap AddButton");
        Assert.Equal(1, replayRequests);

        await OpenWorkbenchAsync();
        await _page.Locator("#df-workbench-stage-review").ClickAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Record more steps", Exact = true }).ClickAsync();
        await Expect(_page.Locator("#df-workbench-panel-steps")).ToBeVisibleAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Stop recording", Exact = true }).ClickAsync();
        await Expect(_page.Locator("#df-workbench-panel-review")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-workbench-panel-review .df-review-step-list")).ToHaveAttributeAsync("role", "list");
        await Expect(_page.Locator("#df-workbench-panel-review .df-review-step-row")).ToHaveCountAsync(2);
        await Expect(_page.Locator("#df-workbench-panel-review")).ToContainTextAsync("Tap AddButton");
        await Expect(_page.Locator("#df-workbench-panel-review")).ToContainTextAsync("Tap AnotherButton");
        var rows = _page.Locator("#df-workbench-panel-review .df-review-step-row");
        await rows.First.FocusAsync();
        await _page.Keyboard.PressAsync("ArrowDown");
        await Expect(rows.Nth(1)).ToBeFocusedAsync();
        Assert.Equal("BUTTON", await _page.EvaluateAsync<string>("() => document.activeElement && document.activeElement.tagName"));
        await Expect(_page.Locator("#df-workbench-panel-review .df-review-step-editor")).ToContainTextAsync("Step 2");
        await rows.Nth(1).ClickAsync();
        await Expect(rows.Nth(1)).ToBeFocusedAsync();
        Assert.True(await _page.Locator("#df-workbench-panel-review .df-expected-result-editor")
            .EvaluateAsync<bool>("details => details.open"));
        await CloseWorkbenchAsync();

        await OpenWorkbenchAsync();
        await _page.Locator("#df-workbench-stage-goal").ClickAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Open saved test", Exact = true }).ClickAsync();
        var fileChooser = await _page.RunAndWaitForFileChooserAsync(
            () => _page.Locator("#df-saved-test-file").ClickAsync());
        await fileChooser.SetFilesAsync(new FilePayload
        {
            Name = "local.md",
            MimeType = "text/markdown",
            Buffer = Encoding.UTF8.GetBytes(localMarkdown),
        });
        await Expect(_page.Locator("#df-workbench-strip")).ToContainTextAsync("local.md");
    }

    [LiveInspectorFact]
    public async Task OpacityEditorUsesBoundedFractionalKeyboardStep()
    {
        await _page.GotoAsync(BaseUrl);
        var header = _page.Locator("[data-automationId='HeaderLabel']");
        await Expect(header).ToBeAttachedAsync();
        var elementId = await header.GetAttributeAsync("data-id");
        Assert.False(string.IsNullOrEmpty(elementId));
        var treeRow = _page.Locator($".df-tree-node[data-tree-id='{elementId}']");
        await Expect(treeRow).ToBeAttachedAsync();
        await treeRow.ClickAsync();
        await Expect(_page.Locator("#df-props-pane")).ToBeVisibleAsync();

        var opacity = _page
            .Locator(".df-prop-row")
            .Filter(new() { HasText = "Opacity" })
            .Locator("input[type='number']");
        await Expect(opacity).ToHaveAttributeAsync("min", "0");
        await Expect(opacity).ToHaveAttributeAsync("max", "1");
        await Expect(opacity).ToHaveAttributeAsync("step", "0.05");
    }

    [LiveInspectorFact]
    public async Task PropertyGridDistinguishesUnsetValuesAndReportsErrorsInline()
    {
        var setPropertyRequests = 0;
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));
        await _page.RouteAsync("**/api/getProperties", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """
                {
                  "ok": true,
                  "supported": true,
                  "properties": [
                    { "name": "BackgroundColor", "kind": "color", "value": null, "writable": true, "persistable": true },
                    { "name": "FontSize", "kind": "number", "value": "28", "writable": true, "persistable": true, "min": 0.1 },
                    { "name": "FontAttributes", "kind": "enum", "value": "None", "writable": true, "persistable": true, "choices": ["None", "Bold", "Italic"] },
                    { "name": "Opacity", "kind": "number", "value": "1", "writable": true, "persistable": true, "min": 0, "max": 1, "step": 0.05 },
                    { "name": "IsVisible", "kind": "bool", "value": "true", "writable": true, "persistable": true },
                    { "name": "IsEnabled", "kind": "bool", "value": "true", "writable": true, "persistable": true }
                  ]
                }
                """,
        }));
        await _page.RouteAsync("**/api/setProperty", route =>
        {
            Interlocked.Increment(ref setPropertyRequests);
            return route.AbortAsync();
        });

        await _page.GotoAsync(BaseUrl);
        var header = _page.Locator("[data-automationId='HeaderLabel']");
        var elementId = await header.GetAttributeAsync("data-id");
        Assert.False(string.IsNullOrEmpty(elementId));
        await _page.Locator($".df-tree-node[data-tree-id='{elementId}']").ClickAsync();
        await Expect(_page.Locator("#df-prop-filter")).ToBeVisibleAsync();
        await Expect(_page.Locator(".df-prop-row").Filter(new() { HasText = "BackgroundColor" }).Locator(".df-color-unset-label"))
            .ToHaveTextAsync("Unset");

        await _page.Locator("#df-prop-filter").FillAsync("Font");
        Assert.Equal(2, await _page.Locator(".df-prop-row:visible").CountAsync());
        await _page.Locator("#df-prop-filter").FillAsync("not-a-property");
        await Expect(_page.Locator(".df-prop-no-results")).ToBeVisibleAsync();
        await Expect(_page.Locator(".df-prop-no-results")).ToContainTextAsync("No matching properties");
        await _page.Locator("#df-prop-filter").FillAsync("");

        var opacity = _page.Locator(".df-prop-row").Filter(new() { HasText = "Opacity" }).Locator("input[type='number']");
        await opacity.FillAsync("2");
        await opacity.DispatchEventAsync("change");
        await Expect(opacity).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(_page.Locator(".df-prop-row").Filter(new() { HasText = "Opacity" }).Locator(".df-prop-error"))
            .ToContainTextAsync("valid Opacity");

        var fontSize = _page.Locator(".df-prop-row").Filter(new() { HasText = "FontSize" }).Locator("input[type='number']");
        await Expect(fontSize).ToHaveAttributeAsync("min", "0.1");
        await fontSize.FillAsync("-5");
        await fontSize.DispatchEventAsync("change");
        await Expect(fontSize).ToHaveAttributeAsync("aria-invalid", "true");
        Assert.Equal(0, setPropertyRequests);

        await fontSize.FillAsync("29");
        await fontSize.DispatchEventAsync("change");
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("Could not reach the running app");
        Assert.Equal(1, setPropertyRequests);

        await _page.SetViewportSizeAsync(420, 820);
        await _page.WaitForTimeoutAsync(100);
        var overflow = await _page.Locator("#df-props").EvaluateAsync<double>(
            "element => element.scrollWidth - element.clientWidth");
        Assert.True(overflow <= 1, $"Expected no horizontal Properties overflow, got {overflow}px.");
    }

    [LiveInspectorFact]
    public async Task PropertyGridUsesAgentDescriptorsForEditorsAndWritability()
    {
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));
        await _page.RouteAsync("**/api/getProperties", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """
                {
                  "ok": true,
                  "supported": true,
                  "properties": [
                    { "name": "FontAttributes", "kind": "enum", "value": "Bold", "writable": true, "persistable": true, "choices": ["None", "Bold", "Italic"] },
                    { "name": "Opacity", "kind": "number", "value": "0.5", "writable": true, "persistable": true, "min": 0, "max": 1, "step": 0.05 },
                    { "name": "WidthRequest", "kind": "number", "value": "100", "writable": false, "persistable": false }
                  ]
                }
                """,
        }));

        await _page.GotoAsync(BaseUrl);
        var header = _page.Locator("[data-automationId='HeaderLabel']");
        var elementId = await header.GetAttributeAsync("data-id");
        Assert.False(string.IsNullOrEmpty(elementId));
        await _page.Locator($".df-tree-node[data-tree-id='{elementId}']").ClickAsync();

        var fontAttributes = _page.Locator(".df-prop-row").Filter(new() { HasText = "FontAttributes" });
        await Expect(fontAttributes.Locator("select")).ToHaveValueAsync("Bold");
        await Expect(fontAttributes.Locator("option")).ToHaveCountAsync(3);

        var opacity = _page.Locator(".df-prop-row").Filter(new() { HasText = "Opacity" }).Locator("input[type='number']");
        await Expect(opacity).ToHaveAttributeAsync("min", "0");
        await Expect(opacity).ToHaveAttributeAsync("max", "1");
        await Expect(opacity).ToHaveAttributeAsync("step", "0.05");

        var width = _page.Locator(".df-prop-row").Filter(new() { HasText = "WidthRequest" });
        await Expect(width.Locator("input")).ToBeDisabledAsync();
        await Expect(width.Locator(".df-prop-source")).ToHaveCountAsync(0);
    }

    [LiveInspectorFact]
    public async Task ApplyToXamlStaysBesideFieldAndTracksRuntimeDirtyState()
    {
        var setRequests = 0;
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));
        await _page.RouteAsync("**/api/getProperties", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """
                {
                  "ok": true,
                  "supported": true,
                  "properties": [
                    { "name": "Text", "kind": "text", "value": "My Todos", "writable": true, "persistable": true }
                  ]
                }
                """,
        }));
        await _page.RouteAsync("**/api/setProperty", route =>
        {
            Interlocked.Increment(ref setRequests);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = "{\"ok\":true}",
            });
        });

        await _page.SetViewportSizeAsync(420, 820);
        await _page.GotoAsync(BaseUrl);
        var header = _page.Locator("[data-automationId='HeaderLabel']");
        var elementId = await header.GetAttributeAsync("data-id");
        Assert.False(string.IsNullOrEmpty(elementId));
        await _page.Locator("#df-toggle-tree").ClickAsync();
        await _page.Locator($".df-tree-node[data-tree-id='{elementId}']").ClickAsync();

        var row = _page.Locator(".df-prop-row").Filter(new() { HasText = "Text" });
        var editor = row.Locator("textarea");
        var source = row.Locator(".df-prop-source");
        await _page.EvaluateAsync("""
            () => {
                const originalFetch = window.fetch;
                window.__persistRequests = 0;
                window.__persistValue = null;
                window.fetch = async (url, options) => {
                    if (typeof url === 'string' && url.endsWith('/api/persistProperty')) {
                        const body = JSON.parse(options?.body || '{}');
                        window.__persistRequests++;
                        window.__persistValue = body.value;
                        await new Promise(resolve => setTimeout(resolve, 750));
                        return new Response('{"ok":true,"file":"MainPage.xaml"}', {
                            status: 200,
                            headers: { 'Content-Type': 'application/json' },
                        });
                    }
                    return originalFetch(url, options);
                };
            }
            """);
        await Expect(source).ToBeVisibleAsync();
        await Expect(source).ToBeDisabledAsync();
        await Expect(source).ToHaveAttributeAsync("data-state", "clean");
        await Expect(source.Locator("use")).ToHaveAttributeAsync("href", "#i-source");

        var editorBounds = await editor.BoundingBoxAsync();
        var sourceBounds = await source.BoundingBoxAsync();
        Assert.NotNull(editorBounds);
        Assert.NotNull(sourceBounds);
        Assert.True(sourceBounds.X >= editorBounds.X + editorBounds.Width + 3,
            $"Expected Apply to XAML beside the editor, editor right={editorBounds.X + editorBounds.Width}, button left={sourceBounds.X}");
        Assert.True(Math.Abs(
            (editorBounds.Y + editorBounds.Height / 2) - (sourceBounds.Y + sourceBounds.Height / 2)) <= 1,
            "Expected Apply to XAML vertically aligned with the editor.");

        await editor.FillAsync("Updated title");
        await Expect(source).ToHaveAttributeAsync("data-state", "pending");
        await Expect(source).ToBeDisabledAsync();
        await Expect(source.Locator("use")).ToHaveAttributeAsync("href", "#i-edit");
        Assert.Equal(0, await _page.EvaluateAsync<int>("() => window.__persistRequests"));

        await editor.BlurAsync();
        await Expect(source).ToHaveAttributeAsync("data-state", "dirty");
        await Expect(source).ToBeEnabledAsync();
        await Expect(source.Locator("use")).ToHaveAttributeAsync("href", "#i-save");
        Assert.Equal(1, setRequests);

        await source.ClickAsync();
        await Expect(source).ToHaveAttributeAsync("data-state", "busy");
        await Expect(source).ToBeDisabledAsync();
        await Expect(editor).ToBeDisabledAsync();
        await Expect(source).ToHaveAttributeAsync("data-state", "saved");
        await Expect(source).ToBeDisabledAsync();
        await Expect(editor).ToBeEnabledAsync();
        await Expect(source.Locator("use")).ToHaveAttributeAsync("href", "#i-check");
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("Saved Text to MainPage.xaml");
        Assert.Equal(1, await _page.EvaluateAsync<int>("() => window.__persistRequests"));
        Assert.Equal("Updated title", await _page.EvaluateAsync<string>("() => window.__persistValue"));

        await _page.WaitForTimeoutAsync(4200);
        await Expect(source).ToHaveAttributeAsync("data-state", "saved");
        await Expect(source).ToBeDisabledAsync();
    }

    [LiveInspectorFact]
    public async Task ReadOnlyPropertyFilterRemainsUsable()
    {
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":false,\"heldByOther\":true,\"label\":\"Other Inspector\"}",
        }));

        await _page.GotoAsync(BaseUrl);
        var header = _page.Locator("[data-automationId='HeaderLabel']");
        var id = await header.GetAttributeAsync("data-id");
        await _page.Locator($".df-tree-node[data-tree-id='{id}']").ClickAsync();

        await Expect(_page.Locator("#df-prop-filter")).ToBeEnabledAsync();
        await Expect(_page.Locator(".df-prop-row input[type='number']").First).ToBeDisabledAsync();
        await _page.Locator("#df-prop-filter").FillAsync("Font");
        Assert.Equal(2, await _page.Locator(".df-prop-row:visible").CountAsync());
    }

    [LiveInspectorFact]
    public async Task PassiveRecordingObserverClearsWhenRecordingEnds()
    {
        var ownsLease = true;
        var recording = false;
        var endedStatusObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = ownsLease
                ? "{\"youAreWriter\":true,\"heldByOther\":false}"
                : "{\"youAreWriter\":false,\"heldByOther\":true,\"label\":\"Other Inspector\"}",
        }));
        await _page.RouteAsync("**/api/flows/record/start", route =>
        {
            recording = true;
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = "{\"ok\":true,\"recordingId\":\"passive\",\"name\":\"passive\"}",
            });
        });
        await _page.RouteAsync("**/api/flows/record/status", route =>
        {
            if (!recording) endedStatusObserved.TrySetResult();
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = recording
                    ? "{\"ok\":true,\"recording\":true,\"recordingId\":\"passive\",\"name\":\"passive\",\"steps\":1}"
                    : "{\"ok\":true,\"recording\":false,\"steps\":0}",
            });
        });

        await _page.GotoAsync(BaseUrl);
        await StartManagedRecordingAsync("Observe a recording that ends in another session.");
        await Expect(_page.Locator("#df-timeline")).ToBeVisibleAsync();

        ownsLease = false;
        recording = false;
        await _page.EvaluateAsync("document.dispatchEvent(new Event('visibilitychange'))");
        await endedStatusObserved.Task.WaitAsync(TimeSpan.FromSeconds(8));

        await Expect(_page.Locator("#df-timeline")).ToBeHiddenAsync();
        await Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Stop recording", Exact = true })).ToHaveCountAsync(0);
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("ended in another session");
    }

    [LiveInspectorFact]
    public async Task HostedRecordingObserverDiscoversExternalRecordingChanges()
    {
        var statusObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));
        await _page.RouteAsync("**/api/flows/record/status", route =>
        {
            statusObserved.TrySetResult();
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = "{\"ok\":true,\"recording\":true,\"recordingId\":\"external-recording\",\"name\":\"external\",\"steps\":2}",
            });
        });

        await _page.SetViewportSizeAsync(1100, 800);
        var trustedEmbedUrl = TrustedEmbedUrl();
        await _page.SetContentAsync($$"""
            <iframe id="hosted" style="width:1000px;height:700px;border:0" src="{{trustedEmbedUrl}}#devflowBridge=test-bridge"></iframe>
            <script>
              window.addEventListener('message', function (e) {
                const d = e.data;
                if (!d || d.bridgeId !== 'test-bridge') return;
                if (d.type === 'devflow:ready') {
                  e.source.postMessage({
                    type: 'devflow:host',
                    v: 1,
                    bridgeId: 'test-bridge',
                    hostId: 'canvas',
                    capabilities: []
                  }, '*');
                }
              });
              window.reportRecordingChange = function () {
                document.getElementById('hosted').contentWindow.postMessage({
                  type: 'devflow:recordingChanged',
                  v: 1,
                  bridgeId: 'test-bridge'
                }, '*');
              };
            </script>
            """);

        var frame = _page.FrameLocator("#hosted");
        await Expect(frame.Locator(".devflow-element").First).ToBeAttachedAsync();
        await Expect(frame.Locator("body")).ToHaveAttributeAsync("data-host-kind", "canvas");
        await frame.Locator("#df-toggle-workbench").ClickAsync();
        await frame.Locator("#df-workbench-stage-goal").ClickAsync();
        await frame.Locator("#df-goal-input").FillAsync("Observe an externally controlled recording.");

        await _page.EvaluateAsync("window.reportRecordingChange()");
        await statusObserved.Task.WaitAsync(TimeSpan.FromSeconds(8));
        await Expect(frame.Locator("#df-timeline")).ToBeVisibleAsync();

        await frame.Locator("#df-workbench-stage-record").ClickAsync();
        await Expect(frame.GetByRole(AriaRole.Button, new() { Name = "Stop recording", Exact = true })).ToBeVisibleAsync();
        await Expect(frame.Locator("#df-workbench-panel-steps")).ToContainTextAsync("2 steps captured");
    }

    [LiveInspectorFact]
    public async Task MissingRecordingStopClearsStaleLocalState()
    {
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));
        await _page.RouteAsync("**/api/flows/record/start", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"ok\":true,\"recordingId\":\"stale\",\"name\":\"stale\"}",
        }));
        await _page.RouteAsync("**/api/flows/record/status", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"ok\":true,\"recording\":true,\"recordingId\":\"stale\",\"name\":\"stale\",\"steps\":0}",
        }));
        await _page.RouteAsync("**/api/flows/record/stop", route => route.FulfillAsync(new()
        {
            Status = 400,
            ContentType = "application/json",
            Body = "{\"ok\":false,\"error\":\"No recording is active for this app.\"}",
        }));

        await _page.GotoAsync(BaseUrl);
        await StartManagedRecordingAsync("Clear stale recording state when the recording is missing.");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Stop recording", Exact = true }).ClickAsync();

        await Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Stop recording", Exact = true })).ToHaveCountAsync(0);
        await Expect(_page.Locator("#df-timeline")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("already ended in another session");
    }

    [LiveInspectorFact]
    public async Task TransientMenusRemainAvailableOverInspectorSurfaces()
    {
        await _page.SetViewportSizeAsync(420, 820);
        await _page.GotoAsync(BaseUrl);
        if (!await _page.Locator("#df-tree-pane").IsVisibleAsync())
            await _page.Locator("#df-toggle-tree").ClickAsync();
        await Expect(_page.Locator("#df-tree-pane")).ToBeVisibleAsync();
        await _page.Locator(".df-tree-node").Last.ClickAsync();
        await Expect(_page.Locator("#df-props-pane")).ToBeVisibleAsync();

        await _page.Locator("#df-more").ClickAsync();
        await Expect(_page.Locator("#df-toolbar-overflow")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-props-pane")).ToBeVisibleAsync();

        await _page.Locator("#df-toggle-dock").ClickAsync();
        await Expect(_page.Locator("#df-dock")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-toolbar-overflow")).ToBeHiddenAsync();

        await _page.Locator("#df-more").ClickAsync();
        await Expect(_page.Locator("#df-toolbar-overflow")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-dock")).ToBeVisibleAsync();
        await _page.Locator("#df-more").ClickAsync();

        await _page.SetViewportSizeAsync(420, 820);
        await _page.WaitForTimeoutAsync(100);
        await _page.Locator("#df-toggle-tree").ClickAsync();
        await Expect(_page.Locator("#df-tree-pane")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-dock")).ToBeHiddenAsync();
    }

    [LiveInspectorFact]
    public async Task FilesTabExplainsEmptyAppDataRoot()
    {
        await _page.RouteAsync("**/api/files/roots", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """
                {"ok":true,"roots":{"roots":[{"id":"appData","displayName":"App data","kind":"appData","isReadOnly":false,"isPersistent":true,"isUserVisible":false}]}}
                """,
        }));
        await _page.RouteAsync("**/api/files/list", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"files":{"root":"appData","path":"","entries":[]}}""",
        }));

        await OpenDataDockAsync();
        await _page.Locator("[data-tab='files']").ClickAsync();

        Assert.Equal("App data", await _page.Locator("#df-files-root option").First.InnerTextAsync());
        await Expect(_page.Locator("#df-dock-action-strip")).ToContainTextAsync("Private app storage · persistent");
        await Expect(_page.Locator("#df-dock-action-strip")).ToContainTextAsync("Browse only");
        await Expect(_page.Locator(".df-files-empty")).ToContainTextAsync("App data is empty.");
        await Expect(_page.Locator(".df-files-empty")).ToContainTextAsync("In-memory data and Preferences are not files.");
    }

    [LiveInspectorFact]
    public async Task AlertsTabDetectsAndDismissesExplicitButton()
    {
        var dismissRequests = 0;
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));
        await _page.RouteAsync("**/api/alerts", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = Volatile.Read(ref dismissRequests) == 0
                ? "{\"ok\":true,\"supported\":true,\"alert\":{\"title\":\"Permission\",\"buttons\":[{\"label\":\"Allow\"},{\"label\":\"Deny\"}]}}"
                : "{\"ok\":true,\"supported\":true,\"alert\":null}",
        }));
        await _page.RouteAsync("**/api/alerts/dismiss", route =>
        {
            using var body = JsonDocument.Parse(route.Request.PostData ?? "{}");
            Assert.Equal("Allow", body.RootElement.GetProperty("buttonLabel").GetString());
            Interlocked.Increment(ref dismissRequests);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = "{\"ok\":true,\"supported\":true,\"dismissed\":true}",
            });
        });

        await _page.GotoAsync(BaseUrl);
        await OpenDataDockAsync();
        await _page.Locator("[data-tab='alerts']").ClickAsync();
        await Expect(_page.Locator("#df-dock-action-strip")).ToContainTextAsync("Permission");
        await _page.Locator("[data-alert-action='dismiss']").Filter(new() { HasText = "Allow" }).ClickAsync();

        Assert.Equal(1, dismissRequests);
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("Chose Allow");
        await Expect(_page.Locator("#df-dock-body")).ToContainTextAsync("No native alert is visible");
    }

    [LiveInspectorFact]
    public async Task StaleAlertResponseDoesNotOverwriteAnotherTab()
    {
        var dismissStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDismiss = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));
        await _page.RouteAsync("**/api/alerts", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"supported":true,"revision":"old","alert":{"title":"Permission","buttons":[{"label":"Allow"}]}}""",
        }));
        await _page.RouteAsync("**/api/alerts/dismiss", async route =>
        {
            dismissStarted.TrySetResult(true);
            await releaseDismiss.Task;
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"ok":false,"supported":true,"revision":"new","error":"changed","alert":{"title":"Delete item?","buttons":[{"label":"Delete"}]}}""",
            });
        });
        await _page.RouteAsync("**/api/logs", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"logs":[{"t":"2026-08-10T12:00:00Z","l":"Info","m":"still on logs"}]}""",
        }));

        await OpenDataDockAsync();
        await _page.Locator("[data-tab='alerts']").ClickAsync();
        await _page.Locator("[data-alert-action='dismiss']").ClickAsync();
        await dismissStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _page.Locator("[data-tab='logs']").ClickAsync();
        releaseDismiss.TrySetResult(true);

        await Expect(_page.Locator("[data-tab='logs']")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(_page.Locator("#df-dock-body")).ToContainTextAsync("still on logs");
        await Expect(_page.Locator("#df-dock-body")).Not.ToContainTextAsync("Delete item");
        await Expect(_page.Locator("#df-dock-action-strip")).ToBeHiddenAsync();
    }

    [LiveInspectorFact]
    public async Task WebViewJavaScriptRequiresExplicitConfirmation()
    {
        var evalRequests = 0;
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));
        await _page.RouteAsync("**/api/cdp/webviews", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"ok\":true,\"webviews\":{\"webviews\":[{\"id\":\"web-1\",\"title\":\"App\"},{\"id\":\"web-2\",\"title\":\"Other\"}]}}",
        }));
        await _page.RouteAsync("**/api/cdp/eval", route =>
        {
            using var body = JsonDocument.Parse(route.Request.PostData ?? "{}");
            Assert.Equal("web-1", body.RootElement.GetProperty("webviewId").GetString());
            Interlocked.Increment(ref evalRequests);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = "{\"ok\":true,\"result\":{\"value\":\"App\"}}",
            });
        });

        await _page.GotoAsync(BaseUrl);
        await OpenDataDockAsync();
        await _page.Locator("[data-tab='webview']").ClickAsync();
        await _page.Locator("#df-cdp-expr").FillAsync("document.title");
        await _page.Locator("#df-cdp-expr").PressAsync("Enter");

        var dialog = _page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToContainTextAsync("Run this JavaScript");
        Assert.Equal(0, evalRequests);
        await _page.Locator("#df-dock-action-strip select").EvaluateAsync("""
            select => {
                select.value = 'web-2';
                select.dispatchEvent(new Event('change', { bubbles: true }));
            }
            """);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Run JavaScript" }).PressAsync("Enter");
        await Expect(_page.Locator("#df-cdp-out")).ToContainTextAsync("App");
        Assert.Equal(1, evalRequests);
        await Expect(dialog).ToHaveCountAsync(0);
    }

        [LiveInspectorFact]
        public async Task DeviceTabExplainsRestrictedBatteryCapability()
        {
                await _page.RouteAsync("**/api/device", route => route.FulfillAsync(new()
                {
                        Status = 200,
                        ContentType = "application/json",
                        Body = """
                                {
                                    "ok": true,
                                    "device": {
                                        "device-info": { "platform": "Android" },
                                        "battery": {
                                            "success": false,
                                            "error": "Failed to get battery info: You need to declare using the permission: android.permission.BATTERY_STATS",
                                            "reason": "missing_permission",
                                            "details": { "platform": "Android", "permission": "android.permission.BATTERY_STATS" }
                                        }
                                    }
                                }
                                """,
                }));

                await OpenDataDockAsync();
                await _page.Locator("[data-tab='device']").ClickAsync();
                var body = _page.Locator("#df-dock-body");
                await Expect(body).ToContainTextAsync("privileged Android capability");
                await Expect(body).Not.ToContainTextAsync("declare using the permission");
                await Expect(body).Not.ToContainTextAsync("BATTERY_STATS");
        }

    [LiveInspectorFact]
    public async Task DeviceTabIntegratesTheHostControlSurface()
    {
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));
        await _page.RouteAsync("**/api/device", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"device":{"device-info":{"platform":"Android","model":"Pixel 8"}}}""",
        }));
        await _page.RouteAsync("**/api/device/host", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = DeviceHostPayload,
        }));
        await _page.RouteAsync("**/api/device/recording*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"recording":{"deviceId":"android:emulator:pixel","isRecording":false,"hasArtifact":false}}""",
        }));

        await _page.GotoAsync(BaseUrl);
        await OpenDataDockAsync();
        await _page.Locator("[data-tab='device']").ClickAsync();

        var body = _page.Locator("#df-dock-body");
        await Expect(body).ToContainTextAsync("Pixel 8");
        await Expect(body).ToContainTextAsync("Direct device input");
        await Expect(body).ToContainTextAsync("Exact coordinate input");
        await Expect(body).ToContainTextAsync("Device data");
        await Expect(_page.Locator("#df-dock-action-strip select[aria-label='Managed device']")).ToHaveValueAsync("android:emulator:pixel");
        await Expect(_page.Locator("[data-command-id='device.reveal']")).ToBeEnabledAsync();
        await Expect(_page.Locator("[data-command-id='device.screenshot']")).ToBeEnabledAsync();
        await Expect(_page.Locator("[data-command-id='device.recording-start']")).ToBeEnabledAsync();
        await Expect(_page.Locator("[data-command-id='device.type-text']")).ToBeEnabledAsync();

        await _page.Locator("[data-command-id='device.create']").ClickAsync(new() { Force = true });
        await Expect(body).ToContainTextAsync("Create an explicit virtual device");
        await Expect(_page.Locator("[data-command-id='device.create-submit']")).ToBeEnabledAsync();
    }

    [LiveInspectorFact]
    public async Task DeviceDangerZoneRequiresExactDeviceConfirmation()
    {
        string? requestBody = null;
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));
        await _page.RouteAsync("**/api/device", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"device":{"device-info":{"platform":"Android"}}}""",
        }));
        await _page.RouteAsync("**/api/device/host", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = DeviceHostPayload,
        }));
        await _page.RouteAsync("**/api/device/recording*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"recording":{"deviceId":"android:emulator:pixel","isRecording":false}}""",
        }));
        await _page.RouteAsync("**/api/device/control", route =>
        {
            requestBody = route.Request.PostData;
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"success":true}""",
            });
        });

        await _page.GotoAsync(BaseUrl);
        await OpenDataDockAsync();
        await _page.Locator("[data-tab='device']").ClickAsync();
        await _page.Locator("[data-command-id='device.delete']").ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog);
        var confirmation = dialog.GetByRole(AriaRole.Textbox, new()
        {
            Name = "Type delete android:emulator:pixel to confirm",
            Exact = true,
        });
        var delete = dialog.GetByRole(AriaRole.Button, new() { Name = "Delete device", Exact = true });
        await Expect(delete).ToBeDisabledAsync();
        await confirmation.FillAsync("delete something-else");
        await Expect(delete).ToBeDisabledAsync();
        await confirmation.FillAsync("delete android:emulator:pixel");
        await Expect(delete).ToBeEnabledAsync();
        await delete.ClickAsync();

        await Expect(dialog).ToHaveCountAsync(0);
        Assert.NotNull(requestBody);
        using var request = JsonDocument.Parse(requestBody);
        Assert.Equal("delete", request.RootElement.GetProperty("action").GetString());
        Assert.True(request.RootElement.GetProperty("confirm").GetBoolean());
        Assert.Equal(
            "delete android:emulator:pixel",
            request.RootElement.GetProperty("confirmationText").GetString());
    }

    private const string DeviceHostPayload = """
        {
          "available": true,
          "devices": [{
            "id": "android:emulator:pixel",
            "platform": "android",
            "provider": "android-emulator",
            "nativeId": "emulator-5554",
            "name": "Pixel 8",
            "state": "booted",
            "isAvailable": true,
            "isBooted": true,
            "osVersion": "35",
            "runtimeId": "android-35",
            "runtimeName": "Android 15",
            "deviceTypeId": "pixel_8",
            "deviceTypeName": "Pixel 8",
            "architecture": "x86_64",
            "agentId": "agent",
            "display": {
              "pointWidth": 412,
              "pointHeight": 915,
              "scale": 2.625,
              "orientation": "portrait"
            },
            "capabilities": {
              "boot": true,
              "shutdown": true,
              "restart": true,
              "erase": true,
              "delete": true,
              "reveal": true,
              "tap": true,
              "longPress": true,
              "swipe": true,
              "text": true,
              "key": true,
              "button": true,
              "rotate": true,
              "screenshot": true,
              "liveStream": false,
              "recording": true
            }
          }],
          "runtimes": [{
            "id": "android-35",
            "name": "Android 15",
            "version": "35",
            "platform": "android",
            "isAvailable": true,
            "supportedDeviceTypeIds": ["pixel_8"]
          }],
          "deviceTypes": [{
            "id": "pixel_8",
            "name": "Pixel 8",
            "platform": "android"
          }],
          "diagnostics": [{
            "platform": "android",
            "ready": true,
            "checks": []
          }]
        }
        """;

    [LiveInspectorFact]
    public async Task NetworkTabRefreshesAutomaticallyAndKeepsDetailOpen()
    {
        var networkCalls = 0;
        var detailCalls = 0;
        await _page.RouteAsync("**/api/network", async route =>
        {
            var call = Interlocked.Increment(ref networkCalls);
            var marker = call == 1 ? "first" : "second";
            if (call == 1) await Task.Delay(3000);
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = $$"""
                    {"ok":true,"requests":[{"id":"request-1","method":"GET","url":"https://example.test/{{marker}}","statusCode":200,"durationMs":12}]}
                    """,
            });
        });
        await _page.RouteAsync("**/api/network/detail", async route =>
        {
            var call = Interlocked.Increment(ref detailCalls);
            await Task.Delay(500);
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = call == 1
                    ? """
                    {"ok":true,"request":{"id":"request-1","method":"GET","url":"https://example.test/second","statusCode":200,"statusText":"OK","durationMs":12}}
                    """
                    : """{"ok":false,"error":"temporary detail failure"}""",
            });
        });

        await OpenDataDockAsync();
        await _page.Locator("[data-tab='network']").ClickAsync();

        await Expect(_page.Locator("#df-dock-body")).ToContainTextAsync(
            "https://example.test/second",
            new() { Timeout = 6000 });
        await _page.WaitForTimeoutAsync(1500);
        await Expect(_page.Locator("#df-dock-body")).Not.ToContainTextAsync("https://example.test/first");
        Assert.True(networkCalls >= 2, $"Expected an automatic network refresh, got {networkCalls} request(s).");
        await Expect(_page.Locator("#df-dock-meta")).ToContainTextAsync("live");

        await Expect(_page.Locator("#df-attach-data")).ToBeEnabledAsync();
        await _page.Locator("#df-dock-body .df-data-link").First.ClickAsync();
        await Expect(_page.Locator("#df-attach-data")).ToBeDisabledAsync();
        await Expect(_page.Locator("#df-dock-action-strip")).ToContainTextAsync("Requests");
        await Expect(_page.Locator("#df-attach-data")).ToBeEnabledAsync();
        await _page.WaitForTimeoutAsync(2500);
        await Expect(_page.Locator("#df-dock-action-strip")).ToContainTextAsync("Requests");
        await Expect(_page.Locator("#df-dock-meta")).ToContainTextAsync("live paused");

        await _page.Locator("#df-dock-refresh").ClickAsync();
        await Expect(_page.Locator("#df-dock-action-strip")).ToContainTextAsync("https://example.test/second");
        await Expect(_page.Locator("#df-dock-body")).Not.ToContainTextAsync("temporary detail failure");
        await Expect(_page.Locator("#df-dock-meta")).ToContainTextAsync("stale");

        await _page.Locator("#df-dock-action-strip")
            .GetByRole(AriaRole.Button, new() { Name = "Requests", Exact = true })
            .ClickAsync();
        await Expect(_page.Locator("#df-dock-body")).ToContainTextAsync("https://example.test/second");
        Assert.True(await _page.Locator("#df-dock-body .df-data-link").First.EvaluateAsync<bool>(
            "button => button === document.activeElement"));
    }

    [LiveInspectorFact]
    public async Task GlobalDataRefreshDoesNotRunEffectfulTabActions()
    {
        var sensorLoads = 0;
        var locationReads = 0;
        var performanceStarts = 0;
        var evaluations = 0;
        await _page.RouteAsync("**/api/sensors", route =>
        {
            Interlocked.Increment(ref sensorLoads);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"ok":true,"sensors":[]}""",
            });
        });
        await _page.RouteAsync("**/api/geolocation", route =>
        {
            Interlocked.Increment(ref locationReads);
            return route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = """{"ok":true}""" });
        });
        await _page.RouteAsync("**/api/performance/snapshot", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":false,"error":"unavailable"}""",
        }));
        await _page.RouteAsync("**/api/performance/start", route =>
        {
            Interlocked.Increment(ref performanceStarts);
            return route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = """{"ok":false}""" });
        });
        await _page.RouteAsync("**/api/cdp/webviews", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"webviews":{"webviews":[{"id":"web-1","title":"App"}]}}""",
        }));
        await _page.RouteAsync("**/api/cdp/eval", route =>
        {
            Interlocked.Increment(ref evaluations);
            return route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = """{"ok":true}""" });
        });

        await OpenDataDockAsync();
        await _page.Locator("[data-tab='sensors']").ClickAsync();
        await _page.Locator("#df-dock-refresh").ClickAsync();
        await Expect(_page.Locator("#df-dock-action-strip")).ToContainTextAsync("Read location");
        Assert.True(sensorLoads >= 2);
        Assert.Equal(0, locationReads);

        await _page.Locator("[data-tab='performance']").ClickAsync();
        await _page.Locator("#df-dock-refresh").ClickAsync();
        Assert.Equal(0, performanceStarts);

        await _page.Locator("[data-tab='webview']").ClickAsync();
        await _page.Locator("#df-cdp-expr").FillAsync("document.title");
        await _page.Locator("#df-dock-refresh").ClickAsync();
        await Expect(_page.Locator("#df-cdp-expr")).ToHaveValueAsync("document.title");
        Assert.Equal(0, evaluations);
    }

    [LiveInspectorFact]
    public async Task PerformanceActionsReenableAfterStartAndStop()
    {
        static string Response(bool active) => JsonSerializer.Serialize(new
        {
            ok = true,
            owned = true,
            summary = new
            {
                session = new { active, sampleCount = active ? 1 : 0, sampledDurationMs = 0, sampleIntervalMs = 250 },
                capability = new { platform = "Test", mode = "debug", lowPerturbation = false, limitations = Array.Empty<string>() },
            },
        });

        var active = false;
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
        }));
        await _page.RouteAsync("**/api/performance/snapshot", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = Response(active),
        }));
        await _page.RouteAsync("**/api/performance/start", route =>
        {
            active = true;
            return route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = Response(true) });
        });
        await _page.RouteAsync("**/api/performance/stop", route =>
        {
            active = false;
            return route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = Response(false) });
        });

        await OpenDataDockAsync();
        await _page.Locator("[data-tab='performance']").ClickAsync();
        var strip = _page.Locator("#df-dock-action-strip");
        var start = strip.GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true });
        await Expect(start).ToBeEnabledAsync();
        await start.ClickAsync();

        var stop = strip.GetByRole(AriaRole.Button, new() { Name = "Stop", Exact = true });
        await Expect(stop).ToBeEnabledAsync();
        await stop.ClickAsync();
        await Expect(strip.GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true })).ToBeEnabledAsync();
    }

    [LiveInspectorFact]
    public async Task DataRefreshFailureRetainsPreferencesAndSnapshot()
    {
        var calls = 0;
        await _page.RouteAsync("**/api/preferences", route =>
        {
            var call = Interlocked.Increment(ref calls);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = call == 1
                    ? """{"ok":true,"preferences":{"theme":"dark","pageSize":20}}"""
                    : """{"ok":false,"error":"temporary failure"}""",
            });
        });

        await OpenDataDockAsync();
        await _page.Locator("[data-tab='preferences']").ClickAsync();
        var body = _page.Locator("#df-dock-body");
        await Expect(body).ToContainTextAsync("theme");
        await Expect(body).ToContainTextAsync("dark");
        await Expect(body).Not.ToContainTextAsync("Known preferences");
        await Expect(_page.Locator("#df-attach-data")).ToBeEnabledAsync();

        await _page.Locator("#df-dock-refresh").ClickAsync();

        await Expect(body).ToContainTextAsync("theme");
        await Expect(body).ToContainTextAsync("dark");
        await Expect(body).Not.ToContainTextAsync("temporary failure");
        await Expect(_page.Locator("#df-dock-meta")).ToContainTextAsync("stale");
        await Expect(_page.Locator("#df-attach-data")).ToBeEnabledAsync();
    }

    [LiveInspectorFact]
    public async Task DataContextCopyIsBoundedAndRedacted()
    {
        await CaptureClipboardWritesAsync();
        var deepEncodedUrl = EncodeUrlLayers(
            "https://deep-user:deep-password@example.test/path?token=deep-token",
            7);
        var malformedEncodedUrl = EncodeUrlLayers(
            "https://malformed-user:malformed-password@example.test/path?token=malformed-token",
            2) + "%ZZ";
        var encodedWrapperUrl = Uri.EscapeDataString(
            "\"https://wrapper-user:wrapper-password@example.test/path?sig=wrapper-secret\"");
        var deeplyEncodedKey = EncodeQueryKeyLayers("sig", 7);
        var preferences = new Dictionary<string, string>
        {
            ["theme"] = "dark",
            ["apiToken"] = "super-secret-value",
            ["endpoint"] = "https://example.test/items?access_token=url-secret-value",
            ["embeddedUrl"] = "Request failed for https://embedded-user:embedded-password@example.test/path?access_token=embedded-token#embedded-fragment",
            ["indentedUrl"] = "  https://indented-user:indented-password@example.test/path?sig=indented-signature#indented-fragment",
            ["bracketedUrl"] = "Failure [https://bracket-user:bracket-password@example.test]",
            ["nestedUrl"] = "https://proxy.example/fetch?url=https://nested-user:nested-password@internal.example/path?X-Goog-Signature=nested-signature",
            ["outerNestedUrl"] = "https://proxy.example/redirect?to=https://inner.example/a&code=outer-nested-code",
            ["encodedNestedUrl"] = "https://proxy.example/fetch?url=https%3A%2F%2Fencoded-user%3Aencoded-password%40internal.example%2Fpath%3Fkey%3Dencoded-key",
            ["doubleEncodedNestedUrl"] = "https://proxy.example/fetch?url=https%253A%252F%252Fdouble-user%253Adouble-password%2540internal.example%252Fpath%253Fkey%253Ddouble-key",
            ["encodedPathUrl"] = "https://proxy.example/fetch/https%253A%252F%252Fpath-user%253Apath-password%2540internal.example%252Fpath%253Fkey%253Dpath-key",
            ["wholeEncodedUrl"] = Uri.EscapeDataString("https://whole-user:whole-password@example.test/path?token=whole-token"),
            ["deepEncodedUrl"] = deepEncodedUrl,
            ["malformedEncodedUrl"] = "https://proxy.example/fetch?url=" + malformedEncodedUrl,
            ["backslashUrl"] = """https:\\backslash-user:backslash-password@example.test\path?sig=backslash-secret""",
            ["mixedSlashUrl"] = """https:/\mixed-user:mixed-password@example.test/path?sig=mixed-secret""",
            ["zeroSlashUrl"] = "https:storage.example/path?X-Goog-Signature=zero-slash-secret",
            ["mixedEncodedZeroSlashUrl"] = "https:%75ser:mixed-encoded-password@example.test/path?sig=mixed-encoded-zero-secret#mixed-encoded-fragment",
            ["mixedEncodedSlashesUrl"] = "https:%2F%2Fmixed-slash-user%3Amixed-slash-password%40example.test%2Fpath%3Fsig%3Dmixed-slash-encoded-secret%23mixed-slash-fragment",
            ["tabbedKeyUrl"] = "https://storage.example/path?X-Goog-\tSignature=tabbed-key-secret",
            ["gluedUrl"] = "dashboard.https://glued-user:glued-password@example.test/path?sig=glued-secret",
            ["encodedWrapperUrl"] = encodedWrapperUrl,
            ["quotedQueryUrl"] = "https://example.test/path?note=\"useful\"&sig=\"quoted-secret\"",
            ["emptyQuotedQueryUrl"] = "https://example.test/path?sig=\"\"",
            ["wrappedPunctuationUrl"] = "Prefix (https://example.test/path?code=wrapped-punctuation-secret).Next",
            ["parenthesizedPathUrl"] = "See (https://example.test/a(b)c?sig=parenthesized-path-secret#parenthesized-fragment).",
            ["bracketedIpv6Url"] = "[https://[2001:db8::1]/path?sig=ipv6-secret#ipv6-fragment]",
            ["encodedStructureUrl"] = "https://proxy.example/fetch?url="
                + Uri.EscapeDataString("https://safe.test/p?a=1&sig=encoded-structure-secret")
                + "&mode=raw",
            ["queryKeyUrl"] = "https://proxy.example/?https://query-key-user:query-key-password@inner.example/path=value",
            ["providerUrl"] = "https://storage.example/object?key=bare-key&subscription-key=subscription-secret&subscriptionKey=camel-subscription-secret&X-Goog-Signature=provider-signature&XGoogSignature=camel-provider-signature&X-Goog-Credential=provider-credential&hmac=hmac-secret&hdnts=hdnts-secret&hdnea=hdnea-secret&monkey=banana",
            ["encodedKeyUrl"] = "https://example.test/path?%2573%2569%2567=encoded-query-key-secret",
            ["deeplyEncodedKeyUrl"] = "https://example.test/path?" + deeplyEncodedKey + "=deeply-encoded-key-secret",
            ["sessionUrl"] = "https://example.test/path;jsessionid=path-session-secret?JSESSIONID=query-session-secret",
            ["authUrl"] = "https://login.example/callback?ticket=cas-ticket-secret&oobCode=firebase-code-secret&SAMLart=saml-artifact-secret&SAMLResponse=saml-response-secret",
            ["punctuatedUrl"] = "(https://example.test/callback?code=punctuation-secret).",
            ["safeUrl"] = "https://example.test/search?q=hello%20world&tag=~value&tag=two).",
            ["credentialedUrl"] = "https://url-user:url-password@example.test/private#fragment-secret",
            ["note"] = "authorization=header-secret-value",
            ["wireDump"] = "Authorization: Basic basic-secret-value\nCookie: session=cookie-secret-value; theme=dark",
            ["connection"] = "Connection String=\"Server=localhost;Password=connection-secret-value\"",
            ["nearLimitUrl"] = new string('a', 1950) + " https://example.test/?token=near-limit-secret",
        };
        await _page.RouteAsync("**/api/preferences", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = System.Text.Json.JsonSerializer.Serialize(new { ok = true, preferences }),
        }));

        await OpenDataDockAsync();
        await _page.Locator("[data-tab='preferences']").ClickAsync();
        await Expect(_page.Locator("#df-attach-data")).ToBeEnabledAsync();
        await _page.Locator("#df-attach-data").ClickAsync();

        var copied = await _page.EvaluateAsync<string>("() => window.__copiedDevFlowData || ''");
        Assert.Contains("MAUI DevFlow Data snapshot:", copied);
        Assert.Contains("\"theme\": \"dark\"", copied);
        Assert.Contains("redacted", copied, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"agent\":", copied);
        Assert.Contains("\"port\":", copied);
        Assert.Contains("\"followUpTools\":", copied);
        Assert.Contains("maui_preferences_get", copied);
        Assert.DoesNotContain("\"appName\": \"DevFlow Inspector\"", copied);
        Assert.DoesNotContain("super-secret-value", copied);
        Assert.DoesNotContain("url-secret-value", copied);
        Assert.DoesNotContain("header-secret-value", copied);
        Assert.DoesNotContain("url-user", copied);
        Assert.DoesNotContain("url-password", copied);
        Assert.DoesNotContain("fragment-secret", copied);
        Assert.DoesNotContain("basic-secret-value", copied);
        Assert.DoesNotContain("cookie-secret-value", copied);
        Assert.DoesNotContain("connection-secret-value", copied);
        Assert.DoesNotContain("embedded-user", copied);
        Assert.DoesNotContain("embedded-password", copied);
        Assert.DoesNotContain("embedded-token", copied);
        Assert.DoesNotContain("embedded-fragment", copied);
        Assert.DoesNotContain("indented-user", copied);
        Assert.DoesNotContain("indented-password", copied);
        Assert.DoesNotContain("indented-signature", copied);
        Assert.DoesNotContain("indented-fragment", copied);
        Assert.DoesNotContain("bracket-user", copied);
        Assert.DoesNotContain("bracket-password", copied);
        Assert.DoesNotContain("nested-user", copied);
        Assert.DoesNotContain("nested-password", copied);
        Assert.DoesNotContain("nested-signature", copied);
        Assert.DoesNotContain("encoded-user", copied);
        Assert.DoesNotContain("encoded-password", copied);
        Assert.DoesNotContain("encoded-key", copied);
        Assert.DoesNotContain("double-user", copied);
        Assert.DoesNotContain("double-password", copied);
        Assert.DoesNotContain("double-key", copied);
        Assert.DoesNotContain("path-user", copied);
        Assert.DoesNotContain("path-password", copied);
        Assert.DoesNotContain("path-key", copied);
        Assert.DoesNotContain("outer-nested-code", copied);
        Assert.DoesNotContain("whole-user", copied);
        Assert.DoesNotContain("whole-password", copied);
        Assert.DoesNotContain("whole-token", copied);
        Assert.DoesNotContain("deep-user", copied);
        Assert.DoesNotContain("deep-password", copied);
        Assert.DoesNotContain("deep-token", copied);
        Assert.DoesNotContain("malformed-user", copied);
        Assert.DoesNotContain("malformed-password", copied);
        Assert.DoesNotContain("malformed-token", copied);
        Assert.DoesNotContain("backslash-user", copied);
        Assert.DoesNotContain("backslash-password", copied);
        Assert.DoesNotContain("backslash-secret", copied);
        Assert.DoesNotContain("mixed-user", copied);
        Assert.DoesNotContain("mixed-password", copied);
        Assert.DoesNotContain("mixed-secret", copied);
        Assert.DoesNotContain("zero-slash-secret", copied);
        Assert.DoesNotContain("mixed-encoded-password", copied);
        Assert.DoesNotContain("mixed-encoded-zero-secret", copied);
        Assert.DoesNotContain("mixed-encoded-fragment", copied);
        Assert.DoesNotContain("mixed-slash-user", copied);
        Assert.DoesNotContain("mixed-slash-password", copied);
        Assert.DoesNotContain("mixed-slash-encoded-secret", copied);
        Assert.DoesNotContain("mixed-slash-fragment", copied);
        Assert.DoesNotContain("tabbed-key-secret", copied);
        Assert.DoesNotContain("glued-user", copied);
        Assert.DoesNotContain("glued-password", copied);
        Assert.DoesNotContain("glued-secret", copied);
        Assert.DoesNotContain("wrapper-user", copied);
        Assert.DoesNotContain("wrapper-password", copied);
        Assert.DoesNotContain("wrapper-secret", copied);
        Assert.DoesNotContain("quoted-secret", copied);
        Assert.DoesNotContain("wrapped-punctuation-secret", copied);
        Assert.DoesNotContain("parenthesized-path-secret", copied);
        Assert.DoesNotContain("parenthesized-fragment", copied);
        Assert.DoesNotContain("ipv6-secret", copied);
        Assert.DoesNotContain("ipv6-fragment", copied);
        Assert.DoesNotContain("encoded-structure-secret", copied);
        Assert.DoesNotContain("query-key-user", copied);
        Assert.DoesNotContain("query-key-password", copied);
        Assert.DoesNotContain("bare-key", copied);
        Assert.DoesNotContain("subscription-secret", copied);
        Assert.DoesNotContain("camel-subscription-secret", copied);
        Assert.DoesNotContain("provider-signature", copied);
        Assert.DoesNotContain("camel-provider-signature", copied);
        Assert.DoesNotContain("provider-credential", copied);
        Assert.DoesNotContain("hmac-secret", copied);
        Assert.DoesNotContain("hdnts-secret", copied);
        Assert.DoesNotContain("hdnea-secret", copied);
        Assert.DoesNotContain("encoded-query-key-secret", copied);
        Assert.DoesNotContain("deeply-encoded-key-secret", copied);
        Assert.DoesNotContain("path-session-secret", copied);
        Assert.DoesNotContain("query-session-secret", copied);
        Assert.DoesNotContain("cas-ticket-secret", copied);
        Assert.DoesNotContain("firebase-code-secret", copied);
        Assert.DoesNotContain("saml-artifact-secret", copied);
        Assert.DoesNotContain("saml-response-secret", copied);
        Assert.DoesNotContain("punctuation-secret", copied);
        Assert.DoesNotContain("near-limit-secret", copied);
        Assert.Contains("monkey=banana", copied);
        Assert.Contains("(https://example.test/callback?code=<redacted>).", copied);
        Assert.Contains("Prefix (https://example.test/path?code=<redacted>).Next", copied);
        Assert.Contains("https://example.test/path?sig=\\\"<redacted>\\\"", copied);
        Assert.Contains("url=https%3A%2F%2Fsafe.test%2Fp%3Fa%3D1%26sig%3D%3Credacted%3E&mode=raw", copied);
        Assert.Contains("https://example.test/search?q=hello%20world&tag=~value&tag=two).", copied);
        Assert.True(copied.Length < 20000, $"Expected bounded Data context, got {copied.Length} characters.");
    }

    [LiveInspectorFact]
    public async Task DataContextLogsCaptureNewestEntries()
    {
        await CaptureClipboardWritesAsync();
        var logs = Enumerable.Range(0, 200)
            .Select(index => new { l = "Info", m = $"log-{199 - index:D3}" })
            .ToArray();
        await _page.RouteAsync("**/api/logs", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = System.Text.Json.JsonSerializer.Serialize(new { ok = true, logs }),
        }));

        await OpenDataDockAsync();
        await _page.Locator("[data-tab='logs']").ClickAsync();
        await Expect(_page.Locator("#df-attach-data")).ToBeEnabledAsync();
        await _page.Locator("#df-attach-data").ClickAsync();
        await Expect(_page.Locator("#df-dock-meta")).ToContainTextAsync("captured");
        await Expect(_page.Locator("#df-dock-meta-note")).ToHaveTextAsync("May include prior launches.");
        await _page.Locator("#df-dock-refresh").ClickAsync();
        await Expect(_page.Locator("#df-dock-meta")).ToContainTextAsync("captured");
        await Expect(_page.Locator("#df-dock-meta-note")).ToHaveTextAsync("May include prior launches.");

        var copied = await _page.EvaluateAsync<string>("() => window.__copiedDevFlowData || ''");
        Assert.Contains("newest 100 of 200", copied);
        Assert.Contains("May include prior launches.", copied);
        Assert.Contains("log-199", copied);
        Assert.Contains("log-100", copied);
        Assert.DoesNotContain("log-000", copied);
    }

    [LiveInspectorFact]
    public async Task HostedCopilotMenuSendsOnlyTheChosenInspectorContext()
    {
        const string legacyMarkdown = "# Workflow\n\n```json maui-test\n{\"schemaVersion\":1,\"name\":\"saved\",\"steps\":[]}\n```";
        await _page.RouteAsync("**/api/flows/files/list", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"ok\":true,\"supported\":true,\"tests\":[{\"name\":\"saved.md\",\"size\":100}]}",
        }));
        await _page.RouteAsync("**/api/flows/files/load", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new { ok = true, name = "saved.md", markdown = legacyMarkdown, steps = 1 }),
        }));

        await _page.SetViewportSizeAsync(1100, 800);
        var trustedEmbedUrl = TrustedEmbedUrl();
        await _page.SetContentAsync($$"""
            <iframe id="hosted" style="width:1000px;height:700px;border:0" src="{{trustedEmbedUrl}}#devflowBridge=test-bridge"></iframe>
            <script>
              window.__copilotRequests = [];
              window.__workflowPicks = 0;
              window.addEventListener('message', function (e) {
                const d = e.data;
                if (!d || d.bridgeId !== 'test-bridge') return;
                if (d.type === 'devflow:ready') {
                  e.source.postMessage({
                    type: 'devflow:host',
                    v: 1,
                    bridgeId: 'test-bridge',
                    hostId: 'test-host',
                    capabilities: ['copilotContext', 'workflowFilePicker']
                  }, '*');
                } else if (d.type === 'devflow:attachCopilot') {
                  window.__copilotRequests.push({ context: d.context, payload: d.payload });
                  e.source.postMessage({
                    type: 'devflow:hostResult',
                    v: 1,
                    bridgeId: 'test-bridge',
                    requestId: d.requestId,
                    ok: true,
                    message: 'Attached requested context.'
                  }, '*');
                } else if (d.type === 'devflow:pickWorkflow') {
                  window.__workflowPicks++;
                  e.source.postMessage({
                    type: 'devflow:hostResult',
                    v: 1,
                    bridgeId: 'test-bridge',
                    requestId: d.requestId,
                    ok: true,
                    name: 'host-picked.md',
                    markdown: {{JsonSerializer.Serialize(legacyMarkdown)}}
                  }, '*');
                }
              });
            </script>
            """);

        var frame = _page.FrameLocator("#hosted");
        await Expect(frame.Locator(".devflow-element").First).ToBeAttachedAsync();
        var elementId = await frame.Locator("[data-automationId='HeaderLabel']").GetAttributeAsync("data-id");
        Assert.False(string.IsNullOrEmpty(elementId));
        await frame.Locator($".df-tree-node[data-tree-id='{elementId}']").ClickAsync();

        await ClickHostedToolbarActionAsync(frame, "#df-send-copilot");
        await frame.Locator("[data-copilot-context='selection']").ClickAsync();
        await Expect(frame.Locator("#df-status")).ToHaveTextAsync("Attached requested context.");

        await frame.Locator("#df-toggle-workbench").ClickAsync();
        await frame.GetByRole(AriaRole.Button, new() { Name = "Open saved test", Exact = true }).ClickAsync();
        await frame.Locator("#df-saved-test-select").SelectOptionAsync("saved.md");
        await frame.Locator("#df-saved-test-open").ClickAsync();
        await Expect(frame.Locator("#df-workbench-strip")).ToContainTextAsync("saved.md");

        await ClickHostedToolbarActionAsync(frame, "#df-send-copilot");
        await frame.Locator("[data-copilot-context='workflow']").ClickAsync();
        await Expect(frame.Locator("#df-status")).ToHaveTextAsync("Attached requested context.");

        await ClickHostedToolbarActionAsync(frame, "#df-send-copilot");
        await frame.Locator("[data-copilot-context='combined']").ClickAsync();
        await Expect(frame.Locator("#df-status")).ToHaveTextAsync("Attached requested context.");

        var requestsJson = await _page.EvaluateAsync<string>("() => JSON.stringify(window.__copilotRequests)");
        using var requests = JsonDocument.Parse(requestsJson);
        Assert.Equal(3, requests.RootElement.GetArrayLength());
        Assert.Equal("selection", requests.RootElement[0].GetProperty("context").GetString());
        Assert.Equal("HeaderLabel",
            requests.RootElement[0].GetProperty("payload").GetProperty("element").GetProperty("automationId").GetString());
        Assert.Equal(JsonValueKind.Null,
            requests.RootElement[0].GetProperty("payload").GetProperty("markdown").ValueKind);
        Assert.False(
            requests.RootElement[0].GetProperty("payload").TryGetProperty("flow", out _),
            "Selection-only payload should not leak workflow context.");
        Assert.Equal("workflow", requests.RootElement[1].GetProperty("context").GetString());
        Assert.Equal(JsonValueKind.Null,
            requests.RootElement[1].GetProperty("payload").GetProperty("element").ValueKind);
        AssertNormalizedWorkflowMarkdown(
            requests.RootElement[1].GetProperty("payload").GetProperty("markdown").GetString());
        Assert.Equal("combined", requests.RootElement[2].GetProperty("context").GetString());
        Assert.Equal("HeaderLabel",
            requests.RootElement[2].GetProperty("payload").GetProperty("element").GetProperty("automationId").GetString());
        AssertNormalizedWorkflowMarkdown(
            requests.RootElement[2].GetProperty("payload").GetProperty("markdown").GetString());

        await frame.Locator("#df-workbench-stage-goal").ClickAsync();
        await frame.GetByRole(AriaRole.Button, new() { Name = "Open saved test", Exact = true }).ClickAsync();
        await frame.Locator("#df-saved-test-file").ClickAsync();
        await Expect(frame.Locator("#df-workbench-strip")).ToContainTextAsync("host-picked.md");
        Assert.Equal(1, await _page.EvaluateAsync<int>("() => window.__workflowPicks"));

        static void AssertNormalizedWorkflowMarkdown(string? markdown)
        {
            Assert.False(string.IsNullOrWhiteSpace(markdown));
            Assert.DoesNotContain("schemaVersion", markdown, StringComparison.Ordinal);
            var parsed = FlowMarkdown.Parse(markdown!);
            Assert.True(parsed.Ok, parsed.Error);
            Assert.Equal(1, parsed.Flow!.Schema);
            Assert.Equal("saved", parsed.Flow.Name);
            Assert.Empty(parsed.Flow.Steps);
            Assert.DoesNotContain("HeaderLabel", markdown, StringComparison.Ordinal);
        }
    }

    [LiveInspectorFact]
    public async Task HostedDataAttachmentWaitsForHostAcknowledgement()
    {
        await _page.RouteAsync("**/api/device", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"device":{"device-info":{"platform":"WinUI"}}}""",
        }));
        await _page.SetViewportSizeAsync(1100, 800);
        var trustedEmbedUrl = TrustedEmbedUrl();
        await _page.SetContentAsync($$"""
            <iframe id="hosted" style="width:1000px;height:700px;border:0" src="{{trustedEmbedUrl}}#devflowBridge=test-bridge"></iframe>
            <script>
              window.__attachRequests = 0;
              window.addEventListener('message', function (e) {
                const d = e.data;
                if (!d || d.bridgeId !== 'test-bridge') return;
                if (d.type === 'devflow:ready') {
                  e.source.postMessage({
                    type: 'devflow:host',
                    v: 1,
                    bridgeId: 'test-bridge',
                    hostId: 'test-host',
                    capabilities: ['attachData']
                  }, '*');
                } else if (d.type === 'devflow:attachData') {
                  window.__attachRequests++;
                  setTimeout(function () {
                    e.source.postMessage({
                      type: 'devflow:hostResult',
                      v: 1,
                      bridgeId: 'test-bridge',
                      requestId: d.requestId,
                      ok: false,
                      error: 'Host rejected snapshot.'
                    }, '*');
                  }, 800);
                }
              });
            </script>
            """);

        var frame = _page.FrameLocator("#hosted");
        await Expect(frame.Locator(".devflow-element").First).ToBeAttachedAsync();
        await Expect(frame.Locator("body")).ToHaveAttributeAsync("data-host-kind", "test-host");
        await ClickHostedToolbarActionAsync(frame, "#df-toggle-dock");
        await frame.Locator("[data-tab='device']").ClickAsync();
        await Expect(frame.Locator("#df-attach-data")).ToBeEnabledAsync();
        await frame.Locator("#df-attach-data").ClickAsync();
        await Expect(frame.Locator("#df-status")).ToContainTextAsync("Adding Device snapshot to Copilot");
        await Expect(frame.Locator("#df-status")).ToHaveTextAsync("Host rejected snapshot.", new() { Timeout = 3000 });
        Assert.Equal(1, await _page.EvaluateAsync<int>("() => window.__attachRequests"));
    }

    private string TrustedEmbedUrl()
    {
        var token = Environment.GetEnvironmentVariable("INSPECTOR_EMBED_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            var brokerStatePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".mauidevflow",
                "broker.json");
            Assert.True(File.Exists(brokerStatePath), $"Broker state not found at {brokerStatePath}");
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(brokerStatePath));
            Assert.True(document.RootElement.TryGetProperty("embedToken", out var tokenElement));
            token = tokenElement.GetString();
        }
        Assert.False(string.IsNullOrWhiteSpace(token), "Broker embed token is missing.");
        var separator = BaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return BaseUrl + separator + "embed=" + Uri.EscapeDataString(token);
    }

    private Task CaptureClipboardWritesAsync() =>
        _page.AddInitScriptAsync("""
            window.__copiedDevFlowData = "";
            Object.defineProperty(navigator, "clipboard", {
              configurable: true,
              value: { writeText: async text => { window.__copiedDevFlowData = text; } }
            });
            """);

    private async Task ResetInspectorStateAsync()
    {
        try
        {
            var recordingIds = await _page.EvaluateAsync<string[]>("""
                () => {
                  try {
                    const ids = JSON.parse(sessionStorage.getItem('maui-devflow-recording-capabilities-v1') || '[]');
                    return Array.isArray(ids)
                      ? ids.filter(id => /^[a-f0-9]{24}$/.test(String(id))).slice(0, 16)
                      : [];
                  } catch {
                    return [];
                  }
                }
                """);
            foreach (var recordingId in recordingIds)
            {
                try
                {
                    var serializedId = JsonSerializer.Serialize(recordingId);
                    await _page.EvaluateAsync($$"""
                        async () => {
                          const basePath = location.pathname.replace(/\/$/, '');
                          await fetch(`${basePath}/api/flows/record/cancel`, {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ recordingId: {{serializedId}} })
                          });
                        }
                        """);
                }
                catch
                {
                    // A test route can deliberately abort this request; page disposal still releases its lease.
                }
            }

            await _page.EvaluateAsync("""
                async () => {
                  document.querySelector('#df-workbench-close')?.click();
                  document.querySelector('#df-timeline-close')?.click();
                  document.querySelector('#df-dock-close')?.click();
                  document.querySelector('#df-props-close')?.click();
                  document.querySelectorAll('[role="dialog"]').forEach(dialog => dialog.parentElement?.remove());
                  const basePath = location.pathname.replace(/\/$/, '');
                  await fetch(`${basePath}/api/control`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ action: 'release' })
                  });
                  sessionStorage.clear();
                  localStorage.clear();
                }
                """);
        }
        catch
        {
            // Live-inspector cleanup is best effort when a test intentionally disconnects its page.
        }
    }

    private async Task OpenWorkbenchAsync()
    {
        var workbench = _page.Locator("#df-workbench");
        if (!await workbench.IsVisibleAsync())
            await _page.Locator("#df-toggle-workbench").ClickAsync();
        await Expect(workbench).ToBeVisibleAsync();
    }

    private async Task CloseWorkbenchAsync()
    {
        var workbench = _page.Locator("#df-workbench");
        if (await workbench.IsVisibleAsync())
        {
            await _page.Locator("#df-workbench-close").ClickAsync();
            await Expect(workbench).ToBeHiddenAsync();
        }
    }

    private async Task FillManagedGoalAsync(string goal)
    {
        await OpenWorkbenchAsync();
        await _page.Locator("#df-workbench-stage-goal").ClickAsync();
        var input = _page.Locator("#df-goal-input");
        await Expect(input).ToBeVisibleAsync();
        await input.FillAsync(goal);
        await Expect(input).ToHaveAttributeAsync("aria-invalid", "false");
        await CloseWorkbenchAsync();
    }

    private async Task StartManagedRecordingAsync(string goal)
    {
        await OpenWorkbenchAsync();
        await _page.Locator("#df-workbench-stage-goal").ClickAsync();
        var input = _page.Locator("#df-goal-input");
        await input.FillAsync(goal);
        var record = _page.GetByRole(AriaRole.Button, new() { Name = "Record steps", Exact = true });
        await Expect(record).ToBeEnabledAsync();
        await record.ClickAsync();
        await Expect(_page.Locator("#df-workbench-panel-steps")).ToBeVisibleAsync();
    }

    private async Task OpenSavedTestAsync(string name)
    {
        await OpenWorkbenchAsync();
        await _page.Locator("#df-workbench-stage-goal").ClickAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Open saved test", Exact = true }).ClickAsync();
        await Expect(_page.Locator($"#df-saved-test-select option[value='{name}']")).ToHaveCountAsync(1);
        await _page.Locator("#df-saved-test-select").SelectOptionAsync(name);
        await _page.Locator("#df-saved-test-open").ClickAsync();
        await Expect(_page.Locator("#df-workbench-strip")).ToContainTextAsync(name);
    }

    private async Task OpenLegacyQuickReplayAsync()
    {
        await OpenWorkbenchAsync();
        await _page.Locator("#df-workbench-stage-run").ClickAsync();
        var details = _page.Locator("#df-workbench-panel-run .df-run-details");
        await Expect(details).ToBeVisibleAsync();
        if (!await details.EvaluateAsync<bool>("element => element.open"))
            await details.Locator("summary").First.ClickAsync();
        var replay = details.GetByRole(AriaRole.Button, new() { Name = "Legacy quick replay (advanced)" });
        await Expect(replay).ToBeEnabledAsync();
        await replay.ClickAsync();
    }

    private async Task ClickToolbarActionAsync(string selector)
    {
        var action = _page.Locator(selector);
        if (!await action.IsVisibleAsync())
        {
            await _page.Locator("#df-more").ClickAsync();
            await Expect(action).ToBeVisibleAsync();
        }
        await action.ClickAsync();
    }

    private async Task ClickHostedToolbarActionAsync(IFrameLocator frame, string selector)
    {
        var action = frame.Locator(selector);
        if (!await action.IsVisibleAsync())
        {
            var more = frame.Locator("#df-more");
            await Expect(more).ToBeVisibleAsync();
            await more.ClickAsync();
            await Expect(action).ToBeVisibleAsync();
        }
        await action.ClickAsync();
    }

    private async Task ExpectToolbarToggleStateAsync(string selector, bool expected)
    {
        var action = _page.Locator(selector);
        var value = expected ? "true" : "false";
        var inOverflow = await action.EvaluateAsync<bool>(
            "button => button.parentElement?.id === 'df-toolbar-overflow'");
        if (inOverflow)
        {
            await Expect(action).ToHaveAttributeAsync("role", "menuitemcheckbox");
            await Expect(action).ToHaveAttributeAsync("aria-checked", value);
        }
        else
        {
            await Expect(action).ToHaveAttributeAsync("aria-pressed", value);
        }
    }

    private static string EncodeUrlLayers(string value, int layers)
    {
        for (var layer = 0; layer < layers; layer++)
            value = Uri.EscapeDataString(value);
        return value;
    }

    private static string EncodeQueryKeyLayers(string value, int layers)
    {
        value = string.Concat(value.Select(character => $"%{(int)character:X2}"));
        for (var layer = 1; layer < layers; layer++)
            value = Uri.EscapeDataString(value);
        return value;
    }

    private async Task OpenDataDockAsync()
    {
        await _page.SetViewportSizeAsync(700, 700);
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator(".devflow-element").First).ToBeAttachedAsync();
        var data = _page.Locator("#df-toggle-dock");
        if (!await data.IsVisibleAsync())
            await _page.Locator("#df-more").ClickAsync();
        await data.ClickAsync();
        await Expect(_page.Locator("#df-dock")).ToBeVisibleAsync();
    }

    private ILocatorAssertions Expect(ILocator locator) =>
        Assertions.Expect(locator);
}
