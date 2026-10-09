using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using AgentService = Microsoft.Maui.DevFlow.Agent.Core.DevFlowAgentService;

namespace Microsoft.Maui.DevFlow.WebView;

/// <summary>One host-neutral CDP engine, shared by native WebView host adapters.</summary>
public abstract class WebViewDebugServiceBase : IDisposable
{
    private readonly object _gate = new();
    private readonly List<WebViewBridge> _bridges = new();
    private AgentService? _agent;
    private bool _disposed;

    public Action<string>? LogCallback { get; set; }
    public Action<string, string, string?>? WebViewLogCallback { get; set; }
    public bool IsReady { get { lock (_gate) return _bridges.Any(b => b.IsReady); } }
    public IReadOnlyList<WebViewBridge> Bridges { get { lock (_gate) return _bridges.ToArray(); } }
    public abstract void ConfigureHandler();

    protected virtual Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> func)
        => MainThread.InvokeOnMainThreadAsync(func);
    protected virtual Task<T> RunOnMainThreadAsync<T>(Func<T> func)
        => MainThread.InvokeOnMainThreadAsync(func);
    protected virtual Task RunOnMainThreadAsync(Func<Task> func)
        => MainThread.InvokeOnMainThreadAsync(func);
    protected virtual Task RunOnMainThreadAsync(Action action)
        => MainThread.InvokeOnMainThreadAsync(action);
    protected virtual void PostToMainThread(Action action)
        => MainThread.BeginInvokeOnMainThread(action);
    protected virtual int GetWebViewLoadDelayMs() => 2000;
    protected virtual int GetCdpResponseTimeoutMs() => 10000;

    /// <summary>Adapters can add readiness rules without putting framework assumptions in the engine.</summary>
    protected virtual Task<bool> IsHostReadyAsync(Func<string, Task<string?>> evaluate, CancellationToken token)
        => Task.FromResult(true);

    /// <summary>Override for host-specific routing, such as Blazor's client-side router.</summary>
    protected virtual async Task NavigateAsync(WebViewBridge bridge, string url)
    {
        if (HasExplicitUriScheme(url))
            await RunOnMainThreadAsync(() => bridge.NavigateNative(url));
        else
            await bridge.EvaluateAsync($"location.href = {JsonSerializer.Serialize(url)};");
    }

    protected static bool HasExplicitUriScheme(string url)
    {
        var separator = url.IndexOf(':');
        return separator > 0 && Uri.CheckSchemeName(url[..separator]) &&
            Uri.TryCreate(url, UriKind.Absolute, out _);
    }

    protected int AddWebViewBridge(Func<string, Task<string?>> evalJs, Action reload,
        Action<string> navigate, string? automationId = null)
        => AddWebViewBridge(evalJs, reload, navigate, automationId, null);

    protected int AddWebViewBridge(Func<string, Task<string?>> evalJs, Action reload,
        Action<string> navigate, string? automationId, Action? deactivated)
        => AddWebViewBridge(evalJs, reload, navigate, automationId, deactivated, "webview", null);

    protected int AddWebViewBridge(Func<string, Task<string?>> evalJs, Action reload,
        Action<string> navigate, string? automationId, Action? deactivated, string hostKind, object? owner)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var bridge = new WebViewBridge(this, evalJs, reload, navigate, automationId, deactivated, hostKind, owner);
            _bridges.Add(bridge);
            if (_agent is not null)
                bridge.Register(_agent);
            return _bridges.Count - 1;
        }
    }

    /// <summary>Connect to the agent's typed registry. Safe to call repeatedly with the same agent.</summary>
    public void ConnectAgent(AgentService agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (ReferenceEquals(_agent, agent)) return;
            _agent = agent;
            WebViewLogCallback = (level, message, exception) =>
                agent.WriteWebViewLog(level, "WebView.Console", message, exception);
            foreach (var bridge in _bridges)
                bridge.Register(agent);
        }
    }

    protected void DeactivateWebViewBridge(int bridgeIndex) => GetBridge(bridgeIndex)?.Deactivate();
    protected Task InitializeBridgeAsync(int bridgeIndex)
    {
        var bridge = GetBridge(bridgeIndex);
        return bridge?.InitializeAndMonitorAsync() ?? Task.CompletedTask;
    }
    protected Task ResetAndReinitializeBridgeAsync(int bridgeIndex)
    {
        var bridge = GetBridge(bridgeIndex);
        bridge?.ResetReadyState();
        return bridge?.InitializeAndMonitorAsync() ?? Task.CompletedTask;
    }

    public Task<string> SendCdpCommandAsync(string cdpJson) => SendCdpCommandAsync(0, cdpJson);
    public Task<string> SendCdpCommandAsync(int bridgeIndex, string cdpJson)
        => GetBridge(bridgeIndex)?.SendCdpCommandAsync(cdpJson)
           ?? Task.FromResult("{\"error\":{\"code\":-32000,\"message\":\"Invalid WebView index\"}}");

    public void Initialize()
    {
        foreach (var bridge in Bridges)
            _ = bridge.InitializeAndMonitorAsync();
    }

    private WebViewBridge? GetBridge(int index)
    {
        lock (_gate) return index >= 0 && index < _bridges.Count ? _bridges[index] : null;
    }
    protected void Log(string message) => LogCallback?.Invoke(message);

    /// <summary>Failures remain visible to Trace capture and the agent even with routine diagnostics disabled.</summary>
    protected void LogError(string message, Exception exception)
    {
        System.Diagnostics.Trace.TraceError($"[WebViewDevFlow] {message} {exception}");
        Volatile.Read(ref _agent)?.WriteWebViewLog("Error", "WebView.DevFlow", message, exception.ToString());
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var bridge in _bridges) bridge.Dispose();
            _agent = null;
            WebViewLogCallback = null;
        }
    }

    /// <summary>A native attachment with an independent, serialized CDP mailbox and lifetime.</summary>
    public class WebViewBridge : IDisposable
    {
        private readonly WebViewDebugServiceBase _service;
        private Func<string, Task<string?>> _evaluate;
        private Action _reload;
        private Action<string> _navigate;
        private Action? _cleanup;
        private readonly SemaphoreSlim _operations = new(1, 1);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly CancellationToken _token;
        private readonly object _registrationGate = new();
        private AgentService? _agent;
        private int _agentIndex;
        private int _active = 1;
        private int _monitorStarted;
        private int _nextId = 1000;
        private static long _nextWireId;
        private volatile bool _ready;
        private string? _lastError;
        private readonly WeakReference<object>? _owner;
        private readonly Func<string, Task<string>> _commandHandler;

        public string? AutomationId { get; }
        public string? ElementId { get; set; }
        public string HostKind { get; }
        public bool IsActive => Volatile.Read(ref _active) == 1;
        public bool IsReady => IsActive && _ready;
        /// <summary>The most recent attachment failure; cleared after successful initialization.</summary>
        public string? LastError => Volatile.Read(ref _lastError);
        internal CancellationToken LifetimeToken => _token;

        internal WebViewBridge(WebViewDebugServiceBase service, Func<string, Task<string?>> evaluate,
            Action reload, Action<string> navigate, string? automationId, Action? cleanup,
            string hostKind, object? owner)
        {
            _service = service;
            _evaluate = evaluate;
            _reload = reload;
            _navigate = navigate;
            _cleanup = cleanup;
            AutomationId = automationId;
            HostKind = hostKind;
            _owner = owner is null ? null : new(owner);
            _token = _lifetime.Token;
            _commandHandler = SendCdpCommandAsync;
        }

        internal void Register(AgentService agent)
        {
            lock (_registrationGate)
            {
                if (!IsActive || ReferenceEquals(_agent, agent)) return;
                _agent?.UnregisterCdpWebView(_agentIndex, _commandHandler);
                object? owner = null;
                _owner?.TryGetTarget(out owner);
                _agentIndex = agent.RegisterCdpWebView(_commandHandler, () => IsReady,
                    AutomationId, ElementId, null, HostKind, owner);
                _agent = agent;
            }
        }

        public async Task<string?> EvaluateAsync(string script)
        {
            _token.ThrowIfCancellationRequested();
            var result = await _service.RunOnMainThreadAsync(() => _evaluate(script))
                .WaitAsync(TimeSpan.FromSeconds(15), _token);
            _token.ThrowIfCancellationRequested();
            return NormalizeEvaluationResult(result);
        }

        /// <summary>Decode native JSON-wrapped results, preserving actual JSON objects/arrays.</summary>
        public static string? NormalizeEvaluationResult(string? value)
        {
            if (string.IsNullOrEmpty(value) || value is "null" or "undefined" or "<null>") return null;
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                try { return JsonSerializer.Deserialize<string>(value); }
                catch (JsonException) { }
            }
            return value;
        }

        internal void NavigateNative(string url) => _navigate(url);
        internal void ResetReadyState() => _ready = false;

        internal async Task InitializeAndMonitorAsync()
        {
            try
            {
                await _operations.WaitAsync(_token);
                try { await InitializeCoreAsync(); }
                finally { _operations.Release(); }
            }
            catch (OperationCanceledException) when (!IsActive) { }
            catch (Exception ex) { RecordError("Initialization failed", ex); }
            finally
            {
                // A native evaluator can fail during its first load. Keep observing that attachment
                // so Hybrid hosts (without a managed Navigated event) recover without a CDP request.
                if (IsActive && Interlocked.Exchange(ref _monitorStarted, 1) == 0)
                    _ = MonitorAsync();
            }
        }

        private async Task<bool> InitializeCoreAsync()
        {
            if (IsReady) return true;
            _token.ThrowIfCancellationRequested();
            var deadline = DateTime.UtcNow.AddMilliseconds(_service.GetWebViewLoadDelayMs());
            var documentReady = false;
            Exception? probeFailure = null;
            do
            {
                try
                {
                    var state = await EvaluateAsync("document.readyState");
                    if (state is "interactive" or "complete") { documentReady = true; break; }
                }
                catch (Exception ex) when (!_token.IsCancellationRequested) { probeFailure = ex; }
                await Task.Delay(100, _token);
            } while (DateTime.UtcNow < deadline);
            if (!documentReady)
                throw new TimeoutException($"Document readiness timed out. {probeFailure?.Message ?? LastError}", probeFailure);
            if (!await _service.IsHostReadyAsync(EvaluateAsync, _token))
                throw new TimeoutException($"Host readiness timed out. {LastError}");

            if (await EvaluateAsync("typeof chobitsu !== 'undefined' ? 'loaded' : 'waiting'") != "loaded")
                await EvaluateAsync(ChobitsuDebugScript.GetLoadScript());
            if (await EvaluateAsync("typeof chobitsu !== 'undefined' ? 'loaded' : 'waiting'") != "loaded")
                throw new InvalidOperationException("Embedded chobitsu injection failed.");
            var result = await EvaluateAsync(ChobitsuDebugScript.GetInjectionScript());
            if (result is not ("ready" or "already_initialized") ||
                await EvaluateAsync("window.__chobitsuReady ? 'ready' : 'waiting'") != "ready")
                throw new InvalidOperationException("CDP initialization did not report ready.");
            await EvaluateAsync(ScriptResources.Load("console-intercept.js"));
            _token.ThrowIfCancellationRequested();
            Volatile.Write(ref _lastError, null);
            _ready = true;
            _service.Log($"[WebViewDevFlow] {HostKind} bridge initialized (automationId={AutomationId ?? "(none)"}).");
            return true;
        }

        private async Task MonitorAsync()
        {
            var iteration = 0;
            while (!_token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(500, _token);
                    await _operations.WaitAsync(_token);
                    try
                    {
                        if (await EvaluateAsync("window.__chobitsuReady ? 'ready' : 'waiting'") != "ready")
                            _ready = false;
                        if (!IsReady) await InitializeCoreAsync();
                        if (++iteration % 4 == 0 && IsReady && _service.WebViewLogCallback is not null)
                            await DrainLogsAsync();
                    }
                    finally { _operations.Release(); }
                }
                catch (OperationCanceledException) when (_token.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _ready = false;
                    RecordError("Document/console monitor failed", ex);
                }
            }
        }

        private async Task DrainLogsAsync()
        {
            var raw = await EvaluateAsync(ScriptResources.Load("drain-console-logs.js"));
            if (string.IsNullOrEmpty(raw)) return;
            using var document = JsonDocument.Parse(raw);
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                var level = entry.GetProperty("l").GetString() switch
                {
                    "error" => "Error", "warn" => "Warning", "debug" => "Debug", _ => "Information"
                };
                _service.WebViewLogCallback?.Invoke(level, entry.GetProperty("m").GetString() ?? "",
                    entry.TryGetProperty("e", out var error) ? error.GetString() : null);
            }
        }

        public async Task<string> SendCdpCommandAsync(string cdpJson)
        {
            var id = 0;
            var acquired = false;
            try
            {
                using var command = JsonDocument.Parse(cdpJson);
                id = command.RootElement.TryGetProperty("id", out var idValue)
                    ? idValue.GetInt32() : Interlocked.Increment(ref _nextId);
                var method = command.RootElement.GetProperty("method").GetString() ?? "";
                await _operations.WaitAsync(_token);
                acquired = true;
                _token.ThrowIfCancellationRequested();
                if (method == "Browser.getVersion")
                    return JsonSerializer.Serialize(new { id, result = new {
                        protocolVersion = "1.3", product = "MAUI WebView/1.0",
                        userAgent = "Microsoft.Maui.DevFlow", jsVersion = "" } });
                if (method.StartsWith("Browser.", StringComparison.Ordinal))
                    return Error(id, $"Unsupported CDP method: {method}", -32601);
                if (method == "Page.reload")
                {
                    _ready = false;
                    await _service.RunOnMainThreadAsync(() => _reload()).WaitAsync(_token);
                    return Success(id);
                }
                if (method == "Page.navigate")
                {
                    var url = command.RootElement.GetProperty("params").GetProperty("url").GetString()
                        ?? throw new ArgumentException("Page.navigate requires url.");
                    _ready = false;
                    await _service.NavigateAsync(this, url).WaitAsync(_token);
                    return JsonSerializer.Serialize(new { id, result = new { frameId = "main" } });
                }
                if (!await InitializeCoreAsync()) return Error(id, LastError ?? "WebView not ready");
                if (method == "Input.insertText")
                {
                    var text = command.RootElement.GetProperty("params").GetProperty("text").GetString() ?? "";
                    await EvaluateAsync(ScriptResources.Load("insert-text.js")
                        .Replace("%TEXT%", EscapeJsString(text))
                        .Replace("%TEXT_LENGTH%", text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    return Success(id);
                }
                // External callers may reuse IDs, even after a timeout. Never reuse one on the
                // JavaScript wire, where a late response can otherwise satisfy a newer command.
                var wireId = Interlocked.Increment(ref _nextWireId);
                var message = System.Text.Json.Nodes.JsonNode.Parse(cdpJson)!.AsObject();
                message["id"] = wireId;
                var sendResult = await EvaluateAsync(ScriptResources.Load("cdp-send-receive.js")
                    .Replace("%CDP_MESSAGE%", EscapeJsString(message.ToJsonString())));
                if (sendResult != "__cdp_pending__")
                {
                    // The send script checks the bridge before dispatching the command.
                    // A replaced document is transient, but the command must not be replayed.
                    if (sendResult is not null)
                    {
                        using var rejected = JsonDocument.Parse(sendResult);
                        if (rejected.RootElement.TryGetProperty("error", out var reason)
                            && reason.ValueKind == JsonValueKind.String
                            && reason.GetString() == "chobitsu not loaded")
                        {
                            _ready = false;
                            throw new InvalidOperationException("WebView not ready: the document changed before CDP dispatch.");
                        }
                    }
                    throw new InvalidOperationException($"Failed to send CDP command: {sendResult ?? "null"}");
                }
                var deadline = DateTime.UtcNow.AddMilliseconds(_service.GetCdpResponseTimeoutMs());
                do
                {
                    var response = await EvaluateAsync(ScriptResources.Load("cdp-read-response.js"));
                    if (response is not null)
                    {
                        using var parsed = JsonDocument.Parse(response);
                        if (parsed.RootElement.TryGetProperty("id", out var responseId) &&
                            responseId.TryGetInt64(out var receivedId) && receivedId == wireId)
                        {
                            var reply = System.Text.Json.Nodes.JsonNode.Parse(response)!.AsObject();
                            reply["id"] = id;
                            return reply.ToJsonString();
                        }
                    }
                    await Task.Delay(50, _token);
                } while (DateTime.UtcNow < deadline);
                // Never replay a timed-out mutation: the original may already have run.
                _ready = false;
                throw new TimeoutException("CDP response timeout");
            }
            catch (OperationCanceledException) when (_token.IsCancellationRequested)
            { return Error(id, "WebView is no longer available"); }
            catch (Exception ex)
            {
                RecordError("CDP command failed", ex);
                return Error(id, LastError!);
            }
            finally { if (acquired) _operations.Release(); }
        }

        private static string Success(int id) => $"{{\"id\":{id},\"result\":{{}}}}";
        private static string Error(int id, string message, int code = -32000)
            => JsonSerializer.Serialize(new { id, error = new { code, message } });
        private static string EscapeJsString(string value)
            => value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n")
                .Replace("\r", "\\r").Replace("\t", "\\t").Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");

        private void RecordError(string operation, Exception exception)
        {
            var message = $"{operation} ({HostKind}, automationId={AutomationId ?? "(none)"}): {exception.Message}";
            Volatile.Write(ref _lastError, message);
            _service.LogError(message, exception);
        }

        internal void Deactivate()
        {
            if (Interlocked.Exchange(ref _active, 0) == 0) return;
            _ready = false;
            _lifetime.Cancel();
            lock (_registrationGate)
            {
                _agent?.UnregisterCdpWebView(_agentIndex, _commandHandler);
                _agent = null;
            }
            _evaluate = static _ => Task.FromResult<string?>(null);
            _reload = static () => { };
            _navigate = static _ => { };
            try { Interlocked.Exchange(ref _cleanup, null)?.Invoke(); }
            catch (Exception ex) { RecordError("Attachment cleanup failed", ex); }
        }

        public void Dispose() => Deactivate();
    }
}
