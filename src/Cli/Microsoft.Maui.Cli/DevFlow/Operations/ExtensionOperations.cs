using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed class ExtensionOperations(AgentClient client)
{
    internal Task<Dictionary<string, ExtensionDescriptor>> ListAsync() =>
        client.GetExtensionsAsync();

    internal async Task<ExtensionDescriptor?> DescribeAsync(string ns)
    {
        var extensions = await client.GetExtensionsAsync();

        return extensions.GetValueOrDefault(ns);
    }

    internal async Task<(string? Result, string? Error)> CallAsync(string ns, string toolName, string? parameters)
    {
        var extension = await DescribeAsync(ns);
        if (extension is null)
            return (null, $"Extension '{ns}' was not found.");

        var tool = extension.Tools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.Ordinal));
        if (tool is null)
            return (null, $"Tool '{toolName}' was not found in extension '{ns}'.");

        JsonElement? parameterJson = string.IsNullOrWhiteSpace(parameters) ? null : CliJson.ParseElement(parameters);

        return (await client.CallExtensionToolAsync(tool.Method, tool.Path, parameterJson), null);
    }

    internal async Task<(ExtensionStreamInfo? Stream, string? Error)> FindStreamAsync(string ns, string streamName)
    {
        var extension = await DescribeAsync(ns);
        if (extension is null)
            return (null, $"Extension '{ns}' was not found.");

        var stream = extension.Streams.FirstOrDefault(s => string.Equals(s.Name, streamName, StringComparison.OrdinalIgnoreCase));
        if (stream is null)
        {
            var available = extension.Streams.Count == 0 ? "none" : string.Join(", ", extension.Streams.Select(s => s.Name));
            return (null, $"Stream '{streamName}' was not found in extension '{ns}'. Available streams: {available}.");
        }

        return (stream, null);
    }

    internal Task<ExtensionStreamPage> ReadEventsAsync(string ns, string streamName, long since, int limit) =>
        client.GetExtensionEventsAsync(ns, streamName, since, limit);

    /// <summary>
    /// Follows an extension stream over its WebSocket until <paramref name="cancellationToken"/> is
    /// cancelled or the agent closes the connection.
    /// </summary>
    internal async Task WatchAsync(string ns, string streamName, long? since, Action<ExtensionStreamEvent> onEvent, CancellationToken cancellationToken)
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(client.GetExtensionStreamWebSocketUrl(ns, streamName, since)), cancellationToken);

        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (!cancellationToken.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await ws.ReceiveAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                break;

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
                continue;

            var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);
            var evt = JsonSerializer.Deserialize(text, DevFlowCliJsonContext.Default.ExtensionStreamEvent);
            if (evt is not null)
                onEvent(evt);
        }

        if (ws.State == WebSocketState.Open)
        {
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
            catch (WebSocketException) { }
        }
    }
}
