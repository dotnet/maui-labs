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
    /// Platform-specific delay in milliseconds to wait for WebView to load before injecting debug scripts.
    /// Default is 2000ms. Override in platform subclasses if more time is needed.
    /// </summary>
    protected virtual int GetWebViewLoadDelayMs() => 2000;

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

        var delayMs = GetWebViewLoadDelayMs();
        Log($"[BlazorDevFlow] Waiting {delayMs}ms for WebView {bridgeIndex} to load...");
        await Task.Delay(delayMs);

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

        var delayMs = GetWebViewLoadDelayMs();
        Log($"[BlazorDevFlow] Re-initialization: waiting {delayMs}ms for WebView {bridgeIndex} to settle...");
        await Task.Delay(delayMs);

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
        private bool _injecting;
        private int _cdpIdCounter = 1000;
        private CancellationTokenSource? _drainCts;

        /// <summary>AutomationId of the MAUI BlazorWebView control.</summary>
        public string? AutomationId { get; }

        /// <summary>Visual tree element ID for correlation with MAUI tree output.</summary>
        public string? ElementId { get; set; }

        /// <summary>Whether this WebView is ready for CDP commands.</summary>
        public bool IsReady => _chobitsuLoaded;

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
            if (_injecting) return;
            _injecting = true;

            try
            {
                await InjectDebugScriptCoreAsync();
            }
            finally
            {
                _injecting = false;
            }
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
            // Wait for chobitsu to be available
            for (int i = 0; i < 30; i++)
            {
                var check = await _evalJs(
                    "typeof chobitsu !== 'undefined' ? 'loaded' : 'waiting'");
                if (i == 0 || check?.ToString() == "loaded")
                    _owner.Log($"[BlazorDevFlow] Chobitsu check #{i}: {check}");
                if (check?.ToString() == "loaded") break;

                if (i == 10)
                {
                    var hasTag = await _evalJs(
                        "document.querySelector('script[src*=\"chobitsu\"]') ? 'found' : 'missing'");
                    if (hasTag?.ToString() == "missing")
                    {
                        _owner.Log("[BlazorDevFlow] ⚠️ No chobitsu script tag found.");
                        return;
                    }
                }

                if (i == 29)
                {
                    _owner.Log("[BlazorDevFlow] Chobitsu not loaded after 15s");
                    return;
                }
                await Task.Delay(500);
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

        public async Task<string> SendCdpCommandAsync(string cdpJson)
        {
            if (!IsReady)
                return "{\"error\":\"WebView not ready\"}";

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

                if (method == "Input.insertText")
                    return await HandleInputInsertTextAsync(cdpJson, id);
                if (method == "Page.reload")
                    return await HandlePageReloadAsync(id);
                if (method == "Page.navigate")
                    return await HandlePageNavigateAsync(cdpJson, id);
                if (method.StartsWith("Browser."))
                    return HandleBrowserMethod(method, id);

                var escaped = cdpJson.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
                var sendScript = ScriptResources.Load("cdp-send-receive.js")
                    .Replace("%CDP_MESSAGE%", escaped);
                var readScript = ScriptResources.Load("cdp-read-response.js");

                _owner.Log($"[BlazorDevFlow] SendCdpCommand: method={method}");

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
                        _owner.Log($"[BlazorDevFlow] SendCdpCommand got response after {i + 1} poll(s)");
                        return unescaped;
                    }

                    await Task.Delay(50);
                }

                _owner.Log($"[BlazorDevFlow] SendCdpCommand: no response after polling (10s timeout)");
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
            await _owner.RunOnMainThreadAsync(() => { _navigate(url); });
            await Task.Delay(1500);
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
