using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Testing;

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
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _agentRouteGates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _agentStateGates = new(StringComparer.Ordinal);
    private readonly MutationLeaseRegistry _mutationLeases;
    private readonly BrokerFlowCoordinator _flows;
    private readonly RouteCheckpointCoordinator _checkpoints;
    private readonly WorkflowRunCoordinator _workflowRuns;
    private readonly HashSet<int> _assignedPorts = new();
    private readonly object _portLock = new();
    private DateTime _lastActivity = DateTime.UtcNow;
    private Timer? _idleTimer;
    private bool _disposed;
    private Action<string>? _log;

    public int Port => _port;
    public int AgentCount => _agents.Count;
    public bool IsRunning => _listener?.IsListening ?? false;

    public BrokerServer(
        int port = DefaultPort,
        TimeSpan? idleTimeout = null,
        Action<string>? log = null)
        : this(
            port,
            idleTimeout,
            log,
            checkpointStore: null,
            recordingStorageRoot: null,
            clock: null)
    {
    }

    internal BrokerServer(
        int port,
        TimeSpan? idleTimeout,
        Action<string>? log,
        RouteCheckpointStore? checkpointStore,
        string? recordingStorageRoot,
        TimeProvider? clock)
    {
        _port = port;
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(5);
        _log = log;
        _flows = new BrokerFlowCoordinator(
            new FlowRecordingStore(clock),
            new FlowRecordingSpoolStore(recordingStorageRoot, clock, warning => Log("Warning: " + warning)));
        _checkpoints = new RouteCheckpointCoordinator(checkpointStore);
        _mutationLeases = new MutationLeaseRegistry();
        _workflowRuns = new WorkflowRunCoordinator(
            _mutationLeases,
            ExecuteWorkflowRunAsync,
            new WorkflowRunCoordinatorOptions
            {
                ArtifactRoot = string.IsNullOrWhiteSpace(recordingStorageRoot)
                    ? null
                    : Path.Combine(recordingStorageRoot, "workflow-runs"),
            },
            clock: clock,
            controlLedger: ControlWorkflowRunLedgerAsync);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        // Also accept a literal 127.0.0.1 Host header: HTTP.sys routes by the request's Host,
        // and a bare "localhost" prefix rejects "127.0.0.1" with 400 "Invalid Hostname". Some
        // local clients connect with a 127.0.0.1 Host, so register the loopback IP too.
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            // The 127.0.0.1 prefix can require a URL ACL reservation on some machines, whereas the
            // "localhost" prefix is always permitted without elevation. Retry with localhost only
            // before resorting to the strong wildcard (which itself needs elevation), so adding the
            // loopback-IP prefix can never regress startup on a stock machine.
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Start();
            }
            catch (HttpListenerException)
            {
                // Last-resort fallback for platforms where the loopback prefixes don't bind.
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://+:{_port}/");
                _listener.Start();
            }
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

            // Defense-in-depth: the broker is designed to be reachable only on
            // loopback, but HttpListener falls back to binding on all interfaces
            // (http://+:port/) when localhost reservation fails — see line 56-60
            // below. In that fallback, non-browser HTTP clients on the LAN (curl,
            // scripts, attacker) can reach this port without sending an Origin
            // header, so the Origin check alone (further down) doesn't help.
            // Reject any caller whose RemoteEndPoint isn't a loopback address.
            // Legitimate uses (CLI tool, inspector UI in a local browser, MAUI
            // agent running on the same machine, Android emulator port-forwarded
            // back to host loopback) all use 127.0.0.1 or ::1.
            var remoteIp = context.Request.RemoteEndPoint?.Address;
            if (remoteIp == null || !IPAddress.IsLoopback(remoteIp))
            {
                context.Response.StatusCode = 403;
                context.Response.ContentType = "text/plain";
                var msg = Encoding.UTF8.GetBytes("Forbidden: loopback required");
                context.Response.ContentLength64 = msg.Length;
                await context.Response.OutputStream.WriteAsync(msg);
                context.Response.Close();
                return;
            }

            // WebSocket upgrade for agents
            if (context.Request.IsWebSocketRequest && path == "/ws/agent")
            {
                await HandleAgentWebSocket(context);
                return;
            }

            // WebSocket upgrade for inspector event relay
            if (context.Request.IsWebSocketRequest && path.StartsWith("/inspector", StringComparison.OrdinalIgnoreCase))
            {
                await HandleInspectorRoute(context, path);
                return;
            }

            // HTTP endpoints for CLI
            // Block state-mutating endpoints from non-loopback origins BEFORE dispatching
            // the handler — otherwise a cross-origin POST to /api/shutdown would still
            // tear down the broker even though we return 403.
            var origin = context.Request.Headers["Origin"];
            if (method == "POST" && !LocalOriginValidator.IsAllowed(origin, _port))
            {
                context.Response.StatusCode = 403;
                context.Response.ContentType = "application/json";
                var forbidden = Encoding.UTF8.GetBytes(CliJson.SerializeUntyped(new JsonObject { ["error"] = "Forbidden origin" }, indented: false));
                context.Response.ContentLength64 = forbidden.Length;
                await context.Response.OutputStream.WriteAsync(forbidden);
                context.Response.Close();
                return;
            }

            if (path.StartsWith("/api/leases/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleMutationLeaseRoute(context, method, path);
                return;
            }
            if (path.StartsWith("/api/recordings/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleRecordingRoute(context, method, path);
                return;
            }
            if (path.StartsWith("/api/checkpoints/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleCheckpointRoute(context, method, path);
                return;
            }
            if (path.StartsWith("/api/workflow-runs", StringComparison.OrdinalIgnoreCase))
            {
                await HandleWorkflowRunRoute(context, method, path);
                return;
            }

            var (statusCode, body) = (method, path) switch
            {
                ("GET", "/api/health") => (200, CliJson.SerializeUntyped(new JsonObject
                {
                    ["status"] = "ok",
                    ["agents"] = _agents.Count
                }, indented: false)),
                ("GET", "/api/agents") => (200, HandleListAgents()),
                ("POST", "/api/shutdown") => HandleShutdown(),
                // Browsers auto-request /favicon.ico; answer 204 so the inspector page doesn't log a 404.
                ("GET", "/favicon.ico") => (204, ""),
                _ => (0, "") // handled below for inspector routes
            };

            // Inspector routes — serve the web inspector for connected agents
            if (statusCode == 0)
            {
                if (path.StartsWith("/inspector", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleInspectorRoute(context, path);
                    return;
                }

                statusCode = 404;
                body = CliJson.SerializeUntyped(new JsonObject { ["error"] = "Not found" }, indented: false);
            }

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            // Mirror Origin only for loopback callers; the previous wildcard let any web
            // page read /api/agents (leaking IDs) and POST /api/shutdown.
            if (LocalOriginValidator.IsAllowed(origin, _port) && !string.IsNullOrEmpty(origin) && origin != "null")
            {
                context.Response.Headers.Add("Access-Control-Allow-Origin", origin);
                context.Response.Headers.Add("Vary", "Origin");
            }

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
        // Reject cross-origin WebSocket connections; only the local agent process
        // or CLI tools (no Origin header) may register.
        var origin = context.Request.Headers["Origin"];
        if (!LocalOriginValidator.IsAllowed(origin, _port))
        {
            context.Response.StatusCode = 403;
            context.Response.Close();
            return;
        }

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
        AgentConnection? publishedConnection = null;

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

            var id = AgentRegistration.ComputeId(
                registration.PackageId ?? registration.Project,
                registration.Tfm,
                registration.SessionId,
                registration.ProcessId);

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
                PackageId = registration.PackageId,
                Port = assignedPort,
                Version = registration.Version,
                SessionId = registration.SessionId,
                ProcessId = registration.ProcessId,
                InstanceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                ConnectedAt = DateTime.UtcNow
            };

            var connection = new AgentConnection(agent, ws);
            var routeGate = AgentRouteGate(id);
            await routeGate.WaitAsync(_cts?.Token ?? CancellationToken.None);
            try
            {
                var replaced = ReplaceConnection(_agents, id, connection);
                if (_inspectors.TryRemove(id, out var staleInspector))
                    staleInspector.Dispose();
                if (replaced is not null)
                {
                    _workflowRuns.MarkAgentInstanceUnavailable(
                        replaced.Registration.Id,
                        replaced.Registration.InstanceId,
                        "The agent reconnected with a new instance.");
                    if (replaced.Registration.Port != assignedPort)
                        ReleasePort(replaced.Registration.Port);
                    try { replaced.WebSocket.Dispose(); } catch { }
                    Log($"Agent replaced: {agent.AppName}|{agent.Tfm} (was port {replaced.Registration.Port})");
                }
            }
            finally
            {
                routeGate.Release();
            }
            publishedConnection = connection;

            Log($"Agent connected: {agent.AppName}|{agent.Tfm} → port {assignedPort} (id: {id})");

            // Send registration response
            var response = CliJson.SerializeUntyped(new JsonObject
            {
                ["type"] = "registered",
                ["id"] = id,
                ["port"] = assignedPort,
                ["instanceId"] = agent.InstanceId
            }, indented: false);
            await ws.SendAsync(Encoding.UTF8.GetBytes(response), WebSocketMessageType.Text, true, CancellationToken.None);

            // Keep connection alive — wait for disconnect
            await MonitorAgentConnection(connection);
        }
        catch (WebSocketException) { }
        catch (OperationCanceledException) { }
        finally
        {
            if (publishedConnection is not null)
                await CleanupAgentConnectionAsync(publishedConnection);
            ws.Dispose();
        }
    }

    internal static TConnection? ReplaceConnection<TConnection>(
        ConcurrentDictionary<string, TConnection> connections,
        string id,
        TConnection connection)
        where TConnection : class
    {
        while (true)
        {
            if (connections.TryGetValue(id, out var existing))
            {
                if (connections.TryUpdate(id, connection, existing))
                    return existing;
                continue;
            }

            if (connections.TryAdd(id, connection))
                return null;
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
    }

    private async Task CleanupAgentConnectionAsync(AgentConnection connection)
    {
        // Use the KeyValuePair overload so a reconnecting agent that re-registered with the same ID
        // cannot be evicted by stale cleanup from the superseded socket.
        var routeGate = AgentRouteGate(connection.Registration.Id);
        await routeGate.WaitAsync();
        try
        {
            if (_agents.TryRemove(new KeyValuePair<string, AgentConnection>(connection.Registration.Id, connection)))
            {
                var stateGate = AgentStateGate(connection.Registration.Id);
                await stateGate.WaitAsync();
                try
                {
                    ReleasePort(connection.Registration.Port);
                    _mutationLeases.Remove(connection.Registration.Id);
                    _flows.RemoveAgent(connection.Registration.Id);
                    _workflowRuns.MarkAgentInstanceUnavailable(
                        connection.Registration.Id,
                        connection.Registration.InstanceId,
                        "The agent disconnected.");
                    if (_inspectors.TryRemove(connection.Registration.Id, out var inspector))
                        inspector.Dispose();
                    Log($"Agent disconnected: {connection.Registration.AppName}|{connection.Registration.Tfm}");
                }
                finally
                {
                    stateGate.Release();
                }
            }
        }
        finally
        {
            routeGate.Release();
        }
    }

    private SemaphoreSlim AgentRouteGate(string agentId)
        => _agentRouteGates.GetOrAdd(agentId, static _ => new SemaphoreSlim(1, 1));

    private SemaphoreSlim AgentStateGate(string agentId)
        => _agentStateGates.GetOrAdd(agentId, static _ => new SemaphoreSlim(1, 1));

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
        _mutationLeases.Clear();
        _flows.Clear();
        _workflowRuns.Dispose();

        // Dispose inspector instances. Without this, a Shutdown() that doesn't
        // go through Dispose() (e.g. /api/shutdown handler or idle timeout)
        // leaks every InspectorServer's AgentClient (HttpClient) and CTS.
        foreach (var insp in _inspectors.Values)
        {
            try { insp.Dispose(); } catch { }
        }
        _inspectors.Clear();

        // Delete state file
        DeleteBrokerState();

        try { _listener?.Close(); } catch { }

        Log("Broker stopped");
    }

    private void WriteBrokerState()
    {
        var tmpPath = BrokerPaths.StateFile + ".tmp";
        try
        {
            var dir = BrokerPaths.ConfigDir;
            Directory.CreateDirectory(dir);
            BrokerPaths.RestrictConfigDirectoryPermissions(dir);

            var state = new BrokerState
            {
                Pid = Environment.ProcessId,
                Port = _port,
                StartedAt = DateTime.UtcNow,
                EmbedToken = _embedToken
            };

            var json = CliJson.SerializeUntyped(state, indented: true);
            File.WriteAllText(tmpPath, json);
            BrokerPaths.RestrictStateFilePermissions(tmpPath);
            File.Move(tmpPath, BrokerPaths.StateFile, overwrite: true);
        }
        catch (Exception ex)
        {
            try { File.Delete(tmpPath); } catch { }
            Log($"Warning: failed to write broker state: {ex.Message}");
        }
    }

    private async Task HandleMutationLeaseRoute(HttpListenerContext context, string method, string path)
    {
        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonResponseAsync(context, 405, new JsonObject { ["error"] = "Method not allowed" });
            return;
        }

        var segments = path.Trim('/').Split('/');
        if (segments.Length != 3 ||
            !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) ||
            !segments[1].Equals("leases", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonResponseAsync(context, 404, new JsonObject { ["error"] = "Not found" });
            return;
        }

        var agentId = Uri.UnescapeDataString(segments[2]);

        if (context.Request.ContentLength64 > 64 * 1024)
        {
            await WriteJsonResponseAsync(context, 413, new JsonObject { ["error"] = "Request body too large" });
            return;
        }

        JsonObject body;
        try
        {
            var text = await ReadBoundedBodyAsync(
                context.Request.InputStream,
                context.Request.ContentEncoding ?? Encoding.UTF8,
                64 * 1024);
            body = string.IsNullOrWhiteSpace(text)
                ? new JsonObject()
                : JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
        }
        catch (RequestBodyTooLargeException)
        {
            await WriteJsonResponseAsync(context, 413, new JsonObject { ["error"] = "Request body too large" });
            return;
        }
        catch
        {
            await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = "Invalid JSON body" });
            return;
        }

        var action = body["action"]?.GetValue<string>()?.Trim().ToLowerInvariant() ?? "status";
        var leaseId = body["leaseId"]?.GetValue<string>();
        var holderKind = body["holderKind"]?.GetValue<string>();
        var label = body["label"]?.GetValue<string>();
        var force = body["force"]?.GetValue<bool>() ?? false;
        var transactionId = body["transactionId"]?.GetValue<string>();

        MutationLeaseSnapshot status;
        var stateGate = AgentStateGate(agentId);
        await stateGate.WaitAsync(_cts?.Token ?? CancellationToken.None);
        try
        {
            if (!_agents.ContainsKey(agentId))
            {
                await WriteJsonResponseAsync(context, 404, new JsonObject { ["error"] = $"Agent '{agentId}' not found" });
                return;
            }
            status = _mutationLeases.Control(agentId, action, leaseId, holderKind, label, force, transactionId);
        }
        catch (ArgumentException ex)
        {
            await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = ex.Message });
            return;
        }
        finally
        {
            stateGate.Release();
        }

        await WriteJsonResponseAsync(context, 200, new JsonObject
        {
            ["ok"] = true,
            ["allowed"] = status.Allowed,
            ["youHold"] = status.YouHold,
            ["heldByOther"] = status.HeldByOther,
            ["leaseId"] = status.LeaseId,
            ["transactionId"] = status.TransactionId,
            ["holderKind"] = status.HolderKind,
            ["label"] = status.Label,
            ["expiresInMs"] = status.ExpiresInMs,
            ["authorityEpoch"] = status.AuthorityEpoch,
            ["authority"] = "broker"
        });
    }

    private static async Task WriteJsonResponseAsync(HttpListenerContext context, int statusCode, JsonObject body)
    {
        var bytes = Encoding.UTF8.GetBytes(CliJson.SerializeUntyped(body, indented: false));
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private async Task HandleRecordingRoute(HttpListenerContext context, string method, string path)
    {
        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonResponseAsync(context, 405, new JsonObject { ["error"] = "Method not allowed" });
            return;
        }

        var segments = path.Trim('/').Split('/');
        if (segments.Length != 3 ||
            !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) ||
            !segments[1].Equals("recordings", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonResponseAsync(context, 404, new JsonObject { ["error"] = "Not found" });
            return;
        }

        var agentId = Uri.UnescapeDataString(segments[2]);
        if (context.Request.ContentLength64 > 128 * 1024)
        {
            await WriteJsonResponseAsync(context, 413, new JsonObject { ["error"] = "Request body too large" });
            return;
        }

        JsonObject body;
        try
        {
            var text = await ReadBoundedBodyAsync(
                context.Request.InputStream,
                context.Request.ContentEncoding ?? Encoding.UTF8,
                128 * 1024);
            body = string.IsNullOrWhiteSpace(text)
                ? new JsonObject()
                : JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
        }
        catch (RequestBodyTooLargeException)
        {
            await WriteJsonResponseAsync(context, 413, new JsonObject { ["error"] = "Request body too large" });
            return;
        }
        catch
        {
            await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = "Invalid JSON body" });
            return;
        }

        var action = body["action"]?.GetValue<string>()?.Trim().ToLowerInvariant() ?? "status";
        var leaseId = body["leaseId"]?.GetValue<string>();
        var recordingId = body["recordingId"]?.GetValue<string>();
        if (!action.Equals("status", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(leaseId))
        {
            await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = "leaseId is required" });
            return;
        }
        if (!action.Equals("status", StringComparison.Ordinal))
        {
            var lease = _mutationLeases.Control(agentId, "validate", leaseId, null, null, force: false);
            if (!lease.Allowed)
            {
                await WriteJsonResponseAsync(context, 409, new JsonObject
                {
                    ["ok"] = false,
                    ["reason"] = "lease",
                    ["error"] = "Another session is driving this app.",
                    ["holderKind"] = lease.HolderKind,
                    ["label"] = lease.Label
                });
                return;
            }
        }

        BrokerFlowResult result;
        var stateGate = AgentStateGate(agentId);
        await stateGate.WaitAsync(_cts?.Token ?? CancellationToken.None);
        try
        {
            if (!_agents.TryGetValue(agentId, out var connection))
            {
                await WriteJsonResponseAsync(context, 404, new JsonObject { ["error"] = $"Agent '{agentId}' not found" });
                return;
            }
            var flowAgentId = connection.Registration.Id;
            var stableFlowAgentId = RouteCheckpointCoordinator.StableAgentId(connection.Registration);
            _flows.ConnectAgent(
                flowAgentId,
                stableFlowAgentId,
                connection.Registration.SessionId,
                recordingId);

            switch (action)
            {
                case "start":
                    result = _flows.Start(
                        flowAgentId,
                        body["name"]?.GetValue<string>() ?? "scenario",
                        body["app"]?.GetValue<string>() ?? connection.Registration.AppName,
                        body["platform"]?.GetValue<string>() ?? connection.Registration.Platform,
                        body["preconditions"]?.GetValue<string>(),
                        connection.Registration.SessionId,
                        stableFlowAgentId);
                    break;
                case "status":
                    result = _flows.Status(flowAgentId, recordingId);
                    break;
                case "observe":
                    var observationNode = body["observation"];
                    var observation = observationNode?.Deserialize<FlowObservation>(
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    result = observation is null
                        ? BrokerFlowResult.Failure("observation is required")
                        : _flows.Observe(flowAgentId, observation, recordingId);
                    break;
                case "stop":
                    result = _flows.Stop(flowAgentId, recordingId);
                    break;
                case "cancel":
                    result = _flows.Cancel(flowAgentId, recordingId);
                    break;
                case "cancel-if-empty":
                    result = _flows.CancelIfEmpty(flowAgentId, recordingId);
                    break;
                default:
                    result = BrokerFlowResult.Failure($"Unknown recording action '{action}'.");
                    break;
            }
        }
        finally
        {
            stateGate.Release();
        }

        var resultNode = JsonSerializer.SerializeToNode(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        })?.AsObject() ?? new JsonObject();
        await WriteJsonResponseAsync(context, result.Ok ? 200 : 400, resultNode);
    }

    /// <summary>
    /// Broker-owned local route checkpoint API. Unlike workflow recording this deliberately does
    /// not require a mutation lease: save/restore/clear are explicit user commands, and restore is
    /// never performed by connection lifecycle code.
    /// </summary>
    private async Task HandleCheckpointRoute(HttpListenerContext context, string method, string path)
    {
        var segments = path.Trim('/').Split('/');
        if (segments.Length != 3 ||
            !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) ||
            !segments[1].Equals("checkpoints", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonResponseAsync(context, 404, new JsonObject { ["error"] = "Not found" });
            return;
        }

        var agentId = Uri.UnescapeDataString(segments[2]);
        var action = method.Equals("GET", StringComparison.OrdinalIgnoreCase) ? "status" : null;
        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            if (context.Request.ContentLength64 > 16 * 1024)
            {
                await WriteJsonResponseAsync(context, 413, new JsonObject { ["error"] = "Request body too large" });
                return;
            }
            try
            {
                var text = await ReadBoundedBodyAsync(
                    context.Request.InputStream,
                    context.Request.ContentEncoding ?? Encoding.UTF8,
                    16 * 1024);
                action = string.IsNullOrWhiteSpace(text)
                    ? "status"
                    : JsonNode.Parse(text)?["action"]?.GetValue<string>()?.Trim().ToLowerInvariant();
            }
            catch
            {
                await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = "Invalid JSON body" });
                return;
            }
        }
        else if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonResponseAsync(context, 405, new JsonObject { ["error"] = "Method not allowed" });
            return;
        }

        if (!_agents.TryGetValue(agentId, out var connection))
        {
            await WriteJsonResponseAsync(context, 404, new JsonObject
            {
                ["ok"] = false,
                ["error"] = $"Agent '{agentId}' is not connected. Checkpoints are retained until explicitly cleared or expired."
            });
            return;
        }

        RouteCheckpointStatus status;
        try
        {
            status = action switch
            {
                "status" => _checkpoints.Status(connection.Registration),
                "save" => await _checkpoints.SaveAsync(connection.Registration, _cts?.Token ?? CancellationToken.None),
                "restore" => await _checkpoints.RestoreAsync(connection.Registration, _cts?.Token ?? CancellationToken.None),
                "clear" => ClearCheckpoint(connection.Registration),
                _ => new RouteCheckpointStatus
                {
                    Ok = false,
                    Connected = true,
                    Warning = $"Unknown checkpoint action '{action}'."
                }
            };
        }
        catch (Exception ex)
        {
            Log($"Warning: checkpoint {action} failed for {agentId}: {ex.Message}");
            status = new RouteCheckpointStatus
            {
                Ok = false,
                Connected = true,
                Warning = "Checkpoint operation failed."
            };
        }

        var node = JsonSerializer.SerializeToNode(status, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        })?.AsObject() ?? new JsonObject();
        await WriteJsonResponseAsync(context, status.Ok ? 200 : 400, node);
    }

    private RouteCheckpointStatus ClearCheckpoint(AgentRegistration registration)
    {
        _checkpoints.Clear(registration);
        return new RouteCheckpointStatus { Connected = true, HasCheckpoint = false };
    }

    private async Task HandleWorkflowRunRoute(HttpListenerContext context, string method, string path)
    {
        const int maxBodyChars = 1_048_576;
        var normalizedPath = path.TrimEnd('/');

        if (string.Equals(normalizedPath, "/api/workflow-runs/capabilities", StringComparison.OrdinalIgnoreCase))
        {
            if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    405,
                    WorkflowRunStatusResponse.Failure("Method not allowed."));
                return;
            }

            await WriteTypedJsonResponseAsync(context, 200, _workflowRuns.GetCapabilities());
            return;
        }

        if (string.Equals(normalizedPath, "/api/workflow-runs/start", StringComparison.OrdinalIgnoreCase))
        {
            if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    405,
                    WorkflowRunStatusResponse.Failure("Method not allowed."));
                return;
            }

            var request = await ReadWorkflowRunBodyAsync<WorkflowRunStartRequest>(context, maxBodyChars);
            if (request is null)
                return;

            if (string.IsNullOrWhiteSpace(request.AgentId) || string.IsNullOrWhiteSpace(request.AgentInstanceId))
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    400,
                    WorkflowRunStartResult.Rejected(400, "agentId and agentInstanceId are required.", null, null));
                return;
            }

            var routeGate = AgentRouteGate(request.AgentId);
            await routeGate.WaitAsync(_cts?.Token ?? CancellationToken.None);
            try
            {
                if (!_agents.TryGetValue(request.AgentId, out var connection) ||
                    !string.Equals(
                        connection.Registration.InstanceId,
                        request.AgentInstanceId,
                        StringComparison.Ordinal))
                {
                    await WriteTypedJsonResponseAsync(
                        context,
                        409,
                        WorkflowRunStartResult.Conflict(
                            "The requested agent instance is stale or no longer connected."));
                    return;
                }

                var target = CreateWorkflowRunTarget(connection.Registration);
                var result = _workflowRuns.Start(
                    request,
                    target,
                    () => IsCurrentAgentConnection(connection));
                await WriteTypedJsonResponseAsync(context, result.StatusCode, result);
            }
            finally
            {
                routeGate.Release();
            }
            return;
        }

        var segments = normalizedPath.Trim('/').Split('/');
        if (segments.Length != 4 ||
            !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) ||
            !segments[1].Equals("workflow-runs", StringComparison.OrdinalIgnoreCase))
        {
            await WriteTypedJsonResponseAsync(context, 404, WorkflowRunStatusResponse.Failure("Not found."));
            return;
        }

        var runId = Uri.UnescapeDataString(segments[2]);
        var action = segments[3];
        if (string.Equals(action, "status", StringComparison.OrdinalIgnoreCase))
        {
            if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    405,
                    WorkflowRunStatusResponse.Failure("Method not allowed."));
                return;
            }

            var request = await ReadWorkflowRunBodyAsync<WorkflowRunAccessRequest>(context, maxBodyChars);
            if (request is null)
                return;

            var result = _workflowRuns.GetStatus(runId, request.CapabilityToken);
            await WriteTypedJsonResponseAsync(
                context,
                result.StatusCode,
                result.Run is null
                    ? WorkflowRunStatusResponse.Failure(result.Error ?? "Workflow run was not found.")
                    : WorkflowRunStatusResponse.Success(result.Run));
            return;
        }

        if (string.Equals(action, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    405,
                    WorkflowRunStatusResponse.Failure("Method not allowed."));
                return;
            }

            var request = await ReadWorkflowRunBodyAsync<WorkflowRunAccessRequest>(context, maxBodyChars);
            if (request is null)
                return;

            var result = _workflowRuns.Cancel(runId, request.CapabilityToken);
            await WriteTypedJsonResponseAsync(context, result.StatusCode, result);
            return;
        }

        await WriteTypedJsonResponseAsync(context, 404, WorkflowRunStatusResponse.Failure("Not found."));
    }

    private async Task<T?> ReadWorkflowRunBodyAsync<T>(HttpListenerContext context, int maxChars)
        where T : class
    {
        if (context.Request.ContentLength64 > maxChars)
        {
            await WriteTypedJsonResponseAsync(
                context,
                413,
                WorkflowRunStatusResponse.Failure("Request body too large."));
            return null;
        }

        try
        {
            var text = await ReadBoundedBodyAsync(
                context.Request.InputStream,
                context.Request.ContentEncoding ?? Encoding.UTF8,
                maxChars,
                _cts?.Token ?? CancellationToken.None);
            if (string.IsNullOrWhiteSpace(text))
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    400,
                    WorkflowRunStatusResponse.Failure("A JSON request body is required."));
                return null;
            }

            var value = JsonSerializer.Deserialize<T>(text, WorkflowRunJsonOptions);
            if (value is null)
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    400,
                    WorkflowRunStatusResponse.Failure("Invalid JSON request body."));
            }
            return value;
        }
        catch (RequestBodyTooLargeException)
        {
            await WriteTypedJsonResponseAsync(
                context,
                413,
                WorkflowRunStatusResponse.Failure("Request body too large."));
            return null;
        }
        catch (JsonException)
        {
            await WriteTypedJsonResponseAsync(
                context,
                400,
                WorkflowRunStatusResponse.Failure("Invalid JSON request body."));
            return null;
        }
    }

    private static readonly JsonSerializerOptions WorkflowRunJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static async Task WriteTypedJsonResponseAsync<T>(
        HttpListenerContext context,
        int statusCode,
        T body)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, WorkflowRunJsonOptions);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private static WorkflowRunTarget CreateWorkflowRunTarget(AgentRegistration registration)
        => new(
            registration.Id,
            registration.InstanceId,
            registration.Port,
            registration.Platform,
            registration.AppName);

    private bool IsCurrentAgentConnection(AgentConnection expected)
        => _agents.TryGetValue(expected.Registration.Id, out var current) &&
           ReferenceEquals(current, expected) &&
           string.Equals(
               current.Registration.InstanceId,
               expected.Registration.InstanceId,
               StringComparison.Ordinal);

    private async Task<FlowReplayReport> ExecuteWorkflowRunAsync(
        WorkflowRunExecution execution,
        CancellationToken cancellationToken)
    {
        using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", execution.Target.AgentPort)
        {
            AutoAcquireMutationLease = false,
            RetryMutatingRequests = false,
            TransientFailureRetryCount = 0,
            MutationLeaseHolderKind = "workflow-run",
            MutationLeaseLabel = execution.RunId
        };
        using var leaseScope = client.UseMutationLease(
            execution.LeaseId,
            "workflow-run",
            execution.RunId);
        using var workflowScope = client.UseWorkflowRun(new Microsoft.Maui.DevFlow.Driver.WorkflowRunContext
        {
            RunId = execution.RunId,
            AgentInstanceId = execution.Target.AgentInstanceId,
            AuthorityEpoch = execution.AuthorityEpoch
        });
        var evidenceCapture = execution.Options.EvidenceCaptureFactory?.Invoke(client);
        var runner = new MauiFlowRunner(
            client,
            new MauiFlowRunnerOptions
            {
                RunId = execution.RunId,
                Target = new MauiFlowRunTarget
                {
                    TargetId = execution.Target.AgentId,
                    AgentId = execution.Target.AgentId,
                    AgentInstanceId = execution.Target.AgentInstanceId,
                    Platform = execution.Target.Platform,
                    AppId = execution.Target.AppName,
                },
                Plan = execution.SafetyRequest.Plan,
                RunContext = execution.SafetyRequest.Context,
                ThrowOnCancellation = false,
            },
            evidenceCapture);
        return (await runner.RunWithLegacyAsync(execution.Flow, file: null, cancellationToken)
            .ConfigureAwait(false)).LegacyReport;
    }

    private async Task<WorkflowRunLedgerControlResult> ControlWorkflowRunLedgerAsync(
        WorkflowRunLedgerControl control,
        CancellationToken cancellationToken)
    {
        using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", control.Target.AgentPort)
        {
            AutoAcquireMutationLease = false,
            RetryMutatingRequests = false,
            TransientFailureRetryCount = 0,
            MutationLeaseHolderKind = "workflow-run",
            MutationLeaseLabel = control.RunId
        };
        using var leaseScope = client.UseMutationLease(
            control.LeaseId,
            "workflow-run",
            control.RunId);
        var result = await client.ControlWorkflowRunAsync(
            control.Action,
            new Microsoft.Maui.DevFlow.Driver.WorkflowRunContext
            {
                RunId = control.RunId,
                AgentInstanceId = control.Target.AgentInstanceId,
                AuthorityEpoch = control.AuthorityEpoch,
                ApprovalDigest = control.ApprovalDigest
            },
            control.Reason).WaitAsync(cancellationToken).ConfigureAwait(false);

        if (result.Ok && !string.Equals(result.State, "unknown-completion", StringComparison.Ordinal))
            return WorkflowRunLedgerControlResult.Success();

        return WorkflowRunLedgerControlResult.Failure(
            result.Reason ?? (result.State == "unknown-completion" ? "workflow-unknown-completion" : null),
            result.Error ?? (result.State == "unknown-completion"
                ? "The agent reported a workflow command with unknown completion."
                : null));
    }

    internal static async Task<string> ReadBoundedBodyAsync(
        Stream stream,
        Encoding encoding,
        int maxChars,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(
            stream,
            encoding,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);
        var buffer = new char[Math.Min(4096, maxChars + 1)];
        var builder = new StringBuilder(Math.Min(maxChars, 4096));
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) return builder.ToString();
            if (builder.Length + read > maxChars) throw new RequestBodyTooLargeException();
            builder.Append(buffer, 0, read);
        }
    }

    private sealed class RequestBodyTooLargeException : Exception;

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
        foreach (var inspector in _inspectors.Values)
        {
            try { inspector.Dispose(); } catch { }
        }
        _inspectors.Clear();
        _mutationLeases.Clear();
        _flows.Clear();
        _workflowRuns.Dispose();
        _cts?.Dispose();
    }
    private record AgentConnection(AgentRegistration Registration, WebSocket WebSocket);

    // ── Inspector integration ──

    private readonly ConcurrentDictionary<string, InspectorServer> _inspectors = new();

    // Unguessable per-broker token that lets local host shells (canvas, VS Code) embed the inspector
    // in an iframe. Written to broker.json (local-only) and honored by the inspector via ?embed=.
    private readonly string _embedToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private async Task HandleInspectorRoute(HttpListenerContext context, string path)
    {
        // Routes:
        //   /inspector          → list agents with inspector links
        //   /inspector/{id}     → 301 redirect to /inspector/{id}/ so that
        //                         relative asset URLs in inspector.html
        //                         (devflow.css, devflow.js) resolve against
        //                         the per-agent base rather than the broker root.
        //   /inspector/{id}/    → serve inspector HTML for that agent
        //   /inspector/{id}/... → proxy sub-routes to the per-agent InspectorServer

        var segments = path.TrimStart('/').Split('/', 3);

        if (segments.Length == 1 || (segments.Length == 2 && string.IsNullOrEmpty(segments[1])))
        {
            // List agents with inspector links
            await ServeAgentListPage(context);
            return;
        }

        // /inspector/{id} (no trailing slash, no sub-path) → redirect to
        // /inspector/{id}/ so that <link href="devflow.css"> on the inspector
        // page resolves to /inspector/{id}/devflow.css instead of
        // /inspector/devflow.css (which the broker would otherwise try to
        // route as agent id "devflow.css"). Without the trailing slash, the
        // browser treats {id} as a filename and resolves relatives against
        // /inspector/ instead of /inspector/{id}/.
        if (segments.Length == 2 && !string.IsNullOrEmpty(segments[1]) && !path.EndsWith('/'))
        {
            // Preserve the query string on the redirect and URL-encode the
            // agent id so the Location header is a valid URI-reference even
            // when the agent id contains characters that need percent-encoding
            // (spaces, unicode, etc.).
            var query = context.Request.Url?.Query ?? string.Empty;
            context.Response.StatusCode = 301;
            context.Response.RedirectLocation = $"/inspector/{Uri.EscapeDataString(segments[1])}/{query}";
            context.Response.Close();
            return;
        }

        var agentId = segments[1];

        var subPath = segments.Length > 2 ? "/" + segments[2] : "/";

        // Inspector URLs are agent-scoped. Only the explicit "default" convenience route may use
        // the sole connected agent; a stale or mistyped real ID must never drive another app.
        var connection = ResolveInspectorAgent(_agents, agentId);

        if (connection == null)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentType = "text/plain";
            var msg = Encoding.UTF8.GetBytes($"Agent '{agentId}' not found. Connected agents: {_agents.Count}");
            await context.Response.OutputStream.WriteAsync(msg);
            context.Response.Close();
            return;
        }

        // Ordinary HTTP requests hold the generation gate through dispatch, so a same-ID reconnect
        // cannot replace the agent after validation but before a mutation reaches it. Event sockets
        // are long-lived and read-only; replacement disposes their InspectorServer instead.
        var routeGate = context.Request.IsWebSocketRequest ? null : AgentRouteGate(connection.Registration.Id);
        if (routeGate is not null)
            await routeGate.WaitAsync(_cts?.Token ?? CancellationToken.None);
        try
        {

        // Get or create inspector server for this agent.
        // Three lifecycle hazards we have to defend against:
        //   1. Race with disconnect — the agent may disconnect between our
        //      TryGetValue above and creating the inspector. MonitorAgentConnection
        //      runs its own _inspectors.TryRemove() but only sees inspectors that
        //      already exist; one created after its cleanup would be orphaned.
        //   2. Stale port on reconnect — if the agent restarts on a different port,
        //      the cached inspector still holds an AgentClient pointing at the old
        //      (dead) port. Replace it.
        //   3. GetOrAdd factory race — the ConcurrentDictionary factory overload
        //      may invoke the factory concurrently and silently discard the loser,
        //      leaking its AgentClient (HttpClient) and CTS. Construct first, then
        //      GetOrAdd(value), and dispose the loser.
        var agentPort = connection.Registration.Port;
        if (_inspectors.TryGetValue(connection.Registration.Id, out var inspector))
        {
            // Stale-port detection: the agent reconnected on a different port.
            if (inspector.AgentPort != agentPort)
            {
                if (_inspectors.TryRemove(new KeyValuePair<string, InspectorServer>(connection.Registration.Id, inspector)))
                {
                    try { inspector.Dispose(); } catch { }
                }
                inspector = null;
            }
        }

        if (inspector == null)
        {
            var created = new InspectorServer(
                0,
                "localhost",
                agentPort);
            inspector = _inspectors.GetOrAdd(connection.Registration.Id, created);
            if (!ReferenceEquals(inspector, created))
            {
                created.Dispose();
            }
            else
            {
                Log($"Inspector created for agent: {connection.Registration.AppName} (port {agentPort})");
            }

            // Disconnect-race fix: MonitorAgentConnection may have already removed
            // this agent ID from _agents (and tried to remove the not-yet-existing
            // inspector). If so, our newly-stored inspector would leak — clean up
            // and return 503 instead of routing into a dead AgentClient.
            if (!_agents.TryGetValue(connection.Registration.Id, out var currentConnection) ||
                !ReferenceEquals(currentConnection, connection))
            {
                if (_inspectors.TryRemove(new KeyValuePair<string, InspectorServer>(connection.Registration.Id, inspector)))
                {
                    try { inspector.Dispose(); } catch { }
                }
                context.Response.StatusCode = 503;
                context.Response.ContentType = "text/plain";
                var msg = Encoding.UTF8.GetBytes("Agent disconnected");
                await context.Response.OutputStream.WriteAsync(msg);
                context.Response.Close();
                return;
            }
        }

        if (!_agents.TryGetValue(connection.Registration.Id, out var routeConnection) ||
            !ReferenceEquals(routeConnection, connection))
        {
            context.Response.StatusCode = 503;
            context.Response.ContentType = "text/plain";
            var msg = Encoding.UTF8.GetBytes("Agent reconnected; reload Inspector state");
            await context.Response.OutputStream.WriteAsync(msg);
            context.Response.Close();
            return;
        }

        // Proxy the request through the inspector's route handler
        await inspector.HandleBrokerRequestAsync(context, subPath);
        }
        finally
        {
            routeGate?.Release();
        }
    }

    internal static TConnection? ResolveInspectorAgent<TConnection>(
        IReadOnlyDictionary<string, TConnection> agents,
        string agentId)
        where TConnection : class
    {
        if (agents.TryGetValue(agentId, out var connection))
            return connection;
        if (!string.Equals(agentId, "default", StringComparison.OrdinalIgnoreCase))
            return null;

        var candidates = agents.Values.Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private async Task<FlowReplayReport> ReplayInspectorWorkflowAsync(
        AgentConnection connection,
        MauiFlow flow,
        Func<Microsoft.Maui.DevFlow.Driver.AgentClient, IFlowReplayEvidenceCapture?> evidenceCaptureFactory,
        CancellationToken _)
    {
        if (!IsCurrentAgentConnection(connection))
        {
            throw new WorkflowRunRejectedException(
                409,
                "The Inspector target was replaced by a newer agent instance. Reload the Inspector.");
        }

        var registration = connection.Registration;
        var started = _workflowRuns.Start(
            new WorkflowRunStartRequest
            {
                AgentId = registration.Id,
                AgentInstanceId = registration.InstanceId,
                IdempotencyKey = $"inspector-{Guid.NewGuid():N}",
                Flow = flow,
                TimeoutMs = 120_000
            },
            CreateWorkflowRunTarget(registration),
            () => IsCurrentAgentConnection(connection),
            new WorkflowRunExecutionOptions
            {
                EvidenceCaptureFactory = evidenceCaptureFactory
            });
        if (!started.Ok || started.Run is null || string.IsNullOrWhiteSpace(started.CapabilityToken))
        {
            throw new WorkflowRunRejectedException(
                started.StatusCode == 0 ? 500 : started.StatusCode,
                started.Error ?? "The broker could not start the workflow run.");
        }

        var snapshot = await _workflowRuns.WaitForTerminalAsync(
            started.Run.RunId,
            started.CapabilityToken,
            CancellationToken.None).ConfigureAwait(false);
        if (snapshot.CompatibilityReport is not null)
            return snapshot.CompatibilityReport;

        return new FlowReplayReport
        {
            Ok = false,
            Name = flow.Name,
            Total = flow.Steps.Count,
            Failed = flow.Steps.Count,
            DivergencePoint = snapshot.FirstDivergence,
            StoppedEarly = true
        };
    }

    private async Task ServeAgentListPage(HttpListenerContext context)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><title>DevFlow Inspector</title>");
        sb.AppendLine("<style>body{font-family:system-ui;background:#1e1e1e;color:#fff;padding:20px}");
        sb.AppendLine("a{color:#4ec9b0;text-decoration:none}a:hover{text-decoration:underline}");
        sb.AppendLine(".agent{padding:12px;margin:8px 0;background:#2d2d2d;border-radius:6px}</style></head><body>");
        sb.AppendLine("<h1>DevFlow Inspector</h1>");

        if (_agents.IsEmpty)
        {
            sb.AppendLine("<p>No agents connected. Start a MAUI app with DevFlow enabled.</p>");
        }
        else
        {
            foreach (var agent in _agents.Values)
            {
                var reg = agent.Registration;
                sb.AppendLine($"<div class='agent'>");
                sb.AppendLine($"<a href='/inspector/{HttpUtility.UrlEncode(reg.Id)}/'><strong>{HttpUtility.HtmlEncode(reg.AppName)}</strong></a>");
                sb.AppendLine($" — {HttpUtility.HtmlEncode(reg.Platform)} ({HttpUtility.HtmlEncode(reg.Tfm)}) on port {reg.Port}");
                sb.AppendLine($"</div>");
            }
        }

        sb.AppendLine("</body></html>");
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/html; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    internal static Execution.AppActionFlowLifecycleResetOwner? CreateAppActionResetOwnerFor(
        AgentRegistration registration,
        string? preclaimedMutationLeaseId = null)
    {
        if (registration is null || registration.Port <= 0 || string.IsNullOrWhiteSpace(registration.PackageId))
            return null;

        return new Execution.AppActionFlowLifecycleResetOwner(
            registration.Port,
            registration.PackageId,
            registration.DeviceId ?? registration.Platform,
            $"{registration.AppName}:{registration.Tfm}:{registration.Version}",
            preclaimedMutationLeaseId);
    }
}
