using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using Microsoft.Maui.DevFlow.Agent.Core.Profiling;
using Microsoft.Maui.DevFlow.Logging;
using Microsoft.Maui.DevFlow.Agent.Core.Network;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// The .NET MAUI backend for <see cref="DevFlowAgentService"/>.
/// </summary>
/// <remarks>
/// Implements every UI, storage, device and theme seam using MAUI Controls and Essentials.
/// Platform agent packages derive from this type and override the <c>protected virtual</c>
/// native hooks it declares.
/// </remarks>
public partial class MauiDevFlowAgentService : DevFlowAgentService
{
    static MauiDevFlowAgentService()
    {
        FrameworkValueFormatter = FormatMauiPropertyValue;
        Profiling.RuntimeProfilerCollector.DisplayRefreshRateProvider =
            static () => DeviceDisplay.Current.MainDisplayInfo.RefreshRate;
    }

    private readonly RegisteredNativeElementRegistry? _nativeElementRegistry;
    private readonly IDisposable? _nativeElementSubscription;

    /// <summary>
    /// Creates a new MAUI-backed agent service.
    /// </summary>
    public MauiDevFlowAgentService(AgentOptions? options = null)
        : this(options, nativeElementRegistry: null, nativeElementSubscription: null)
    {
    }

    internal MauiDevFlowAgentService(
        AgentOptions? options,
        RegisteredNativeElementRegistry? nativeElementRegistry,
        IDisposable? nativeElementSubscription)
        : base(options)
    {
        _nativeElementRegistry = nativeElementRegistry;
        _nativeElementSubscription = nativeElementSubscription;
        _treeWalker = CreateTreeWalker();
    }

    // ── Framework identity ────────────────────────────────────────────────

    /// <inheritdoc />
    protected override string FrameworkName => "maui";

    /// <inheritdoc />
    protected override string FrameworkDisplayName => ".NET MAUI";

    /// <inheritdoc />
    protected override string UiFrameworkName => "maui-controls";

    /// <inheritdoc />
    protected override string FrameworkVersion =>
        typeof(Microsoft.Maui.Controls.Application).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <inheritdoc />
    protected override bool IsUiSupported => true;

    /// <inheritdoc />
    protected override bool IsScreenshotSupported => true;

    /// <inheritdoc />
    protected override bool IsStorageSupported => true;

    /// <inheritdoc />
    protected override bool IsDeviceInfoSupported => true;

    /// <inheritdoc />
    protected override bool IsSensorsSupported => true;

    /// <inheritdoc />
    protected override bool IsThemeSupported => true;

    /// <inheritdoc />
    protected override void PopulateCapabilities(Dictionary<string, object> capabilities)
    {
        capabilities["ui.tree"] = Capability(2, supported: true,
            ["css-selector", "type", "text", "accessibility-id", "native-owner", "capture-epoch", "registry-generation", "window-id"],
            reason: null);
        capabilities["ui.hit-test"] = Capability(2, supported: true,
            ["native-first", "capture-epoch", "window-logical-coordinates"],
            reason: null);
        capabilities["ui.actions"] = Capability(2, supported: true,
            ["tap", "fill", "clear", "focus", "scroll", "navigate", "resize", "back", "key", "gesture", "batch", "capture-bound-batch", "properties", "stale-capture-rejection"],
            reason: null);
        capabilities["ui.screenshot"] = Capability(2, supported: true,
            SupportsNativeElementScreenshots
                ? ["element", "native-element", "fullscreen", "selector", "capture-epoch"]
                : ["element", "fullscreen", "selector", "capture-epoch"],
            reason: null);
    }

    /// <inheritdoc />
    protected override string AppDisplayName
        => TryGetAppInfoString(() => AppInfo.Current.Name)
            ?? _app?.GetType().Assembly.GetName().Name
            ?? "unknown";

    /// <inheritdoc />
    protected override string AppPackageId
        => TryGetAppInfoString(() => AppInfo.Current.PackageName) ?? "unknown";

    /// <inheritdoc />
    protected override string AppVersionString
        => TryGetAppInfoString(() => AppInfo.Current.VersionString) ?? "unknown";

    /// <inheritdoc />
    protected override string AppBuildString
        => TryGetAppInfoString(() => AppInfo.Current.BuildString) ?? "unknown";

    /// <inheritdoc />
    protected override int WindowCount => _app?.Windows.Count ?? 0;

    /// <inheritdoc />
    protected override (double Width, double Height, double Density) GetWindowMetrics(int? windowIndex)
    {
        var window = GetWindow(windowIndex);
        var w = window?.Width ?? 0;
        var h = window?.Height ?? 0;

        // Try getting window size from native platform view if MAUI reports invalid values
        if (window != null && (!double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0))
        {
            var (nw, nh) = GetNativeWindowSize(window);
            if (nw > 0) w = nw;
            if (nh > 0) h = nh;
        }

        return (w, h, GetWindowDisplayDensity(window));
    }

    /// <inheritdoc />
    protected override string? GetCurrentRouteLocation()
    {
        try { return Shell.Current?.CurrentState?.Location?.ToString(); }
        catch { return null; }
    }

    /// <inheritdoc />
    protected override void DisposeBackendResources()
    {
        StopCaptureInvalidationHooks();
        _nativeElementSubscription?.Dispose();
        _nativeElementRegistry?.Clear();
        Sensors.Dispose();
    }

    protected override Task StopBackendAsync()
    {
        StopCaptureInvalidationHooks();
        return Task.CompletedTask;
    }

    protected override void OnUiOperationSucceeded() => InvalidateUiCapture();

    // ── Lifecycle ─────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the agent and binds to the running MAUI app.
    /// </summary>
    public void Start(Application app, IDispatcher dispatcher)
    {
        if (_disposed || !_options.Enabled) return;
        _app = app;
        StartServerOnly(new MauiAgentDispatcher(dispatcher));
    }

    /// <summary>
    /// Starts the HTTP server without an Application binding.
    /// Use when Application.Current is unavailable (e.g., Comet apps).
    /// Endpoints requiring the app will return errors until BindApp() is called.
    /// </summary>
    public void StartServerOnly(IDispatcher dispatcher)
        => StartServerOnly(new MauiAgentDispatcher(dispatcher));

    /// <summary>
    /// Late-binds the Application instance after the server is already running.
    /// </summary>
    public void BindApp(Application app)
    {
        if (_disposed || !_options.Enabled) return;
        _app = app;
        try
        {
            if (app.Dispatcher is { } dispatcher)
                _dispatcher = new MauiAgentDispatcher(dispatcher);
        }
        catch (InvalidOperationException)
        {
            // Keep the dispatcher captured during server-only startup if the app
            // has not been associated with one yet.
        }
        Console.WriteLine("[Microsoft.Maui.DevFlow.Agent] Application bound to running agent");
        PublishUiEvent("lifecycle", new
        {
            state = "started",
            timestamp = DateTimeOffset.UtcNow.ToString("O")
        });
    }

    /// <inheritdoc />
    protected override bool IsMainThreadDispatchRequired()
    {
        try { return !MainThread.IsMainThread; }
        catch { return false; }
    }

    /// <inheritdoc />
    protected override Task<T> DispatchViaMainThreadAsync<T>(Func<T> func)
        => MainThread.InvokeOnMainThreadAsync(func);

    /// <inheritdoc />
    protected override Task<T?> DispatchViaMainThreadAsync<T>(Func<Task<T?>> func) where T : class
        => MainThread.InvokeOnMainThreadAsync(func);

    /// <summary>
    /// Adapts a MAUI <see cref="IDispatcher"/> to the framework-neutral <see cref="IAgentDispatcher"/>.
    /// </summary>
    protected static IAgentDispatcher ToAgentDispatcher(IDispatcher dispatcher) => new MauiAgentDispatcher(dispatcher);

    private sealed class MauiAgentDispatcher(IDispatcher dispatcher) : IAgentDispatcher
    {
        public bool IsDispatchRequired => dispatcher.IsDispatchRequired;

        public bool Dispatch(Action action) => dispatcher.Dispatch(action);

        public bool DispatchDelayed(TimeSpan delay, Action action) => dispatcher.DispatchDelayed(delay, action);
    }

    private readonly VisualTreeWalker _treeWalker;

    protected Application? _app;

    /// <summary>
    /// Manages sensor subscriptions and broadcasts readings to WebSocket clients.
    /// </summary>
    private readonly EssentialsAgentSupport _essentials = new();

    public SensorManager Sensors => _essentials.Sensors;

    private const int UiHookScanIntervalMs = 3000;

    private readonly ConditionalWeakTable<BindableObject, UiHookState> _uiHookStates = new();

    private readonly List<Action> _uiHookUnsubscribers = new();

    private int _uiHookGeneration = 1;

    private int _uiHookScanInFlight;

    private DateTime _lastUiHookScanTsUtc = DateTime.MinValue;

    private const int NativeUiProbeTimeoutMs = 1500;

    // Tracks a previously-dispatched UI capture task that timed out. If still
    // pending when a new CaptureUiOrNativeAsync arrives we skip enqueuing another
    // uiCallback to avoid unbounded queueing on a blocked dispatcher.
    private Task? _pendingCaptureUiTask;

    private readonly object _pendingCaptureUiGate = new();
    private Task<List<ElementInfo>>? _pendingNativeProbeTask;
    private readonly object _pendingNativeProbeGate = new();
    private readonly object _captureStateGate = new();
    private readonly object _captureInvalidationHookGate = new();
    private readonly object _knownNativeWindowHandlesGate = new();
    private readonly SortedDictionary<long, UiCaptureContext> _captureLeases = new();
    private readonly Dictionary<int, IReadOnlyList<IntPtr>> _knownNativeWindowHandles = [];
    private readonly ConditionalWeakTable<BindableObject, CaptureInvalidationHookState>
        _captureInvalidationHookStates = new();
    private readonly List<WeakReference<BindableObject>> _captureInvalidationTargets = [];
    private const int MaxCaptureLeases = 128;
    private const int MaxCapturedElementIdentities = 100_000;
    private readonly SemaphoreSlim _uiMutationGate = new(1, 1);
    private long _captureEpochSequence;
    private long _uiMutationGeneration;
    private long _aggregateExternalMutationGeneration;
    private readonly Dictionary<int, long> _windowExternalMutationGenerations = [];
    private int _uiMutationInProgress;
    private UiCaptureContext _latestCapture;
    private Shell? _hookedShell;

    private DateTime? _navigationStartedAtUtc;

    private string? _navigationTargetRoute;

    private readonly ConditionalWeakTable<Page, PageLifecycleState> _pageLifecycleStates = new();

    private readonly ConditionalWeakTable<VisualElement, ElementRenderState> _elementRenderStates = new();

    private readonly ConditionalWeakTable<BindableObject, ScrollBatchState> _scrollBatchStates = new();

    private sealed class UiHookState
    {
        public int Generation { get; set; }
        public HashSet<string> HookKeys { get; } = new(StringComparer.Ordinal);
    }

    private sealed class CaptureInvalidationHookState
    {
        public int? WindowId { get; set; }
    }

    private readonly record struct UiCaptureContext(
        long Epoch,
        long RegistryGeneration,
        long MutationGeneration,
        long ExternalMutationGeneration,
        int? WindowId,
        Dictionary<string, CapturedElementIdentity>? ElementIdentities);

    private sealed class CapturedElementIdentity
    {
        private readonly object? _strongIdentity;
        private readonly WeakReference<object>? _weakIdentity;

        public CapturedElementIdentity(object identity, bool retainStrongly)
        {
            if (retainStrongly)
                _strongIdentity = identity;
            else
                _weakIdentity = new WeakReference<object>(identity);
        }

        public bool TryGetTarget([NotNullWhen(true)] out object? identity)
        {
            if (_strongIdentity is not null)
            {
                identity = _strongIdentity;
                return true;
            }

            if (_weakIdentity?.TryGetTarget(out identity) == true)
                return true;

            identity = null;
            return false;
        }
    }

    private sealed class CaptureMetadataRequest : CaptureBoundRequest
    {
    }

    private sealed class PageLifecycleState
    {
        public DateTime AppearingAtUtc { get; set; }
        public string? Route { get; set; }
        public bool FirstLayoutPublished { get; set; }
        public int SizeChangedCount { get; set; }
        public int MeasureInvalidatedCount { get; set; }
    }

    private sealed class ElementRenderState
    {
        public DateTime TrackingStartedAtUtc { get; set; }
        public string? Role { get; set; }
        public bool FirstLayoutPublished { get; set; }
        public int SizeChangedCount { get; set; }
        public int MeasureInvalidatedCount { get; set; }
    }

    private sealed class ScrollBatchState
    {
        public bool IsActive { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime LastEventAtUtc { get; set; }
        public int EventCount { get; set; }
        public int FlushVersion { get; set; }
        public double StartOffsetX { get; set; }
        public double StartOffsetY { get; set; }
        public double LastOffsetX { get; set; }
        public double LastOffsetY { get; set; }
        public int? StartFirstVisibleIndex { get; set; }
        public int? StartLastVisibleIndex { get; set; }
        public int? LastFirstVisibleIndex { get; set; }
        public int? LastLastVisibleIndex { get; set; }
    }

    public override bool IsAppBound => _app != null;

    /// <summary>
    /// Gets the window at the given index, or the first window when index is null.
    /// </summary>
    private Window? GetWindow(int? index)
    {
        if (_app == null) return null;
        if (index == null) return _app.Windows.FirstOrDefault() as Window;
        if (index.Value < 0 || index.Value >= _app.Windows.Count) return null;
        return _app.Windows[index.Value] as Window;
    }

    /// <summary>
    /// Creates the visual tree walker. Override in platform-specific subclasses
    /// to return a walker with native info population.
    /// </summary>
    protected virtual VisualTreeWalker CreateTreeWalker() => new VisualTreeWalker();

    internal RegisteredNativeElementRegistry? NativeElementRegistry => _nativeElementRegistry;
    /// <summary>Platform name for status reporting. Override for platforms without DeviceInfo.</summary>
    protected override string PlatformName => DeviceInfo.Current.Platform.ToString();

    /// <summary>Device type for status reporting. Override for platforms without DeviceInfo.</summary>
    protected override string DeviceTypeName => DeviceInfo.Current.DeviceType.ToString();

    /// <summary>Device idiom for status reporting. Override for platforms without DeviceInfo.</summary>
    protected override string IdiomName => DeviceInfo.Current.Idiom.ToString();

    /// <summary>
    /// Gets the display density (scale factor) for a specific window. Returns 1.0 for standard,
    /// 2.0 for @2x (Retina), 3.0 for @3x (iPhone Pro Max), etc.
    /// Used to auto-scale screenshots to 1x logical resolution.
    /// Override in platform-specific agents to query the native window's actual screen density,
    /// which may vary across displays in multi-monitor setups.
    /// </summary>
    protected virtual double GetWindowDisplayDensity(IWindow? window)
    {
        try { return DeviceDisplay.MainDisplayInfo.Density; }
        catch { return 1.0; }
    }

    /// <summary>Gets native window dimensions when MAUI reports 0. Override for platform-specific access.</summary>
    protected virtual (double width, double height) GetNativeWindowSize(IWindow window) => (0, 0);


    protected override async Task<HttpResponse> HandleTree(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        int maxDepth = 0;
        if (request.QueryParams.TryGetValue("depth", out var depthStr))
            int.TryParse(depthStr, out maxDepth);

        var windowIndex = ParseWindowIndex(request);
        var capture = BeginUiCapture(windowIndex);
        var tree = await CaptureUiOrNativeAsync(
            () => _treeWalker.WalkTree(_app, maxDepth, windowIndex),
            hwnds => _treeWalker.WalkNativeTree(hwnds, maxDepth),
            windowIndex);
        StampCaptureMetadata(tree, capture);
        if (!CommitUiCapture(capture))
            return BuildCaptureChangedResponse(capture);
        return HttpResponse.Json(tree);
    }

    protected override async Task<HttpResponse> HandleElement(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");
        if (!request.RouteParams.TryGetValue("id", out var id))
            return HttpResponse.Error("Element ID required");

        var capture = BeginUiCapture(windowIndex: null);
        await DispatchAsync(() =>
        {
            RefreshCaptureInvalidationHooks(windowIndex: null);
            return true;
        });
        if (IsRegisteredNativeElementId(id))
        {
            var registeredElement = await DispatchAsync(() =>
            {
                var element = _treeWalker.GetNativeElementInfoById(id);
                if (element != null)
                    element.WindowId ??= _treeWalker.GetRegisteredNativeWindowId(id, _app);

                return element;
            });
            if (registeredElement != null)
            {
                StampCaptureMetadata(registeredElement, capture);
                if (!CommitUiCapture(capture))
                    return BuildCaptureChangedResponse(capture);
            }
            return registeredElement != null
                ? HttpResponse.Json(registeredElement)
                : HttpResponse.NotFound($"Element '{id}' not found");
        }

        if (IsNativeElementId(id) && _treeWalker.SupportsNativeElements)
        {
            var nativeElement = await Task.Run(() => _treeWalker.GetNativeElementInfoById(id));
            if (nativeElement != null)
            {
                StampCaptureMetadata(nativeElement, capture);
                if (!CommitUiCapture(capture))
                    return BuildCaptureChangedResponse(capture);
            }
            return nativeElement != null ? HttpResponse.Json(nativeElement) : HttpResponse.NotFound($"Element '{id}' not found");
        }

        var element = await DispatchAsync(() =>
        {
            var el = _treeWalker.GetElementById(id, _app);
            if (el is IVisualTreeElement vte)
                return (object?)_treeWalker.WalkElement(vte, null, 1, 2);

            // Synthetic elements: build detail from marker
            if (el != null)
                return (object?)_treeWalker.BuildSyntheticElementInfo(id, el);

            return null;
        });

        if (element is ElementInfo elementInfo)
        {
            StampCaptureMetadata(elementInfo, capture);
            if (!CommitUiCapture(capture))
                return BuildCaptureChangedResponse(capture);
        }
        return element != null ? HttpResponse.Json(element) : HttpResponse.NotFound($"Element '{id}' not found");
    }

    protected override async Task<HttpResponse> HandleQuery(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        // CSS selector takes precedence over simple filters
        if (request.QueryParams.TryGetValue("selector", out var selector) && !string.IsNullOrWhiteSpace(selector))
        {
            try
            {
                var capture = BeginUiCapture(windowIndex: null);
                var results = await CaptureUiOrNativeAsync(
                    () => _treeWalker.QueryCss(_app, selector),
                    hwnds => _treeWalker.QueryNative(hwnds, selector: selector));
                StampCaptureMetadata(results, capture);
                if (!CommitUiCapture(capture))
                    return BuildCaptureChangedResponse(capture);
                return HttpResponse.Json(results);
            }
            catch (FormatException ex)
            {
                return HttpResponse.Error($"Invalid CSS selector: {ex.Message}");
            }
        }

        request.QueryParams.TryGetValue("type", out var type);
        request.QueryParams.TryGetValue("automationId", out var automationId);
        request.QueryParams.TryGetValue("text", out var text);

        if (type == null && automationId == null && text == null)
            return HttpResponse.Error("At least one query parameter required: type, automationId, text, or selector");

        var simpleCapture = BeginUiCapture(windowIndex: null);
        var simpleResults = await CaptureUiOrNativeAsync(
            () => _treeWalker.Query(_app, type, automationId, text),
            hwnds => _treeWalker.QueryNative(hwnds, type, automationId, text));
        StampCaptureMetadata(simpleResults, simpleCapture);
        if (!CommitUiCapture(simpleCapture))
            return BuildCaptureChangedResponse(simpleCapture);
        return HttpResponse.Json(simpleResults);
    }

