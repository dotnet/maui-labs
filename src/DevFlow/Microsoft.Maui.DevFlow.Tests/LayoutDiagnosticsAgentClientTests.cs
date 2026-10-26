using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class LayoutDiagnosticsAgentClientTests
{
    [Fact]
    public async Task AnalyzeLayoutAsync_SendsVersionedPostAndDeserializesFindings()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync();
            using var stream = connection.GetStream();
            var buffer = new byte[16384];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("POST /api/v1/ui/diagnostics/layout", request);
            Assert.Contains("\"schemaVersion\":\"1.0\"", request);
            Assert.Contains("\"profile\":\"strict\"", request);

            const string body = """
                {
                  "schemaVersion":"1.0",
                  "ruleSetVersion":"1.0",
                  "snapshot":{"id":"s1","capturedAt":"now","platform":"test","treeRevision":"r1","stable":true,"nodeCount":1,"windows":[]},
                  "coverage":{"overall":"partial","rules":[],"opaqueSubtrees":[],"limitations":[]},
                  "summary":{"violations":1,"observations":0,"incomplete":0,"passes":0,"suppressed":0},
                  "findings":[{
                    "id":"f1",
                    "ruleId":"layout.element-clipped",
                    "outcome":"violation",
                    "severity":"serious",
                    "confidence":"high",
                    "actionability":"fix",
                    "element":{"id":"button","type":"Button","interactive":true},
                    "relatedElements":[],
                    "message":"clipped",
                    "fixCategories":[],
                    "suppressed":false
                  }]
                }
                """;
            var bytes = Encoding.UTF8.GetBytes(body);
            var header =
                "HTTP/1.1 200 OK\r\n"
                + "Content-Type: application/json\r\n"
                + $"Content-Length: {bytes.Length}\r\n"
                + "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(header));
            await stream.WriteAsync(bytes);
        });

        using var client = new AgentClient("127.0.0.1", port);
        var result = await client.AnalyzeLayoutAsync(new LayoutInspectionRequest { Profile = "strict" });

        Assert.NotNull(result);
        var finding = Assert.Single(result!.Findings);
        Assert.Equal(LayoutDiagnosticRules.ElementClipped, finding.RuleId);
        Assert.Equal("button", finding.Element.Id);
        await server;
    }

    [Fact]
    public async Task AnalyzeLayoutAsync_HttpBusy_ThrowsRetryableDiagnosticsError()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync();
            using var stream = connection.GetStream();
            var buffer = new byte[4096];
            _ = await stream.ReadAsync(buffer);

            const string body = """{"success":false,"error":"busy"}""";
            var bytes = Encoding.UTF8.GetBytes(body);
            var header =
                "HTTP/1.1 429 Too Many Requests\r\n"
                + "Content-Type: application/json\r\n"
                + $"Content-Length: {bytes.Length}\r\n"
                + "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(header));
            await stream.WriteAsync(bytes);
        });

        using var client = new AgentClient("127.0.0.1", port);

        var exception = await Assert.ThrowsAsync<LayoutDiagnosticsException>(() =>
            client.AnalyzeLayoutAsync(new LayoutInspectionRequest()));
        Assert.Equal(429, exception.StatusCode);
        Assert.True(exception.Retryable);
        Assert.Equal("busy", exception.Message);
        await server;
    }

    [Fact]
    public async Task AnalyzeLayoutAsync_TransportFailure_ThrowsUnavailableDiagnosticsError()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();

        using var client = new AgentClient("127.0.0.1", port)
        {
            TransientFailureRetryCount = 0
        };
        var exception = await Assert.ThrowsAsync<LayoutDiagnosticsException>(() =>
            client.AnalyzeLayoutAsync(new LayoutInspectionRequest()));

        Assert.Equal(0, exception.StatusCode);
        Assert.Equal("layout-diagnostics-unavailable", exception.ErrorType);
        Assert.True(exception.Retryable);
        Assert.NotNull(exception.InnerException);
    }
}
