using System.Reflection;
using System.Text.Json;

using Microsoft.Maui.DevFlow.Agent.Core.Network;
using Microsoft.Maui.DevFlow.Agent.Core.Profiling;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// The main agent service that hosts the HTTP API and coordinates
/// visual tree inspection and element interactions.
/// </summary>
/// <remarks>
/// This half of the service is framework neutral — it contains routing, transport, and every
/// handler that does not depend on a UI framework. UI framework specific behaviour is exposed
/// through <c>protected virtual</c> seams that return a <c>501 not_supported</c> envelope by
/// default and are overridden by a backend (MAUI, Android views, UIKit, AppKit, GTK, WPF).
/// </remarks>
public partial class DevFlowAgentService : IDisposable, IMarkerPublisher
{
    /// <summary>
    /// The UI thread dispatcher supplied by the hosting backend, when available.
    /// </summary>
    protected IAgentDispatcher? _dispatcher;

    /// <summary>
    /// Creates a new agent service instance.
    /// </summary>
    public DevFlowAgentService(AgentOptions? options = null)
    {
        _options = options ?? new AgentOptions();
        _server = new AgentHttpServer(_options.Port);
        NetworkStore = new NetworkRequestStore(_options.MaxNetworkBufferSize);
        _profilerCollector = CreateProfilerCollector();
        _profilerSessions = new ProfilerSessionStore(
            Math.Max(1, _options.MaxProfilerSamples),
            Math.Max(1, _options.MaxProfilerMarkers),
            Math.Max(1, _options.MaxProfilerSpans));
        if (_options.EnableNetworkMonitoring)
            DevFlowHttp.SetStore(NetworkStore);
        NetworkStore.OnRequestCaptured += HandleCapturedNetworkRequest;
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
        RegisterRoutes();
    }

    /// <summary>
    /// Gets a value indicating whether the agent is bound to a running application object.
    /// Backends that track an application instance override this.
    /// </summary>
    public virtual bool IsAppBound => false;

    // ── Framework identity ────────────────────────────────────────────────

    /// <summary>Short framework identifier reported by <c>/api/v1/agent/capabilities</c>.</summary>
    protected virtual string FrameworkName => "native";

    /// <summary>Human readable framework name reported by <c>/api/v1/agent/status</c>.</summary>
    protected virtual string FrameworkDisplayName => ".NET";

    /// <summary>
    /// Identifies the UI framework the backend drives — for example <c>maui-controls</c>,
    /// <c>android-views</c>, <c>uikit</c>, <c>appkit</c>, <c>gtk</c> or <c>wpf</c>.
    /// </summary>
    protected virtual string UiFrameworkName => DetectNativeUiFrameworkName();

    /// <summary>Version string for the UI framework the backend drives.</summary>
    protected virtual string FrameworkVersion => Environment.Version.ToString();

    /// <summary>Detects the default native UI framework for the current runtime platform.</summary>
    protected static string DetectNativeUiFrameworkName()
    {
        if (OperatingSystem.IsAndroid()) return "android-views";
        if (OperatingSystem.IsMacCatalyst()) return "uikit";
        if (OperatingSystem.IsIOS()) return "uikit";
        if (OperatingSystem.IsMacOS()) return "appkit";
        if (OperatingSystem.IsWindows()) return "winui";
        return "unknown";
    }

    /// <summary>Detects a platform name without depending on a UI framework.</summary>
    protected static string DetectPlatformName()
    {
        if (OperatingSystem.IsAndroid()) return "Android";
        if (OperatingSystem.IsMacCatalyst()) return "MacCatalyst";
        if (OperatingSystem.IsIOS()) return "iOS";
        if (OperatingSystem.IsTvOS()) return "tvOS";
        if (OperatingSystem.IsMacOS()) return "macOS";
        if (OperatingSystem.IsWindows()) return "WinUI";
        if (OperatingSystem.IsLinux()) return "Linux";
        return "Unknown";
    }

    /// <summary>Platform name reported to clients.</summary>
    protected virtual string PlatformName => DetectPlatformName();

    /// <summary>Device type ("Physical" / "Virtual") reported to clients.</summary>
    protected virtual string DeviceTypeName => "Unknown";

    /// <summary>Device idiom ("Phone" / "Tablet" / "Desktop") reported to clients.</summary>
    protected virtual string IdiomName => "Unknown";