    private async Task<List<ElementInfo>> CaptureUiOrNativeAsync(
        Func<List<ElementInfo>> uiCallback,
        Func<IReadOnlyList<IntPtr>, List<ElementInfo>> nativeCallback,
        int? windowIndex = null)
    {
        if (!_treeWalker.SupportsNativeElements)
        {
            return await DispatchAsync(() =>
            {
                var result = uiCallback();
                RefreshCaptureInvalidationHooks(windowIndex);
                return result;
            });
        }

        // Gate: if a previous CaptureUiOrNativeAsync's UI dispatch is still
        // pending (the dispatcher is blocked), skip enqueuing another one and
        // go native-only. Otherwise repeated tree/query calls while the UI
        // thread is blocked would accumulate unbounded queued work.
        Task? priorPending;
        lock (_pendingCaptureUiGate)
            priorPending = _pendingCaptureUiTask;

        if (priorPending is not null && !priorPending.IsCompleted)
        {
            try
            {
                var hwnds = GetCachedKnownNativeWindowHandles(windowIndex);
                var gatedNativeTask = TryStartNativeProbe(async () =>
                {
                    await Task.CompletedTask;
                    try { return nativeCallback(hwnds); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] Native UI probe failed (gated): {ex.GetBaseException().Message}");
                        return new List<ElementInfo>();
                    }
                });
                return await AwaitNativeProbeAsync(gatedNativeTask).ConfigureAwait(false);
            }
            catch
            {
                return new List<ElementInfo>();
            }
        }

        var hwndSource = new TaskCompletionSource<IReadOnlyList<IntPtr>>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Shared CTS so the surviving Task.Delay timers can be cancelled once a race
        // is decided. Without this, every CaptureUiOrNativeAsync call leaves up to
        // two timers running for the full NativeUiProbeTimeoutMs window, which under
        // automation throughput accumulates uncancelled timers per second.
        using var probeCts = new CancellationTokenSource();
        var uiTask = DispatchAsync(() =>
        {
            try
            {
                var hwnds = _app is null
                    ? Array.Empty<IntPtr>()
                    : _treeWalker.GetKnownNativeWindowHandles(_app, windowIndex);
                CacheKnownNativeWindowHandles(windowIndex, hwnds);
                hwndSource.TrySetResult(hwnds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] Native HWND discovery failed: {ex.GetBaseException().Message}");
                hwndSource.TrySetResult(Array.Empty<IntPtr>());
            }

            var result = uiCallback();
            RefreshCaptureInvalidationHooks(windowIndex);
            return result;
        });

        var nativeTask = TryStartNativeProbe(async () =>
        {
            Task delayTask;
            try
            {
                delayTask = Task.Delay(NativeUiProbeTimeoutMs, probeCts.Token);
            }
            catch (OperationCanceledException)
            {
                delayTask = Task.CompletedTask;
            }

            var winner = await Task.WhenAny(hwndSource.Task, delayTask).ConfigureAwait(false);
            var hwnds = winner == hwndSource.Task
                ? await hwndSource.Task.ConfigureAwait(false)
                : Array.Empty<IntPtr>();

            try
            {
                return nativeCallback(hwnds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] Native UI probe failed: {ex.GetBaseException().Message}");
                return [];
            }
        });

        Task uiDelay;
        try
        {
            uiDelay = Task.Delay(NativeUiProbeTimeoutMs, probeCts.Token);
        }
        catch (OperationCanceledException)
        {
            uiDelay = Task.CompletedTask;
        }

        var uiWinner = await Task.WhenAny(uiTask, uiDelay).ConfigureAwait(false);
        if (uiWinner != uiTask)
        {
            // Record this pending uiTask so concurrent callers can detect the
            // dispatcher is blocked and avoid enqueuing additional UI work.
            lock (_pendingCaptureUiGate)
                _pendingCaptureUiTask = uiTask;

            hwndSource.TrySetResult(Array.Empty<IntPtr>());
            // The UI dispatcher is blocked (the exact scenario this code path targets).
            // Observe any later fault on the abandoned uiTask so it doesn't trigger
            // TaskScheduler.UnobservedTaskException when it eventually completes.
            _ = uiTask.ContinueWith(
                t =>
                {
                    lock (_pendingCaptureUiGate)
                    {
                        if (ReferenceEquals(_pendingCaptureUiTask, t))
                            _pendingCaptureUiTask = null;
                    }

                    if (t.IsFaulted)
                        System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] Abandoned uiTask faulted: {t.Exception?.GetBaseException().Message}");
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            probeCts.Cancel();
            return await AwaitNativeProbeAsync(nativeTask).ConfigureAwait(false);
        }

        probeCts.Cancel();

        var uiResult = await uiTask.ConfigureAwait(false);
        var nativeResult = await AwaitNativeProbeAsync(nativeTask).ConfigureAwait(false);
        if (nativeResult.Count == 0)
            return uiResult;

        var merged = new List<ElementInfo>(uiResult.Count + nativeResult.Count);
        merged.AddRange(uiResult);
        merged.AddRange(nativeResult);
        return merged;
    }

    private void CacheKnownNativeWindowHandles(
        int? windowIndex,
        IReadOnlyList<IntPtr> handles)
    {
        lock (_knownNativeWindowHandlesGate)
            _knownNativeWindowHandles[windowIndex ?? -1] = handles.ToArray();
    }

    private IReadOnlyList<IntPtr> GetCachedKnownNativeWindowHandles(int? windowIndex)
    {
        lock (_knownNativeWindowHandlesGate)
            return _knownNativeWindowHandles.TryGetValue(windowIndex ?? -1, out var handles)
                ? handles
                : Array.Empty<IntPtr>();
    }

    private Task<List<ElementInfo>>? TryStartNativeProbe(
        Func<Task<List<ElementInfo>>> probe)
    {
        lock (_pendingNativeProbeGate)
        {
            if (_pendingNativeProbeTask is { IsCompleted: false })
                return null;

            var task = Task.Run(probe);
            _pendingNativeProbeTask = task;
            _ = task.ContinueWith(
                completed =>
                {
                    lock (_pendingNativeProbeGate)
                    {
                        if (ReferenceEquals(_pendingNativeProbeTask, completed))
                            _pendingNativeProbeTask = null;
                    }

                    if (completed.IsFaulted)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Microsoft.Maui.DevFlow] Native UI probe failed: {completed.Exception?.GetBaseException().Message}");
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return task;
        }
    }

    private static async Task<List<ElementInfo>> AwaitNativeProbeAsync(
        Task<List<ElementInfo>>? nativeTask)
    {
        if (nativeTask is null)
            return [];

        var winner = await Task.WhenAny(
            nativeTask,
            Task.Delay(NativeUiProbeTimeoutMs, CancellationToken.None)).ConfigureAwait(false);
        return winner == nativeTask
            ? await nativeTask.ConfigureAwait(false)
            : [];
    }

    private void RefreshCaptureInvalidationHooks(int? windowIndex)
    {
        if (_app is not IVisualTreeElement appElement)
            return;

        var targets = new Dictionary<BindableObject, int?>(ReferenceEqualityComparer.Instance);
        if (_app.Windows.Count == 0)
        {
            CollectCaptureInvalidationTargets(appElement, windowId: 0, targets);
        }
        else if (windowIndex.HasValue)
        {
            if (windowIndex.Value < 0 || windowIndex.Value >= _app.Windows.Count)
                return;
            CollectCaptureInvalidationTargets(
                _app.Windows[windowIndex.Value],
                windowIndex.Value,
                targets);
        }
        else
        {
            for (var index = 0; index < _app.Windows.Count; index++)
            {
                CollectCaptureInvalidationTargets(
                    _app.Windows[index],
                    index,
                    targets);
            }
        }

        lock (_captureInvalidationHookGate)
        {
            for (var index = _captureInvalidationTargets.Count - 1; index >= 0; index--)
            {
                if (!_captureInvalidationTargets[index].TryGetTarget(out var target))
                {
                    _captureInvalidationTargets.RemoveAt(index);
                    continue;
                }

                if (!_captureInvalidationHookStates.TryGetValue(target, out var state))
                {
                    _captureInvalidationTargets.RemoveAt(index);
                    continue;
                }

                if (targets.ContainsKey(target)
                    || windowIndex.HasValue && state.WindowId != windowIndex)
                {
                    continue;
                }

                target.PropertyChanged -= OnCaptureInvalidationTargetPropertyChanged;
                if (target is Element element)
                {
                    element.ChildAdded -= OnCaptureInvalidationTargetChildChanged;
                    element.ChildRemoved -= OnCaptureInvalidationTargetChildChanged;
                }
                _captureInvalidationHookStates.Remove(target);
                _captureInvalidationTargets.RemoveAt(index);
            }

            foreach (var entry in targets)
            {
                if (_captureInvalidationHookStates.TryGetValue(entry.Key, out var state))
                {
                    state.WindowId = entry.Value;
                    continue;
                }

                _captureInvalidationHookStates.Add(
                    entry.Key,
                    new CaptureInvalidationHookState { WindowId = entry.Value });
                _captureInvalidationTargets.Add(new WeakReference<BindableObject>(entry.Key));
                entry.Key.PropertyChanged += OnCaptureInvalidationTargetPropertyChanged;
                if (entry.Key is Element element)
                {
                    element.ChildAdded += OnCaptureInvalidationTargetChildChanged;
                    element.ChildRemoved += OnCaptureInvalidationTargetChildChanged;
                }
            }
        }
    }

    private static void CollectCaptureInvalidationTargets(
        IVisualTreeElement element,
        int windowId,
        Dictionary<BindableObject, int?> targets)
    {
        if (element is BindableObject bindableObject)
        {
            if (targets.ContainsKey(bindableObject))
                return;
            targets.Add(bindableObject, windowId);
        }

        if (element is View view)
        {
            foreach (var gestureRecognizer in view.GestureRecognizers)
            {
                if (gestureRecognizer is BindableObject bindableGestureRecognizer)
                    targets.TryAdd(bindableGestureRecognizer, windowId);
            }
        }

        if (element is Page page)
        {
            foreach (var toolbarItem in page.ToolbarItems)
                targets.TryAdd(toolbarItem, windowId);
        }

        foreach (var child in element.GetVisualChildren())
            CollectCaptureInvalidationTargets(child, windowId, targets);
    }

    private void OnCaptureInvalidationTargetPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (_disposed || sender is not BindableObject target)
            return;

        lock (_captureInvalidationHookGate)
        {
            if (_captureInvalidationHookStates.TryGetValue(target, out var state))
                InvalidateUiCaptureForWindow(state.WindowId);
        }
    }

    private void OnCaptureInvalidationTargetChildChanged(object? sender, ElementEventArgs args)
    {
        if (_disposed || sender is not BindableObject target)
            return;

        lock (_captureInvalidationHookGate)
        {
            if (_captureInvalidationHookStates.TryGetValue(target, out var state))
                InvalidateUiCaptureForWindow(state.WindowId);
        }
    }

    private void StopCaptureInvalidationHooks()
    {
        lock (_captureInvalidationHookGate)
        {
            foreach (var weakTarget in _captureInvalidationTargets)
            {
                if (!weakTarget.TryGetTarget(out var target))
                    continue;

                target.PropertyChanged -= OnCaptureInvalidationTargetPropertyChanged;
                if (target is Element element)
                {
                    element.ChildAdded -= OnCaptureInvalidationTargetChildChanged;
                    element.ChildRemoved -= OnCaptureInvalidationTargetChildChanged;
                }
                _captureInvalidationHookStates.Remove(target);
            }
            _captureInvalidationTargets.Clear();
        }
    }

    private static bool IsNativeElementId(string? elementId)
        => elementId?.StartsWith("native:", StringComparison.Ordinal) == true;

    private static bool IsRegisteredNativeElementId(string? elementId)
        => elementId?.StartsWith("native:registered:", StringComparison.Ordinal) == true;

    private UiCaptureContext BeginUiCapture(int? windowIndex)
    {
        lock (_captureStateGate)
        {
            return new UiCaptureContext(
                Interlocked.Increment(ref _captureEpochSequence),
                _nativeElementRegistry?.Generation ?? 0,
                _uiMutationGeneration,
                GetExternalMutationGenerationNoLock(windowIndex),
                windowIndex,
                new Dictionary<string, CapturedElementIdentity>(StringComparer.Ordinal));
        }
    }

    private bool CommitUiCapture(UiCaptureContext capture)
    {
        lock (_captureStateGate)
        {
            if (_uiMutationInProgress != 0
                || capture.RegistryGeneration != (_nativeElementRegistry?.Generation ?? 0)
                || capture.MutationGeneration != _uiMutationGeneration
                || capture.ExternalMutationGeneration
                    != GetExternalMutationGenerationNoLock(capture.WindowId))
            {
                return false;
            }

            _captureLeases[capture.Epoch] = capture;
            var capturedIdentityCount = _captureLeases.Values.Sum(
                lease => lease.ElementIdentities?.Count ?? 0);
            while (_captureLeases.Count > MaxCaptureLeases
                || _captureLeases.Count > 1
                    && capturedIdentityCount > MaxCapturedElementIdentities)
            {
                var oldestEpoch = _captureLeases.Keys.First();
                capturedIdentityCount -=
                    _captureLeases[oldestEpoch].ElementIdentities?.Count ?? 0;
                _captureLeases.Remove(oldestEpoch);
            }

            _latestCapture = _captureLeases.Count > 0
                ? _captureLeases.Values.Last()
                : default;
            return true;
        }
    }

    private bool DidUiChangeDuringCapture(UiCaptureContext capture)
    {
        lock (_captureStateGate)
        {
            return _uiMutationInProgress != 0
                || capture.RegistryGeneration != (_nativeElementRegistry?.Generation ?? 0)
                || capture.MutationGeneration != _uiMutationGeneration
                || capture.ExternalMutationGeneration
                    != GetExternalMutationGenerationNoLock(capture.WindowId);
        }
    }

    private UiCaptureContext GetLatestUiCapture()
    {
        lock (_captureStateGate)
            return _latestCapture;
    }

    private void InvalidateUiCapture()
    {
        lock (_captureStateGate)
        {
            _uiMutationGeneration++;
            _captureLeases.Clear();
            _latestCapture = default;
        }
    }

    private void InvalidateUiCaptureForWindow(int? windowId)
    {
        lock (_captureStateGate)
        {
            _aggregateExternalMutationGeneration++;
            if (windowId.HasValue)
            {
                _windowExternalMutationGenerations[windowId.Value] =
                    _windowExternalMutationGenerations.GetValueOrDefault(windowId.Value) + 1;
            }

            foreach (var epoch in _captureLeases
                .Where(entry => entry.Value.WindowId is null
                    || entry.Value.WindowId == windowId)
                .Select(entry => entry.Key)
                .ToArray())
            {
                _captureLeases.Remove(epoch);
            }

            _latestCapture = _captureLeases.Count > 0
                ? _captureLeases.Values.Last()
                : default;
        }
    }

    private long GetExternalMutationGenerationNoLock(int? windowId)
        => windowId.HasValue
            ? _windowExternalMutationGenerations.GetValueOrDefault(windowId.Value)
            : _aggregateExternalMutationGeneration;

    private long GetExternalMutationGeneration(int? windowId)
    {
        lock (_captureStateGate)
            return GetExternalMutationGenerationNoLock(windowId);
    }

    private bool IsUiCaptureCurrent(UiCaptureContext capture)
    {
        lock (_captureStateGate)
        {
            return capture.Epoch > 0
                && _uiMutationInProgress == 0
                && capture.RegistryGeneration == (_nativeElementRegistry?.Generation ?? 0)
                && capture.MutationGeneration == _uiMutationGeneration
                && capture.ExternalMutationGeneration
                    == GetExternalMutationGenerationNoLock(capture.WindowId)
                && _captureLeases.TryGetValue(capture.Epoch, out var lease)
                && lease.Equals(capture);
        }
    }

    private bool TryGetUiCapture(long epoch, out UiCaptureContext capture)
    {
        lock (_captureStateGate)
            return _captureLeases.TryGetValue(epoch, out capture);
    }

    private HttpResponse? ValidateUiCapture(CaptureBoundRequest request)
    {
        if (request.CaptureEpoch is null && request.RegistryGeneration is null)
            return null;
        if (request.CaptureEpoch is null)
        {
            return HttpResponse.Error(
                "captureEpoch is required when registryGeneration is supplied.",
                statusCode: 400,
                reason: "capture-epoch-required");
        }

        UiCaptureContext latest;
        long currentRegistryGeneration;
        bool isCurrent;
        lock (_captureStateGate)
        {
            latest = _latestCapture;
            currentRegistryGeneration = _nativeElementRegistry?.Generation ?? 0;
            var captureToValidate = latest;
            if (request.CaptureEpoch is long requestedEpoch
                && _captureLeases.TryGetValue(requestedEpoch, out var requestedCapture))
            {
                captureToValidate = requestedCapture;
            }

            isCurrent = _uiMutationInProgress == 0
                && captureToValidate.Epoch > 0
                && captureToValidate.RegistryGeneration == currentRegistryGeneration
                && captureToValidate.MutationGeneration == _uiMutationGeneration
                && captureToValidate.ExternalMutationGeneration
                    == GetExternalMutationGenerationNoLock(captureToValidate.WindowId)
                && request.CaptureEpoch == captureToValidate.Epoch
                && (request.RegistryGeneration is null || request.RegistryGeneration == currentRegistryGeneration);
        }
        if (isCurrent)
            return null;

        return BuildStaleCaptureResponse(
            request.CaptureEpoch,
            request.RegistryGeneration,
            latest,
            currentRegistryGeneration);
    }

    private (HttpResponse? Error, UiCaptureContext Capture) ReserveUiCapture(CaptureBoundRequest request)
    {
        if (request.CaptureEpoch is null && request.RegistryGeneration is null)
            return (null, default);
        if (request.CaptureEpoch is null)
        {
            return (HttpResponse.Error(
                "captureEpoch is required when registryGeneration is supplied.",
                statusCode: 400,
                reason: "capture-epoch-required"), default);
        }

        UiCaptureContext latest;
        long currentRegistryGeneration;
        lock (_captureStateGate)
        {
            latest = _latestCapture;
            currentRegistryGeneration = _nativeElementRegistry?.Generation ?? 0;
            var currentMutationGeneration = Volatile.Read(ref _uiMutationGeneration);
            var captureToValidate = latest;
            if (request.CaptureEpoch is long requestedEpoch
                && _captureLeases.TryGetValue(requestedEpoch, out var requestedCapture))
            {
                captureToValidate = requestedCapture;
            }

            var isCurrent = captureToValidate.Epoch > 0
                && captureToValidate.RegistryGeneration == currentRegistryGeneration
                && captureToValidate.MutationGeneration == currentMutationGeneration
                && captureToValidate.ExternalMutationGeneration
                    == GetExternalMutationGenerationNoLock(captureToValidate.WindowId)
                && (request.CaptureEpoch is null || request.CaptureEpoch == captureToValidate.Epoch)
                && (request.RegistryGeneration is null || request.RegistryGeneration == currentRegistryGeneration);
            if (isCurrent)
            {
                _captureLeases.Remove(captureToValidate.Epoch);
                _latestCapture = _captureLeases.Count > 0
                    ? _captureLeases.Values.Last()
                    : default;
                return (null, captureToValidate);
            }
        }

        return (BuildStaleCaptureResponse(
            request.CaptureEpoch,
            request.RegistryGeneration,
            latest,
            currentRegistryGeneration), default);
    }

