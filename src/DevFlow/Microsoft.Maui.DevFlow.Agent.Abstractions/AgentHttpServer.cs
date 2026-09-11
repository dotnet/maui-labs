using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Lightweight HTTP server using TcpListener (sandbox-friendly, no HttpListener).
/// Routes incoming requests to registered handlers.
/// </summary>
public class AgentHttpServer : IDisposable
{
    /// <summary>
    /// Body ceiling. Bodies are buffered whole, so this is what keeps a file upload from taking the
    /// app under test down with it. Raised from 1MB when the storage routes gained real uploads -
    /// a database or a screenshot is routinely larger than that.
    /// </summary>
    private const int MaxRequestBodyBytes = 64 * 1024 * 1024;

    /// <summary>Header block ceiling. A request still short of its blank line past this is not one we want.</summary>
    private const int MaxHeaderBytes = 64 * 1024;

    /// <summary>How long a body read waits for the next block before giving the connection up.</summary>
    private static readonly TimeSpan BodyIdleTimeout = TimeSpan.FromSeconds(15);
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _disposed;
    private readonly int _port;
    private readonly Dictionary<string, RouteHandler> _getRoutes = new();
    private readonly Dictionary<string, RouteHandler> _postRoutes = new();
    private readonly Dictionary<string, RouteHandler> _putRoutes = new();
    private readonly Dictionary<string, RouteHandler> _deleteRoutes = new();
    private readonly Dictionary<string, Func<TcpClient, NetworkStream, HttpRequest, CancellationToken, Task>> _wsRoutes = new();
    private readonly SemaphoreSlim _mutationAdmissionGate = new(1, 1);

    public int Port => _port;
    public bool IsRunning => _listenTask != null && !_listenTask.IsCompleted;
    internal Func<HttpRequest, Task<MutationLeaseStatus>>? MutationLeaseValidator { get; set; }
    internal Func<HttpRequest, HttpResponse, Task>? MutationObserver { get; set; }
    /// <summary>
    /// How long a mutation waits for the one ahead of it before giving up with a retryable 503.
    /// Long enough for a normal UI round-trip, short enough that a wedged dispatcher cannot
    /// silently swallow every subsequent request.
    /// </summary>
    internal TimeSpan MutationAdmissionTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public AgentHttpServer(int port = 9223)
    {
        _port = port;
    }

    public void MapGet(string path, Func<HttpRequest, Task<HttpResponse>> handler)
        => _getRoutes[path.TrimEnd('/')] = new RouteHandler(handler, RequiresMutationLease: false);

    public void MapPost(string path, Func<HttpRequest, Task<HttpResponse>> handler, bool requiresMutationLease = true)
        => MapPost(path, handler, requiresMutationLease, mutationLeaseExemption: null);

    public void MapPost(
        string path,
        Func<HttpRequest, Task<HttpResponse>> handler,
        bool requiresMutationLease,
        Func<HttpRequest, bool>? mutationLeaseExemption)
        => _postRoutes[path.TrimEnd('/')] = new RouteHandler(handler, requiresMutationLease, mutationLeaseExemption);

    public void MapPut(string path, Func<HttpRequest, Task<HttpResponse>> handler, bool requiresMutationLease = true)
        => _putRoutes[path.TrimEnd('/')] = new RouteHandler(handler, requiresMutationLease);

    public void MapDelete(string path, Func<HttpRequest, Task<HttpResponse>> handler, bool requiresMutationLease = true)
        => _deleteRoutes[path.TrimEnd('/')] = new RouteHandler(handler, requiresMutationLease);