    // ── App identity ──────────────────────────────────────────────────────

    /// <summary>Display name of the host application.</summary>
    protected virtual string AppDisplayName
        => Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown";

    /// <summary>Package / bundle identifier of the host application.</summary>
    protected virtual string AppPackageId => "unknown";

    /// <summary>Marketing version of the host application.</summary>
    protected virtual string AppVersionString
        => Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

    /// <summary>Build number of the host application.</summary>
    protected virtual string AppBuildString => "unknown";

    /// <summary>Number of windows the host application currently owns.</summary>
    protected virtual int WindowCount => 0;

    /// <summary>
    /// Returns the logical size and display density of the requested window.
    /// </summary>
    protected virtual (double Width, double Height, double Density) GetWindowMetrics(int? windowIndex)
        => (0, 0, 1.0);

    /// <summary>
    /// Returns the current navigation route/location, when the UI framework exposes one.
    /// </summary>
    protected virtual string? GetCurrentRouteLocation() => null;

    /// <summary>
    /// Base path used to resolve the default file storage root.
    /// </summary>
    protected virtual string GetAppDataBasePath()
        => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    /// Releases backend owned resources during <see cref="Dispose"/>.
    /// </summary>
    protected virtual void DisposeBackendResources()
    {
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the HTTP server using the supplied UI thread dispatcher.
    /// </summary>
    public void StartServerOnly(IAgentDispatcher? dispatcher)
    {
        if (_disposed || !_options.Enabled) return;
        _dispatcher = dispatcher ?? _dispatcher;
        try
        {
            _server.Start();
            Console.WriteLine($"[Microsoft.Maui.DevFlow.Agent] HTTP server started on port {_options.Port}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Microsoft.Maui.DevFlow.Agent] Failed to start HTTP server: {ex.Message}");
        }
    }

    // ── Status / capabilities ─────────────────────────────────────────────

    /// <summary>
    /// Assembly informational version of the agent.
    /// </summary>
    protected static string AgentVersion { get; } =
        typeof(DevFlowAgentService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private async Task<HttpResponse> HandleStatus(HttpRequest request)
    {
        var windowIndex = ParseWindowIndex(request);
        var appName = AppDisplayName;
        var packageId = AppPackageId;
        var appVersion = AppVersionString;
        var appBuild = AppBuildString;

        var result = await DispatchAsync(() =>
        {
            var (w, h, density) = GetWindowMetrics(windowIndex);

            return new
            {
                timestamp = DateTimeOffset.UtcNow.ToString("O"),
                agent = new
                {
                    name = "Microsoft.Maui.DevFlow.Agent",
                    version = AgentVersion,
                    framework = FrameworkDisplayName,
                    frameworkVersion = Environment.Version.ToString(),
                    uiFramework = UiFrameworkName,
                    sessionId = _sessionId,
                },
                device = new
                {
                    platform = PlatformName,
                    deviceType = DeviceTypeName,
                    idiom = IdiomName,
                    displayDensity = density,
                    windowCount = WindowCount,
                    windowWidth = double.IsFinite(w) ? w : 0,
                    windowHeight = double.IsFinite(h) ? h : 0,
                },
                app = new
                {
                    name = appName,
                    packageId = packageId,
                    version = appVersion,
                    build = appBuild,
                },
                capabilities = new
                {
                    ui = IsUiSupported,
                    screenshots = IsScreenshotSupported,
                    webview = _cdpWebViews.Any(v => v.IsReady),
                    network = true,
                    logs = true,
                    sensors = IsSensorsSupported,
                    storage = IsStorageSupported,
                    profiler = IsProfilerFeatureAvailable,
                    jobs = IsJobsSupported,
                    theme = IsThemeSupported,
                },
                running = IsAppBound,
                cdpReady = _cdpWebViews.Any(v => v.IsReady),
                cdpWebViewCount = _cdpWebViews.Count,
                profiler = BuildProfilerCapabilitiesPayload(),
                profilerSession = _profilerSessions.CurrentSession,
                extensions = BuildExtensionsMarker()
            };
        });

        return HttpResponse.Json(result!);
    }

    // ── Capability support flags ──────────────────────────────────────────

    /// <summary>Whether the backend can walk and interact with a UI tree.</summary>
    protected virtual bool IsUiSupported => false;

    /// <summary>Whether the backend can capture screenshots.</summary>
    protected virtual bool IsScreenshotSupported => false;

    /// <summary>Whether preferences / secure storage endpoints are backed by an implementation.</summary>
    protected virtual bool IsStorageSupported => false;

    /// <summary>Whether device info endpoints are backed by an implementation.</summary>
    protected virtual bool IsDeviceInfoSupported => false;

    /// <summary>Whether sensor endpoints are backed by an implementation.</summary>
    protected virtual bool IsSensorsSupported => false;

    /// <summary>Whether app theme endpoints are backed by an implementation.</summary>
    protected virtual bool IsThemeSupported => false;

    /// <summary>
    /// Reason surfaced to clients when a capability group is unavailable.
    /// </summary>
    protected virtual string UnsupportedCapabilityReason
        => $"Not supported by the '{UiFrameworkName}' DevFlow backend.";

    private static readonly string[] s_noFeatures = [];

    private static object Capability(int version, bool supported, string[] features, string? reason)
        => supported
            ? new { version, supported = true, features, reason = (string?)null }
            : new { version, supported = false, features = s_noFeatures, reason };

    /// <summary>
    /// Allows a backend to add or replace capability entries.
    /// </summary>
    protected virtual void PopulateCapabilities(Dictionary<string, object> capabilities)
    {
    }

    private Task<HttpResponse> HandleCapabilities(HttpRequest request)
    {
        var reason = UnsupportedCapabilityReason;
        var capabilities = new Dictionary<string, object>();

        capabilities["ui.tree"] = Capability(1, IsUiSupported,
            ["css-selector", "type", "text", "accessibility-id"], reason);
        capabilities["ui.actions"] = Capability(1, IsUiSupported,
            ["tap", "fill", "clear", "focus", "scroll", "navigate", "resize", "back", "key", "gesture", "batch", "properties"], reason);
        capabilities["ui.screenshot"] = Capability(1, IsScreenshotSupported,
            ["element", "fullscreen", "selector"], reason);

        if (_cdpWebViews.Count > 0)
            capabilities["webview"] = Capability(1, true,
                ["evaluate", "contexts", "source", "dom", "dom-query", "network", "console", "screenshot"], null);

        capabilities["profiler"] = Capability(1, IsProfilerFeatureAvailable, BuildProfilerFeatureList(), "Profiler collector is unavailable on this platform.");
        capabilities["network"] = Capability(1, true, ["list", "detail", "clear", "stream"], null);
        capabilities["logs"] = Capability(1, true, ["list", "stream"], null);
        capabilities["device.info"] = Capability(1, IsDeviceInfoSupported,
            ["app", "device", "display", "battery", "connectivity"], reason);
        capabilities["device.sensors"] = Capability(1, IsSensorsSupported,
            ["list", "start", "stop", "stream"], reason);
        capabilities["device.jobs"] = Capability(1, IsJobsSupported,
            IsJobRunSupported ? ["list", "run"] : ["list"],
            $"Background jobs are not supported on {PlatformName}.");
        capabilities["storage.preferences"] = Capability(1, IsStorageSupported,
            ["list", "get", "set", "delete", "clear"], reason);
        capabilities["storage.secure"] = Capability(1, IsStorageSupported,
            ["get", "set", "delete", "clear"], reason);
        capabilities["storage.files"] = Capability(1, true,
            ["roots", "list", "download", "upload", "delete"], null);
        capabilities["invoke"] = Capability(1, true, ["actions"], null);
        var themeCapability = Capability(1, IsThemeSupported, ["get", "set"], reason);
        capabilities["theme"] = themeCapability;
        capabilities["app.theme"] = themeCapability;

        PopulateCapabilities(capabilities);

        var result = new Dictionary<string, object?>
        {
            ["agent"] = new
            {
                name = "Microsoft.Maui.DevFlow.Agent",
                version = AgentVersion,
                framework = FrameworkName,
                uiFramework = UiFrameworkName,
                frameworkVersion = FrameworkVersion
            },
            ["capabilities"] = capabilities
        };

        if (_options.Extensions.Count > 0)
        {
            var extensions = BuildExtensionMetadata();
            foreach (var extension in _options.Extensions)
            {
                capabilities[extension.Namespace] = new
                {
                    version = GetCapabilityVersion(extension.Version),
                    supported = true,
                    features = extension.Features.Count > 0
                        ? extension.Features
                        : extension.Tools.Select(tool => tool.Name).ToArray()
                };
            }

            result["extensions"] = extensions;
        }

        return Task.FromResult(HttpResponse.Json(result));
    }

    // ── Not-supported envelope ────────────────────────────────────────────

    /// <summary>
    /// Builds the uniform <c>501</c> payload returned when a capability is unavailable
    /// on the active backend.
    /// </summary>
    protected HttpResponse NotSupported(string capability, string? reason = null)
        => HttpResponse.Json(new
        {
            error = "not_supported",
            capability,
            reason = reason ?? UnsupportedCapabilityReason
        }, 501);

    private Task<HttpResponse> NotSupportedTask(string capability, string? reason = null)
        => Task.FromResult(NotSupported(capability, reason));

    // ── UI seams (overridden by UI backends) ──────────────────────────────

    /// <summary>Handles <c>GET /api/v1/ui/tree</c>.</summary>
    protected virtual Task<HttpResponse> HandleTree(HttpRequest request) => NotSupportedTask("ui.tree");

    /// <summary>Handles <c>GET /api/v1/ui/element/{id}</c>.</summary>
    protected virtual Task<HttpResponse> HandleElement(HttpRequest request) => NotSupportedTask("ui.tree");

    /// <summary>Handles <c>GET /api/v1/ui/query</c>.</summary>
    protected virtual Task<HttpResponse> HandleQuery(HttpRequest request) => NotSupportedTask("ui.tree");

    /// <summary>Handles <c>GET /api/v1/ui/hittest</c>.</summary>
    protected virtual Task<HttpResponse> HandleHitTest(HttpRequest request) => NotSupportedTask("ui.tree");

    /// <summary>Handles <c>GET /api/v1/ui/screenshot</c>.</summary>
    protected virtual Task<HttpResponse> HandleScreenshot(HttpRequest request) => NotSupportedTask("ui.screenshot");

    /// <summary>Handles <c>GET /api/v1/ui/element/{id}/property</c>.</summary>
    protected virtual Task<HttpResponse> HandleProperty(HttpRequest request) => NotSupportedTask("ui.actions");

    /// <summary>Handles <c>PUT /api/v1/ui/element/{id}/property</c>.</summary>
    protected virtual Task<HttpResponse> HandleSetProperty(HttpRequest request) => NotSupportedTask("ui.actions");

    /// <summary>Handles <c>POST /api/v1/ui/tap</c>.</summary>
    protected virtual Task<HttpResponse> HandleTap(HttpRequest request) => NotSupportedTask("ui.actions");

    /// <summary>Handles <c>POST /api/v1/ui/fill</c>.</summary>
    protected virtual Task<HttpResponse> HandleFill(HttpRequest request) => NotSupportedTask("ui.actions");

    /// <summary>Handles <c>POST /api/v1/ui/clear</c>.</summary>
    protected virtual Task<HttpResponse> HandleClear(HttpRequest request) => NotSupportedTask("ui.actions");

    /// <summary>Handles <c>POST /api/v1/ui/focus</c>.</summary>
    protected virtual Task<HttpResponse> HandleFocus(HttpRequest request) => NotSupportedTask("ui.actions");

    /// <summary>Handles <c>POST /api/v1/ui/navigate</c>.</summary>
    protected virtual Task<HttpResponse> HandleNavigate(HttpRequest request) => NotSupportedTask("ui.actions");

    /// <summary>Handles <c>POST /api/v1/ui/resize</c>.</summary>
    protected virtual Task<HttpResponse> HandleResize(HttpRequest request) => NotSupportedTask("ui.actions");

    /// <summary>Handles <c>POST /api/v1/ui/back</c>.</summary>
    protected virtual Task<HttpResponse> HandleBack(HttpRequest request) => NotSupportedTask("ui.actions");

    /// <summary>Handles <c>POST /api/v1/ui/key</c>.</summary>
    protected virtual Task<HttpResponse> HandleKey(HttpRequest request) => NotSupportedTask("ui.actions");

    /// <summary>Handles <c>POST /api/v1/ui/gesture</c>.</summary>
    protected virtual Task<HttpResponse> HandleGesture(HttpRequest request) => NotSupportedTask("ui.actions");

    /// <summary>Handles <c>POST /api/v1/ui/batch</c>.</summary>
    protected virtual async Task<HttpResponse> HandleBatch(HttpRequest request)
    {
        if (!IsUiSupported) return NotSupported("ui.actions");
        if (!IsAppBound) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<BatchRequest>();
        if (body?.Actions == null || body.Actions.Count == 0)
            return HttpResponse.Error("actions are required");

        var results = new List<object>(body.Actions.Count);
        var allSucceeded = true;

        foreach (var action in body.Actions)
        {
            var actionName = (action.Action ?? action.Type ?? string.Empty).Trim().ToLowerInvariant();
            HttpResponse response;

            switch (actionName)
            {
                case "tap":
                    response = await HandleTap(new HttpRequest { Method = "POST", Body = JsonSerializer.Serialize(new ActionRequest { ElementId = action.ElementId }) });
                    break;
                case "fill":
                    response = await HandleFill(new HttpRequest
                    {
                        Method = "POST",
                        Body = JsonSerializer.Serialize(new FillRequest { ElementId = action.ElementId, Text = action.Text ?? string.Empty })
                    });
                    break;
                case "clear":
                    response = await HandleClear(new HttpRequest { Method = "POST", Body = JsonSerializer.Serialize(new ActionRequest { ElementId = action.ElementId }) });
                    break;
                case "focus":
                    response = await HandleFocus(new HttpRequest { Method = "POST", Body = JsonSerializer.Serialize(new ActionRequest { ElementId = action.ElementId }) });
                    break;
                case "navigate":
                    response = await HandleNavigate(new HttpRequest
                    {
                        Method = "POST",
                        Body = JsonSerializer.Serialize(new NavigateRequest { Route = action.Route ?? string.Empty })
                    });
                    break;
                case "resize":
                    response = await HandleResize(new HttpRequest { Method = "POST", Body = JsonSerializer.Serialize(new ResizeRequest(action.Width, action.Height)) });
                    break;
                case "scroll":
                    response = await HandleScroll(new HttpRequest
                    {
                        Method = "POST",
                        Body = JsonSerializer.Serialize(new ScrollRequest
                        {
                            ElementId = action.ElementId,
                            DeltaX = action.DeltaX,
                            DeltaY = action.DeltaY,
                            ItemIndex = action.ItemIndex,
                            GroupIndex = action.GroupIndex,
                            ScrollToPosition = action.ScrollToPosition,
                            Animated = action.Animated
                        })
                    });
                    break;
                case "back":
                    response = await HandleBack(new HttpRequest { Method = "POST" });
                    break;
                case "key":
                    response = await HandleKey(new HttpRequest
                    {
                        Method = "POST",
                        Body = JsonSerializer.Serialize(new KeyActionRequest { ElementId = action.ElementId, Key = action.Key, Text = action.Text })
                    });
                    break;
                case "gesture":
                    response = await HandleGesture(new HttpRequest
                    {
                        Method = "POST",
                        Body = JsonSerializer.Serialize(new GestureActionRequest
                        {
                            ElementId = action.ElementId,
                            Type = action.Type ?? action.Action,
                            Direction = action.Direction,
                            Distance = action.Distance,
                            DurationMs = action.DurationMs
                        })
                    });
                    break;
                case "set-property":
                case "set_property":
                    response = await HandleSetProperty(new HttpRequest
                    {
                        Method = "PUT",
                        RouteParams = new Dictionary<string, string>
                        {
                            ["id"] = action.ElementId ?? string.Empty,
                            ["name"] = action.Property ?? string.Empty
                        },
                        Body = JsonSerializer.Serialize(new SetPropertyRequest { Value = action.Value ?? string.Empty })
                    });
                    break;
                case "invoke-action":
                case "invoke_action":
                    response = await HandleInvokeAction(new HttpRequest
                    {
                        Method = "POST",
                        RouteParams = new Dictionary<string, string>
                        {
                            ["name"] = action.Name ?? string.Empty
                        },
                        Body = JsonSerializer.Serialize(new InvokeActionRequest { Args = action.Args })
                    });
                    break;
                default:
                    response = HttpResponse.Error($"Unsupported batch action '{actionName}'");
                    break;
            }

            var succeeded = response.StatusCode < 400;
            allSucceeded &= succeeded;
            results.Add(new
            {
                action = actionName,
                success = succeeded,
                statusCode = response.StatusCode,
                response = response.Body
            });

            if (!succeeded && !body.ContinueOnError)
                break;
        }

        return HttpResponse.Json(new
        {
            success = allSucceeded,
            results
        });
    }

    /// <summary>Handles <c>POST /api/v1/ui/scroll</c>.</summary>
    protected virtual Task<HttpResponse> HandleScroll(HttpRequest request) => NotSupportedTask("ui.actions");

    /// <summary>Installs automatic UI correlation hooks used by the profiler.</summary>
    protected virtual void EnsureAutoUiHooks()
    {
    }

    /// <summary>Removes automatic UI correlation hooks installed by <see cref="EnsureAutoUiHooks"/>.</summary>
    protected virtual void StopAutoUiHooks()
    {
    }

    /// <summary>
    /// Attempts to capture a registered WebView by rendering the hosting UI element.
    /// </summary>
    protected virtual Task<HttpResponse?> TryCaptureRegisteredWebViewAsync(CdpWebViewInfo webView)
        => Task.FromResult<HttpResponse?>(null);

    // ── Storage seams ─────────────────────────────────────────────────────

    /// <summary>Handles <c>GET /api/v1/storage/preferences</c>.</summary>
    protected virtual Task<HttpResponse> HandlePreferencesList(HttpRequest request) => NotSupportedTask("storage.preferences");

    /// <summary>Handles <c>GET /api/v1/storage/preferences/{key}</c>.</summary>
    protected virtual Task<HttpResponse> HandlePreferencesGet(HttpRequest request) => NotSupportedTask("storage.preferences");

    /// <summary>Handles <c>PUT /api/v1/storage/preferences/{key}</c>.</summary>
    protected virtual Task<HttpResponse> HandlePreferencesSet(HttpRequest request) => NotSupportedTask("storage.preferences");

    /// <summary>Handles <c>DELETE /api/v1/storage/preferences/{key}</c>.</summary>
    protected virtual Task<HttpResponse> HandlePreferencesDelete(HttpRequest request) => NotSupportedTask("storage.preferences");

    /// <summary>Handles <c>DELETE /api/v1/storage/preferences</c>.</summary>
    protected virtual Task<HttpResponse> HandlePreferencesClear(HttpRequest request) => NotSupportedTask("storage.preferences");

    /// <summary>Handles <c>GET /api/v1/storage/secure/{key}</c>.</summary>
    protected virtual Task<HttpResponse> HandleSecureStorageGet(HttpRequest request) => NotSupportedTask("storage.secure");

    /// <summary>Handles <c>PUT /api/v1/storage/secure/{key}</c>.</summary>
    protected virtual Task<HttpResponse> HandleSecureStorageSet(HttpRequest request) => NotSupportedTask("storage.secure");

    /// <summary>Handles <c>DELETE /api/v1/storage/secure/{key}</c>.</summary>
    protected virtual Task<HttpResponse> HandleSecureStorageDelete(HttpRequest request) => NotSupportedTask("storage.secure");

    /// <summary>Handles <c>DELETE /api/v1/storage/secure</c>.</summary>
    protected virtual Task<HttpResponse> HandleSecureStorageClear(HttpRequest request) => NotSupportedTask("storage.secure");

    // ── Device / theme seams ──────────────────────────────────────────────

    /// <summary>Handles <c>GET /api/v1/device/app</c>.</summary>
    protected virtual Task<HttpResponse> HandlePlatformAppInfo(HttpRequest request) => NotSupportedTask("device.info");

    /// <summary>Handles <c>GET /api/v1/device/app/theme</c>.</summary>
    protected virtual Task<HttpResponse> HandleThemeGet(HttpRequest request) => NotSupportedTask("app.theme");

    /// <summary>Handles <c>PUT /api/v1/device/app/theme</c>.</summary>
    protected virtual Task<HttpResponse> HandleThemeSet(HttpRequest request) => NotSupportedTask("app.theme");

    /// <summary>Handles <c>GET /api/v1/device/info</c>.</summary>
    protected virtual Task<HttpResponse> HandlePlatformDeviceInfo(HttpRequest request) => NotSupportedTask("device.info");

    /// <summary>Handles <c>GET /api/v1/device/display</c>.</summary>
    protected virtual Task<HttpResponse> HandlePlatformDeviceDisplay(HttpRequest request) => NotSupportedTask("device.info");

    /// <summary>Handles <c>GET /api/v1/device/battery</c>.</summary>
    protected virtual Task<HttpResponse> HandlePlatformBattery(HttpRequest request) => NotSupportedTask("device.info");

    /// <summary>Handles <c>GET /api/v1/device/connectivity</c>.</summary>
    protected virtual Task<HttpResponse> HandlePlatformConnectivity(HttpRequest request) => NotSupportedTask("device.info");

    /// <summary>Handles <c>GET /api/v1/device/app/version</c>.</summary>
    protected virtual Task<HttpResponse> HandlePlatformVersionTracking(HttpRequest request) => NotSupportedTask("device.info");

    /// <summary>Handles <c>GET /api/v1/device/permissions</c>.</summary>
    protected virtual Task<HttpResponse> HandlePlatformPermissions(HttpRequest request) => NotSupportedTask("device.info");

    /// <summary>Handles <c>GET /api/v1/device/permissions/{permission}</c>.</summary>
    protected virtual Task<HttpResponse> HandlePlatformPermissionCheck(HttpRequest request) => NotSupportedTask("device.info");

    /// <summary>Handles <c>GET /api/v1/device/geolocation</c>.</summary>
    protected virtual Task<HttpResponse> HandlePlatformGeolocation(HttpRequest request) => NotSupportedTask("device.info");

    /// <summary>Handles <c>GET /api/v1/device/sensors</c>.</summary>
    protected virtual Task<HttpResponse> HandleSensorsList(HttpRequest request) => NotSupportedTask("device.sensors");

    /// <summary>Handles <c>POST /api/v1/device/sensors/{sensor}/start</c>.</summary>
    protected virtual Task<HttpResponse> HandleSensorStart(HttpRequest request) => NotSupportedTask("device.sensors");

    /// <summary>Handles <c>POST /api/v1/device/sensors/{sensor}/stop</c>.</summary>
    protected virtual Task<HttpResponse> HandleSensorStop(HttpRequest request) => NotSupportedTask("device.sensors");

    /// <summary>Handles the <c>/ws/v1/sensors</c> websocket.</summary>
    protected virtual Task HandleSensorWebSocket(
        System.Net.Sockets.TcpClient client,
        System.Net.Sockets.NetworkStream stream,
        HttpRequest request,
        CancellationToken ct)
        => Task.CompletedTask;

    // ── Dispatch ──────────────────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="func"/> on the UI thread when a dispatcher requires it.
    /// </summary>
    protected async Task<T> DispatchAsync<T>(Func<T> func)
    {
        if (_dispatcher is { IsDispatchRequired: true })
            return await DispatchViaAgentDispatcherAsync(func);

        if (IsMainThreadDispatchRequired())
            return await DispatchViaMainThreadAsync(func);

        return func();
    }

    private async Task<T> DispatchViaAgentDispatcherAsync<T>(Func<T> func)
    {
        var dispatcher = _dispatcher ?? throw new InvalidOperationException("Dispatcher is not available.");
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Dispatch(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return await tcs.Task;
    }

    /// <summary>
    /// Runs <paramref name="func"/> on the UI thread when a dispatcher requires it.
    /// </summary>
    protected async Task<T?> DispatchAsync<T>(Func<Task<T?>> func) where T : class
    {
        if (_dispatcher is { IsDispatchRequired: true })
            return await DispatchViaAgentDispatcherAsync(func);

        if (IsMainThreadDispatchRequired())
            return await DispatchViaMainThreadAsync(func);

        return await func();
    }

    private async Task<T?> DispatchViaAgentDispatcherAsync<T>(Func<Task<T?>> func) where T : class
    {
        var dispatcher = _dispatcher ?? throw new InvalidOperationException("Dispatcher is not available.");
        var tcs = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Dispatch(async () =>
        {
            try { tcs.SetResult(await func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return await tcs.Task;
    }

    /// <summary>
    /// Whether the current call must be marshalled to the platform main thread.
    /// </summary>
    protected virtual bool IsMainThreadDispatchRequired() => false;

    /// <summary>Runs <paramref name="func"/> on the platform main thread.</summary>
    protected virtual Task<T> DispatchViaMainThreadAsync<T>(Func<T> func)
        => Task.FromResult(func());

    /// <summary>Runs <paramref name="func"/> on the platform main thread.</summary>
    protected virtual Task<T?> DispatchViaMainThreadAsync<T>(Func<Task<T?>> func) where T : class
        => func();
}
