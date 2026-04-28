using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Central broker daemon that manages agent registration and port assignment.
/// Agents connect via WebSocket; CLI queries via HTTP.
/// </summary>
public class BrokerServer : IDisposable
{
    public const int DefaultPort = 19223;
    public const int PortRangeStart = 10223;
    public const int PortRangeEnd = 10899;

    private readonly int _port;
    private readonly TimeSpan _idleTimeout;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, AgentConnection> _agents = new();
    private readonly HashSet<int> _assignedPorts = new();
    private readonly object _portLock = new();
    private DateTime _lastActivity = DateTime.UtcNow;
    private Timer? _idleTimer;
    private bool _disposed;
    private Action<string>? _log;

    public int Port => _port;
    public int AgentCount => _agents.Count;
    public bool IsRunning => _listener?.IsListening ?? false;

    public BrokerServer(int port = DefaultPort, TimeSpan? idleTimeout = null, Action<string>? log = null)
    {
        _port = port;
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(5);
        _log = log;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            // Fallback for platforms where localhost doesn't work
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{_port}/");
            _listener.Start();
        }

        Log($"Broker started on port {_port} (PID {Environment.ProcessId})");

        // Write state file
        WriteBrokerState();

        // Start idle timer
        _idleTimer = new Timer(_ => CheckIdle(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync().WaitAsync(_cts.Token);
                _ = HandleRequestAsync(context);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            Shutdown();
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        TouchActivity();

        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            var method = context.Request.HttpMethod;

            // WebSocket upgrade for agents
            if (context.Request.IsWebSocketRequest && path == "/ws/agent")
            {
                await HandleAgentWebSocket(context);
                return;
            }

            // HTTP endpoints for CLI
            var (statusCode, body) = (method, path) switch
            {
                ("GET", "/api/health") => (200, CliJson.SerializeUntyped(new JsonObject
                {
                    ["status"] = "ok",
                    ["agents"] = _agents.Count
                }, indented: false)),
                ("GET", "/api/agents") => (200, HandleListAgents()),
                ("POST", "/api/shutdown") => HandleShutdown(),
                _ => (404, CliJson.SerializeUntyped(new JsonObject
                {
                    ["error"] = "Not found"
                }, indented: false))
            };

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            var responseBytes = Encoding.UTF8.GetBytes(body);
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            Log($"Error handling request: {ex.Message}");
            try { context.Response.Close(); } catch { }
        }
    }

    private async Task HandleAgentWebSocket(HttpListenerContext context)
    {
        WebSocketContext wsContext;
        try
        {
            wsContext = await context.AcceptWebSocketAsync(null);
        }
        catch (Exception ex)
        {
            Log($"WebSocket accept failed: {ex.Message}");
            context.Response.StatusCode = 500;
            context.Response.Close();
            return;
        }

        var ws = wsContext.WebSocket;
        var buffer = new byte[4096];

        try
        {
            // Read registration message
            var result = await ws.ReceiveAsync(buffer, _cts?.Token ?? CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) return;

            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var registration = CliJson.Deserialize<RegistrationMessage>(message);
            if (registration == null || registration.Type != "register")
            {
                await ws.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "Expected register message", CancellationToken.None);
                return;
            }

            var id = AgentRegistration.ComputeId(registration.Project, registration.Tfm);

            // If the agent already has an HTTP listener (late reconnection), use its current port
            int assignedPort;
            if (registration.CurrentPort is > 0)
            {
                assignedPort = registration.CurrentPort.Value;
            }
            else
            {
                var newPort = AssignPort();
                if (newPort == null)
                {
                    var errorMsg = CliJson.SerializeUntyped(new JsonObject
                    {
                        ["type"] = "error",
                        ["message"] = "No ports available"
                    }, indented: false);
                    await ws.SendAsync(Encoding.UTF8.GetBytes(errorMsg), WebSocketMessageType.Text, true, CancellationToken.None);
                    await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, "No ports available", CancellationToken.None);
                    return;
                }
                assignedPort = newPort.Value;
            }

            var agent = new AgentRegistration
            {
                Id = id,
                Project = registration.Project,
                Tfm = registration.Tfm,
                Platform = registration.Platform,
                AppName = registration.AppName,
                Port = assignedPort,
                Version = registration.Version,
                SessionId = registration.SessionId,
                ConnectedAt = DateTime.UtcNow
            };

            // Remove existing registration for same id (app restarted)
            if (_agents.TryRemove(id, out var existing))
            {
                if (existing.Registration.Port != assignedPort)
                    ReleasePort(existing.Registration.Port);
                try { existing.WebSocket.Dispose(); } catch { }
                Log($"Agent replaced: {agent.AppName}|{agent.Tfm} (was port {existing.Registration.Port})");
            }

