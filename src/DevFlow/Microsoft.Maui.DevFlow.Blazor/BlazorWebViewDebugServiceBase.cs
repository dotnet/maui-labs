namespace Microsoft.Maui.DevFlow.Blazor;

/// <summary>
/// Base class for BlazorWebView debug services. Manages multiple WebView instances,
/// each with independent CDP command handling and script injection.
/// Platform-specific subclasses provide WebView capture and JavaScript evaluation.
/// </summary>
public abstract class BlazorWebViewDebugServiceBase : IDisposable
{
    private bool _disposed;
    private readonly List<WebViewBridge> _bridges = new();

    /// <summary>Optional log callback for debug messages.</summary>
    public Action<string>? LogCallback { get; set; }

    /// <summary>
    /// Callback for routing WebView console logs to the native logging pipeline.
    /// Parameters: level, message, exception (nullable).
    /// </summary>
    public Action<string, string, string?>? WebViewLogCallback { get; set; }

    /// <summary>Whether at least one WebView is ready for CDP commands.</summary>
    public bool IsReady => _bridges.Any(b => b.IsReady);

    /// <summary>The registered WebView bridges.</summary>
    public IReadOnlyList<WebViewBridge> Bridges => _bridges;

    protected BlazorWebViewDebugServiceBase() { }

