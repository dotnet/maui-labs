using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private const int ClosedFlyoutX = -320;
    private const int OpenFlyoutX = 20;

    private WebApplication? _agent;
    private InspectorServer? _inspector;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;
    private readonly ConcurrentQueue<(string Name, string Value)> _writes = new();
    private readonly ConcurrentQueue<string> _pageErrors = new();
    private readonly ConcurrentDictionary<IRequest, byte> _pendingRequests = new();
    private string _text = "Original text";
    private int _flyoutX = ClosedFlyoutX;
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
    private ILocator ColorRow(string name) => Page.Locator($".df-prop-row[data-property-name='{name.ToLowerInvariant()}']");

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
                capabilities = new Dictionary<string, object>
                {
                    ["ui.screenshot"] = new
                    {
                        version = 3,
                        features = new[]
                        {
                            "element",
                            "fullscreen",
                            "resolution-hints"
                        }
                    }
                },
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
                    // The Agent reads colors as #RRGGBB, or #RRGGBBAA when alpha is not FF.
                    new { name = "BackgroundColor", kind = "color", value = "#00000000", writable = true },
                    new { name = "TextColor", kind = "color", value = "#FFFF00", writable = true },
                    new { name = "BorderColor", kind = "color", value = "#00FF0080", writable = true },
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
    public async Task Tap_ForcedRefreshSupersedesStalledStateFetchAndIgnoresLateResponse()
    {
        var hint = Page.Locator("#df-preview-pending");
        var button = Page.Locator(".devflow-element[data-id='fixture-button']");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _screenshotGate = gate;
        await Expect(hint).ToBeHiddenAsync();

        // A Playwright route cannot expose a late response after the browser aborts it.
        await Page.EvaluateAsync("""
            () => {
              const realFetch = window.fetch.bind(window);
              let heldState = false;
              window.fixtureRestoreFetch = () => { window.fetch = realFetch; };
              window.fetch = (url, options = {}) => {
                if (!heldState && typeof url === 'string' && url.endsWith('/api/state')) {
                  heldState = true;
                  options.signal?.addEventListener('abort', () => {
                    document.body.dataset.fixtureHeldStateAborted = 'true';
                  }, { once: true });
                  const forwarded = { ...options };
                  delete forwarded.signal;
                  return realFetch(url, forwarded).then(async response => {
                    const body = await response.text();
                    const responseInit = {
                      status: response.status,
                      statusText: response.statusText,
                      headers: response.headers,
                    };
                    document.body.dataset.fixtureHeldStateStarted = 'true';
                    return new Promise(resolve => {
                      window.fixtureReleaseHeldState = () => {
                        const staleResponse = new Response(body, responseInit);
                        const readJson = staleResponse.json.bind(staleResponse);
                        staleResponse.json = async () => {
                          const value = await readJson();
                          document.body.dataset.fixtureHeldStateConsumed = 'true';
                          return value;
                        };
                        resolve(staleResponse);
                      };
                    });
                  });
                }
                return realFetch(url, options);
              };
            }
            """);
        try
        {
            await Page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))");
            await Expect(Page.Locator("body"))
                .ToHaveAttributeAsync("data-fixture-held-state-started", "true");

            await button.ClickAsync();
            await _screenshotHeld.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Expect(Page.Locator("body"))
                .ToHaveAttributeAsync("data-fixture-held-state-aborted", "true");
            await Expect(hint).ToBeVisibleAsync();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Original text");
            await Expect(Page.Locator("#df-disconnected-overlay")).ToBeHiddenAsync();

            gate.TrySetResult();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Tapped");
            await Expect(hint).ToBeHiddenAsync();
            var currentScreenshot = await Page.EvaluateAsync<string>(
                "() => document.querySelector('#screenshot').src");

            await Page.EvaluateAsync("""
                async () => {
                  window.fixtureReleaseHeldState();
                  window.fixtureRestoreFetch();
                  await new Promise(resolve => setTimeout(resolve, 0));
                  await new Promise(resolve => setTimeout(resolve, 0));
                  document.body.dataset.fixtureHeldStateSettled = 'true';
                }
                """);
            await Expect(Page.Locator("body"))
                .ToHaveAttributeAsync("data-fixture-held-state-settled", "true");
            await Expect(Page.Locator("body")).Not
                .ToHaveAttributeAsync("data-fixture-held-state-consumed", "true");
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Tapped");
            Assert.Equal(currentScreenshot, await Page.EvaluateAsync<string>(
                "() => document.querySelector('#screenshot').src"));
            await Expect(Page.Locator("#df-disconnected-overlay")).ToBeHiddenAsync();
            Assert.Empty(_pageErrors);
        }
        finally
        {
            gate.TrySetResult();
            await Page.EvaluateAsync("""
                () => {
                  window.fixtureReleaseHeldState?.();
                  window.fixtureRestoreFetch?.();
                }
                """);
        }
    }

    [InspectorBrowserFixtureFact]
    public async Task Tap_ScheduledRefreshSharesStalledCapture_ButExplicitRefreshSupersedesIt()
    {
        var hint = Page.Locator("#df-preview-pending");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _screenshotGate = gate;
        var stateRequests = 0;
        Page.Request += (_, request) =>
        {
            if (request.Url.EndsWith("/api/state", StringComparison.Ordinal))
                Interlocked.Increment(ref stateRequests);
        };
        await Page.EvaluateAsync("""
            () => {
              const originalDecode = HTMLImageElement.prototype.decode;
              const originalSetTimeout = window.setTimeout;
              let heldDecode = false;
              let releaseHeldDecode;
              const pendingDecode = new Promise(resolve => { releaseHeldDecode = resolve; });
              window.fixtureReleaseHeldDecode = () => {
                releaseHeldDecode();
                return new Promise(done => setTimeout(() => {
                  document.body.dataset.fixtureHeldDecodeReleased = 'true';
                  done();
                }, 0));
              };
              window.fixtureRestoreImageDecode = () => {
                HTMLImageElement.prototype.decode = originalDecode;
              };
              window.fixtureRestoreSetTimeout = () => {
                window.setTimeout = originalSetTimeout;
              };
              window.setTimeout = (handler, delay, ...args) => {
                if (delay === 3000) {
                  document.body.dataset.fixtureRetryScheduled = 'true';
                }
                return originalSetTimeout(handler, delay, ...args);
              };
              HTMLImageElement.prototype.decode = function () {
                if (!this.id && !heldDecode) {
                  heldDecode = true;
                  document.body.dataset.fixtureHeldDecodeStarted = 'true';
                  return pendingDecode;
                }
                return originalDecode.call(this);
              };
            }
            """);
        try
        {
            await Page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))");
            await Expect(Page.Locator("body"))
                .ToHaveAttributeAsync("data-fixture-held-decode-started", "true");
            var requestsBeforeTap = Volatile.Read(ref stateRequests);

            await Page.Locator(".devflow-element[data-id='fixture-button']").ClickAsync();
            await _screenshotHeld.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(requestsBeforeTap + 1, Volatile.Read(ref stateRequests));
            await Expect(Page.Locator("body")).Not
                .ToHaveAttributeAsync("data-fixture-retry-scheduled", "true");
            await Expect(hint).ToBeVisibleAsync();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Original text");
            await Expect(Page.Locator("#df-disconnected-overlay")).ToBeHiddenAsync();
            await Expect(Page.Locator("#df-disconnected-overlay"))
                .ToHaveAttributeAsync("aria-busy", "true");

            var explicitRefreshStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Page.Request += (_, request) =>
            {
                if (request.Url.EndsWith("/api/state", StringComparison.Ordinal))
                    explicitRefreshStarted.TrySetResult();
            };
            await Page.Locator("#df-retry-connection").DispatchEventAsync("click");
            await explicitRefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(requestsBeforeTap + 2, Volatile.Read(ref stateRequests));
            await Expect(hint).ToBeVisibleAsync();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Original text");

            gate.TrySetResult();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Tapped");
            await Expect(hint).ToBeHiddenAsync();
            var currentScreenshot = await Page.EvaluateAsync<string>(
                "() => document.querySelector('#screenshot').src");

            await Page.EvaluateAsync("() => window.fixtureReleaseHeldDecode()");
            await Expect(Page.Locator("body"))
                .ToHaveAttributeAsync("data-fixture-held-decode-released", "true");
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Tapped");
            Assert.Equal(currentScreenshot, await Page.EvaluateAsync<string>(
                "() => document.querySelector('#screenshot').src"));
            await Expect(Page.Locator("#df-disconnected-overlay")).ToBeHiddenAsync();
            Assert.Empty(_pageErrors);
        }
        finally
        {
            gate.TrySetResult();
            await Page.EvaluateAsync("""
                () => {
                  window.fixtureReleaseHeldDecode?.();
                  window.fixtureRestoreImageDecode?.();
                  window.fixtureRestoreSetTimeout?.();
                }
                """);
        }
    }

    [InspectorBrowserFixtureFact]
    public async Task Tap_NewerAcceptedTapKeepsPendingFeedbackUntilItsFrameCommits()
    {
        var hint = Page.Locator("#df-preview-pending");
        var button = Page.Locator(".devflow-element[data-id='fixture-button']");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _screenshotGate = gate;
        var stateRequests = 0;
        Page.Request += (_, request) =>
        {
            if (request.Url.EndsWith("/api/state", StringComparison.Ordinal))
                Interlocked.Increment(ref stateRequests);
        };
        var requestsBeforeTap = Volatile.Read(ref stateRequests);

        try
        {
            await button.ClickAsync();
            await _screenshotHeld.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Expect(hint).ToBeVisibleAsync();
            Assert.Equal(requestsBeforeTap + 1, Volatile.Read(ref stateRequests));

            var secondRefreshStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Page.Request += (_, request) =>
            {
                if (request.Url.EndsWith("/api/state", StringComparison.Ordinal))
                    secondRefreshStarted.TrySetResult();
            };
            await button.ClickAsync();
            await secondRefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(requestsBeforeTap + 2, Volatile.Read(ref stateRequests));
            await Expect(hint).ToBeVisibleAsync();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Original text");

            gate.TrySetResult();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Tapped");
            await Expect(hint).ToBeHiddenAsync();
            Assert.Empty(_pageErrors);
        }
        finally
        {
            gate.TrySetResult();
        }
    }

    [InspectorBrowserFixtureFact]
    public async Task Tap_FrameMetadataCommitsAtomicallyWithDecodedScreenshot()
    {
        string? committedFrameId = null;
        await Page.RouteAsync("**/api/state", async route =>
        {
            var response = await route.FetchAsync();
            var responseBody = await response.TextAsync();
            var state = JsonNode.Parse(responseBody)!.AsObject();
            var elements = state["elements"]?.GetValue<string>() ?? string.Empty;
            if (!elements.Contains("data-text=\"Tapped\"", StringComparison.Ordinal) ||
                committedFrameId is not null)
            {
                await route.FulfillAsync(new()
                {
                    Status = response.Status,
                    ContentType = response.Headers.GetValueOrDefault("content-type"),
                    Body = responseBody
                });
                return;
            }

            var frame = state["frame"]!.AsObject();
            committedFrameId = frame["frameId"]!.GetValue<string>();
            frame["logicalWidth"] = 200;
            frame["logicalHeight"] = 150;
            frame["rootOffsetX"] = 11;
            frame["rootOffsetY"] = 13;
            frame["captureScale"] = 2;
            state["viewportWidth"] = 200;
            state["viewportHeight"] = 150;
            state["rootOffsetX"] = 11;
            state["rootOffsetY"] = 13;
            state["elements"] = elements.Replace(
                "data-text=\"Tapped\"",
                "data-text=\"Frame B\"",
                StringComparison.Ordinal);
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = state.ToJsonString()
            });
        });
        await Page.EvaluateAsync("""
            () => {
              window.fixtureOriginalImageDecode = HTMLImageElement.prototype.decode;
              window.fixtureDecodeStarted = false;
              window.fixtureReleaseDecode = null;
              HTMLImageElement.prototype.decode = function () {
                if (!this.id && !window.fixtureDecodeStarted) {
                  window.fixtureDecodeStarted = true;
                  document.body.dataset.fixtureDecodeStarted = 'true';
                  return new Promise(resolve => {
                    window.fixtureReleaseDecode = () => {
                      resolve();
                      return new Promise(done => setTimeout(() => {
                        document.body.dataset.fixtureDecodeReleased = 'true';
                        done();
                      }, 0));
                    };
                  });
                }
                return window.fixtureOriginalImageDecode.call(this);
              };
            }
            """);
        try
        {
            await Page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))");
            await Expect(Page.Locator("body"))
                .ToHaveAttributeAsync("data-fixture-decode-started", "true");

            await Page.Locator(".devflow-element[data-id='fixture-button']").ClickAsync();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Frame B", new() { Timeout = 10_000 });
            await Expect(Page.Locator("#app-viewport")).ToHaveAttributeAsync("data-width", "200");
            await Expect(Page.Locator("#app-viewport")).ToHaveAttributeAsync("data-height", "150");
            await Expect(Page.Locator("#app-viewport")).ToHaveAttributeAsync("data-root-offset-x", "11");
            await Expect(Page.Locator("#app-viewport")).ToHaveAttributeAsync("data-root-offset-y", "13");
            Assert.Equal(
                200,
                await Page.Locator("#screenshot")
                    .EvaluateAsync<double>("element => parseFloat(getComputedStyle(element).width)"));
            Assert.Equal(
                150,
                await Page.Locator("#screenshot")
                    .EvaluateAsync<double>("element => parseFloat(getComputedStyle(element).height)"));
            Assert.NotNull(committedFrameId);
            await Expect(Page.Locator("#screenshot"))
                .ToHaveAttributeAsync("src", new System.Text.RegularExpressions.Regex($"frame={committedFrameId}$"));

            await Page.EvaluateAsync("() => window.fixtureReleaseDecode?.()");
            await Expect(Page.Locator("body"))
                .ToHaveAttributeAsync("data-fixture-decode-released", "true");
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Frame B");
            await Expect(Page.Locator("#app-viewport")).ToHaveAttributeAsync("data-width", "200");
            Assert.Empty(_pageErrors);
        }
        finally
        {
            await Page.EvaluateAsync("""
                () => {
                  window.fixtureReleaseDecode?.();
                  if (window.fixtureOriginalImageDecode) {
                    HTMLImageElement.prototype.decode = window.fixtureOriginalImageDecode;
                  }
                }
                """);
        }
    }

    [InspectorBrowserFixtureFact]
    public async Task Tap_ShowsUpdatingHintBeforeAcknowledgement_AndClearsRejectedTap()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync("**/api/tap", async route =>
        {
            started.TrySetResult();
            await release.Task;
            await route.FulfillAsync(new()
            {
                Status = 400,
                ContentType = "application/json",
                Body = """{"ok":false,"reason":"Fixture rejected the tap."}"""
            });
        });
        try
        {
            await Page.Locator(".devflow-element[data-id='fixture-button']").ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Expect(Page.Locator("#df-preview-pending")).ToBeVisibleAsync(new() { Timeout = 1000 });
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Original text");
            release.TrySetResult();
            await Expect(Page.Locator("#df-status")).ToContainTextAsync("Fixture rejected the tap.");
            await Expect(Page.Locator("#df-preview-pending")).ToBeHiddenAsync();
            Assert.Empty(_pageErrors);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [InspectorBrowserFixtureFact]
    public async Task Tap_FailedRefreshKeepsHintUntilAutomaticRetryRenders()
    {
        var requests = 0;
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync("**/api/state", async route =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                await route.FulfillAsync(new()
                {
                    Status = 503,
                    ContentType = "application/json",
                    Body = """{"ok":false,"retryable":true}"""
                });
                return;
            }
            retryStarted.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        try
        {
            await Page.Locator(".devflow-element[data-id='fixture-button']").ClickAsync();
            await Expect(Page.Locator("#df-disconnected-overlay")).ToBeVisibleAsync();
            await Expect(Page.Locator("#df-preview-pending")).ToBeVisibleAsync(new() { Timeout = 1000 });
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Original text");
            await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            release.TrySetResult();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Tapped");
            await Expect(Page.Locator("#df-preview-pending")).ToBeHiddenAsync();
            Assert.Empty(_pageErrors);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [InspectorBrowserFixtureFact]
    public async Task Tap_KeepsHintAndPreviousFrameUntilScreenshotLoads()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync("**/screenshot.png?frame=*", async route =>
        {
            started.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        try
        {
            await Page.Locator(".devflow-element[data-id='fixture-button']").ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Expect(Page.Locator("#df-preview-pending")).ToBeVisibleAsync(new() { Timeout = 1000 });
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Original text");
            release.TrySetResult();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Tapped");
            await Expect(Page.Locator("#df-preview-pending")).ToBeHiddenAsync();
            Assert.Empty(_pageErrors);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [InspectorBrowserFixtureFact]
    public async Task Tap_FailedScreenshotKeepsPreviousFrameUntilRetryLoads()
    {
        var requests = 0;
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync("**/screenshot.png?frame=*", async route =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                await route.FulfillAsync(new() { Status = 404, Body = "Inspector frame expired" });
                return;
            }
            retryStarted.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        try
        {
            await Page.Locator(".devflow-element[data-id='fixture-button']").ClickAsync();
            await Expect(Page.Locator("#df-disconnected-overlay")).ToBeVisibleAsync();
            await Expect(Page.Locator("#df-preview-pending")).ToBeVisibleAsync();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Original text");
            await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            release.TrySetResult();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Tapped");
            await Expect(Page.Locator("#df-preview-pending")).ToBeHiddenAsync();
            Assert.Empty(_pageErrors);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [InspectorBrowserFixtureFact]
    public async Task Tap_StalledScreenshotDecodeObservesStateDeadline()
    {
        await Page.Clock.InstallAsync();
        await Page.Clock.PauseAtAsync(DateTime.UtcNow.AddSeconds(1));
        await Page.EvaluateAsync("""
            () => {
              window.fixtureOriginalImageDecode = HTMLImageElement.prototype.decode;
              window.fixtureDecodeStarted = false;
              HTMLImageElement.prototype.decode = function () {
                if (!this.id && !window.fixtureDecodeStarted) {
                  window.fixtureDecodeStarted = true;
                  document.body.dataset.fixtureDecodeStarted = 'true';
                  return new Promise(() => {});
                }
                return window.fixtureOriginalImageDecode.call(this);
              };
            }
            """);
        try
        {
            await Page.Locator(".devflow-element[data-id='fixture-button']").ClickAsync();
            await Expect(Page.Locator("#df-status")).ToContainTextAsync("Tap sent to");
            await Page.Locator("#screenshot").DispatchEventAsync("error");
            await Page.Clock.RunForAsync(50);
            await Expect(Page.Locator("body")).ToHaveAttributeAsync("data-fixture-decode-started", "true");
            await Expect(Page.Locator("#df-disconnected-overlay")).ToHaveAttributeAsync("aria-busy", "true");
            await Page.Clock.RunForAsync(10_100);
            await Expect(Page.Locator("#df-disconnected-overlay")).ToBeVisibleAsync();
            await Expect(Page.Locator("#df-disconnected-overlay")).Not.ToHaveAttributeAsync("aria-busy", "true");
            await Expect(Page.Locator("#df-preview-pending")).ToBeVisibleAsync();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Original text");
            Assert.Empty(_pageErrors);
        }
        finally
        {
            await Page.EvaluateAsync("""
                () => {
                  if (window.fixtureOriginalImageDecode) {
                    HTMLImageElement.prototype.decode = window.fixtureOriginalImageDecode;
                  }
                }
                """);
        }
    }

    [InspectorBrowserFixtureFact]
    public async Task Tap_RejectedLaterTapDoesNotClearAcceptedTapPendingCapture()
    {
        _screenshotGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var button = Page.Locator(".devflow-element[data-id='fixture-button']");
            await button.ClickAsync();
            await _screenshotHeld.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Page.RouteAsync("**/api/tap", route => route.FulfillAsync(new()
            {
                Status = 400,
                ContentType = "application/json",
                Body = """{"ok":false,"reason":"Fixture rejected the later tap."}"""
            }));
            await button.ClickAsync();
            await Expect(Page.Locator("#df-status")).ToContainTextAsync("Fixture rejected the later tap.");
            await Expect(Page.Locator("#df-preview-pending")).ToBeVisibleAsync();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Original text");
            _screenshotGate.TrySetResult();
            await Expect(Overlay).ToHaveAttributeAsync("data-text", "Tapped");
            await Expect(Page.Locator("#df-preview-pending")).ToBeHiddenAsync();
            Assert.Empty(_pageErrors);
        }
        finally
        {
            _screenshotGate.TrySetResult();
        }
    }

    [InspectorBrowserFixtureFact]
    public async Task Tap_StartsPostAcknowledgementCaptureWithoutFixedWait()
    {
        await Page.Clock.InstallAsync();
        await Page.Clock.PauseAtAsync(DateTime.UtcNow.AddSeconds(1));
        _screenshotGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Page.Locator(".devflow-element[data-id='fixture-button']").ClickAsync();
            await Expect(Page.Locator("#df-status")).ToContainTextAsync("Tap sent to");
            await Page.Clock.RunForAsync(50);
            await _screenshotHeld.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Expect(Page.Locator("#df-preview-pending")).ToBeVisibleAsync();
            Assert.Empty(_pageErrors);
        }
        finally
        {
            _screenshotGate.TrySetResult();
        }
    }

    [InspectorBrowserFixtureFact]
    public async Task Tap_UnchangedImmediateCaptureRetriesDelayedUiMutation()
    {
        await Page.Clock.InstallAsync();
        await Page.Clock.PauseAtAsync(DateTime.UtcNow.AddSeconds(1));
        var firstStateCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        await Page.RouteAsync("**/api/tap", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"ok":true,"elementId":"fixture-button"}"""
        }));
        await Page.RouteAsync("**/api/state", async route =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                var response = await route.FetchAsync();
                await route.FulfillAsync(new() { Response = response });
                firstStateCompleted.TrySetResult();
                return;
            }

            retryStarted.TrySetResult();
            await route.ContinueAsync();
        });

        await Page.Locator(".devflow-element[data-id='fixture-button']").ClickAsync();
        await Expect(Page.Locator("#df-status")).ToContainTextAsync("Tap sent to");
        await Page.Clock.RunForAsync(50);
        await firstStateCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Expect(Page.Locator("#df-preview-pending")).ToBeVisibleAsync();
        await Expect(Overlay).ToHaveAttributeAsync("data-text", "Original text");
        await Expect(Page.Locator("#df-disconnected-overlay")).Not.ToHaveAttributeAsync("aria-busy", "true");

        Volatile.Write(ref _text, "Tapped");
        await Page.Clock.RunForAsync(299);
        Assert.False(retryStarted.Task.IsCompleted);
        await Page.Clock.RunForAsync(51);
        await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Expect(Overlay).ToHaveAttributeAsync("data-text", "Tapped");
        await Expect(Page.Locator("#df-preview-pending")).ToBeHiddenAsync();
        Assert.Empty(_pageErrors);
    }

    [InspectorBrowserFixtureFact]
    public async Task Resize_NegotiatesOneBucketedScreenshotAfterDebounce()
    {
        var negotiatedRequests = new ConcurrentQueue<string>();
        var negotiated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Page.Request += (_, request) =>
        {
            if (request.Url.Contains("/api/state?", StringComparison.Ordinal)
                && request.Url.Contains("targetScale=", StringComparison.Ordinal))
            {
                negotiatedRequests.Enqueue(request.Url);
                negotiated.TrySetResult();
            }
        };

        await Page.EvaluateAsync("""
            () => {
              const wrap = document.querySelector('#df-viewport-wrap');
              wrap.style.flex = '0 0 80px';
              wrap.style.width = '80px';
              wrap.style.height = '60px';
              document.querySelector('#df-toggle-fit').click();
              document.querySelector('#df-toggle-fit').click();
            }
            """);
        Assert.Contains(
            "scale(",
            await Page.Locator("#app-viewport").GetAttributeAsync("style") ?? string.Empty,
            StringComparison.Ordinal);
        await Page.EvaluateAsync("""
            () => {
              const wrap = document.querySelector('#df-viewport-wrap');
              wrap.style.flexBasis = '84px';
              wrap.style.width = '84px';
              document.querySelector('#df-toggle-fit').click();
              document.querySelector('#df-toggle-fit').click();
              wrap.style.flexBasis = '82px';
              wrap.style.width = '82px';
              document.querySelector('#df-toggle-fit').click();
              document.querySelector('#df-toggle-fit').click();
            }
            """);
        for (var refresh = 0; refresh < 10 && !negotiated.Task.IsCompleted; refresh++)
        {
            await Page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))");
            await Page.WaitForTimeoutAsync(300);
        }
        Assert.True(
            negotiated.Task.IsCompleted,
            "Continuous app refreshes postponed the stable resolution downgrade.");
        await negotiated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Page.WaitForTimeoutAsync(250);

        var request = Assert.Single(negotiatedRequests);
        Assert.Matches(@"[?&]targetScale=(?:0\.25|0\.5|0\.75|1\.5|2|3|4)(?:&|$)", request);
        await Expect(Page.Locator("#df-disconnected-overlay")).ToBeHiddenAsync();
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
    public async Task CompactLayout_DocksPropertiesBesideScreenshot_AndFitRefitsAroundIt()
    {
        // 900px is compact, and beside the Properties pane the 400px screenshot has to shrink to fit.
        await Page.SetViewportSizeAsync(900, 780);
        await Expect(Page.Locator("body")).ToHaveAttributeAsync("data-host-layout", "compact");
        await WaitForScreenshotFitAsync(propertiesOpen: true);
        await Expect(Page.Locator("#df-pane-scrim")).ToBeHiddenAsync();

        // Nothing is dimmed or covered, so the close button and Escape are what dismiss Properties.
        await Page.Locator("#df-props-close").ClickAsync();
        await Expect(Page.Locator("#df-props-pane")).ToBeHiddenAsync();
        await WaitForScreenshotFitAsync(propertiesOpen: false);

        await Page.Locator(".df-tree-node[data-tree-id='fixture-label']").ClickAsync();
        await WaitForScreenshotFitAsync(propertiesOpen: true);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator("#df-props-pane")).ToBeHiddenAsync();
        await WaitForScreenshotFitAsync(propertiesOpen: false);
        Assert.Empty(_pageErrors);
    }

    // Fit re-runs from a ResizeObserver and a requestAnimationFrame, so poll until it settles.
    private Task WaitForScreenshotFitAsync(bool propertiesOpen) => Page.WaitForFunctionAsync("""
        propertiesOpen => {
          const shot = document.getElementById('screenshot').getBoundingClientRect();
          const props = document.getElementById('df-props-pane').getBoundingClientRect();
          return propertiesOpen
            ? shot.width < 400 && shot.right <= props.left + 0.5
            : Math.round(shot.width) === 400;
        }
        """, propertiesOpen);

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

    [InspectorBrowserFixtureFact]
    public async Task OffscreenElement_IsDimmedInTreeAndNotDrawnOnPreview()
    {
        var flyoutRow = Page.Locator(".devflow-element[data-id='fixture-flyout-row']");
        var flyoutTreeRow = Page.Locator(".df-tree-node[data-tree-id='fixture-flyout-row']");
        var labelTreeRow = Page.Locator(".df-tree-node[data-tree-id='fixture-label']");
        var badge = Page.Locator("#df-badge");
        var status = Page.Locator("#df-status");
        var dimmed = new System.Text.RegularExpressions.Regex(@"\bdf-hidden-el\b");

        await Expect(flyoutRow).ToHaveAttributeAsync("data-offscreen", "true");
        await Expect(flyoutRow).ToBeHiddenAsync();
        await Expect(flyoutTreeRow).ToHaveClassAsync(dimmed);
        await Expect(Overlay).Not.ToHaveAttributeAsync("data-offscreen", "true");
        await Expect(labelTreeRow).Not.ToHaveClassAsync(dimmed);

        await flyoutTreeRow.HoverAsync();
        await Expect(flyoutRow).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bdf-hover\b"));
        await Expect(badge).ToBeHiddenAsync();

        await flyoutTreeRow.ClickAsync();
        await Expect(status).ToHaveTextAsync("Grid (off-screen)");

        // Opening the flyout brings the row back into the Preview and the tree.
        Volatile.Write(ref _flyoutX, OpenFlyoutX);
        await Expect(flyoutRow).Not.ToHaveAttributeAsync("data-offscreen", "true", new() { Timeout = 10_000 });
        await Expect(flyoutRow).ToBeVisibleAsync();
        await Expect(flyoutTreeRow).Not.ToHaveClassAsync(dimmed);
        await Expect(status).ToHaveTextAsync("Grid");

        // Closing it again dismisses a hover on it even though the pointer never moves.
        await flyoutRow.HoverAsync();
        await Expect(badge).ToBeVisibleAsync();
        Volatile.Write(ref _flyoutX, ClosedFlyoutX);
        await Expect(flyoutRow).ToHaveAttributeAsync("data-offscreen", "true", new() { Timeout = 10_000 });
        await Expect(badge).ToBeHiddenAsync();
        await Expect(status).ToHaveTextAsync("Grid (off-screen)");

        // A refresh only updates the selection's own status text, never another message.
        await Page.Locator("#df-mode-inspect").ClickAsync();
        Volatile.Write(ref _flyoutX, OpenFlyoutX);
        await Expect(flyoutRow).Not.ToHaveAttributeAsync("data-offscreen", "true", new() { Timeout = 10_000 });
        await Expect(status).ToContainTextAsync("Inspect mode");
        Assert.Empty(_pageErrors);
    }

    [InspectorBrowserFixtureFact]
    public async Task TransparentColor_ShowsPlaceholder_AndPickIsWrittenOpaque()
    {
        var row = ColorRow("BackgroundColor");
        var editor = row.Locator("input[type='color']");
        await Expect(row.Locator(".df-color-unset-label")).ToHaveTextAsync("Transparent");
        await Expect(editor).ToHaveAttributeAsync("aria-label", "BackgroundColor: transparent; activate to choose a color");
        // The input is invisible inside the placeholder, so the placeholder itself must show keyboard focus.
        var placeholder = row.Locator(".df-color-unset");
        var border = await placeholder.EvaluateAsync<string>("element => getComputedStyle(element).borderTopColor");
        await editor.FocusAsync();
        Assert.NotEqual(border, await placeholder.EvaluateAsync<string>("element => getComputedStyle(element).borderTopColor"));
        await editor.FillAsync("#0000ff");
        await Expect(row.Locator(".df-prop-source")).ToHaveAttributeAsync("data-state", "dirty");
        await Expect(placeholder).ToHaveCountAsync(0);
        await Expect(editor).ToHaveAttributeAsync("title", "#0000ff");
        // The Agent parses writes as #AARRGGBB: re-attaching the old alpha sent "#000000ff", blue at alpha 00.
        Assert.Equal(("BackgroundColor", "#0000ff"), Assert.Single(_writes));
        Assert.Empty(_pageErrors);
    }

    [InspectorBrowserFixtureFact]
    public async Task OpaqueColor_ShowsSwatch_AndPickIsWrittenOpaque()
    {
        var row = ColorRow("TextColor");
        var editor = row.Locator("input[type='color']");
        await Expect(editor).ToHaveValueAsync("#ffff00");
        await Expect(row.Locator(".df-color-unset")).ToHaveCountAsync(0);
        await editor.FillAsync("#0000ff");
        await Expect(row.Locator(".df-prop-source")).ToHaveAttributeAsync("data-state", "dirty");
        Assert.Equal(("TextColor", "#0000ff"), Assert.Single(_writes));
        Assert.Empty(_pageErrors);
    }

    [InspectorBrowserFixtureFact]
    public async Task SemiTransparentColor_ReadsAlphaFromTrailingByte_AndPickIsWrittenOpaque()
    {
        var row = ColorRow("BorderColor");
        var editor = row.Locator("input[type='color']");
        // #RRGGBBAA is green at alpha 0x80. Reading the first byte as alpha would make it
        // fully transparent (a placeholder) with the swatch RGB #ff0080.
        await Expect(editor).ToHaveValueAsync("#00ff00");
        await Expect(row.Locator(".df-color-unset")).ToHaveCountAsync(0);
        await editor.FillAsync("#0000ff");
        await Expect(row.Locator(".df-prop-source")).ToHaveAttributeAsync("data-state", "dirty");
        // A pick is opaque and carries no alpha byte; "#800000ff" would be blue at alpha 0x80 (#AARRGGBB).
        Assert.Equal(("BorderColor", "#0000ff"), Assert.Single(_writes));
        Assert.Empty(_pageErrors);
    }

    private object Label() => new
    {
        id = "fixture-label", type = "Label", automationId = "FixtureLabel",
        text = Volatile.Read(ref _text), isVisible = true, isEnabled = true,
        bounds = new { x = 20, y = 30, width = 250, height = 40 },
        sourceFile = "FixturePage.xaml", sourceLine = 2,
        children = new[] { Button(), FlyoutRow() },
    };

    // Nested under the Label because the Inspector scopes each frame to the window's last child.
    private static object Button() => new
    {
        id = "fixture-button", type = "Button", text = "Tap me", isVisible = true, isEnabled = true,
        bounds = new { x = 20, y = 90, width = 160, height = 44 },
        children = Array.Empty<object>(),
    };

    // A Shell flyout row: while closed it is realized left of the window yet still reported as displayed.
    private object FlyoutRow() => new
    {
        id = "fixture-flyout-row", type = "Grid", isVisible = true, isEnabled = true,
        bounds = new { x = Volatile.Read(ref _flyoutX), y = 100, width = 320, height = 50 },
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
                for (var attempt = 0; attempt < 20 && !_pendingRequests.IsEmpty; attempt++)
                    await Task.Delay(50);
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
