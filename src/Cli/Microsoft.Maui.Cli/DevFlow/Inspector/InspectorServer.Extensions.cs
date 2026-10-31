using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

// Extensions panel proxies. The browser never names an agent path: it names an extension namespace
// plus a tool or stream, and the agent path is resolved here from the agent's own capabilities
// descriptor. That keeps these routes from becoming a generic agent proxy. Every route is token-gated
// (plugin data can be as sensitive as logs or preferences), and the stream WebSocket additionally
// passes the same local-origin check as /ws/events.
public sealed partial class InspectorServer
{
    internal const string ExtensionStreamWebSocketPath = "/ws/extensions";
    private const string ExtensionToolPathPrefix = "/api/v1/ext/";
    private const string ExtensionStreamWebSocketPathPrefix = "/ws/v1/ext/";
    private const int MaxExtensionResponseBytes = 1_048_576;
    private const int MaxExtensionEventLimit = 500;

    // One shared client: tool calls are infrequent and must carry this inspector's lease headers,
    // which AgentClient.CallExtensionToolAsync would apply but then discard the agent's error body.
    private static readonly HttpClient ExtensionHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    private async Task<(int, string, byte[])> HandleExtensionsListAsync()
    {
        var capabilities = await _client.GetCapabilitiesAsync();
        if (capabilities.ValueKind != JsonValueKind.Object)
            return Ok("{\"ok\":false,\"unavailable\":true,\"error\":\"The DevFlow agent is unavailable.\"}");

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", true);
            writer.WritePropertyName("extensions");
            if (capabilities.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Object)
                extensions.WriteTo(writer);
            else
                writer.WriteRawValue("{}");
            writer.WritePropertyName("extensionErrors");
            if (capabilities.TryGetProperty("extensionErrors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                errors.WriteTo(writer);
            else
                writer.WriteRawValue("[]");
            writer.WriteEndObject();
        }
        return (200, "application/json", buffer.ToArray());
    }

    private async Task<(int, string, byte[])> HandleExtensionCallAsync(
        string? body,
        string leaseId,
        string holderKind,
        string holderLabel)
    {
        string? ns, toolName;
        JsonElement? parameters = null;
        try
        {
            if (string.IsNullOrEmpty(body))
                return BadRequest("namespace and tool are required");
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            ns = root.TryGetProperty("namespace", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            toolName = root.TryGetProperty("tool", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (root.TryGetProperty("parameters", out var p) && p.ValueKind != JsonValueKind.Null)
            {
                if (p.ValueKind != JsonValueKind.Object)
                    return BadRequest("parameters must be a JSON object");
                parameters = p.Clone();
            }
        }
        catch (JsonException)
        {
            return BadRequest("request body must be JSON");
        }

        if (string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(toolName) || ns.Length > 256 || toolName.Length > 256)
            return BadRequest("namespace and tool are required");

        var extensions = await _client.GetExtensionsAsync();
        if (!extensions.TryGetValue(ns, out var extension))
            return NotFoundJson($"Extension '{ns}' was not found.");
        var tool = extension.Tools.FirstOrDefault(x => string.Equals(x.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
            return NotFoundJson($"Tool '{toolName}' was not found in extension '{ns}'.");
        if (!tool.Path.StartsWith(ExtensionToolPathPrefix, StringComparison.Ordinal))
            return BadRequest("The agent advertised a tool path outside the extension route space.");

        var method = string.IsNullOrWhiteSpace(tool.Method) ? "GET" : tool.Method.Trim().ToUpperInvariant();
        if (method is not ("GET" or "POST" or "PUT" or "DELETE"))
            return BadRequest($"Unsupported tool method '{method}'.");

        var mutates = ExtensionToolMutates(method, tool.Annotations?.ReadOnly == true);
        if (mutates)
        {
            if (Volatile.Read(ref _replayInProgress) == 1)
                return (409, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"A replay is in progress — try again when it finishes.\"}"));

            // Same agent-enforced global lease the Inspector's own mutations claim.
            var status = await _client.ControlMutationLeaseAsync("claim", force: false, leaseId, holderKind, holderLabel);
            if (!status.YouHold)
            {
                var payload = JsonSerializer.Serialize(new
                {
                    ok = false,
                    reason = "writer",
                    error = "Another session is driving this app.",
                    holderKind = status.HolderKind,
                    label = status.Label,
                    expiresInMs = status.ExpiresInMs,
                    authority = status.Authority
                }, CamelCase);
                return (409, "application/json", Encoding.UTF8.GetBytes(payload));
            }
        }

        using var request = new HttpRequestMessage(new HttpMethod(method), BuildExtensionToolUri(method, tool.Path, parameters));
        if (method != "GET")
            request.Content = new StringContent(parameters?.GetRawText() ?? "{}", Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("X-DevFlow-Lease", leaseId);
        request.Headers.TryAddWithoutValidation("X-DevFlow-Holder", holderKind);
        request.Headers.TryAddWithoutValidation("X-DevFlow-Label", holderLabel);

        var started = System.Diagnostics.Stopwatch.StartNew();
        using var response = await ExtensionHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var (text, truncated) = await ReadCappedAsync(response.Content);
        started.Stop();
        if (mutates && response.IsSuccessStatusCode)
            InvalidateScreenshotCache();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", response.IsSuccessStatusCode);
            writer.WriteNumber("status", (int)response.StatusCode);
            writer.WriteString("method", method);
            writer.WriteString("path", tool.Path);
            writer.WriteNumber("durationMs", started.ElapsedMilliseconds);
            writer.WriteBoolean("truncated", truncated);
            if (contentType is not null)
                writer.WriteString("contentType", contentType);
            writer.WritePropertyName("body");
            if (!truncated && TryParseJson(text, out var json))
                json.WriteTo(writer);
            else
                writer.WriteStringValue(text);
            writer.WriteEndObject();
        }
        return (200, "application/json", buffer.ToArray());
    }

    private async Task<(int, string, byte[])> HandleExtensionEventsAsync(string? body)
    {
        var ns = ReadStringField(body, "namespace");
        var streamName = ReadStringField(body, "stream");
        if (string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(streamName))
            return BadRequest("namespace and stream are required");
        var since = ReadLongField(body, "since");
        var limit = ReadIntField(body, "limit", 100, 1, MaxExtensionEventLimit);

        var stream = await FindExtensionStreamAsync(ns, streamName);
        if (stream is null)
            return NotFoundJson($"Stream '{streamName}' was not found in extension '{ns}'.");

        var page = await _client.GetExtensionEventsAsync(ns, stream.Name, since, limit);
        return Ok(JsonSerializer.Serialize(new
        {
            ok = true,
            events = page.Events.Select(e => new { seq = e.Sequence, ts = e.Timestamp, data = e.Data }),
            nextSequence = page.NextSequence,
            dropped = page.Dropped
        }, CamelCase));
    }

    internal static bool ExtensionToolMutates(string method, bool readOnly)
        => !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && !readOnly;

    internal static string BuildExtensionToolUri(string agentBase, string method, string path, JsonElement? parameters)
    {
        var uri = agentBase + path;
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) || parameters is not { ValueKind: JsonValueKind.Object } values)
            return uri;

        var query = values.EnumerateObject()
            .Where(p => p.Value.ValueKind != JsonValueKind.Null)
            .Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText())}")
            .ToArray();
        return query.Length == 0 ? uri : uri + "?" + string.Join("&", query);
    }

