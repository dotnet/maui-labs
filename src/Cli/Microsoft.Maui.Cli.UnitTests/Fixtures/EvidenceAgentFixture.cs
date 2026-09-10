using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Microsoft.Maui.Cli.UnitTests.Fixtures;

internal sealed class EvidenceAgentFixture : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentBag<Task> _connections = [];
    private readonly ConcurrentQueue<string> _requests = [];
    private readonly Task _loop;
    private readonly bool _networkUnavailable;
    private int _screenshotRequests;

    internal EvidenceAgentFixture(bool networkUnavailable = false)
    {
        _networkUnavailable = networkUnavailable;
        _listener.Start();
        _loop = ListenAsync();
    }

    internal int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    internal int ScreenshotRequests => Volatile.Read(ref _screenshotRequests);
    internal string[] Requests => _requests.ToArray();

    private async Task ListenAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _connections.Add(HandleAsync(client, _cts.Token));
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        catch (SocketException) when (_cts.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var target = await ReadRequestAsync(stream, ct);
                var path = target.Split('?')[0];
                _requests.Enqueue(path);
                if (path == "/api/v1/network/requests" && _networkUnavailable)
                {
                    await WriteAsync(stream, 503, "application/json",
                        Encoding.UTF8.GetBytes("""{"error":"Network capture is unavailable."}"""), ct);
                    return;
                }
                if (path == "/api/v1/ui/screenshot")
                {
                    Interlocked.Increment(ref _screenshotRequests);
                    await WriteAsync(stream, 200, "image/png", Convert.FromBase64String(
                        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aA1cAAAAASUVORK5CYII="), ct);
                    return;
                }

                var json = path switch
                {
                    "/api/v1/agent/status" => """
                        {"running":true,"agent":{"version":"0.1.0","framework":".NET MAUI","frameworkVersion":"10.0"},
                         "device":{"platform":"Windows","deviceType":"Physical","idiom":"Desktop","windowWidth":430,"windowHeight":760,"displayDensity":1.5,"windowCount":1},
                         "app":{"name":"Evidence Sample","version":"1.2","build":"42","packageId":"com.example.evidence"},"route":"//home"}
                        """,
                    "/api/v1/agent/capabilities" => """{"capabilities":{"ui.actions":{"version":1},"diagnostics.layout":{"version":1}}}""",
                    "/api/v1/ui/tree" => """
                        [{"id":"page","type":"ContentPage","isVisible":true,"isEnabled":true,
                          "bounds":{"x":0,"y":0,"width":430,"height":760},"windowBounds":{"x":0,"y":0,"width":430,"height":760},
                          "children":[{"id":"e1","type":"Label","text":"secret-label-text","automationId":"OrderConfirmation",
                            "isVisible":true,"isEnabled":true,"bounds":{"x":20,"y":20,"width":220,"height":40},
                            "windowBounds":{"x":20,"y":20,"width":220,"height":40}}]}]
                        """,
                    "/api/v1/ui/diagnostics/layout" => """
                        {"schemaVersion":"1.0","ruleSetVersion":"1.0",
                         "snapshot":{"id":"snapshot-1","capturedAt":"2026-09-09T10:00:00Z","platform":"Windows","stable":true,"nodeCount":2},
                         "coverage":{"overall":"partial","rules":[],"limitations":["Managed layout state only."]},
                         "summary":{"violations":0,"incomplete":1},"findings":[]}
                        """,
                    "/api/v1/logs" => """[{"t":"2026-09-09T10:00:00Z","l":"info","c":"App","m":"started"}]""",
                    "/api/v1/network/requests" => "[]",
                    "/api/v1/device/info" => """{"manufacturer":"Contoso","model":"Test Tablet","platform":"Windows","osVersion":"11","deviceType":"Physical","name":"private device name"}""",
                    "/api/v1/device/display" => """{"width":1920,"height":1080,"density":2,"orientation":"Landscape","rotation":"Rotation0"}""",
                    "/api/v1/device/app" => """{"effectiveTheme":"dark","requestedTheme":"dark","userAppTheme":"system"}""",
                    "/api/v1/device/connectivity" => """{"networkAccess":"Internet","connectionProfiles":["WiFi"],"ssid":"private network name"}""",
                    _ => null,
                };
                await WriteAsync(stream, json is null ? 404 : 200, "application/json",
                    Encoding.UTF8.GetBytes(json ?? """{"success":false,"error":"Unsupported fixture endpoint"}"""), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (IOException) { /* A browser may abort an in-flight state poll. */ }
        }
    }

    private static async Task<string> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        using var received = new MemoryStream();
        var buffer = new byte[4096];
        string header;
        int headerEnd;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) throw new IOException("The request ended before its headers.");
            received.Write(buffer, 0, read);
            header = Encoding.ASCII.GetString(received.GetBuffer(), 0, checked((int)received.Length));
            headerEnd = header.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd >= 0) break;
            if (received.Length > 16_384) throw new IOException("Fixture request headers exceeded their limit.");
        }
        var lines = header[..headerEnd].Split("\r\n");
        var contentLength = lines.FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        var length = contentLength is null ? 0 :
            int.Parse(contentLength[(contentLength.IndexOf(':') + 1)..], CultureInfo.InvariantCulture);
        var remaining = length - (received.Length - headerEnd - 4);
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)), ct);
            if (read == 0) throw new IOException("The request body ended early.");
            remaining -= read;
        }
        return lines[0].Split(' ')[1];
    }

    private static async Task WriteAsync(NetworkStream stream, int status, string type, byte[] body, CancellationToken ct)
    {
        var reason = status switch { 200 => "OK", 503 => "Service Unavailable", _ => "Not Found" };
        var header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
        await stream.WriteAsync(body, ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        await _loop;
        await Task.WhenAll(_connections);
        _cts.Dispose();
    }
}
