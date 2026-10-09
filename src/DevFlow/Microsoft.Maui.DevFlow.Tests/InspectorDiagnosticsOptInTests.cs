using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Inspector;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Layout diagnostics run a full analysis pass on the agent for every frame, so the Inspector
/// must only ask for them while a client is actually showing the Layout panel. Before this was
/// opt-in, every routine <c>/api/state</c> poll — the inspector's heartbeat — paid that cost.
/// </summary>
public class InspectorDiagnosticsOptInTests
{
    // The fake agent listens on a dedicated loopback address. Sibling tests bind agents on
    // 127.0.0.1 with OS-assigned ports, and one of them targeting this port would otherwise show
    // up as phantom traffic in the assertions below.
    private const string AgentHost = "127.0.0.9";

    [Fact]
    public async Task State_WithoutDiagnosticsFlag_DoesNotAnalyzeLayout()
    {
        await using var agent = new DiagnosticsAgent();
        await using var inspector = await StartAsync(agent.Port);
        using var http = new HttpClient();

        using var response = await http.GetAsync($"{inspector.Url}/api/state");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            agent.AnalyzeCalls == 0,
            $"analyze called {agent.AnalyzeCalls}x; agent saw: {string.Join(" | ", agent.Calls)}");
        Assert.DoesNotContain("/api/v1/ui/diagnostics/layout", agent.Calls);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(
            JsonValueKind.Null,
            json.RootElement.GetProperty("diagnostics").ValueKind);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    public async Task State_WithDiagnosticsFlag_AnalyzesLayoutAndReturnsFindings(string flag)
    {
        await using var agent = new DiagnosticsAgent();
        await using var inspector = await StartAsync(agent.Port);
        using var http = new HttpClient();

        using var response = await http.GetAsync($"{inspector.Url}/api/state?diagnostics={flag}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, agent.AnalyzeCalls);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(
            JsonValueKind.Object,
            json.RootElement.GetProperty("diagnostics").ValueKind);
    }

    private static async Task<RunningInspector> StartAsync(int agentPort)
    {
        var port = FreePort();
        var inspector = new InspectorServer(port, AgentHost, agentPort);
        inspector.Start();
        using var http = new HttpClient();
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                using var probe = await http.GetAsync($"http://127.0.0.1:{port}/devflow.js");
                if (probe.IsSuccessStatusCode)
                    return new RunningInspector(inspector, $"http://127.0.0.1:{port}");
            }
            catch (HttpRequestException) { }
            await Task.Delay(25);
        }

        await inspector.StopAsync();
        inspector.Dispose();
        throw new InvalidOperationException("Inspector did not start.");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class RunningInspector(InspectorServer inspector, string url) : IAsyncDisposable
    {
        public string Url => url;

        public async ValueTask DisposeAsync()
        {
            await inspector.StopAsync();
            inspector.Dispose();
        }
    }

    /// <summary>Minimal agent: enough tree + screenshot for a frame, and a counted analyze route.</summary>
    private sealed class DiagnosticsAgent : IAsyncDisposable
    {
        // 1x1 transparent PNG.
        private static readonly byte[] Png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private int _analyzeCalls;

        public DiagnosticsAgent()
        {
            _listener = new TcpListener(IPAddress.Parse(AgentHost), 0);
            _listener.Start();
            _loop = AcceptAsync(_cts.Token);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public int AnalyzeCalls => Volatile.Read(ref _analyzeCalls);
        public IReadOnlyList<string> Calls
        {
            get { lock (_calls) return _calls.ToArray(); }
        }

        private readonly List<string> _calls = [];

        private async Task AcceptAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(ct); }
                catch { break; }
                _ = HandleAsync(client, ct);
            }
        }

        private async Task HandleAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var (method, path) = await ReadRequestAsync(stream, ct);
                    lock (_calls) _calls.Add(path);
                    byte[] payload;
                    var contentType = "application/json";

                    if (method == "POST" && path == "/api/v1/ui/diagnostics/layout")
                    {
                        Interlocked.Increment(ref _analyzeCalls);
                        payload = Encoding.UTF8.GetBytes(
                            """
                            {"snapshot":{"treeRevision":"rev-1","windows":[]},
                             "summary":{"violations":0,"observations":0,"incomplete":0,"passes":1,"notApplicable":0,"suppressed":0},
                             "coverage":{"overall":"partial","rules":[]},
                             "findings":[]}
                            """);
                    }
                    else if (method == "GET" && path.StartsWith("/api/v1/ui/tree", StringComparison.Ordinal))
                    {
                        payload = Encoding.UTF8.GetBytes(
                            """
                            {"revision":"rev-1","elements":[
                              {"id":"root","type":"ContentPage","isVisible":true,"isEnabled":true,
                               "bounds":{"x":0,"y":0,"width":100,"height":100},
                               "windowBounds":{"x":0,"y":0,"width":100,"height":100}}]}
                            """);
                    }
                    else if (method == "GET" && path.StartsWith("/api/v1/ui/screenshot", StringComparison.Ordinal))
                    {
                        payload = Png;
                        contentType = "image/png";
                    }
                    else if (method == "POST" && path == "/api/v1/agent/lease")
                    {
                        payload = Encoding.UTF8.GetBytes(
                            "{\"ok\":true,\"allowed\":true,\"youHold\":true,\"heldByOther\":false}");
                    }
                    else if (method == "GET" && path == "/api/v1/agent/status")
                    {
                        payload = Encoding.UTF8.GetBytes(
                            "{\"running\":true,\"app\":{\"name\":\"Fake\"},\"device\":{\"platform\":\"test\"}}");
                    }
                    else
                    {
                        payload = Encoding.UTF8.GetBytes("{}");
                    }

                    var header =
                        $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
                    await stream.WriteAsync(payload, ct);
                    await stream.FlushAsync(ct);
                }
                catch { }
            }
        }

        private static async Task<(string Method, string Path)> ReadRequestAsync(
            NetworkStream stream,
            CancellationToken ct)
        {
            var buffer = new byte[8192];
            var text = new StringBuilder();
            while (text.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal) < 0)
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read <= 0) break;
                text.Append(Encoding.UTF8.GetString(buffer, 0, read));
            }
            var parts = text.ToString().Split("\r\n", 2)[0].Split(' ');
            return (
                parts.Length > 0 ? parts[0] : "",
                parts.Length > 1 ? parts[1] : "");
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            try { await _loop; } catch { }
            _cts.Dispose();
        }
    }
}
