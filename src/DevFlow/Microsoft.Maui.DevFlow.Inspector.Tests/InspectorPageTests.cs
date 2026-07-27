using System.Globalization;
using System.Text;
using System.Text.Json;
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
        await _context.DisposeAsync();
        await _browser.DisposeAsync();
        _playwright.Dispose();
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
    }

    [LiveInspectorFact]
    public async Task InspectorModulesAreServedAsJavaScript()
    {
        foreach (var (file, exportedSymbol) in new[]
        {
            ("inspector-api.js", "export function createInspectorApi"),
            ("inspector-dialog.js", "export function confirmModal"),
            ("inspector-data-context.js", "export function createDataSnapshot"),
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
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = "{\"ok\":true}",
            });
        });
        await _page.GotoAsync(BaseUrl);

        // Find an element with positive width and height in style (not -1 or 0)
        var allPositioned = _page.Locator(".devflow-element[style*='width:']");
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
        await Expect(_page.Locator("#df-toggle-record")).ToBeEnabledAsync();
        try
        {
            await _context.SetOfflineAsync(true);
            await Expect(_page.Locator("#df-status")).ToContainTextAsync("App disconnected", new() { Timeout = 10_000 });
            await Expect(_page.Locator("#df-presence")).ToContainTextAsync("Disconnected");
            await Expect(_page.Locator("#df-disconnected-overlay")).ToBeVisibleAsync();
            await Expect(_page.Locator("#df-disconnected-overlay")).ToContainTextAsync("Showing the last captured frame");
            Assert.True(await _page.Locator("body").EvaluateAsync<bool>(
                "body => body.classList.contains('df-disconnected')"));
            await Expect(_page.Locator("#df-toggle-record")).ToBeDisabledAsync();
            var entry = _page.Locator("[data-automationId='NewTodoEntry']");
            await Expect(entry).ToBeAttachedAsync();
            var bounds = await entry.BoundingBoxAsync();
            Assert.NotNull(bounds);
            await _page.Mouse.ClickAsync(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            await Expect(_page.Locator("#app-viewport > input, #app-viewport > textarea")).ToHaveCountAsync(0);

            await _context.SetOfflineAsync(false);
            await Expect(_page.Locator("#df-toggle-record")).ToBeEnabledAsync(new() { Timeout = 15_000 });
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
        await _page.RouteAsync("**/api/flows/record/stop", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"ok\":true,\"empty\":true,\"steps\":0}",
        }));

        await _page.GotoAsync(BaseUrl);
        var record = _page.Locator("#df-toggle-record");
        await Expect(record).ToBeEnabledAsync();
        await record.ClickAsync();
        await Expect(_page.Locator("#df-timeline")).ToBeVisibleAsync();

        await record.ClickAsync();

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
        var record = _page.Locator("#df-toggle-record");
        await record.ClickAsync();
        await Expect(record).ToHaveAttributeAsync("aria-pressed", "true");

        await _page.Locator("[data-automationId='ShowModalButton']").ClickAsync(new() { Force = true });
        await Expect(_page.Locator("#df-presence")).ToContainTextAsync("Canvas Inspector");
        await Expect(record).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(record).ToBeDisabledAsync();
        await Expect(_page.Locator("#df-timeline")).ToBeVisibleAsync();

        await _page.Locator("#df-take-control").ClickAsync();
        await _page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Take control" }).ClickAsync();
        await Expect(record).ToBeEnabledAsync();
        await Expect(record).ToHaveAttributeAsync("aria-pressed", "true");
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
        var record = _page.Locator("#df-toggle-record");
        await record.ClickAsync();
        await Expect(record).ToHaveAttributeAsync("aria-pressed", "true");

        await _page.Locator("#df-record-cancel").ClickAsync();
        var dialog = _page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToContainTextAsync("Discard this recording");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Discard" }).ClickAsync();

        Assert.Equal(1, cancelRequests);
        await Expect(record).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(_page.Locator("#df-timeline")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("Recording discarded");
    }

    [LiveInspectorFact]
    public async Task AdaptiveLayoutTracksHostViewport()
    {
        await _page.SetViewportSizeAsync(1440, 820);
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator(".devflow-element").First).ToBeAttachedAsync();

        Assert.Equal("wide", await _page.Locator("body").GetAttributeAsync("data-host-layout"));
        await Expect(_page.Locator("#df-toolbar-secondary")).ToBeVisibleAsync();
        await _page.Locator(".df-tree-node").Last.ClickAsync();
        await Expect(_page.Locator("#df-props-pane")).ToBeVisibleAsync();
        await _page.Keyboard.PressAsync("Escape");
        await Expect(_page.Locator("#df-props-pane")).ToBeHiddenAsync();

        await _page.SetViewportSizeAsync(1200, 820);
        await _page.WaitForTimeoutAsync(100);
        Assert.Equal("compact", await _page.Locator("body").GetAttributeAsync("data-host-layout"));
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
        Assert.Equal("short", await _page.Locator("body").GetAttributeAsync("data-host-layout"));
        Assert.True(await _page.Locator("body").EvaluateAsync<bool>("body => body.classList.contains('df-tree-hidden')"));
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
        await _page.SetViewportSizeAsync(1440, 820);
        await _page.GotoAsync(BaseUrl);
        await Expect(_page.Locator(".devflow-element").First).ToBeAttachedAsync();
        var total = await _page.Locator("#df-toolbar-secondary > button, #df-toolbar-overflow > button").CountAsync();
        var wideInline = await _page.Locator("#df-toolbar-secondary > button").CountAsync();
        var focusedInline = _page.Locator("#df-toolbar-secondary > button").First;
        await focusedInline.FocusAsync();
        await _page.EvaluateAsync("() => window.dispatchEvent(new Event('resize'))");
        await _page.WaitForTimeoutAsync(100);
        Assert.True(await focusedInline.EvaluateAsync<bool>("button => button === document.activeElement"),
            "Toolbar reflow should preserve focus for an action that remains inline.");

        var compactInline = 0;
        var compactOverflow = 0;
        foreach (var width in new[] { 1300, 1200, 1100, 1000, 900 })
        {
            await _page.SetViewportSizeAsync(width, 820);
            await _page.WaitForTimeoutAsync(150);
            compactInline = await _page.Locator("#df-toolbar-secondary > button").CountAsync();
            compactOverflow = await _page.Locator("#df-toolbar-overflow > button").CountAsync();
            if (compactInline > 0 && compactOverflow > 0)
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
        Assert.True(Math.Abs(content.Y - (tabs.Y + tabs.Height)) <= 1,
            $"Expected Data content to start at the tab boundary, gap was {content.Y - (tabs.Y + tabs.Height)}px");
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
        await Expect(_page.Locator("#df-dock-body button use[href='#i-location']")).ToHaveCountAsync(1);
        Assert.DoesNotContain("📍", await _page.Locator("#df-dock-body").InnerTextAsync());

        await _page.Locator("[data-tab='files']").ClickAsync();
        await Expect(_page.Locator("#df-dock-body use[href='#i-folder']")).ToHaveCountAsync(1);
        await Expect(_page.Locator("#df-dock-body use[href='#i-file']")).ToHaveCountAsync(1);
        var filesText = await _page.Locator("#df-dock-body").InnerTextAsync();
        Assert.DoesNotContain("📁", filesText);
        Assert.DoesNotContain("📄", filesText);
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

        await Expect(_page.Locator("#df-toggle-bounds")).ToHaveAttributeAsync("aria-pressed", "false");
        await _page.Locator("#df-toggle-bounds").ClickAsync();
        await Expect(_page.Locator("#df-toggle-bounds")).ToHaveAttributeAsync("aria-pressed", "true");

        await Expect(_page.Locator("#df-toggle-record")).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(_page.Locator("#df-toggle-dock")).ToHaveAttributeAsync("aria-pressed", "false");
        await _page.Locator("#df-toggle-dock").ClickAsync();
        await Expect(_page.Locator("#df-toggle-dock")).ToHaveAttributeAsync("aria-pressed", "true");
        await _page.Locator("#df-dock-close").ClickAsync();
        await Expect(_page.Locator("#df-toggle-dock")).ToHaveAttributeAsync("aria-pressed", "false");
    }

    [LiveInspectorFact]
    public async Task HoverDistinguishesInteractiveAndNonInteractiveElementsInAllModes()
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

        await scrollableElement.HoverAsync(new() { Force = true });
        await Expect(scrollableElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));

        await staticElement.HoverAsync(new() { Force = true });
        await Expect(staticElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));
        await Expect(staticElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover-noninteractive(\\s|$)"));
        Assert.Equal("dashed", await staticElement.EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));
        Assert.Equal("rgba(0, 0, 0, 0)", await staticElement.EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));

        await disabledElement.HoverAsync(new() { Force = true });
        await Expect(disabledElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));
        await Expect(disabledElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover-noninteractive(\\s|$)"));

        await overlappingStatic.HoverAsync(new() { Force = true });
        await Expect(underlyingInteractive).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));
        await Expect(overlappingStatic).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));

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

        await interactiveElement.HoverAsync(new() { Force = true });
        await Expect(interactiveElement).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover(\\s|$)"));
        await Expect(interactiveElement).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("(^|\\s)df-hover-noninteractive(\\s|$)"));
        Assert.Equal("solid", await interactiveElement.EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));
        Assert.Equal("rgba(0, 0, 0, 0)", await interactiveElement.EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
    }

    [LiveInspectorFact]
    public async Task InspectorExposesAccessibleDocumentStructure()
    {
        await _page.SetViewportSizeAsync(900, 820);
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
        await Expect(_page.Locator("#df-toolbar-overflow > button").First).ToHaveAttributeAsync("role", "menuitem");
        var assertAction = _page.Locator("#df-assert");
        await Expect(assertAction).ToHaveAttributeAsync("aria-disabled", "true");
        Assert.False(await assertAction.EvaluateAsync<bool>("button => button.disabled"));
        await assertAction.EvaluateAsync("button => button.focus()");
        Assert.True(await assertAction.EvaluateAsync<bool>("button => button === document.activeElement"));
        await assertAction.DispatchEventAsync("click");
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("Enabled while recording");
        await Expect(_page.Locator(".df-dock-tab-list")).ToHaveAttributeAsync("aria-label", "App data");
        await Expect(_page.Locator("#df-viewport-wrap")).ToHaveAttributeAsync("role", "region");
        await Expect(_page.Locator("#df-viewport-wrap")).ToHaveAttributeAsync("aria-label", "Live app viewport");
    }

    [LiveInspectorFact]
    public async Task TransientInspectorChromeRestoresKeyboardFocus()
    {
        await _page.SetViewportSizeAsync(900, 820);
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
        await Expect(titleRows).ToHaveCountAsync(3);
        var titleLabels = await titleRows.AllInnerTextsAsync();
        Assert.Contains(titleLabels, label => label.Contains("Buy groceries", StringComparison.Ordinal));
        Assert.Contains(titleLabels, label => label.Contains("Walk the dog", StringComparison.Ordinal));
        Assert.Contains(titleLabels, label => label.Contains("Finish Microsoft.Maui.DevFlow project", StringComparison.Ordinal));

        var checkRows = _page.Locator(".df-tree-node").Filter(new() { HasText = "CheckBox TodoCheckBox" });
        await Expect(checkRows).ToHaveCountAsync(3);
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
        await Expect(_page.Locator("#df-dock-body")).ToHaveAttributeAsync("role", "tabpanel");
        await Expect(_page.Locator("#df-dock-body")).ToHaveAttributeAsync("aria-labelledby", "df-tab-network");
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
        await Expect(_page.Locator("#df-toggle-record")).ToBeEnabledAsync();
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
        await Expect(_page.Locator("#df-toggle-record")).ToBeDisabledAsync();
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
            Body = "{\"ok\":true,\"name\":\"keyboard-test\",\"steps\":1,\"markdown\":\"# keyboard test\"}",
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
        var record = _page.Locator("#df-toggle-record");
        await Expect(record).ToBeEnabledAsync();
        await record.ClickAsync();
        await record.ClickAsync();
        var replay = _page.Locator("#df-toggle-replay");
        await Expect(replay).ToBeEnabledAsync();
        await replay.ClickAsync();

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
    public async Task WorkflowPanelLoadsProjectAndLocalFilesAndShowsReplayResultsInline()
    {
        const string projectMarkdown = "# Project workflow\n\n```json maui-test\n{\"schemaVersion\":1,\"name\":\"project\",\"steps\":[]}\n```";
        const string localMarkdown = "# Local workflow\n\n```json maui-test\n{\"schemaVersion\":1,\"name\":\"local\",\"steps\":[]}\n```";
        var replayRequests = 0;
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"youAreWriter\":true,\"heldByOther\":false}",
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
        await _page.RouteAsync("**/api/flows/replay", async route =>
        {
            using var body = JsonDocument.Parse(route.Request.PostData ?? "{}");
            Assert.Equal(projectMarkdown, body.RootElement.GetProperty("markdown").GetString());
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
        await _page.Locator("#df-load-flow").ClickAsync();
        await Expect(_page.Locator("#df-timeline")).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-workflow-select option")).ToHaveCountAsync(2);
        await _page.Locator("#df-workflow-select").SelectOptionAsync("saved.md");
        await Expect(_page.Locator("#df-timeline-meta")).ToContainTextAsync("saved.md");
        await Expect(_page.Locator("#df-workflow-replay")).ToBeEnabledAsync();

        await _page.Locator("#df-workflow-replay").ClickAsync();
        await _page.GetByRole(AriaRole.Dialog).GetByRole(
            AriaRole.Button,
            new() { Name = "Replay" }).ClickAsync();
        await Expect(_page.Locator("#df-timeline-title-text")).ToContainTextAsync("Replay passed");
        await Expect(_page.Locator("#df-timeline .df-step-passed")).ToHaveCountAsync(1);
        await Expect(_page.Locator("#df-timeline")).ToContainTextAsync("Tap AddButton");
        Assert.Equal(1, replayRequests);

        await _page.Locator("#df-workflow-file-input").SetInputFilesAsync(new FilePayload
        {
            Name = "local.md",
            MimeType = "text/markdown",
            Buffer = Encoding.UTF8.GetBytes(localMarkdown),
        });
        await Expect(_page.Locator("#df-timeline-meta")).ToContainTextAsync("local.md");
        await Expect(_page.Locator("#df-workflow-replay")).ToBeEnabledAsync();
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
            Body = "{\"ok\":false,\"supported\":false}",
        }));
        await _page.RouteAsync("**/api/getProperty", async route =>
        {
            using var body = System.Text.Json.JsonDocument.Parse(route.Request.PostData ?? "{}");
            var name = body.RootElement.GetProperty("name").GetString();
            var value = name switch
            {
                "BackgroundColor" => "null",
                "FontSize" => "28",
                "Opacity" => "1",
                "IsVisible" or "IsEnabled" => "true",
                _ => "value",
            };
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = $"{{\"value\":{(value == "null" ? "null" : $"\"{value}\"")}}}",
            });
        });
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
        await _page.Locator("#df-toggle-record").ClickAsync();
        await Expect(_page.Locator("#df-timeline")).ToBeVisibleAsync();

        ownsLease = false;
        recording = false;
        await _page.EvaluateAsync("document.dispatchEvent(new Event('visibilitychange'))");
        await endedStatusObserved.Task.WaitAsync(TimeSpan.FromSeconds(8));

        await Expect(_page.Locator("#df-timeline")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-toggle-record")).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("ended in another session");
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
        await _page.RouteAsync("**/api/flows/record/stop", route => route.FulfillAsync(new()
        {
            Status = 400,
            ContentType = "application/json",
            Body = "{\"ok\":false,\"error\":\"No recording is active for this app.\"}",
        }));

        await _page.GotoAsync(BaseUrl);
        var record = _page.Locator("#df-toggle-record");
        await record.ClickAsync();
        await record.ClickAsync();

        await Expect(record).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(_page.Locator("#df-timeline")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("already ended in another session");
    }

    [LiveInspectorFact]
    public async Task TransientMenusRemainAvailableOverInspectorSurfaces()
    {
        await _page.SetViewportSizeAsync(900, 820);
        await _page.GotoAsync(BaseUrl);
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
        await Expect(_page.Locator("#df-files-root-info")).ToContainTextAsync("Private app storage · persistent");
        await Expect(_page.Locator(".df-files-mode")).ToHaveTextAsync("Browse only");
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
        await Expect(_page.Locator("#df-dock-body")).ToContainTextAsync("Permission");
        await _page.Locator("[data-alert-action='dismiss']").Filter(new() { HasText = "Allow" }).ClickAsync();

        Assert.Equal(1, dismissRequests);
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("Dismissed native alert with Allow");
        await Expect(_page.Locator("#df-dock-body")).ToContainTextAsync("No native alert is visible");
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
        await _page.Locator("#df-dock-body select").EvaluateAsync("""
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
    public async Task NetworkTabRefreshesAutomaticallyAndKeepsDetailOpen()
    {
        var networkCalls = 0;
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
            await Task.Delay(500);
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = """
                    {"ok":true,"request":{"id":"request-1","method":"GET","url":"https://example.test/second","statusCode":200,"statusText":"OK","durationMs":12}}
                    """,
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
        await _page.Locator("#df-dock-body tr.df-row-click").First.ClickAsync();
        await Expect(_page.Locator("#df-attach-data")).ToBeDisabledAsync();
        await Expect(_page.Locator("#df-dock-body")).ToContainTextAsync("Back to requests");
        await Expect(_page.Locator("#df-attach-data")).ToBeEnabledAsync();
        await _page.WaitForTimeoutAsync(2500);
        await Expect(_page.Locator("#df-dock-body")).ToContainTextAsync("Back to requests");
        await Expect(_page.Locator("#df-dock-meta")).ToContainTextAsync("live paused");
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
            .Select(index => new { l = "Info", m = $"log-{index:D3}" })
            .ToArray();
        await _page.RouteAsync("**/api/logs", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = System.Text.Json.JsonSerializer.Serialize(new { ok = true, logs }),
        }));

        await OpenDataDockAsync();
        await Expect(_page.Locator("#df-attach-data")).ToBeEnabledAsync();
        await _page.Locator("#df-attach-data").ClickAsync();

        var copied = await _page.EvaluateAsync<string>("() => window.__copiedDevFlowData || ''");
        Assert.Contains("newest 100 of 200", copied);
        Assert.Contains("log-000", copied);
        Assert.Contains("log-099", copied);
        Assert.DoesNotContain("log-100", copied);
        Assert.DoesNotContain("log-199", copied);
    }

    [LiveInspectorFact]
    public async Task HostedCopilotMenuSendsOnlyTheChosenInspectorContext()
    {
        const string markdown = "# Workflow\n\n```json maui-test\n{\"schemaVersion\":1,\"name\":\"saved\",\"steps\":[]}\n```";
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
            Body = JsonSerializer.Serialize(new { ok = true, name = "saved.md", markdown, steps = 1 }),
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
                    hostKind: 'test-host',
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
                    markdown: {{JsonSerializer.Serialize(markdown)}}
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

        await frame.Locator("#df-more").ClickAsync();
        await frame.Locator("#df-send-copilot").ClickAsync();
        await frame.Locator("[data-copilot-context='selection']").ClickAsync();
        await Expect(frame.Locator("#df-status")).ToHaveTextAsync("Attached requested context.");

        var loadFlow = frame.Locator("#df-load-flow");
        if (!await loadFlow.IsVisibleAsync())
            await frame.Locator("#df-more").ClickAsync();
        await loadFlow.ClickAsync();
        await frame.Locator("#df-workflow-select").SelectOptionAsync("saved.md");
        await Expect(frame.Locator("#df-timeline-meta")).ToContainTextAsync("saved.md");

        await frame.Locator("#df-more").ClickAsync();
        await frame.Locator("#df-send-copilot").ClickAsync();
        await frame.Locator("[data-copilot-context='workflow']").ClickAsync();
        await Expect(frame.Locator("#df-status")).ToHaveTextAsync("Attached requested context.");

        await frame.Locator("#df-more").ClickAsync();
        await frame.Locator("#df-send-copilot").ClickAsync();
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
        Assert.Equal("workflow", requests.RootElement[1].GetProperty("context").GetString());
        Assert.Equal(JsonValueKind.Null,
            requests.RootElement[1].GetProperty("payload").GetProperty("element").ValueKind);
        Assert.Equal(markdown,
            requests.RootElement[1].GetProperty("payload").GetProperty("markdown").GetString());
        Assert.Equal("combined", requests.RootElement[2].GetProperty("context").GetString());
        Assert.Equal("HeaderLabel",
            requests.RootElement[2].GetProperty("payload").GetProperty("element").GetProperty("automationId").GetString());
        Assert.Equal(markdown,
            requests.RootElement[2].GetProperty("payload").GetProperty("markdown").GetString());

        await frame.Locator("#df-workflow-file").ClickAsync();
        await Expect(frame.Locator("#df-timeline-meta")).ToContainTextAsync("host-picked.md");
        Assert.Equal(1, await _page.EvaluateAsync<int>("() => window.__workflowPicks"));
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
                    hostKind: 'test-host',
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
        await frame.Locator("#df-more").ClickAsync();
        await frame.Locator("#df-toggle-dock").ClickAsync();
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
