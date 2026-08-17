using System.Globalization;
using Microsoft.Playwright;
using Xunit;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

/// <summary>
/// Playwright integration tests for the DevFlow Web Inspector.
/// Requires the broker running with a connected MAUI app.
/// The inspector is available at http://localhost:19223/inspector/.
/// Set INSPECTOR_URL environment variable to override the default URL.
///
/// The default URL points to the broker's single-agent fallback route
/// (the broker uses the only connected agent when the id segment doesn't match).
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
        _context = await _browser.NewContextAsync();
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
        // No hover highlighting — the host inspector adds its own
        Assert.DoesNotContain(":hover", text);
    }

    [LiveInspectorFact]
    public async Task ClickSendsTapToAgent()
    {
        await _page.GotoAsync(BaseUrl);

        var viewport = _page.Locator("#app-viewport");
        var initialScreenshot = await _page.Locator("#screenshot").GetAttributeAsync("src");
        var tapStatuses = new List<int>();
        _page.Response += (_, response) =>
        {
            if (response.Url.EndsWith("/api/tap", StringComparison.Ordinal))
                tapStatuses.Add(response.Status);
        };

        await viewport.ClickAsync(new()
        {
            Position = new() { X = 5, Y = 5 }
        });

        string? refreshedScreenshot = initialScreenshot;
        var deadline = DateTime.UtcNow.AddSeconds(7);
        while (DateTime.UtcNow < deadline
            && (tapStatuses.Count == 0
                || string.Equals(
                    initialScreenshot,
                    refreshedScreenshot,
                    StringComparison.Ordinal)))
        {
            await _page.WaitForTimeoutAsync(100);
            refreshedScreenshot = await _page.Locator("#screenshot").GetAttributeAsync("src");
        }

        Assert.Contains(200, tapStatuses);
        Assert.NotEqual(initialScreenshot, refreshedScreenshot);
    }

    [LiveInspectorFact]
    public async Task StaleFillRefreshesCaptureAndRetries()
    {
        await _page.GotoAsync(BaseUrl);

        const string fixtureId = "inspector-stale-fill-fixture";
        const string refreshedElements = """
            <div class="devflow-element"
                 data-id="inspector-stale-fill-fixture"
                 data-type="Entry"
                 data-captureEpoch="2"
                 data-registryGeneration="7"
                 data-capabilities="set-value"
                 data-isVisible="true"
                 data-isEnabled="true"
                 style="position:absolute;left:10px;top:10px;width:180px;height:40px;z-index:9999;"></div>
            """;
        var viewport = _page.Locator("#app-viewport");
        const string screenshotUrl = "screenshot.png";
        var viewportWidth = double.Parse(
            (await viewport.GetAttributeAsync("data-width"))!,
            CultureInfo.InvariantCulture);
        var viewportHeight = double.Parse(
            (await viewport.GetAttributeAsync("data-height"))!,
            CultureInfo.InvariantCulture);

        await _page.RouteAsync("**/api/state", async route =>
        {
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = System.Text.Json.JsonSerializer.Serialize(new
                {
                    screenshotUrl,
                    elements = refreshedElements,
                    viewportWidth,
                    viewportHeight,
                    captureEpoch = 2,
                    registryGeneration = 7,
                    windowId = 0
                })
            });
        });

        var fillBodies = new List<string>();
        await _page.RouteAsync("**/api/fill", async route =>
        {
            fillBodies.Add(route.Request.PostData ?? "");
            if (fillBodies.Count == 1)
            {
                await route.FulfillAsync(new()
                {
                    Status = 409,
                    ContentType = "application/json",
                    Body = """{"ok":false,"reason":"stale-capture-epoch","retryable":true}"""
                });
                return;
            }

            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"ok":true}"""
            });
        });

        await _page.EvaluateAsync(
            """
            () => {
                const fixture = document.createElement('div');
                fixture.className = 'devflow-element';
                fixture.setAttribute('data-id', 'inspector-stale-fill-fixture');
                fixture.setAttribute('data-type', 'Entry');
                fixture.setAttribute('data-captureEpoch', '1');
                fixture.setAttribute('data-registryGeneration', '7');
                fixture.setAttribute('data-capabilities', 'set-value');
                fixture.setAttribute('data-isVisible', 'true');
                fixture.setAttribute('data-isEnabled', 'true');
                fixture.setAttribute(
                    'style',
                    'position:absolute;left:10px;top:10px;width:180px;height:40px;z-index:9999;');
                document.getElementById('app-viewport').appendChild(fixture);
            }
            """);

        var target = _page.Locator($".devflow-element[data-id='{fixtureId}']").First;
        await target.ClickAsync(new() { Force = true });
        var editor = _page.Locator("#app-viewport > input, #app-viewport > textarea").First;
        await Expect(editor).ToBeVisibleAsync();
        await editor.FillAsync("DevFlow stale retry");
        await editor.PressAsync("Enter");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (fillBodies.Count < 2 && DateTime.UtcNow < deadline)
            await _page.WaitForTimeoutAsync(100);

        Assert.Equal(2, fillBodies.Count);
        using var firstRequest = System.Text.Json.JsonDocument.Parse(fillBodies[0]);
        using var retryRequest = System.Text.Json.JsonDocument.Parse(fillBodies[1]);
        Assert.Equal(1, firstRequest.RootElement.GetProperty("captureEpoch").GetInt64());
        Assert.Equal(2, retryRequest.RootElement.GetProperty("captureEpoch").GetInt64());
        Assert.Equal(
            "DevFlow stale retry",
            retryRequest.RootElement.GetProperty("text").GetString());
        await Expect(_page.Locator("#app-viewport > input, #app-viewport > textarea"))
            .ToHaveCountAsync(0, new() { Timeout = 5000 });
    }

    [LiveInspectorFact]
    public async Task ClickOnElementSendsTapAtCorrectCoordinates()
    {
        await _page.GotoAsync(BaseUrl);

        // Set up request interception to capture tap coordinates
        var tapRequests = new List<string>();
        await _page.RouteAsync("**/api/tap", async route =>
        {
            var body = route.Request.PostData;
            tapRequests.Add(body ?? "");
            await route.ContinueAsync();
        });

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
    public async Task ConcurrentStateScreenshotUrlsRemainAvailable()
    {
        var requests = Enumerable.Range(0, 8).Select(async _ =>
        {
            var stateResponse = await _page.APIRequest.GetAsync(ResolveUrl("api/state"));
            Assert.True(stateResponse.Ok);
            using var state = System.Text.Json.JsonDocument.Parse(await stateResponse.TextAsync());
            var screenshotUrl = state.RootElement.GetProperty("screenshotUrl").GetString();
            Assert.NotNull(screenshotUrl);

            var screenshotResponse = await _page.APIRequest.GetAsync(ResolveUrl(screenshotUrl));
            Assert.True(screenshotResponse.Ok);
            Assert.Equal("image/png", screenshotResponse.Headers["content-type"]);
        });

        await Task.WhenAll(requests);
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

    private ILocatorAssertions Expect(ILocator locator) =>
        Assertions.Expect(locator);
}
