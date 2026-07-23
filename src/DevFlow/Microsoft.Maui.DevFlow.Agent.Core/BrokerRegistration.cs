using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Connects the agent to the broker daemon for registration and port assignment.
/// Falls back gracefully if the broker is unavailable.
/// </summary>
public class BrokerRegistration : IDisposable
{
    public const int DefaultBrokerPort = 19223;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Timer? _reconnectTimer;
    private bool _disposed;
    private readonly object _lifecycleGate = new();
    private readonly string _project;
    private readonly string _tfm;
    private readonly string _platform;
    private readonly string _appName;
    private readonly string? _sessionId;
    private string _agentId;
    private int _brokerPort;
    private int? _assignedPort;
    private ILogger? _logger;
    private static ILogger? _staticLogger;
    private static readonly HttpClient _brokerHttp = new(new SocketsHttpHandler
    {
        ConnectCallback = ConnectBrokerLoopbackAsync
    })
    {
        Timeout = TimeSpan.FromSeconds(2)
    };
    private static readonly JsonSerializerOptions _leaseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// The port assigned by the broker, or null if not registered.
    /// </summary>
    public int? AssignedPort => _assignedPort;

    /// <summary>
    /// The port the agent's HTTP listener is actually running on.
    /// Set after the listener starts so late reconnections can inform the broker.
    /// </summary>
    public int? CurrentPort { get; set; }

    /// <summary>
    /// Whether the agent is currently connected to the broker.
    /// </summary>
    public bool IsConnected => _ws?.State == WebSocketState.Open;
    internal bool HasBrokerAuthority => !_disposed && _assignedPort.HasValue;
    internal string AgentId => _agentId;
    internal int BrokerPort => _brokerPort;
    private static readonly string[] _brokerHttpHosts = ["127.0.0.1", "localhost"];
    internal static IReadOnlyList<string> BrokerHttpHosts => _brokerHttpHosts;

