using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Maui.DevFlow.Agent.Core.Profiling;
using Microsoft.Maui.DevFlow.Logging;
using Microsoft.Maui.DevFlow.Agent.Core.Network;

namespace Microsoft.Maui.DevFlow.Agent.Core;

public partial class DevFlowAgentService
{
    protected readonly AgentOptions _options;

    protected readonly AgentHttpServer _server;

    protected FileLogProvider? _logProvider;

    protected BrokerRegistration? _brokerRegistration;

    protected string? _sessionId;

    protected bool _disposed;

    /// <summary>
    /// The network request store for capturing HTTP traffic.
    /// </summary>
    public NetworkRequestStore NetworkStore { get; }

    protected readonly IProfilerCollector _profilerCollector;

    protected readonly ProfilerSessionStore _profilerSessions;

    protected readonly SemaphoreSlim _profilerStateGate = new(1, 1);

    protected CancellationTokenSource? _profilerLoopCts;

    protected Task? _profilerLoopTask;

    protected DateTime _lastAutoJankSpanTsUtc = DateTime.MinValue;

    protected readonly List<UiEventSubscription> _uiEventSubscriptions = new();

    protected readonly object _uiEventSubscriptionGate = new();

    protected sealed class UiEventSubscription
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Queue { get; } = new();
        public HashSet<string> Events { get; } = new(StringComparer.OrdinalIgnoreCase) { "all" };
    }

    /// <summary>
    /// Delegate for sending CDP commands to the Blazor WebView.
    /// Set by the Blazor package when both are registered.
    /// Deprecated: use RegisterCdpWebView() for multi-WebView support.
    /// Setting this property registers the handler as WebView index 0.
    /// </summary>
    public Func<string, Task<string>>? CdpCommandHandler
    {
        get => _cdpWebViews.Count > 0 ? _cdpWebViews[0].CommandHandler : null;
        set
        {
            if (value == null)
            {
                if (_cdpWebViews.Count > 0)
                    _cdpWebViews.RemoveAt(0);
                return;
            }
            if (_cdpWebViews.Count > 0)
                _cdpWebViews[0].CommandHandler = value;
            else
                _cdpWebViews.Add(new CdpWebViewInfo { Index = 0, CommandHandler = value, ReadyCheck = () => true });
        }
    }

    /// <summary>Whether the CDP handler is ready to process commands.
    /// Deprecated: use RegisterCdpWebView() for multi-WebView support.</summary>
    public Func<bool>? CdpReadyCheck
    {
        get => _cdpWebViews.Count > 0 ? _cdpWebViews[0].ReadyCheck : null;
        set
        {
            if (_cdpWebViews.Count > 0 && value != null)
                _cdpWebViews[0].ReadyCheck = value;
        }
    }

    protected readonly List<CdpWebViewInfo> _cdpWebViews = new();

    protected int _nextWebViewIndex = 0;

    /// <summary>Register a CDP-capable WebView with the agent.</summary>
    public int RegisterCdpWebView(Func<string, Task<string>> commandHandler, Func<bool> readyCheck,
        string? automationId = null, string? elementId = null, string? url = null)
    {
        // Shell route changes can recreate the same logical BlazorWebView multiple times.
        // Reuse the existing slot for the same AutomationId/ElementId so callers don't get
        // stranded on a stale index 0 bridge after navigating away and back.
        var existing = _cdpWebViews.LastOrDefault(w =>
            (!string.IsNullOrWhiteSpace(elementId) &&
             string.Equals(w.ElementId, elementId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(automationId) &&
             string.Equals(w.AutomationId, automationId, StringComparison.OrdinalIgnoreCase)));

        if (existing != null)
        {
            existing.CommandHandler = commandHandler;
            existing.ReadyCheck = readyCheck;
            existing.AutomationId = automationId ?? existing.AutomationId;
            existing.ElementId = elementId ?? existing.ElementId;
            existing.Url = url ?? existing.Url;
            return existing.Index;
        }

        var index = _nextWebViewIndex++;
        _cdpWebViews.Add(new CdpWebViewInfo
        {
            Index = index,
            AutomationId = automationId,
            ElementId = elementId,
            Url = url,
            CommandHandler = commandHandler,
            ReadyCheck = readyCheck,
        });
        return index;
    }

    /// <summary>Unregister a CDP WebView by index.</summary>
    public void UnregisterCdpWebView(int index)
    {
        _cdpWebViews.RemoveAll(w => w.Index == index);
    }

    /// <summary>Update metadata for a registered WebView.</summary>
    public void UpdateCdpWebView(int index, string? automationId = null, string? elementId = null, string? url = null)
    {
        var wv = _cdpWebViews.FirstOrDefault(w => w.Index == index);
        if (wv == null) return;
        if (automationId != null) wv.AutomationId = automationId;
        if (elementId != null) wv.ElementId = elementId;
        if (url != null) wv.Url = url;
    }

    protected CdpWebViewInfo? ResolveCdpWebView(string? webviewId)
    {
        if (_cdpWebViews.Count == 0) return null;
        if (string.IsNullOrEmpty(webviewId))
        {
            // Prefer the most recently registered ready bridge, falling back to the newest
            // bridge overall. This avoids defaulting to a stale, no-longer-visible WebView
            // after Shell recreates a page.
            return _cdpWebViews.LastOrDefault(w => w.IsReady) ?? _cdpWebViews.Last();
        }

        // Try index
        if (int.TryParse(webviewId, out var idx))
        {
            var byIndex = _cdpWebViews.FirstOrDefault(w => w.Index == idx);
            if (byIndex != null) return byIndex;
        }

        // Try AutomationId
        var byAutomationId = _cdpWebViews.LastOrDefault(w =>
            !string.IsNullOrEmpty(w.AutomationId) && w.AutomationId.Equals(webviewId, StringComparison.OrdinalIgnoreCase));
        if (byAutomationId != null) return byAutomationId;

        // Try ElementId
        var byElementId = _cdpWebViews.LastOrDefault(w =>
            !string.IsNullOrEmpty(w.ElementId) && w.ElementId.Equals(webviewId, StringComparison.OrdinalIgnoreCase));
        if (byElementId != null) return byElementId;

        return null;
    }

    public bool IsRunning => _server.IsRunning;

    public int Port => _options.Port;

    /// <summary>
    /// Parses the optional "window" query parameter as a 0-based window index.
    /// Returns null when not specified (callers should default to first window).
    /// </summary>
    protected static int? ParseWindowIndex(HttpRequest request)
    {
        if (request.QueryParams.TryGetValue("window", out var ws) && int.TryParse(ws, out var wi))
            return wi;
        return null;
    }

    /// <summary>
    /// Creates the profiler collector. Override in platform-specific subclasses
    /// to provide native frame/CPU integrations.
    /// </summary>
    protected virtual IProfilerCollector CreateProfilerCollector() => new RuntimeProfilerCollector();

    /// <summary>Whether platform background jobs can be queried on this agent.</summary>
    protected virtual bool IsJobsSupported => false;

    /// <summary>Whether platform background jobs can be triggered on this agent.</summary>
    protected virtual bool IsJobRunSupported => IsJobsSupported;

    /// <summary>
    /// Gets the list of platform background jobs (Android Workers / iOS BGTasks).
    /// Override in platform-specific subclasses to query WorkManager or BGTaskScheduler.
    /// </summary>
    protected virtual Task<object?> GetPlatformJobsAsync() => Task.FromResult<object?>(null);

    /// <summary>
    /// Triggers a platform background job by identifier.
    /// Override in platform-specific subclasses to enqueue via WorkManager or submit via BGTaskScheduler.
    /// </summary>
    protected virtual Task<object?> RunPlatformJobAsync(string identifier, string? type = null) => Task.FromResult<object?>(null);

    protected bool IsProfilerFeatureAvailable => _options.EnableProfiler;

    /// <summary>
    /// Sets the file log provider for serving logs via the API.
    /// Called by AgentServiceExtensions during registration.
    /// </summary>
    public void SetLogProvider(FileLogProvider provider)
        => _logProvider = provider;

    /// <summary>
    /// Attaches the broker registration and stamps it with this backend's framework identity so the
    /// broker and CLI can tell MAUI apps apart from plain .NET apps.
    /// </summary>
    public void SetBrokerRegistration(BrokerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Framework = FrameworkName;
        registration.UiFramework = UiFrameworkName;
        _brokerRegistration = registration;
    }

    /// <summary>
    /// Sets the DevFlow session identity for this agent, derived from the build environment.
    /// Included in status responses so clients can identify which environment built the running app.
    /// </summary>
    public void SetSessionId(string? sessionId)
        => _sessionId = sessionId;

    /// <summary>
    /// Writes a log entry originating from the WebView/Blazor console.
    /// Called by the Blazor package via reflection to route JS console output through ILogger.
    /// </summary>
    public void WriteWebViewLog(string level, string category, string message, string? exception = null)
    {
        if (_logProvider == null) return;

        var entry = new Logging.FileLogEntry(
            Timestamp: DateTime.UtcNow,
            Level: level,
            Category: category,
            Message: message,
            Exception: exception,
            Source: "webview"
        );
        _logProvider.Writer.Write(entry);
    }

    public async Task StopAsync()
    {
        await StopProfilerAsync();
        await StopBackendAsync();
        await _server.StopAsync();
    }

    protected void RegisterRoutes()
    {
        // Canonical DevFlow v1 routes aligned with the formal spec.
        _server.MapGet("/api/v1/agent/status", HandleStatus);
        _server.MapGet("/api/v1/agent/capabilities", HandleCapabilities);

        _server.MapGet("/api/v1/ui/tree", HandleTree);
        _server.MapGet("/api/v1/ui/elements", HandleQuery);
        _server.MapGet("/api/v1/ui/elements/{id}", HandleElement);
        _server.MapGet("/api/v1/ui/hit-test", HandleHitTest);
        // Accept POST as well so callers can send coordinates in a body; both verbs share a handler.
        _server.MapPost("/api/v1/ui/hit-test", HandleHitTest);
        _server.MapGet("/api/v1/ui/screenshot", HandleScreenshot);
        _server.MapGet("/api/v1/ui/elements/{id}/properties/{name}", HandleProperty);
        _server.MapPut("/api/v1/ui/elements/{id}/properties/{name}", request => ExecuteUiMutationAsync(request, HandleSetProperty));
        _server.MapPost("/api/v1/ui/actions/tap", request => ExecuteUiMutationAsync(request, HandleTap));
        _server.MapPost("/api/v1/ui/actions/fill", request => ExecuteUiMutationAsync(request, HandleFill));
        _server.MapPost("/api/v1/ui/actions/clear", request => ExecuteUiMutationAsync(request, HandleClear));
        _server.MapPost("/api/v1/ui/actions/focus", request => ExecuteUiMutationAsync(request, HandleFocus));
        _server.MapPost("/api/v1/ui/actions/navigate", request => ExecuteUiMutationAsync(request, HandleNavigate));
        _server.MapPost("/api/v1/ui/actions/resize", request => ExecuteUiMutationAsync(request, HandleResize));
        _server.MapPost("/api/v1/ui/actions/scroll", request => ExecuteUiMutationAsync(request, HandleScroll));
        _server.MapPost("/api/v1/ui/actions/back", request => ExecuteUiMutationAsync(request, HandleBack));
        _server.MapPost("/api/v1/ui/actions/key", request => ExecuteUiMutationAsync(request, HandleKey));
        _server.MapPost("/api/v1/ui/actions/gesture", request => ExecuteUiMutationAsync(request, HandleGesture));
        _server.MapPost("/api/v1/ui/actions/batch", request => ExecuteUiMutationAsync(request, HandleBatch));

        _server.MapGet("/api/v1/logs", HandleLogs);
        _server.MapWebSocket("/ws/v1/logs", HandleLogsWebSocket);

        _server.MapPost("/api/v1/webview/evaluate", HandleCdp);
        _server.MapGet("/api/v1/webview/contexts", HandleCdpWebViews);
        _server.MapGet("/api/v1/webview/source", HandleCdpSource);
        _server.MapGet("/api/v1/webview/dom", HandleWebViewDom);
        _server.MapPost("/api/v1/webview/dom/query", HandleWebViewDomQuery);
        _server.MapPost("/api/v1/webview/navigate", request => ExecuteUiMutationAsync(request, HandleWebViewNavigate));
        _server.MapPost("/api/v1/webview/input/click", request => ExecuteUiMutationAsync(request, HandleWebViewInputClick));
        _server.MapPost("/api/v1/webview/input/fill", request => ExecuteUiMutationAsync(request, HandleWebViewInputFill));
        _server.MapPost("/api/v1/webview/input/text", request => ExecuteUiMutationAsync(request, HandleWebViewInputText));
        _server.MapGet("/api/v1/webview/network", HandleWebViewNetwork);
        _server.MapGet("/api/v1/webview/console", HandleWebViewConsole);
        _server.MapGet("/api/v1/webview/screenshot", HandleWebViewScreenshot);

        _server.MapGet("/api/v1/profiler/capabilities", HandleProfilerCapabilities);
        _server.MapPost("/api/v1/profiler/sessions", HandleProfilerStart);
        _server.MapDelete("/api/v1/profiler/sessions/{id}", HandleProfilerStop);
        _server.MapGet("/api/v1/profiler/sessions/{id}/samples", HandleProfilerSamples);
        _server.MapPost("/api/v1/profiler/markers", HandleProfilerMarker);
        _server.MapPost("/api/v1/profiler/spans", HandleProfilerSpan);
        _server.MapGet("/api/v1/profiler/hotspots", HandleProfilerHotspots);
        _server.MapWebSocket("/ws/v1/profiler", HandleProfilerWebSocket);

        _server.MapGet("/api/v1/network/requests", HandleNetworkList);
        _server.MapGet("/api/v1/network/requests/{id}", HandleNetworkDetail);
        _server.MapDelete("/api/v1/network/requests", HandleNetworkClear);
        _server.MapWebSocket("/ws/v1/network", HandleNetworkWebSocket);

        _server.MapWebSocket("/ws/v1/ui/events", HandleUiEventsWebSocket);

        _server.MapGet("/api/v1/device/app", HandlePlatformAppInfo);
        _server.MapGet("/api/v1/device/info", HandlePlatformDeviceInfo);
        _server.MapGet("/api/v1/device/display", HandlePlatformDeviceDisplay);
        _server.MapGet("/api/v1/device/battery", HandlePlatformBattery);
        _server.MapGet("/api/v1/device/connectivity", HandlePlatformConnectivity);
        _server.MapGet("/api/v1/device/version-tracking", HandlePlatformVersionTracking);
        _server.MapGet("/api/v1/device/permissions", HandlePlatformPermissions);
        _server.MapGet("/api/v1/device/permissions/{permission}", HandlePlatformPermissionCheck);
        _server.MapGet("/api/v1/device/geolocation", HandlePlatformGeolocation);
        _server.MapGet("/api/v1/device/sensors", HandleSensorsList);
        _server.MapPost("/api/v1/device/sensors/{sensor}/start", HandleSensorStart);
        _server.MapPost("/api/v1/device/sensors/{sensor}/stop", HandleSensorStop);
        _server.MapWebSocket("/ws/v1/sensors", HandleSensorWebSocket);

        _server.MapGet("/api/v1/device/jobs", HandleJobsList);
        _server.MapPost("/api/v1/device/jobs/{identifier}/run", HandleJobRun);

        _server.MapGet("/api/v1/device/app/theme", HandleThemeGet);
        _server.MapPut("/api/v1/device/app/theme", HandleThemeSet);
        _server.MapGet("/api/v1/storage/preferences", HandlePreferencesList);
        _server.MapGet("/api/v1/storage/preferences/{key}", HandlePreferencesGet);
        _server.MapPut("/api/v1/storage/preferences/{key}", HandlePreferencesSet);
        _server.MapDelete("/api/v1/storage/preferences/{key}", HandlePreferencesDelete);
        _server.MapDelete("/api/v1/storage/preferences", HandlePreferencesClear);
        _server.MapGet("/api/v1/storage/secure/{key}", HandleSecureStorageGet);
        _server.MapPut("/api/v1/storage/secure/{key}", HandleSecureStorageSet);
        _server.MapDelete("/api/v1/storage/secure/{key}", HandleSecureStorageDelete);
        _server.MapDelete("/api/v1/storage/secure", HandleSecureStorageClear);

        _server.MapGet("/api/v1/storage/roots", HandleStorageRoots);
        _server.MapGet("/api/v1/storage/files", HandleFilesList);
        _server.MapGet("/api/v1/storage/files/{path}", HandleFileDownload);
        _server.MapPut("/api/v1/storage/files/{path}", HandleFileUpload);
        _server.MapDelete("/api/v1/storage/files/{path}", HandleFileDelete);

        // Invoke / reflection
        _server.MapGet("/api/v1/invoke/actions", HandleListActions);
        _server.MapPost("/api/v1/invoke/actions/{name}", HandleInvokeAction);

        RegisterExtensionRoutes();
    }

    protected void RegisterExtensionRoutes()
    {
        var namespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in _options.Extensions)
        {
            if (!namespaces.Add(extension.Namespace))
                throw new InvalidOperationException($"Duplicate extension namespace registration: {extension.Namespace}");

            foreach (var route in extension.Routes)
            {
                var key = $"{route.Method} {route.Path}";
                if (!seen.Add(key))
                    throw new InvalidOperationException($"Duplicate extension route registration: {key}");

                switch (route.Method)
                {
                    case "GET":
                        _server.MapGet(route.Path, route.Handler);
                        break;
                    case "POST":
                        _server.MapPost(route.Path, route.Handler);
                        break;
                    case "PUT":
                        _server.MapPut(route.Path, route.Handler);
                        break;
                    case "DELETE":
                        _server.MapDelete(route.Path, route.Handler);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported extension route method: {route.Method}");
                }
            }
        }
    }

    protected static string? TryGetAppInfoString(Func<string?> getter)
    {
        try
        {
            return getter();
        }
        catch (Exception ex) when (ex.GetType().Name == "NotImplementedInReferenceAssemblyException")
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    protected object BuildExtensionsMarker()
    {
        var metadata = BuildExtensionMetadata();
        return new
        {
            count = metadata.Count,
            hash = ComputeExtensionHash(metadata)
        };
    }

    protected Dictionary<string, object> BuildExtensionMetadata()
    {
        var extensions = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var extension in _options.Extensions.OrderBy(e => e.Namespace, StringComparer.Ordinal))
        {
            extensions[extension.Namespace] = new
            {
                version = extension.Version,
                description = extension.Description,
                tools = extension.Tools.Select(tool => new
                {
                    name = tool.Name,
                    description = tool.Description,
                    method = tool.Method,
                    path = tool.Path,
                    parameters = tool.Parameters,
                    returns = tool.Returns,
                    annotations = tool.Annotations is null ? null : new
                    {
                        readOnly = tool.Annotations.ReadOnly,
                        idempotent = tool.Annotations.Idempotent,
                        destructive = tool.Annotations.Destructive,
                        category = tool.Annotations.Category
                    }
                }).ToArray()
            };
        }

        return extensions;
    }

    protected static string ComputeExtensionHash(Dictionary<string, object> metadata)
    {
        var json = JsonSerializer.Serialize(metadata);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    protected static int GetCapabilityVersion(string version)
    {
        var dot = version.IndexOf('.');
        var major = dot >= 0 ? version[..dot] : version;
        return int.TryParse(major, out var parsed) && parsed > 0 ? parsed : 1;
    }

    protected string[] BuildProfilerFeatureList()
    {
        if (!IsProfilerFeatureAvailable)
            return Array.Empty<string>();

        var features = new List<string> { "capabilities", "sessions", "samples", "markers", "spans", "hotspots" };
        var capabilities = _profilerCollector.GetCapabilities();
        if (capabilities.ManagedMemorySupported)
            features.Add("managed-memory");
        if (capabilities.NativeMemorySupported)
            features.Add("native-memory");
        if (capabilities.CpuPercentSupported)
            features.Add("cpu");
        if (capabilities.FpsSupported)
            features.Add("fps");

        return features.ToArray();
    }

    /// <summary>
    /// Builds an HTTP error response for a failed screenshot capture, enriching it with an
    /// actionable, retryable cause when the platform described one (see
    /// <see cref="DescribeScreenshotFailure"/>). Falls back to <paramref name="defaultMessage"/>.
    /// </summary>
    protected static HttpResponse BuildScreenshotFailureResponse(ScreenshotCaptureFailure? failure, string defaultMessage)
    {
        if (failure == null)
            return HttpResponse.Error(defaultMessage);

        var details = new Dictionary<string, object?>
        {
            ["retryable"] = failure.Retryable
        };
        if (failure.Suggestions is { Length: > 0 })
            details["suggestions"] = failure.Suggestions;

        var message = string.IsNullOrWhiteSpace(failure.Message) ? defaultMessage : failure.Message;

        // 409 Conflict signals a transient, retryable precondition (window focus/visibility);
        // the structured body carries the authoritative retryable flag for clients.
        return HttpResponse.Error(
            message,
            statusCode: failure.Retryable ? 409 : 400,
            reason: failure.Reason,
            details: details);
    }

    /// <summary>
    /// Resizes a PNG image based on display density and/or max width constraint.
    /// By default, HiDPI screenshots are scaled to 1x logical resolution (e.g., a 3x iPhone
    /// screenshot of 1290px becomes 430px). An explicit maxWidth overrides density scaling.
    /// </summary>
    protected static byte[] ResizePngIfNeeded(byte[] pngData, int? maxWidth, double density = 1.0, bool autoScale = true)
    {
        // Determine target width: explicit maxWidth takes priority, then auto-scale by density
        int? targetWidth = maxWidth;
        if (targetWidth == null && autoScale && density > 1.0)
        {
            try
            {
                using var probe = SkiaSharp.SKBitmap.Decode(pngData);
                if (probe != null)
                    targetWidth = (int)(probe.Width / density);
            }
            catch { return pngData; }
        }

        if (targetWidth == null || targetWidth <= 0) return pngData;

        try
        {
            using var original = SkiaSharp.SKBitmap.Decode(pngData);
            if (original == null || original.Width <= targetWidth.Value) return pngData;

            var scale = (float)targetWidth.Value / original.Width;
            var newHeight = (int)(original.Height * scale);

            using var resized = original.Resize(new SkiaSharp.SKImageInfo(targetWidth.Value, newHeight), SkiaSharp.SKSamplingOptions.Default);
            if (resized == null) return pngData;

            using var image = SkiaSharp.SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }
        catch
        {
            return pngData;
        }
    }

    protected record ResizeRequest(int Width, int Height);

    protected sealed class KeyActionRequest : CaptureBoundRequest
    {
        public string? ElementId { get; set; }
        public string? Key { get; set; }
        public string? Text { get; set; }
    }

    protected sealed class GestureActionRequest : CaptureBoundRequest
    {
        public string? ElementId { get; set; }
        public string? Type { get; set; }
        public string? Direction { get; set; }
        public double Distance { get; set; } = 120;
        public int DurationMs { get; set; } = 200;
    }

    protected sealed class BatchRequest : CaptureBoundRequest
    {
        public List<BatchActionRequest> Actions { get; set; } = [];
        public bool ContinueOnError { get; set; }
    }

    protected sealed class BatchActionRequest
    {
        public string? Action { get; set; }
        public string? Type { get; set; }
        public string? ElementId { get; set; }
        public string? Text { get; set; }
        public string? Route { get; set; }
        public string? Property { get; set; }
        public string? Value { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double DeltaX { get; set; }
        public double DeltaY { get; set; }
        public int? ItemIndex { get; set; }
        public int? GroupIndex { get; set; }
        public string? ScrollToPosition { get; set; }
        public bool Animated { get; set; } = true;
        public string? Key { get; set; }
        public string? Direction { get; set; }
        public double Distance { get; set; } = 120;
        public int DurationMs { get; set; } = 200;
        public JsonElement[]? Args { get; set; }
        public string? Name { get; set; }
    }

    protected object BuildProfilerCapabilitiesPayload()
    {
        var capabilities = _profilerCollector.GetCapabilities();
        return new
        {
            available = IsProfilerFeatureAvailable,
            supportedInBuild = true,
            featureEnabled = _options.EnableProfiler,
            platform = capabilities.Platform,
            managedMemorySupported = capabilities.ManagedMemorySupported,
            nativeMemorySupported = capabilities.NativeMemorySupported,
            gcSupported = capabilities.GcSupported,
            cpuPercentSupported = capabilities.CpuPercentSupported,
            fpsSupported = capabilities.FpsSupported,
            frameTimingsEstimated = capabilities.FrameTimingsEstimated,
            nativeFrameTimingsSupported = capabilities.NativeFrameTimingsSupported,
            jankEventsSupported = capabilities.JankEventsSupported,
            uiThreadStallSupported = capabilities.UiThreadStallSupported,
            threadCountSupported = capabilities.ThreadCountSupported
        };
    }

    protected string GetRequestedProfilerSessionId(HttpRequest request)
    {
        if (request.RouteParams.TryGetValue("id", out var routeId) && !string.IsNullOrWhiteSpace(routeId))
            return routeId;
        if (request.QueryParams.TryGetValue("sessionId", out var sessionId) && !string.IsNullOrWhiteSpace(sessionId))
            return sessionId;
        return "current";
    }

    protected HttpResponse? ValidateProfilerSessionRequest(HttpRequest request, out string requestedSessionId)
    {
        requestedSessionId = GetRequestedProfilerSessionId(request);
        if (requestedSessionId.Equals("current", StringComparison.OrdinalIgnoreCase))
            return null;

        var currentSession = _profilerSessions.CurrentSession;
        return currentSession != null && currentSession.SessionId.Equals(requestedSessionId, StringComparison.Ordinal)
            ? null
            : HttpResponse.NotFound($"Profiler session '{requestedSessionId}' not found");
    }

    protected Task<HttpResponse> HandleProfilerCapabilities(HttpRequest request)
        => Task.FromResult(HttpResponse.Json(BuildProfilerCapabilitiesPayload()));

    protected async Task<HttpResponse> HandleProfilerStart(HttpRequest request)
    {
        if (!_options.EnableProfiler)
            return HttpResponse.Error("Profiler is disabled. Set AgentOptions.EnableProfiler=true");

        var body = request.BodyAs<StartProfilerRequest>();
        var intervalMs = body?.SampleIntervalMs ?? _options.ProfilerSampleIntervalMs;
        if (intervalMs < 50 || intervalMs > 60_000)
            return HttpResponse.Error("sampleIntervalMs must be between 50 and 60000");

        var session = await StartProfilerAsync(intervalMs);
        return HttpResponse.Json(new { session, capabilities = BuildProfilerCapabilitiesPayload() });
    }

    protected async Task<HttpResponse> HandleProfilerStop(HttpRequest request)
    {
        var validationError = ValidateProfilerSessionRequest(request, out _);
        if (validationError != null)
            return validationError;

        var session = await StopProfilerAsync();
        return HttpResponse.Json(new { session, stoppedAtUtc = DateTime.UtcNow });
    }

    protected Task<HttpResponse> HandleProfilerSamples(HttpRequest request)
    {
        var validationError = ValidateProfilerSessionRequest(request, out _);
        if (validationError != null)
            return Task.FromResult(validationError);

        if (!long.TryParse(request.QueryParams.GetValueOrDefault("sampleCursor", "0"), out var sampleCursor))
            sampleCursor = 0;
        if (!long.TryParse(request.QueryParams.GetValueOrDefault("markerCursor", "0"), out var markerCursor))
            markerCursor = 0;
        if (!long.TryParse(request.QueryParams.GetValueOrDefault("spanCursor", "0"), out var spanCursor))
            spanCursor = 0;
        if (!int.TryParse(request.QueryParams.GetValueOrDefault("limit", "500"), out var limit))
            limit = 500;

        limit = Math.Clamp(limit, 1, 5000);
        var batch = _profilerSessions.GetBatch(sampleCursor, markerCursor, limit, spanCursor);
        return Task.FromResult(HttpResponse.Json(batch));
    }

    protected Task<HttpResponse> HandleProfilerMarker(HttpRequest request)
    {
        if (!IsProfilerFeatureAvailable)
            return Task.FromResult<HttpResponse>(HttpResponse.Error("Profiler is not available"));
        if (!_profilerSessions.IsActive)
            return Task.FromResult<HttpResponse>(HttpResponse.Error("No active profiler session"));

        var body = request.BodyAs<PublishProfilerMarkerRequest>();
        if (string.IsNullOrWhiteSpace(body?.Name))
            return Task.FromResult(HttpResponse.Error("name is required"));

        var marker = new ProfilerMarker
        {
            TsUtc = DateTime.UtcNow,
            Type = string.IsNullOrWhiteSpace(body.Type) ? "user.action" : body.Type!,
            Name = body.Name!,
            PayloadJson = body.PayloadJson
        };

        Publish(marker);
        return Task.FromResult(HttpResponse.Ok("Marker published"));
    }

    protected Task<HttpResponse> HandleProfilerSpan(HttpRequest request)
    {
        if (!IsProfilerFeatureAvailable)
            return Task.FromResult<HttpResponse>(HttpResponse.Error("Profiler is not available"));
        if (!_profilerSessions.IsActive)
            return Task.FromResult<HttpResponse>(HttpResponse.Error("No active profiler session"));

        var body = request.BodyAs<PublishProfilerSpanRequest>();
        if (string.IsNullOrWhiteSpace(body?.Name))
            return Task.FromResult(HttpResponse.Error("name is required"));

        var startTsUtc = body.StartTsUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        var endTsUtc = body.EndTsUtc?.ToUniversalTime() ?? startTsUtc;

        var span = new ProfilerSpan
        {
            SpanId = Guid.NewGuid().ToString("N"),
            ParentSpanId = body.ParentSpanId,
            TraceId = body.TraceId,
            StartTsUtc = startTsUtc,
            EndTsUtc = endTsUtc,
            Kind = string.IsNullOrWhiteSpace(body.Kind) ? "ui.operation" : body.Kind!,
            Name = body.Name!,
            Status = string.IsNullOrWhiteSpace(body.Status) ? "ok" : body.Status!,
            ThreadId = body.ThreadId,
            Screen = body.Screen,
            ElementPath = body.ElementPath,
            TagsJson = body.TagsJson,
            Error = body.Error
        };

        Publish(span);
        return Task.FromResult(HttpResponse.Ok("Span published"));
    }

    protected Task<HttpResponse> HandleProfilerHotspots(HttpRequest request)
    {
        if (!int.TryParse(request.QueryParams.GetValueOrDefault("limit", "20"), out var limit))
            limit = 20;
        if (!int.TryParse(request.QueryParams.GetValueOrDefault("minDurationMs", "16"), out var minDurationMs))
            minDurationMs = 16;

        limit = Math.Clamp(limit, 1, 200);
        minDurationMs = Math.Clamp(minDurationMs, 0, 60_000);
        var kind = request.QueryParams.GetValueOrDefault("kind");
        var hotspots = _profilerSessions.GetHotspots(limit, minDurationMs, kind);
        return Task.FromResult(HttpResponse.Json(hotspots));
    }

    protected async Task<ProfilerSessionInfo> StartProfilerAsync(int intervalMs)
    {
        await _profilerStateGate.WaitAsync();
        try
        {
            var current = _profilerSessions.CurrentSession;
            if (current?.IsActive == true)
                return current;

            _profilerCollector.Start(intervalMs);
            var session = _profilerSessions.Start(intervalMs);
            _lastAutoJankSpanTsUtc = DateTime.MinValue;
            EnsureAutoUiHooks();
            _profilerLoopCts = new CancellationTokenSource();
            _profilerLoopTask = Task.Run(() => RunProfilerLoopAsync(intervalMs, _profilerLoopCts.Token));
            return session;
        }
        finally
        {
            _profilerStateGate.Release();
        }
    }

    protected async Task<ProfilerSessionInfo?> StopProfilerAsync()
    {
        await _profilerStateGate.WaitAsync();
        try
        {
            var cts = _profilerLoopCts;
            var loopTask = _profilerLoopTask;
            _profilerLoopCts = null;
            _profilerLoopTask = null;

            cts?.Cancel();

            if (loopTask != null)
            {
                try
                {
                    await loopTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            cts?.Dispose();
            _profilerCollector.Stop();
            StopAutoUiHooks();
            return _profilerSessions.Stop();
        }
        finally
        {
            _profilerStateGate.Release();
        }
    }

    protected async Task RunProfilerLoopAsync(int intervalMs, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            EnsureAutoUiHooks();
            if (_profilerCollector.TryCollect(out var sample))
            {
                _profilerSessions.AddSample(sample);
                PublishNativeFrameSignals(sample);
                TryPublishAutoJankSpan(sample);
            }

            await Task.Delay(intervalMs, ct);
        }
    }

    protected void PublishNativeFrameSignals(ProfilerSample sample)
    {
        if (sample.JankFrameCount <= 0 && sample.UiThreadStallCount <= 0)
            return;

        if (sample.JankFrameCount > 0)
        {
            Publish(new ProfilerMarker
            {
                TsUtc = sample.TsUtc,
                Type = "ui.frame.jank.native",
                Name = sample.FrameSource,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    jankFrames = sample.JankFrameCount,
                    frameTimeMsP95 = sample.FrameTimeMsP95,
                    worstFrameTimeMs = sample.WorstFrameTimeMs,
                    frameSource = sample.FrameSource,
                    frameQuality = sample.FrameQuality
                })
            });
        }

        if (sample.UiThreadStallCount > 0)
        {
            Publish(new ProfilerMarker
            {
                TsUtc = sample.TsUtc,
                Type = "ui.thread.stall.native",
                Name = sample.FrameSource,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    stallCount = sample.UiThreadStallCount,
                    worstFrameTimeMs = sample.WorstFrameTimeMs,
                    frameSource = sample.FrameSource,
                    frameQuality = sample.FrameQuality
                })
            });
        }
    }

    protected void TryPublishAutoJankSpan(ProfilerSample sample)
    {
        var frameMs = sample.FrameTimeMsP95;
        var hasNativeJankSignal = sample.JankFrameCount > 0 || sample.UiThreadStallCount > 0;
        if (!frameMs.HasValue || (frameMs.Value < 20d && !hasNativeJankSignal))
            return;

        var throttleMs = sample.FrameSource.StartsWith("native.", StringComparison.OrdinalIgnoreCase) ? 100d : 250d;
        if (_lastAutoJankSpanTsUtc != DateTime.MinValue
            && (sample.TsUtc - _lastAutoJankSpanTsUtc).TotalMilliseconds < throttleMs)
            return;

        _lastAutoJankSpanTsUtc = sample.TsUtc;
        var (actionName, actionElementPath, actionLagMs) = GetRecentUserAction(sample.TsUtc, TimeSpan.FromSeconds(3));
        var isStall = sample.UiThreadStallCount > 0 || (sample.WorstFrameTimeMs ?? 0d) >= 150d;
        Publish(new ProfilerSpan
        {
            SpanId = Guid.NewGuid().ToString("N"),
            TraceId = _profilerSessions.CurrentSession?.SessionId,
            StartTsUtc = sample.TsUtc.AddMilliseconds(-frameMs.Value),
            EndTsUtc = sample.TsUtc,
            Kind = "ui.operation",
            Name = isStall
                ? (string.IsNullOrWhiteSpace(actionName) ? "ui.thread.stall" : "ui.action.stall")
                : (string.IsNullOrWhiteSpace(actionName) ? "ui.frame.jank" : "ui.action.jank"),
            Status = isStall ? "error" : "ok",
            ThreadId = Environment.CurrentManagedThreadId,
            Screen = GetCurrentRouteLocation(),
            ElementPath = actionElementPath,
            TagsJson = JsonSerializer.Serialize(new
            {
                frameTimeMsP95 = frameMs.Value,
                fps = sample.Fps,
                frameSource = sample.FrameSource,
                frameQuality = sample.FrameQuality,
                jankFrameCount = sample.JankFrameCount,
                uiThreadStallCount = sample.UiThreadStallCount,
                worstFrameTimeMs = sample.WorstFrameTimeMs,
                actionName,
                actionLagMs
            })
        });
    }

    protected void PublishUiEvent(string type, object data)
    {
        var payload = JsonSerializer.Serialize(new
        {
            type,
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            data
        });

        lock (_uiEventSubscriptionGate)
        {
            foreach (var subscription in _uiEventSubscriptions)
            {
                if (subscription.Events.Contains("all") || subscription.Events.Contains(type))
                    subscription.Queue.Enqueue(payload);
            }
        }
    }

    protected static void ApplyUiEventSubscriptionMessage(UiEventSubscription subscription, JsonElement message)
    {
        if (!message.TryGetProperty("type", out var typeElement))
            return;

        var messageType = typeElement.GetString();
        if (!message.TryGetProperty("data", out var dataElement) ||
            !dataElement.TryGetProperty("events", out var eventsElement) ||
            eventsElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var events = eventsElement.EnumerateArray()
            .Select(static e => e.GetString())
            .Where(static e => !string.IsNullOrWhiteSpace(e))
            .Select(static e => e!)
            .ToArray();

        if (events.Length == 0)
            return;

        if (string.Equals(messageType, "subscribe", StringComparison.OrdinalIgnoreCase))
        {
            if (events.Contains("all", StringComparer.OrdinalIgnoreCase))
            {
                subscription.Events.Clear();
                subscription.Events.Add("all");
                return;
            }

            if (subscription.Events.Contains("all"))
                subscription.Events.Clear();

            foreach (var eventName in events)
                subscription.Events.Add(eventName);
        }
        else if (string.Equals(messageType, "unsubscribe", StringComparison.OrdinalIgnoreCase))
        {
            if (events.Contains("all", StringComparer.OrdinalIgnoreCase))
            {
                subscription.Events.Clear();
                return;
            }

            foreach (var eventName in events)
                subscription.Events.Remove(eventName);
        }
    }

    /// <summary>Guards the UI hook bookkeeping state shared with UI backends.</summary>
    protected readonly object _uiHookGate = new();

    /// <summary>Timestamp of the most recent user action, used for profiler correlation.</summary>
    protected DateTime _lastUserActionTsUtc = DateTime.MinValue;

    /// <summary>Name of the most recent user action.</summary>
    protected string? _lastUserActionName;

    /// <summary>Element path of the most recent user action.</summary>
    protected string? _lastUserActionElementPath;

    protected void RememberUserAction(string name, string? elementPath, DateTime timestampUtc)
    {
        lock (_uiHookGate)
        {
            _lastUserActionTsUtc = timestampUtc;
            _lastUserActionName = name;
            _lastUserActionElementPath = elementPath;
        }
    }

    protected (string? ActionName, string? ElementPath, double? LagMs) GetRecentUserAction(DateTime sampleTsUtc, TimeSpan maxAge)
    {
        lock (_uiHookGate)
        {
            if (_lastUserActionTsUtc == DateTime.MinValue || string.IsNullOrWhiteSpace(_lastUserActionName))
                return (null, null, null);

            var lag = sampleTsUtc - _lastUserActionTsUtc;
            if (lag < TimeSpan.Zero || lag > maxAge)
                return (null, null, null);

            return (_lastUserActionName, _lastUserActionElementPath, lag.TotalMilliseconds);
        }
    }

    protected static double TryReadDoubleProperty(object instance, string propertyName)
    {
        var value = instance.GetType().GetProperty(propertyName)?.GetValue(instance);
        return value switch
        {
            double asDouble => asDouble,
            float asFloat => asFloat,
            int asInt => asInt,
            long asLong => asLong,
            _ => 0d
        };
    }

    protected static int? TryReadIntProperty(object instance, string propertyName)
    {
        var value = instance.GetType().GetProperty(propertyName)?.GetValue(instance);
        return value switch
        {
            int asInt => asInt,
            long asLong => (int)asLong,
            short asShort => asShort,
            _ => null
        };
    }

    protected static string? TryReadNavigationRoute(object eventArgs, string statePropertyName)
    {
        var state = eventArgs.GetType().GetProperty(statePropertyName)?.GetValue(eventArgs);
        if (state == null)
            return null;

        var location = state.GetType().GetProperty("Location")?.GetValue(state);
        return location?.ToString() ?? state.ToString();
    }

    protected static string? TryReadNavigationSource(object eventArgs)
        => eventArgs.GetType().GetProperty("Source")?.GetValue(eventArgs)?.ToString();

    public void Publish(ProfilerMarker marker)
    {
        if (!IsProfilerFeatureAvailable || !_profilerSessions.IsActive)
            return;

        if (marker.TsUtc == default)
            marker.TsUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(marker.Type))
            marker.Type = "user.action";
        if (string.IsNullOrWhiteSpace(marker.Name))
            marker.Name = marker.Type;

        _profilerSessions.AddMarker(marker);
    }

    public void Publish(ProfilerSpan span)
    {
        if (!IsProfilerFeatureAvailable || !_profilerSessions.IsActive)
            return;

        if (string.IsNullOrWhiteSpace(span.Kind))
            span.Kind = "ui.operation";
        if (string.IsNullOrWhiteSpace(span.Name))
            span.Name = span.Kind;
        if (string.IsNullOrWhiteSpace(span.Status))
            span.Status = "ok";
        if (span.StartTsUtc == default)
            span.StartTsUtc = DateTime.UtcNow;
        if (span.EndTsUtc == default || span.EndTsUtc < span.StartTsUtc)
            span.EndTsUtc = span.StartTsUtc;
        if (span.ThreadId == null)
            span.ThreadId = Environment.CurrentManagedThreadId;

        _profilerSessions.AddSpan(span);
    }

    protected void PublishUiOperationSpan(
        string name,
        DateTime startedAtUtc,
        bool success,
        string? error = null,
        string? elementPath = null,
        object? tags = null)
    {
        if (success)
            OnUiOperationSucceeded();

        var endTsUtc = DateTime.UtcNow;
        var route = GetCurrentRouteLocation();
        var span = new ProfilerSpan
        {
            SpanId = Guid.NewGuid().ToString("N"),
            TraceId = _profilerSessions.CurrentSession?.SessionId,
            StartTsUtc = startedAtUtc,
            EndTsUtc = endTsUtc,
            Kind = "ui.operation",
            Name = name,
            Status = success ? "ok" : "error",
            ThreadId = Environment.CurrentManagedThreadId,
            Screen = route,
            ElementPath = elementPath,
            TagsJson = tags == null ? null : JsonSerializer.Serialize(tags),
            Error = error
        };

        Publish(span);
    }

    protected void HandleCapturedNetworkRequest(NetworkRequestEntry entry)
    {
        if (!IsProfilerFeatureAvailable || !_profilerSessions.IsActive)
            return;

        var endTimestampUtc = entry.Timestamp.UtcDateTime;
        var startTimestampUtc = endTimestampUtc - TimeSpan.FromMilliseconds(Math.Max(0, entry.DurationMs));
        var markerName = $"{entry.Method} {entry.Path ?? entry.Url}";

        Publish(new ProfilerMarker
        {
            TsUtc = startTimestampUtc,
            Type = "network.request.start",
            Name = markerName,
            PayloadJson = JsonSerializer.Serialize(new
            {
                id = entry.Id,
                method = entry.Method,
                url = entry.Url,
                host = entry.Host
            })
        });

        Publish(new ProfilerMarker
        {
            TsUtc = endTimestampUtc,
            Type = "network.request.end",
            Name = markerName,
            PayloadJson = JsonSerializer.Serialize(new
            {
                id = entry.Id,
                method = entry.Method,
                url = entry.Url,
                statusCode = entry.StatusCode,
                durationMs = entry.DurationMs,
                error = entry.Error
            })
        });

        if (entry.DurationMs >= 50 || !string.IsNullOrWhiteSpace(entry.Error))
        {
            Publish(new ProfilerSpan
            {
                SpanId = Guid.NewGuid().ToString("N"),
                TraceId = _profilerSessions.CurrentSession?.SessionId,
                StartTsUtc = startTimestampUtc,
                EndTsUtc = endTimestampUtc,
                Kind = "network.request",
                Name = markerName,
                Status = string.IsNullOrWhiteSpace(entry.Error) ? "ok" : "error",
                ThreadId = Environment.CurrentManagedThreadId,
                Screen = GetCurrentRouteLocation(),
                TagsJson = JsonSerializer.Serialize(new
                {
                    id = entry.Id,
                    method = entry.Method,
                    host = entry.Host,
                    statusCode = entry.StatusCode
                }),
                Error = entry.Error
            });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NetworkStore.OnRequestCaptured -= HandleCapturedNetworkRequest;
        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoaded;
        StopAutoUiHooks();
        DisposeBackendResources();

        var cts = _profilerLoopCts;
        var loopTask = _profilerLoopTask;
        _profilerLoopCts = null;
        _profilerLoopTask = null;

        cts?.Cancel();
        if (loopTask != null)
        {
            try { loopTask.Wait(TimeSpan.FromSeconds(3)); }
            catch (AggregateException) { }
        }
        cts?.Dispose();

        _profilerCollector.Stop();
        (_profilerCollector as IDisposable)?.Dispose();
        _profilerStateGate.Dispose();
        _brokerRegistration?.Dispose();
        _server.Dispose();
        _logProvider?.Dispose();
    }

    // ── Network monitoring endpoints ──

    protected Task<HttpResponse> HandleNetworkList(HttpRequest request)
    {
        var limit = int.TryParse(request.QueryParams.GetValueOrDefault("limit", "100"), out var l) ? l : 100;
        var host = request.QueryParams.GetValueOrDefault("host");
        var method = request.QueryParams.GetValueOrDefault("method");
        int? status = request.QueryParams.TryGetValue("status", out var s) && int.TryParse(s, out var si) ? si : null;

        var entries = NetworkStore.GetRecent(limit, host, method, status);
        // Return summary-only (no headers/body) for the list
        var summaries = entries.Select(e => e.ToSummary()).ToList();
        return Task.FromResult(HttpResponse.Json(summaries));
    }

    protected Task<HttpResponse> HandleNetworkDetail(HttpRequest request)
    {
        var id = request.RouteParams.GetValueOrDefault("id");
        if (string.IsNullOrEmpty(id))
            return Task.FromResult(HttpResponse.Error("Missing request ID"));

        var entry = NetworkStore.GetById(id);
        if (entry == null)
            return Task.FromResult(HttpResponse.NotFound($"Network request '{id}' not found"));

        return Task.FromResult(HttpResponse.Json(entry));
    }

    protected Task<HttpResponse> HandleNetworkClear(HttpRequest request)
    {
        NetworkStore.Clear();
        return Task.FromResult(HttpResponse.Ok("Network request buffer cleared"));
    }

    protected async Task HandleNetworkWebSocket(
        System.Net.Sockets.TcpClient client,
        System.Net.Sockets.NetworkStream stream,
        HttpRequest request,
        CancellationToken ct)
    {
        // Send replay of recent entries
        var recent = NetworkStore.GetRecent(100);
        var replayMsg = JsonSerializer.Serialize(new
        {
            type = "replay",
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            entries = recent.Select(e => e.ToSummary())
        });
        await AgentHttpServer.WebSocketSendTextAsync(stream, replayMsg, ct);

        // Subscribe to live entries
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sendQueue = new System.Collections.Concurrent.ConcurrentQueue<Network.NetworkRequestEntry>();

        void OnRequest(Network.NetworkRequestEntry entry) => sendQueue.Enqueue(entry);
        NetworkStore.OnRequestCaptured += OnRequest;

        try
        {
            // Read loop (handles client messages + detects disconnection)
            var readTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var msg = await AgentHttpServer.WebSocketReadTextAsync(stream, cts.Token);
                    if (msg == null) { await cts.CancelAsync(); break; }

                    try
                    {
                        using var doc = JsonDocument.Parse(msg);
                        var msgType = doc.RootElement.GetProperty("type").GetString();

                        if (msgType == "get_details" && doc.RootElement.TryGetProperty("id", out var idEl))
                        {
                            var id = idEl.GetString();
                            var entry = id != null ? NetworkStore.GetById(id) : null;
                            var resp = JsonSerializer.Serialize(new
                            {
                                type = "details",
                                timestamp = DateTimeOffset.UtcNow.ToString("O"),
                                entry
                            });
                            await AgentHttpServer.WebSocketSendTextAsync(stream, resp, cts.Token);
                        }
                        else if (msgType == "clear")
                        {
                            NetworkStore.Clear();
                        }
                    }
                    catch { }
                }
            }, cts.Token);

            // Send loop — drain queue and send pings periodically
            var lastPing = DateTime.UtcNow;
            while (!cts.Token.IsCancellationRequested)
            {
                while (sendQueue.TryDequeue(out var entry))
                {
                    try
                    {
                        var json = JsonSerializer.Serialize(new
                        {
                            type = "request",
                            timestamp = DateTimeOffset.UtcNow.ToString("O"),
                            entry = entry.ToSummary()
                        });
                        await AgentHttpServer.WebSocketSendTextAsync(stream, json, cts.Token);
                    }
                    catch { await cts.CancelAsync(); break; }
                }

                // Send WebSocket ping every 15 seconds to keep connection alive
                if ((DateTime.UtcNow - lastPing).TotalSeconds >= 15)
                {
                    try
                    {
                        await AgentHttpServer.WebSocketSendPingAsync(stream, cts.Token);
                        lastPing = DateTime.UtcNow;
                    }
                    catch { await cts.CancelAsync(); break; }
                }

                try { await Task.Delay(50, cts.Token); }
                catch { break; }
            }

            await readTask;
        }
        finally
        {
            NetworkStore.OnRequestCaptured -= OnRequest;
        }
    }

    protected async Task HandleLogsWebSocket(
        System.Net.Sockets.TcpClient client,
        System.Net.Sockets.NetworkStream stream,
        HttpRequest request,
        CancellationToken ct)
    {
        if (_logProvider == null) return;

        // Parse optional source filter from query string
        request.QueryParams.TryGetValue("source", out var sourceFilter);

        // Parse optional replay count (default 100, 0 to skip replay)
        var replayCount = 100;
        if (request.QueryParams.TryGetValue("replay", out var replayStr) && int.TryParse(replayStr, out var rc))
            replayCount = Math.Max(0, rc);

        // Send replay of recent log entries
        if (replayCount > 0)
        {
            var recent = _logProvider.Reader.Read(replayCount, 0, sourceFilter);
            var replayMsg = JsonSerializer.Serialize(new
            {
                type = "replay",
                timestamp = DateTimeOffset.UtcNow.ToString("O"),
                entries = recent
            });
            await AgentHttpServer.WebSocketSendTextAsync(stream, replayMsg, ct);
        }

        // Subscribe to live log entries
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sendQueue = new System.Collections.Concurrent.ConcurrentQueue<Logging.FileLogEntry>();

        void OnLog(Logging.FileLogEntry entry)
        {
            if (sourceFilter != null && !string.Equals(entry.Source, sourceFilter, StringComparison.OrdinalIgnoreCase))
                return;
            sendQueue.Enqueue(entry);
        }
        _logProvider.Writer.OnLogWritten += OnLog;

        try
        {
            // Read loop (detects disconnection)
            var readTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var msg = await AgentHttpServer.WebSocketReadTextAsync(stream, cts.Token);
                    if (msg == null) { await cts.CancelAsync(); break; }
                }
            }, cts.Token);

            // Send loop — drain queue and send pings periodically
            var lastPing = DateTime.UtcNow;
            while (!cts.Token.IsCancellationRequested)
            {
                while (sendQueue.TryDequeue(out var entry))
                {
                    try
                    {
                        var json = JsonSerializer.Serialize(new
                        {
                            type = "log",
                            timestamp = DateTimeOffset.UtcNow.ToString("O"),
                            entry
                        });
                        await AgentHttpServer.WebSocketSendTextAsync(stream, json, cts.Token);
                    }
                    catch { await cts.CancelAsync(); break; }
                }

                if ((DateTime.UtcNow - lastPing).TotalSeconds >= 15)
                {
                    try
                    {
                        await AgentHttpServer.WebSocketSendPingAsync(stream, cts.Token);
                        lastPing = DateTime.UtcNow;
                    }
                    catch { await cts.CancelAsync(); break; }
                }

                try { await Task.Delay(50, cts.Token); }
                catch { break; }
            }

            await readTask;
        }
        finally
        {
            _logProvider.Writer.OnLogWritten -= OnLog;
        }
    }

    protected async Task HandleProfilerWebSocket(
        System.Net.Sockets.TcpClient client,
        System.Net.Sockets.NetworkStream stream,
        HttpRequest request,
        CancellationToken ct)
    {
        var requestedSessionId = request.QueryParams.GetValueOrDefault("sessionId");
        if (string.IsNullOrWhiteSpace(requestedSessionId))
        {
            await AgentHttpServer.WebSocketSendTextAsync(stream,
                JsonSerializer.Serialize(new
                {
                    type = "error",
                    timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    error = "sessionId query parameter is required"
                }), ct);
            return;
        }

        if (!long.TryParse(request.QueryParams.GetValueOrDefault("sampleCursor", "0"), out var sampleCursor))
            sampleCursor = 0;
        if (!long.TryParse(request.QueryParams.GetValueOrDefault("markerCursor", "0"), out var markerCursor))
            markerCursor = 0;
        if (!long.TryParse(request.QueryParams.GetValueOrDefault("spanCursor", "0"), out var spanCursor))
            spanCursor = 0;
        if (!int.TryParse(request.QueryParams.GetValueOrDefault("limit", "500"), out var limit))
            limit = 500;

        limit = Math.Clamp(limit, 1, 5000);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            var readTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var msg = await AgentHttpServer.WebSocketReadTextAsync(stream, cts.Token);
                    if (msg == null)
                    {
                        await cts.CancelAsync();
                        break;
                    }
                }
            }, cts.Token);

            var sentInitialBatch = false;
            var lastPing = DateTime.UtcNow;

            while (!cts.Token.IsCancellationRequested)
            {
                var currentSession = _profilerSessions.CurrentSession;
                if (currentSession == null)
                {
                    await AgentHttpServer.WebSocketSendTextAsync(stream, JsonSerializer.Serialize(new
                    {
                        type = "stopped",
                        timestamp = DateTimeOffset.UtcNow.ToString("O"),
                        data = new { sessionId = requestedSessionId }
                    }), cts.Token);
                    break;
                }

                if (!requestedSessionId.Equals("current", StringComparison.OrdinalIgnoreCase) &&
                    !requestedSessionId.Equals(currentSession.SessionId, StringComparison.Ordinal))
                {
                    await AgentHttpServer.WebSocketSendTextAsync(stream, JsonSerializer.Serialize(new
                    {
                        type = "error",
                        timestamp = DateTimeOffset.UtcNow.ToString("O"),
                        error = $"Profiler session '{requestedSessionId}' not found"
                    }), cts.Token);
                    break;
                }

                var batch = _profilerSessions.GetBatch(sampleCursor, markerCursor, limit, spanCursor);
                var hasNewData = batch.SampleCursor != sampleCursor ||
                    batch.MarkerCursor != markerCursor ||
                    batch.SpanCursor != spanCursor;

                if (!sentInitialBatch || hasNewData)
                {
                    sampleCursor = batch.SampleCursor;
                    markerCursor = batch.MarkerCursor;
                    spanCursor = batch.SpanCursor;
                    sentInitialBatch = true;

                    await AgentHttpServer.WebSocketSendTextAsync(stream, JsonSerializer.Serialize(new
                    {
                        type = "batch",
                        timestamp = DateTimeOffset.UtcNow.ToString("O"),
                        data = batch
                    }), cts.Token);
                }

                if (!batch.IsActive)
                {
                    await AgentHttpServer.WebSocketSendTextAsync(stream, JsonSerializer.Serialize(new
                    {
                        type = "stopped",
                        timestamp = DateTimeOffset.UtcNow.ToString("O"),
                        data = new { sessionId = batch.SessionId }
                    }), cts.Token);
                    break;
                }

                if ((DateTime.UtcNow - lastPing).TotalSeconds >= 15)
                {
                    try
                    {
                        await AgentHttpServer.WebSocketSendPingAsync(stream, cts.Token);
                        lastPing = DateTime.UtcNow;
                    }
                    catch
                    {
                        await cts.CancelAsync();
                        break;
                    }
                }

                try
                {
                    await Task.Delay(Math.Max(100, currentSession.SampleIntervalMs / 2), cts.Token);
                }
                catch
                {
                    break;
                }
            }

            await readTask;
        }
        catch
        {
            await cts.CancelAsync();
        }
    }

    protected async Task HandleUiEventsWebSocket(
        System.Net.Sockets.TcpClient client,
        System.Net.Sockets.NetworkStream stream,
        HttpRequest request,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var subscription = new UiEventSubscription();

        lock (_uiEventSubscriptionGate)
        {
            _uiEventSubscriptions.Add(subscription);
        }

        try
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            subscription.Queue.Enqueue(JsonSerializer.Serialize(new
            {
                type = "lifecycle",
                timestamp = now,
                data = new
                {
                    state = IsAppBound ? "started" : "stopped",
                    timestamp = now
                }
            }));

            var currentRoute = GetCurrentRouteLocation();
            if (!string.IsNullOrWhiteSpace(currentRoute))
            {
                subscription.Queue.Enqueue(JsonSerializer.Serialize(new
                {
                    type = "navigation",
                    timestamp = now,
                    data = new
                    {
                        from = (string?)null,
                        to = currentRoute,
                        route = currentRoute,
                        timestamp = now
                    }
                }));
            }

            var readTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var msg = await AgentHttpServer.WebSocketReadTextAsync(stream, cts.Token);
                    if (msg == null)
                    {
                        await cts.CancelAsync();
                        break;
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(msg);
                        lock (_uiEventSubscriptionGate)
                        {
                            ApplyUiEventSubscriptionMessage(subscription, doc.RootElement);
                        }
                    }
                    catch
                    {
                    }
                }
            }, cts.Token);

            var lastPing = DateTime.UtcNow;
            while (!cts.Token.IsCancellationRequested)
            {
                while (subscription.Queue.TryDequeue(out var message))
                {
                    try
                    {
                        await AgentHttpServer.WebSocketSendTextAsync(stream, message, cts.Token);
                    }
                    catch
                    {
                        await cts.CancelAsync();
                        break;
                    }
                }

                if ((DateTime.UtcNow - lastPing).TotalSeconds >= 15)
                {
                    try
                    {
                        await AgentHttpServer.WebSocketSendPingAsync(stream, cts.Token);
                        lastPing = DateTime.UtcNow;
                    }
                    catch
                    {
                        await cts.CancelAsync();
                        break;
                    }
                }

                try { await Task.Delay(50, cts.Token); }
                catch { break; }
            }

            await readTask;
        }
        finally
        {
            lock (_uiEventSubscriptionGate)
            {
                _uiEventSubscriptions.Remove(subscription);
            }
        }
    }

    protected Task<HttpResponse> HandleLogs(HttpRequest request)
    {
        if (_logProvider == null)
            return Task.FromResult(HttpResponse.Error("File logging is not enabled"));

        var limitStr = request.QueryParams.GetValueOrDefault("limit", "100");
        var skipStr = request.QueryParams.GetValueOrDefault("skip", "0");
        var source = request.QueryParams.TryGetValue("source", out var s) ? s : null;

        if (!int.TryParse(limitStr, out var limit)) limit = 100;
        if (!int.TryParse(skipStr, out var skip)) skip = 0;

        var entries = _logProvider.Reader.Read(limit, skip, source);
        return Task.FromResult(HttpResponse.Json(entries));
    }

    protected static string? GetRequestedWebViewId(HttpRequest request, string? contextId = null)
        => request.QueryParams.GetValueOrDefault("webview")
            ?? request.QueryParams.GetValueOrDefault("contextId")
            ?? contextId;

    protected bool TryResolveReadyCdpWebView(
        string? webviewId,
        [NotNullWhen(true)] out CdpWebViewInfo? webView,
        [NotNullWhen(false)] out HttpResponse? error)
    {
        if (_cdpWebViews.Count == 0)
        {
            webView = null;
            error = HttpResponse.Error("CDP not available (no Blazor WebViews registered)");
            return false;
        }

        webView = ResolveCdpWebView(webviewId);
        if (webView == null)
        {
            error = HttpResponse.Error($"WebView '{webviewId}' not found. Use GET /api/v1/webview/contexts to list available WebViews.");
            return false;
        }

        // Do not hard-block transient "not ready" states here. The underlying
        // WebView bridge can re-inject chobitsu on demand inside CommandHandler,
        // so rejecting the request at resolution time prevents the self-heal path
        // from ever running and leaves callers stuck in a 400 loop.
        error = null;
        return true;
    }

    protected static string BuildCdpCommand(int id, string method, object? parameters = null)
        => JsonSerializer.Serialize(new { id, method, @params = parameters });

    protected static string? TryGetCdpError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var errorElement))
            return null;

        return errorElement.ValueKind switch
        {
            JsonValueKind.String => errorElement.GetString(),
            JsonValueKind.Object when errorElement.TryGetProperty("message", out var messageElement) => messageElement.GetString(),
            _ => errorElement.GetRawText()
        };
    }

    protected static bool TryGetCdpValue(JsonElement root, out JsonElement value)
    {
        value = default;
        return root.TryGetProperty("result", out var result) &&
               result.TryGetProperty("result", out var innerResult) &&
               innerResult.TryGetProperty("value", out value);
    }

    protected async Task<JsonElement?> EvaluateWebViewExpressionAsync(CdpWebViewInfo webView, string expression, int id = 99996)
    {
        var resultJson = await webView.CommandHandler(BuildCdpCommand(id, "Runtime.evaluate", new
        {
            expression,
            returnByValue = true
        }));

        using var doc = JsonDocument.Parse(resultJson);
        var error = TryGetCdpError(doc.RootElement);
        if (!string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException(error);

        if (TryGetCdpValue(doc.RootElement, out var value))
            return value.Clone();

        // Some bridges (notably Android's Chobitsu-backed path) do not reliably honor
        // returnByValue for arrays/objects and instead hand back an object reference.
        // Fall back to JSON.stringify() so callers still get structured data.
        var fallbackJson = await webView.CommandHandler(BuildCdpCommand(id + 1, "Runtime.evaluate", new
        {
            expression = $"JSON.stringify(({expression}))",
            returnByValue = true
        }));

        using var fallbackDoc = JsonDocument.Parse(fallbackJson);
        error = TryGetCdpError(fallbackDoc.RootElement);
        if (!string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException(error);

        if (TryGetCdpValue(fallbackDoc.RootElement, out var fallbackValue))
        {
            if (fallbackValue.ValueKind == JsonValueKind.String)
            {
                var json = fallbackValue.GetString();
                if (!string.IsNullOrWhiteSpace(json) && !string.Equals(json, "undefined", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(json);
                        return jsonDoc.RootElement.Clone();
                    }
                    catch
                    {
                        return JsonSerializer.SerializeToElement(json);
                    }
                }
            }

            return fallbackValue.Clone();
        }

        return null;
    }

    protected async Task<HttpResponse> HandleCdp(HttpRequest request)
    {
        request.QueryParams.TryGetValue("webview", out var webviewId);
        if (!TryResolveReadyCdpWebView(webviewId, out var webView, out var error))
            return error!;

        if (string.IsNullOrEmpty(request.Body))
            return HttpResponse.Error("Missing CDP command body");

        try
        {
            var result = await webView.CommandHandler(request.Body);
            return new HttpResponse
            {
                ContentType = "application/json",
                Body = result
            };
        }
        catch (Exception ex)
        {
            return HttpResponse.Error($"CDP command failed: {ex.Message}");
        }
    }

    protected sealed class WebViewDomQueryRequest
    {
        public string? Selector { get; set; }
        public string? ContextId { get; set; }
    }

    protected async Task<HttpResponse> HandleWebViewNavigate(HttpRequest request)
    {
        var body = request.BodyAs<WebViewNavigateRequest>();
        if (string.IsNullOrWhiteSpace(body?.Url))
            return HttpResponse.Error("url is required");

        var webviewId = GetRequestedWebViewId(request, body.ContextId);
        if (!TryResolveReadyCdpWebView(webviewId, out var webView, out var error))
            return error!;

        try
        {
            var resultJson = await webView!.CommandHandler(BuildCdpCommand(99995, "Page.navigate", new { url = body.Url }));
            using var doc = JsonDocument.Parse(resultJson);
            var cdpError = TryGetCdpError(doc.RootElement);
            if (!string.IsNullOrWhiteSpace(cdpError))
                return HttpResponse.Error($"WebView navigation failed: {cdpError}");

            webView.Url = body.Url;
            PublishUiEvent("navigation", new
            {
                from = (string?)null,
                to = body.Url,
                route = body.Url,
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            });

            return HttpResponse.Json(new
            {
                success = true,
                contextId = webviewId ?? webView.Index.ToString(),
                url = body.Url
            });
        }
        catch (Exception ex)
        {
            return HttpResponse.Error($"WebView navigation failed: {ex.Message}");
        }
    }

    protected async Task<HttpResponse> HandleWebViewInputClick(HttpRequest request)
    {
        var body = request.BodyAs<WebViewInputClickRequest>();
        if (string.IsNullOrWhiteSpace(body?.Selector))
            return HttpResponse.Error("selector is required");

        var webviewId = GetRequestedWebViewId(request, body.ContextId);
        if (!TryResolveReadyCdpWebView(webviewId, out var webView, out var error))
            return error!;

        try
        {
            var selectorJson = JsonSerializer.Serialize(body.Selector);
            var value = await EvaluateWebViewExpressionAsync(
                webView!,
                $@"(function() {{
                    const el = document.querySelector({selectorJson});
                    if (!el) return {{ success: false, error: 'Element not found' }};
                    if (typeof el.scrollIntoView === 'function') el.scrollIntoView({{ block: 'center', inline: 'center' }});
                    if (typeof el.focus === 'function') el.focus();
                    if (typeof el.click === 'function') el.click();
                    else el.dispatchEvent(new MouseEvent('click', {{ bubbles: true, cancelable: true, view: window }}));
                    return {{ success: true, tagName: el.tagName ? el.tagName.toLowerCase() : null }};
                }})()");

            if (value is not JsonElement clickResult)
                return HttpResponse.Error("Click did not return a result");

            if (clickResult.ValueKind == JsonValueKind.Object &&
                clickResult.TryGetProperty("success", out var successElement) &&
                !successElement.GetBoolean())
            {
                return HttpResponse.NotFound($"No element matches selector '{body.Selector}'");
            }

            return new HttpResponse
            {
                ContentType = "application/json",
                Body = clickResult.GetRawText()
            };
        }
        catch (Exception ex)
        {
            return HttpResponse.Error($"WebView click failed: {ex.Message}");
        }
    }

    protected async Task<HttpResponse> HandleWebViewInputFill(HttpRequest request)
    {
        var body = request.BodyAs<WebViewInputFillRequest>();
        if (string.IsNullOrWhiteSpace(body?.Selector))
            return HttpResponse.Error("selector is required");
        if (body.Text == null)
            return HttpResponse.Error("text is required");

        var webviewId = GetRequestedWebViewId(request, body.ContextId);
        if (!TryResolveReadyCdpWebView(webviewId, out var webView, out var error))
            return error!;

        try
        {
            var selectorJson = JsonSerializer.Serialize(body.Selector);
            var textJson = JsonSerializer.Serialize(body.Text);
            var value = await EvaluateWebViewExpressionAsync(
                webView!,
                $@"(function() {{
                    const el = document.querySelector({selectorJson});
                    if (!el) return {{ success: false, error: 'Element not found' }};
                    if (typeof el.focus === 'function') el.focus();

                    if ('value' in el) el.value = {textJson};
                    else if (el.isContentEditable) el.textContent = {textJson};
                    else return {{ success: false, error: 'Element does not accept text input' }};

                    el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                    el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                    return {{ success: true, textLength: {body.Text.Length} }};
                }})()");

            if (value is not JsonElement fillResult)
                return HttpResponse.Error("Fill did not return a result");

            if (fillResult.ValueKind == JsonValueKind.Object &&
                fillResult.TryGetProperty("success", out var successElement) &&
                !successElement.GetBoolean())
            {
                var errorMessage = fillResult.TryGetProperty("error", out var errorElement)
                    ? errorElement.GetString()
                    : null;

                return string.Equals(errorMessage, "Element not found", StringComparison.OrdinalIgnoreCase)
                    ? HttpResponse.NotFound($"No element matches selector '{body.Selector}'")
                    : HttpResponse.Error(errorMessage ?? "WebView fill failed");
            }

            PublishUiEvent("treeChange", new
            {
                changeType = "modified",
                elementId = body.Selector,
                elementType = "webview-input",
                parentId = (string?)null,
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            });

            return new HttpResponse
            {
                ContentType = "application/json",
                Body = fillResult.GetRawText()
            };
        }
        catch (Exception ex)
        {
            return HttpResponse.Error($"WebView fill failed: {ex.Message}");
        }
    }

    protected async Task<HttpResponse> HandleWebViewInputText(HttpRequest request)
    {
        var body = request.BodyAs<WebViewInputTextRequest>();
        if (body?.Text == null)
            return HttpResponse.Error("text is required");

        var webviewId = GetRequestedWebViewId(request, body.ContextId);
        if (!TryResolveReadyCdpWebView(webviewId, out var webView, out var error))
            return error!;

        try
        {
            var resultJson = await webView!.CommandHandler(BuildCdpCommand(99994, "Input.insertText", new { text = body.Text }));
            using var doc = JsonDocument.Parse(resultJson);
            var cdpError = TryGetCdpError(doc.RootElement);
            if (!string.IsNullOrWhiteSpace(cdpError))
                return HttpResponse.Error($"WebView text input failed: {cdpError}");

            PublishUiEvent("treeChange", new
            {
                changeType = "modified",
                elementId = body.ContextId ?? webviewId ?? webView.Index.ToString(),
                elementType = "webview-input",
                parentId = (string?)null,
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            });

            return HttpResponse.Json(new
            {
                success = true,
                textLength = body.Text.Length
            });
        }
        catch (Exception ex)
        {
            return HttpResponse.Error($"WebView text input failed: {ex.Message}");
        }
    }

    protected Task<HttpResponse> HandleWebViewDom(HttpRequest request)
        => HandleCdpSource(request);

    protected async Task<HttpResponse> HandleWebViewDomQuery(HttpRequest request)
    {
        var body = request.BodyAs<WebViewDomQueryRequest>();
        if (string.IsNullOrWhiteSpace(body?.Selector))
            return HttpResponse.Error("selector is required");

        var webviewId = GetRequestedWebViewId(request, body.ContextId);
        if (!TryResolveReadyCdpWebView(webviewId, out var webView, out var error))
            return error!;

        try
        {
            var selectorJson = JsonSerializer.Serialize(body.Selector);
            var value = await EvaluateWebViewExpressionAsync(
                webView!,
                $@"(function() {{
                    return Array.from(document.querySelectorAll({selectorJson})).map((el, index) => ({{
                        index,
                        tagName: el.tagName ? el.tagName.toLowerCase() : null,
                        id: el.id || null,
                        className: el.className || null,
                        text: (el.innerText || el.textContent || '').trim()
                    }}));
                }})()",
                id: 99998);

            if (value is JsonElement matches)
            {
                return new HttpResponse
                {
                    ContentType = "application/json",
                    Body = matches.GetRawText()
                };
            }

            return HttpResponse.Error("Failed to query DOM");
        }
        catch (Exception ex)
        {
            return HttpResponse.Error($"DOM query failed: {ex.Message}");
        }
    }

    protected Task<HttpResponse> HandleWebViewNetwork(HttpRequest request)
        => HandleNetworkList(request);

    protected Task<HttpResponse> HandleWebViewConsole(HttpRequest request)
    {
        request.QueryParams["source"] = "webview";
        return HandleLogs(request);
    }

    protected async Task<HttpResponse> HandleWebViewScreenshot(HttpRequest request)
    {
        var webviewId = GetRequestedWebViewId(request);
        if (!TryResolveReadyCdpWebView(webviewId, out var webView, out var error))
            return error!;

        try
        {
            var nativeCapture = await TryCaptureRegisteredWebViewAsync(webView!);
            if (nativeCapture != null)
                return nativeCapture;

            var cdpCommand = JsonSerializer.Serialize(new
            {
                id = 99997,
                method = "Page.captureScreenshot",
                @params = new { format = "png" }
            });

            var resultJson = await webView.CommandHandler(cdpCommand);
            using var doc = JsonDocument.Parse(resultJson);
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("data", out var data) &&
                data.GetString() is { Length: > 0 } base64)
            {
                return HttpResponse.Png(Convert.FromBase64String(base64));
            }

            var fallback = await TryCaptureRegisteredWebViewAsync(webView!);
            return fallback ?? HttpResponse.Error("Failed to capture WebView screenshot");
        }
        catch (Exception ex)
        {
            var fallback = webView != null
                ? await TryCaptureRegisteredWebViewAsync(webView)
                : null;
            return fallback ?? HttpResponse.Error($"WebView screenshot failed: {ex.Message}");
        }
    }

    protected Task<HttpResponse> HandleCdpWebViews(HttpRequest request)
    {
        var webviews = _cdpWebViews.Select(w => new
        {
            id = !string.IsNullOrWhiteSpace(w.AutomationId)
                ? w.AutomationId
                : !string.IsNullOrWhiteSpace(w.ElementId)
                    ? w.ElementId
                    : w.Index.ToString(),
            index = w.Index,
            automationId = w.AutomationId,
            elementId = w.ElementId,
            url = w.Url,
            title = (string?)null,
            ready = w.IsReady,
            isReady = w.IsReady,
        }).ToList();

        return Task.FromResult(HttpResponse.Json(new { webviews }));
    }

    protected async Task<HttpResponse> HandleCdpSource(HttpRequest request)
    {
        var webviewId = GetRequestedWebViewId(request);
        if (!TryResolveReadyCdpWebView(webviewId, out var webView, out var error))
            return error!;

        try
        {
            var cdpCommand = System.Text.Json.JsonSerializer.Serialize(new
            {
                id = 99999,
                method = "Runtime.evaluate",
                @params = new { expression = "document.documentElement.outerHTML", returnByValue = true }
            });

            var resultJson = await webView.CommandHandler(cdpCommand);
            using var doc = System.Text.Json.JsonDocument.Parse(resultJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("result", out var result) &&
                result.TryGetProperty("result", out var innerResult) &&
                innerResult.TryGetProperty("value", out var value))
            {
                return new HttpResponse
                {
                    ContentType = "text/html",
                    Body = value.GetString() ?? ""
                };
            }

            return HttpResponse.Error("Failed to extract page source from CDP response");
        }
        catch (Exception ex)
        {
            return HttpResponse.Error($"Failed to get page source: {ex.Message}");
        }
    }

    // ── File storage endpoints ──

    protected const string DefaultFileStorageRootId = "appData";

    protected const string FileStorageOperationList = "list";

    protected const string FileStorageOperationDownload = "download";

    protected const string FileStorageOperationUpload = "upload";

    protected const string FileStorageOperationDelete = "delete";

    protected sealed class FileStorageRoot
    {
        public FileStorageRoot(
            string id,
            string displayName,
            string kind,
            string basePath,
            bool isWritable,
            bool isPersistent,
            bool isBackedUp,
            bool mayBeClearedBySystem,
            bool isUserVisible,
            params string[] supportedOperations)
        {
            Id = id;
            DisplayName = displayName;
            Kind = kind;
            BasePath = basePath;
            IsWritable = isWritable;
            IsPersistent = isPersistent;
            IsBackedUp = isBackedUp;
            MayBeClearedBySystem = mayBeClearedBySystem;
            IsUserVisible = isUserVisible;
            SupportedOperations = supportedOperations;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Kind { get; }
        public string BasePath { get; }
        public bool IsWritable { get; }
        public bool IsReadOnly => !IsWritable;
        public bool IsPersistent { get; }
        public bool IsBackedUp { get; }
        public bool MayBeClearedBySystem { get; }
        public bool IsUserVisible { get; }
        public IReadOnlyList<string> SupportedOperations { get; }

        public bool SupportsOperation(string operation)
            => SupportedOperations.Contains(operation, StringComparer.Ordinal);
    }

    protected virtual IReadOnlyList<FileStorageRoot> GetFileStorageRoots()
    {
        var appDataPath = GetAppDataBasePath();
        if (string.IsNullOrWhiteSpace(appDataPath))
            return Array.Empty<FileStorageRoot>();

        return new[]
        {
            new FileStorageRoot(
                DefaultFileStorageRootId,
                "App data",
                "appData",
                appDataPath,
                isWritable: true,
                isPersistent: true,
                isBackedUp: true,
                mayBeClearedBySystem: false,
                isUserVisible: false,
                FileStorageOperationList,
                FileStorageOperationDownload,
                FileStorageOperationUpload,
                FileStorageOperationDelete)
        };
    }

    protected Task<HttpResponse> HandleStorageRoots(HttpRequest request)
    {
        try
        {
            return Task.FromResult(HttpResponse.Json(new
            {
                roots = GetFileStorageRoots().Select(ToFileStorageRootDescriptor).ToArray()
            }));
        }
        catch (Exception)
        {
            return Task.FromResult(HttpResponse.Error("Failed to list storage roots"));
        }
    }

    protected static object ToFileStorageRootDescriptor(FileStorageRoot root)
        => new
        {
            id = root.Id,
            displayName = root.DisplayName,
            kind = root.Kind,
            isWritable = root.IsWritable,
            isReadOnly = root.IsReadOnly,
            isPersistent = root.IsPersistent,
            isBackedUp = root.IsBackedUp,
            mayBeClearedBySystem = root.MayBeClearedBySystem,
            isUserVisible = root.IsUserVisible,
            supportedOperations = root.SupportedOperations.ToArray()
        };

    protected FileStorageRoot ResolveFileStorageRoot(HttpRequest request, string operation)
    {
        var rootId = request.QueryParams.GetValueOrDefault("root");
        if (string.IsNullOrWhiteSpace(rootId))
            rootId = DefaultFileStorageRootId;

        var root = GetFileStorageRoots().FirstOrDefault(
            r => string.Equals(r.Id, rootId, StringComparison.Ordinal));

        if (root == null)
            throw new InvalidOperationException($"Storage root '{rootId}' is not available. Use /api/v1/storage/roots to list supported roots.");

        if (!root.SupportsOperation(operation))
            throw new InvalidOperationException($"Storage root '{root.Id}' does not support '{operation}'.");

        if (string.IsNullOrWhiteSpace(root.BasePath))
            throw new InvalidOperationException($"Storage root '{root.Id}' is not available.");

        return root;
    }

    protected Task<HttpResponse> HandleFilesList(HttpRequest request)
    {
        try
        {
            var root = ResolveFileStorageRoot(request, FileStorageOperationList);
            var subdir = request.QueryParams.GetValueOrDefault("path", "");
            var resolved = FileStoragePathResolver.Resolve(root.BasePath, subdir, allowRoot: true);
            FileStoragePathResolver.EnsureNoReparsePointTraversal(resolved.BasePath, resolved.FullPath, includeTarget: true);

            if (!Directory.Exists(resolved.FullPath))
                return Task.FromResult(HttpResponse.Json(new
                {
                    root = root.Id,
                    path = resolved.RelativePath,
                    entries = Array.Empty<object>()
                }));

            var entries = new List<object>();
            foreach (var dir in Directory.GetDirectories(resolved.FullPath))
            {
                var info = new DirectoryInfo(dir);
                entries.Add(new
                {
                    name = info.Name,
                    type = "directory",
                    lastModified = info.LastWriteTimeUtc.ToString("O")
                });
            }
            foreach (var file in Directory.GetFiles(resolved.FullPath))
            {
                var info = new FileInfo(file);
                entries.Add(new
                {
                    name = info.Name,
                    type = "file",
                    size = info.Length,
                    lastModified = info.LastWriteTimeUtc.ToString("O")
                });
            }

            return Task.FromResult(HttpResponse.Json(new
            {
                root = root.Id,
                path = resolved.RelativePath,
                entries
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ex is InvalidOperationException
                ? HttpResponse.Error(ex.Message)
                : HttpResponse.Error("Failed to list files"));
        }
    }

    protected async Task<HttpResponse> HandleFileDownload(HttpRequest request)
    {
        try
        {
            if (!request.RouteParams.TryGetValue("path", out var relativePath) || string.IsNullOrWhiteSpace(relativePath))
                return HttpResponse.Error("file path is required");

            var root = ResolveFileStorageRoot(request, FileStorageOperationDownload);
            relativePath = Uri.UnescapeDataString(relativePath);
            var resolved = FileStoragePathResolver.Resolve(root.BasePath, relativePath);
            FileStoragePathResolver.EnsureNoReparsePointTraversal(resolved.BasePath, resolved.FullPath, includeTarget: true);

            if (!File.Exists(resolved.FullPath))
                return HttpResponse.NotFound($"File not found: {relativePath}");

            var bytes = await File.ReadAllBytesAsync(resolved.FullPath);
            var contentBase64 = Convert.ToBase64String(bytes);
            var info = new FileInfo(resolved.FullPath);

            return HttpResponse.Json(new
            {
                root = root.Id,
                path = resolved.RelativePath,
                size = info.Length,
                lastModified = info.LastWriteTimeUtc.ToString("O"),
                contentBase64
            });
        }
        catch (InvalidOperationException ex)
        {
            return HttpResponse.Error(ex.Message);
        }
        catch (Exception)
        {
            return HttpResponse.Error("Failed to download file");
        }
    }

    protected async Task<HttpResponse> HandleFileUpload(HttpRequest request)
    {
        try
        {
            if (!request.RouteParams.TryGetValue("path", out var relativePath) || string.IsNullOrWhiteSpace(relativePath))
                return HttpResponse.Error("file path is required");

            var root = ResolveFileStorageRoot(request, FileStorageOperationUpload);
            relativePath = Uri.UnescapeDataString(relativePath);
            var resolved = FileStoragePathResolver.Resolve(root.BasePath, relativePath);
            FileStoragePathResolver.EnsureNoReparsePointTraversal(resolved.BasePath, resolved.FullPath, includeTarget: true);

            var body = request.BodyAs<FileUploadRequest>();
            if (body == null || string.IsNullOrEmpty(body.ContentBase64))
                return HttpResponse.Error("Request body must include 'contentBase64'");

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(body.ContentBase64);
            }
            catch (FormatException)
            {
                return HttpResponse.Error("Invalid base64 content");
            }

            var dir = Path.GetDirectoryName(resolved.FullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            FileStoragePathResolver.EnsureNoReparsePointTraversal(resolved.BasePath, resolved.FullPath, includeTarget: true);

            await File.WriteAllBytesAsync(resolved.FullPath, bytes);
            var info = new FileInfo(resolved.FullPath);

            return HttpResponse.Json(new
            {
                success = true,
                root = root.Id,
                path = resolved.RelativePath,
                size = info.Length,
                lastModified = info.LastWriteTimeUtc.ToString("O")
            });
        }
        catch (InvalidOperationException ex)
        {
            return HttpResponse.Error(ex.Message);
        }
        catch (Exception)
        {
            return HttpResponse.Error("Failed to upload file");
        }
    }

    protected Task<HttpResponse> HandleFileDelete(HttpRequest request)
    {
        try
        {
            if (!request.RouteParams.TryGetValue("path", out var relativePath) || string.IsNullOrWhiteSpace(relativePath))
                return Task.FromResult(HttpResponse.Error("file path is required"));

            var root = ResolveFileStorageRoot(request, FileStorageOperationDelete);
            relativePath = Uri.UnescapeDataString(relativePath);
            var resolved = FileStoragePathResolver.Resolve(root.BasePath, relativePath);
            FileStoragePathResolver.EnsureNoReparsePointTraversal(resolved.BasePath, resolved.FullPath, includeTarget: true);

            if (!File.Exists(resolved.FullPath))
                return Task.FromResult(HttpResponse.NotFound($"File not found: {relativePath}"));

            File.Delete(resolved.FullPath);
            return Task.FromResult(HttpResponse.Json(new
            {
                success = true,
                root = root.Id,
                path = resolved.RelativePath,
                message = $"File deleted: {resolved.RelativePath}"
            }));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(HttpResponse.Error(ex.Message));
        }
        catch (Exception)
        {
            return Task.FromResult(HttpResponse.Error("Failed to delete file"));
        }
    }

    // ── Platform info endpoints ──

    public const string PlatformErrorReasonMissingPermission = "missing_permission";

    public const string PlatformErrorReasonNotSupported = "not_supported";

    public const string PlatformErrorReasonMainThreadRequired = "main_thread_required";

    public const string PlatformErrorReasonTimeout = "timeout";

    public const string PlatformErrorReasonUnknown = "unknown";

    public const string PlatformErrorReasonInvalidRequest = "invalid_request";

    protected static readonly Regex AndroidPermissionRegex = new(@"android\.permission\.[A-Z0-9_\.]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    protected static readonly string[] SupportedThemeNames = ["light", "dark", "system"];

    public static HttpResponse CreatePlatformError(string message, Exception ex, int statusCode = 400, Dictionary<string, object?>? details = null)
    {
        var payload = BuildPlatformErrorPayload(ex, details);
        return HttpResponse.Error(message, payload.StatusCode ?? statusCode, payload.Reason, payload.Details);
    }

    public static HttpResponse CreatePlatformError(string message, string reason, int statusCode = 400, Dictionary<string, object?>? details = null)
    {
        var payloadDetails = CreatePlatformErrorDetails();
        if (details != null)
        {
            foreach (var (key, value) in details)
            {
                if (value != null)
                    payloadDetails[key] = value;
            }
        }

        return HttpResponse.Error(message, statusCode, reason, payloadDetails.Count > 0 ? payloadDetails : null);
    }

    public static (string Reason, Dictionary<string, object?>? Details, int? StatusCode) BuildPlatformErrorPayload(
        Exception ex,
        Dictionary<string, object?>? details = null)
    {
        var payloadDetails = CreatePlatformErrorDetails();
        if (details != null)
        {
            foreach (var (key, value) in details)
            {
                if (value != null)
                    payloadDetails[key] = value;
            }
        }

        if (IsMissingPermissionException(ex))
        {
            if (TryExtractPermission(ex.Message) is { Length: > 0 } permission)
                payloadDetails["permission"] = permission;

            return (PlatformErrorReasonMissingPermission, payloadDetails.Count > 0 ? payloadDetails : null, 403);
        }

        if (IsMainThreadAccessException(ex))
            return (PlatformErrorReasonMainThreadRequired, payloadDetails.Count > 0 ? payloadDetails : null, null);

        if (ex is TimeoutException or TaskCanceledException or OperationCanceledException)
            return (PlatformErrorReasonTimeout, payloadDetails.Count > 0 ? payloadDetails : null, 408);

        if (ex is NotSupportedException or PlatformNotSupportedException
            || IsNamedException(ex, "FeatureNotSupportedException")
            || IsNamedException(ex, "FeatureNotEnabledException"))
        {
            if (IsNamedException(ex, "FeatureNotEnabledException"))
                payloadDetails["enabled"] = false;

            return (PlatformErrorReasonNotSupported, payloadDetails.Count > 0 ? payloadDetails : null, null);
        }

        payloadDetails["exceptionType"] = ex.GetType().Name;
        return (PlatformErrorReasonUnknown, payloadDetails, null);
    }

    /// <summary>
    /// Matches an exception by simple type name so framework specific exception types
    /// can be recognised without referencing the owning assembly.
    /// </summary>
    public static bool IsNamedException(Exception ex, string typeName)
    {
        for (var type = ex.GetType(); type != null; type = type.BaseType)
        {
            if (string.Equals(type.Name, typeName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static Dictionary<string, object?> CreatePlatformErrorDetails()
    {
        var details = new Dictionary<string, object?>(StringComparer.Ordinal);
        try
        {
            details["platform"] = DetectPlatformName();
        }
        catch
        {
        }

        return details;
    }

    protected static bool IsMissingPermissionException(Exception ex)
    {
        return IsNamedException(ex, "PermissionException")
            || AndroidPermissionRegex.IsMatch(ex.Message)
            || ex.Message.Contains("AndroidManifest", StringComparison.OrdinalIgnoreCase);
    }

    protected static string? TryExtractPermission(string message)
    {
        var match = AndroidPermissionRegex.Match(message);
        return match.Success ? match.Value : null;
    }

    protected static bool IsMainThreadAccessException(Exception ex)
    {
        return ex.GetType().Name.Equals("UIKitThreadAccessException", StringComparison.Ordinal)
            || ex.Message.Contains("main thread", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("UI thread", StringComparison.OrdinalIgnoreCase);
    }

    // ── Job endpoints ──

    protected async Task<HttpResponse> HandleJobsList(HttpRequest request)
    {
        var jobs = await GetPlatformJobsAsync();
        if (jobs == null)
            return HttpResponse.Json(new { platform = PlatformName, supported = false, jobs = Array.Empty<object>() });

        return HttpResponse.Json(jobs);
    }

    protected async Task<HttpResponse> HandleJobRun(HttpRequest request)
    {
        if (!request.RouteParams.TryGetValue("identifier", out var identifier) || string.IsNullOrWhiteSpace(identifier))
            return HttpResponse.Error("job identifier is required");

        var runRequest = request.BodyAs<JobRunRequest>();
        var type = runRequest?.Type;
        if (string.IsNullOrWhiteSpace(type) && request.QueryParams.TryGetValue("type", out var queryType))
            type = queryType;

        var result = await RunPlatformJobAsync(identifier, type);
        if (result == null)
            return HttpResponse.Error($"Running jobs is not supported on {PlatformName}", 501, "unsupported-capability");

        return HttpResponse.Json(result);
    }
}

/// <summary>
/// Describes an actionable cause for a failed screenshot capture, used to return a clear,
/// often-retryable error to clients instead of a generic failure. See
/// <see cref="DevFlowAgentService.DescribeScreenshotFailure"/>.
/// </summary>
public sealed class ScreenshotCaptureFailure
{
    public ScreenshotCaptureFailure(string message, string reason, bool retryable, string[]? suggestions = null)
    {
        Message = message;
        Reason = reason;
        Retryable = retryable;
        Suggestions = suggestions;
    }

    /// <summary>Human-readable, actionable error message.</summary>
    public string Message { get; }

    /// <summary>Machine-readable cause identifier (e.g. <c>window-not-frontmost</c>).</summary>
    public string Reason { get; }

    /// <summary>Whether retrying (e.g. after foregrounding the app) may succeed.</summary>
    public bool Retryable { get; }

    /// <summary>Optional actionable suggestions for the caller.</summary>
    public string[]? Suggestions { get; }
}

// Request DTOs
public class CaptureBoundRequest
{
    public long? CaptureEpoch { get; set; }
    public long? RegistryGeneration { get; set; }
}

public class ActionRequest : CaptureBoundRequest
{
    public string? ElementId { get; set; }
}

public class FillRequest : CaptureBoundRequest
{
    public string? ElementId { get; set; }
    public string? Text { get; set; }
}

public class JobRunRequest
{
    public string? Type { get; set; }
}

public class NavigateRequest
{
    public string? Route { get; set; }
}

public class WebViewNavigateRequest
{
    public string? Url { get; set; }
    public string? ContextId { get; set; }
}

public class WebViewInputClickRequest
{
    public string? Selector { get; set; }
    public string? ContextId { get; set; }
}

public class WebViewInputFillRequest
{
    public string? Selector { get; set; }
    public string? Text { get; set; }
    public string? ContextId { get; set; }
}

public class WebViewInputTextRequest
{
    public string? Text { get; set; }
    public string? ContextId { get; set; }
}

public class SetPropertyRequest : CaptureBoundRequest
{
    public string? Value { get; set; }
}

public class ScrollRequest : CaptureBoundRequest
{
    public string? ElementId { get; set; }
    public double DeltaX { get; set; }
    public double DeltaY { get; set; }
    public bool Animated { get; set; } = true;
    public int? ItemIndex { get; set; }
    public int? GroupIndex { get; set; }
    public string? ScrollToPosition { get; set; }
}

public class PreferenceSetRequest
{
    public object? Value { get; set; }
    public string? Type { get; set; }
    public string? SharedName { get; set; }
}

public class ThemeSetRequest
{
    public string? Theme { get; set; }
}

public class SecureStorageSetRequest
{
    public string? Value { get; set; }
}

public class FileUploadRequest
{
    public string? ContentBase64 { get; set; }
}