    public void MapWebSocket(string path, Func<TcpClient, NetworkStream, HttpRequest, CancellationToken, Task> handler)
        => _wsRoutes[path.TrimEnd('/')] = handler;

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AgentHttpServer));
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _listenTask = AcceptLoop(_cts.Token);
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();
        if (_listenTask != null)
            await _listenTask.ConfigureAwait(false);
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { /* swallow connection errors */ }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            var stream = client.GetStream();
            HttpRequest? request;
            try
            {
                request = await ReadRequestAsync(stream, ct).ConfigureAwait(false);
            }
            catch (RequestBodyTooLargeException)
            {
                using (client)
                {
                    await WriteResponseAsync(
                        stream,
                        HttpResponse.Error("Request body too large.", 413),
                        ct).ConfigureAwait(false);
                }
                return;
            }
            if (request == null)
            {
                client.Dispose();
                return;
            }

            request.Headers.TryGetValue("Origin", out var origin);
            if (!IsTrustedBrowserOrigin(origin))
            {
                using (client)
                {
                    await WriteResponseAsync(
                        stream,
                        HttpResponse.Error("Browser origin is not allowed.", 403),
                        ct).ConfigureAwait(false);
                }
                return;
            }

            // Check for WebSocket upgrade
            if (request.Headers.TryGetValue("Upgrade", out var upgrade)
                && upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase)
                && _wsRoutes.TryGetValue(request.Path, out var wsHandler))
            {
                // Perform WebSocket handshake
                if (!request.Headers.TryGetValue("Sec-WebSocket-Key", out var wsKey))
                {
                    client.Dispose();
                    return;
                }

                var acceptKey = ComputeWebSocketAcceptKey(wsKey);
                var handshake = "HTTP/1.1 101 Switching Protocols\r\n"
                    + "Upgrade: websocket\r\n"
                    + "Connection: Upgrade\r\n"
                    + $"Sec-WebSocket-Accept: {acceptKey}\r\n"
                    + "\r\n";
                var handshakeBytes = Encoding.UTF8.GetBytes(handshake);
                await stream.WriteAsync(handshakeBytes, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);

                // Hand off to WebSocket handler (takes ownership of client — no using/dispose here)
                client.Client.SetSocketOption(
                    System.Net.Sockets.SocketOptionLevel.Socket,
                    System.Net.Sockets.SocketOptionName.KeepAlive, true);
                _ = Task.Run(async () =>
                {
                    try { await wsHandler(client, stream, request, ct); }
                    catch { }
                    finally { client.Dispose(); }
                }, ct);
                return;
            }

            // Normal HTTP flow
            using (client)
            {
                using var requestCts =
                    CancellationTokenSource.CreateLinkedTokenSource(ct);
                request.CancellationToken = requestCts.Token;
                var disconnectMonitor = MonitorClientDisconnectAsync(
                    client,
                    requestCts);
                try
                {
                    var response = await RouteRequestAsync(request, requestCts.Token)
                        .ConfigureAwait(false);
                    if (!requestCts.IsCancellationRequested)
                    {
                        await WriteResponseAsync(
                                stream,
                                response,
                                requestCts.Token,
                                origin)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    await requestCts.CancelAsync();
                    try
                    {
                        await disconnectMonitor.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) { Console.WriteLine($"[Microsoft.Maui.DevFlow.Agent] Request error: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static async Task MonitorClientDisconnectAsync(
        TcpClient client,
        CancellationTokenSource requestCts)
    {
        while (!requestCts.IsCancellationRequested)
        {
            try
            {
                if (client.Client.Poll(
                        1_000,
                        SelectMode.SelectRead)
                    && client.Available == 0)
                {
                    await requestCts.CancelAsync();
                    return;
                }
            }
            catch (SocketException)
            {
                await requestCts.CancelAsync();
                return;
            }
            catch (ObjectDisposedException)
            {
                await requestCts.CancelAsync();
                return;
            }

            await Task.Delay(100, requestCts.Token)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads one request off the wire. Headers are text, bodies are not - a file upload is arbitrary
    /// bytes - so only the header block is ever decoded, and the body is handed on as bytes.
    /// </summary>
    private async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        var buffer = new byte[8192];
        var pending = new MemoryStream();
        var headerEnd = -1;

        try
        {
            // Keep reading until the blank line that ends the headers. A single read is not enough
            // once a request carries more than a couple of kilobytes of headers.
            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token).ConfigureAwait(false);
                if (read == 0)
                    return null;

                pending.Write(buffer, 0, read);
                headerEnd = IndexOfHeaderEnd(pending.GetBuffer(), (int)pending.Length);

                if (headerEnd < 0 && pending.Length > MaxHeaderBytes)
                    return null;
            }
        }
        catch { return null; }

        var raw = pending.GetBuffer();
        var rawLength = (int)pending.Length;

        // The request line and headers are ASCII, so this is the only decode the request needs -
        // and it deliberately stops at the blank line, leaving the body as the bytes it is.
        var lines = Encoding.UTF8.GetString(raw, 0, headerEnd).Split("\r\n");
        if (lines.Length == 0) return null;

        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2) return null;

        var method = requestLine[0];
        var fullPath = requestLine[1];

        // Parse path and query string
        var queryStart = fullPath.IndexOf('?');
        var path = queryStart >= 0 ? fullPath[..queryStart] : fullPath;
        var queryString = queryStart >= 0 ? fullPath[(queryStart + 1)..] : "";

        var queryParams = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(queryString))
        {
            foreach (var param in queryString.Split('&'))
            {
                var kv = param.Split('=', 2);
                if (kv.Length == 2)
                    queryParams[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
            }
        }

        // Parse headers
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) break;
            var colonIdx = lines[i].IndexOf(':');
            if (colonIdx > 0)
                headers[lines[i][..colonIdx].Trim()] = lines[i][(colonIdx + 1)..].Trim();
        }

        // Body: whatever came in behind the headers, plus the rest of it. Counted and kept in bytes
        // rather than characters - Content-Length counts bytes, and a multi-byte or binary payload
        // makes the two differ.
        byte[]? bodyBytes = null;
        var bodyStart = headerEnd + 4;
        var haveBodyBytes = Math.Max(0, rawLength - bodyStart);

        // Chunked comes first: a chunked request also has no Content-Length, and .NET's own
        // JsonContent sends this way, so treating the chunk framing as the body loses every such
        // request.
        if (headers.TryGetValue("Transfer-Encoding", out var transferEncoding)
            && transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            bodyBytes = await ReadChunkedBodyAsync(stream, raw, bodyStart, haveBodyBytes, ct).ConfigureAwait(false);
            if (bodyBytes == null)
                return null;
        }
        else if (headers.TryGetValue("Content-Length", out var contentLengthText))
        {
            if (!int.TryParse(contentLengthText, out var contentLength) || contentLength < 0)
                return null;

            if (contentLength > MaxRequestBodyBytes)
                throw new RequestBodyTooLargeException();

            if (contentLength > 0)
            {
                bodyBytes = new byte[contentLength];
                var copied = Math.Min(haveBodyBytes, contentLength);
                Buffer.BlockCopy(raw, bodyStart, bodyBytes, 0, copied);

                try
                {
                    while (copied < contentLength)
                    {
                        var read = await ReadWithIdleTimeoutAsync(
                            stream, bodyBytes.AsMemory(copied, contentLength - copied), ct).ConfigureAwait(false);

                        if (read == 0) break;
                        copied += read;
                    }
                }
                catch { return null; }

                if (copied < contentLength)
                    Array.Resize(ref bodyBytes, copied);
            }
        }
        else if (haveBodyBytes > 0)
        {
            bodyBytes = new byte[haveBodyBytes];
            Buffer.BlockCopy(raw, bodyStart, bodyBytes, 0, haveBodyBytes);
        }

        return new HttpRequest
        {
            Method = method,
            Path = path.TrimEnd('/'),
            QueryParams = queryParams,
            Headers = headers,
            BodyBytes = bodyBytes
        };
    }

    /// <summary>
    /// Decodes an RFC 7230 chunked body: a hex length line, that many bytes, CRLF, repeated until a
    /// zero-length chunk. Trailers after it are read but discarded - nothing here uses them.
    /// </summary>
    /// <returns>The body, or null if the framing was malformed or the stream ended early.</returns>
    /// <exception cref="RequestBodyTooLargeException">The chunks added up past the ceiling.</exception>
    private static async Task<byte[]?> ReadChunkedBodyAsync(
        NetworkStream stream, byte[] initial, int offset, int count, CancellationToken ct)
    {
        var pending = new MemoryStream();
        pending.Write(initial, offset, count);

        var body = new MemoryStream();
        var pos = 0;

        while (true)
        {
            int lineEnd;
            while ((lineEnd = IndexOfCrLf(pending.GetBuffer(), pos, (int)pending.Length)) < 0)
            {
                if (!await FillAsync(stream, pending, ct).ConfigureAwait(false))
                    return null;
            }

            var sizeText = Encoding.ASCII.GetString(pending.GetBuffer(), pos, lineEnd - pos);
            var extension = sizeText.IndexOf(';');
            if (extension >= 0)
                sizeText = sizeText[..extension];

            if (!int.TryParse(sizeText.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var chunkSize) || chunkSize < 0)
                return null;

            pos = lineEnd + 2;
            if (chunkSize == 0)
                break;

            if (body.Length + chunkSize > MaxRequestBodyBytes)
                throw new RequestBodyTooLargeException();

            // The chunk plus its trailing CRLF.
            while (pending.Length - pos < chunkSize + 2)
            {
                if (!await FillAsync(stream, pending, ct).ConfigureAwait(false))
                    return null;
            }

            body.Write(pending.GetBuffer(), pos, chunkSize);
            pos += chunkSize;

            // Every chunk ends with CRLF. Skipping two bytes without checking would quietly accept
            // malformed framing and put the following bytes out of step with the length lines.
            var terminator = pending.GetBuffer();
            if (terminator[pos] != (byte)'\r' || terminator[pos + 1] != (byte)'\n')
                return null;

            pos += 2;
        }

        return body.ToArray();
    }

    /// <summary>
    /// Reads once, giving up if nothing arrives for <see cref="BodyIdleTimeout"/>.
    /// </summary>
    /// <remarks>
    /// Idle rather than a deadline on the whole body, and the 64MB ceiling is why: a large upload
    /// over a slow link is legitimate and can take a while, but a client that has stopped sending
    /// should not hold a server task open. Resetting on every block distinguishes the two.
    /// </remarks>
    private static async Task<int> ReadWithIdleTimeoutAsync(
        NetworkStream stream, Memory<byte> destination, CancellationToken ct)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(BodyIdleTimeout);

        return await stream.ReadAsync(destination, idle.Token).ConfigureAwait(false);
    }

    /// <summary>Reads one more block onto the end of <paramref name="pending"/>. False at end of stream.</summary>
    private static async Task<bool> FillAsync(NetworkStream stream, MemoryStream pending, CancellationToken ct)
    {
        var buffer = new byte[8192];
        int read;
        try
        {
            read = await ReadWithIdleTimeoutAsync(stream, buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
        }
        catch { return false; }

        if (read == 0)
            return false;

        var resume = pending.Position;
        pending.Position = pending.Length;
        pending.Write(buffer, 0, read);
        pending.Position = resume;
        return true;
    }

    /// <summary>Offset of the CRLFCRLF that ends the header block, or -1 while it is still incoming.</summary>
    private static int IndexOfHeaderEnd(byte[] buffer, int length)
    {
        for (var i = 0; i + 3 < length; i++)
        {
            if (buffer[i] == (byte)'\r' && buffer[i + 1] == (byte)'\n'
                && buffer[i + 2] == (byte)'\r' && buffer[i + 3] == (byte)'\n')
                return i;
        }
        return -1;
    }

    private static int IndexOfCrLf(byte[] buffer, int start, int length)
    {
        for (var i = start; i + 1 < length; i++)
        {
            if (buffer[i] == (byte)'\r' && buffer[i + 1] == (byte)'\n')
                return i;
        }
        return -1;
    }

    private async Task<HttpResponse> RouteRequestAsync(HttpRequest request, CancellationToken ct)
    {
        var routes = request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) ? _postRoutes
            : request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ? _putRoutes
            : request.Method.Equals("DELETE", StringComparison.OrdinalIgnoreCase) ? _deleteRoutes
            : _getRoutes;

        // Try exact match first
        if (routes.TryGetValue(request.Path, out var handler))
            return await InvokeRouteAsync(handler, request, ct).ConfigureAwait(false);

        // Try pattern match (e.g., /api/element/{id})
        foreach (var kvp in routes)
        {
            var routeParts = kvp.Key.Split('/');
            var requestParts = request.Path.Split('/');
            if (routeParts.Length != requestParts.Length) continue;

            bool match = true;
            for (int i = 0; i < routeParts.Length; i++)
            {
                if (routeParts[i].StartsWith('{') && routeParts[i].EndsWith('}'))
                {
                    var paramName = routeParts[i][1..^1];
                    request.RouteParams[paramName] = requestParts[i];
                    continue;
                }
                if (!routeParts[i].Equals(requestParts[i], StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return await InvokeRouteAsync(kvp.Value, request, ct).ConfigureAwait(false);

            request.RouteParams.Clear();
        }

        return HttpResponse.NotFound("Route not found");
    }

    private async Task<HttpResponse> InvokeRouteAsync(
        RouteHandler route,
        HttpRequest request,
        CancellationToken ct)
    {
        var requiresMutationLease = route.RequiresMutationLease &&
            !(route.MutationLeaseExemption?.Invoke(request) ?? false);

        // Only mutations are admitted one at a time. Lease control (/api/v1/agent/lease) must
        // stay OUT of this gate: a forced takeover exists precisely to preempt a mutation that
        // is stuck — typically on a blocked UI dispatcher — so queueing it behind that mutation
        // would deadlock the one escape hatch. MutationLeaseCoordinator serializes itself.
        if (!route.RequiresMutationLease)
            return await RunRouteAsync(route, request, requiresMutationLease).ConfigureAwait(false);

        if (!await _mutationAdmissionGate.WaitAsync(MutationAdmissionTimeout, ct).ConfigureAwait(false))
        {
            // Bounded, not indefinite: a wedged mutation must not accumulate every later
            // request behind it until each client times out on its own.
            //
            // Reuse the established "ui-mutation-busy" envelope (the same one the in-handler
            // gate in ExecuteUiMutationAsync returns) and state details.retryable explicitly.
            // AgentClient only treats a failure as retryable when it sees details.retryable or
            // one of its known reasons, so a bespoke reason here would decode as terminal:
            // Inspector replay would stop, MCP would throw without retry guidance, and the CLI
            // would report retryable:false — the opposite of what Retry-After promises.
            var busy = HttpResponse.Error(
                "The app is still applying another mutation. Retry shortly.",
                statusCode: 503,
                reason: "ui-mutation-busy",
                details: new { retryable = true });
            busy.Headers["Retry-After"] = "1";
            return busy;
        }

        try
        {
            return await RunRouteAsync(route, request, requiresMutationLease).ConfigureAwait(false);
        }
        finally
        {
            _mutationAdmissionGate.Release();
        }
    }

    private async Task<HttpResponse> RunRouteAsync(
        RouteHandler route,
        HttpRequest request,
        bool requiresMutationLease)
    {
        if (requiresMutationLease && MutationLeaseValidator is not null)
        {
            var status = await MutationLeaseValidator(request).ConfigureAwait(false);
            request.MutationLease = status;
            if (!status.Allowed)
            {
                return HttpResponse.Error(
                    "Another DevFlow session is driving this app. Take control before mutating it.",
                    statusCode: 409,
                    reason: "lease",
                    details: new
                    {
                        status.HolderKind,
                        status.Label,
                        status.ExpiresInMs,
                        status.Authority
                    });
            }
        }

        var response = await route.Handler(request).ConfigureAwait(false);
        if (requiresMutationLease &&
            response.StatusCode is >= 200 and < 300 &&
            MutationObserver is not null)
        {
            try { await MutationObserver(request, response).ConfigureAwait(false); }
            catch { }
        }
        return response;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        HttpResponse response,
        CancellationToken ct,
        string? origin = null)
    {
        var bodyBytes = response.Body != null ? Encoding.UTF8.GetBytes(response.Body) : Array.Empty<byte>();
        var headerBuilder = new StringBuilder();
        headerBuilder.Append($"HTTP/1.1 {response.StatusCode} {response.StatusText}\r\n");
        headerBuilder.Append($"Content-Type: {response.ContentType}\r\n");
        headerBuilder.Append($"Content-Length: {(response.BodyBytes ?? bodyBytes).Length}\r\n");
        if (!string.IsNullOrWhiteSpace(origin))
        {
            headerBuilder.Append($"Access-Control-Allow-Origin: {origin}\r\n");
            headerBuilder.Append("Vary: Origin\r\n");
        }
        foreach (var header in response.Headers)
        {
            if (header.Key.Contains('\r') || header.Key.Contains('\n')
                || header.Value.Contains('\r') || header.Value.Contains('\n'))
            {
                continue;
            }

            headerBuilder.Append($"{header.Key}: {header.Value}\r\n");
        }
        headerBuilder.Append("Connection: close\r\n");
        headerBuilder.Append("\r\n");

        var headerBytes = Encoding.UTF8.GetBytes(headerBuilder.ToString());
        await stream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
        await stream.WriteAsync(response.BodyBytes ?? bodyBytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private bool IsTrustedBrowserOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp || uri.Port != _port)
            return false;
        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
    }

    private sealed record RouteHandler(
        Func<HttpRequest, Task<HttpResponse>> Handler,
        bool RequiresMutationLease,
        Func<HttpRequest, bool>? MutationLeaseExemption = null);

    private sealed class RequestBodyTooLargeException : Exception;

    // ── WebSocket helpers (RFC 6455) ──

    private static readonly byte[] WsMagicGuid = Encoding.UTF8.GetBytes("258EAFA5-E914-47DA-95CA-C5AB0DC85B11");

    private static string ComputeWebSocketAcceptKey(string clientKey)
    {
        var combined = Encoding.UTF8.GetBytes(clientKey.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11");
        var hash = SHA1.HashData(combined);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Sends a text frame over a WebSocket connection.
    /// </summary>
    public static async Task WebSocketSendTextAsync(NetworkStream stream, string text, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        await WebSocketSendFrameAsync(stream, 0x81, payload, ct); // 0x81 = FIN + text opcode
    }

    /// <summary>
    /// Sends a ping frame to keep the WebSocket connection alive.
    /// </summary>
    public static async Task WebSocketSendPingAsync(NetworkStream stream, CancellationToken ct)
    {
        await WebSocketSendFrameAsync(stream, 0x89, Array.Empty<byte>(), ct); // 0x89 = FIN + ping opcode
    }

    /// <summary>
    /// Reads a text frame from a WebSocket connection. Returns null on close/error.
    /// </summary>
    public static async Task<string?> WebSocketReadTextAsync(NetworkStream stream, CancellationToken ct)
    {
        try
        {
            var header = new byte[2];
            if (await ReadExactAsync(stream, header, ct) < 2) return null;

            var fin = (header[0] & 0x80) != 0;
            var opcode = header[0] & 0x0F;
            var masked = (header[1] & 0x80) != 0;
            var payloadLen = (long)(header[1] & 0x7F);

            if (opcode == 0x08) return null; // close frame

            if (payloadLen == 126)
            {
                var extLen = new byte[2];
                if (await ReadExactAsync(stream, extLen, ct) < 2) return null;
                payloadLen = (extLen[0] << 8) | extLen[1];
            }
            else if (payloadLen == 127)
            {
                var extLen = new byte[8];
                if (await ReadExactAsync(stream, extLen, ct) < 8) return null;
                payloadLen = 0;
                for (int i = 0; i < 8; i++)
                    payloadLen = (payloadLen << 8) | extLen[i];
            }

            byte[]? mask = null;
            if (masked)
            {
                mask = new byte[4];
                if (await ReadExactAsync(stream, mask, ct) < 4) return null;
            }

            if (payloadLen > 1_048_576) return null; // 1MB limit

            var payload = new byte[payloadLen];
            if (payloadLen > 0 && await ReadExactAsync(stream, payload, ct) < payloadLen) return null;

            if (mask != null)
            {
                for (int i = 0; i < payload.Length; i++)
                    payload[i] ^= mask[i % 4];
            }

            // Text frame (opcode 1) or continuation
            if (opcode == 0x01 || opcode == 0x00)
                return Encoding.UTF8.GetString(payload);

            // Ping → send pong
            if (opcode == 0x09)
            {
                await WebSocketSendFrameAsync(stream, 0x8A, payload, ct); // pong
                return await WebSocketReadTextAsync(stream, ct); // continue reading
            }

            return null;
        }
        catch { return null; }
    }

    private static async Task WebSocketSendFrameAsync(NetworkStream stream, byte opcodeWithFin, byte[] payload, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(opcodeWithFin);

        if (payload.Length < 126)
        {
            ms.WriteByte((byte)payload.Length);
        }
        else if (payload.Length <= 65535)
        {
            ms.WriteByte(126);
            ms.WriteByte((byte)(payload.Length >> 8));
            ms.WriteByte((byte)(payload.Length & 0xFF));
        }
        else
        {
            ms.WriteByte(127);
            var len = (long)payload.Length;
            for (int i = 7; i >= 0; i--)
                ms.WriteByte((byte)((len >> (i * 8)) & 0xFF));
        }

        ms.Write(payload);
        var frame = ms.ToArray();
        await stream.WriteAsync(frame, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0) return totalRead;
            totalRead += read;
        }
        return totalRead;
    }
}

public class HttpRequest
{
    public string Method { get; set; } = "GET";
    public string Path { get; set; } = "/";
    public Dictionary<string, string> QueryParams { get; set; } = new();
    public Dictionary<string, string> RouteParams { get; set; } = new();
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The body exactly as it arrived. This is the one to use for anything that is not text.</summary>
    public byte[]? BodyBytes
    {
        get => _bodyBytes;
        set
        {
            _bodyBytes = value;
            _body = null;
            _bodyDecoded = false;
        }
    }

    /// <summary>
    /// UTF-8 view of <see cref="BodyBytes"/>, decoded on first read. Handlers that only ever see
    /// JSON keep using this; a binary upload never touches it and so is never mangled by the decode.
    /// </summary>
    public string? Body
    {
        get
        {
            if (!_bodyDecoded)
            {
                _body = _bodyBytes == null ? null : Encoding.UTF8.GetString(_bodyBytes);
                _bodyDecoded = true;
            }
            return _body;
        }
        set
        {
            _body = value;
            _bodyDecoded = true;
            _bodyBytes = value == null ? null : Encoding.UTF8.GetBytes(value);
        }
    }

    /// <summary>The request's Content-Type with any parameters stripped, lower-cased.</summary>
    public string? ContentType
    {
        get
        {
            if (!Headers.TryGetValue("Content-Type", out var value) || string.IsNullOrWhiteSpace(value))
                return null;

            var semicolon = value.IndexOf(';');
            return (semicolon >= 0 ? value[..semicolon] : value).Trim().ToLowerInvariant();
        }
    }

    private byte[]? _bodyBytes;
    private string? _body;
    private bool _bodyDecoded;

    internal MutationLeaseStatus? MutationLease { get; set; }
    internal string? MutationTargetAutomationId { get; set; }
    // Fallback identity for a target without an AutomationId, snapshotted BEFORE the mutation
    // runs. A control that rewrites its own text (the default MAUI counter button is the
    // canonical case) would otherwise be recorded under its post-mutation text, which cannot
    // resolve when the flow is replayed from the initial state.
    internal string? MutationTargetText { get; set; }
    internal string? MutationTargetType { get; set; }

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }

    internal object? MutationState { get; set; }

    private static readonly JsonSerializerOptions _readOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Reflection-based. Prefer the <see cref="JsonTypeInfo{T}"/> overload for anything new - it is
    /// the one that survives trimming and AOT.
    /// </summary>
    public T? BodyAs<T>() where T : class
        => Body != null ? JsonSerializer.Deserialize<T>(Body, _readOptions) : null;

    /// <summary>
    /// Deserializes the body through a source-generated contract, so nothing reflects over the type
    /// at runtime and the route survives trimming and AOT.
    /// </summary>
    public T? BodyAs<T>(JsonTypeInfo<T> typeInfo) where T : class
        => Body != null ? JsonSerializer.Deserialize(Body, typeInfo) : null;
}

public class HttpResponse
{
    public int StatusCode { get; set; } = 200;
    public string StatusText { get; set; } = "OK";
    public string ContentType { get; set; } = "application/json";
    public string? Body { get; set; }
    public byte[]? BodyBytes { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reflection-based. Prefer the <see cref="JsonTypeInfo{T}"/> overload for anything new - it is
    /// the one that survives trimming and AOT.
    /// </summary>
    public static HttpResponse Json(object data) => new()
    {
        Body = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })
    };

    /// <inheritdoc cref="Json(object)"/>
    public static HttpResponse Json(object data, int statusCode) => new()
    {
        StatusCode = statusCode,
        StatusText = StatusTextFor(statusCode),
        Body = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })
    };

    /// <summary>
    /// Serializes through a source-generated contract. The overload to reach for: nothing reflects
    /// over the type at runtime, so the route keeps working under trimming and AOT.
    /// </summary>
    public static HttpResponse Json<T>(T data, JsonTypeInfo<T> typeInfo) => new()
    {
        Body = JsonSerializer.Serialize(data, typeInfo)
    };

    public static HttpResponse Png(byte[] data) => new()
    {
        ContentType = "image/png",
        BodyBytes = data
    };

    /// <summary>Raw bytes with a caller-chosen content type - a file download that skips base64.</summary>
    public static HttpResponse Binary(byte[] data, string contentType = "application/octet-stream") => new()
    {
        ContentType = contentType,
        BodyBytes = data
    };

    internal static string StatusTextFor(int statusCode) => statusCode switch
    {
        200 => "OK",
        201 => "Created",
        204 => "No Content",
        403 => "Forbidden",
        404 => "Not Found",
        408 => "Request Timeout",
        409 => "Conflict",
        413 => "Payload Too Large",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        501 => "Not Implemented",
        503 => "Service Unavailable",
        _ => "Bad Request"
    };

    public static HttpResponse Ok(string? message = null) => new()
    {
        Body = JsonSerializer.Serialize(
            new SuccessResponse { Success = true, Message = message },
            AgentJsonContext.Default.SuccessResponse)
    };

    public static HttpResponse Error(string message, int statusCode = 400, string? reason = null, object? details = null)
    {
        // The overwhelmingly common case carries no details, and it goes through the source
        // generator. Only the handful of routes that attach an arbitrary details payload fall
        // through to the reflection path below.
        var body = details == null
            ? JsonSerializer.Serialize(
                new ErrorResponse { Success = false, Error = message, Reason = NullIfBlank(reason) },
                AgentJsonContext.Default.ErrorResponse)
            : SerializeErrorWithDetails(message, reason, details);

        return new HttpResponse
        {
            StatusCode = statusCode,
            StatusText = StatusTextFor(statusCode),
            Body = body
        };
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>The details payload is caller-supplied, so this one cannot be source-generated.</summary>
    private static string SerializeErrorWithDetails(string message, string? reason, object details)
    {
        var body = new Dictionary<string, object?>
        {
            ["success"] = false,
            ["error"] = message
        };

        if (!string.IsNullOrWhiteSpace(reason))
            body["reason"] = reason;

        body["details"] = details;

        return JsonSerializer.Serialize(body, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    public static HttpResponse NotFound(string message = "Not found") => Error(message, 404);
}
