using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.Cli.UnitTests.Fixtures;

public sealed class MockAgentServer : IAsyncDisposable
{
    private readonly List<RecordedRequest> _recordedRequests = [];
    private readonly object _lock = new();
    private readonly bool _supportsCaptureEpoch;
    private readonly bool _failFirstHitTestCandidate;
    private readonly bool _changeHitTestCandidates;
    private readonly bool _staleFirstHitTestCandidate;
    private readonly bool _staleFirstFill;
    private readonly bool _failScreenshotsAfterFirst;
    private readonly bool _useScrollableHitTestCandidate;
    private readonly int _nativeProbeBusyHitTestCount;
    private readonly bool _failTapWithServerError;
    private readonly bool _staleFirstElementTap;
    private readonly bool _staleFirstKey;
    private readonly bool _includeDetachedNativeRoot;
    private readonly bool _returnEmptyTree;
    private readonly bool _rejectNativeProperty;
    private readonly bool _malformedPropertyResponse;
    private readonly bool _propertyFailureWithoutReason;
    private readonly bool _propertyNotFound;
    private int _hitTestCount;
    private int _tapCount;
    private int _fillCount;
    private int _screenshotCount;
    private int _keyCount;
    private WebApplication? _app;

    public MockAgentServer(
        bool supportsCaptureEpoch = true,
        bool failFirstHitTestCandidate = false,
        bool changeHitTestCandidates = false,
        bool staleFirstHitTestCandidate = false,
        bool staleFirstFill = false,
        bool failScreenshotsAfterFirst = false,
        bool useScrollableHitTestCandidate = false,
        int nativeProbeBusyHitTestCount = 0,
        bool failTapWithServerError = false,
        bool staleFirstElementTap = false,
        bool staleFirstKey = false,
        bool includeDetachedNativeRoot = false,
        bool returnEmptyTree = false,
        bool rejectNativeProperty = false,
        bool malformedPropertyResponse = false,
        bool propertyFailureWithoutReason = false,
        bool propertyNotFound = false)
    {
        _supportsCaptureEpoch = supportsCaptureEpoch;
        _failFirstHitTestCandidate = failFirstHitTestCandidate;
        _changeHitTestCandidates = changeHitTestCandidates;
        _staleFirstHitTestCandidate = staleFirstHitTestCandidate;
        _staleFirstFill = staleFirstFill;
        _failScreenshotsAfterFirst = failScreenshotsAfterFirst;
        _useScrollableHitTestCandidate = useScrollableHitTestCandidate;
        _nativeProbeBusyHitTestCount = nativeProbeBusyHitTestCount;
        _failTapWithServerError = failTapWithServerError;
        _staleFirstElementTap = staleFirstElementTap;
        _staleFirstKey = staleFirstKey;
        _includeDetachedNativeRoot = includeDetachedNativeRoot;
        _returnEmptyTree = returnEmptyTree;
        _rejectNativeProperty = rejectNativeProperty;
        _malformedPropertyResponse = malformedPropertyResponse;
        _propertyFailureWithoutReason = propertyFailureWithoutReason;
        _propertyNotFound = propertyNotFound;
    }

    public int Port { get; private set; }

    public IReadOnlyList<RecordedRequest> RecordedRequests
    {
        get
        {
            lock (_lock)
                return _recordedRequests.ToList();
        }
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();

        _app = builder.Build();

        _app.Use(async (context, next) =>
        {
            string? body = null;
            if (context.Request.ContentLength > 0 || context.Request.ContentType is not null)
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
                body = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;
            }

            lock (_lock)
            {
                _recordedRequests.Add(new RecordedRequest
                {
                    Method = context.Request.Method,
                    Path = context.Request.Path.Value ?? string.Empty,
                    QueryString = context.Request.QueryString.Value ?? string.Empty,
                    Body = body
                });
            }

            await next();
        });

        RegisterAgentEndpoints(_app);
        RegisterUiEndpoints(_app);
        RegisterDeviceEndpoints(_app);
        RegisterStorageEndpoints(_app);
        RegisterWebViewEndpoints(_app);
        RegisterNetworkEndpoints(_app);
        RegisterExtensionEndpoints(_app);

