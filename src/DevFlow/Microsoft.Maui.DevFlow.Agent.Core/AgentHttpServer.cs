using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Lightweight HTTP server using TcpListener (sandbox-friendly, no HttpListener).
/// Routes incoming requests to registered handlers.
/// </summary>
public class AgentHttpServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _disposed;
    private readonly int _port;
    private readonly Dictionary<string, Func<HttpRequest, Task<HttpResponse>>> _getRoutes = new();
    private readonly Dictionary<string, Func<HttpRequest, Task<HttpResponse>>> _postRoutes = new();
    private readonly Dictionary<string, Func<HttpRequest, Task<HttpResponse>>> _putRoutes = new();
    private readonly Dictionary<string, Func<HttpRequest, Task<HttpResponse>>> _deleteRoutes = new();
    private readonly Dictionary<string, Func<TcpClient, NetworkStream, HttpRequest, CancellationToken, Task>> _wsRoutes = new();

    public int Port => _port;
    public bool IsRunning => _listenTask != null && !_listenTask.IsCompleted;

    public AgentHttpServer(int port = 9223)
    {
        _port = port;
    }

    public void MapGet(string path, Func<HttpRequest, Task<HttpResponse>> handler)
        => _getRoutes[path.TrimEnd('/')] = handler;

    public void MapPost(string path, Func<HttpRequest, Task<HttpResponse>> handler)
        => _postRoutes[path.TrimEnd('/')] = handler;

    public void MapPut(string path, Func<HttpRequest, Task<HttpResponse>> handler)
        => _putRoutes[path.TrimEnd('/')] = handler;

    public void MapDelete(string path, Func<HttpRequest, Task<HttpResponse>> handler)
        => _deleteRoutes[path.TrimEnd('/')] = handler;

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
            var request = await ReadRequestAsync(stream, ct).ConfigureAwait(false);
            if (request == null)
            {
                client.Dispose();
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
                    + "Access-Control-Allow-Origin: *\r\n"
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
                var response = await RouteRequestAsync(request).ConfigureAwait(false);
                await WriteResponseAsync(stream, response, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) { Console.WriteLine($"[Microsoft.Maui.DevFlow.Agent] Request error: {ex.GetType().Name}: {ex.Message}"); }
    }

    private async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var totalRead = 0;

        // Read with timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token).ConfigureAwait(false);
            if (read == 0) return null;
            totalRead = read;
        }
        catch { return null; }

        var raw = Encoding.UTF8.GetString(buffer, 0, totalRead);
        var lines = raw.Split("\r\n");
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

        // Find body (after blank line)
        string? body = null;
        var blankLineIdx = raw.IndexOf("\r\n\r\n");
        if (blankLineIdx >= 0)
        {
            body = raw[(blankLineIdx + 4)..];

            // Check Content-Length for more body data
            var contentLengthLine = lines.FirstOrDefault(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
            if (contentLengthLine != null)
            {
                var clValue = contentLengthLine.Split(':', 2)[1].Trim();
                if (int.TryParse(clValue, out var contentLength) && body.Length < contentLength)
                {
                    var remaining = contentLength - body.Length;
                    var bodyBuffer = new byte[remaining];
                    var bodyRead = 0;
                    while (bodyRead < remaining)
                    {
                        var r = await stream.ReadAsync(bodyBuffer.AsMemory(bodyRead, remaining - bodyRead), ct).ConfigureAwait(false);
                        if (r == 0) break;
                        bodyRead += r;
                    }
                    body += Encoding.UTF8.GetString(bodyBuffer, 0, bodyRead);
                }
            }
        }

        return new HttpRequest
        {
            Method = method,
            Path = path.TrimEnd('/'),
            QueryParams = queryParams,
            Headers = headers,
            Body = body
        };
    }

    private async Task<HttpResponse> RouteRequestAsync(HttpRequest request)
    {
        var routes = request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) ? _postRoutes
            : request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ? _putRoutes
            : request.Method.Equals("DELETE", StringComparison.OrdinalIgnoreCase) ? _deleteRoutes
            : _getRoutes;

        // Try exact match first
        if (routes.TryGetValue(request.Path, out var handler))
            return await handler(request).ConfigureAwait(false);

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
                return await kvp.Value(request).ConfigureAwait(false);

            request.RouteParams.Clear();
        }

        return HttpResponse.NotFound("Route not found");
    }

    private static async Task WriteResponseAsync(NetworkStream stream, HttpResponse response, CancellationToken ct)
    {
        var bodyBytes = response.Body != null ? Encoding.UTF8.GetBytes(response.Body) : Array.Empty<byte>();
        var headerBuilder = new StringBuilder();
        headerBuilder.Append($"HTTP/1.1 {response.StatusCode} {response.StatusText}\r\n");
        headerBuilder.Append($"Content-Type: {response.ContentType}\r\n");
        headerBuilder.Append($"Content-Length: {(response.BodyBytes ?? bodyBytes).Length}\r\n");
        headerBuilder.Append("Access-Control-Allow-Origin: *\r\n");
        headerBuilder.Append("Connection: close\r\n");
        headerBuilder.Append("\r\n");

        var headerBytes = Encoding.UTF8.GetBytes(headerBuilder.ToString());
        await stream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
        await stream.WriteAsync(response.BodyBytes ?? bodyBytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
    }

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
    public string? Body { get; set; }

    private static readonly JsonSerializerOptions _readOptions = new() { PropertyNameCaseInsensitive = true };

    public T? BodyAs<T>() where T : class
        => Body != null ? JsonSerializer.Deserialize<T>(Body, _readOptions) : null;
}

public class HttpResponse
{
    public int StatusCode { get; set; } = 200;
    public string StatusText { get; set; } = "OK";
    public string ContentType { get; set; } = "application/json";
    public string? Body { get; set; }
    public byte[]? BodyBytes { get; set; }

    public static HttpResponse Json(object data) => new()
    {
        Body = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })
    };

    public static HttpResponse Png(byte[] data) => new()
    {
        ContentType = "image/png",
        BodyBytes = data
    };

    public static HttpResponse Ok(string? message = null) => new()
    {
        Body = JsonSerializer.Serialize(new { success = true, message })
    };

    public static HttpResponse Error(string message, int statusCode = 400, string? reason = null, object? details = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["success"] = false,
            ["error"] = message
        };

        if (!string.IsNullOrWhiteSpace(reason))
            body["reason"] = reason;

        if (details != null)
            body["details"] = details;

        return new HttpResponse
        {
            StatusCode = statusCode,
            StatusText = statusCode switch
            {
                403 => "Forbidden",
                404 => "Not Found",
                408 => "Request Timeout",
                500 => "Internal Server Error",
                _ => "Bad Request"
            },
            Body = JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            })
        };
    }

    public static HttpResponse NotFound(string message = "Not found") => Error(message, 404);
}
