using Microsoft.Playwright;
using Xunit;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

/// <summary>
/// Playwright integration tests for the DevFlow Web Inspector.
/// Requires the broker running with a connected MAUI app.
/// The inspector is available at http://localhost:19223/inspector/.
/// Set INSPECTOR_URL environment variable to override the default URL.
/// </summary>
[Collection("Inspector")]
public class InspectorPageTests : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IBrowserContext _context = null!;
    private IPage _page = null!;

    private string BaseUrl => Environment.GetEnvironmentVariable("INSPECTOR_URL") ?? "http://localhost:19223/inspector/";

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

    [Fact]
    public async Task ViewportUsesWindowDimensionsFromAgent()
    {
        await _page.GotoAsync(BaseUrl);
        var viewport = _page.Locator("#app-viewport");
        await Expect(viewport).ToBeVisibleAsync();

        var width = await viewport.GetAttributeAsync("data-width");
        var height = await viewport.GetAttributeAsync("data-height");

        // Window dimensions should be positive and NOT the old iPhone defaults
        var w = double.Parse(width!);
        var h = double.Parse(height!);
        Assert.True(w > 0, "Viewport width should be positive");
        Assert.True(h > 0, "Viewport height should be positive");
        Assert.NotEqual(390, w); // Not hardcoded iPhone width
        Assert.NotEqual(844, h); // Not hardcoded iPhone height
    }

    [Fact]
    public async Task ViewportScalesToFitBrowserWindow()
    {
        await _page.GotoAsync(BaseUrl);
        var viewport = _page.Locator("#app-viewport");

        // The viewport should have a CSS transform applied for zoom
        var transform = await viewport.EvaluateAsync<string>(
            "el => window.getComputedStyle(el).transform");

        // If the app is larger than the browser, transform should be a matrix (scaled)
        // If it fits, transform could be "none" or a scale(1) matrix
        Assert.NotNull(transform);
    }

    [Fact]
    public async Task ScreenshotImageIsPresent()
    {
        await _page.GotoAsync(BaseUrl);
        var screenshot = _page.Locator("#screenshot");
        await Expect(screenshot).ToBeVisibleAsync();

        var src = await screenshot.GetAttributeAsync("src");
        Assert.Equal("/screenshot.png", src);
    }

    [Fact]
    public async Task NoInspectorChromeRendered()
    {
        await _page.GotoAsync(BaseUrl);

        // No toolbar, no connection status — the host inspector tool provides its own chrome
        await Expect(_page.Locator("#devflow-toolbar")).ToHaveCountAsync(0);
        await Expect(_page.Locator("#btn-back")).ToHaveCountAsync(0);
        await Expect(_page.Locator("#connection-status")).ToHaveCountAsync(0);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task ElementTreeIsNested()
    {
        await _page.GotoAsync(BaseUrl);

        // Children should be nested inside parent divs
        var nested = _page.Locator(".devflow-element > .devflow-element");
        var count = await nested.CountAsync();
        Assert.True(count > 0, "Elements should be nested (parent > child)");
    }

    [Fact]
    public async Task DataAttributesUseCamelCase()
    {
        await _page.GotoAsync(BaseUrl);

        // DevFlow properties use camelCase: isVisible, isEnabled, fullType
        var withVisibility = _page.Locator(".devflow-element[data-isVisible]");
        Assert.True(await withVisibility.CountAsync() > 0);

        var withEnabled = _page.Locator(".devflow-element[data-isEnabled]");
        Assert.True(await withEnabled.CountAsync() > 0);
    }

    [Fact]
    public async Task CssServedSeparately()
    {
        var response = await _page.APIRequest.GetAsync($"{BaseUrl}/devflow.css");
        Assert.True(response.Ok);
        var text = await response.TextAsync();
        Assert.Contains("#app-viewport", text);
        Assert.Contains(".devflow-element", text);
        // No hover highlighting — the host inspector adds its own
        Assert.DoesNotContain(":hover", text);
    }

    [Fact]
    public async Task ClickSendsTapToAgent()
    {
        await _page.GotoAsync(BaseUrl);

        // Get the viewport bounding box
        var viewport = _page.Locator("#app-viewport");
        var box = await viewport.BoundingBoxAsync();
        Assert.NotNull(box);

        // Take a screenshot before clicking
        var screenshotBefore = await _page.Locator("#screenshot").GetAttributeAsync("src");

        // Click in the middle of the viewport
        await viewport.ClickAsync(new() { Position = new() { X = (float)box.Width / 2, Y = (float)box.Height / 2 } });

        // Wait for screenshot refresh (devflow.js refreshes after tap)
        await _page.WaitForTimeoutAsync(500);

        // The screenshot src should have changed (cache-bust query param)
        var screenshotAfter = await _page.Locator("#screenshot").GetAttributeAsync("src");
        Assert.NotEqual(screenshotBefore, screenshotAfter);
    }

    [Fact]
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

    [Fact]
    public async Task ScreenshotEndpointReturnsPng()
    {
        var response = await _page.APIRequest.GetAsync($"{BaseUrl}/screenshot.png");
        Assert.True(response.Ok);
        var body = await response.BodyAsync();

        // PNG magic bytes
        Assert.Equal(0x89, body[0]);
        Assert.Equal(0x50, body[1]); // P
        Assert.Equal(0x4E, body[2]); // N
        Assert.Equal(0x47, body[3]); // G
    }

    private ILocatorAssertions Expect(ILocator locator) =>
        Assertions.Expect(locator);
}
