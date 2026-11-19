using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed class LogOperations(AgentClient client)
{
    internal Task<string> GetAsync(int limit, int skip, string? source) =>
        client.GetLogsAsync(limit, skip, source);

    internal async Task FollowAsync(string? source, int replay, Action connected,
        Action<JsonElement> entryReceived, CancellationToken cancellationToken)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(source))
            query.Add($"source={Uri.EscapeDataString(source)}");
        if (replay != 100)
            query.Add($"replay={replay}");
        var url = client.BaseUrl.Replace("http://", "ws://", StringComparison.Ordinal) + "/ws/v1/logs";
        if (query.Count > 0)
            url += "?" + string.Join("&", query);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(url), cancellationToken);
                connected();

                var buffer = new byte[65536];
                var sb = new StringBuilder();

                while (!cancellationToken.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result;
                    sb.Clear();
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (cancellationToken.IsCancellationRequested) break;

                    var message = sb.ToString();
                    if (string.IsNullOrEmpty(message)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(message);
                        var type = doc.RootElement.GetProperty("type").GetString();

                        if (type == "replay" && doc.RootElement.TryGetProperty("entries", out var entries))
                        {
                            foreach (var entry in entries.EnumerateArray())
                            {
                                entryReceived(entry);
                            }
                        }
                        else if (type == "log" && doc.RootElement.TryGetProperty("entry", out var entry))
                        {
                            entryReceived(entry);
                        }
                    }
                    catch (JsonException) { }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (WebSocketException)
            {
                if (cancellationToken.IsCancellationRequested) break;
                try { await Task.Delay(1000, cancellationToken); }
                catch { break; }
            }
        }
    }
}
