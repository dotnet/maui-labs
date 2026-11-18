using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Serves the port of an agent that cannot listen on one itself, such as WebAssembly in a browser.
/// Listens on loopback, relays each HTTP request to the agent over its broker WebSocket as an
/// <c>http-request</c> frame, and writes back the agent's <c>http-response</c> frame. Clients reach
/// a tunneled agent on its assigned port exactly as they reach any other agent.
/// </summary>
/// <remarks>
/// Like the agent's own server, this reads HTTP/1.1 from a <see cref="TcpListener"/> on IPv4
/// loopback, one request per connection. <see cref="HttpListener"/> would route by Host header and
/// answer 404 to a client that dials 127.0.0.1 while sending <c>Host: localhost</c>.
/// </remarks>
internal sealed class AgentTunnel : IDisposable
{
    internal const string RequestFrameType = "http-request";
    internal const string ResponseFrameType = "http-response";
    internal const string CancelFrameType = "http-cancel";
    private const int MaxRequestBodyBytes = 1_048_576;
    private const int MaxHeaderBytes = 64 * 1024;

    // Hop-by-hop and framing headers belong to each connection, not to the relayed message.
    private static readonly HashSet<string> s_unrelayedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Content-Length", "Keep-Alive", "Transfer-Encoding", "Upgrade"
    };

    private readonly WebSocket _socket;
    private readonly TcpListener _listener;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();
    private long _nextRequestId;
    private bool _disposed;

    private AgentTunnel(int port, WebSocket socket, TcpListener listener, Action<string> log)
    {
        Port = port;
        _socket = socket;
        _listener = listener;
        _log = log;
    }

    public int Port { get; }

    /// <summary>How long a relayed request waits for the agent before failing with 504.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Starts listening on <paramref name="port"/> and relaying to the agent on <paramref name="socket"/>.
    /// The broker's receive loop hands agent messages to <see cref="HandleAgentMessage"/>.
    /// </summary>
    public static AgentTunnel Start(int port, WebSocket socket, Action<string> log, TimeSpan? requestTimeout = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();

        var tunnel = new AgentTunnel(port, socket, listener, log)
        {
            RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(120)
        };
        _ = tunnel.AcceptLoopAsync();
        return tunnel;
    }

    /// <summary>
    /// Completes the pending request an agent message answers. Returns false for messages that are
    /// not tunnel responses.
    /// </summary>
    public bool HandleAgentMessage(ReadOnlySpan<byte> message)
    {
        JsonElement frame;
        try
        {
            using var document = JsonDocument.Parse(message.ToArray());
            frame = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return false;
        }

        if (frame.ValueKind != JsonValueKind.Object
            || !frame.TryGetProperty("type", out var type) || type.GetString() != ResponseFrameType
            || !frame.TryGetProperty("id", out var id) || id.GetString() is not { } requestId)
        {
            return false;
        }

        if (_pending.TryRemove(requestId, out var completion))
            completion.TrySetResult(frame);
        return true;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = RelayAsync(client);
        }
    }

    private async Task RelayAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            try
            {
                var request = await ReadRequestAsync(stream);
                if (request is null)
                {
                    await WriteErrorAsync(stream, 400, "Malformed request.");
                    return;
                }

                if (request.Headers.TryGetValue("Upgrade", out var upgrade)
                    && upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteErrorAsync(stream, 501, "WebSocket streams are not available through the broker tunnel.");
                    return;
                }

                if (request.Body is null)
                {
                    await WriteErrorAsync(stream, 413, "Request body too large.");
                    return;
                }

                // The request is fully read, so the client sends nothing more until it hangs up.
                var clientGone = WatchForDisconnectAsync(stream);
                var response = await SendAsync(request, clientGone);
                await WriteResponseAsync(stream, response);
            }
            catch (TimeoutException)
            {
                await WriteErrorAsync(stream, 504, "The agent did not answer through the broker tunnel in time.");
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                await WriteErrorAsync(stream, 503, "The agent's broker connection closed.");
            }
            catch (IOException)
            {
                // The client went away.
            }
            catch (Exception ex)
            {
                _log($"Tunnel request failed: {ex.Message}");
                await WriteErrorAsync(stream, 502, "The broker could not relay the request to the agent.");
            }
        }
    }

    private async Task<JsonElement> SendAsync(TunnelRequest request, Task clientGone)
    {
        var requestId = Interlocked.Increment(ref _nextRequestId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var headers = new JsonObject();
        foreach (var header in request.Headers.Where(header => !s_unrelayedHeaders.Contains(header.Key)))
            headers[header.Key] = header.Value;

        var frame = new JsonObject
        {
            ["type"] = RequestFrameType,
            ["id"] = requestId,
            ["method"] = request.Method,
            ["target"] = request.Target,
            ["headers"] = headers,
            ["body"] = request.Body is { Length: > 0 } body ? body : null
        };

        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = completion;
        try
        {
            await SendFrameAsync(frame);

            Task finished;
            try
            {
                finished = await Task.WhenAny(completion.Task, clientGone).WaitAsync(RequestTimeout, _cts.Token);
            }
            catch (TimeoutException)
            {
                await CancelAgentRequestAsync(requestId);
                throw;
            }

            if (finished != completion.Task)
            {
                // Stop the agent working on an answer nobody will read, such as a queued mutation.
                await CancelAgentRequestAsync(requestId);
                throw new IOException("The client disconnected before the agent answered.");
            }
            return await completion.Task;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private async Task CancelAgentRequestAsync(string requestId)
    {
        try
        {
            await SendFrameAsync(new JsonObject { ["type"] = CancelFrameType, ["id"] = requestId });
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The agent left; it has nothing left to cancel.
        }
    }

    private async Task SendFrameAsync(JsonObject frame)
    {
        var bytes = Encoding.UTF8.GetBytes(CliJson.SerializeUntyped(frame, indented: false));
        await _sendGate.WaitAsync(_cts.Token);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <summary>
    /// Completes when the client closes its connection, or when the relay disposes it after
    /// writing the response. Callers only look at it while the agent has yet to answer.
    /// </summary>
    private static async Task WatchForDisconnectAsync(NetworkStream stream)
    {
        var probe = new byte[1];
        try
        {
            while (await stream.ReadAsync(probe) > 0)
            {
                // Bytes after the request (a pipelined request) are ignored; one request per connection.
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
        }
    }

    /// <summary>
    /// Reads one request. Returns null for malformed input; a request whose body exceeds the limit
    /// comes back with a null <see cref="TunnelRequest.Body"/>.
    /// </summary>
    private static async Task<TunnelRequest?> ReadRequestAsync(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[8192];
        using var head = new MemoryStream();
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(buffer, timeout.Token);
            if (read == 0 || head.Length + read > MaxHeaderBytes)
                return null;
            head.Write(buffer, 0, read);
            headerEnd = head.GetBuffer().AsSpan(0, (int)head.Length).IndexOf("\r\n\r\n"u8);
        }

        // The request line and headers are ASCII; the body starts after the blank line.
        var raw = head.GetBuffer();
        var lines = Encoding.ASCII.GetString(raw, 0, headerEnd).Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 3)
            return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1).Where(line => line.IndexOf(':') > 0))
        {
            var colon = line.IndexOf(':');
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            headers[name] = headers.TryGetValue(name, out var existing) ? $"{existing}, {value}" : value;
        }

        var contentLength = 0;
        if (headers.TryGetValue("Content-Length", out var declared)
            && (!int.TryParse(declared, out contentLength) || contentLength < 0))
        {
            return null;
        }
        if (contentLength > MaxRequestBodyBytes)
            return new TunnelRequest(requestLine[0], requestLine[1], headers, Body: null);

        var body = new byte[contentLength];
        var buffered = Math.Min((int)head.Length - (headerEnd + 4), contentLength);
        Array.Copy(raw, headerEnd + 4, body, 0, buffered);
        var received = buffered;
        while (received < contentLength)
        {
            var read = await stream.ReadAsync(body.AsMemory(received), timeout.Token);
            if (read == 0)
                return null;
            received += read;
        }

        return new TunnelRequest(requestLine[0], requestLine[1], headers, Encoding.UTF8.GetString(body));
    }

    private static async Task WriteResponseAsync(NetworkStream stream, JsonElement frame)
    {
        var status = frame.TryGetProperty("status", out var code) && code.TryGetInt32(out var value) ? value : 502;
        var contentType = frame.TryGetProperty("contentType", out var type) ? type.GetString() : null;
        var body = frame.TryGetProperty("bodyBase64", out var encoded) && encoded.GetString() is { } base64
            ? Convert.FromBase64String(base64)
            : [];

        var headers = new StringBuilder();
        if (frame.TryGetProperty("headers", out var relayed) && relayed.ValueKind == JsonValueKind.Object)
        {
            // Drop header injection attempts and anything the framing below owns.
            foreach (var header in relayed.EnumerateObject()
                .Where(header => !s_unrelayedHeaders.Contains(header.Name) && !header.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                .Select(header => (header.Name, Value: header.Value.GetString() ?? ""))
                .Where(header => header.Name.IndexOfAny(['\r', '\n', ':']) < 0 && header.Value.IndexOfAny(['\r', '\n']) < 0))
            {
                headers.Append(header.Name).Append(": ").Append(header.Value).Append("\r\n");
            }
        }

        await WriteAsync(stream, status, contentType, headers.ToString(), body);
    }

    private static async Task WriteErrorAsync(NetworkStream stream, int statusCode, string message)
    {
        try
        {
            var body = Encoding.UTF8.GetBytes(CliJson.SerializeUntyped(
                new JsonObject { ["success"] = false, ["error"] = message },
                indented: false));
            await WriteAsync(stream, statusCode, "application/json", headers: "", body);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }

    private static async Task WriteAsync(NetworkStream stream, int statusCode, string? contentType, string headers, byte[] body)
    {
        var reason = Enum.IsDefined(typeof(HttpStatusCode), statusCode) ? ((HttpStatusCode)statusCode).ToString() : "Status";
        var head = new StringBuilder()
            .Append("HTTP/1.1 ").Append(statusCode).Append(' ').Append(reason).Append("\r\n")
            .Append(contentType is null ? "" : $"Content-Type: {contentType}\r\n")
            .Append(headers)
            .Append("Content-Length: ").Append(body.Length).Append("\r\n")
            .Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private sealed record TunnelRequest(string Method, string Target, Dictionary<string, string> Headers, string? Body);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        foreach (var pending in _pending.Values)
            pending.TrySetCanceled();
        _pending.Clear();
        _cts.Dispose();
    }
}