    internal static bool ShouldTryAlternateBrokerHost(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound;

    /// <summary>
    /// Sets the static logger to be used by all BrokerRegistration instances that don't have an instance logger.
    /// </summary>
    public static void SetLogger(ILogger logger) => _staticLogger = logger;

    /// <summary>
    /// Backward-compatible constructor preserving the old signature for external consumers.
    /// </summary>
    public BrokerRegistration(string project, string tfm, string platform, string appName, int brokerPort = DefaultBrokerPort, ILogger? logger = null)
        : this(project, tfm, platform, appName, sessionId: null, brokerPort, logger)
    {
    }

    public BrokerRegistration(string project, string tfm, string platform, string appName, string? sessionId, int brokerPort = DefaultBrokerPort, ILogger? logger = null)
    {
        _project = project;
        _tfm = tfm;
        _platform = platform;
        _appName = appName;
        _sessionId = sessionId;
        _brokerPort = brokerPort;
        _logger = logger;
        _agentId = ComputeId(project, tfm, sessionId, Environment.ProcessId);
    }

    /// <summary>
    /// Attempts to connect to the broker and register. Returns the assigned port, or null if broker is unavailable.
    /// If the broker is unavailable, starts background retries so the agent registers when the broker comes up later.
    /// </summary>
    public async Task<int?> TryRegisterAsync(TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(3);
        _cts = new CancellationTokenSource();
        
        var logger = _logger ?? _staticLogger;
        logger?.LogInformation("DevFlow agent connecting to broker at ws://localhost:{BrokerPort}/ws/agent", _brokerPort);

        try
        {
            _ws = new ClientWebSocket();

            using var connectCts = new CancellationTokenSource(timeout.Value);
            await _ws.ConnectAsync(new Uri($"ws://localhost:{_brokerPort}/ws/agent"), connectCts.Token);

            // Send registration
            var registration = BuildRegistrationJson();
            await _ws.SendAsync(Encoding.UTF8.GetBytes(registration), WebSocketMessageType.Text, true, connectCts.Token);

            // Read response
            var buffer = new byte[1024];
            var result = await _ws.ReceiveAsync(buffer, connectCts.Token);
            var response = JsonSerializer.Deserialize<RegistrationResponse>(
                Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (response?.Type == "registered" && response.Port > 0)
            {
                if (!string.IsNullOrWhiteSpace(response.Id))
                    _agentId = response.Id;
                _assignedPort = response.Port;
                logger?.LogInformation("DevFlow agent registered. Broker assigned port: {Port}", _assignedPort);

                // Start background task to keep connection alive
                _ = Task.Run(() => MonitorConnectionAsync(_cts.Token));

                return _assignedPort;
            }

            // Registration failed
            logger?.LogWarning("DevFlow agent failed to register with broker: Invalid response");
            _ws.Dispose();
            _ws = null;
            StartReconnection();
            return null;
        }
        catch (Exception ex)
        {
            // Broker unavailable — start background retries so we register when it comes up
            logger?.LogDebug("Broker unavailable at ws://localhost:{BrokerPort}/ws/agent — starting background reconnection: {Message}", _brokerPort, ex.Message);
            _ws?.Dispose();
            _ws = null;
            StartReconnection();
            return null;
        }
    }

    private async Task MonitorConnectionAsync(CancellationToken ct)
    {
        var buffer = new byte[256];
        try
        {
            while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch { }

        // Connection lost — start reconnection
        if (!_disposed && !ct.IsCancellationRequested)
        {
            var logger = _logger ?? _staticLogger;
            logger?.LogWarning("DevFlow agent lost broker connection — starting reconnection");
            StartReconnection();
        }
    }

    private void StartReconnection()
    {
        if (_disposed || _reconnectTimer != null) return;

        var delays = new[] { 2000, 5000, 10000, 15000 };
        var attempt = 0;
        var logger = _logger ?? _staticLogger;
        var lifetimeToken = _cts?.Token ?? CancellationToken.None;

        _reconnectTimer = new Timer(async _ =>
        {
            if (_disposed || lifetimeToken.IsCancellationRequested) return;

            attempt++;
            logger?.LogDebug("DevFlow agent reconnection attempt {Attempt} to broker...", attempt);

            ClientWebSocket? candidate = null;
            try
            {
                candidate = new ClientWebSocket();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                await candidate.ConnectAsync(new Uri($"ws://localhost:{_brokerPort}/ws/agent"), cts.Token);

                // Re-register
                var registration = BuildRegistrationJson();
                await candidate.SendAsync(Encoding.UTF8.GetBytes(registration), WebSocketMessageType.Text, true, cts.Token);

                var buffer = new byte[1024];
                var result = await candidate.ReceiveAsync(buffer, cts.Token);
                var response = JsonSerializer.Deserialize<RegistrationResponse>(
                    Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (response?.Type == "registered")
                {
                    ClientWebSocket? previous = null;
                    lock (_lifecycleGate)
                    {
                        if (_disposed || lifetimeToken.IsCancellationRequested)
                            return;
                        previous = _ws;
                        _ws = candidate;
                        candidate = null;
                        if (!string.IsNullOrWhiteSpace(response.Id))
                            _agentId = response.Id;
                        _assignedPort = response.Port;
                    }
                    previous?.Dispose();
                    logger?.LogInformation("DevFlow agent reconnected to broker after {Attempt} attempts", attempt);
                    _reconnectTimer?.Dispose();
                    _reconnectTimer = null;
                    _ = Task.Run(() => MonitorConnectionAsync(lifetimeToken));
                    return;
                }
            }
            catch (Exception ex)
            {
                logger?.LogDebug("DevFlow agent reconnection attempt {Attempt} failed — retrying: {Message}", attempt, ex.Message);
            }
            finally
            {
                candidate?.Dispose();
            }

            // Keep retrying with backoff up to 15s, indefinitely
            if (_disposed || lifetimeToken.IsCancellationRequested) return;
            var delay = delays[Math.Min(attempt, delays.Length - 1)];
            try { _reconnectTimer?.Change(delay, Timeout.Infinite); } catch { }
        }, null, 2000, Timeout.Infinite);
    }

    /// <summary>
    /// Computes the agent identity hash from project path and TFM.
    /// </summary>
    public static string ComputeId(string project, string tfm)
    {
        var input = $"{project}|{tfm}";
        return ComputeId(input);
    }

    /// <summary>
    /// Computes a process-specific agent identity while preserving the legacy identity
    /// for registrations that do not provide a process ID.
    /// </summary>
    public static string ComputeId(string project, string tfm, string? sessionId, int processId)
    {
        if (processId <= 0)
            return ComputeId(project, tfm);

        var input = $"{project}|{tfm}|{sessionId ?? ""}|{processId}";
        return ComputeId(input);
    }

    private static string ComputeId(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    internal async Task<MutationLeaseStatus?> ControlMutationLeaseAsync(MutationLeaseRequest request)
    {
        if (!HasBrokerAuthority)
            return null;

        try
        {
            var json = JsonSerializer.Serialize(request, _leaseJsonOptions);
            using var response = await PostBrokerAsync(
                $"/api/leases/{Uri.EscapeDataString(_agentId)}",
                json).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
                return MutationLeaseStatus.Failure("The DevFlow broker did not return a lease response.");

            var result = JsonSerializer.Deserialize<MutationLeaseStatus>(body, _leaseJsonOptions);
            if (result is null)
                return MutationLeaseStatus.Failure("The DevFlow broker returned an invalid lease response.");
            if (result is not null && !response.IsSuccessStatusCode)
                result.Ok = false;
            return result;
        }
        catch (Exception ex)
        {
            (_logger ?? _staticLogger)?.LogDebug(
                "DevFlow broker lease request failed: {Message}", ex.Message);
            return MutationLeaseStatus.Failure("The DevFlow broker lease authority is temporarily unavailable.");
        }
    }

    internal async Task<MutationRecordingStatus?> ControlMutationRecordingAsync(MutationRecordingRequest request)
    {
        if (!HasBrokerAuthority)
            return null;

        try
        {
            var json = JsonSerializer.Serialize(request, _leaseJsonOptions);
            using var response = await PostBrokerAsync(
                $"/api/recordings/{Uri.EscapeDataString(_agentId)}",
                json).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
                return null;
            var result = JsonSerializer.Deserialize<MutationRecordingStatus>(body, _leaseJsonOptions);
            if (result is not null && !response.IsSuccessStatusCode)
                result.Ok = false;
            return result;
        }
        catch (Exception ex)
        {
            (_logger ?? _staticLogger)?.LogDebug(
                "DevFlow broker recording request failed: {Message}", ex.Message);
            return null;
        }
    }

    private async Task<HttpResponseMessage> PostBrokerAsync(string path, string json)
    {
        for (var index = 0; index < _brokerHttpHosts.Length; index++)
        {
            var host = _brokerHttpHosts[index];
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _brokerHttp.PostAsync(
                    $"http://{host}:{_brokerPort}{path}",
                    content).ConfigureAwait(false);
                if (index == _brokerHttpHosts.Length - 1 ||
                    !ShouldTryAlternateBrokerHost(response.StatusCode))
                {
                    return response;
                }

                (_logger ?? _staticLogger)?.LogDebug(
                    "DevFlow broker rejected Host {Host} with status {StatusCode}; retrying alternate loopback host.",
                    host,
                    (int)response.StatusCode);
                response.Dispose();
            }
            catch (HttpRequestException ex) when (index < _brokerHttpHosts.Length - 1)
            {
                (_logger ?? _staticLogger)?.LogDebug(
                    "DevFlow broker request through Host {Host} failed; retrying alternate loopback host: {Message}",
                    host,
                    ex.Message);
            }
        }

        throw new HttpRequestException("The DevFlow broker was unavailable on all loopback hosts.");
    }

    private static async ValueTask<Stream> ConnectBrokerLoopbackAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        // The callback always dials IPv4 loopback. PostBrokerAsync varies the HTTP Host header
        // independently so it can match either literal-IP or localhost-only broker prefixes.
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
        try
        {
            await socket.ConnectAsync(IPAddress.Loopback, context.DnsEndPoint.Port, cancellationToken)
                .ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        ClientWebSocket? socket;
        Timer? reconnectTimer;
        CancellationTokenSource? lifetimeCts;
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            _disposed = true;
            reconnectTimer = _reconnectTimer;
            _reconnectTimer = null;
            lifetimeCts = _cts;
            socket = _ws;
            _ws = null;
            _assignedPort = null;
        }
        reconnectTimer?.Dispose();
        lifetimeCts?.Cancel();
        try
        {
            socket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Agent shutting down", CancellationToken.None)
                .Wait(TimeSpan.FromSeconds(1));
        }
        catch { }
        socket?.Dispose();
        lifetimeCts?.Dispose();
    }

    private string BuildRegistrationJson() => JsonSerializer.Serialize(new
    {
        type = "register",
        project = _project,
        tfm = _tfm,
        platform = _platform,
        appName = _appName,
        currentPort = CurrentPort,
        version = typeof(BrokerRegistration).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        sessionId = _sessionId,
        processId = Environment.ProcessId
    });

    private record RegistrationResponse
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = "";

        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("port")]
        public int Port { get; init; }
    }
}