    private string BuildExtensionToolUri(string method, string path, JsonElement? parameters)
        => BuildExtensionToolUri($"http://{_agentHost}:{_agentPort}", method, path, parameters);

    private async Task<ExtensionStreamInfo?> FindExtensionStreamAsync(string ns, string streamName)
    {
        var extensions = await _client.GetExtensionsAsync();
        if (!extensions.TryGetValue(ns, out var extension))
            return null;
        var stream = extension.Streams.FirstOrDefault(s => string.Equals(s.Name, streamName, StringComparison.OrdinalIgnoreCase));
        return stream is not null && stream.WebSocketPath.StartsWith(ExtensionStreamWebSocketPathPrefix, StringComparison.Ordinal)
            ? stream
            : null;
    }

    /// <summary>
    /// Resolves the agent WebSocket URL for a browser stream subscription, or returns the HTTP status
    /// the upgrade must be refused with.
    /// </summary>
    private async Task<(Uri? AgentUri, int Status)> ResolveExtensionStreamSocketAsync(IReadOnlyDictionary<string, string> query)
    {
        var token = query.TryGetValue("token", out var t) ? t : null;
        if (string.IsNullOrEmpty(token) || !string.Equals(token, _readToken, StringComparison.Ordinal))
            return (null, 403);

        var ns = query.TryGetValue("namespace", out var n) ? n : null;
        var streamName = query.TryGetValue("stream", out var s) ? s : null;
        if (string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(streamName))
            return (null, 400);

        ExtensionStreamInfo? stream;
        try { stream = await FindExtensionStreamAsync(ns, streamName); }
        catch (Exception ex) when (IsAgentUnavailableException(ex)) { return (null, 503); }
        if (stream is null)
            return (null, 404);

        var url = $"ws://{_agentHost}:{_agentPort}{stream.WebSocketPath}";
        if (query.TryGetValue("since", out var sinceText) && long.TryParse(sinceText, out var since) && since >= 0)
            url += $"?since={since}";
        return (new Uri(url), 200);
    }

