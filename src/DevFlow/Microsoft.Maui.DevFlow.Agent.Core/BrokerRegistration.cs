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
    private readonly string _project;
    private readonly string _tfm;
    private readonly string _platform;
    private readonly string _appName;
    private int _brokerPort;
    private int? _assignedPort;
    private ILogger? _logger;
    private static ILogger? _staticLogger;

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

    /// <summary>
    /// Sets the static logger to be used by all BrokerRegistration instances that don't have an instance logger.
    /// </summary>
    public static void SetLogger(ILogger logger) => _staticLogger = logger;

    public BrokerRegistration(string project, string tfm, string platform, string appName, int brokerPort = DefaultBrokerPort, ILogger? logger = null)
    {
        _project = project;
        _tfm = tfm;
        _platform = platform;
        _appName = appName;
        _brokerPort = brokerPort;
        _logger = logger;
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

        _reconnectTimer = new Timer(async _ =>
        {
            if (_disposed) return;

            attempt++;
            logger?.LogDebug("DevFlow agent reconnection attempt {Attempt} to broker...", attempt);

            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _ws.ConnectAsync(new Uri($"ws://localhost:{_brokerPort}/ws/agent"), cts.Token);

                // Re-register
                var registration = BuildRegistrationJson();
                await _ws.SendAsync(Encoding.UTF8.GetBytes(registration), WebSocketMessageType.Text, true, cts.Token);

                var buffer = new byte[1024];
                var result = await _ws.ReceiveAsync(buffer, cts.Token);
                var response = JsonSerializer.Deserialize<RegistrationResponse>(
                    Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (response?.Type == "registered")
                {
                    _assignedPort = response.Port;
                    logger?.LogInformation("DevFlow agent reconnected to broker after {Attempt} attempts", attempt);
                    _reconnectTimer?.Dispose();
                    _reconnectTimer = null;
                    _ = Task.Run(() => MonitorConnectionAsync(_cts?.Token ?? CancellationToken.None));
                    return;
                }
            }
            catch (Exception ex)
            {
                logger?.LogDebug("DevFlow agent reconnection attempt {Attempt} failed — retrying: {Message}", attempt, ex.Message);
            }

            // Keep retrying with backoff up to 15s, indefinitely
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
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reconnectTimer?.Dispose();
        _cts?.Cancel();
        try
        {
            _ws?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Agent shutting down", CancellationToken.None)
                .Wait(TimeSpan.FromSeconds(1));
        }
        catch { }
        _ws?.Dispose();
        _cts?.Dispose();
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
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
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
