using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.DevFlow.Broker;

namespace Microsoft.Maui.Cli.DevFlow;

internal interface IDeviceMutationLeaseClient
{
    Task<DeviceMutationLeaseResult> ControlAsync(
        string action,
        string leaseId,
        string? transactionId,
        string? deviceId,
        bool catalog,
        CancellationToken cancellationToken);
}

internal sealed class BrokerDeviceMutationLeaseClient(int brokerPort) : IDeviceMutationLeaseClient
{
    public Task<DeviceMutationLeaseResult> ControlAsync(
        string action,
        string leaseId,
        string? transactionId,
        string? deviceId,
        bool catalog,
        CancellationToken cancellationToken) =>
        BrokerClient.ControlDeviceMutationLeaseAsync(
            brokerPort,
            action,
            leaseId,
            transactionId,
            deviceId,
            catalog,
            cancellationToken);
}

internal static class MobileCanvasToolMutationPolicy
{
    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.Ordinal)
    {
        "mobile_device_catalog",
        "mobile_device_list",
        "mobile_device_get",
        "mobile_device_get_selected",
        "mobile_device_display",
        "mobile_device_ui_dump",
        "mobile_device_ui_find",
        "mobile_device_app_list",
        "mobile_device_log",
        "mobile_device_crashes",
        "mobile_device_crash_report",
        "mobile_device_file_list",
        "mobile_device_permission_list",
        "mobile_device_settings_get",
        "mobile_device_presentation_get",
        "mobile_device_app_op_list",
        "mobile_device_hardware_get",
        "mobile_device_calls",
        "mobile_device_clipboard_get",
        "mobile_device_screenshot",
        "mobile_device_recording_status",
    };

    public static bool IsReadOnly(string? toolName, bool? advertisedReadOnly)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return false;
        // The fixed inventory is the pin. The annotation is accepted only as additional evidence,
        // never as permission to turn an unknown future tool into an unleased mutation.
        return ReadOnlyTools.Contains(toolName) && advertisedReadOnly != false;
    }

    public static bool UsesCatalogLease(string? toolName) =>
        string.Equals(toolName, "mobile_device_create", StringComparison.Ordinal);
}

internal sealed class MobileCanvasMcpProxy
{
    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(30);

    private readonly IDeviceMutationLeaseClient _leases;
    private readonly ConcurrentDictionary<string, PendingCall> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _requestMethods = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _advertisedReadOnly = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _childWriteGate = new(1, 1);
    private readonly SemaphoreSlim _clientWriteGate = new(1, 1);
    private readonly CancellationTokenSource _fatal = new();
    private readonly TimeSpan _heartbeatInterval;

    public MobileCanvasMcpProxy(
        IDeviceMutationLeaseClient leases,
        TimeSpan? heartbeatInterval = null)
    {
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        _heartbeatInterval = heartbeatInterval is { } interval && interval > TimeSpan.Zero
            ? interval
            : DefaultHeartbeatInterval;
    }

    /// <summary>
    /// True once the proxy tore itself down because a lease it was holding was lost. Callers must
    /// not report a run that ended this way as a healthy start.
    /// </summary>
    internal bool FatalLeaseLoss => _fatal.IsCancellationRequested;

    public static async Task<MobileCanvasMcpRunResult> RunProcessAsync(
        string executable,
        int brokerPort,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("mcp");
        if (!process.Start())
            return new MobileCanvasMcpRunResult(false, 1, "The Mobile Canvas MCP process did not start.");

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var proxy = new MobileCanvasMcpProxy(new BrokerDeviceMutationLeaseClient(brokerPort));
        var stderr = process.StandardError.BaseStream.CopyToAsync(
            Console.OpenStandardError(),
            lifetime.Token);
        // Observe the stderr relay unconditionally. Every early return below leaves it running,
        // and an unobserved fault there would tear the process down through the finalizer.
        ObserveQuietly(stderr);
        var protocol = proxy.RunAsync(
            Console.OpenStandardInput(),
            Console.OpenStandardOutput(),
            process.StandardInput.BaseStream,
            process.StandardOutput.BaseStream,
            () =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            },
            lifetime.Token);