        await _app.StartAsync();
        Port = _app.Urls.Select(url => new Uri(url).Port).First();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is null)
            return;

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    public void ClearRecordedRequests()
    {
        lock (_lock)
            _recordedRequests.Clear();
    }

    private void RegisterAgentEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/agent/status", () => Results.Content(MockAgentResponses.AgentStatus, "application/json"));
        app.MapGet("/api/v1/agent/capabilities", () => Results.Content(
            _supportsCaptureEpoch
                ? MockAgentResponses.AgentCapabilities
                : MockAgentResponses.LegacyAgentCapabilities,
            "application/json"));
    }

    private static void RegisterExtensionEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/ext/com.example.diagnostics/build-info", () =>
            Results.Content("""{"app":"TestApp","version":"1.0.0","build":"42"}""", "application/json"));
        app.MapPost("/api/v1/ext/com.example.diagnostics/echo", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            return Results.Content($$"""{"body":{{body}}}""", "application/json");
        });
    }

    private void RegisterUiEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/ui/tree", () => Results.Content(
            _returnEmptyTree
                ? "[]"
                : _includeDetachedNativeRoot
                ? MockAgentResponses.VisualTreeWithDetachedNativeRoot
                : MockAgentResponses.VisualTree,
            "application/json"));
        app.MapGet("/api/v1/ui/elements", (HttpContext context) =>
        {
            var response =
                string.Equals(
                    context.Request.Query["automationId"],
                    "DuplicateActionTarget",
                    StringComparison.Ordinal)
                    ? MockAgentResponses.DuplicateActionElements
                    : string.Equals(
                        context.Request.Query["text"],
                        "Shared action",
                        StringComparison.Ordinal)
                        ? MockAgentResponses.DistinctActionElements
                        : MockAgentResponses.QueryElements;
            return Results.Content(response, "application/json");
        });
        app.MapGet("/api/v1/ui/elements/{id}", (string id) => Results.Content(MockAgentResponses.SingleElement(id), "application/json"));
        app.MapGet("/api/v1/ui/elements/{id}/properties/{name}", (string id, string name) =>
        {
            if (_malformedPropertyResponse)
            {
                // Simulates a transport failure / unparsable response deterministically:
                // an empty 200 body causes GetJsonAsync to return JsonValueKind.Undefined,
                // without relying on brittle real network-failure timing.
                return Results.Content(string.Empty, "application/json");
            }

            if (_propertyFailureWithoutReason)
            {
                // Mirrors server responses such as "Agent not bound to app" that report
                // "success": false but omit the optional "reason" field entirely.
                return Results.Json(
                    new
                    {
                        success = false,
                        error = "Agent not bound to app"
                    },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (_propertyNotFound)
            {
                // Mirrors the real agent's genuine "property not found" response: HTTP 404,
                // "success": false, no "reason" field. This must still resolve to a null
                // return (not an exception) to preserve the maui_get_property/maui_assert
                // not-found contract.
                return Results.Json(
                    new
                    {
                        success = false,
                        error = $"Property '{name}' not found on element '{id}'"
                    },
                    statusCode: StatusCodes.Status404NotFound);
            }

            if (_rejectNativeProperty)
            {
                return Results.Json(
                    new
                    {
                        success = false,
                        error = "Generic property reflection is not supported for native elements. Use the element metadata and advertised capabilities instead.",
                        reason = "native-property-not-supported"
                    },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Content($$"""{"id":"{{id}}","property":"{{name}}","value":"Hello, World!"}""", "application/json");
        });
        app.MapPut("/api/v1/ui/elements/{id}/properties/{name}", () =>
        {
            if (_rejectNativeProperty)
            {
                return Results.Json(
                    new
                    {
                        success = false,
                        error = "Generic property mutation is not supported for native elements. Use a native action advertised by the element capabilities instead.",
                        reason = "native-property-not-supported"
                    },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Content(MockAgentResponses.ActionSuccess, "application/json");
        });
        app.MapGet("/api/v1/ui/hit-test", () =>
        {
            var hitTestNumber = Interlocked.Increment(ref _hitTestCount);
            if (hitTestNumber <= _nativeProbeBusyHitTestCount)
            {
                return Results.Json(
                    new
                    {
                        success = false,
                        error = "Native hit testing is busy.",
                        reason = "native-probe-busy",
                        details = new { retryable = true }
                    },
                    statusCode: StatusCodes.Status409Conflict);
            }

            var epoch = hitTestNumber + 40;
            var parentId = _changeHitTestCandidates && epoch > 41
                ? "hit-parent-refreshed"
                : "hit-parent";
            var response = _useScrollableHitTestCandidate
                ? MockAgentResponses.ScrollableHitTestResult(epoch)
                : MockAgentResponses.HitTestResult(epoch, parentId);
            return Results.Content(response, "application/json");
        });
        app.MapGet("/api/v1/ui/screenshot", () =>
        {
            if (_failScreenshotsAfterFirst
                && Interlocked.Increment(ref _screenshotCount) > 1)
            {
                return Results.Json(
                    new
                    {
                        success = false,
                        error = "The UI snapshot is stale.",
                        reason = "stale-capture-epoch"
                    },
                    statusCode: StatusCodes.Status409Conflict);
            }

            return Results.File(MockAgentResponses.ScreenshotPng, "image/png");
        });

        app.MapPost("/api/v1/ui/actions/tap", async (HttpContext context) =>
        {
            var tapNumber = Interlocked.Increment(ref _tapCount);
            using var body = await JsonDocument.ParseAsync(context.Request.Body);
            if (_failTapWithServerError)
            {
                return Results.Json(
                    new { success = false, error = "Agent action failed." },
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            if (_staleFirstElementTap
                && tapNumber == 1
                && body.RootElement.TryGetProperty("elementId", out var directElementId)
                && directElementId.GetString() == "element-42")
            {
                return Results.Json(
                    new
                    {
                        success = false,
                        error = "The UI snapshot is stale.",
                        reason = "stale-capture-epoch"
                    },
                    statusCode: StatusCodes.Status409Conflict);
            }

            if (_staleFirstHitTestCandidate
                && tapNumber == 1
                && body.RootElement.TryGetProperty("elementId", out var staleElementId)
                && staleElementId.GetString() == "hit-child")
            {
                return Results.Json(
                    new
                    {
                        success = false,
                        error = "The UI snapshot is stale.",
                        reason = "stale-capture-epoch"
                    },
                    statusCode: StatusCodes.Status409Conflict);
            }

            if (_failFirstHitTestCandidate
                && body.RootElement.TryGetProperty("elementId", out var elementId)
                && elementId.GetString() == "hit-child")
            {
                return Results.BadRequest(new { success = false, error = "not tappable" });
            }

            return Results.Content(MockAgentResponses.ActionSuccess, "application/json");
        });

        app.MapPost("/api/v1/ui/actions/fill", () =>
        {
            if (_staleFirstFill && Interlocked.Increment(ref _fillCount) == 1)
            {
                return Results.Json(
                    new
                    {
                        success = false,
                        error = "The UI snapshot is stale.",
                        reason = "stale-capture-epoch"
                    },
                    statusCode: StatusCodes.Status409Conflict);
            }

            return Results.Content(MockAgentResponses.ActionSuccess, "application/json");
        });

        app.MapPost("/api/v1/ui/actions/key", () =>
        {
            if (_staleFirstKey && Interlocked.Increment(ref _keyCount) == 1)
            {
                return Results.Json(
                    new
                    {
                        success = false,
                        error = "The UI snapshot is stale.",
                        reason = "stale-capture-epoch"
                    },
                    statusCode: StatusCodes.Status409Conflict);
            }

            return Results.Content(MockAgentResponses.ActionSuccess, "application/json");
        });

        foreach (var action in new[] { "clear", "focus", "navigate", "scroll", "resize", "back", "gesture", "batch" })
            app.MapPost($"/api/v1/ui/actions/{action}", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));
    }

    private static void RegisterDeviceEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/device/info", () => Results.Content(MockAgentResponses.DeviceInfo, "application/json"));
        app.MapGet("/api/v1/device/app", () => Results.Content(MockAgentResponses.DeviceInfo, "application/json"));
        app.MapGet("/api/v1/device/app/theme", () => Results.Content(MockAgentResponses.ThemeInfo, "application/json"));
        app.MapPut("/api/v1/device/app/theme", async (HttpContext context) =>
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body);
            var theme = document.RootElement.GetProperty("theme").GetString() ?? "system";
            return Results.Content($$"""
                {
                  "theme": "{{theme}}",
                  "requestedTheme": "{{theme}}",
                  "userAppTheme": "{{theme}}",
                  "effectiveTheme": "{{theme}}",
                  "supportedThemes": ["light", "dark", "system"],
                  "source": "app"
                }
                """, "application/json");
        });
        app.MapGet("/api/v1/device/display", () => Results.Content(MockAgentResponses.DeviceInfo, "application/json"));
        app.MapGet("/api/v1/device/battery", () => Results.Content(MockAgentResponses.DeviceInfo, "application/json"));
        app.MapGet("/api/v1/device/connectivity", () => Results.Content(MockAgentResponses.DeviceInfo, "application/json"));
        app.MapGet("/api/v1/device/geolocation", () => Results.Content(MockAgentResponses.DeviceInfo, "application/json"));
        app.MapGet("/api/v1/device/version-tracking", () =>
            Results.Content("""{"currentVersion":"1.0.0","previousVersion":null,"firstInstalledVersion":"1.0.0"}""", "application/json"));
        app.MapGet("/api/v1/device/permissions", () =>
            Results.Content("""{"camera":"granted","location":"granted"}""", "application/json"));
        app.MapGet("/api/v1/device/permissions/{name}", (string name) =>
            Results.Content($$"""{"name":"{{name}}","status":"granted"}""", "application/json"));
        app.MapGet("/api/v1/device/sensors", () => Results.Content("""["accelerometer","gyroscope"]""", "application/json"));
        app.MapPost("/api/v1/device/sensors/{sensor}/start", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));
        app.MapPost("/api/v1/device/sensors/{sensor}/stop", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));
    }

    private static void RegisterStorageEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/storage/preferences", () => Results.Content(MockAgentResponses.PreferencesList, "application/json"));
        app.MapGet("/api/v1/storage/preferences/{key}", () => Results.Content(MockAgentResponses.PreferenceValue, "application/json"));
        app.MapPut("/api/v1/storage/preferences/{key}", () => Results.Content(MockAgentResponses.PreferenceValue, "application/json"));
        app.MapDelete("/api/v1/storage/preferences/{key}", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));
        app.MapDelete("/api/v1/storage/preferences", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));

        app.MapGet("/api/v1/storage/secure/{key}", () => Results.Content(MockAgentResponses.SecureStorageValue, "application/json"));
        app.MapPut("/api/v1/storage/secure/{key}", () => Results.Content(MockAgentResponses.SecureStorageValue, "application/json"));
        app.MapDelete("/api/v1/storage/secure/{key}", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));
        app.MapDelete("/api/v1/storage/secure", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));

        app.MapGet("/api/v1/storage/roots", () => Results.Content(MockAgentResponses.StorageRoots, "application/json"));
        app.MapGet("/api/v1/storage/files", () => Results.Content(MockAgentResponses.FilesList, "application/json"));
        app.MapGet("/api/v1/storage/files/{path}", (string path) => Results.Content(MockAgentResponses.FileDownload(path), "application/json"));
        app.MapPut("/api/v1/storage/files/{path}", (string path) => Results.Content(MockAgentResponses.FileUpload(path), "application/json"));
        app.MapDelete("/api/v1/storage/files/{path}", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));
    }

    private static void RegisterWebViewEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/webview/contexts", () => Results.Content(MockAgentResponses.WebViews, "application/json"));
        app.MapGet("/api/v1/webview/source", () => Results.Content(MockAgentResponses.WebViewSource, "text/html"));
        app.MapGet("/api/v1/webview/dom", () => Results.Content("""{"root":{"tag":"html"}}""", "application/json"));
        app.MapGet("/api/v1/webview/dom/query", () => Results.Content("""{"matches":[{"tag":"div","id":"app"}]}""", "application/json"));
        app.MapGet("/api/v1/webview/network", () => Results.Content("""{"entries":[]}""", "application/json"));
        app.MapGet("/api/v1/webview/console", () => Results.Content("""{"entries":[]}""", "application/json"));
        app.MapGet("/api/v1/webview/screenshot", () => Results.File(MockAgentResponses.ScreenshotPng, "image/png"));

        app.MapPost("/api/v1/webview/evaluate", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            var method = JsonDocument.Parse(body).RootElement.GetProperty("method").GetString() ?? string.Empty;
            return Results.Content(MockAgentResponses.WebViewEvaluate(method), "application/json");
        });
    }

    private static void RegisterNetworkEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/network/requests", () => Results.Content("""[]""", "application/json"));
        app.MapGet("/api/v1/network/requests/{id}", (string id) => Results.Content($$"""{"id":"{{id}}"}""", "application/json"));
        app.MapDelete("/api/v1/network/requests", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));
        app.MapGet("/api/v1/logs", () => Results.Content("""[{"level":"info","message":"ok"}]""", "application/json"));
    }
}

public sealed class RecordedRequest
{
    public string Method { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string QueryString { get; init; } = string.Empty;
    public string? Body { get; init; }
}
