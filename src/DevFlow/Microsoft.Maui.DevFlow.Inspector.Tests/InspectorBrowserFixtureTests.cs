using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Playwright;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

[Trait("Category", "BrowserFixture")]
public sealed class InspectorBrowserFixtureTests(ITestOutputHelper output) : IAsyncLifetime
{
    private WebApplication? _agent;
    private InspectorServer? _inspector;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;
    private readonly ConcurrentQueue<(string Name, string Value)> _writes = new();
    private readonly ConcurrentQueue<string> _pageErrors = new();
    private readonly ConcurrentDictionary<IRequest, byte> _pendingRequests = new();
    private string _text = "Original text";
    private int _treeRequests;
    private volatile bool _rejectWrites;
    private readonly TaskCompletionSource _screenshotHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile TaskCompletionSource? _screenshotGate;
    private int _disposed;

    private IPage Page => _page!;
    private ILocator Row => Page.Locator(".df-prop-row[data-property-name='text']");
    private ILocator Editor => Row.Locator("textarea.df-field");
    private ILocator SourceButton => Row.Locator(".df-prop-source");
    private ILocator Overlay => Page.Locator(".devflow-element[data-id='fixture-label']");

    public async Task InitializeAsync()
    {
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Logging.ClearProviders();
            _agent = builder.Build();
            _agent.MapGet("/api/v1/agent/status", () => Results.Json(new
            {
                agent = new { framework = "maui", version = "fixture" },
                device = new { platform = "Fixture", windowWidth = 400, windowHeight = 300, displayDensity = 1 },
                app = new { name = "Loopback fixture" },
                running = true,
            }));
            _agent.MapGet("/api/v1/agent/capabilities", () => Results.Json(new
            {
                agent = new { framework = "maui" },
                capabilities = new Dictionary<string, object>(),
            }));
            _agent.MapPost("/api/v1/agent/recording", () => Results.Json(new { ok = true, recording = false, steps = 0 }));
            _agent.MapGet("/api/v1/ui/tree", () =>
            {
                Interlocked.Increment(ref _treeRequests);
                return Results.Json(new[] { Root() });
            });
            _agent.MapGet("/api/v1/ui/elements/{id}", () => Results.Json(Label()));
            _agent.MapGet("/api/v1/ui/elements/{id}/properties", () => Results.Json(new
            {
                properties = new object[]
                {
                    new { name = "Text", kind = "text", value = Volatile.Read(ref _text), writable = true },
                    new { name = "Opacity", kind = "number", value = 1, writable = true, min = 0, max = 1, step = 0.05 },
                },
            }));
            _agent.MapPut("/api/v1/ui/elements/{id}/properties/{name}", async (HttpContext context, string name) =>
            {
                using var body = await JsonDocument.ParseAsync(context.Request.Body);
                var value = body.RootElement.GetProperty("value").GetString()!;
                _writes.Enqueue((name, value));
                if (_rejectWrites)
                    return Results.Json(new { success = false }, statusCode: 400);
                if (name == "Text") Volatile.Write(ref _text, value);
                return Results.Json(new { success = true });
            });
            _agent.MapPost("/api/v1/ui/actions/tap", () =>
            {
                Volatile.Write(ref _text, "Tapped");
                return Results.Json(new { success = true });
            });
            _agent.MapGet("/api/v1/ui/screenshot", () => Results.File(
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAZAAAAEsCAIAAABi1XKVAAABdElEQVR42u3BMQEAAADCoPVPbQwfoAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD+Bn+3AAHo8fTrAAAAAElFTkSuQmCC"),
                "image/png"))
                .AddEndpointFilter(async (context, next) =>
                {
                    // Park only post-tap captures so the page's periodic poll cannot trip the gate first.
                    if (Volatile.Read(ref _text) == "Tapped" && _screenshotGate is { } gate)
                    {
                        _screenshotHeld.TrySetResult();
                        await gate.Task.WaitAsync(context.HttpContext.RequestAborted);
                    }
                    return await next(context);
                });
            await _agent.StartAsync();
            var agentPort = new Uri(_agent.Urls.Single()).Port;

            // InspectorServer does not expose its bound endpoint when passed port 0.
            // Retry only the small reservation-to-bind race, never use a shared fixed port.
            for (var attempt = 0; ; attempt++)
            {
                using var reservation = new TcpListener(IPAddress.Loopback, 0);
                reservation.Start();
                var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
                reservation.Stop();
                _inspector = new InspectorServer(port, "localhost", agentPort);
                try
                {
                    _inspector.Start();
                    _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
                    _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
                    _page = await _browser.NewPageAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } });
                    break;
                }
                catch (SocketException) when (attempt < 4)
                {
                    _inspector.Dispose();
                }
            }
            _page.PageError += (_, error) => _pageErrors.Enqueue(error);
            _page.Request += (_, request) => _pendingRequests.TryAdd(request, 0);
            _page.RequestFinished += (sender, request) => _pendingRequests.TryRemove(request, out _);
            _page.RequestFailed += (sender, request) => _pendingRequests.TryRemove(request, out _);
            // Track timers and pagehide solely for teardown; leave normal Inspector behavior intact.
            await Page.AddInitScriptAsync("""
                window.fixtureIntervals = [];
                window.fixtureTimeouts = [];
                window.fixturePagehide = [];
                const originalSetInterval = window.setInterval.bind(window);
                const originalSetTimeout = window.setTimeout.bind(window);
                const originalAddEventListener = window.addEventListener.bind(window);
                window.setInterval = (...args) => {
                  const id = originalSetInterval(...args);
                  window.fixtureIntervals.push(id);
                  return id;
                };
                window.setTimeout = (...args) => {
                  const id = originalSetTimeout(...args);
                  window.fixtureTimeouts.push(id);
                  return id;
                };
                window.addEventListener = (type, handler, ...args) => {
                  if (type === 'pagehide') window.fixturePagehide.push(handler);
                  return originalAddEventListener(type, handler, ...args);
                };
                """);
            await Page.GotoAsync($"http://localhost:{_inspector.Port}/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Page.WaitForFunctionAsync("""
                () => {
                  const image = document.querySelector('#screenshot');
                  return image?.complete && image.naturalWidth === 400 && image.naturalHeight === 300;
                }
                """);
            output.WriteLine($"Headless Chromium {_browser!.Version}; Inspector HTTP localhost:{_inspector.Port}; loopback agent port {agentPort}; decoded PNG 400x300.");
            await Page.Locator(".df-tree-node[data-tree-id='fixture-label']").ClickAsync();
            await Expect(Editor).ToHaveValueAsync("Original text");
            await Expect(Editor).ToBeEnabledAsync();
        }
        catch (Exception error)
        {
            var details = $"Browser errors: {string.Join("; ", _pageErrors)}. Pending HTTP: {string.Join(", ", _pendingRequests.Keys.Select(request => request.Url))}";
            try { await DisposeAsync(); }
            catch (Exception cleanupError) { details += $". Teardown: {cleanupError.Message}"; }
            throw new InvalidOperationException(details, error);
        }
    }

    [InspectorBrowserFixtureFact]
    public async Task RuntimeEdit_RefreshesRealStateWithoutLosingSelectionOrSourceDirtyState()
    {
        var initialTreeRequests = Volatile.Read(ref _treeRequests);
        await Editor.FillAsync("Updated through HTTP");
        await Editor.PressAsync("Tab");
        await Expect(SourceButton).ToHaveAttributeAsync("data-state", "dirty");
        await Expect(Overlay).ToHaveAttributeAsync("data-text", "Updated through HTTP", new() { Timeout = 10_000 });
        await Expect(Overlay).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bdf-selected\b"));
        await Expect(Page.Locator(".df-tree-node[data-tree-id='fixture-label']"))
            .ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Editor).ToHaveValueAsync("Updated through HTTP");
        await Expect(SourceButton).ToBeEnabledAsync();
        Assert.True(Volatile.Read(ref _treeRequests) > initialTreeRequests);
        Assert.Equal(("Text", "Updated through HTTP"), Assert.Single(_writes));
        Assert.Empty(_pageErrors);
    }

    [InspectorBrowserFixtureFact]
    public async Task Tap_ShowsUpdatingHintUntilSlowCaptureRefreshesPreview()
    {
        var hint = Page.Locator("#df-preview-pending");
        var button = Page.Locator(".devflow-element[data-id='fixture-button']");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _screenshotGate = gate;
        await Expect(hint).ToBeHiddenAsync();

        var stateRequests = 0;
        Page.Request += (_, request) =>
        {
            if (request.Url.EndsWith("/api/state", StringComparison.Ordinal)) Interlocked.Increment(ref stateRequests);
        };

        // The server retries a capture that a tap invalidates, so only a response held in the
        // browser is stale. Hold a real pre-tap /api/state response to keep a refresh in flight.
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync("**/api/state", async route =>
        {
            var response = await route.FetchAsync();
            held.SetResult();
            await release.Task;
            await route.FulfillAsync(new() { Response = response });
        }, new() { Times = 1 });
        try
        {
            // The online handler starts a debounced refresh, like a WebSocket event would.
            await Page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))");
            await held.Task.WaitAsync(TimeSpan.FromSeconds(10));

            await button.ClickAsync();
            await Expect(hint).ToBeVisibleAsync();
            // Outlast the 400 ms post-tap refresh timer so it fires while the stale refresh is in flight.
            await Page.WaitForTimeoutAsync(1000);
            // A second tap's refresh also waits on the stale one, and both must share one post-tap capture.
            await button.ClickAsync();
            await Page.WaitForTimeoutAsync(1000);
            var requestsBeforeRelease = Volatile.Read(ref stateRequests);
            release.SetResult();

            await _screenshotHeld.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Let a duplicate post-tap request, if any, arrive before counting.
            await Page.WaitForTimeoutAsync(500);
            await Expect(hint).ToBeVisibleAsync();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Original text");
            Assert.Equal(requestsBeforeRelease + 1, Volatile.Read(ref stateRequests));
        }
        finally
        {
            release.TrySetResult();
        }

        gate.SetResult();
        await Expect(Overlay).ToHaveAttributeAsync("data-text", "Tapped", new() { Timeout = 10_000 });
        await Expect(hint).ToBeHiddenAsync();
        Assert.Empty(_pageErrors);
    }

    [InspectorBrowserFixtureFact]
    public async Task SourceState_RequiresSuccessfulRuntimeEditBeforeEnablingPersistence()
    {
        await Expect(SourceButton).ToHaveAttributeAsync("data-state", "clean");
        await Expect(SourceButton).ToBeDisabledAsync();
        await Editor.FillAsync("Pending edit");
        await Expect(SourceButton).ToHaveAttributeAsync("data-state", "pending");
        await Expect(SourceButton).ToBeDisabledAsync();
        Assert.Empty(_writes);
        await Editor.PressAsync("Tab");
        await Expect(SourceButton).ToHaveAttributeAsync("data-state", "dirty");
        await Expect(SourceButton).ToBeEnabledAsync();
        Assert.Equal(("Text", "Pending edit"), Assert.Single(_writes));
        Assert.Empty(_pageErrors);
    }

    [InspectorBrowserFixtureFact]
    public async Task RejectedRuntimeEdit_ShowsErrorAndDoesNotEnableSourcePersistence()
    {
        _rejectWrites = true;
        await Editor.FillAsync("Rejected edit");
        await Editor.PressAsync("Tab");
        await Expect(Row.Locator("[role='alert']")).ToContainTextAsync("The running app rejected Text.");
        await Expect(Editor).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(SourceButton).ToHaveAttributeAsync("data-state", "pending");
        await Expect(SourceButton).ToBeDisabledAsync();
        Assert.Equal("Original text", Volatile.Read(ref _text));
        Assert.Equal(("Text", "Rejected edit"), Assert.Single(_writes));
        Assert.Empty(_pageErrors);
    }

    [InspectorBrowserFixtureFact]
    public async Task PropertyFilter_AndInvalidNumericEdit_DoNotWriteToAgent()
    {
        await Page.Locator("#df-prop-filter").FillAsync("opacity");
        await Expect(Row).ToBeHiddenAsync();
        var opacity = Page.Locator(".df-prop-row[data-property-name='opacity']");
        await Expect(opacity).ToBeVisibleAsync();
        var editor = opacity.Locator("input.df-field");
        await Expect(editor).ToHaveAttributeAsync("max", "1");
        await editor.FillAsync("2");
        await editor.PressAsync("Tab");
        await Expect(editor).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(opacity.Locator(".df-prop-source")).ToBeDisabledAsync();
        Assert.Empty(_writes);
        await Page.Locator("#df-prop-filter").FillAsync("no-such-property");
        await Expect(Page.Locator(".df-prop-no-results")).ToBeVisibleAsync();
        Assert.Empty(_pageErrors);
    }

    private object Label() => new
    {
        id = "fixture-label", type = "Label", automationId = "FixtureLabel",
        text = Volatile.Read(ref _text), isVisible = true, isEnabled = true,
        bounds = new { x = 20, y = 30, width = 250, height = 40 },
        sourceFile = "FixturePage.xaml", sourceLine = 2,
        children = new[] { Button() },
    };

    // Nested under the Label because the Inspector scopes each frame to the window's last child.
    private static object Button() => new
    {
        id = "fixture-button", type = "Button", text = "Tap me", isVisible = true, isEnabled = true,
        bounds = new { x = 20, y = 90, width = 160, height = 44 },
        children = Array.Empty<object>(),
    };

    private object Root() => new
    {
        id = "fixture-page", type = "ContentPage", isVisible = true, isEnabled = true,
        bounds = new { x = 0, y = 0, width = 400, height = 300 },
        children = new[] { Label() },
    };

    public async Task DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _screenshotGate?.TrySetResult();
        try
        {
            if (_page is { IsClosed: false })
            {
                // Avoid scheduling a new keepalive release after the drain. This fixture uses
                // an isolated compatibility lease; pagehide lease behavior is not under test.
                await _page.EvaluateAsync("""
                    () => {
                      window.fixtureIntervals?.forEach(id => clearInterval(id));
                      window.fixtureTimeouts?.forEach(id => clearTimeout(id));
                      for (const handler of window.fixturePagehide || []) {
                        window.removeEventListener('pagehide', handler);
                      }
                    }
                    """);
                // Do not dispose Inspector semaphores while its untracked HTTP handlers run.
                // With polling stopped, network-idle drains real requests before browser closure.
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                Assert.Empty(_pendingRequests);
            }
        }
        finally
        {
            try
            {
                if (_browser is not null) await _browser.DisposeAsync();
            }
            finally
            {
                _playwright?.Dispose();
                try
                {
                    if (_inspector is not null)
                    {
                        await _inspector.StopAsync();
                        _inspector.Dispose();
                    }
                }
                finally
                {
                    if (_agent is not null)
                    {
                        await _agent.StopAsync();
                        await _agent.DisposeAsync();
                    }
                }
            }
        }
    }
}