        try
        {
            await protocol.ConfigureAwait(false);
            if (!process.HasExited)
            {
                try { process.StandardInput.Close(); } catch (ObjectDisposedException) { }
                using var grace = new CancellationTokenSource(ShutdownGrace);
                try { await process.WaitForExitAsync(grace.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            lifetime.Cancel();
            try { await stderr.ConfigureAwait(false); } catch (OperationCanceledException) { }
            // The proxy stops arbitrating the moment it loses a device lease it was holding, and
            // it kills the child to make that stop true. Reporting that as a normal start would
            // tell the caller the surface is live when it is gone.
            return proxy.FatalLeaseLoss
                ? new MobileCanvasMcpRunResult(
                    false,
                    process.HasExited ? process.ExitCode : 1,
                    "The Mobile Canvas MCP proxy lost its device mutation lease and stopped.")
                : new MobileCanvasMcpRunResult(true, process.HasExited ? process.ExitCode : 1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            throw;
        }
        catch (Exception exception)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            return new MobileCanvasMcpRunResult(false, 1, exception.Message);
        }
    }

    internal async Task RunAsync(
        Stream clientInput,
        Stream clientOutput,
        Stream childInput,
        Stream childOutput,
        Action terminateChild,
        CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _fatal.Token);
        using var clientReader = new StreamReader(clientInput, Encoding.UTF8, false, 8192, leaveOpen: true);
        using var childReader = new StreamReader(childOutput, Encoding.UTF8, false, 8192, leaveOpen: true);
        using var childWriter = new StreamWriter(childInput, new UTF8Encoding(false), 8192, leaveOpen: false)
        {
            AutoFlush = true,
            NewLine = "\n",
        };
        using var clientWriter = new StreamWriter(clientOutput, new UTF8Encoding(false), 8192, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n",
        };

        try
        {
            var fromClient = PumpClientAsync(clientReader, childWriter, clientWriter, terminateChild, lifetime.Token);
            var fromChild = PumpChildAsync(childReader, clientWriter, terminateChild, lifetime.Token);
            try
            {
                var first = await Task.WhenAny(fromClient, fromChild).ConfigureAwait(false);

                if (ReferenceEquals(first, fromClient))
                {
                    await _childWriteGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    try { childWriter.Close(); } finally { _childWriteGate.Release(); }
                    try
                    {
                        await fromChild.WaitAsync(ShutdownGrace, cancellationToken).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        terminateChild();
                    }
                }
                else
                {
                    lifetime.Cancel();
                    terminateChild();
                }

                // Surface the winner's own fault rather than returning as if the relay ended
                // cleanly. Task.WhenAny never throws, so without this a pump crash looked to the
                // caller exactly like the client closing its stdin.
                await first.ConfigureAwait(false);
            }
            finally
            {
                // Both pumps are observed on every path, including the one that timed out above
                // and the one that lost the WhenAny race. An unobserved fault here would be
                // rethrown on a finalizer thread long after the proxy stopped mattering.
                ObserveQuietly(fromClient);
                ObserveQuietly(fromChild);
            }
        }
        finally
        {
            foreach (var pending in _pending.Values)
                pending.Abandon();
        }
    }

    /// <summary>
    /// Attaches a no-op observer so a task that is deliberately abandoned cannot resurface as an
    /// unobserved <see cref="TaskScheduler.UnobservedTaskException"/>.
    /// </summary>
    private static void ObserveQuietly(Task task) =>
        task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private async Task PumpClientAsync(
        StreamReader reader,
        StreamWriter child,
        StreamWriter client,
        Action terminateChild,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                return;

            try
            {
                JsonNode? message;
                try { message = JsonNode.Parse(line); }
                catch (JsonException)
                {
                    await WriteClientAsync(client, JsonRpcError(null, -32700, "Invalid JSON-RPC payload."), cancellationToken);
                    continue;
                }
                if (message is JsonArray batch)
                {
                    if (BatchContainsMutation(batch) || BatchHasDuplicateRequestId(batch))
                    {
                        await WriteClientAsync(
                            client,
                            BatchRejection(
                                batch,
                                "Batched mutating calls and duplicate active JSON-RPC ids are not supported."),
                            cancellationToken);
                        continue;
                    }
                    TrackReadRequests(batch);
                    await WriteChildAsync(child, line, cancellationToken);
                    continue;
                }
                if (message is not JsonObject request)
                {
                    await WriteClientAsync(client, JsonRpcError(null, -32600, "Invalid JSON-RPC request."), cancellationToken);
                    continue;
                }

                // Every field below is attacker-shaped: a hostile or merely buggy client can send a
                // number, an object, or null where the protocol says string. Each of those must become
                // a bounded JSON-RPC error, never an InvalidOperationException that kills the pump and
                // takes the arbitration with it.
                var id = RequestId(request);
                if (!TryReadString(request, "method", out var method))
                {
                    await WriteClientAsync(
                        client,
                        JsonRpcError(SafeId(request), -32600, "The JSON-RPC method must be a string."),
                        cancellationToken);
                    continue;
                }
                if (id is not null && method is not null &&
                    !_requestMethods.TryAdd(id, method))
                {
                    await WriteClientAsync(
                        client,
                        JsonRpcError(SafeId(request), -32600, "A request with this JSON-RPC id is already active."),
                        cancellationToken);
                    continue;
                }

                if (!string.Equals(method, "tools/call", StringComparison.Ordinal))
                {
                    await WriteChildAsync(child, line, cancellationToken);
                    continue;
                }

                if (request["params"] is not null and not JsonObject)
                {
                    if (id is not null)
                        _requestMethods.TryRemove(id, out _);
                    await WriteClientAsync(
                        client,
                        JsonRpcError(SafeId(request), -32602, "The JSON-RPC params member must be an object."),
                        cancellationToken);
                    continue;
                }

                var parameters = request["params"] as JsonObject;
                if (!TryReadString(parameters, "name", out var toolName))
                {
                    if (id is not null)
                        _requestMethods.TryRemove(id, out _);
                    await WriteClientAsync(
                        client,
                        JsonRpcError(SafeId(request), -32602, "The tools/call params.name must be a string."),
                        cancellationToken);
                    continue;
                }

                var readOnly = _advertisedReadOnly.TryGetValue(toolName ?? "", out var advertised)
                    ? advertised
                    : (bool?)null;
                if (MobileCanvasToolMutationPolicy.IsReadOnly(toolName, readOnly))
                {
                    await WriteChildAsync(child, line, cancellationToken);
                    continue;
                }
                if (id is null)
                    continue;

                var catalog = MobileCanvasToolMutationPolicy.UsesCatalogLease(toolName);
                if (parameters?["arguments"] is not null and not JsonObject)
                {
                    _requestMethods.TryRemove(id, out _);
                    await WriteClientAsync(
                        client,
                        ToolError(SafeId(request), "The tools/call params.arguments must be an object."),
                        cancellationToken);
                    continue;
                }

                var arguments = parameters?["arguments"] as JsonObject;
                if (!TryReadString(arguments, "deviceId", out var rawDeviceId))
                {
                    _requestMethods.TryRemove(id, out _);
                    await WriteClientAsync(
                        client,
                        ToolError(SafeId(request), "A mutating Mobile Canvas call requires a string arguments.deviceId."),
                        cancellationToken);
                    continue;
                }

                var deviceId = rawDeviceId?.Trim();
                if (!catalog && string.IsNullOrWhiteSpace(deviceId))
                {
                    _requestMethods.TryRemove(id, out _);
                    await WriteClientAsync(client, ToolError(SafeId(request), "A mutating Mobile Canvas call requires arguments.deviceId."), cancellationToken);
                    continue;
                }

                var transactionId = "mcp-call:" + Guid.NewGuid().ToString("N");
                var leaseId = "mobile-canvas-mcp:" + Guid.NewGuid().ToString("N");
                var pending = await PendingCall.AcquireAsync(
                    _leases,
                    leaseId,
                    transactionId,
                    deviceId,
                    catalog,
                    _heartbeatInterval,
                    () =>
                    {
                        _fatal.Cancel();
                        terminateChild();
                    },
                    cancellationToken).ConfigureAwait(false);
                if (!pending.Acquired)
                {
                    _requestMethods.TryRemove(id, out _);
                    await WriteClientAsync(client, ToolError(SafeId(request), pending.Error!), cancellationToken);
                    continue;
                }

                if (!_pending.TryAdd(id, pending))
                {
                    _requestMethods.TryRemove(id, out _);
                    await pending.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
                    await WriteClientAsync(client, ToolError(SafeId(request), "A mutating call with this JSON-RPC id is already active."), cancellationToken);
                    continue;
                }
                await WriteChildAsync(child, line, cancellationToken);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or FormatException or JsonException
                    or ArgumentException or OverflowException or NotSupportedException)
            {
                // Last resort. A shape nothing above anticipated still has to become a bounded
                // error the client can read, because a pump that throws stops arbitrating leases
                // while the child process keeps accepting mutations.
                await WriteClientAsync(
                    client,
                    JsonRpcError(null, -32603, "The Mobile Canvas proxy could not process this JSON-RPC message."),
                    cancellationToken);
            }
        }
    }

