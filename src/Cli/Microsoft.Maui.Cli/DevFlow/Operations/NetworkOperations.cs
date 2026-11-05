using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed class NetworkOperations(AgentClient client)
{
    internal Task<List<NetworkRequest>> ListAsync(int limit, string? host, string? method) =>
        client.GetNetworkRequestsAsync(limit, host, method);

    internal Task<NetworkRequest?> DetailAsync(string id) =>
        client.GetNetworkRequestDetailAsync(id);

    internal Task<bool> ClearAsync() =>
        client.ClearNetworkRequestsAsync();

    internal async Task<int> MonitorAsync(int limit, string? filterHost, string? filterMethod,
        Action<int> connected, Action<int, JsonElement> entryReceived, Action<Exception> error,
        CancellationToken cancellationToken)
    {
        var counter = 0;
        var url = client.BaseUrl.Replace("http://", "ws://", StringComparison.Ordinal) + "/ws/v1/network";

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(url), cancellationToken);
                connected(counter);

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
                                if (!MatchesFilter(entry, filterHost, filterMethod)) continue;
                                counter++;
                                entryReceived(counter, entry);
                            }
                        }
                        else if (type == "request" && doc.RootElement.TryGetProperty("entry", out var entry))
                        {
                            if (!MatchesFilter(entry, filterHost, filterMethod)) continue;
                            counter++;
                            if (counter > limit) continue;
                            entryReceived(counter, entry);
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
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested) break;
                error(ex);
                try { await Task.Delay(2000, cancellationToken); }
                catch { break; }
            }
        }

        return counter;
    }

    internal static bool MatchesFilter(JsonElement entry, string? filterHost, string? filterMethod)
    {
        if (!string.IsNullOrEmpty(filterHost))
        {
            var host = entry.TryGetProperty("host", out var h) ? h.GetString() : null;
            if (host == null || !host.Contains(filterHost, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (!string.IsNullOrEmpty(filterMethod))
        {
            var method = entry.GetProperty("method").GetString();
            if (!string.Equals(method, filterMethod, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }
}