    /// <summary>
    /// Dispatches an async action to the main/UI thread and returns the result.
    /// Override in platform subclasses that don't support MainThread.InvokeOnMainThreadAsync.
    /// </summary>
    protected virtual Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> func)
        => MainThread.InvokeOnMainThreadAsync(func);

    /// <summary>
    /// Dispatches an action to the main/UI thread and returns the result.
    /// Override in platform subclasses that don't support MainThread.InvokeOnMainThreadAsync.
    /// </summary>
    protected virtual Task<T> RunOnMainThreadAsync<T>(Func<T> func)
        => MainThread.InvokeOnMainThreadAsync(func);

    /// <summary>
    /// Dispatches an async action to the main/UI thread.
    /// Override in platform subclasses that don't support MainThread.InvokeOnMainThreadAsync.
    /// </summary>
    protected virtual Task RunOnMainThreadAsync(Func<Task> func)
        => MainThread.InvokeOnMainThreadAsync(func);

    /// <summary>
    /// Dispatches an action to the main/UI thread.
    /// Override in platform subclasses that don't support MainThread.InvokeOnMainThreadAsync.
    /// </summary>
    protected virtual Task RunOnMainThreadAsync(Action action)
        => MainThread.InvokeOnMainThreadAsync(action);

    /// <summary>
    /// Posts an action to the main/UI thread without waiting (fire-and-forget).
    /// Override in platform subclasses that don't support MainThread.BeginInvokeOnMainThread.
    /// </summary>
    protected virtual void PostToMainThread(Action action)
        => MainThread.BeginInvokeOnMainThread(action);

    /// <summary>
    /// Configures the BlazorWebViewHandler to capture the platform WebView reference.
    /// Called during service registration before the app starts.
    /// </summary>
    public abstract void ConfigureHandler();

    /// <summary>
    /// Platform-specific maximum delay in milliseconds to wait for WebView to load before injecting debug scripts.
    /// Default is 2000ms. Override in platform subclasses if more time is needed.
    /// We poll document.readyState === 'complete' inside this budget and return as soon as it completes.
    /// </summary>
    protected virtual int GetWebViewLoadDelayMs() => 2000;

    /// <summary>
    /// Wait for the WebView's document.readyState to reach 'complete', bounded by GetWebViewLoadDelayMs().
    /// Falls back to a fixed delay if evaluation isn't available.
    /// </summary>
    private async Task WaitForWebViewLoadedAsync(Func<string, Task<string?>> evalJs)
    {
        var maxDelayMs = GetWebViewLoadDelayMs();
        // Minimum settle time before first probe, to let the platform fully attach the WebView.
        var minSettleMs = Math.Min(200, maxDelayMs);
        await Task.Delay(minSettleMs);

        var deadline = DateTime.UtcNow.AddMilliseconds(maxDelayMs - minSettleMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var state = await evalJs("document.readyState");
                if (state != null)
                {
                    var s = state.ToString();
                    if (s == "complete" || s == "\"complete\"")
                        return;
                }
            }
            catch
            {
                // Evaluator may not be ready yet; keep waiting.
            }

            await Task.Delay(100);
        }
    }

    /// <summary>
    /// Register a new WebView bridge. Called by platform subclasses when a WebView is captured.
    /// Returns the bridge index.
    /// </summary>
    protected int AddWebViewBridge(Func<string, Task<string?>> evalJs, Action reload,
        Action<string> navigate, string? automationId = null)
    {
        var bridge = new WebViewBridge(this, evalJs, reload, navigate, automationId);
        _bridges.Add(bridge);
        return _bridges.Count - 1;
    }

    /// <summary>
    /// Called after a WebView bridge is added and the WebView is ready.
    /// Injects chobitsu scripts and starts log drain for the bridge.
    /// </summary>
    protected async Task InitializeBridgeAsync(int bridgeIndex)
    {
        if (bridgeIndex < 0 || bridgeIndex >= _bridges.Count) return;
        var bridge = _bridges[bridgeIndex];

        Log($"[BlazorDevFlow] Waiting for WebView {bridgeIndex} to load (max {GetWebViewLoadDelayMs()}ms)...");
        await WaitForWebViewLoadedAsync(bridge.EvalJsProbe);

        Log($"[BlazorDevFlow] Injecting debug script into WebView {bridgeIndex}...");
        await bridge.InjectDebugScriptAsync();
    }

    /// <summary>
    /// Reset bridge state and re-inject debug scripts after page navigation.
    /// Called by platform-specific navigation detection (e.g., Android OnPageFinished).
    /// </summary>
    internal async Task ResetAndReinitializeBridgeAsync(int bridgeIndex)
    {
        if (bridgeIndex < 0 || bridgeIndex >= _bridges.Count) return;
        var bridge = _bridges[bridgeIndex];

        Log($"[BlazorDevFlow] Re-initialization: waiting for WebView {bridgeIndex} to settle (max {GetWebViewLoadDelayMs()}ms)...");
        await WaitForWebViewLoadedAsync(bridge.EvalJsProbe);

        Log($"[BlazorDevFlow] Re-injecting debug script into WebView {bridgeIndex}...");
        bridge.ResetReadyState();
        await bridge.InjectDebugScriptAsync();
    }

    // Backward compat: sends to first bridge
    public Task<string> SendCdpCommandAsync(string cdpJson)
        => SendCdpCommandAsync(0, cdpJson);

    /// <summary>Send a CDP command to a specific WebView bridge.</summary>
    public Task<string> SendCdpCommandAsync(int bridgeIndex, string cdpJson)
    {
        if (bridgeIndex < 0 || bridgeIndex >= _bridges.Count)
            return Task.FromResult("{\"error\":\"Invalid WebView index\"}");
        return _bridges[bridgeIndex].SendCdpCommandAsync(cdpJson);
    }

    // Backward compat: checks first bridge
    public void Initialize()
    {
        if (_bridges.Count > 0 && _bridges[0].IsReady)
        {
            PostToMainThread(async () => await _bridges[0].InjectDebugScriptAsync());
        }
    }

    internal void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        Console.WriteLine(message);
        LogCallback?.Invoke(message);
    }

    internal void LogError(string message, Exception? ex = null)
    {
        Log($"[ERROR] {message}");
        if (ex != null)
            Log($"  Exception: {ex.GetType().Name}: {ex.Message}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var bridge in _bridges)
            bridge.Dispose();
        Log("[BlazorDevFlow] Disposed");
    }

    /// <summary>
    /// Encapsulates a single WebView with its own CDP handling and script injection state.
    /// </summary>
    public class WebViewBridge : IDisposable
    {
        private readonly BlazorWebViewDebugServiceBase _owner;
        private readonly Func<string, Task<string?>> _evalJs;
        private readonly Action _reload;
        private readonly Action<string> _navigate;
        private bool _chobitsuLoaded;
        private readonly object _injectGate = new();
        private Task? _injectTask;
        private int _cdpIdCounter = 1000;
        private CancellationTokenSource? _drainCts;

        /// <summary>AutomationId of the MAUI BlazorWebView control.</summary>
        public string? AutomationId { get; }

        /// <summary>Visual tree element ID for correlation with MAUI tree output.</summary>
        public string? ElementId { get; set; }

        /// <summary>Whether this WebView is ready for CDP commands.</summary>
        public bool IsReady => _chobitsuLoaded;

        /// <summary>Internal: exposes the JS evaluator so the owner can probe the page before injection.</summary>
        internal Func<string, Task<string?>> EvalJsProbe => _evalJs;

        internal WebViewBridge(BlazorWebViewDebugServiceBase owner,
            Func<string, Task<string?>> evalJs, Action reload, Action<string> navigate,
            string? automationId)
        {
            _owner = owner;
            _evalJs = evalJs;
            _reload = reload;
            _navigate = navigate;
            AutomationId = automationId;
        }

        internal async Task InjectDebugScriptAsync()
        {
            Task injectTask;
            lock (_injectGate)
            {
                if (_injectTask is { IsCompleted: false })
                {
                    injectTask = _injectTask;
                }
                else
                {
                    injectTask = InjectDebugScriptCoreAsync();
                    _injectTask = injectTask;
                }
            }

            await injectTask;
        }

        /// <summary>
        /// Reset the ready state so the script can be re-injected after page navigation.
        /// </summary>
        internal void ResetReadyState()
        {
            _chobitsuLoaded = false;
        }

        private async Task InjectDebugScriptCoreAsync()
        {
            // Wait for chobitsu to become available. Some apps may provide it via a script tag,
            // while others rely on the embedded fallback injection below.
            // We poll briefly before falling back to direct eval injection.
            var loaded = false;
            for (int i = 0; i < 10; i++)
            {
                var check = await _evalJs(
                    "typeof chobitsu !== 'undefined' ? 'loaded' : 'waiting'");
                if (i == 0 || check?.ToString() == "loaded")
                    _owner.Log($"[BlazorDevFlow] Chobitsu check #{i}: {check}");
                if (check?.ToString() == "loaded")
                {
                    loaded = true;
                    break;
                }

                await Task.Delay(500);
            }

            if (!loaded)
            {
                // The script tag may be absent or unavailable in the WebView static asset host.
                // Fall back to injecting chobitsu.js directly via JS eval from the embedded resource.
                // IMPORTANT: We must wait for Blazor to finish rendering before injecting chobitsu
                // because the large eval() can interfere with Blazor's startup on WebView2.
                _owner.Log("[BlazorDevFlow] Chobitsu not loaded via script tag — waiting for Blazor to render before embedded injection...");
                try
                {
                    // Wait for Blazor to replace "Loading..." with actual content (up to 30s).
                    for (int w = 0; w < 60; w++)
                    {
                        var appContent = await _evalJs(
                            "document.querySelector('#app')?.innerHTML?.substring(0, 20) || ''");
                        var content = appContent?.ToString() ?? "";
                        if (!content.Contains("Loading") && content.Length > 0)
                        {
                            _owner.Log($"[BlazorDevFlow] Blazor rendered (content: {content})");
                            break;
                        }
                        await Task.Delay(500);
                    }

                    _owner.Log("[BlazorDevFlow] Injecting embedded chobitsu.js via eval...");
                    var embeddedJs = ChobitsuDebugScript.GetEmbeddedChobitsuJs();
                    await _evalJs(embeddedJs);

                    // Verify it loaded
                    var verify = await _evalJs("typeof chobitsu !== 'undefined' ? 'loaded' : 'waiting'");
                    if (verify?.ToString() == "loaded")
                    {
                        _owner.Log("[BlazorDevFlow] Chobitsu loaded via embedded fallback.");
                        loaded = true;
                    }
                    else
                    {
                        _owner.Log($"[BlazorDevFlow] Embedded chobitsu injection did not define chobitsu global (got: {verify}).");
                    }
                }
                catch (Exception ex)
                {
                    _owner.LogError("[BlazorDevFlow] Failed to inject embedded chobitsu.js", ex);
                }

                if (!loaded)
                    return;
            }

            var script = ChobitsuDebugScript.GetInjectionScript();
            _owner.Log($"[BlazorDevFlow] Injecting init script ({script.Length} chars)...");

            try
            {
                var result = await _evalJs(script);
                _owner.Log($"[BlazorDevFlow] Script injection result: {result?.ToString() ?? "null"}");
                _chobitsuLoaded = true;

                await InjectConsoleInterceptAsync();
                StartLogDrain();
            }
            catch (Exception ex)
            {
                _owner.LogError("[BlazorDevFlow] Failed to inject script", ex);
            }
        }

        public Task<string> SendCdpCommandAsync(string cdpJson)
            => SendCdpCommandCoreAsync(cdpJson, allowRecovery: true);

        private async Task<string> SendCdpCommandCoreAsync(string cdpJson, bool allowRecovery)
        {
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(cdpJson);
                var hasId = json.RootElement.TryGetProperty("id", out var idProp);
                var id = hasId ? idProp.GetInt32() : 0;
                var method = json.RootElement.TryGetProperty("method", out var methodProp) ? methodProp.GetString() ?? "" : "";

                if (!hasId)
                {
                    id = System.Threading.Interlocked.Increment(ref _cdpIdCounter);
                    using var ms = new System.IO.MemoryStream();
                    using (var writer = new System.Text.Json.Utf8JsonWriter(ms))
                    {
                        writer.WriteStartObject();
                        writer.WriteNumber("id", id);
                        foreach (var prop in json.RootElement.EnumerateObject())
                            prop.WriteTo(writer);
                        writer.WriteEndObject();
                    }
                    cdpJson = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                }

                // These commands already use direct native JS evaluation and should remain
                // available while the bridge is still warming up or recovering.
                if (method == "Input.insertText")
                    return await HandleInputInsertTextAsync(cdpJson, id);
                if (method == "Page.reload")
                    return await HandlePageReloadAsync(id);
                if (method == "Page.navigate")
                    return await HandlePageNavigateAsync(cdpJson, id);
                if (method.StartsWith("Browser."))
                    return HandleBrowserMethod(method, id);

                if (!IsReady)
                {
                    // The first injection attempt may have raced with DOM parse on slow runners.
                    // Try one more time before giving up — this is cheap when chobitsu IS loaded
                    // (single eval returns 'loaded' immediately) and self-heals the common
                    // "bridge created too early" case without the caller having to do anything.
                    _owner.Log("[BlazorDevFlow] SendCdpCommand: bridge not ready, retrying injection...");
                    await InjectDebugScriptAsync();

                    if (!IsReady)
                        return "{\"error\":\"WebView not ready\"}";
                }

                var escaped = cdpJson.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
                var sendScript = ScriptResources.Load("cdp-send-receive.js")
                    .Replace("%CDP_MESSAGE%", escaped);
                var readScript = ScriptResources.Load("cdp-read-response.js");

                var sendResult = await _owner.RunOnMainThreadAsync(async () =>
                {
                    return await _evalJs(sendScript);
                });

                await Task.Delay(50);

                string? result = null;
                for (int i = 0; i < 200; i++)
                {
                    result = await _owner.RunOnMainThreadAsync(async () =>
                    {
                        return await _evalJs(readScript);
                    });

                    var unescaped = UnescapeEvalResult(result);
                    if (unescaped != null)
                    {
                        if (i >= 10)
                            _owner.Log($"[BlazorDevFlow] SendCdpCommand: method={method} responded after {i + 1} poll(s)");
                        return unescaped;
                    }

                    await Task.Delay(50);
                }

                _owner.Log($"[BlazorDevFlow] SendCdpCommand: method={method} no response after polling (10s timeout)");
                if (allowRecovery)
                {
                    _owner.Log("[BlazorDevFlow] CDP timeout; resetting and retrying injection once...");
                    ResetReadyState();
                    await InjectDebugScriptAsync();
                    if (IsReady)
                        return await SendCdpCommandCoreAsync(cdpJson, allowRecovery: false);
                }

                return "{\"error\":\"cdp timeout\"}";
            }
            catch (Exception ex)
            {
                _owner.LogError("[BlazorDevFlow] SendCdpCommandAsync failed", ex);
                return $"{{\"error\":\"{EscapeJsonString(ex.Message)}\"}}";
            }
        }

        private string HandleBrowserMethod(string method, int id)
        {
            if (method == "Browser.getVersion")
                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    id,
                    result = new
                    {
                        protocolVersion = "1.3",
                        product = "MAUI Blazor WebView/1.0",
                        userAgent = "Microsoft.Maui.DevFlow",
                        jsVersion = ""
                    }
                });
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
            await _owner.RunOnMainThreadAsync(async () => { await _evalJs(script); });
            return $"{{\"id\":{id},\"result\":{{}}}}";
        }

        private async Task<string> HandlePageReloadAsync(int id)
        {
            await _owner.RunOnMainThreadAsync(() => { _reload(); });
            await Task.Delay(1500);
            await InjectDebugScriptAsync();
            return $"{{\"id\":{id},\"result\":{{}}}}";
        }

        private async Task<string> HandlePageNavigateAsync(string cdpJson, int id)
        {
            var json = System.Text.Json.JsonDocument.Parse(cdpJson);
            var url = json.RootElement.GetProperty("params").GetProperty("url").GetString() ?? "";

            if (Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                await _owner.RunOnMainThreadAsync(() => { _navigate(url); });
            }
            else
            {
                var escapedUrl = EscapeJsString(url);
                var script = $@"(function() {{
                    const target = '{escapedUrl}';
                    try {{
                        if (window.Blazor && typeof window.Blazor.navigateTo === 'function') {{
                            window.Blazor.navigateTo(target);
                            return 'blazor';
                        }}
                    }} catch {{}}

                    try {{
                        history.pushState({{}}, '', target);
                        window.dispatchEvent(new PopStateEvent('popstate'));
                        return 'history';
                    }} catch {{}}

                    location.href = target;
                    return 'location';
                }})()";

                await _owner.RunOnMainThreadAsync(async () => { await _evalJs(script); });
            }

            await Task.Delay(500);
            await InjectDebugScriptAsync();
            return $"{{\"id\":{id},\"result\":{{\"frameId\":\"main\"}}}}";
        }

        private async Task InjectConsoleInterceptAsync()
        {
            try
            {
                var script = ScriptResources.Load("console-intercept.js");
                var result = await _evalJs(script);
                _owner.Log($"[BlazorDevFlow] Console intercept: {result ?? "null"}");
            }
            catch (Exception ex)
            {
                _owner.LogError("[BlazorDevFlow] Failed to inject console interceptor", ex);
            }
        }

        private void StartLogDrain()
        {
            _drainCts?.Cancel();
            _drainCts = new CancellationTokenSource();
            var ct = _drainCts.Token;

            Task.Run(async () =>
            {
                var drainScript = ScriptResources.Load("drain-console-logs.js");

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(2000, ct);
                        if (!IsReady || _owner.WebViewLogCallback == null) continue;

                        var raw = await _owner.RunOnMainThreadAsync(async () =>
                        {
                            return await _evalJs(drainScript);
                        });

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
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[BlazorDevFlow] Log drain error: {ex.Message}");
                    }
                }
            }, ct);
        }

        private static string? UnescapeEvalResult(string? result)
        {
            if (string.IsNullOrEmpty(result)) return null;
            if (result.StartsWith("\"") && result.EndsWith("\""))
            {
                try { return System.Text.Json.JsonSerializer.Deserialize<string>(result); }
                catch { }
            }
            return result;
        }

        private static string EscapeJsString(string s) =>
            s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"")
             .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

        private static string EscapeJsonString(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

        public void Dispose()
        {
            _drainCts?.Cancel();
            _drainCts?.Dispose();
        }
    }
}