    private async Task<HttpResponse?> ValidateCapturedElementIdentityAsync(
        UiCaptureContext capture,
        string? elementId)
    {
        if (capture.Epoch <= 0 || string.IsNullOrWhiteSpace(elementId))
            return null;

        if (capture.ElementIdentities?.ContainsKey(elementId) != true)
        {
            return BuildStaleCaptureResponse(
                capture.Epoch,
                capture.RegistryGeneration,
                capture,
                _nativeElementRegistry?.Generation ?? 0);
        }

        if (IsRegisteredNativeElementId(elementId))
        {
            var registeredElement = await DispatchAsync(() =>
                _treeWalker.GetNativeElementById(elementId));
            return registeredElement is not null
                ? null
                : BuildStaleCaptureResponse(
                    capture.Epoch,
                    capture.RegistryGeneration,
                    capture,
                    _nativeElementRegistry?.Generation ?? 0);
        }

        if (capture.ElementIdentities is null
            || !capture.ElementIdentities.TryGetValue(elementId, out var capturedReference))
        {
            return BuildStaleCaptureResponse(
                capture.Epoch,
                capture.RegistryGeneration,
                capture,
                _nativeElementRegistry?.Generation ?? 0);
        }

        if (!capturedReference.TryGetTarget(out var capturedIdentity))
        {
            return BuildStaleCaptureResponse(
                capture.Epoch,
                capture.RegistryGeneration,
                capture,
                _nativeElementRegistry?.Generation ?? 0);
        }

        object? resolvedElement;
        if (IsRegisteredNativeElementId(elementId))
        {
            resolvedElement = await DispatchAsync(() => _treeWalker.GetNativeElementById(elementId));
        }
        else if (IsNativeElementId(elementId))
        {
            resolvedElement = await Task.Run(() => _treeWalker.GetNativeElementById(elementId));
        }
        else
        {
            resolvedElement = await DispatchAsync(() => _treeWalker.GetElementById(elementId, _app));
        }

        if (resolvedElement is not null
            && _treeWalker.AreElementIdentitiesEqual(
                capturedIdentity,
                _treeWalker.GetElementIdentity(resolvedElement)))
        {
            return null;
        }

        return BuildStaleCaptureResponse(
            capture.Epoch,
            capture.RegistryGeneration,
            capture,
            _nativeElementRegistry?.Generation ?? 0);
    }

    private async Task<(HttpResponse? Error, UiCaptureContext Capture)> ValidateAndConsumeUiCaptureAsync(
        CaptureBoundRequest request,
        params string?[] elementIds)
    {
        var reservation = ReserveUiCapture(request);
        if (reservation.Error is not null)
            return reservation;

        foreach (var elementId in elementIds.Distinct(StringComparer.Ordinal))
        {
            if (await ValidateCapturedElementIdentityAsync(reservation.Capture, elementId) is { } identityError)
                return (identityError, reservation.Capture);
        }

        return (null, reservation.Capture);
    }

    private async Task<HttpResponse?> PrepareUiMutationAsync(
        HttpRequest request,
        CaptureBoundRequest body,
        params string?[] elementIds)
    {
        if (request.MutationState is UiCaptureContext)
            return null;

        var validation = await ValidateAndConsumeUiCaptureAsync(body, elementIds);
        if (validation.Error is not null)
            return validation.Error;

        if (validation.Capture.Epoch > 0)
            request.MutationState = validation.Capture;
        return null;
    }

    private object? ResolveCapturedElement(
        UiCaptureContext capture,
        string elementId,
        Func<string, object?> resolver)
    {
        if (capture.Epoch <= 0)
            return resolver(elementId);

        if (capture.ElementIdentities?.ContainsKey(elementId) != true)
            return null;

        if (IsRegisteredNativeElementId(elementId))
            return resolver(elementId);

        if (capture.ElementIdentities is null
            || !capture.ElementIdentities.TryGetValue(elementId, out var capturedReference)
            || !capturedReference.TryGetTarget(out var capturedIdentity))
        {
            return null;
        }

        var resolvedElement = resolver(elementId);
        return resolvedElement is not null
            && _treeWalker.AreElementIdentitiesEqual(
                capturedIdentity,
                _treeWalker.GetElementIdentity(resolvedElement))
                ? resolvedElement
                : null;
    }

    private object? ResolveCapturedNativeElement(
        UiCaptureContext capture,
        string elementId)
        => ResolveCapturedElement(
            capture,
            elementId,
            _treeWalker.GetNativeElementById);

    private bool IsDetachedNativeElementId(string elementId)
        => IsNativeElementId(elementId) && !IsRegisteredNativeElementId(elementId);

    private Task<object?> ResolveScreenshotElementAsync(
        UiCaptureContext capture,
        string elementId)
    {
        object? Resolve()
            => ResolveCapturedElement(
                capture,
                elementId,
                id => IsNativeElementId(id)
                    ? _treeWalker.GetNativeElementById(id)
                    : _treeWalker.GetElementById(id, _app));

        return IsDetachedNativeElementId(elementId)
            ? Task.Run(Resolve)
            : DispatchAsync(Resolve);
    }

    private async Task StoreScreenshotIdentityAsync(
        UiCaptureContext capture,
        string elementId,
        object resolvedElement)
    {
        var identity = IsDetachedNativeElementId(elementId)
            ? await Task.Run(() => _treeWalker.GetElementIdentity(resolvedElement))
            : _treeWalker.GetElementIdentity(resolvedElement);
        StoreCapturedIdentity(capture, elementId, identity);
    }

    private async Task<ScreenshotCaptureOutcome> CaptureResolvedElementScreenshotAsync(
        string elementId,
        object resolvedElement,
        int? windowIndex)
    {
        if (IsDetachedNativeElementId(elementId))
        {
            var elementInfo = await Task.Run(
                () => _treeWalker.GetNativeElementInfoById(elementId));
            var bytes = await Task.Run(
                () => CaptureNativeElementScreenshotAsync(resolvedElement, elementInfo));
            return new ScreenshotCaptureOutcome
            {
                Data = bytes,
                Density = GetNativeElementDisplayDensity(elementInfo) ?? 1.0
            };
        }

        return await DispatchAsync<ScreenshotCaptureOutcome>(async () =>
        {
            var nativeElementInfo = resolvedElement is VisualElement
                ? null
                : _treeWalker.GetNativeElementInfoById(elementId);
            var bytes = resolvedElement is VisualElement visualElement
                ? await CaptureElementScreenshotAsync(visualElement)
                : await CaptureNativeElementScreenshotAsync(
                    resolvedElement,
                    nativeElementInfo);
            return new ScreenshotCaptureOutcome
            {
                Data = bytes,
                Failure = bytes == null ? DescribeScreenshotFailure() : null,
                Density = GetNativeElementDisplayDensity(nativeElementInfo)
                    ?? GetWindowDisplayDensity(GetWindow(nativeElementInfo?.WindowId ?? windowIndex))
            };
        }) ?? new ScreenshotCaptureOutcome();
    }

    private static double? GetNativeElementDisplayDensity(ElementInfo? elementInfo)
    {
        if (elementInfo?.NativeProperties?.TryGetValue(
                "displayDensity",
                out var densityValue) == true
            && double.TryParse(
                densityValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var density)
            && double.IsFinite(density)
            && density > 0)
        {
            return density;
        }

        return null;
    }

    private static UiCaptureContext GetReservedCapture(HttpRequest request)
        => request.MutationState is UiCaptureContext capture ? capture : default;

    private HttpResponse BuildStaleCaptureResponse(
        long? requestedEpoch,
        long? requestedRegistryGeneration,
        UiCaptureContext latest,
        long currentRegistryGeneration)
        => HttpResponse.Error(
            "The UI snapshot is stale. Capture the tree or hit-test again before acting.",
            statusCode: 409,
            reason: "stale-capture-epoch",
            details: new
            {
                requestedEpoch,
                requestedRegistryGeneration,
                currentEpoch = latest.Epoch,
                currentRegistryGeneration,
                currentMutationGeneration = Volatile.Read(ref _uiMutationGeneration),
                currentExternalMutationGeneration =
                    GetExternalMutationGeneration(latest.WindowId),
                currentWindowId = latest.WindowId
            });

    private HttpResponse BuildCaptureChangedResponse(UiCaptureContext capture)
        => HttpResponse.Error(
            "The UI changed while it was being captured. Retry the tree, query, or hit-test request.",
            statusCode: 409,
            reason: "capture-changed-during-read",
            details: new
            {
                captureEpoch = capture.Epoch,
                capturedRegistryGeneration = capture.RegistryGeneration,
                currentRegistryGeneration = _nativeElementRegistry?.Generation ?? 0,
                capturedMutationGeneration = capture.MutationGeneration,
                currentMutationGeneration = Volatile.Read(ref _uiMutationGeneration),
                capturedExternalMutationGeneration = capture.ExternalMutationGeneration,
                currentExternalMutationGeneration =
                    GetExternalMutationGeneration(capture.WindowId),
                mutationInProgress = Volatile.Read(ref _uiMutationInProgress) != 0,
                windowId = capture.WindowId
            });

    private static long? ParseLongQueryParameter(HttpRequest request, string name)
        => request.QueryParams.TryGetValue(name, out var value)
            && long.TryParse(value, out var parsed)
                ? parsed
                : null;

    protected override async Task<HttpResponse> ExecuteUiMutationAsync(
        HttpRequest request,
        Func<HttpRequest, Task<HttpResponse>> handler)
    {
        if (!await _uiMutationGate.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            return HttpResponse.Error(
                "Another UI mutation is still running. Retry after it completes.",
                statusCode: 409,
                reason: "ui-mutation-busy",
                details: new { retryable = true });
        }

        lock (_captureStateGate)
            _uiMutationInProgress = 1;
        HttpResponse? response = null;
        try
        {
            response = await handler(request);
            return response;
        }
        finally
        {
            lock (_captureStateGate)
            {
                if (response is { StatusCode: >= 200 and < 300 })
                {
                    _uiMutationGeneration++;
                    _captureLeases.Clear();
                    _latestCapture = default;
                }
                _uiMutationInProgress = 0;
            }
            _uiMutationGate.Release();
        }
    }

    private void StampCaptureMetadata(IEnumerable<ElementInfo> elements, UiCaptureContext capture)
    {
        foreach (var element in elements)
            StampCaptureMetadata(element, capture);
    }

    private void StoreCapturedIdentity(
        UiCaptureContext capture,
        string elementId,
        object identity)
    {
        capture.ElementIdentities?[elementId] = new CapturedElementIdentity(
            identity,
            _treeWalker.ShouldRetainElementIdentityStrongly(identity));
    }

    private void StampCaptureMetadata(ElementInfo element, UiCaptureContext capture)
    {
        element.CaptureEpoch = capture.Epoch;
        element.RegistryGeneration = capture.RegistryGeneration;
        element.WindowId = capture.WindowId ?? element.WindowId;
        if (element.IdentityToken is not null)
        {
            StoreCapturedIdentity(capture, element.Id, element.IdentityToken);
        }
        if (element.Children is null)
            return;

        foreach (var child in element.Children)
            StampCaptureMetadata(child, capture);
    }

    protected override async Task<HttpResponse> HandleHitTest(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        if (!request.QueryParams.TryGetValue("x", out var xStr) || !TryParseCoordinate(xStr, out var x))
            return HttpResponse.Error("x coordinate is required");
        if (!request.QueryParams.TryGetValue("y", out var yStr) || !TryParseCoordinate(yStr, out var y))
            return HttpResponse.Error("y coordinate is required");

        var windowIndex = ParseWindowIndex(request) ?? 0;
        var capture = BeginUiCapture(windowIndex);
        var nativeHits = new List<ElementInfo>();
        if (_treeWalker.SupportsNativeElements)
        {
            var knownWindowHandles = await DispatchAsync(() =>
                _treeWalker.GetKnownNativeWindowHandles(_app, windowIndex));
            var nativeHitTask = TryStartNativeProbe(async () =>
            {
                await Task.CompletedTask;
                try
                {
                    return _treeWalker.HitTestNativeElements(knownWindowHandles, x, y);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Microsoft.Maui.DevFlow] Native hit test failed: {ex.GetBaseException().Message}");
                    return [];
                }
            });
            if (nativeHitTask is null)
            {
                return HttpResponse.Error(
                    "Native hit testing is busy. Retry after the current native probe completes.",
                    statusCode: 409,
                    reason: "native-probe-busy",
                    details: new { retryable = true });
            }
            var nativeHitWinner = await Task.WhenAny(
                nativeHitTask,
                Task.Delay(NativeUiProbeTimeoutMs, CancellationToken.None)).ConfigureAwait(false);
            if (nativeHitWinner != nativeHitTask)
            {
                return HttpResponse.Error(
                    "Native hit testing timed out. Retry after the current native probe completes.",
                    statusCode: 409,
                    reason: "native-probe-busy",
                    details: new { retryable = true });
            }
            nativeHits = await nativeHitTask.ConfigureAwait(false);
            StampCaptureMetadata(nativeHits, capture);
        }

        var result = await DispatchAsync(() =>
        {
            var window = GetWindow(windowIndex);
            if (window == null) return (object?)null;

            // Ensure tree is walked so element IDs are assigned and synthetic bounds are populated
            _treeWalker.WalkTree(_app!, 0, windowIndex);
            RefreshCaptureInvalidationHooks(windowIndex);

            // Build active Shell context to filter out inactive ShellItem subtrees
            var activeShellItemIds = BuildActiveShellItemIds(window);

            var platformHits = VisualTreeElementExtensions.GetVisualTreeElements(window, x, y);

            // Supplement with bounds-based hit testing — some platforms (e.g. macOS AppKit)
            // don't traverse into all containers via GetVisualTreeElements
            var boundsHits = _treeWalker.HitTestByBounds(x, y, _app!, windowIndex);
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var allHits = new List<IVisualTreeElement>();
            foreach (var h in platformHits)
            {
                seen.Add(h);
                allHits.Add(h);
            }
            foreach (var bh in boundsHits)
            {
                if (seen.Add(bh))
                    allHits.Add(bh);
            }

            var elements = new List<object>();
            var seenElementIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Detect modal pages — elements behind the topmost modal should be excluded
            var modalPage = window.Navigation?.ModalStack?.LastOrDefault();

            // Exact registered native controls take precedence over synthetic chrome.
            foreach (var nativeInfo in _treeWalker.HitTestRegisteredNativeElements(x, y, windowIndex))
            {
                if (modalPage is not null
                    && !_treeWalker.IsRegisteredNativeElementUnderPage(nativeInfo.Id, modalPage))
                    continue;

                StampCaptureMetadata(nativeInfo, capture);

                elements.Add(new Dictionary<string, object?>
                {
                    ["id"] = nativeInfo.Id,
                    ["type"] = nativeInfo.Type,
                    ["role"] = nativeInfo.Role,
                    ["bounds"] = nativeInfo.Bounds,
                    ["windowBounds"] = nativeInfo.WindowBounds,
                    ["native"] = true,
                    ["ownerId"] = nativeInfo.OwnerId
                });
                seenElementIds.Add(nativeInfo.Id);
            }

            foreach (var nativeHit in nativeHits)
            {
                if (!seenElementIds.Add(nativeHit.Id))
                    continue;

                elements.Add(new Dictionary<string, object?>
                {
                    ["id"] = nativeHit.Id,
                    ["type"] = nativeHit.Type,
                    ["role"] = nativeHit.Role,
                    ["text"] = nativeHit.Text,
                    ["bounds"] = nativeHit.Bounds,
                    ["windowBounds"] = nativeHit.WindowBounds,
                    ["native"] = true,
                    ["origin"] = nativeHit.Origin,
                    ["capabilities"] = nativeHit.Capabilities
                });
            }

            var syntheticHits = _treeWalker.HitTestSynthetics(x, y);
            foreach (var (synId, marker, bounds) in syntheticHits)
            {
                if (modalPage != null && !IsSyntheticForPage(marker, modalPage))
                    continue;

                StoreCapturedIdentity(
                    capture,
                    synId,
                    _treeWalker.GetElementIdentity(marker));

                var synInfo = new Dictionary<string, object?>
                {
                    ["id"] = synId,
                    ["type"] = _treeWalker.GetSyntheticTypeName(marker),
                    ["bounds"] = bounds,
                    ["windowBounds"] = bounds,
                    ["synthetic"] = true,
                };
                var text = _treeWalker.GetSyntheticText(marker);
                if (text != null) synInfo["text"] = text;
                elements.Add(synInfo);
            }

            foreach (var hit in allHits)
            {
                if (hit is not IVisualTreeElement vte) continue;

                // Skip elements under inactive ShellItem subtrees
                if (activeShellItemIds != null && IsUnderInactiveShellItem(hit, activeShellItemIds))
                    continue;

                // Skip elements behind the modal page
                if (modalPage != null && !IsDescendantOfPage(hit, modalPage))
                    continue;

                var id = _treeWalker.GetIdForElement(vte);
                if (id == null) continue;

                StoreCapturedIdentity(
                    capture,
                    id,
                    _treeWalker.GetElementIdentity(hit));

                var info = new Dictionary<string, object?> { ["id"] = id, ["type"] = hit.GetType().Name };
                if (hit is VisualElement ve)
                {
                    info["automationId"] = ve.AutomationId;
                    info["bounds"] = new BoundsInfo
                    {
                        X = double.IsFinite(ve.Frame.X) ? ve.Frame.X : 0,
                        Y = double.IsFinite(ve.Frame.Y) ? ve.Frame.Y : 0,
                        Width = double.IsFinite(ve.Frame.Width) ? ve.Frame.Width : 0,
                        Height = double.IsFinite(ve.Frame.Height) ? ve.Frame.Height : 0
                    };

                    var wb = _treeWalker.ResolveWindowBoundsPublic(ve);
                    if (wb != null) info["windowBounds"] = wb;
                }
                if (hit is Label l) info["text"] = l.Text;
                else if (hit is Button b) info["text"] = b.Text;
                elements.Add(info);
            }

            return (object?)new
            {
                x,
                y,
                window = windowIndex,
                captureEpoch = capture.Epoch,
                registryGeneration = capture.RegistryGeneration,
                elements
            };
        });

        if (result != null && !CommitUiCapture(capture))
            return BuildCaptureChangedResponse(capture);
        return result != null
            ? HttpResponse.Json(result)
            : HttpResponse.Error($"Window {windowIndex} not found");
    }