            var connection = new AgentConnection(agent, ws);
            _agents[id] = connection;

            Log($"Agent connected: {agent.AppName}|{agent.Tfm} → port {assignedPort} (id: {id})");

            // Send registration response
            var response = CliJson.SerializeUntyped(new JsonObject
            {
                ["type"] = "registered",
                ["id"] = id,
                ["port"] = assignedPort
            }, indented: false);
            await ws.SendAsync(Encoding.UTF8.GetBytes(response), WebSocketMessageType.Text, true, CancellationToken.None);

            // Keep connection alive — wait for disconnect
            await MonitorAgentConnection(connection);
        }
        catch (WebSocketException) { }
        catch (OperationCanceledException) { }
        finally
        {
            ws.Dispose();
        }
    }

    private async Task MonitorAgentConnection(AgentConnection connection)
    {
        var buffer = new byte[256];
        try
        {
            while (connection.WebSocket.State == WebSocketState.Open && !(_cts?.Token.IsCancellationRequested ?? true))
            {
                var result = await connection.WebSocket.ReceiveAsync(buffer, _cts?.Token ?? CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                TouchActivity();
            }
        }
        catch { }
        finally
        {
            if (_agents.TryRemove(connection.Registration.Id, out _))
            {
                ReleasePort(connection.Registration.Port);
                Log($"Agent disconnected: {connection.Registration.AppName}|{connection.Registration.Tfm}");
            }
        }
    }

    private string HandleListAgents()
    {
        var agents = _agents.Values.Select(c => c.Registration).ToArray();
        return CliJson.SerializeUntyped(agents, indented: true);
    }

    private (int, string) HandleShutdown()
    {
        Log("Shutdown requested via API");
        _ = Task.Run(async () =>
        {
            await Task.Delay(100); // Let response send first
            _cts?.Cancel();
        });
        return (200, CliJson.SerializeUntyped(new JsonObject
        {
            ["status"] = "shutting_down"
        }, indented: false));
    }

    private int? AssignPort()
    {
        lock (_portLock)
        {
            for (int port = PortRangeStart; port <= PortRangeEnd; port++)
            {
                if (_assignedPorts.Contains(port)) continue;
                if (IsPortInUse(port)) continue;
                _assignedPorts.Add(port);
                return port;
            }
        }
        return null;
    }

    private void ReleasePort(int port)
    {
        lock (_portLock)
        {
            _assignedPorts.Remove(port);
        }
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return false;
        }
        catch
        {
            return true;
        }
    }

    private void TouchActivity() => _lastActivity = DateTime.UtcNow;

    private void CheckIdle()
    {
        if (_agents.Count > 0) return;
        if (DateTime.UtcNow - _lastActivity < _idleTimeout) return;

        Log("Idle timeout reached, shutting down");
        _cts?.Cancel();
    }

    private void Shutdown()
    {
        _idleTimer?.Dispose();

        // Close all agent WebSockets
        foreach (var agent in _agents.Values)
        {
            try
            {
                agent.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Broker shutting down", CancellationToken.None)
                    .Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
            agent.WebSocket.Dispose();
        }
        _agents.Clear();

        // Delete state file
        DeleteBrokerState();

        try { _listener?.Close(); } catch { }

        Log("Broker stopped");
    }

    private void WriteBrokerState()
    {
        try
        {
            var dir = BrokerPaths.ConfigDir;
            Directory.CreateDirectory(dir);

            var state = new BrokerState
            {
                Pid = Environment.ProcessId,
                Port = _port,
                StartedAt = DateTime.UtcNow
            };

            var json = CliJson.SerializeUntyped(state, indented: true);
            var tmpPath = BrokerPaths.StateFile + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, BrokerPaths.StateFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log($"Warning: failed to write broker state: {ex.Message}");
        }
    }

    private static void DeleteBrokerState()
    {
        try { File.Delete(BrokerPaths.StateFile); } catch { }
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message}";
        try { _log?.Invoke(line); } catch { }

        try
        {
            var logFile = BrokerPaths.LogFile;
            Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);

            // Truncate if > 1MB
            if (File.Exists(logFile) && new FileInfo(logFile).Length > 1_000_000)
                File.WriteAllText(logFile, "");

            File.AppendAllText(logFile, line + Environment.NewLine);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _idleTimer?.Dispose();
        try { _listener?.Close(); } catch { }
        _cts?.Dispose();
    }
    private record AgentConnection(AgentRegistration Registration, WebSocket WebSocket);
}
