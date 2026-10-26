using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui;

namespace Microsoft.Maui.DevFlow.Blazor.Gtk;

/// <summary>
/// Blazor WebView debug service for WebKitGTK on Linux.
/// Captures WebKit.WebViews from BlazorWebViewHandlers and provides
/// CDP command handling via Chobitsu.js injection and JS evaluation.
/// Supports multiple WebViews per app.
/// </summary>
public class GtkBlazorWebViewDebugService : IDisposable
{
    private readonly List<GtkWebViewBridge> _bridges = new();
    private readonly object _sync = new();
    private readonly Dictionary<GtkWebViewBridge, int> _registrations = new();
    private DevFlowAgentService? _agent;
    private bool _disposed;
    private readonly Dictionary<Microsoft.Maui.IElementHandler, GtkWebViewBridge> _capturedHandlers = new();
    private static int _handlerCaptureConfigured;

    public Action<string>? LogCallback { get; set; }
    public Action<string, string, string?>? WebViewLogCallback { get; set; }

    public bool IsReady => Bridges.FirstOrDefault()?.IsReady == true;
    public IReadOnlyList<GtkWebViewBridge> Bridges
    {
        get { lock (_sync) return _bridges.ToArray(); }
    }

    internal void WireToAgent(DevFlowAgentService agent)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (ReferenceEquals(_agent, agent)) return;
            if (_agent != null)
                throw new InvalidOperationException("The GTK Blazor service is already wired to another agent.");
            _agent = agent;
            WebViewLogCallback = (level, message, exception) =>
                agent.WriteWebViewLog(level, "WebView.Console", message, exception);
            foreach (var bridge in _bridges)
                RegisterBridge(bridge);
        }
    }

    internal void AddBridge(GtkWebViewBridge bridge)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_bridges.Contains(bridge)) return;
            _bridges.Add(bridge);
            RegisterBridge(bridge);
        }
    }

    private void RegisterBridge(GtkWebViewBridge bridge)
    {
        if (_agent != null && !_registrations.ContainsKey(bridge))
        {
            var index = _agent.RegisterCdpWebView(
                bridge.SendCdpCommandAsync, () => bridge.IsReady, bridge.AutomationId, bridge.ElementId);
            foreach (var previous in _registrations.Where(entry => entry.Value == index).Select(entry => entry.Key).ToArray())
                _registrations.Remove(previous);
            _registrations.Add(bridge, index);
        }
    }

    internal static void ConfigureHandlerCapture()
    {
        if (Interlocked.Exchange(ref _handlerCaptureConfigured, 1) != 0)
            return;

        Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView.BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping(
            "DevFlowCapture",
            (handler, view) => handler.MauiContext?.Services
                .GetService<GtkBlazorWebViewDebugService>()?.CaptureWebView(handler, view));
    }

    private void CaptureWebView(Microsoft.Maui.IElementHandler handler, IBlazorWebView view)
    {
        if (view is not Microsoft.Maui.Controls.View control)
            throw new InvalidOperationException("The GTK Blazor view must be a MAUI View.");

        var webView = ExtractWebKitWebView(control)
            ?? throw new InvalidOperationException("The GTK Blazor handler did not expose a WebKit WebView.");
        GtkWebViewBridge bridge;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_capturedHandlers.ContainsKey(handler))
                return;

            bridge = new GtkWebViewBridge(this, webView, control.AutomationId);
            EventHandler<Microsoft.Maui.Controls.HandlerChangingEventArgs>? changing = null;
            changing = (_, args) =>
            {
                if (ReferenceEquals(args.OldHandler, handler))
                    RemoveBridge(bridge);
            };
            bridge.LifetimeCleanup = () =>
            {
                control.HandlerChanging -= changing;
                lock (_sync) _capturedHandlers.Remove(handler);
            };
            control.HandlerChanging += changing;
            _capturedHandlers.Add(handler, bridge);
            AddBridge(bridge);
        }
        _ = bridge.InitializeAsync();
    }

    internal void RemoveBridge(GtkWebViewBridge bridge)
    {
        lock (_sync)
        {
            if (!_bridges.Remove(bridge))
                return;
            if (_registrations.Remove(bridge, out var index))
                _agent?.UnregisterCdpWebView(index);
        }
        bridge.Dispose();
    }

    /// <summary>
    /// Per-WebView bridge encapsulating CDP state and WebKit.WebView reference.
    /// </summary>
    public class GtkWebViewBridge : IDisposable
    {
        private readonly GtkBlazorWebViewDebugService _owner;
        private readonly object _lifetimeSync = new();
        private readonly CancellationTokenSource _lifetimeCts = new();
        private readonly CancellationToken _lifetimeToken;
        private readonly SemaphoreSlim _initializationGate = new(1, 1);
        private readonly Func<string, Task<string?>>? _evaluateJavaScript;
        private readonly Func<CancellationToken, Task<byte[]>>? _captureScreenshot;
        private global::WebKit.WebView? _webView;
        private bool _isInitialized;
        private bool _chobitsuLoaded;
        private CancellationTokenSource? _drainCts;
        private bool _disposed;
        private int _cdpIdCounter;
        private int _documentRevision;

        public string? AutomationId { get; }
        public string? ElementId { get; set; }
        public bool IsReady => !_disposed && _isInitialized && (_webView != null || _evaluateJavaScript != null) && _chobitsuLoaded;
        internal Action? LifetimeCleanup { get; set; }
        internal Task? LogDrainTask { get; private set; }

        internal GtkWebViewBridge(GtkBlazorWebViewDebugService owner, global::WebKit.WebView webView,
            string? automationId, Func<string, Task<string?>>? evaluateJavaScript = null,
            Func<CancellationToken, Task<byte[]>>? captureScreenshot = null)
        {
            _owner = owner;
            _webView = webView;
            AutomationId = automationId;
            _evaluateJavaScript = evaluateJavaScript;
            _captureScreenshot = captureScreenshot;
            _lifetimeToken = _lifetimeCts.Token;
            if (_webView != null)
                _webView.OnLoadChanged += OnLoadChanged;
        }

        internal async Task InitializeAsync()
        {
            try
            {
                await _initializationGate.WaitAsync(_lifetimeToken);
                try
                {
                    if (_disposed)
                        return;
                    _isInitialized = true;
                    var documentRevision = Volatile.Read(ref _documentRevision);
                    if (_webView != null && await DispatchOnUiAsync(() =>
                        _webView.IsLoading || string.IsNullOrEmpty(_webView.GetUri())))
                        return;
                    if (documentRevision != Volatile.Read(ref _documentRevision))
                        return;
                    _owner.Log("[BlazorDevFlow.Gtk] Injecting debug script into the loaded document...");
                    await InjectDebugScriptAsync(documentRevision);
                }
                finally
                {
                    _initializationGate.Release();
                }
            }
            catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _chobitsuLoaded = false;
                _owner.Log($"[BlazorDevFlow.Gtk] Bridge initialization failed: {ex}");
            }
        }

        private void OnLoadChanged(global::WebKit.WebView sender, global::WebKit.WebView.LoadChangedSignalArgs args)
        {
            if (_disposed)
                return;
            if (args.LoadEvent == global::WebKit.LoadEvent.Started)
            {
                Interlocked.Increment(ref _documentRevision);
                _chobitsuLoaded = false;
                lock (_lifetimeSync)
                    _drainCts?.Cancel();
            }
            else if (args.LoadEvent == global::WebKit.LoadEvent.Finished)
            {
                _ = InitializeAsync();
            }
        }

        internal async Task<string?> EvaluateJavaScriptAsync(string script)
        {
            if (_disposed || (_webView == null && _evaluateJavaScript == null)) return null;

            try
            {
                var evaluation = _evaluateJavaScript != null ? _evaluateJavaScript(script) : DispatchOnUiAsync(async () =>
                {
                    if (_webView == null) return null;
                    var value = await _webView.EvaluateJavascriptAsync(script);
                    return value?.ToString();
                });
                return await evaluation.WaitAsync(_lifetimeToken);
            }
            catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                _owner.Log($"[BlazorDevFlow.Gtk] JS eval error: {ex}");
                return null;
            }
        }

        private async Task InjectDebugScriptAsync(int documentRevision)
        {
            try
            {
                if (_disposed || (_webView == null && _evaluateJavaScript == null))
                {
                    _owner.Log("[BlazorDevFlow.Gtk] WebView is null");
                    return;
                }

                var securityShim = @"
                    (function () {
                        function installStorageShim(name) {
                            try { void window[name]; }
                            catch (_) {
                                try {
                                    Object.defineProperty(window, name, {
                                        configurable: true,
                                        get: function () {
                                            return {
                                                getItem: function () { return null; },
                                                setItem: function () {},
                                                removeItem: function () {},
                                                clear: function () {}
                                            };
                                        }
                                    });
                                } catch (_) {}
                            }
                        }

                        installStorageShim('localStorage');
                        installStorageShim('sessionStorage');

                        try { void document.cookie; }
                        catch (_) {
                            try {
                                Object.defineProperty(Document.prototype, 'cookie', {
                                    configurable: true,
                                    get: function () { return ''; },
                                    set: function () {}
                                });
                            } catch (_) {}
                        }
                    })();
                ";
                await EvaluateJavaScriptAsync(securityShim);

                var check = await EvaluateJavaScriptAsync(
                    "typeof chobitsu !== 'undefined' ? 'loaded' : 'waiting'");

                if (check != "loaded")
                {
                    _owner.Log("[BlazorDevFlow.Gtk] Injecting chobitsu.js from embedded resource...");
                    var chobitsuJs = ScriptResources.Load("chobitsu.js");
                    await EvaluateJavaScriptAsync(chobitsuJs);

                    for (int i = 0; i < 20; i++)
                    {
                        check = await EvaluateJavaScriptAsync(
                            "typeof chobitsu !== 'undefined' ? 'loaded' : 'waiting'");
                        if (check == "loaded") break;
                        await Task.Delay(250, _lifetimeToken);
                    }

                    if (check != "loaded")
                    {
                        _owner.Log("[BlazorDevFlow.Gtk] Chobitsu failed to load after injection");
                        return;
                    }
                }

                var script = ScriptResources.Load("chobitsu-init.js");
                _owner.Log($"[BlazorDevFlow.Gtk] Injecting init script ({script.Length} chars)...");

                var result = await EvaluateJavaScriptAsync(script);
                _owner.Log($"[BlazorDevFlow.Gtk] Script injection result: {result ?? "null"}");
                if (documentRevision != Volatile.Read(ref _documentRevision) || _disposed)
                    return;
                _chobitsuLoaded = true;

                var consoleScript = ScriptResources.Load("console-intercept.js");
                await EvaluateJavaScriptAsync(consoleScript);
                if (documentRevision == Volatile.Read(ref _documentRevision) && !_disposed)
                    StartLogDrain();
            }
            catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _owner.Log($"[BlazorDevFlow.Gtk] Failed to inject script: {ex.Message}");
            }
        }

        public async Task<string> SendCdpCommandAsync(string cdpJson)
        {
            if (!IsReady)
                return "{\"error\":\"WebView not ready\"}";

            try
            {
                cdpJson = EnsureCommandId(cdpJson, ref _cdpIdCounter);
                using var json = System.Text.Json.JsonDocument.Parse(cdpJson);
                var id = json.RootElement.GetProperty("id").GetInt32();
                var method = json.RootElement.TryGetProperty("method", out var methodProp) ? methodProp.GetString() ?? "" : "";

                if (method == "Input.insertText")
                    return await HandleInputInsertTextAsync(cdpJson, id);
                if (method == "Page.reload")
                    return await HandlePageReloadAsync(id);
                if (method == "Page.navigate")
                    return await HandlePageNavigateAsync(cdpJson, id);
                if (method == "Page.captureScreenshot")
                    return await HandlePageCaptureScreenshotAsync(json.RootElement, id);
                if (method.StartsWith("Browser."))
                    return HandleBrowserMethod(method, id);

                var escaped = cdpJson.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
                var sendScript = ScriptResources.Load("cdp-send-receive.js")
                    .Replace("%CDP_MESSAGE%", escaped);
                var readScript = ScriptResources.Load("cdp-read-response.js");

                await EvaluateJavaScriptAsync(sendScript);
                await Task.Delay(50);

                for (int i = 0; i < 60; i++)
                {
                    var result = await EvaluateJavaScriptAsync(readScript);
                    var unescaped = UnescapeEvalResult(result);
                    if (unescaped != null)
                        return unescaped;
                    await Task.Delay(50);
                }

                return "{\"error\":\"cdp timeout\"}";
            }
            catch (Exception ex)
            {
                return $"{{\"error\":\"{EscapeJsonString(ex.Message)}\"}}";
            }
        }

        internal static string EnsureCommandId(string cdpJson, ref int sequence)
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(cdpJson) is not System.Text.Json.Nodes.JsonObject command)
                throw new System.Text.Json.JsonException("A CDP command must be a JSON object.");
            if (!command.ContainsKey("id"))
                command["id"] = Interlocked.Increment(ref sequence);
            return command.ToJsonString();
        }

        private static string HandleBrowserMethod(string method, int id)
        {
            if (method == "Browser.getVersion")
                return new System.Text.Json.Nodes.JsonObject
                {
                    ["id"] = id,
                    ["result"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["protocolVersion"] = "1.3",
                        ["product"] = "MAUI Blazor WebView (WebKitGTK)/1.0",
                        ["userAgent"] = "Microsoft.Maui.DevFlow.Gtk",
                        ["jsVersion"] = ""
                    }
                }.ToJsonString();
            return $"{{\"id\":{id},\"result\":{{}}}}";
        }

        private async Task<string> HandleInputInsertTextAsync(string cdpJson, int id)
        {
            var json = System.Text.Json.JsonDocument.Parse(cdpJson);
            var text = json.RootElement.GetProperty("params").GetProperty("text").GetString() ?? "";
            var escapedText = EscapeJsString(text);

            var script = ScriptResources.Load("insert-text.js")
                .Replace("%TEXT%", escapedText)
                .Replace("%TEXT_LENGTH%", text.Length.ToString());
            await EvaluateJavaScriptAsync(script);
            return $"{{\"id\":{id},\"result\":{{}}}}";
        }

        private async Task<string> HandlePageReloadAsync(int id)
        {
            await DispatchOnUiAsync(() => _webView?.Reload());
            await Task.Delay(1500);
            await InitializeAsync();
            return $"{{\"id\":{id},\"result\":{{}}}}";
        }

        private async Task<string> HandlePageCaptureScreenshotAsync(System.Text.Json.JsonElement command, int id)
        {
            if (command.TryGetProperty("params", out var parameters))
            {
                if (parameters.TryGetProperty("format", out var format) && format.GetString() != "png")
                    throw new NotSupportedException("GTK WebView screenshots support PNG only.");
                if (parameters.TryGetProperty("clip", out _) ||
                    (parameters.TryGetProperty("captureBeyondViewport", out var beyondViewport) && beyondViewport.GetBoolean()))
                    throw new NotSupportedException("GTK WebView screenshots capture the visible viewport only.");
            }

            try
            {
                // The bridge owns the exact native WebView for this CDP context.
                // A MAUI wrapper's cached GTK render node need not contain the
                // web process's current pixels; WebKit completes this asynchronously.
                var png = _captureScreenshot != null
                    ? await _captureScreenshot(_lifetimeToken).WaitAsync(_lifetimeToken)
                    : await DispatchOnUiAsync<byte[]>(async () => await WebKitSnapshot.CaptureAsync(
                        _webView ?? throw new InvalidOperationException("The WebView was detached before capture."),
                        _lifetimeToken)).WaitAsync(_lifetimeToken);
                _lifetimeToken.ThrowIfCancellationRequested();
                if (png is not { Length: > 0 })
                    throw new InvalidOperationException("WebKit returned no screenshot PNG data.");

                return new System.Text.Json.Nodes.JsonObject
                {
                    ["id"] = id,
                    ["result"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["data"] = Convert.ToBase64String(png)
                    }
                }.ToJsonString();
            }
            catch (Exception ex)
            {
                _owner.Log($"[BlazorDevFlow.Gtk] Native WebView screenshot failed: {ex.Message}");
                throw;
            }
        }

        private async Task<string> HandlePageNavigateAsync(string cdpJson, int id)
        {
            var json = System.Text.Json.JsonDocument.Parse(cdpJson);
            var url = json.RootElement.GetProperty("params").GetProperty("url").GetString() ?? "";
            if (Microsoft.Maui.DevFlow.Blazor.WebViewNavigation.IsAbsoluteUrl(url))
                await DispatchOnUiAsync(() => _webView?.LoadUri(url));
            else
                await EvaluateJavaScriptAsync(Microsoft.Maui.DevFlow.Blazor.WebViewNavigation.CreateRelativeNavigationScript(url));
            await Task.Delay(1500);
            await InitializeAsync();
            return $"{{\"id\":{id},\"result\":{{\"frameId\":\"main\"}}}}";
        }

        private void StartLogDrain()
        {
            lock (_lifetimeSync)
            {
                if (_disposed) return;
                _drainCts?.Cancel();
                _drainCts?.Dispose();
                _drainCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
                var ct = _drainCts.Token;

                LogDrainTask = Task.Run(async () =>
                {
                    var drainScript = ScriptResources.Load("drain-console-logs.js");
                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(2000, ct);
                            if (!IsReady || _owner.WebViewLogCallback == null) continue;

                            var raw = await EvaluateJavaScriptAsync(drainScript);
                            var json = UnescapeEvalResult(raw);
                            if (string.IsNullOrEmpty(json) || json == "null") continue;

                            using var doc = System.Text.Json.JsonDocument.Parse(json);
                            foreach (var entry in doc.RootElement.EnumerateArray())
                            {
                                var jsLevel = entry.GetProperty("l").GetString() ?? "log";
                                var message = entry.GetProperty("m").GetString() ?? "";
                                var exception = entry.TryGetProperty("e", out var eProp) ? eProp.GetString() : null;

                                var level = jsLevel switch
                                {
                                    "error" => "Error",
                                    "warn" => "Warning",
                                    "debug" => "Debug",
                                    "info" => "Information",
                                    _ => "Information"
                                };
                                _owner.WebViewLogCallback(level, message, exception);
                            }
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                        catch (Exception ex)
                        {
                            _owner.Log($"[BlazorDevFlow.Gtk] Log drain failed: {ex}");
                        }
                    }
                }, ct);
            }
        }

        public void Dispose()
        {
            Action? cleanup;
            lock (_lifetimeSync)
            {
                if (_disposed) return;
                _disposed = true;
                if (_webView != null)
                    _webView.OnLoadChanged -= OnLoadChanged;
                _lifetimeCts.Cancel();
                _lifetimeCts.Dispose();
                _drainCts?.Cancel();
                _drainCts?.Dispose();
                _drainCts = null;
                _webView = null;
                cleanup = LifetimeCleanup;
                LifetimeCleanup = null;
            }
            cleanup?.Invoke();
        }
    }

    /// <summary>
    /// Captures existing WebViews and observes future handler creation for the app lifetime.
    /// </summary>
    public void StartWebViewDiscovery()
    {
        ConfigureHandlerCapture();
        void CaptureExisting()
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var app = Microsoft.Maui.Controls.Application.Current;
                if (app is null)
                    return;
                foreach (var window in app.Windows)
                {
                    if (window.Page is Microsoft.Maui.IVisualTreeElement root)
                        CaptureChildren(root);
                }
            }
        }

        var dispatcher = Microsoft.Maui.Controls.Application.Current?.Dispatcher;
        if (dispatcher?.IsDispatchRequired == true)
        {
            if (!dispatcher.Dispatch(CaptureExisting))
                throw new InvalidOperationException("Unable to dispatch existing GTK WebView capture.");
        }
        else
            CaptureExisting();

        void CaptureChildren(Microsoft.Maui.IVisualTreeElement element)
        {
            if (element is IBlazorWebView view && element is Microsoft.Maui.Controls.View control &&
                control.Handler is Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView.BlazorWebViewHandler handler)
                CaptureWebView(handler, view);
            foreach (var child in element.GetVisualChildren())
                CaptureChildren(child);
        }
    }

    private static global::WebKit.WebView? ExtractWebKitWebView(Microsoft.Maui.Controls.View view)
    {
        try
        {
            var handler = view.Handler;
            if (handler == null) return null;

            var platformView = handler.PlatformView;
            if (platformView == null) return null;

            if (platformView is global::Gtk.Box box)
            {
                var child = box.GetFirstChild();
                while (child != null)
                {
                    if (child is global::WebKit.WebView webView)
                        return webView;
                    child = child.GetNextSibling();
                }
            }

            if (platformView is global::WebKit.WebView directWebView)
                return directWebView;
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Backward-compatible: sends CDP command to the first WebView bridge.
    /// </summary>
    public Task<string> SendCdpCommandAsync(string cdpJson)
    {
        var bridge = Bridges.FirstOrDefault();
        if (bridge == null)
            return Task.FromResult("{\"error\":\"No WebViews available\"}");
        return bridge.SendCdpCommandAsync(cdpJson);
    }

    private void Log(string message)
    {
        LogCallback?.Invoke(message);
    }

    private static string? UnescapeEvalResult(string? result)
    {
        if (string.IsNullOrEmpty(result)) return null;
        if (result.StartsWith("\"") && result.EndsWith("\""))
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(result);
                return document.RootElement.GetString();
            }
            catch { }
        }
        return result;
    }

    private static string EscapeJsString(string s)
        => s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"")
            .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    private static string EscapeJsonString(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    private static Task<T?> DispatchOnUiAsync<T>(Func<T?> func)
    {
        var dispatcher = Microsoft.Maui.Controls.Application.Current?.Dispatcher;
        if (dispatcher == null || !dispatcher.IsDispatchRequired)
            return Task.FromResult(func());

        var tcs = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.Dispatch(() =>
        {
            try
            {
                tcs.TrySetResult(func());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }))
            tcs.TrySetException(new InvalidOperationException("Unable to dispatch GTK WebView inspection."));
        return tcs.Task;
    }

    private static Task<T?> DispatchOnUiAsync<T>(Func<Task<T?>> func)
    {
        var dispatcher = Microsoft.Maui.Controls.Application.Current?.Dispatcher;
        if (dispatcher == null || !dispatcher.IsDispatchRequired)
            return func();

        var tcs = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.Dispatch(async () =>
        {
            try
            {
                tcs.TrySetResult(await func());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }))
            tcs.TrySetException(new InvalidOperationException("Unable to dispatch GTK WebView evaluation."));
        return tcs.Task;
    }

    private static Task DispatchOnUiAsync(Action action)
    {
        var dispatcher = Microsoft.Maui.Controls.Application.Current?.Dispatcher;
        if (dispatcher == null || !dispatcher.IsDispatchRequired)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.Dispatch(() =>
        {
            try
            {
                action();
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }))
            tcs.TrySetException(new InvalidOperationException("Unable to dispatch GTK WebView action."));
        return tcs.Task;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var index in _registrations.Values)
                _agent?.UnregisterCdpWebView(index);
            _registrations.Clear();
            foreach (var bridge in _bridges)
                bridge.Dispose();
            _bridges.Clear();
            _capturedHandlers.Clear();
            WebViewLogCallback = null;
            _agent = null;
        }
    }
}
