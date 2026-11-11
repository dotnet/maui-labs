using System.Net.WebSockets;
using System.Text;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed class SensorOperations(AgentClient client)
{
    internal Task<bool> StartAsync(string sensor, string speed, int throttleMs) =>
        client.StartSensorAsync(sensor, speed, throttleMs);

    internal async Task StreamAsync(string sensor, Action<string> reading, CancellationToken cancellationToken)
    {
        using var ws = new ClientWebSocket();
        var uri = new Uri(client.BaseUrl.Replace("http://", "ws://", StringComparison.Ordinal)
            + $"/ws/v1/sensors?sensor={Uri.EscapeDataString(sensor)}");
        await ws.ConnectAsync(uri, cancellationToken);
        var buffer = new byte[4096];

        while (!cancellationToken.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            reading(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }

    }
}