    private async Task PumpChildAsync(
        StreamReader child,
        StreamWriter client,
        Action terminateChild,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await child.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                return;

            JsonNode? message = null;
            try { message = JsonNode.Parse(line); } catch (JsonException) { }
            if (message is JsonObject response)
                await ObserveResponseAsync(response, cancellationToken).ConfigureAwait(false);
            else if (message is JsonArray batch)
            {
                foreach (var responseItem in batch.OfType<JsonObject>())
                    await ObserveResponseAsync(responseItem, cancellationToken).ConfigureAwait(false);
            }

            await WriteClientAsync(client, line, cancellationToken);
        }
    }

    private async Task ObserveResponseAsync(JsonObject response, CancellationToken cancellationToken)
    {
        if (response["method"] is not null)
            return;
        var id = RequestId(response);
        if (id is null)
            return;

        if (_requestMethods.TryRemove(id, out var method) &&
            string.Equals(method, "tools/list", StringComparison.Ordinal))
        {
            CaptureToolInventory(response);
        }
        if (_pending.TryRemove(id, out var pending))
            await pending.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private void CaptureToolInventory(JsonObject response)
    {
        if (response["result"] is not JsonObject result || result["tools"] is not JsonArray tools)
            return;
        foreach (var tool in tools.OfType<JsonObject>())
        {
            if (!TryReadString(tool, "name", out var name) || string.IsNullOrWhiteSpace(name))
                continue;
            // A non-boolean readOnlyHint is not evidence of anything, so it is recorded as "not
            // read-only". The fixed inventory is the pin either way.
            _advertisedReadOnly[name] = tool["annotations"] is JsonObject annotations &&
                annotations["readOnlyHint"] is JsonNode hint &&
                hint.GetValueKind() == JsonValueKind.True;
        }
    }

    private bool BatchContainsMutation(JsonArray batch)
    {
        foreach (var request in batch.OfType<JsonObject>())
        {
            if (!TryReadString(request, "method", out var method))
                return true;
            if (!string.Equals(method, "tools/call", StringComparison.Ordinal))
                continue;
            if (request["params"] is not null and not JsonObject)
                return true;
            if (!TryReadString(request["params"] as JsonObject, "name", out var name))
                return true;
            var advertised = _advertisedReadOnly.TryGetValue(name ?? "", out var value)
                ? value
                : (bool?)null;
            if (!MobileCanvasToolMutationPolicy.IsReadOnly(name, advertised))
                return true;
        }
        return false;
    }

    private void TrackReadRequests(JsonArray batch)
    {
        foreach (var request in batch.OfType<JsonObject>())
        {
            var id = RequestId(request);
            if (id is not null && TryReadString(request, "method", out var method) && method is not null)
                _requestMethods.TryAdd(id, method);
        }
    }

    private bool BatchHasDuplicateRequestId(JsonArray batch)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var request in batch.OfType<JsonObject>())
        {
            var id = RequestId(request);
            if (id is null)
                continue;
            if (_requestMethods.ContainsKey(id) || !seen.Add(id))
                return true;
        }
        return false;
    }

    private static string BatchRejection(JsonArray batch, string message)
    {
        var responses = new JsonArray();
        foreach (var request in batch.OfType<JsonObject>())
        {
            if (SafeId(request) is { } id)
                responses.Add(JsonNode.Parse(JsonRpcError(id, -32600, message)));
        }
        // A batch whose every member was a notification (or a malformed id) still has to be
        // refused visibly: an empty array is not a legal JSON-RPC response.
        return responses.Count == 0
            ? JsonRpcError(null, -32600, message)
            : responses.ToJsonString();
    }

    private static string ToolError(JsonNode? id, string message) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = new JsonObject
            {
                ["isError"] = true,
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = message.Length <= 1024 ? message : message[..1024],
                    },
                },
            },
        }.ToJsonString();

    private static string JsonRpcError(JsonNode? id, int code, string message) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        }.ToJsonString();

    private static string? RequestId(JsonObject message)
    {
        if (message["id"] is not JsonNode id)
            return null;
        return id.GetValueKind() switch
        {
            JsonValueKind.String => "s:" + id.GetValue<string>(),
            JsonValueKind.Number => "n:" + id.ToJsonString(),
            _ => null,
        };
    }

    /// <summary>
    /// Echoes back only an id JSON-RPC actually permits. An object or array in the id position is
    /// not an id, and copying it into an error response would let a client choose the shape of a
    /// message the proxy emits.
    /// </summary>
    private static JsonNode? SafeId(JsonObject message) =>
        message["id"] is JsonNode id &&
        id.GetValueKind() is JsonValueKind.String or JsonValueKind.Number
            ? id.DeepClone()
            : null;

    /// <summary>
    /// Reads a string member, distinguishing "absent or null" (true, value null) from "present but
    /// not a string" (false). Callers turn the second case into a bounded JSON-RPC error instead of
    /// letting <see cref="JsonNode.GetValue{T}"/> throw and take the pump down.
    /// </summary>
    private static bool TryReadString(JsonObject? owner, string property, out string? value)
    {
        value = null;
        if (owner is null || owner[property] is not JsonNode node)
            return true;
        if (node.GetValueKind() == JsonValueKind.Null)
            return true;
        if (node.GetValueKind() != JsonValueKind.String)
            return false;
        value = node.GetValue<string>();
        return true;
    }

    private async Task WriteChildAsync(
        StreamWriter writer,
        string line,
        CancellationToken cancellationToken)
    {
        await _childWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false); }
        finally { _childWriteGate.Release(); }
    }

    private async Task WriteClientAsync(
        StreamWriter writer,
        string line,
        CancellationToken cancellationToken)
    {
        await _clientWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false); }
        finally { _clientWriteGate.Release(); }
    }

    private sealed class PendingCall : IDisposable
    {
        private readonly IDeviceMutationLeaseClient _leases;
        private readonly string _leaseId;
        private readonly string _transactionId;
        private readonly string? _deviceId;
        private readonly bool _catalog;
        private readonly TimeSpan _heartbeatInterval;
        private readonly CancellationTokenSource _heartbeatCancellation = new();
        private Task? _heartbeat;

        private PendingCall(
            IDeviceMutationLeaseClient leases,
            string leaseId,
            string transactionId,
            string? deviceId,
            bool catalog,
            TimeSpan heartbeatInterval)
        {
            _leases = leases;
            _leaseId = leaseId;
            _transactionId = transactionId;
            _deviceId = deviceId;
            _catalog = catalog;
            _heartbeatInterval = heartbeatInterval;
        }

        public bool Acquired { get; private set; }
        public string? Error { get; private set; }

        public static async Task<PendingCall> AcquireAsync(
            IDeviceMutationLeaseClient leases,
            string leaseId,
            string transactionId,
            string? deviceId,
            bool catalog,
            TimeSpan heartbeatInterval,
            Action onLeaseLost,
            CancellationToken cancellationToken)
        {
            var pending = new PendingCall(
                leases,
                leaseId,
                transactionId,
                deviceId,
                catalog,
                heartbeatInterval);
            var claim = await leases.ControlAsync(
                "claim", leaseId, null, deviceId, catalog, cancellationToken).ConfigureAwait(false);
            if (!claim.Allowed)
                return WithFailure(claim.Error ?? LeaseConflict(claim));

            var begin = await leases.ControlAsync(
                "begin", leaseId, transactionId, deviceId, catalog, cancellationToken).ConfigureAwait(false);
            if (!begin.Allowed || !string.Equals(begin.TransactionId, transactionId, StringComparison.Ordinal))
            {
                await leases.ControlAsync(
                    "release", leaseId, null, deviceId, catalog, CancellationToken.None).ConfigureAwait(false);
                return WithFailure(begin.Error ?? "The broker could not open the device mutation transaction.");
            }

            pending.Acquired = true;
            pending._heartbeat = pending.HeartbeatAsync(onLeaseLost);
            return pending;

            PendingCall WithFailure(string error)
            {
                pending.Error = error;
                return pending;
            }
        }

        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            _heartbeatCancellation.Cancel();
            if (_heartbeat is not null)
            {
                try { await _heartbeat.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            await _leases.ControlAsync(
                "end", _leaseId, _transactionId, _deviceId, _catalog, cancellationToken).ConfigureAwait(false);
            await _leases.ControlAsync(
                "release", _leaseId, null, _deviceId, _catalog, cancellationToken).ConfigureAwait(false);
            Dispose();
        }

        public void Abandon()
        {
            // Unknown completion: stop heartbeating but deliberately leave the broker transaction
            // open until its bounded transaction TTL expires.
            _heartbeatCancellation.Cancel();
        }

        private async Task HeartbeatAsync(Action onLeaseLost)
        {
            try
            {
                while (!_heartbeatCancellation.IsCancellationRequested)
                {
                    await Task.Delay(_heartbeatInterval, _heartbeatCancellation.Token).ConfigureAwait(false);
                    var heartbeat = await _leases.ControlAsync(
                        "heartbeat",
                        _leaseId,
                        _transactionId,
                        _deviceId,
                        _catalog,
                        _heartbeatCancellation.Token).ConfigureAwait(false);
                    if (!heartbeat.Allowed ||
                        !string.Equals(heartbeat.TransactionId, _transactionId, StringComparison.Ordinal))
                    {
                        onLeaseLost();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static string LeaseConflict(DeviceMutationLeaseResult result) =>
            result.HeldByOther
                ? $"Another DevFlow session ({result.Label ?? result.HolderKind ?? "unknown"}) is driving this device."
                : "The broker did not grant the device mutation lease.";

        public void Dispose() => _heartbeatCancellation.Dispose();
    }
}