    /// <summary>Broker mode: relays an extension stream WebSocket through HttpListener.</summary>
    private async Task HandleBrokerExtensionStreamProxy(HttpListenerContext context)
    {
        var query = context.Request.QueryString.AllKeys
            .Where(static key => key is not null)
            .ToDictionary(static key => key!, key => context.Request.QueryString[key!] ?? "", StringComparer.OrdinalIgnoreCase);
        var (agentUri, status) = await ResolveExtensionStreamSocketAsync(query);
        if (agentUri is null)
        {
            context.Response.StatusCode = status;
            context.Response.Close();
            return;
        }

        using var linkedCts = _cts != null
            ? CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, _cts.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var ct = linkedCts.Token;

        using var agentWs = new ClientWebSocket();
        try
        {
            await agentWs.ConnectAsync(agentUri, ct);
        }
        catch
        {
            context.Response.StatusCode = 503;
            context.Response.Close();
            return;
        }

        var wsContext = await context.AcceptWebSocketAsync(null);
        var clientWs = wsContext.WebSocket;
        try
        {
            var agentToClient = RelayLoopAsync(agentWs, clientWs, ct);
            var clientToAgent = RelayLoopAsync(clientWs, agentWs, ct);
            await Task.WhenAny(agentToClient, clientToAgent);
            try { linkedCts.Cancel(); } catch { }
        }
        catch { }
        finally
        {
            // Closing with 1012 (service restart) tells the browser the agent side went away, so it
            // reconnects instead of treating the stream as finished.
            if (clientWs.State == WebSocketState.Open)
                try { await clientWs.CloseAsync((WebSocketCloseStatus)1012, "Agent stream closed", CancellationToken.None); } catch { }
            else if (clientWs.State == WebSocketState.CloseReceived)
                // The browser closed first: complete its close handshake.
                try { await clientWs.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
            if (agentWs.State == WebSocketState.Open)
                try { await agentWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        }
    }

    /// <summary>Standalone mode: relays an extension stream over the raw TCP listener.</summary>
    private async Task HandleExtensionStreamProxy(NetworkStream clientStream, HttpRequestInfo request, CancellationToken ct)
    {
        if (!request.Headers.TryGetValue("sec-websocket-key", out var wsKey))
        {
            await WriteResponseAsync(clientStream, 400, "text/plain", Encoding.UTF8.GetBytes("Bad Request"), ct);
            return;
        }

        var (agentUri, status) = await ResolveExtensionStreamSocketAsync(request.Query);
        if (agentUri is null)
        {
            await WriteResponseAsync(clientStream, status, "text/plain", Encoding.UTF8.GetBytes(status == 403 ? "Forbidden" : "Unavailable"), ct);
            return;
        }

        using var browserClosedCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, browserClosedCts.Token);
        var linkedCt = linkedCts.Token;
        using var agentWs = new ClientWebSocket();
        try
        {
            await agentWs.ConnectAsync(agentUri, linkedCt);
        }
        catch
        {
            await WriteResponseAsync(clientStream, 503, "text/plain", Encoding.UTF8.GetBytes("Service Unavailable"), ct);
            return;
        }

        var handshake = $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {ComputeWebSocketAcceptKey(wsKey)}\r\n\r\n";
        await clientStream.WriteAsync(Encoding.UTF8.GetBytes(handshake), ct);
        await clientStream.FlushAsync(ct);

        try
        {
            // The browser only ever closes this socket (the relay never pings it), so any inbound
            // frame or EOF ends the relay, as for /ws/events.
            _ = Task.Run(async () =>
            {
                var monitor = new byte[256];
                try
                {
                    var read = await clientStream.ReadAsync(monitor, linkedCt);
                    _ = read; // a frame (Close) or 0 at EOF; either way the browser is gone
                }
                catch { }
                finally { try { browserClosedCts.Cancel(); } catch { } }
            }, linkedCt);

            const int MaxAssembledMessageBytes = 4 * 1024 * 1024;
            var buffer = new byte[8192];
            using var assembled = new MemoryStream();
            using var sendGate = new SemaphoreSlim(1, 1);
            while (!linkedCt.IsCancellationRequested && agentWs.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try { result = await agentWs.ReceiveAsync(buffer, linkedCt); }
                catch { break; }
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                assembled.Write(buffer, 0, result.Count);
                if (assembled.Length > MaxAssembledMessageBytes)
                    break;
                if (!result.EndOfMessage)
                    continue;
                var payload = assembled.ToArray();
                assembled.SetLength(0);
                await SendWebSocketFrameAsync(clientStream, sendGate, payload, linkedCt);
            }

            // Tell the browser the agent side ended (1012 = service restart) so it reconnects.
            try
            {
                await clientStream.WriteAsync(new byte[] { 0x88, 0x02, 0x03, 0xF4 }, CancellationToken.None);
                await clientStream.FlushAsync(CancellationToken.None);
            }
            catch { }
        }
        catch { }
        finally
        {
            try { browserClosedCts.Cancel(); } catch { }
            if (agentWs.State == WebSocketState.Open)
                try { await agentWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        }
    }

    private static async Task<(string Text, bool Truncated)> ReadCappedAsync(HttpContent content)
    {
        await using var stream = await content.ReadAsStreamAsync();
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            var take = (int)Math.Min(read, MaxExtensionResponseBytes - ms.Length);
            ms.Write(buffer, 0, take);
            if (ms.Length >= MaxExtensionResponseBytes)
                return (Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length), true);
        }
        return (Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length), false);
    }

    private static bool TryParseJson(string text, out JsonElement element)
    {
        element = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(text);
            element = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static long ReadLongField(string? body, string name)
    {
        if (string.IsNullOrEmpty(body)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n))
                return Math.Max(0, n);
        }
        catch { }
        return 0;
    }

    private static (int, string, byte[]) NotFoundJson(string error)
        => (404, "application/json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ok = false, error }, CamelCase)));
}
