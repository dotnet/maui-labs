using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

/// <summary>
/// Lightweight HTTP server that serves the DevFlow Web Inspector.
/// Generates an interactive HTML page representing the native app's visual tree
/// and proxies interaction commands to the DevFlow agent.
/// </summary>
public sealed class InspectorServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private readonly int _port;
    private readonly string _agentHost;
    private readonly int _agentPort;
    private byte[]? _cachedScreenshot;
    private DateTime _screenshotCacheTime;
    private string? _rootPageId;
    private static readonly TimeSpan ScreenshotCacheDuration = TimeSpan.FromMilliseconds(200);

    public int Port => _port;

    public InspectorServer(int port, string agentHost, int agentPort)
    {
        _port = port;
        _agentHost = agentHost;
        _agentPort = agentPort;
    }

    /// <summary>
    /// Handles an HTTP request from the broker, routing it through the inspector logic.
    /// This allows the broker to serve inspector pages without a separate listener.
    /// </summary>
    public async Task HandleBrokerRequestAsync(HttpListenerContext context, string path)
    {
        try
        {
            // Handle WebSocket upgrade for /ws/events
            if (context.Request.IsWebSocketRequest && path.TrimEnd('/') == "/ws/events")
            {
                await HandleBrokerWebSocketProxy(context);
                return;
            }

            var method = context.Request.HttpMethod;
            string? body = null;
            if (method == "POST" && context.Request.HasEntityBody)
            {
                using var reader = new System.IO.StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                body = await reader.ReadToEndAsync();
            }

            var request = new HttpRequestInfo { Method = method, Path = path, Body = body };
            var (statusCode, contentType, responseBody) = await RouteAsync(request);

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = contentType;
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.ContentLength64 = responseBody.Length;
            await context.Response.OutputStream.WriteAsync(responseBody);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            try
            {
                context.Response.StatusCode = 500;
                var msg = Encoding.UTF8.GetBytes($"Inspector error: {ex.Message}");
                await context.Response.OutputStream.WriteAsync(msg);
                context.Response.Close();
            }
            catch { }
        }
    }

    /// <summary>
    /// Proxies a WebSocket connection from the broker to the agent's /ws/events endpoint.
    /// </summary>
    private async Task HandleBrokerWebSocketProxy(HttpListenerContext context)
    {
        var wsContext = await context.AcceptWebSocketAsync(null);
        var clientWs = wsContext.WebSocket;

        using var agentWs = new System.Net.WebSockets.ClientWebSocket();
        var agentUri = new Uri($"ws://{_agentHost}:{_agentPort}/ws/events");

        try
        {
            await agentWs.ConnectAsync(agentUri, CancellationToken.None);
        }
        catch
        {
            await clientWs.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.EndpointUnavailable,
                "Agent not reachable", CancellationToken.None);
            return;
        }

        // Relay messages from agent to browser
        var buffer = new byte[4096];
        try
        {
            while (agentWs.State == System.Net.WebSockets.WebSocketState.Open &&
                   clientWs.State == System.Net.WebSockets.WebSocketState.Open)
            {
                var result = await agentWs.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;

                await clientWs.SendAsync(
                    new ArraySegment<byte>(buffer, 0, result.Count),
                    result.MessageType, result.EndOfMessage, CancellationToken.None);
            }
        }
        catch { }
        finally
        {
            if (clientWs.State == System.Net.WebSockets.WebSocketState.Open)
                try { await clientWs.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
            if (agentWs.State == System.Net.WebSockets.WebSocketState.Open)
                try { await agentWs.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        }
    }

    public void Start()
    {
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

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, ct);
                if (request == null) return;

                // Check for WebSocket upgrade on /ws/events
                if (request.Path == "/ws/events" &&
                    request.Headers.TryGetValue("upgrade", out var upgrade) &&
                    upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleWebSocketProxy(client, stream, request, ct);
                    return;
                }

                var (statusCode, contentType, body) = await RouteAsync(request);
                await WriteResponseAsync(stream, statusCode, contentType, body, ct);
            }
        }
        catch { }
    }

    private async Task<(int statusCode, string contentType, byte[] body)> RouteAsync(HttpRequestInfo request)
    {
        try
        {
            return request.Method switch
            {
                "GET" => request.Path switch
                {
                    "/" or "" => await HandleRootAsync(),
                    "/api/state" => await HandleStateAsync(),
                    "/screenshot.png" => await HandleScreenshotAsync(),
                    "/devflow.js" => HandleEmbeddedFile("devflow.js", "application/javascript"),
                    "/devflow.css" => HandleEmbeddedFile("devflow.css", "text/css"),
                    _ => (404, "text/plain", Encoding.UTF8.GetBytes("Not Found"))
                },
                "POST" => request.Path switch
                {
                    "/api/tap" => await HandleProxyTapAsync(request.Body),
                    "/api/scroll" => await HandleProxyScrollAsync(request.Body),
                    "/api/gesture" => await HandleProxyGestureAsync(request.Body),
                    "/api/back" => await HandleProxyBackAsync(),
                    "/api/fill" => await HandleProxyFillAsync(request.Body),
                    "/api/key" => await HandleProxyKeyAsync(request.Body),
                    _ => (404, "text/plain", Encoding.UTF8.GetBytes("Not Found"))
                },
                _ => (405, "text/plain", Encoding.UTF8.GetBytes("Method Not Allowed"))
            };
        }
        catch (Exception ex)
        {
            return (500, "text/plain", Encoding.UTF8.GetBytes($"Error: {ex.Message}"));
        }
    }

    private async Task<(int, string, byte[])> HandleRootAsync()
    {
        using var client = new AgentClient(_agentHost, _agentPort);
        var tree = await client.GetTreeAsync();

        // Find the root page element (first child of Window with content).
        // On Mac Catalyst, the default screenshot captures the full screen but element
        // bounds are relative to the page content. By screenshotting the page element
        // directly we get a 1:1 match between pixel coordinates and element bounds.
        var rootPageId = FindRootPageId(tree);
        _rootPageId = rootPageId;
        var screenshot = await GetCachedScreenshotAsync(client, rootPageId);
        var hasScreenshot = screenshot?.Length > 0;

        double viewportWidth = 800, viewportHeight = 600;
        if (hasScreenshot)
        {
            var (pw, ph) = GetPngDimensions(screenshot!);
            viewportWidth = pw;
            viewportHeight = ph;
        }

        var html = HtmlRenderer.Render(tree, hasScreenshot, (int)viewportWidth, (int)viewportHeight, 1, 1);
        return (200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
    }

    /// <summary>
    /// Returns JSON state for AJAX polling: screenshot (as timestamped URL) + element divs HTML.
    /// This avoids full page reload flash.
    /// </summary>
    private async Task<(int, string, byte[])> HandleStateAsync()
    {
        using var client = new AgentClient(_agentHost, _agentPort);
        var tree = await client.GetTreeAsync();

        var rootPageId = FindRootPageId(tree);
        _rootPageId = rootPageId;
        _cachedScreenshot = null; // force fresh screenshot
        var screenshot = await GetCachedScreenshotAsync(client, rootPageId);
        var hasScreenshot = screenshot?.Length > 0;

        double viewportWidth = 800, viewportHeight = 600;
        if (hasScreenshot)
        {
            var (pw, ph) = GetPngDimensions(screenshot!);
            viewportWidth = pw;
            viewportHeight = ph;
        }

        var elementsHtml = HtmlRenderer.RenderElements(tree, 1);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var json = JsonSerializer.Serialize(new
        {
            screenshotUrl = $"screenshot.png?t={timestamp}",
            elements = elementsHtml,
            viewportWidth,
            viewportHeight
        });

        return (200, "application/json", Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Finds the ID of the topmost page element in the tree.
    /// When a modal page is showing, it appears as a later child of the Window,
    /// so we take the last child which is the topmost visible page.
    /// </summary>
    private static string? FindRootPageId(List<ElementInfo> tree)
    {
        if (tree.Count == 0) return null;
        var window = tree[0];
        if (window.Children is not { Count: > 0 }) return null;
        // Last child is the topmost (modal pages are added after the shell)
        return window.Children[^1].Id;
    }

    /// <summary>Reads width/height from PNG IHDR chunk (bytes 16-23).</summary>
    private static (int width, int height) GetPngDimensions(byte[] png)
    {
        if (png.Length < 24) return (0, 0);
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w, h);
    }

    private async Task<(int, string, byte[])> HandleScreenshotAsync()
    {
        using var client = new AgentClient(_agentHost, _agentPort);
        var png = await GetCachedScreenshotAsync(client, _rootPageId);
        if (png == null || png.Length == 0)
            return (404, "text/plain", Encoding.UTF8.GetBytes("No screenshot available"));
        return (200, "image/png", png);
    }

    private (int, string, byte[]) HandleEmbeddedFile(string fileName, string contentType)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Microsoft.Maui.Cli.DevFlow.Inspector.Web.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return (404, "text/plain", Encoding.UTF8.GetBytes($"Resource not found: {resourceName}"));

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return (200, contentType, ms.ToArray());
    }

    private async Task<byte[]?> GetCachedScreenshotAsync(AgentClient client, string? elementId = null)
    {
        if (_cachedScreenshot != null && DateTime.UtcNow - _screenshotCacheTime < ScreenshotCacheDuration)
            return _cachedScreenshot;

        _cachedScreenshot = await client.ScreenshotAsync(elementId: elementId);
        _screenshotCacheTime = DateTime.UtcNow;
        return _cachedScreenshot;
    }

    // ── Proxy handlers ──

    private async Task<(int, string, byte[])> HandleProxyTapAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Support coordinate-based tap: hit-test first, then tap the element
        if (root.TryGetProperty("x", out var xProp) && root.TryGetProperty("y", out var yProp))
        {
            var x = xProp.GetDouble();
            var y = yProp.GetDouble();

            using var client = new AgentClient(_agentHost, _agentPort);
            var hitResult = await client.HitTestAsync(x, y);

            // Parse hit-test result — response is { elements: [{ id, ... }, ...] }
            using var hitDoc = JsonDocument.Parse(hitResult);
            var elements = hitDoc.RootElement.GetProperty("elements");
            if (elements.GetArrayLength() > 0)
            {
                // Try elements from most specific to most general until one accepts tap
                for (int i = 0; i < elements.GetArrayLength(); i++)
                {
                    var elementId = elements[i].GetProperty("id").GetString();
                    if (!string.IsNullOrEmpty(elementId))
                    {
                        var success = await client.TapAsync(elementId);
                        if (success)
                        {
                            _cachedScreenshot = null;
                            return (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"));
                        }
                    }
                }
            }
            return (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"reason\":\"No tappable element at coordinates\"}"));
        }

        // Support elementId-based tap
        if (root.TryGetProperty("elementId", out var elIdProp))
        {
            var elementId = elIdProp.GetString();
            if (!string.IsNullOrEmpty(elementId))
            {
                using var client = new AgentClient(_agentHost, _agentPort);
                var success = await client.TapAsync(elementId);
                _cachedScreenshot = null;
                return success
                    ? (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
                    : (500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"));
            }
        }

        return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"x/y or elementId required\"}"));
    }

    private async Task<(int, string, byte[])> HandleProxyScrollAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var deltaX = root.TryGetProperty("deltaX", out var dxProp) ? dxProp.GetDouble() : 0;
        var deltaY = root.TryGetProperty("deltaY", out var dyProp) ? dyProp.GetDouble() : 0;

        // If coordinates provided, hit-test and try each element for scroll
        if (root.TryGetProperty("x", out var xProp) && root.TryGetProperty("y", out var yProp))
        {
            using var client = new AgentClient(_agentHost, _agentPort);
            var hitResult = await client.HitTestAsync(xProp.GetDouble(), yProp.GetDouble());
            using var hitDoc = JsonDocument.Parse(hitResult);
            var elements = hitDoc.RootElement.GetProperty("elements");

            // Try each element from most specific to general until one accepts scroll
            for (int i = 0; i < elements.GetArrayLength(); i++)
            {
                var elementId = elements[i].GetProperty("id").GetString();
                if (!string.IsNullOrEmpty(elementId))
                {
                    var success = await client.ScrollAsync(elementId: elementId, deltaX: deltaX, deltaY: deltaY);
                    if (success)
                    {
                        _cachedScreenshot = null;
                        return (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"));
                    }
                }
            }
        }

        // Fallback: scroll without element target
        {
            using var client = new AgentClient(_agentHost, _agentPort);
            var success = await client.ScrollAsync(deltaX: deltaX, deltaY: deltaY);
            _cachedScreenshot = null;
            return success
                ? (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
                : (500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"));
        }
    }

    private async Task<(int, string, byte[])> HandleProxyGestureAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Determine swipe direction from gesture points
        if (root.TryGetProperty("points", out var pointsArr) && pointsArr.GetArrayLength() >= 2)
        {
            var first = pointsArr[0];
            var last = pointsArr[pointsArr.GetArrayLength() - 1];
            var dx = last.GetProperty("x").GetDouble() - first.GetProperty("x").GetDouble();
            var dy = last.GetProperty("y").GetDouble() - first.GetProperty("y").GetDouble();

            var direction = Math.Abs(dx) > Math.Abs(dy)
                ? (dx > 0 ? "right" : "left")
                : (dy > 0 ? "down" : "up");

            var distance = Math.Sqrt(dx * dx + dy * dy);

            using var client = new AgentClient(_agentHost, _agentPort);
            var success = await client.GestureAsync("swipe", direction: direction, distance: distance);
            _cachedScreenshot = null;
            return success
                ? (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
                : (500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"));
        }

        return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"points array required\"}"));
    }

    private async Task<(int, string, byte[])> HandleProxyBackAsync()
    {
        using var client = new AgentClient(_agentHost, _agentPort);
        var success = await client.BackAsync();
        _cachedScreenshot = null;
        return success
            ? (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
            : (500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"));
    }

    private async Task<(int, string, byte[])> HandleProxyFillAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var elementId = root.TryGetProperty("elementId", out var idProp) ? idProp.GetString() : null;
        var text = root.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;

        if (string.IsNullOrEmpty(elementId) || text == null)
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"elementId and text required\"}"));

        using var client = new AgentClient(_agentHost, _agentPort);
        var success = await client.FillAsync(elementId, text);
        _cachedScreenshot = null;
        return success
            ? (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
            : (500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"));
    }

    private async Task<(int, string, byte[])> HandleProxyKeyAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var key = root.TryGetProperty("key", out var keyProp) ? keyProp.GetString() : null;
        var elementId = root.TryGetProperty("elementId", out var idProp) ? idProp.GetString() : null;

        if (string.IsNullOrEmpty(key))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"key required\"}"));

        using var client = new AgentClient(_agentHost, _agentPort);
        var success = await client.KeyAsync(key, elementId);
        _cachedScreenshot = null;
        return success
            ? (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
            : (500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"));
    }

    // ── WebSocket proxy (pass-through to agent /ws/v1/ui/events) ──

    private async Task HandleWebSocketProxy(TcpClient tcpClient, NetworkStream clientStream, HttpRequestInfo request, CancellationToken ct)
    {
        // Complete WebSocket handshake with browser
        if (!request.Headers.TryGetValue("sec-websocket-key", out var wsKey))
            return;

        var acceptKey = Convert.ToBase64String(
            System.Security.Cryptography.SHA1.HashData(
                Encoding.UTF8.GetBytes(wsKey + "258EAFA5-E914-47DA-95CA-5AB5DC4B46D6")));

        var handshake = $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {acceptKey}\r\n\r\n";
        await clientStream.WriteAsync(Encoding.UTF8.GetBytes(handshake), ct);
        await clientStream.FlushAsync(ct);

        // Connect to agent WebSocket and relay messages
        using var agentWs = new System.Net.WebSockets.ClientWebSocket();
        try
        {
            await agentWs.ConnectAsync(new Uri($"ws://{_agentHost}:{_agentPort}/ws/v1/ui/events"), ct);

            // Subscribe to all events
            var subscribe = Encoding.UTF8.GetBytes("{\"type\":\"subscribe\",\"data\":{\"events\":[\"all\"]}}");
            await agentWs.SendAsync(subscribe, System.Net.WebSockets.WebSocketMessageType.Text, true, ct);

            // Relay agent messages to browser
            var buffer = new byte[8192];
            while (!ct.IsCancellationRequested && agentWs.State == System.Net.WebSockets.WebSocketState.Open)
            {
                var result = await agentWs.ReceiveAsync(buffer, ct);
                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    break;

                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Text)
                {
                    var payload = buffer.AsMemory(0, result.Count).ToArray();
                    await SendWebSocketFrameAsync(clientStream, payload, ct);
                }
            }
        }
        catch { }
    }

    private static async Task SendWebSocketFrameAsync(NetworkStream stream, byte[] payload, CancellationToken ct)
    {
        // Build a text frame (FIN + opcode 0x1)
        var frame = new List<byte> { 0x81 }; // FIN + text
        if (payload.Length < 126)
            frame.Add((byte)payload.Length);
        else if (payload.Length <= 65535)
        {
            frame.Add(126);
            frame.Add((byte)(payload.Length >> 8));
            frame.Add((byte)(payload.Length & 0xFF));
        }
        else
        {
            frame.Add(127);
            var len = (long)payload.Length;
            for (int i = 7; i >= 0; i--)
                frame.Add((byte)((len >> (i * 8)) & 0xFF));
        }
        frame.AddRange(payload);
        await stream.WriteAsync(frame.ToArray(), ct);
        await stream.FlushAsync(ct);
    }

    // ── HTTP parsing helpers ──

    private static async Task<HttpRequestInfo?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        int read;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            read = await stream.ReadAsync(buffer, timeoutCts.Token);
            if (read == 0) return null;
        }
        catch { return null; }

        var raw = Encoding.UTF8.GetString(buffer, 0, read);
        var headerEnd = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEnd < 0) return null;

        var headerSection = raw[..headerEnd];
        var bodySection = raw[(headerEnd + 4)..];

        var lines = headerSection.Split("\r\n");
        if (lines.Length == 0) return null;

        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2) return null;

        var method = requestLine[0].ToUpperInvariant();
        var path = requestLine[1].Split('?')[0].TrimEnd('/');
        if (string.IsNullOrEmpty(path)) path = "/";

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            var colonIdx = lines[i].IndexOf(':');
            if (colonIdx > 0)
            {
                var key = lines[i][..colonIdx].Trim();
                var value = lines[i][(colonIdx + 1)..].Trim();
                headers[key] = value;
            }
        }

        // Read remaining body if Content-Length indicates more
        var body = bodySection;
        if (headers.TryGetValue("content-length", out var clStr) && int.TryParse(clStr, out var contentLength))
        {
            while (Encoding.UTF8.GetByteCount(body) < contentLength)
            {
                var extraRead = await stream.ReadAsync(buffer, ct);
                if (extraRead == 0) break;
                body += Encoding.UTF8.GetString(buffer, 0, extraRead);
            }
        }

        return new HttpRequestInfo
        {
            Method = method,
            Path = path,
            Headers = headers,
            Body = body
        };
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int statusCode, string contentType, byte[] body, CancellationToken ct)
    {
        var statusText = statusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            500 => "Internal Server Error",
            _ => "Unknown"
        };

        var header = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                     $"Content-Type: {contentType}\r\n" +
                     $"Content-Length: {body.Length}\r\n" +
                     "Access-Control-Allow-Origin: *\r\n" +
                     "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                     "Access-Control-Allow-Headers: Content-Type\r\n" +
                     "Connection: close\r\n\r\n";

        await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    internal sealed class HttpRequestInfo
    {
        public string Method { get; init; } = "";
        public string Path { get; init; } = "";
        public Dictionary<string, string> Headers { get; init; } = new();
        public string? Body { get; init; }
    }
}
