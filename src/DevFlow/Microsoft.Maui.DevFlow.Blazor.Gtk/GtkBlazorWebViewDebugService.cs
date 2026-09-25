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
    private bool _disposed;
    private CancellationTokenSource? _discoveryCts;

    public Action<string>? LogCallback { get; set; }
    public Action<string, string, string?>? WebViewLogCallback { get; set; }

    public bool IsReady => _bridges.Count > 0 && _bridges[0].IsReady;
    public IReadOnlyList<GtkWebViewBridge> Bridges => _bridges;

    /// <summary>
    /// Per-WebView bridge encapsulating CDP state and WebKit.WebView reference.
    /// </summary>
    public class GtkWebViewBridge : IDisposable
    {
        private readonly GtkBlazorWebViewDebugService _owner;
        private global::WebKit.WebView? _webView;
        private bool _isInitialized;
        private bool _injecting;
        private bool _chobitsuLoaded;
        private CancellationTokenSource? _drainCts;
        private bool _disposed;

        public string? AutomationId { get; }
        public string? ElementId { get; set; }
        public bool IsReady => _isInitialized && _webView != null && _chobitsuLoaded;

        internal GtkWebViewBridge(GtkBlazorWebViewDebugService owner, global::WebKit.WebView webView, string? automationId)
        {
            _owner = owner;
            _webView = webView;
            AutomationId = automationId;
        }

        internal async Task InitializeAsync()
        {
            _isInitialized = true;
            _owner.Log($"[BlazorDevFlow.Gtk] Bridge WebView captured (automationId={AutomationId}), waiting for page load...");
            await Task.Delay(2000);
            _owner.Log("[BlazorDevFlow.Gtk] Injecting debug script...");
            await InjectDebugScriptAsync();
        }

        internal async Task<string?> EvaluateJavaScriptAsync(string script)
        {
            if (_webView == null) return null;

            try
            {
                var result = await DispatchOnUiAsync(async () =>
                {
                    if (_webView == null) return null;
                    var value = await _webView.EvaluateJavascriptAsync(script);
                    return value?.ToString();
                });
                return result;
            }
            catch (Exception ex)
            {
                _owner.Log($"[BlazorDevFlow.Gtk] JS eval error: {ex}");
                return null;
            }
        }

        private async Task InjectDebugScriptAsync()
        {
            if (_injecting) return;
            _injecting = true;

            try
            {
                if (_webView == null)
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
                        await Task.Delay(250);
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
                _chobitsuLoaded = true;

                var consoleScript = ScriptResources.Load("console-intercept.js");
                await EvaluateJavaScriptAsync(consoleScript);

                StartLogDrain();
            }
            catch (Exception ex)
            {
                _owner.Log($"[BlazorDevFlow.Gtk] Failed to inject script: {ex.Message}");
            }
            finally
            {
                _injecting = false;
            }
        }

        public async Task<string> SendCdpCommandAsync(string cdpJson)
        {
            if (!IsReady)
                return "{\"error\":\"WebView not ready\"}";

            try
            {
                var json = System.Text.Json.JsonDocument.Parse(cdpJson);
                var id = json.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                var method = json.RootElement.TryGetProperty("method", out var methodProp) ? methodProp.GetString() ?? "" : "";

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

        private static string HandleBrowserMethod(string method, int id)
        {
            if (method == "Browser.getVersion")
                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    id,
                    result = new
                    {
                        protocolVersion = "1.3",
                        product = "MAUI Blazor WebView (WebKitGTK)/1.0",
                        userAgent = "Microsoft.Maui.DevFlow.Gtk",
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
            await EvaluateJavaScriptAsync(script);
            return $"{{\"id\":{id},\"result\":{{}}}}";
        }

        private async Task<string> HandlePageReloadAsync(int id)
        {
            await DispatchOnUiAsync(() => _webView?.Reload());
            await Task.Delay(1500);
            await InjectDebugScriptAsync();
            return $"{{\"id\":{id},\"result\":{{}}}}";
        }

        private async Task<string> HandlePageNavigateAsync(string cdpJson, int id)
        {
            var json = System.Text.Json.JsonDocument.Parse(cdpJson);
            var url = json.RootElement.GetProperty("params").GetProperty("url").GetString() ?? "";
            await DispatchOnUiAsync(() => _webView?.LoadUri(url));
            await Task.Delay(1500);
            await InjectDebugScriptAsync();
            return $"{{\"id\":{id},\"result\":{{\"frameId\":\"main\"}}}}";
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
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, ct);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _drainCts?.Cancel();
            _drainCts?.Dispose();
        }
    }

    /// <summary>
    /// Starts a background task that periodically scans the visual tree for BlazorWebViews
    /// and captures their WebKit.WebViews for CDP commands.
    /// </summary>
    public void StartWebViewDiscovery()
    {
        _discoveryCts?.Cancel();
        _discoveryCts = new CancellationTokenSource();
        var ct = _discoveryCts.Token;

        Task.Run(async () =>
        {
            Log("[BlazorDevFlow.Gtk] Starting WebView discovery...");
            var knownWebViews = new HashSet<int>(); // track by object hash

            for (int attempt = 0; attempt < 300 && !ct.IsCancellationRequested; attempt++)
            {
                await Task.Delay(2000, ct);

                try
                {
                    var found = await DispatchOnUiAsync(() =>
                    {
                        var app = Microsoft.Maui.Controls.Application.Current;
                        return app == null ? new List<(global::WebKit.WebView, string?)>() : FindAllWebKitWebViews(app);
                    });

                    if (found != null)
                    {
                        foreach (var (webView, automationId) in found)
                        {
                            var hash = webView.GetHashCode();
                            if (knownWebViews.Contains(hash)) continue;
                            knownWebViews.Add(hash);

                            Log($"[BlazorDevFlow.Gtk] WebKit.WebView discovered (automationId={automationId})");
                            var bridge = new GtkWebViewBridge(this, webView, automationId);
                            _bridges.Add(bridge);
                            await bridge.InitializeAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[BlazorDevFlow.Gtk] Discovery error: {ex.Message}");
                }
            }
            Log("[BlazorDevFlow.Gtk] WebView discovery ended");
        }, ct);
    }

    private static List<(global::WebKit.WebView, string?)> FindAllWebKitWebViews(Microsoft.Maui.Controls.Application app)
    {
        var results = new List<(global::WebKit.WebView, string?)>();
        foreach (var window in app.Windows)
        {
            if (window.Page is Microsoft.Maui.IVisualTreeElement root)
                SearchForWebKitWebViews(root, results);
        }
        return results;
    }

    private static void SearchForWebKitWebViews(Microsoft.Maui.IVisualTreeElement element, List<(global::WebKit.WebView, string?)> results)
    {
        if (element is Microsoft.Maui.Controls.View view)
        {
            var typeName = view.GetType().FullName ?? "";
            if (typeName.Contains("BlazorWebView", StringComparison.OrdinalIgnoreCase))
            {
                var webView = ExtractWebKitWebView(view);
                if (webView != null)
                {
                    var automationId = (view as Microsoft.Maui.Controls.VisualElement)?.AutomationId;
                    results.Add((webView, automationId));
                }
            }
        }

        foreach (var child in element.GetVisualChildren())
            SearchForWebKitWebViews(child, results);
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
        if (_bridges.Count == 0)
            return Task.FromResult("{\"error\":\"No WebViews available\"}");
        return _bridges[0].SendCdpCommandAsync(cdpJson);
    }

    private void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        Console.WriteLine(message);
        LogCallback?.Invoke(message);
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

        var tcs = new TaskCompletionSource<T?>();
        dispatcher.Dispatch(() =>
        {
            try
            {
                tcs.TrySetResult(func());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    private static Task<T?> DispatchOnUiAsync<T>(Func<Task<T?>> func)
    {
        var dispatcher = Microsoft.Maui.Controls.Application.Current?.Dispatcher;
        if (dispatcher == null || !dispatcher.IsDispatchRequired)
            return func();

        var tcs = new TaskCompletionSource<T?>();
        dispatcher.Dispatch(async () =>
        {
            try
            {
                tcs.TrySetResult(await func());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
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

        var tcs = new TaskCompletionSource();
        dispatcher.Dispatch(() =>
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
        });
        return tcs.Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var bridge in _bridges)
            bridge.Dispose();
        _discoveryCts?.Cancel();
        _discoveryCts?.Dispose();
    }
}