    internal static bool TryParseCoordinate(string value, out double coordinate)
        => double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out coordinate);

    /// <summary>
    /// Builds a set of active ShellItem objects for filtering hit test results.
    /// Returns null if the window doesn't contain a Shell (no filtering needed).
    /// </summary>
    private static HashSet<object>? BuildActiveShellItemIds(Window window)
    {
        var shell = window.Page as Shell;
        if (shell == null) return null;

        var currentItem = shell.CurrentItem;
        if (currentItem == null) return null;

        // Only the current ShellItem is active
        return new HashSet<object>(ReferenceEqualityComparer.Instance) { currentItem };
    }

    /// <summary>
    /// Checks if an element is under an inactive ShellItem subtree.
    /// Walks up the parent chain to find the containing ShellItem.
    /// </summary>
    private static bool IsUnderInactiveShellItem(object element, HashSet<object> activeShellItems)
    {
        var current = element as Element;
        while (current != null)
        {
            if (current is ShellItem si)
                return !activeShellItems.Contains(si);
            current = current.Parent;
        }
        return false;
    }

    /// <summary>
    /// Checks if an element is a descendant of the given page (or the page itself).
    /// Used to filter hit test results when a modal page is active.
    /// </summary>
    private static bool IsDescendantOfPage(object element, Page page)
    {
        var current = element as Element;
        while (current != null)
        {
            if (ReferenceEquals(current, page)) return true;
            current = current.Parent;
        }
        return false;
    }

    /// <summary>
    /// Checks if a synthetic marker belongs to the given modal page context.
    /// The modal page may be a NavigationPage, TabbedPage, or FlyoutPage wrapping
    /// inner pages, so we use descendant checks rather than reference equality.
    /// </summary>
    private static bool IsSyntheticForPage(object marker, Page modalPage)
    {
        return marker switch
        {
            VisualTreeWalker.NavBarTitleMarker m => IsDescendantOfPage(m.Page, modalPage),
            ToolbarItem ti => IsDescendantOfPage(ti, modalPage),
            VisualTreeWalker.BackButtonMarker => true,
            VisualTreeWalker.SearchHandlerMarker => false,
            _ => false
        };
    }

    protected override async Task<HttpResponse> HandleScreenshot(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var requestedWindowIndex = ParseWindowIndex(request);
        var requestedEpoch = ParseLongQueryParameter(request, "captureEpoch");
        var requestedRegistryGeneration = ParseLongQueryParameter(request, "registryGeneration");
        var captureRequest = new CaptureMetadataRequest
        {
            CaptureEpoch = requestedEpoch,
            RegistryGeneration = requestedRegistryGeneration
        };
        var captureValidation = ValidateUiCapture(captureRequest);
        if (captureValidation != null)
            return captureValidation;

        var capture = requestedEpoch.HasValue
            && TryGetUiCapture(requestedEpoch.Value, out var leasedCapture)
                ? leasedCapture
                : BeginUiCapture(requestedWindowIndex);
        var windowIndex = requestedEpoch.HasValue && requestedWindowIndex is null
            ? capture.WindowId
            : requestedWindowIndex;
        if (requestedEpoch.HasValue
            && requestedWindowIndex.HasValue
            && capture.WindowId.HasValue
            && capture.WindowId != requestedWindowIndex.Value)
        {
            return HttpResponse.Error(
                $"Capture epoch {capture.Epoch} belongs to window {capture.WindowId}, not window {requestedWindowIndex.Value}.",
                statusCode: 409,
                reason: "capture-window-mismatch",
                details: new
                {
                    captureEpoch = capture.Epoch,
                    captureWindowId = capture.WindowId,
                    requestedWindowId = requestedWindowIndex.Value
                });
        }

        HttpResponse CompleteScreenshot(byte[] data)
        {
            if (requestedEpoch.HasValue)
            {
                if (!IsUiCaptureCurrent(capture))
                {
                    return BuildStaleCaptureResponse(
                        requestedEpoch,
                        requestedRegistryGeneration,
                        GetLatestUiCapture(),
                        _nativeElementRegistry?.Generation ?? 0);
                }
            }
            else if (capture.RegistryGeneration != (_nativeElementRegistry?.Generation ?? 0)
                || !CommitUiCapture(capture))
            {
                return BuildCaptureChangedResponse(capture);
            }

            var response = HttpResponse.Png(data);
            response.Headers["X-DevFlow-Capture-Epoch"] = capture.Epoch.ToString();
            response.Headers["X-DevFlow-Registry-Generation"] = capture.RegistryGeneration.ToString();
            if (capture.WindowId.HasValue)
                response.Headers["X-DevFlow-Window-Id"] = capture.WindowId.Value.ToString();
            return response;
        }

        HttpResponse CompleteScreenshotFailure(HttpResponse failure)
            => DidUiChangeDuringCapture(capture)
                ? BuildCaptureChangedResponse(capture)
                : failure;

        int? maxWidth = null;
        if (request.QueryParams.TryGetValue("maxWidth", out var mwStr) && int.TryParse(mwStr, out var mw) && mw > 0)
            maxWidth = mw;

        // Auto-scale to 1x by default on HiDPI displays. Override with scale=native to keep full resolution.
        bool autoScale = true;
        if (request.QueryParams.TryGetValue("scale", out var scaleParam))
        {
            autoScale = !scaleParam.Equals("native", StringComparison.OrdinalIgnoreCase)
                     && !scaleParam.Equals("full", StringComparison.OrdinalIgnoreCase);
        }

        // Check for fullscreen mode (captures all windows including dialogs)
        if (request.QueryParams.TryGetValue("fullscreen", out var fs) &&
            fs.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var pngData = await CaptureFullScreenAsync(windowIndex);
                if (pngData != null)
                {
                    var density = await DispatchAsync(
                        () => GetWindowDisplayDensity(GetWindow(windowIndex)));
                    return CompleteScreenshot(ResizePngIfNeeded(pngData, maxWidth, density, autoScale));
                }
                return CompleteScreenshotFailure(
                    HttpResponse.Error("Full-screen capture not supported on this platform"));
            }
            catch (Exception ex)
            {
                return CompleteScreenshotFailure(
                    HttpResponse.Error($"Full-screen screenshot failed: {ex.Message}"));
            }
        }

        // Element-level screenshot by ID
        var hasElementId = request.QueryParams.TryGetValue("id", out var elementId)
            || request.QueryParams.TryGetValue("elementId", out elementId);

        if (hasElementId && !string.IsNullOrWhiteSpace(elementId))
        {
            try
            {
                var screenshotCapture = requestedEpoch.HasValue ? capture : default;
                var resolvedElement = await ResolveScreenshotElementAsync(
                    screenshotCapture,
                    elementId);

                if (resolvedElement == null)
                {
                    if (requestedEpoch.HasValue)
                    {
                        return BuildStaleCaptureResponse(
                            requestedEpoch,
                            requestedRegistryGeneration,
                            capture,
                            _nativeElementRegistry?.Generation ?? 0);
                    }

                    return CompleteScreenshotFailure(
                        HttpResponse.Error($"Element '{elementId}' not found"));
                }

                if (!requestedEpoch.HasValue)
                    await StoreScreenshotIdentityAsync(capture, elementId, resolvedElement);

                var outcome = await CaptureResolvedElementScreenshotAsync(
                    elementId,
                    resolvedElement,
                    windowIndex);

                if (outcome?.Data == null)
                    return CompleteScreenshotFailure(
                        BuildScreenshotFailureResponse(
                            outcome?.Failure,
                            $"Capture returned null for element '{elementId}'"));

                return CompleteScreenshot(
                    ResizePngIfNeeded(
                        outcome.Data,
                        maxWidth,
                        outcome.Density,
                        autoScale));
            }
            catch (Exception ex)
            {
                return CompleteScreenshotFailure(
                    HttpResponse.Error($"Element screenshot failed: {ex.Message}"));
            }
        }

        // Element-level screenshot by CSS selector (captures first match)
        if (request.QueryParams.TryGetValue("selector", out var selector) && !string.IsNullOrWhiteSpace(selector))
        {
            try
            {
                var matchId = await DispatchAsync(() =>
                {
                    var results = _treeWalker.QueryCss(_app, selector);
                    return results.Count > 0 ? results[0].Id : null;
                });

                if (matchId == null)
                    return CompleteScreenshotFailure(
                        HttpResponse.Error($"No elements matching selector '{selector}'"));

                var screenshotCapture = requestedEpoch.HasValue ? capture : default;
                var resolvedElement = await ResolveScreenshotElementAsync(
                    screenshotCapture,
                    matchId);

                if (resolvedElement == null)
                {
                    if (requestedEpoch.HasValue)
                    {
                        return BuildStaleCaptureResponse(
                            requestedEpoch,
                            requestedRegistryGeneration,
                            capture,
                            _nativeElementRegistry?.Generation ?? 0);
                    }

                    return CompleteScreenshotFailure(
                        HttpResponse.Error($"Element '{matchId}' not found"));
                }

                if (!requestedEpoch.HasValue)
                    await StoreScreenshotIdentityAsync(capture, matchId, resolvedElement);

                var outcome = await CaptureResolvedElementScreenshotAsync(
                    matchId,
                    resolvedElement,
                    windowIndex);

                if (outcome?.Data == null)
                    return CompleteScreenshotFailure(
                        BuildScreenshotFailureResponse(
                            outcome?.Failure,
                            $"Capture returned null for element '{matchId}'"));

                return CompleteScreenshot(
                    ResizePngIfNeeded(
                        outcome.Data,
                        maxWidth,
                        outcome.Density,
                        autoScale));
            }
            catch (FormatException ex)
            {
                return HttpResponse.Error($"Invalid CSS selector: {ex.Message}");
            }
            catch (Exception ex)
            {
                return CompleteScreenshotFailure(
                    HttpResponse.Error($"Element screenshot failed: {ex.Message}"));
            }
        }

        try
        {
            var outcome = await DispatchAsync<ScreenshotCaptureOutcome>(async () =>
            {
                var window = GetWindow(windowIndex);
                if (window == null)
                    return new ScreenshotCaptureOutcome { Failure = DescribeScreenshotFailure() };

                // If a modal page is displayed, capture it instead of the underlying page
                VisualElement? topModal = null;
                try
                {
                    var modalStack = window.Page?.Navigation?.ModalStack;
                    if (modalStack?.Count > 0 && modalStack[^1] is VisualElement ms)
                        topModal = ms;
                }
                catch { }

                // Fallback: check Window's visual children for modal pages
                // (on some platforms like GTK, modals appear as direct children of the Window)
                if (topModal == null && window is IVisualTreeElement windowVte)
                {
                    var children = windowVte.GetVisualChildren();
                    for (int i = children.Count - 1; i >= 0; i--)
                    {
                        if (children[i] is Page page && page != window.Page)
                        {
                            topModal = page;
                            break;
                        }
                    }
                }

                byte[]? bytes;
                if (topModal != null)
                    bytes = await CaptureScreenshotAsync(topModal);
                else if (window.Page is VisualElement rootElement)
                    bytes = await CaptureScreenshotAsync(rootElement);
                else
                    bytes = null;

                // Diagnose in the same UI-thread turn as the failed capture so the
                // reported window/app state matches the state at capture time.
                return new ScreenshotCaptureOutcome
                {
                    Data = bytes,
                    Failure = bytes == null ? DescribeScreenshotFailure() : null,
                    Density = GetWindowDisplayDensity(window)
                };
            });

            if (outcome?.Data == null)
                return CompleteScreenshotFailure(
                    BuildScreenshotFailureResponse(
                        outcome?.Failure,
                        "Failed to capture screenshot"));

            return CompleteScreenshot(
                ResizePngIfNeeded(
                    outcome.Data,
                    maxWidth,
                    outcome.Density,
                    autoScale));
        }
        catch (Exception ex)
        {
            return CompleteScreenshotFailure(
                HttpResponse.Error($"Screenshot failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Captures a screenshot of the given root element. Override in platform-specific subclasses.
    /// </summary>
    protected virtual async Task<byte[]?> CaptureScreenshotAsync(VisualElement rootElement)
    {
        return await VisualDiagnostics.CaptureAsPngAsync(rootElement);
    }

    /// <summary>
    /// Captures a screenshot of a specific element in the visual tree.
    /// Override in platform-specific subclasses when VisualDiagnostics.CaptureAsPngAsync
    /// is not supported (e.g. macOS AppKit).
    /// </summary>
    protected virtual async Task<byte[]?> CaptureElementScreenshotAsync(VisualElement element)
    {
        return await VisualDiagnostics.CaptureAsPngAsync(element);
    }

    /// <summary>
    /// Captures a registered or platform-native element that is not a MAUI
    /// <see cref="VisualElement"/>. Platform agents override this for their native view types.
    /// </summary>
    protected virtual Task<byte[]?> CaptureNativeElementScreenshotAsync(
        object nativeElement,
        ElementInfo? elementInfo)
        => Task.FromResult<byte[]?>(null);

    protected virtual bool SupportsNativeElementScreenshots => false;

    /// <summary>
    /// Captures a full-screen screenshot including all windows (dialogs, popups, etc.).
    /// Override in platform-specific subclasses for native support.
    /// Returns null if not supported.
    /// </summary>
    protected virtual Task<byte[]?> CaptureFullScreenAsync(int? windowIndex = null)
    {
        return Task.FromResult<byte[]?>(null);
    }

    /// <summary>
    /// Describes why a screenshot capture returned null, when the platform can determine an
    /// actionable, often-retryable cause (e.g. the app window is not the frontmost application
    /// on macOS). Returns <c>null</c> when no specific cause is known, in which case the caller
    /// falls back to a generic error.
    /// <para>
    /// Invoked on the UI thread within the same dispatch as the failed capture (see
    /// <see cref="ScreenshotCaptureOutcome"/>), so the probed window/app state reflects the
    /// state at capture time rather than a later re-probe. The result is best-effort/advisory.
    /// </para>
    /// </summary>
    protected virtual ScreenshotCaptureFailure? DescribeScreenshotFailure() => null;

    /// <summary>
    /// Carries the outcome of a screenshot capture attempt: the PNG bytes on success, or the
    /// platform-described <see cref="ScreenshotCaptureFailure"/> (probed atomically in the same
    /// UI dispatch as the capture) on failure.
    /// </summary>
    private sealed class ScreenshotCaptureOutcome
    {
        public byte[]? Data { get; init; }
        public ScreenshotCaptureFailure? Failure { get; init; }
        public double Density { get; init; } = 1.0;
    }

    protected override async Task<HttpResponse> HandleProperty(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");
        if (!request.RouteParams.TryGetValue("id", out var id))
            return HttpResponse.Error("Element ID required");
        if (!request.RouteParams.TryGetValue("name", out var propName))
            return HttpResponse.Error("Property name required");
        if (IsNativeElementId(id))
        {
            return HttpResponse.Error(
                "Generic property reflection is not supported for native elements. Use the element metadata and advertised capabilities instead.",
                statusCode: 400,
                reason: "native-property-not-supported");
        }

        var value = await DispatchAsync(() =>
        {
            var el = _treeWalker.GetElementById(id, _app);
            if (el == null) return (object?)null;
            if (el is Entry { IsPassword: true }
                && propName.Equals(nameof(Entry.Text), StringComparison.OrdinalIgnoreCase))
            {
                return SensitiveValueRedactor.RedactedValue;
            }

            // Support dot-path notation (e.g., "Shadow.Radius")
            var parts = propName.Split('.');
            object? current = el;
            PropertyInfo? prop = null;
            foreach (var part in parts)
            {
                if (current == null) return null;
                var type = current.GetType();
                prop = type.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null) return null;
                current = prop.GetValue(current);
            }
            return FormatMauiPropertyValue(current);
        });

        return value != null
            ? HttpResponse.Json(new { id, property = propName, value })
            : HttpResponse.NotFound($"Property '{propName}' not found on element '{id}'");
    }

    private static string? FormatMauiPropertyValue(object? value)
    {
        if (value == null) return null;
        if (value is string s) return s;

        // Try TypeConverter first — handles Thickness, CornerRadius, Color, enums, etc.
        var converter = System.ComponentModel.TypeDescriptor.GetConverter(value.GetType());
        if (converter.CanConvertTo(typeof(string))
            && converter.GetType() != typeof(System.ComponentModel.TypeConverter)
            && converter is not System.ComponentModel.CollectionConverter)
        {
            try
            {
                var result = converter.ConvertToString(value);
                if (result != null) return result;
            }
            catch { }
        }

        // Fallback for complex types that lack TypeConverter ConvertTo support
        return value switch
        {
            Shadow shadow => FormatShadow(shadow),
            SolidColorBrush scb => $"SolidColorBrush Color={scb.Color?.ToArgbHex() ?? "(null)"}",
            LinearGradientBrush lgb => $"LinearGradientBrush StartPoint={lgb.StartPoint}, EndPoint={lgb.EndPoint}, Stops=[{FormatGradientStops(lgb.GradientStops)}]",
            RadialGradientBrush rgb => $"RadialGradientBrush Center={rgb.Center}, Radius={rgb.Radius}, Stops=[{FormatGradientStops(rgb.GradientStops)}]",
            Brush brush => brush.GetType().Name,
            Microsoft.Maui.Controls.Shapes.RoundRectangle rr => $"RoundRectangle CornerRadius={FormatMauiPropertyValue(rr.CornerRadius)}",
            Microsoft.Maui.Controls.Shapes.Shape shape => shape.GetType().Name,
            ColumnDefinitionCollection cols => string.Join(", ", cols.Select(c => FormatGridLength(c.Width))),
            RowDefinitionCollection rows => string.Join(", ", rows.Select(r => FormatGridLength(r.Height))),
            LayoutOptions lo => $"{lo.Alignment}{(lo.Expands ? ", Expands" : "")}",
            LinearItemsLayout lin => $"LinearItemsLayout Orientation={lin.Orientation}, ItemSpacing={lin.ItemSpacing}",
            GridItemsLayout grid => $"GridItemsLayout Span={grid.Span}, Orientation={grid.Orientation}, HorizontalSpacing={grid.HorizontalItemSpacing}, VerticalSpacing={grid.VerticalItemSpacing}",
            FileImageSource fis => $"File: {fis.File}",
            UriImageSource uis => $"Uri: {uis.Uri}",
            FontImageSource fontIs => $"Font: {fontIs.Glyph} ({fontIs.FontFamily})",
            ImageSource img => img.GetType().Name,
            System.Collections.ICollection col => $"{col.GetType().Name} ({col.Count} items)",
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? value.GetType().Name,
        };
    }

    private static string FormatGridLength(GridLength gl) => gl.IsStar
        ? (gl.Value == 1 ? "*" : $"{gl.Value}*")
        : gl.IsAbsolute ? gl.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "Auto";

    private static string FormatGradientStops(GradientStopCollection? stops)
    {
        if (stops == null || stops.Count == 0) return "";
        return string.Join(", ", stops.Select(s =>
            $"{s.Color.ToArgbHex()} {(s.Offset * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture)}%"));
    }

    private static string FormatShadow(Shadow shadow)
    {
        var parts = new List<string>();
        if (shadow.Brush is SolidColorBrush scb)
            parts.Add($"Brush={scb.Color?.ToArgbHex()}");
        else if (shadow.Brush != null)
            parts.Add($"Brush={shadow.Brush.GetType().Name}");
        parts.Add($"Offset=({shadow.Offset.X},{shadow.Offset.Y})");
        parts.Add($"Radius={shadow.Radius}");
        parts.Add($"Opacity={shadow.Opacity}");
        return string.Join(", ", parts);
    }

    private static BindableProperty? FindBindableProperty(Type type, PropertyInfo property)
    {
        var fieldName = $"{property.Name}Property";

        while (type != null)
        {
            var bpField = type.GetField(fieldName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

            bpField ??= Array.Find(
                type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly),
                f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

            if (bpField?.GetValue(null) is BindableProperty candidate &&
                candidate.PropertyName.Equals(property.Name, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            type = type.BaseType!;
        }

        return null;
    }

    protected override async Task<HttpResponse> HandleSetProperty(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");
        if (!request.RouteParams.TryGetValue("id", out var id))
            return HttpResponse.Error("Element ID required");
        if (!request.RouteParams.TryGetValue("name", out var propName))
            return HttpResponse.Error("Property name required");
        if (IsNativeElementId(id))
        {
            return HttpResponse.Error(
                "Generic property mutation is not supported for native elements. Use a native action advertised by the element capabilities instead.",
                statusCode: 400,
                reason: "native-property-not-supported");
        }

        var body = request.BodyAs<SetPropertyRequest>();
        if (body?.Value == null)
            return HttpResponse.Error("value is required");
        if (await PrepareUiMutationAsync(request, body, id) is { } staleCapture)
            return staleCapture;

        var startedAtUtc = DateTime.UtcNow;
        var reservedCapture = GetReservedCapture(request);
        var result = await DispatchAsync(() =>
        {
            var el = ResolveCapturedElement(
                reservedCapture,
                id,
                elementId => _treeWalker.GetElementById(elementId, _app));
            if (el == null) return "Element not found";

            var type = el.GetType();
            var prop = type.GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (prop == null || !prop.CanWrite)
                return $"Property '{propName}' not found or read-only";

            try
            {
                var converted = ConvertPropertyValue(prop.PropertyType, body.Value);

                // Use BindableObject.SetValue when possible so the handler mapper
                // propagates the change to the native platform view.
                if (el is BindableObject bindable &&
                    FindBindableProperty(type, prop) is BindableProperty bp)
                {
                    bindable.SetValue(bp, converted);
                    return "ok";
                }

                prop.SetValue(el, converted);
                return "ok";
            }
            catch (Exception ex)
            {
                return $"Failed to set property: {ex.Message}";
            }
        });

        PublishUiOperationSpan(
            "action.set-property",
            startedAtUtc,
            result == "ok",
            result == "ok" ? null : result,
            id,
            new { property = propName });

        if (result == "ok")
        {
            PublishUiEvent("treeChange", new
            {
                changeType = "modified",
                elementId = id,
                elementType = "property",
                parentId = (string?)null,
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            });
        }

        return result == "ok"
            ? HttpResponse.Json(new { id, property = propName, value = body.Value })
            : HttpResponse.Error(result);
    }

    private static object? ConvertPropertyValue(Type targetType, string value)
    {
        // Handle nullable types
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying == typeof(string)) return value;
        if (underlying == typeof(bool)) return bool.Parse(value);
        if (underlying == typeof(int)) return int.Parse(value);
        if (underlying == typeof(double)) return double.Parse(value);
        if (underlying == typeof(float)) return float.Parse(value);

        // MAUI Color - supports named colors and hex
        if (underlying == typeof(Microsoft.Maui.Graphics.Color))
        {
            // Try hex format (#RRGGBB or #AARRGGBB)
            if (value.StartsWith('#'))
                return Microsoft.Maui.Graphics.Color.FromArgb(value);

            // Try named colors via reflection on Colors class (check both properties and fields)
            var colorsType = typeof(Microsoft.Maui.Graphics.Colors);
            var colorProp = colorsType.GetProperty(value,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase);
            if (colorProp != null)
                return colorProp.GetValue(null);

            var colorField = colorsType.GetField(value,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase);
            if (colorField != null)
                return colorField.GetValue(null);

            // Try Color.FromArgb as last resort (for rgb hex without #)
            try { return Microsoft.Maui.Graphics.Color.FromArgb($"#{value}"); }
            catch { }

            throw new ArgumentException($"Unknown color: '{value}'. Use hex (#FF6347) or a named color (Red, Blue, Green, etc.).");
        }

        // MAUI Thickness (uniform or "left,top,right,bottom")
        if (underlying == typeof(Microsoft.Maui.Thickness))
        {
            var parts = value.Split(',');
            return parts.Length switch
            {
                1 => new Microsoft.Maui.Thickness(double.Parse(parts[0])),
                2 => new Microsoft.Maui.Thickness(double.Parse(parts[0]), double.Parse(parts[1])),
                4 => new Microsoft.Maui.Thickness(double.Parse(parts[0]), double.Parse(parts[1]),
                    double.Parse(parts[2]), double.Parse(parts[3])),
                _ => throw new ArgumentException($"Invalid Thickness format: {value}")
            };
        }

        // Enum types
        if (underlying.IsEnum)
            return Enum.Parse(underlying, value, ignoreCase: true);

        // Fallback: TypeConverter
        var converter = System.ComponentModel.TypeDescriptor.GetConverter(underlying);
        if (converter.CanConvertFrom(typeof(string)))
            return converter.ConvertFromString(value);

        throw new ArgumentException($"Cannot convert '{value}' to {targetType.Name}");
    }

    protected override async Task<HttpResponse> HandleTap(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<ActionRequest>();
        if (body?.ElementId == null)
            return HttpResponse.Error("elementId is required");
        if (await PrepareUiMutationAsync(request, body, body.ElementId) is { } staleCapture)
            return staleCapture;

        var startedAtUtc = DateTime.UtcNow;
        var reservedCapture = GetReservedCapture(request);
        if (IsNativeElementId(body.ElementId))
        {
            string nativeResult;
            if (IsRegisteredNativeElementId(body.ElementId))
            {
                nativeResult = await DispatchAsync<string>(
                    async () =>
                    {
                        var nativeElement = ResolveCapturedNativeElement(reservedCapture, body.ElementId!);
                        return nativeElement is null
                            ? $"Native element '{body.ElementId}' is stale"
                            : await _treeWalker.TryRegisteredNativeElementTapAsync(body.ElementId!, nativeElement);
                    })
                    ?? $"Native element '{body.ElementId}' could not be invoked";
            }
            else
            {
                var nativeElement = ResolveCapturedNativeElement(reservedCapture, body.ElementId!);
                if (nativeElement is null)
                {
                    nativeResult = $"Native element '{body.ElementId}' is stale";
                }
                else
                {
                    nativeResult = await TryNativeElementTapAsync(body.ElementId!, nativeElement)
                        ?? await Task.Run(() => _treeWalker.TryNativeElementTap(body.ElementId!, nativeElement));
                }
            }

            PublishUiOperationSpan(
                "action.tap",
                startedAtUtc,
                nativeResult == "ok",
                nativeResult == "ok" ? null : nativeResult,
                body.ElementId);

            return nativeResult == "ok" ? HttpResponse.Ok("Tapped") : HttpResponse.Error(nativeResult);
        }

        var result = await DispatchAsync<string>(async () =>
        {
            var el = ResolveCapturedElement(
                reservedCapture,
                body.ElementId,
                elementId => _treeWalker.GetElementById(elementId, _app));
            if (el == null) return "Element not found";

            switch (el)
            {
                case Button btn:
                    if (await TryNativeTapFirstAsync(btn))
                        return "ok";
                    try { btn.SendClicked(); }
                    catch { if (btn is VisualElement ve && !TryNativeTap(ve)) return $"Native tap failed on Button"; }
                    return "ok";
                case ImageButton imgBtn:
                    if (await TryNativeTapFirstAsync(imgBtn))
                        return "ok";
                    try { imgBtn.SendClicked(); }
                    catch { if (imgBtn is VisualElement ve && !TryNativeTap(ve)) return $"Native tap failed on ImageButton"; }
                    return "ok";
                case CheckBox cb:
                    cb.IsChecked = !cb.IsChecked;
                    return "ok";
                case Switch sw:
                    sw.IsToggled = !sw.IsToggled;
                    return "ok";
                case RadioButton rb:
                    rb.IsChecked = true;
                    return "ok";
                case ToolbarItem ti:
                    ((IMenuItemController)ti).Activate();
                    return "ok";
                case VisualTreeWalker.BackButtonMarker back:
                    return await VisualTreeWalker.ActivateBackButtonMarkerAsync(back);
                case VisualTreeWalker.FlyoutButtonMarker flyoutBtn:
                    flyoutBtn.Shell.FlyoutIsPresented = true;
                    return "ok";
                case VisualTreeWalker.ShellFlyoutItemMarker flyoutItem:
                    flyoutItem.Shell.CurrentItem = flyoutItem.Item;
                    return "ok";
                case VisualTreeWalker.ShellTabMarker shellTab:
                    shellTab.Shell.CurrentItem.CurrentItem = shellTab.Section;
                    return "ok";
                case VisualTreeWalker.FlyoutToggleMarker flyoutToggle:
                    flyoutToggle.FlyoutPage.IsPresented = !flyoutToggle.FlyoutPage.IsPresented;
                    return "ok";
                case VisualTreeWalker.TabbedPageTabMarker tab:
                    tab.TabbedPage.CurrentPage = tab.Page;
                    return "ok";
                case MenuItem mi:
                    ((IMenuItemController)mi).Activate();
                    return "ok";
                case Picker picker:
                    picker.Focus();
                    return "ok";
                case DatePicker datePicker:
                    datePicker.Focus();
                    return "ok";
                case TimePicker timePicker:
                    timePicker.Focus();
                    return "ok";
                case Page page when page.Parent is TabbedPage tabbed:
                    tabbed.CurrentPage = page;
                    return "ok";
                case ShellContent sc:
                    if (Shell.Current != null)
                    {
                        sc.IsVisible = true;
                        Shell.Current.CurrentItem = sc.Parent as ShellSection ?? Shell.Current.CurrentItem;
                    }
                    return "ok";
                case ShellSection ss:
                    if (Shell.Current != null)
                        Shell.Current.CurrentItem = ss;
                    return "ok";
                case IView view when view is View v:
                    // Try TapGestureRecognizer: Command first, then Tapped event via reflection
                    var tapGesture = v.GestureRecognizers.OfType<TapGestureRecognizer>().FirstOrDefault();
                    if (tapGesture != null)
                    {
                        if (tapGesture.Command != null)
                        {
                            tapGesture.Command.Execute(tapGesture.CommandParameter);
                            return "ok";
                        }
                        // Fire the Tapped event via reflection (SendTapped is internal)
                        if (TryInvokeTapped(tapGesture, v))
                            return "ok";
                        return $"TapGestureRecognizer found but SendTapped reflection failed on {el.GetType().FullName}";
                    }

                    // Native platform fallback for UIControl/Android.Views.View
                    if (v is VisualElement nativeVe && TryNativeTap(nativeVe))
                        return "ok";

                    return $"No tap handler on {el.GetType().FullName} (gestures:{v.GestureRecognizers.Count}, type:{v.GetType().Name})";
                // Comet views implement IGestureView with Gesture objects that have Invoke().
                // Check via reflection to avoid a hard Comet dependency.
                case IView gestureView when TryInvokeCometGestureTap(gestureView):
                    return "ok";
                // Comet views implement MAUI interfaces (IButton, ISwitch, etc.)
                // but not Microsoft.Maui.Controls classes, so handle via interfaces
                case IButton iBtn:
                    iBtn.Clicked();
                    return "ok";
                case ISwitch iSw:
                    iSw.IsOn = !iSw.IsOn;
                    return "ok";
                case ICheckBox iCb:
                    iCb.IsChecked = !iCb.IsChecked;
                    return "ok";
                case IRadioButton iRb:
                    iRb.IsChecked = true;
                    return "ok";
                case IView iView when iView.Handler?.PlatformView != null:
                    // Last resort: try native tap via handler's platform view
                    if (TryNativeTapOnHandler(iView))
                        return "ok";
                    return $"Unhandled IView type: {el.GetType().FullName}";
                default:
                    return $"Unhandled type: {el.GetType().FullName}";
            }
        }) ?? "Element tap failed";

        PublishUiOperationSpan(
            "action.tap",
            startedAtUtc,
            result == "ok",
            result == "ok" ? null : result,
            body.ElementId);

        return result == "ok" ? HttpResponse.Ok("Tapped") : HttpResponse.Error(result);
    }

    /// <summary>
    /// Invokes the Tapped event on a TapGestureRecognizer via reflection.
    /// Calls internal SendTapped(View sender, Func&lt;IElement?, Point?&gt;? getPosition) method.
    /// </summary>
    private static bool TryInvokeTapped(TapGestureRecognizer tapGesture, View sender)
    {
        try
        {
            // SendTapped is internal on TapGestureRecognizer itself
            var sendTapped = typeof(TapGestureRecognizer).GetMethod("SendTapped",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (sendTapped != null)
            {
                var paramCount = sendTapped.GetParameters().Length;
                var args = paramCount switch
                {
                    0 => Array.Empty<object>(),
                    1 => new object[] { sender },
                    _ => new object?[] { sender, null }
                };
                sendTapped.Invoke(tapGesture, args);
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] TryInvokeTapped failed: {ex.GetBaseException().Message}");
        }
        return false;
    }

    /// <summary>
    /// Attempts to invoke a Comet-style tap gesture on an IView via reflection.
    /// Checks for IGestureView interface by name, iterates Gestures looking for TapGesture,
    /// and calls Invoke(). No hard Comet dependency required.
    /// </summary>
    private static bool TryInvokeCometGestureTap(IView view)
    {
        try
        {
            // Check if the view implements an interface named "IGestureView" with a "Gestures" property
            var gestureViewInterface = view.GetType().GetInterfaces()
                .FirstOrDefault(i => i.Name == "IGestureView");
            if (gestureViewInterface == null) return false;

            var gesturesProp = gestureViewInterface.GetProperty("Gestures");
            if (gesturesProp == null) return false;

            var gestures = gesturesProp.GetValue(view) as System.Collections.IEnumerable;
            if (gestures == null) return false;

            // Find the first gesture whose type name contains "TapGesture"
            foreach (var gesture in gestures)
            {
                if (gesture == null) continue;
                var gestureType = gesture.GetType();
                if (gestureType.Name.Contains("TapGesture") ||
                    (gestureType.BaseType != null && gestureType.BaseType.Name.Contains("TapGesture")))
                {
                    // Call Invoke() — public virtual method on Comet.Gesture
                    var invokeMethod = gestureType.GetMethod("Invoke",
                        BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (invokeMethod != null)
                    {
                        invokeMethod.Invoke(gesture, null);
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] TryInvokeCometGestureTap failed: {ex.GetBaseException().Message}");
        }
        return false;
    }

    /// <summary>
    /// Attempts to tap a native platform view as a fallback.
    /// Override in platform-specific subclasses for native tap support.
    /// </summary>
    protected virtual bool TryNativeTap(VisualElement ve)
    {
        return false;
    }

    /// <summary>
    /// Allows platforms whose native click handlers may open synchronous modal loops to schedule
    /// a native tap before MAUI invokes the managed click event inline.
    /// </summary>
    /// <remarks>
    /// Implementations must complete only after the native invocation callback has run, so capture
    /// invalidation and subsequent actions cannot race queued platform work.
    /// </remarks>
    protected virtual Task<bool> TryNativeTapFirstAsync(VisualElement ve)
        => Task.FromResult(false);

    protected virtual Task<string?> TryNativeElementTapAsync(string elementId, object nativeElement)
        => Task.FromResult<string?>(null);

    /// <summary>
    /// Attempts to tap a native platform view via handler for non-VisualElement IView types (e.g. Comet views).
    /// Uses reflection to get the PlatformView from the handler and invoke SendAccessibilityAction or performClick.
    /// Override in platform-specific subclasses for richer support.
    /// </summary>
    protected virtual bool TryNativeTapOnHandler(IView view)
    {
        try
        {
            var handler = view.Handler;
            if (handler == null) return false;

            // Use safe reflection to get PlatformView (avoids AmbiguousMatchException on generic handlers)
            var platformViewProp = CometViewResolver.GetPropertySafe(handler.GetType(), "PlatformView");
            if (platformViewProp == null) return false;

            var platformView = platformViewProp.GetValue(handler);
            if (platformView == null) return false;

            // Try to invoke SendActionForControlEvents on UIControl (iOS/macCatalyst)
            var sendActionMethod = platformView.GetType().GetMethod("SendActionForControlEvents",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (sendActionMethod != null)
            {
                // UIControlEvent.TouchUpInside = 1 << 6 = 64
                sendActionMethod.Invoke(platformView, new object[] { (nuint)64 });
                return true;
            }

            // Try performClick for Android
            var performClickMethod = platformView.GetType().GetMethod("PerformClick",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (performClickMethod != null)
            {
                performClickMethod.Invoke(platformView, null);
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] TryNativeTapOnHandler failed: {ex.GetBaseException().Message}");
        }
        return false;
    }

    protected override async Task<HttpResponse> HandleFill(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<FillRequest>();
        if (body?.ElementId == null || body.Text == null)
            return HttpResponse.Error("elementId and text are required");
        if (await PrepareUiMutationAsync(request, body, body.ElementId) is { } staleCapture)
            return staleCapture;

        var startedAtUtc = DateTime.UtcNow;
        var reservedCapture = GetReservedCapture(request);
        if (IsNativeElementId(body.ElementId))
        {
            var nativeResult = IsRegisteredNativeElementId(body.ElementId)
                ? await DispatchAsync(() =>
                {
                    var nativeElement = ResolveCapturedNativeElement(reservedCapture, body.ElementId!);
                    return nativeElement is null
                        ? $"Native element '{body.ElementId}' is stale"
                        : _treeWalker.TryNativeElementSetValue(body.ElementId!, nativeElement, body.Text!);
                })
                : await Task.Run(() =>
                {
                    var nativeElement = ResolveCapturedNativeElement(reservedCapture, body.ElementId!);
                    return nativeElement is null
                        ? $"Native element '{body.ElementId}' is stale"
                        : _treeWalker.TryNativeElementSetValue(body.ElementId!, nativeElement, body.Text!);
                });
            PublishUiOperationSpan(
                "action.fill",
                startedAtUtc,
                nativeResult == "ok",
                nativeResult == "ok" ? null : nativeResult,
                body.ElementId,
                new { textLength = body.Text.Length });

            if (nativeResult == "ok")
            {
                PublishUiEvent("treeChange", new
                {
                    changeType = "modified",
                    elementId = body.ElementId,
                    elementType = "input",
                    parentId = (string?)null,
                    timestamp = DateTimeOffset.UtcNow.ToString("O")
                });
            }

            return nativeResult == "ok" ? HttpResponse.Ok("Text set") : HttpResponse.Error(nativeResult);
        }

        var result = await DispatchAsync(() =>
        {
            var el = ResolveCapturedElement(
                reservedCapture,
                body.ElementId,
                elementId => _treeWalker.GetElementById(elementId, _app));
            if (el == null) return "Element not found";

            switch (el)
            {
                case Entry entry:
                    entry.Text = body.Text;
                    entry.Unfocus();
                    return "ok";
                case Editor editor:
                    editor.Text = body.Text;
                    editor.Unfocus();
                    return "ok";
                case SearchBar searchBar:
                    searchBar.Text = body.Text;
                    searchBar.Unfocus();
                    return "ok";
                default:
                    return $"Unhandled type: {el.GetType().FullName}";
            }
        });

        PublishUiOperationSpan(
            "action.fill",
            startedAtUtc,
            result == "ok",
            result == "ok" ? null : result,
            body.ElementId,
            new { textLength = body.Text.Length });

        if (result == "ok")
        {
            PublishUiEvent("treeChange", new
            {
                changeType = "modified",
                elementId = body.ElementId,
                elementType = "input",
                parentId = (string?)null,
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            });
        }

        return result == "ok" ? HttpResponse.Ok("Text set") : HttpResponse.Error(result);
    }

    protected override async Task<HttpResponse> HandleClear(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<ActionRequest>();
        if (body?.ElementId == null)
            return HttpResponse.Error("elementId is required");
        if (await PrepareUiMutationAsync(request, body, body.ElementId) is { } staleCapture)
            return staleCapture;

        var startedAtUtc = DateTime.UtcNow;
        var reservedCapture = GetReservedCapture(request);
        if (IsNativeElementId(body.ElementId))
        {
            var nativeResult = IsRegisteredNativeElementId(body.ElementId)
                ? await DispatchAsync(() =>
                {
                    var nativeElement = ResolveCapturedNativeElement(reservedCapture, body.ElementId!);
                    return nativeElement is null
                        ? $"Native element '{body.ElementId}' is stale"
                        : _treeWalker.TryNativeElementSetValue(body.ElementId!, nativeElement, string.Empty);
                })
                : await Task.Run(() =>
                {
                    var nativeElement = ResolveCapturedNativeElement(reservedCapture, body.ElementId!);
                    return nativeElement is null
                        ? $"Native element '{body.ElementId}' is stale"
                        : _treeWalker.TryNativeElementSetValue(body.ElementId!, nativeElement, string.Empty);
                });
            var nativeSuccess = nativeResult == "ok";
            PublishUiOperationSpan(
                "action.clear",
                startedAtUtc,
                nativeSuccess,
                nativeSuccess ? null : nativeResult,
                body.ElementId);

            if (nativeSuccess)
            {
                PublishUiEvent("treeChange", new
                {
                    changeType = "modified",
                    elementId = body.ElementId,
                    elementType = "input",
                    parentId = (string?)null,
                    timestamp = DateTimeOffset.UtcNow.ToString("O")
                });
            }

            return nativeSuccess ? HttpResponse.Ok("Cleared") : HttpResponse.Error(nativeResult);
        }

        var success = await DispatchAsync(() =>
        {
            var el = ResolveCapturedElement(
                reservedCapture,
                body.ElementId,
                elementId => _treeWalker.GetElementById(elementId, _app));
            if (el == null) return false;

            switch (el)
            {
                case Entry entry:
                    entry.Text = string.Empty;
                    return true;
                case Editor editor:
                    editor.Text = string.Empty;
                    return true;
                case SearchBar searchBar:
                    searchBar.Text = string.Empty;
                    return true;
                default:
                    return false;
            }
        });

        PublishUiOperationSpan(
            "action.clear",
            startedAtUtc,
            success,
            success ? null : "Element does not accept text input",
            body.ElementId);

        if (success)
        {
            PublishUiEvent("treeChange", new
            {
                changeType = "modified",
                elementId = body.ElementId,
                elementType = "input",
                parentId = (string?)null,
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            });
        }

        return success ? HttpResponse.Ok("Cleared") : HttpResponse.Error("Element does not accept text input");
    }

    protected override async Task<HttpResponse> HandleFocus(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<ActionRequest>();
        if (body?.ElementId == null)
            return HttpResponse.Error("elementId is required");
        if (await PrepareUiMutationAsync(request, body, body.ElementId) is { } staleCapture)
            return staleCapture;

        var startedAtUtc = DateTime.UtcNow;
        var reservedCapture = GetReservedCapture(request);
        if (IsNativeElementId(body.ElementId))
        {
            var nativeResult = IsRegisteredNativeElementId(body.ElementId)
                ? await DispatchAsync(() =>
                {
                    var nativeElement = ResolveCapturedNativeElement(reservedCapture, body.ElementId!);
                    return nativeElement is null
                        ? $"Native element '{body.ElementId}' is stale"
                        : _treeWalker.TryNativeElementFocus(body.ElementId!, nativeElement);
                })
                : await Task.Run(() =>
                {
                    var nativeElement = ResolveCapturedNativeElement(reservedCapture, body.ElementId!);
                    return nativeElement is null
                        ? $"Native element '{body.ElementId}' is stale"
                        : _treeWalker.TryNativeElementFocus(body.ElementId!, nativeElement);
                });
            var nativeSuccess = nativeResult == "ok";
            PublishUiOperationSpan(
                "action.focus",
                startedAtUtc,
                nativeSuccess,
                nativeSuccess ? null : nativeResult,
                body.ElementId);

            return nativeSuccess ? HttpResponse.Ok("Focused") : HttpResponse.Error(nativeResult);
        }

        var success = await DispatchAsync(() =>
        {
            var el = ResolveCapturedElement(
                reservedCapture,
                body.ElementId,
                elementId => _treeWalker.GetElementById(elementId, _app));
            if (el is not VisualElement ve) return false;
            ve.Focus();
            return true;
        });

        PublishUiOperationSpan(
            "action.focus",
            startedAtUtc,
            success,
            success ? null : "Cannot focus element",
            body.ElementId);

        return success ? HttpResponse.Ok("Focused") : HttpResponse.Error("Cannot focus element");
    }

    protected override async Task<HttpResponse> HandleNavigate(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<NavigateRequest>();
        if (string.IsNullOrEmpty(body?.Route))
            return HttpResponse.Error("route is required");

        var startedAtUtc = DateTime.UtcNow;
        var fromRoute = Shell.Current?.CurrentState?.Location?.ToString();
        Publish(new ProfilerMarker
        {
            TsUtc = DateTime.UtcNow,
            Type = "navigation.start",
            Name = body.Route,
            PayloadJson = JsonSerializer.Serialize(new { route = body.Route })
        });

        var result = await DispatchAsync(async () =>
        {
            try
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.GoToAsync(body.Route);
                    return "ok";
                }
                return "No Shell.Current available";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        });

        Publish(new ProfilerMarker
        {
            TsUtc = DateTime.UtcNow,
            Type = "navigation.end",
            Name = body.Route,
            PayloadJson = JsonSerializer.Serialize(new { route = body.Route, success = result == "ok", error = result == "ok" ? null : result })
        });

        PublishUiOperationSpan(
            "action.navigate",
            startedAtUtc,
            result == "ok",
            result == "ok" ? null : result,
            elementPath: body.Route,
            tags: new { route = body.Route });

        if (result == "ok")
        {
            PublishUiEvent("navigation", new
            {
                from = fromRoute,
                to = body.Route,
                route = body.Route,
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            });
        }
        else
        {
            PublishUiEvent("error", new
            {
                message = result ?? "Navigation failed",
                stackTrace = (string?)null,
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            });
        }

        return result == "ok" ? HttpResponse.Ok($"Navigated to {body.Route}") : HttpResponse.Error(result ?? "Navigation failed");
    }

    protected override async Task<HttpResponse> HandleResize(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<ResizeRequest>();
        if (body == null || body.Width <= 0 || body.Height <= 0)
            return HttpResponse.Error("width and height are required (positive integers)");

        var startedAtUtc = DateTime.UtcNow;
        var windowIndex = ParseWindowIndex(request);
        var result = await DispatchAsync(() =>
        {
            var window = GetWindow(windowIndex);
            if (window?.Handler?.PlatformView == null)
                return "No window available";

            try
            {
                // Use platform-specific resize
                TryNativeResize(window, body.Width, body.Height);
                return "ok";
            }
            catch (Exception ex)
            {
                return $"Resize failed: {ex.Message}";
            }
        });

        PublishUiOperationSpan(
            "action.resize",
            startedAtUtc,
            result == "ok",
            result == "ok" ? null : result,
            tags: new { width = body.Width, height = body.Height, windowIndex });

        return result == "ok"
            ? HttpResponse.Json(new { success = true, width = body.Width, height = body.Height })
            : HttpResponse.Error(result);
    }

    /// <summary>
    /// Platform-specific window resize. Override in platform agents for native support.
    /// </summary>
    protected virtual void TryNativeResize(IWindow window, int width, int height)
    {
        // Default: try casting to MAUI Window which has settable Width/Height
        if (window is Window mauiWindow)
        {
            mauiWindow.Width = width;
            mauiWindow.Height = height;
        }
    }

    protected override async Task<HttpResponse> HandleBack(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var startedAtUtc = DateTime.UtcNow;
        var windowIndex = ParseWindowIndex(request);
        var result = await DispatchAsync(async () =>
        {
            try
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.GoToAsync("..");
                    return "ok";
                }

                var page = GetWindow(windowIndex)?.Page;
                if (page?.Navigation?.ModalStack?.Count > 0)
                {
                    await page.Navigation.PopModalAsync();
                    return "ok";
                }

                if (page?.Navigation?.NavigationStack?.Count > 1)
                {
                    await page.Navigation.PopAsync();
                    return "ok";
                }

                return "No navigation stack available";
            }
            catch (Exception ex)
            {
                return ex.GetBaseException().Message;
            }
        });

        PublishUiOperationSpan("action.back", startedAtUtc, result == "ok", result == "ok" ? null : result);

        return result == "ok"
            ? HttpResponse.Ok("Navigated back")
            : HttpResponse.Error(result ?? "Back navigation failed");
    }

    protected override async Task<HttpResponse> HandleKey(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<KeyActionRequest>();
        if (body == null || (string.IsNullOrWhiteSpace(body.Key) && string.IsNullOrWhiteSpace(body.Text)))
            return HttpResponse.Error("key or text is required");
        if (await PrepareUiMutationAsync(request, body, body.ElementId) is { } staleCapture)
            return staleCapture;

        var startedAtUtc = DateTime.UtcNow;
        var keyValue = body.Key ?? body.Text ?? string.Empty;
        var reservedCapture = GetReservedCapture(request);
        var result = await DispatchAsync(() =>
        {
            object? el = null;
            if (!string.IsNullOrWhiteSpace(body.ElementId))
            {
                el = ResolveCapturedElement(
                    reservedCapture,
                    body.ElementId,
                    elementId => _treeWalker.GetElementById(elementId, _app));
                if (el == null)
                    return "Element not found";
            }

            var normalizedKey = keyValue.Trim().ToLowerInvariant();
            var text = body.Text ?? (keyValue.Length == 1 ? keyValue : null);

            switch (el)
            {
                case Entry entry:
                    if (normalizedKey is "enter" or "return")
                    {
                        entry.SendCompleted();
                        return "ok";
                    }
                    if (normalizedKey is "backspace" or "delete")
                    {
                        entry.Text = entry.Text?.Length > 0 ? entry.Text[..^1] : string.Empty;
                        return "ok";
                    }
                    if (!string.IsNullOrEmpty(text))
                    {
                        entry.Text += text;
                        return "ok";
                    }
                    return $"Unsupported key '{keyValue}' for Entry";

                case Editor editor:
                    if (normalizedKey is "backspace" or "delete")
                    {
                        editor.Text = editor.Text?.Length > 0 ? editor.Text[..^1] : string.Empty;
                        return "ok";
                    }
                    if (normalizedKey is "enter" or "return")
                    {
                        editor.Text += Environment.NewLine;
                        return "ok";
                    }
                    if (!string.IsNullOrEmpty(text))
                    {
                        editor.Text += text;
                        return "ok";
                    }
                    return $"Unsupported key '{keyValue}' for Editor";

                case SearchBar searchBar:
                    if (normalizedKey is "backspace" or "delete")
                    {
                        searchBar.Text = searchBar.Text?.Length > 0 ? searchBar.Text[..^1] : string.Empty;
                        return "ok";
                    }
                    if (!string.IsNullOrEmpty(text))
                    {
                        searchBar.Text += text;
                        return "ok";
                    }
                    return normalizedKey is "enter" or "return"
                        ? "ok"
                        : $"Unsupported key '{keyValue}' for SearchBar";

                case null:
                    return "ok";

                default:
                    return $"Element '{body.ElementId}' does not accept keyboard input";
            }
        });

        PublishUiOperationSpan(
            "action.key",
            startedAtUtc,
            result == "ok",
            result == "ok" ? null : result,
            body.ElementId,
            new { key = keyValue, text = body.Text });

        return result == "ok"
            ? HttpResponse.Json(new { success = true, key = keyValue, text = body.Text, elementId = body.ElementId })
            : HttpResponse.Error(result ?? "Key action failed");
    }

    protected override async Task<HttpResponse> HandleGesture(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<GestureActionRequest>();
        if (body == null || string.IsNullOrWhiteSpace(body.Type))
            return HttpResponse.Error("type is required");
        if (await PrepareUiMutationAsync(request, body, body.ElementId) is { } staleCapture)
            return staleCapture;

        var gestureType = body.Type.Trim().ToLowerInvariant();

        return gestureType switch
        {
            "tap" => await HandleTap(new HttpRequest
            {
                Method = "POST",
                MutationState = request.MutationState,
                Body = JsonSerializer.Serialize(new ActionRequest
                {
                    ElementId = body.ElementId
                })
            }),
            "longpress" or "long-press" => await HandleTap(new HttpRequest
            {
                Method = "POST",
                MutationState = request.MutationState,
                Body = JsonSerializer.Serialize(new ActionRequest
                {
                    ElementId = body.ElementId
                })
            }),
            "swipe" => await HandleScroll(new HttpRequest
            {
                Method = "POST",
                MutationState = request.MutationState,
                Body = JsonSerializer.Serialize(new ScrollRequest
                {
                    ElementId = body.ElementId,
                    DeltaX = body.Direction?.Equals("left", StringComparison.OrdinalIgnoreCase) == true ? -body.Distance :
                        body.Direction?.Equals("right", StringComparison.OrdinalIgnoreCase) == true ? body.Distance : 0,
                    DeltaY = body.Direction?.Equals("up", StringComparison.OrdinalIgnoreCase) == true ? -body.Distance :
                        body.Direction?.Equals("down", StringComparison.OrdinalIgnoreCase) == true ? body.Distance : 0,
                    Animated = body.DurationMs <= 0 || body.DurationMs < 400
                })
            }),
            _ => HttpResponse.Error($"Gesture '{body.Type}' is not supported")
        };
    }

    protected override async Task<HttpResponse> HandleBatch(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<BatchRequest>();
        if (body?.Actions == null || body.Actions.Count == 0)
            return HttpResponse.Error("actions are required");

        var reservation = ReserveUiCapture(body);
        if (reservation.Error is not null)
            return reservation.Error;
        foreach (var elementId in body.Actions
            .Select(action => action.ElementId)
            .Distinct(StringComparer.Ordinal))
        {
            if (await ValidateCapturedElementIdentityAsync(reservation.Capture, elementId) is { } identityError)
                return identityError;
        }

        var results = new List<object>(body.Actions.Count);
        var allSucceeded = true;

        try
        {
            foreach (var action in body.Actions)
            {
                var actionName = (action.Action ?? action.Type ?? string.Empty).Trim().ToLowerInvariant();
                HttpResponse response;

                if (await ValidateCapturedElementIdentityAsync(
                    reservation.Capture,
                    action.ElementId) is { } identityError)
                {
                    response = identityError;
                }
                else
                {

                    switch (actionName)
                    {
                        case "tap":
                            response = await HandleTap(new HttpRequest
                            {
                                Method = "POST",
                                MutationState = reservation.Capture,
                                Body = JsonSerializer.Serialize(new ActionRequest
                                {
                                    ElementId = action.ElementId
                                })
                            });
                            break;
                        case "fill":
                            response = await HandleFill(new HttpRequest
                            {
                                Method = "POST",
                                MutationState = reservation.Capture,
                                Body = JsonSerializer.Serialize(new FillRequest
                                {
                                    ElementId = action.ElementId,
                                    Text = action.Text ?? string.Empty
                                })
                            });
                            break;
                        case "clear":
                            response = await HandleClear(new HttpRequest
                            {
                                Method = "POST",
                                MutationState = reservation.Capture,
                                Body = JsonSerializer.Serialize(new ActionRequest
                                {
                                    ElementId = action.ElementId
                                })
                            });
                            break;
                        case "focus":
                            response = await HandleFocus(new HttpRequest
                            {
                                Method = "POST",
                                MutationState = reservation.Capture,
                                Body = JsonSerializer.Serialize(new ActionRequest
                                {
                                    ElementId = action.ElementId
                                })
                            });
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
                                MutationState = reservation.Capture,
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
                                MutationState = reservation.Capture,
                                Body = JsonSerializer.Serialize(new KeyActionRequest
                                {
                                    ElementId = action.ElementId,
                                    Key = action.Key,
                                    Text = action.Text
                                })
                            });
                            break;
                        case "gesture":
                            response = await HandleGesture(new HttpRequest
                            {
                                Method = "POST",
                                MutationState = reservation.Capture,
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
                                MutationState = reservation.Capture,
                                RouteParams = new Dictionary<string, string>
                                {
                                    ["id"] = action.ElementId ?? string.Empty,
                                    ["name"] = action.Property ?? string.Empty
                                },
                                Body = JsonSerializer.Serialize(new SetPropertyRequest
                                {
                                    Value = action.Value ?? string.Empty
                                })
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
        finally
        {
            InvalidateUiCapture();
        }
    }

    protected override async Task<HttpResponse> HandleScroll(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<ScrollRequest>();
        if (body == null)
            return HttpResponse.Error("Request body is required");
        if (await PrepareUiMutationAsync(request, body, body.ElementId) is { } staleCapture)
            return staleCapture;

        var position = ParseScrollToPosition(body.ScrollToPosition);
        var startedAtUtc = DateTime.UtcNow;
        var reservedCapture = GetReservedCapture(request);
        if (IsNativeElementId(body.ElementId))
        {
            var nativeResult = IsRegisteredNativeElementId(body.ElementId)
                ? await DispatchAsync(() =>
                {
                    var nativeElement = ResolveCapturedNativeElement(reservedCapture, body.ElementId!);
                    return nativeElement is null
                        ? $"Native element '{body.ElementId}' is stale"
                        : _treeWalker.TryNativeElementScroll(
                            body.ElementId!,
                            nativeElement,
                            body.DeltaX,
                            body.DeltaY);
                })
                : await Task.Run(() =>
                {
                    var nativeElement = ResolveCapturedNativeElement(reservedCapture, body.ElementId!);
                    return nativeElement is null
                        ? $"Native element '{body.ElementId}' is stale"
                        : _treeWalker.TryNativeElementScroll(
                            body.ElementId!,
                            nativeElement,
                            body.DeltaX,
                            body.DeltaY);
                });
            PublishUiOperationSpan(
                "action.scroll",
                startedAtUtc,
                nativeResult == "ok",
                nativeResult == "ok" ? null : nativeResult,
                body.ElementId,
                new { body.DeltaX, body.DeltaY, body.Animated });

            return nativeResult == "ok" ? HttpResponse.Ok("Scrolled") : HttpResponse.Error(nativeResult);
        }

        var result = await DispatchAsync(async () =>
        {
            // Priority 1: Scroll by item index on a specific ItemsView
            if (body.ItemIndex.HasValue)
            {
                object? targetObj = null;
                if (!string.IsNullOrEmpty(body.ElementId))
                {
                    targetObj = ResolveCapturedElement(
                        reservedCapture,
                        body.ElementId,
                        elementId => _treeWalker.GetElementById(elementId, _app));
                    if (targetObj == null) return "Element not found";
                }

                // Find the ItemsView — either the target itself or its ancestor
                var itemsView = targetObj as ItemsView ?? (targetObj is VisualElement tve ? FindAncestor<ItemsView>(tve) : null);
                // Since ListView inherits from ItemsView in .NET 10+, ItemsView check covers both
                if (itemsView == null && targetObj == null)
                {
                    // No element specified — find first ItemsView on the page
                    var window = GetWindow(ParseWindowIndex(request));
                    if (window?.Page != null)
                        itemsView = FindDescendant<ItemsView>(window.Page);
                }

                if (itemsView != null)
                {
                    await ScrollWithTimeoutAsync(
                        () => { itemsView.ScrollTo(body.ItemIndex.Value, body.GroupIndex ?? -1, position, body.Animated); return Task.CompletedTask; },
                        () => { itemsView.ScrollTo(body.ItemIndex.Value, body.GroupIndex ?? -1, position, false); return Task.CompletedTask; });
                    return "ok";
                }

                return "No CollectionView or ListView found for item-index scroll";
            }

            // Priority 2: Scroll element into view
            if (!string.IsNullOrEmpty(body.ElementId))
            {
                var el = ResolveCapturedElement(
                    reservedCapture,
                    body.ElementId,
                    elementId => _treeWalker.GetElementById(elementId, _app));
                if (el == null) return "Element not found";

                if (el is VisualElement ve)
                {
                    // 2a: Check for ItemsView ancestor — use BindingContext to find item index
                    var ancestorItemsView = FindAncestor<ItemsView>(ve);
                    if (ancestorItemsView != null && ve.BindingContext != null)
                    {
                        var index = GetItemIndex(ancestorItemsView.ItemsSource, ve.BindingContext);
                        if (index >= 0)
                        {
                            await ScrollWithTimeoutAsync(
                                () => { ancestorItemsView.ScrollTo(index, position: position, animate: body.Animated); return Task.CompletedTask; },
                                () => { ancestorItemsView.ScrollTo(index, position: position, animate: false); return Task.CompletedTask; });
                            return "ok";
                        }
                    }

                    // 2b: Check for ScrollView ancestor (existing behavior)
                    var scrollView = FindAncestor<ScrollView>(ve);
                    if (scrollView != null)
                    {
                        await ScrollWithTimeoutAsync(
                            () => scrollView.ScrollToAsync(ve, (ScrollToPosition)position, body.Animated),
                            () => scrollView.ScrollToAsync(ve, (ScrollToPosition)position, false));
                        return "ok";
                    }

                    // 2d: Element is itself a scrollable view — apply delta
                    if (el is ScrollView sv && (body.DeltaX != 0 || body.DeltaY != 0))
                    {
                        var newX = Math.Max(0, sv.ScrollX + body.DeltaX);
                        var newY = Math.Max(0, sv.ScrollY + body.DeltaY);
                        await ScrollWithTimeoutAsync(
                            () => sv.ScrollToAsync(newX, newY, body.Animated),
                            () => sv.ScrollToAsync(newX, newY, false));
                        return "ok";
                    }

                    // 2e: Element is an ItemsView — apply delta via native scroll
                    if (el is ItemsView && (body.DeltaX != 0 || body.DeltaY != 0))
                    {
                        if (await TryNativeScroll(ve, body.DeltaX, body.DeltaY))
                            return "ok";
                        return $"Native scroll not supported on this platform for {el.GetType().Name}";
                    }

                    // 2f: Try native scroll as final fallback
                    if (body.DeltaX != 0 || body.DeltaY != 0)
                    {
                        if (await TryNativeScroll(ve, body.DeltaX, body.DeltaY))
                            return "ok";
                    }
                }
                // Comet views implement IView/IScrollView but NOT VisualElement.
                // Try native scroll via the handler's platform view.
                else if (el is IView iView && (body.DeltaX != 0 || body.DeltaY != 0))
                {
                    if (await TryNativeScrollOnHandler(iView, body.DeltaX, body.DeltaY))
                        return "ok";
                    return $"Native scroll not supported for IView type: {el.GetType().FullName}";
                }

                return $"No scrollable ancestor found for element '{body.ElementId}'";
            }

            // Priority 3: Delta scroll with no element — find first scrollable on current page
            var pageWindow = GetWindow(ParseWindowIndex(request));
            if (pageWindow?.Page == null) return "No page available";

            // Use the current visible page (Shell.CurrentPage or the window page)
            var currentPage = (pageWindow.Page as Shell)?.CurrentPage ?? pageWindow.Page;

            // 3a: Try ItemsView via native scroll first (CollectionView/ListView are more common scroll targets)
            var targetItemsView = FindDescendant<ItemsView>(currentPage);
            if (targetItemsView is VisualElement ive)
            {
                if (await TryNativeScroll(ive, body.DeltaX, body.DeltaY))
                    return "ok";
            }

            // 3b: Try ScrollView on current page
            var targetScroll = FindDescendant<ScrollView>(currentPage);
            if (targetScroll != null)
            {
                var newX = targetScroll.ScrollX + body.DeltaX;
                var newY = targetScroll.ScrollY + body.DeltaY;
                var x = Math.Max(0, newX);
                var y = Math.Max(0, newY);
                await ScrollWithTimeoutAsync(
                    () => targetScroll.ScrollToAsync(x, y, body.Animated),
                    () => targetScroll.ScrollToAsync(x, y, false));
                return "ok";
            }

            // 3c: Try IView-based scroll (Comet ScrollView implements IScrollView, not Controls.ScrollView)
            // Walk the visual tree looking for IScrollView implementations via IVisualTreeElement
            var iScrollView = FindDescendantIScrollView(currentPage);
            if (iScrollView != null && await TryNativeScrollOnHandler(iScrollView, body.DeltaX, body.DeltaY))
                return "ok";

            return "No scrollable view found on page";
        });

        PublishUiOperationSpan(
            "action.scroll",
            startedAtUtc,
            result == "ok",
            result == "ok" ? null : result,
            body.ElementId,
            new { body.DeltaX, body.DeltaY, body.Animated });

        return result == "ok" ? HttpResponse.Ok("Scrolled") : HttpResponse.Error(result ?? "Scroll failed");
    }

    /// <summary>
    /// Parse a ScrollToPosition string to the MAUI enum value.
    /// </summary>
    private static ScrollToPosition ParseScrollToPosition(string? value)
    {
        if (string.IsNullOrEmpty(value)) return ScrollToPosition.MakeVisible;
        return value.ToLowerInvariant() switch
        {
            "start" => ScrollToPosition.Start,
            "center" => ScrollToPosition.Center,
            "end" => ScrollToPosition.End,
            "makevisible" => ScrollToPosition.MakeVisible,
            _ => ScrollToPosition.MakeVisible
        };
    }

    /// <summary>
    /// Get item from an IEnumerable by index.
    /// </summary>
    private static object? GetItemByIndex(System.Collections.IEnumerable? source, int index)
    {
        if (source == null) return null;
        if (source is System.Collections.IList list && index >= 0 && index < list.Count)
            return list[index];
        var i = 0;
        foreach (var item in source)
        {
            if (i == index) return item;
            i++;
        }
        return null;
    }

    /// <summary>
    /// Find the index of an item in an IEnumerable by reference or equality.
    /// </summary>
    private static int GetItemIndex(System.Collections.IEnumerable? source, object item)
    {
        if (source == null) return -1;
        if (source is System.Collections.IList list)
            return list.IndexOf(item);
        var i = 0;
        foreach (var obj in source)
        {
            if (ReferenceEquals(obj, item) || Equals(obj, item)) return i;
            i++;
        }
        return -1;
    }

    /// <summary>
    /// Try to scroll a native view by pixel delta. Override in platform-specific subclasses.
    /// Returns true if the scroll was handled natively.
    /// </summary>
    protected virtual Task<bool> TryNativeScroll(VisualElement element, double deltaX, double deltaY)
    {
        return Task.FromResult(false);
    }

    /// <summary>
    /// Attempts native scroll on an IView (e.g. Comet ScrollView) via its handler's platform view.
    /// Uses reflection to find UIScrollView (iOS/macCatalyst), Android ScrollView, or WinUI ScrollViewer.
    /// Override in platform-specific subclasses for richer support.
    /// </summary>
    protected virtual Task<bool> TryNativeScrollOnHandler(IView view, double deltaX, double deltaY)
    {
        try
        {
            var handler = view.Handler;
            if (handler == null) return Task.FromResult(false);

            var platformViewProp = CometViewResolver.GetPropertySafe(handler.GetType(), "PlatformView");
            if (platformViewProp == null) return Task.FromResult(false);

            var platformView = platformViewProp.GetValue(handler);
            if (platformView == null) return Task.FromResult(false);

            // Delegate to platform override's native scroll capability via reflection
            // Look for UIScrollView (iOS/macCatalyst) via searching the native view hierarchy
            var scrollResult = TryNativeScrollOnPlatformView(platformView, deltaX, deltaY);
            return Task.FromResult(scrollResult);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] TryNativeScrollOnHandler failed: {ex.GetBaseException().Message}");
        }
        return Task.FromResult(false);
    }

    /// <summary>
    /// Attempts native scroll directly on a platform view object.
    /// Override in platform-specific subclasses (iOS, Android, Windows) for real implementations.
    /// </summary>
    protected virtual bool TryNativeScrollOnPlatformView(object platformView, double deltaX, double deltaY)
    {
        return false;
    }

    /// <summary>
    /// Walks the visual tree from a root element looking for an IScrollView implementation
    /// (including Comet ScrollView which implements IScrollView but not Controls.ScrollView).
    /// Accepts IVisualTreeElement to traverse Comet views that are not Element subclasses.
    /// </summary>
    private static IView? FindDescendantIScrollView(IVisualTreeElement root)
    {
        if (root is IScrollView && root is IView svView)
            return svView;

        foreach (var child in root.GetVisualChildren())
        {
            if (child is IScrollView && child is IView childView)
                return childView;
            if (child is IVisualTreeElement childVte)
            {
                var found = FindDescendantIScrollView(childVte);
                if (found != null) return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Animated ScrollToAsync can deadlock on iOS when dispatched.
    /// Fall back to non-animated scroll if the animated version doesn't complete in time.
    /// </summary>
    private static async Task ScrollWithTimeoutAsync(Func<Task> animatedScroll, Func<Task> fallbackScroll)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var scrollTask = animatedScroll();
        var completed = await Task.WhenAny(scrollTask, Task.Delay(3000, cts.Token));
        if (completed == scrollTask)
        {
            cts.Cancel();
            return;
        }
        // Animated scroll timed out — fall back to non-animated
        await fallbackScroll();
    }

    private static T? FindAncestor<T>(Element element) where T : Element
    {
        var current = element.Parent;
        while (current != null)
        {
            if (current is T match) return match;
            current = current.Parent;
        }
        return null;
    }

    private static T? FindDescendant<T>(Element element) where T : Element
    {
        if (element is T match) return match;
        if (element is IVisualTreeElement vte)
        {
            foreach (var child in vte.GetVisualChildren())
            {
                if (child is Element childElement)
                {
                    var found = FindDescendant<T>(childElement);
                    if (found != null) return found;
                }
            }
        }
        return null;
    }

    protected override void EnsureAutoUiHooks()
    {
        if (!IsProfilerFeatureAvailable || !_profilerSessions.IsActive || _dispatcher == null || !_options.EnableHighLevelUiHooks)
            return;

        var now = DateTime.UtcNow;
        if ((now - _lastUiHookScanTsUtc).TotalMilliseconds < UiHookScanIntervalMs)
            return;
        if (Interlocked.CompareExchange(ref _uiHookScanInFlight, 1, 0) != 0)
            return;

        _lastUiHookScanTsUtc = now;

        void Scan()
        {
            try
            {
                TryEnsureShellNavigationHooks();
                ScanUiTreeForHooks();
            }
            catch (Exception ex)
            {
                Publish(new ProfilerMarker
                {
                    TsUtc = DateTime.UtcNow,
                    Type = "profiler.hook.error",
                    Name = "ui-hook-scan",
                    PayloadJson = JsonSerializer.Serialize(new { error = ex.GetBaseException().Message })
                });
            }
            finally
            {
                Interlocked.Exchange(ref _uiHookScanInFlight, 0);
            }
        }

        if (_dispatcher.IsDispatchRequired)
        {
            _dispatcher.Dispatch(Scan);
        }
        else
        {
            Scan();
        }
    }

    protected override void StopAutoUiHooks()
    {
        if (_hookedShell != null)
        {
            _hookedShell.Navigating -= OnShellNavigating;
            _hookedShell.Navigated -= OnShellNavigated;
            _hookedShell = null;
        }

        lock (_uiHookGate)
        {
            foreach (var unsubscribe in _uiHookUnsubscribers)
                unsubscribe();
            _uiHookUnsubscribers.Clear();
            _uiHookGeneration = _uiHookGeneration == int.MaxValue ? 1 : _uiHookGeneration + 1;
            _navigationStartedAtUtc = null;
            _navigationTargetRoute = null;
            _lastUserActionTsUtc = DateTime.MinValue;
            _lastUserActionName = null;
            _lastUserActionElementPath = null;
        }
    }

    private void TryEnsureShellNavigationHooks()
    {
        var shell = Shell.Current;
        if (shell == null || ReferenceEquals(shell, _hookedShell))
            return;

        if (_hookedShell != null)
        {
            _hookedShell.Navigating -= OnShellNavigating;
            _hookedShell.Navigated -= OnShellNavigated;
        }

        shell.Navigating += OnShellNavigating;
        shell.Navigated += OnShellNavigated;
        _hookedShell = shell;
    }

    private void ScanUiTreeForHooks()
    {
        if (_app is not IVisualTreeElement appElement)
            return;

        foreach (var child in appElement.GetVisualChildren())
        {
            if (child is Element element)
                ScanElementForHooks(element);
        }
    }

    private void ScanElementForHooks(Element element)
    {
        var detailedHooksEnabled = _options.EnableDetailedUiHooks;
        switch (element)
        {
            case Button button when detailedHooksEnabled:
                AttachButtonHook(button);
                break;
            case ImageButton imageButton when detailedHooksEnabled:
                AttachImageButtonHook(imageButton);
                break;
            case Entry entry when detailedHooksEnabled:
                AttachEntryHook(entry);
                break;
            case SearchBar searchBar when detailedHooksEnabled:
                AttachSearchBarHook(searchBar);
                break;
            case CheckBox checkBox when detailedHooksEnabled:
                AttachCheckBoxHook(checkBox);
                break;
            case Switch toggle when detailedHooksEnabled:
                AttachSwitchHook(toggle);
                break;
            case Picker picker when detailedHooksEnabled:
                AttachPickerHook(picker);
                break;
            case ScrollView scrollView:
                AttachScrollViewHook(scrollView);
                break;
            case CollectionView collectionView:
                AttachCollectionViewHook(collectionView);
                break;
            case Page page:
                AttachPageHooks(page);
                break;
        }

        if (detailedHooksEnabled && element is View view)
        {
            foreach (var tapGesture in view.GestureRecognizers.OfType<TapGestureRecognizer>())
                AttachTapGestureHook(tapGesture);
        }

        if (element is not IVisualTreeElement visualElement)
            return;

        foreach (var child in visualElement.GetVisualChildren())
        {
            if (child is Element childElement)
                ScanElementForHooks(childElement);
        }
    }

    private bool TryRegisterUiHook(BindableObject target, string hookKey, Action? unsubscribe = null)
    {
        lock (_uiHookGate)
        {
            var state = _uiHookStates.GetOrCreateValue(target);
            if (state.Generation != _uiHookGeneration)
            {
                state.Generation = _uiHookGeneration;
                state.HookKeys.Clear();
            }

            if (!state.HookKeys.Add(hookKey))
                return false;

            if (unsubscribe != null)
                _uiHookUnsubscribers.Add(unsubscribe);

            return true;
        }
    }

    private void AttachButtonHook(Button button)
    {
        if (!TryRegisterUiHook(button, "Button.Clicked", () => button.Clicked -= OnButtonClicked))
            return;
        button.Clicked += OnButtonClicked;
    }

    private void AttachImageButtonHook(ImageButton imageButton)
    {
        if (!TryRegisterUiHook(imageButton, "ImageButton.Clicked", () => imageButton.Clicked -= OnImageButtonClicked))
            return;
        imageButton.Clicked += OnImageButtonClicked;
    }

    private void AttachEntryHook(Entry entry)
    {
        if (!TryRegisterUiHook(entry, "Entry.Completed", () => entry.Completed -= OnEntryCompleted))
            return;
        entry.Completed += OnEntryCompleted;
    }

    private void AttachSearchBarHook(SearchBar searchBar)
    {
        if (!TryRegisterUiHook(searchBar, "SearchBar.SearchButtonPressed", () => searchBar.SearchButtonPressed -= OnSearchBarSearchButtonPressed))
            return;
        searchBar.SearchButtonPressed += OnSearchBarSearchButtonPressed;
    }

    private void AttachCheckBoxHook(CheckBox checkBox)
    {
        if (!TryRegisterUiHook(checkBox, "CheckBox.CheckedChanged", () => checkBox.CheckedChanged -= OnCheckBoxCheckedChanged))
            return;
        checkBox.CheckedChanged += OnCheckBoxCheckedChanged;
    }

    private void AttachSwitchHook(Switch toggle)
    {
        if (!TryRegisterUiHook(toggle, "Switch.Toggled", () => toggle.Toggled -= OnSwitchToggled))
            return;
        toggle.Toggled += OnSwitchToggled;
    }

    private void AttachPickerHook(Picker picker)
    {
        if (!TryRegisterUiHook(picker, "Picker.SelectedIndexChanged", () => picker.SelectedIndexChanged -= OnPickerSelectedIndexChanged))
            return;
        picker.SelectedIndexChanged += OnPickerSelectedIndexChanged;
    }

    private void AttachCollectionViewHook(CollectionView collectionView)
    {
        if (!TryRegisterUiHook(collectionView, "CollectionView.SelectionChanged", () => collectionView.SelectionChanged -= OnCollectionViewSelectionChanged))
            return;
        collectionView.SelectionChanged += OnCollectionViewSelectionChanged;
        if (TryRegisterUiHook(collectionView, "CollectionView.Scrolled", () => collectionView.Scrolled -= OnCollectionViewScrolled))
            collectionView.Scrolled += OnCollectionViewScrolled;
        AttachRenderHooks(collectionView, "collection");
    }

    private void AttachScrollViewHook(ScrollView scrollView)
    {
        if (TryRegisterUiHook(scrollView, "ScrollView.Scrolled", () => scrollView.Scrolled -= OnScrollViewScrolled))
            scrollView.Scrolled += OnScrollViewScrolled;
        AttachRenderHooks(scrollView, "scroll");
    }

    private void AttachRenderHooks(VisualElement element, string role)
    {
        var renderState = _elementRenderStates.GetOrCreateValue(element);
        if (renderState.TrackingStartedAtUtc == default)
            renderState.TrackingStartedAtUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(renderState.Role))
            renderState.Role = role;

        if (TryRegisterUiHook(element, $"{role}.SizeChanged", () => element.SizeChanged -= OnTrackedElementSizeChanged))
            element.SizeChanged += OnTrackedElementSizeChanged;
        if (TryRegisterUiHook(element, $"{role}.MeasureInvalidated", () => element.MeasureInvalidated -= OnTrackedElementMeasureInvalidated))
            element.MeasureInvalidated += OnTrackedElementMeasureInvalidated;
    }

    private void AttachTapGestureHook(TapGestureRecognizer tapGesture)
    {
        if (!TryRegisterUiHook(tapGesture, "TapGestureRecognizer.Tapped", () => tapGesture.Tapped -= OnTapGestureTapped))
            return;
        tapGesture.Tapped += OnTapGestureTapped;
    }

    private void AttachPageHooks(Page page)
    {
        if (TryRegisterUiHook(page, "Page.Appearing", () => page.Appearing -= OnPageAppearing))
            page.Appearing += OnPageAppearing;
        if (TryRegisterUiHook(page, "Page.Disappearing", () => page.Disappearing -= OnPageDisappearing))
            page.Disappearing += OnPageDisappearing;
        if (TryRegisterUiHook(page, "Page.SizeChanged", () => page.SizeChanged -= OnPageSizeChanged))
            page.SizeChanged += OnPageSizeChanged;
        if (TryRegisterUiHook(page, "Page.MeasureInvalidated", () => page.MeasureInvalidated -= OnPageMeasureInvalidated))
            page.MeasureInvalidated += OnPageMeasureInvalidated;
        AttachRenderHooks(page, "page");
    }

    private void OnButtonClicked(object? sender, EventArgs args)
        => TrackUiInteraction("ui.input.button.click", sender as Element);

    private void OnImageButtonClicked(object? sender, EventArgs args)
        => TrackUiInteraction("ui.input.image-button.click", sender as Element);

    private void OnEntryCompleted(object? sender, EventArgs args)
        => TrackUiInteraction("ui.input.entry.complete", sender as Element);

    private void OnSearchBarSearchButtonPressed(object? sender, EventArgs args)
        => TrackUiInteraction("ui.input.search.submit", sender as Element);

    private void OnCheckBoxCheckedChanged(object? sender, CheckedChangedEventArgs args)
        => TrackUiInteraction("ui.input.checkbox.toggle", sender as Element, new { value = args.Value });

    private void OnSwitchToggled(object? sender, ToggledEventArgs args)
        => TrackUiInteraction("ui.input.switch.toggle", sender as Element, new { value = args.Value });

    private void OnPickerSelectedIndexChanged(object? sender, EventArgs args)
    {
        var picker = sender as Picker;
        TrackUiInteraction("ui.input.picker.select", picker, new { selectedIndex = picker?.SelectedIndex });
    }

    private void OnCollectionViewSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        var selectionCount = args.CurrentSelection?.Count ?? 0;
        TrackUiInteraction("ui.input.collection.select", sender as Element, new { selectionCount });
    }

    private void OnCollectionViewScrolled(object? sender, ItemsViewScrolledEventArgs args)
    {
        if (sender is not CollectionView collectionView)
            return;

        var horizontalOffset = TryReadDoubleProperty(args, "HorizontalOffset");
        var verticalOffset = TryReadDoubleProperty(args, "VerticalOffset");
        var firstVisibleItem = TryReadIntProperty(args, "FirstVisibleItemIndex");
        var lastVisibleItem = TryReadIntProperty(args, "LastVisibleItemIndex");

        TrackScrollEvent(
            collectionView,
            sourceName: "collection-view",
            offsetX: horizontalOffset,
            offsetY: verticalOffset,
            firstVisibleIndex: firstVisibleItem,
            lastVisibleIndex: lastVisibleItem);
    }

    private void OnScrollViewScrolled(object? sender, ScrolledEventArgs args)
    {
        if (sender is not ScrollView scrollView)
            return;

        TrackScrollEvent(
            scrollView,
            sourceName: "scroll-view",
            offsetX: args.ScrollX,
            offsetY: args.ScrollY);
    }

    private void TrackScrollEvent(
        BindableObject source,
        string sourceName,
        double offsetX,
        double offsetY,
        int? firstVisibleIndex = null,
        int? lastVisibleIndex = null)
    {
        if (!IsProfilerFeatureAvailable || !_profilerSessions.IsActive)
            return;

        var now = DateTime.UtcNow;
        var state = _scrollBatchStates.GetOrCreateValue(source);
        var elementPath = BuildElementPath(source as Element);

        if (!state.IsActive)
        {
            state.IsActive = true;
            state.StartedAtUtc = now;
            state.StartOffsetX = offsetX;
            state.StartOffsetY = offsetY;
            state.EventCount = 0;
            state.StartFirstVisibleIndex = firstVisibleIndex;
            state.StartLastVisibleIndex = lastVisibleIndex;
            RememberUserAction("ui.scroll", elementPath, now);
            Publish(new ProfilerMarker
            {
                TsUtc = now,
                Type = "ui.scroll.start",
                Name = sourceName,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    source = sourceName,
                    elementPath,
                    offsetX,
                    offsetY,
                    firstVisibleIndex,
                    lastVisibleIndex
                })
            });
        }

        state.EventCount++;
        state.LastEventAtUtc = now;
        state.LastOffsetX = offsetX;
        state.LastOffsetY = offsetY;
        state.LastFirstVisibleIndex = firstVisibleIndex;
        state.LastLastVisibleIndex = lastVisibleIndex;
        var flushVersion = ++state.FlushVersion;

        if (_dispatcher != null)
        {
            _dispatcher.DispatchDelayed(
                TimeSpan.FromMilliseconds(220),
                () => TryFlushScrollBatch(source, sourceName, state, flushVersion));
        }
        else
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(220);
                TryFlushScrollBatch(source, sourceName, state, flushVersion);
            });
        }
    }

    private void TryFlushScrollBatch(BindableObject source, string sourceName, ScrollBatchState state, int flushVersion)
    {
        if (!state.IsActive || flushVersion != state.FlushVersion)
            return;
        if ((DateTime.UtcNow - state.LastEventAtUtc).TotalMilliseconds < 180)
            return;

        state.IsActive = false;
        var startTsUtc = state.StartedAtUtc;
        var endTsUtc = state.LastEventAtUtc;
        if (startTsUtc == default || endTsUtc < startTsUtc)
            return;

        var deltaX = state.LastOffsetX - state.StartOffsetX;
        var deltaY = state.LastOffsetY - state.StartOffsetY;
        var visibleShift = ComputeVisibleShift(state);
        var elementPath = BuildElementPath(source as Element);

        Publish(new ProfilerMarker
        {
            TsUtc = endTsUtc,
            Type = "ui.scroll.end",
            Name = sourceName,
            PayloadJson = JsonSerializer.Serialize(new
            {
                source = sourceName,
                elementPath,
                deltaX,
                deltaY,
                visibleShift,
                events = state.EventCount
            })
        });

        Publish(new ProfilerSpan
        {
            SpanId = Guid.NewGuid().ToString("N"),
            TraceId = _profilerSessions.CurrentSession?.SessionId,
            StartTsUtc = startTsUtc,
            EndTsUtc = endTsUtc,
            Kind = "ui.scroll",
            Name = "ui.scroll.batch",
            Status = "ok",
            ThreadId = Environment.CurrentManagedThreadId,
            Screen = Shell.Current?.CurrentState?.Location?.ToString(),
            ElementPath = elementPath,
            TagsJson = JsonSerializer.Serialize(new
            {
                source = sourceName,
                events = state.EventCount,
                startOffsetX = state.StartOffsetX,
                startOffsetY = state.StartOffsetY,
                endOffsetX = state.LastOffsetX,
                endOffsetY = state.LastOffsetY,
                deltaX,
                deltaY,
                startFirstVisibleIndex = state.StartFirstVisibleIndex,
                startLastVisibleIndex = state.StartLastVisibleIndex,
                endFirstVisibleIndex = state.LastFirstVisibleIndex,
                endLastVisibleIndex = state.LastLastVisibleIndex,
                visibleShift
            })
        });
    }

    private static int? ComputeVisibleShift(ScrollBatchState state)
    {
        if (!state.StartFirstVisibleIndex.HasValue || !state.LastFirstVisibleIndex.HasValue)
            return null;

        return Math.Abs(state.LastFirstVisibleIndex.Value - state.StartFirstVisibleIndex.Value);
    }

    private void OnTrackedElementMeasureInvalidated(object? sender, EventArgs args)
    {
        if (sender is not VisualElement element)
            return;

        var state = _elementRenderStates.GetOrCreateValue(element);
        state.MeasureInvalidatedCount++;
    }

    private void OnTrackedElementSizeChanged(object? sender, EventArgs args)
    {
        if (sender is not VisualElement element)
            return;

        var state = _elementRenderStates.GetOrCreateValue(element);
        state.SizeChangedCount++;
        if (state.FirstLayoutPublished || element.Width <= 0 || element.Height <= 0)
            return;

        if (state.TrackingStartedAtUtc == default)
            state.TrackingStartedAtUtc = DateTime.UtcNow;

        state.FirstLayoutPublished = true;
        PublishUiOperationSpan(
            "ui.render.first-layout",
            state.TrackingStartedAtUtc,
            true,
            null,
            BuildElementPath(element),
            new
            {
                role = state.Role,
                viewType = element.GetType().Name,
                width = element.Width,
                height = element.Height,
                sizeChangedCount = state.SizeChangedCount,
                measureInvalidatedCount = state.MeasureInvalidatedCount
            });
    }

    private void OnTapGestureTapped(object? sender, TappedEventArgs args)
    {
        var parameter = args.Parameter?.ToString();
        TrackUiInteraction("ui.input.tap-gesture", sender as Element, new { parameter });
    }

    private void OnPageAppearing(object? sender, EventArgs args)
    {
        if (sender is not Page page)
            return;

        var now = DateTime.UtcNow;
        var route = GetCurrentRouteLocation();
        var state = _pageLifecycleStates.GetOrCreateValue(page);
        state.AppearingAtUtc = now;
        state.Route = route;
        state.FirstLayoutPublished = false;
        state.SizeChangedCount = 0;
        state.MeasureInvalidatedCount = 0;

        TrackUiInteraction("ui.page.appearing", page, new { route, page = page.GetType().Name });
        TryPublishNavigationToAppearing(page, route);
    }

    private void OnPageDisappearing(object? sender, EventArgs args)
    {
        if (sender is not Page page)
            return;

        var route = Shell.Current?.CurrentState?.Location?.ToString();
        TrackUiInteraction("ui.page.disappearing", page, new { route, page = page.GetType().Name });
    }

    private void OnPageMeasureInvalidated(object? sender, EventArgs args)
    {
        if (sender is not Page page)
            return;

        var state = _pageLifecycleStates.GetOrCreateValue(page);
        state.MeasureInvalidatedCount++;
    }

    private void OnPageSizeChanged(object? sender, EventArgs args)
    {
        if (sender is not Page page)
            return;

        var now = DateTime.UtcNow;
        var state = _pageLifecycleStates.GetOrCreateValue(page);
        state.SizeChangedCount++;
        if (state.FirstLayoutPublished || page.Width <= 0 || page.Height <= 0)
            return;

        state.FirstLayoutPublished = true;
        var startTsUtc = state.AppearingAtUtc == default ? now : state.AppearingAtUtc;
        var route = state.Route ?? Shell.Current?.CurrentState?.Location?.ToString();

        PublishUiOperationSpan(
            "ui.page.first-layout",
            startTsUtc,
            true,
            null,
            BuildElementPath(page),
            new
            {
                route,
                page = page.GetType().Name,
                width = page.Width,
                height = page.Height,
                sizeChangedCount = state.SizeChangedCount,
                measureInvalidatedCount = state.MeasureInvalidatedCount
            });

        TryPublishNavigationToFirstLayout(page, route);
    }

    private void OnShellNavigating(object? sender, ShellNavigatingEventArgs args)
    {
        var startedAtUtc = DateTime.UtcNow;
        var targetRoute = TryReadNavigationRoute(args, "Target")
            ?? Shell.Current?.CurrentState?.Location?.ToString()
            ?? "unknown";

        lock (_uiHookGate)
        {
            _navigationStartedAtUtc = startedAtUtc;
            _navigationTargetRoute = targetRoute;
        }
        RememberUserAction("navigation.start", targetRoute, startedAtUtc);

        Publish(new ProfilerMarker
        {
            TsUtc = startedAtUtc,
            Type = "navigation.start",
            Name = targetRoute,
            PayloadJson = JsonSerializer.Serialize(new { route = targetRoute })
        });
    }

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs args)
    {
        var endedAtUtc = DateTime.UtcNow;
        DateTime startedAtUtc;
        string route;

        lock (_uiHookGate)
        {
            startedAtUtc = _navigationStartedAtUtc ?? endedAtUtc;
            route = _navigationTargetRoute
                ?? TryReadNavigationRoute(args, "Current")
                ?? Shell.Current?.CurrentState?.Location?.ToString()
                ?? "unknown";
        }

        var source = TryReadNavigationSource(args) ?? "unknown";
        var currentPage = Shell.Current?.CurrentPage?.GetType().Name;

        Publish(new ProfilerMarker
        {
            TsUtc = endedAtUtc,
            Type = "navigation.end",
            Name = route,
            PayloadJson = JsonSerializer.Serialize(new { route, source, page = currentPage })
        });
        RememberUserAction("navigation.route", route, endedAtUtc);
        PublishUiEvent("navigation", new
        {
            from = (string?)null,
            to = route,
            route,
            timestamp = endedAtUtc.ToString("O")
        });

        PublishUiOperationSpan(
            "navigation.shell.completed",
            startedAtUtc,
            true,
            null,
            route,
            new { route, source, page = currentPage });
    }

    private void TryPublishNavigationToAppearing(Page page, string? route)
    {
        DateTime? navigationStartedAtUtc;
        string? navigationRoute;
        lock (_uiHookGate)
        {
            navigationStartedAtUtc = _navigationStartedAtUtc;
            navigationRoute = _navigationTargetRoute;
        }

        if (!navigationStartedAtUtc.HasValue)
            return;

        PublishUiOperationSpan(
            "navigation.to-page-appearing",
            navigationStartedAtUtc.Value,
            true,
            null,
            BuildElementPath(page),
            new
            {
                targetRoute = navigationRoute,
                currentRoute = route,
                page = page.GetType().Name
            });
    }

    private void TryPublishNavigationToFirstLayout(Page page, string? route)
    {
        DateTime? navigationStartedAtUtc;
        string? navigationRoute;
        lock (_uiHookGate)
        {
            navigationStartedAtUtc = _navigationStartedAtUtc;
            navigationRoute = _navigationTargetRoute;
            _navigationStartedAtUtc = null;
            _navigationTargetRoute = null;
        }

        if (!navigationStartedAtUtc.HasValue)
            return;

        PublishUiOperationSpan(
            "navigation.to-first-layout",
            navigationStartedAtUtc.Value,
            true,
            null,
            BuildElementPath(page),
            new
            {
                targetRoute = navigationRoute,
                currentRoute = route,
                page = page.GetType().Name
            });
    }

    private void TrackUiInteraction(string name, Element? element, object? tags = null)
    {
        if (!IsProfilerFeatureAvailable || !_profilerSessions.IsActive)
            return;

        var startedAtUtc = DateTime.UtcNow;
        var elementPath = BuildElementPath(element);
        var markerPayload = JsonSerializer.Serialize(new
        {
            name,
            elementPath,
            tags
        });

        Publish(new ProfilerMarker
        {
            TsUtc = startedAtUtc,
            Type = "user.action",
            Name = name,
            PayloadJson = markerPayload
        });

        RememberUserAction(name, elementPath, startedAtUtc);

        if (_dispatcher != null)
        {
            _dispatcher.DispatchDelayed(
                TimeSpan.FromMilliseconds(1),
                () => PublishUiOperationSpan(name, startedAtUtc, true, null, elementPath, tags));
            return;
        }

        PublishUiOperationSpan(name, startedAtUtc, true, null, elementPath, tags);
    }

    private static string? BuildElementPath(Element? element)
    {
        if (element == null)
            return null;

        if (!string.IsNullOrWhiteSpace(element.AutomationId))
            return $"{element.GetType().Name}#{element.AutomationId}";
        if (element is Page page && !string.IsNullOrWhiteSpace(page.Title))
            return $"{page.GetType().Name}:{page.Title}";
        if (element is VisualElement visualElement && !string.IsNullOrWhiteSpace(visualElement.StyleId))
            return $"{visualElement.GetType().Name}[{visualElement.StyleId}]";

        return element.GetType().Name;
    }

    protected override async Task<HttpResponse?> TryCaptureRegisteredWebViewAsync(CdpWebViewInfo webView)
    {
        if (_app == null)
            return null;

        try
        {
            var element = await DispatchAsync(() =>
            {
                if (!string.IsNullOrWhiteSpace(webView.ElementId))
                {
                    var byId = _treeWalker.GetElementById(webView.ElementId!, _app) as VisualElement;
                    if (byId != null)
                        return byId;
                }

                if (!string.IsNullOrWhiteSpace(webView.AutomationId))
                {
                    var match = _treeWalker.Query(_app, automationId: webView.AutomationId).FirstOrDefault();
                    if (match?.Id is { Length: > 0 } matchId)
                        return _treeWalker.GetElementById(matchId, _app) as VisualElement;
                }

                return null;
            });

            if (element == null)
                return null;

            var pngData = await DispatchAsync(() => CaptureElementScreenshotAsync(element));
            return pngData is { Length: > 0 }
                ? HttpResponse.Png(pngData)
                : null;
        }
        catch
        {
            return null;
        }
    }

    protected override async Task<HttpResponse> HandlePlatformAppInfo(HttpRequest request)
    {
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var info = AppInfo.Current;
                var theme = BuildThemeInfoPayload(Application.Current ?? _app);
                return HttpResponse.Json(new
                {
                    name = info.Name,
                    packageName = info.PackageName,
                    version = info.VersionString,
                    buildNumber = info.BuildString,
                    theme = theme.Theme,
                    requestedTheme = info.RequestedTheme.ToString(),
                    requestedThemeValue = theme.RequestedTheme,
                    userAppTheme = theme.UserAppTheme,
                    effectiveTheme = theme.EffectiveTheme,
                    requestedLayoutDirection = info.RequestedLayoutDirection.ToString(),
                });
            });
        }
        catch (Exception ex)
        {
            return CreatePlatformError($"Failed to get app info: {ex.Message}", ex);
        }
    }

    protected override async Task<HttpResponse> HandleThemeGet(HttpRequest request)
    {
        try
        {
            var app = Application.Current ?? _app;
            if (app == null)
                return HttpResponse.Error("Agent not bound to app", reason: "agent-not-bound");

            var theme = await DispatchAsync(() => BuildThemeInfoPayload(app));
            return HttpResponse.Json(theme);
        }
        catch (Exception ex)
        {
            return CreatePlatformError($"Failed to get app theme: {ex.Message}", ex);
        }
    }

    protected override async Task<HttpResponse> HandleThemeSet(HttpRequest request)
    {
        var body = request.BodyAs<ThemeSetRequest>();
        if (string.IsNullOrWhiteSpace(body?.Theme))
            return HttpResponse.Error("theme is required", reason: "invalid-argument");

        if (!TryParseTheme(body.Theme, out var appTheme))
        {
            return HttpResponse.Error(
                $"Theme '{body.Theme}' is not supported. Use light, dark, or system.",
                reason: "invalid-argument",
                details: new { supportedThemes = SupportedThemeNames });
        }

        try
        {
            var app = Application.Current ?? _app;
            if (app == null)
                return HttpResponse.Error("Agent not bound to app", reason: "agent-not-bound");

            var theme = await DispatchAsync(() =>
            {
                app.UserAppTheme = appTheme;
                return BuildThemeInfoPayload(app);
            });

            PublishUiEvent("themeChange", new
            {
                theme = theme.Theme,
                requestedTheme = theme.RequestedTheme,
                userAppTheme = theme.UserAppTheme,
                effectiveTheme = theme.EffectiveTheme,
                source = theme.Source,
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            });

            return HttpResponse.Json(theme);
        }
        catch (Exception ex)
        {
            return CreatePlatformError($"Failed to set app theme: {ex.Message}", ex);
        }
    }

    private static ThemeInfoPayload BuildThemeInfoPayload(Application? app, string source = "app", string? message = null)
    {
        var requestedTheme = SafeGetRequestedTheme(app);
        var userAppTheme = SafeGetUserAppTheme(app);
        var effectiveTheme = userAppTheme == AppTheme.Unspecified ? requestedTheme : userAppTheme;

        return new ThemeInfoPayload(
            ThemeToProtocolString(effectiveTheme),
            ThemeToProtocolString(requestedTheme),
            ThemeToProtocolString(userAppTheme),
            ThemeToProtocolString(effectiveTheme),
            SupportedThemeNames,
            source,
            message);
    }

    private static AppTheme SafeGetRequestedTheme(Application? app)
    {
        if (app == null)
            return AppTheme.Unspecified;

        try
        {
            return app.RequestedTheme;
        }
        catch
        {
            return AppTheme.Unspecified;
        }
    }

    private static AppTheme SafeGetUserAppTheme(Application? app)
    {
        if (app == null)
            return AppTheme.Unspecified;

        try
        {
            return app.UserAppTheme;
        }
        catch
        {
            return AppTheme.Unspecified;
        }
    }

    private static bool TryParseTheme(string value, out AppTheme theme)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "light":
                theme = AppTheme.Light;
                return true;
            case "dark":
                theme = AppTheme.Dark;
                return true;
            case "system":
            case "default":
            case "unspecified":
            case "unset":
                theme = AppTheme.Unspecified;
                return true;
            default:
                theme = AppTheme.Unspecified;
                return false;
        }
    }

    private static string ThemeToProtocolString(AppTheme theme) => theme switch
    {
        AppTheme.Light => "light",
        AppTheme.Dark => "dark",
        _ => "system"
    };

    private sealed record ThemeInfoPayload(
        [property: JsonPropertyName("theme")] string Theme,
        [property: JsonPropertyName("requestedTheme")] string RequestedTheme,
        [property: JsonPropertyName("userAppTheme")] string UserAppTheme,
        [property: JsonPropertyName("effectiveTheme")] string EffectiveTheme,
        [property: JsonPropertyName("supportedThemes")] IReadOnlyList<string> SupportedThemes,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("message")] string? Message);

    // ── Essentials-backed endpoints ──
    // Implementations live in the shared EssentialsAgentSupport so the optional
    // add-on for plain .NET apps can reuse them verbatim.

    protected override Task<HttpResponse> HandlePreferencesList(HttpRequest request)
        => _essentials.HandlePreferencesList(request);

    protected override Task<HttpResponse> HandlePreferencesGet(HttpRequest request)
        => _essentials.HandlePreferencesGet(request);

    protected override Task<HttpResponse> HandlePreferencesSet(HttpRequest request)
        => _essentials.HandlePreferencesSet(request);

    protected override Task<HttpResponse> HandlePreferencesDelete(HttpRequest request)
        => _essentials.HandlePreferencesDelete(request);

    protected override Task<HttpResponse> HandlePreferencesClear(HttpRequest request)
        => _essentials.HandlePreferencesClear(request);

    protected override Task<HttpResponse> HandleSecureStorageGet(HttpRequest request)
        => _essentials.HandleSecureStorageGet(request);

    protected override Task<HttpResponse> HandleSecureStorageSet(HttpRequest request)
        => _essentials.HandleSecureStorageSet(request);

    protected override Task<HttpResponse> HandleSecureStorageDelete(HttpRequest request)
        => _essentials.HandleSecureStorageDelete(request);

    protected override Task<HttpResponse> HandleSecureStorageClear(HttpRequest request)
        => _essentials.HandleSecureStorageClear(request);

    protected override string GetAppDataBasePath()
        => _essentials.GetAppDataBasePath();

    protected override Task<HttpResponse> HandlePlatformDeviceInfo(HttpRequest request)
        => _essentials.HandlePlatformDeviceInfo(request);

    protected override Task<HttpResponse> HandlePlatformDeviceDisplay(HttpRequest request)
        => _essentials.HandlePlatformDeviceDisplay(request);

    protected override Task<HttpResponse> HandlePlatformBattery(HttpRequest request)
        => _essentials.HandlePlatformBattery(request);

    protected override Task<HttpResponse> HandlePlatformConnectivity(HttpRequest request)
        => _essentials.HandlePlatformConnectivity(request);

    protected override Task<HttpResponse> HandlePlatformVersionTracking(HttpRequest request)
        => _essentials.HandlePlatformVersionTracking(request);

    protected override Task<HttpResponse> HandlePlatformPermissions(HttpRequest request)
        => _essentials.HandlePlatformPermissions(request);

    protected override Task<HttpResponse> HandlePlatformPermissionCheck(HttpRequest request)
        => _essentials.HandlePlatformPermissionCheck(request);

    protected override Task<HttpResponse> HandlePlatformGeolocation(HttpRequest request)
        => _essentials.HandlePlatformGeolocation(request);

    protected override Task<HttpResponse> HandleSensorsList(HttpRequest request)
        => _essentials.HandleSensorsList(request);

    protected override Task<HttpResponse> HandleSensorStart(HttpRequest request)
        => _essentials.HandleSensorStart(request);

    protected override Task<HttpResponse> HandleSensorStop(HttpRequest request)
        => _essentials.HandleSensorStop(request);

    protected override Task HandleSensorWebSocket(System.Net.Sockets.TcpClient client, System.Net.Sockets.NetworkStream stream, HttpRequest request, CancellationToken ct)
        => _essentials.HandleSensorWebSocket(client, stream, request, ct);
}
