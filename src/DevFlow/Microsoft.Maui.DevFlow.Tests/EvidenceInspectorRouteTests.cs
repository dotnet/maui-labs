using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Evidence;
using Microsoft.Maui.Cli.DevFlow.Inspector;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// End-to-end tests for the Web Inspector's evidence routes: an HTTP client hits
/// <c>/api/evidence/preview</c> and <c>/api/evidence/capture</c>, which collect from a loopback
/// fake agent through the same shared builder the CLI and MCP tools use.
///
/// The routes are read-token gated because a bundle aggregates more than the visible tree, and the
/// capture must stay opt-in for screenshots — both are asserted here.
/// </summary>
public class EvidenceInspectorRouteTests
{
    [Fact]
    public void EvidenceRoutesAreTokenGated()
    {
        Assert.True(InspectorServer.IsTokenGatedPath("/api/evidence/preview"));
        Assert.True(InspectorServer.IsTokenGatedPath("/api/evidence/capture"));
    }

    [Fact]
    public void WorkbenchRunAndArtifactRoutesAreTokenGatedButDoNotClaimDirectMutationAuthority()
    {
        Assert.True(InspectorServer.IsTokenGatedPath("/api/workbench/run/start"));
        Assert.True(InspectorServer.IsTokenGatedPath("/api/workbench/artifacts/import"));
        Assert.False(InspectorServer.IsMutation("/api/workbench/run/start"));
        Assert.False(InspectorServer.IsMutation("/api/workbench/artifacts/import"));
    }

    [Fact]
    public void EvidenceCaptureIsNotAMutationAndIsNotBlockedDuringReplay()
    {
        // Capturing evidence only READS the app, so it must not claim the writer lease and must
        // stay available while a flow replay is driving the app.
        Assert.False(InspectorServer.IsMutation("/api/evidence/capture"));
        Assert.False(InspectorServer.IsBlockedDuringReplay("/api/evidence/capture"));
    }

    [Fact]
    public void WriterHeartbeatAndLegacyReplayAreFencedDuringBrokerRuns()
    {
        Assert.True(InspectorServer.IsBlockedDuringReplay("/api/control"));
        Assert.True(InspectorServer.IsBlockedDuringReplay("/api/flows/replay"));
        Assert.True(InspectorServer.IsBlockedDuringReplay("/api/checkpoint/restore"));
    }

    [Fact]
    public async Task Preview_WithoutTheReadToken_IsForbidden()
    {
        await using var agent = new FakeEvidenceAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = new HttpClient();
            var response = await http.PostAsync($"http://127.0.0.1:{port}/api/evidence/preview", Json("{}"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task Preview_ReturnsThePlanTheDialogRenders()
    {
        await using var agent = new FakeEvidenceAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = CreateTokenClient(inspector);
            var response = await http.PostAsync($"http://127.0.0.1:{port}/api/evidence/preview",
                Json("{\"elementId\":\"e1\"}"));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(body.RootElement.GetProperty("ok").GetBoolean());
            var plan = body.RootElement.GetProperty("plan");
            Assert.Equal("inspector", plan.GetProperty("source").GetString());
            Assert.Equal(EvidenceRedaction.Version, plan.GetProperty("redactionVersion").GetInt32());
            Assert.False(plan.GetProperty("screenshot").GetProperty("requested").GetBoolean());
            Assert.True(plan.GetProperty("included").GetArrayLength() > 0);
            Assert.True(plan.GetProperty("neverIncluded").GetArrayLength() > 0);
            Assert.EndsWith(".mauitrace", plan.GetProperty("suggestedFileName").GetString()!, StringComparison.Ordinal);
            Assert.Equal("e1", plan.GetProperty("selectedElementId").GetString());
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task Preview_ReflectsRequestedWorkflowAndScreenshotOptions()
    {
        await using var agent = new FakeEvidenceAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = CreateTokenClient(inspector);
            var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/evidence/preview",
                Json("{\"includeScreenshot\":true,\"includeWorkflow\":true,\"workflow\":\"# Repro\\n1. Tap\"}"));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var plan = body.RootElement.GetProperty("plan");
            Assert.True(plan.GetProperty("screenshot").GetProperty("requested").GetBoolean());
            Assert.True(plan.GetProperty("screenshot").GetProperty("included").GetBoolean());
            Assert.Contains(
                plan.GetProperty("included").EnumerateArray(),
                entry => entry.GetProperty("name").GetString() == EvidenceFormat.WorkflowEntry);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task Capture_ReturnsAValidBundleWithoutAScreenshotByDefault()
    {
        await using var agent = new FakeEvidenceAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = CreateTokenClient(inspector);
            var response = await http.PostAsync($"http://127.0.0.1:{port}/api/evidence/capture", Json("{}"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);

            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var stream = new MemoryStream(bytes);
            var read = EvidenceBundleReader.Read(stream);

            Assert.True(read.Ok, read.Error);
            Assert.Equal("inspector", read.Manifest!.Source);
            Assert.False(read.Manifest.Screenshot.Included);
            Assert.Null(read.Screenshot);
            Assert.DoesNotContain(EvidenceFormat.ScreenshotEntry, read.Entries);
            Assert.Equal(0, agent.ScreenshotRequests);
            // Element text never leaves the app, even though the agent returned it.
            Assert.DoesNotContain("secret-label-text", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task Capture_IncludesTheScreenshotOnlyWhenTheDialogOptedIn()
    {
        await using var agent = new FakeEvidenceAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = CreateTokenClient(inspector);
            var response = await http.PostAsync($"http://127.0.0.1:{port}/api/evidence/capture",
                Json("{\"includeScreenshot\":true,\"workflow\":\"# Repro\\n1. Tap\"}"));

            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var stream = new MemoryStream(bytes);
            var read = EvidenceBundleReader.Read(stream);

            Assert.True(read.Ok, read.Error);
            Assert.True(read.Manifest!.Screenshot.Included);
            Assert.NotNull(read.Screenshot);
            Assert.Equal(1, agent.ScreenshotRequests);
            Assert.Contains("# Repro", read.Workflow!, StringComparison.Ordinal);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task Capture_OmitsTheWorkflowUnlessTheDialogAttachedOne()
    {
        await using var agent = new FakeEvidenceAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = CreateTokenClient(inspector);
            var response = await http.PostAsync($"http://127.0.0.1:{port}/api/evidence/capture", Json("{}"));

            using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
            var read = EvidenceBundleReader.Read(stream);

            Assert.True(read.Ok, read.Error);
            Assert.Null(read.Workflow);
            Assert.DoesNotContain(EvidenceFormat.WorkflowEntry, read.Entries);
            Assert.Contains(read.Manifest!.Excluded, e => e.Name == EvidenceFormat.WorkflowEntry);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task Capture_ScrubsAnAttachedWorkflow()
    {
        await using var agent = new FakeEvidenceAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = CreateTokenClient(inspector);
            var response = await http.PostAsync($"http://127.0.0.1:{port}/api/evidence/capture",
                Json("{\"workflow\":\"1. Open C:\\\\Users\\\\alice\\\\App.xaml with api_key=zzz-9999\"}"));

            using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
            var read = EvidenceBundleReader.Read(stream);

            Assert.True(read.Ok, read.Error);
            Assert.NotNull(read.Workflow);
            Assert.DoesNotContain("zzz-9999", read.Workflow!, StringComparison.Ordinal);
            Assert.DoesNotContain(@"C:\Users\alice", read.Workflow!, StringComparison.Ordinal);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    private static HttpClient CreateTokenClient(InspectorServer inspector)
    {
        var token = (string)typeof(InspectorServer)
            .GetField("_readToken", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(inspector)!;
        var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-DevFlow-Inspector-Token", token);
        return http;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static int FreePort() => TestPorts.Reserve();

    /// <summary>Loopback agent answering only the reads an evidence capture performs.</summary>
    private sealed class FakeEvidenceAgent : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private int _screenshotRequests;

        public FakeEvidenceAgent()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _loop = Loop(_cts.Token);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public int ScreenshotRequests => Volatile.Read(ref _screenshotRequests);

        private async Task Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(ct); }
                catch { break; }
                _ = Handle(client, ct);
            }
        }

        private async Task Handle(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var path = await ReadRequestPath(stream, ct);
                    if (path is null) return;

                    if (path.StartsWith("/api/v1/ui/screenshot", StringComparison.Ordinal))
                    {
                        Interlocked.Increment(ref _screenshotRequests);
                        var png = new byte[64];
                        png[0] = 0x89; png[1] = 0x50; png[2] = 0x4E; png[3] = 0x47;
                        png[4] = 0x0D; png[5] = 0x0A; png[6] = 0x1A; png[7] = 0x0A;
                        await WriteAsync(stream, "image/png", png, ct);
                        return;
                    }

                    var json = path switch
                    {
                        var p when p.StartsWith("/api/v1/agent/status", StringComparison.Ordinal) =>
                            """{"agent":{"version":"0.1.0"},"device":{"platform":"Windows"},"app":{"name":"Sample","version":"1.0"}}""",
                        var p when p.StartsWith("/api/v1/agent/capabilities", StringComparison.Ordinal) =>
                            """{"capabilities":{"ui.actions":{"version":1}}}""",
                        var p when p.StartsWith("/api/v1/ui/tree", StringComparison.Ordinal) =>
                            """[{"id":"e1","type":"Label","text":"secret-label-text","automationId":"Title"}]""",
                        var p when p.StartsWith("/api/v1/ui/diagnostics/layout", StringComparison.Ordinal) =>
                            """{"schemaVersion":"1.0","ruleSetVersion":"1.0","capturedUtc":"2026-07-29T10:00:00.000Z","platform":"Windows","scope":{"maxElements":2000,"elementsExamined":1,"truncated":false},"coverage":{"overall":"partial","rules":[{"ruleId":"layout.visible-zero-area","support":"full","confidence":"high","evaluated":1,"skipped":0,"limitations":[]}],"limitations":["Managed layout state only."],"neverCaptured":["Element Text/Value content"]},"summary":{"violations":1,"observations":0,"incomplete":0},"findings":[{"id":"layout.visible-zero-area:e1:area","ruleId":"layout.visible-zero-area","outcome":"violation","confidence":"high","message":"Label was arranged with no area.","explanation":"A realized element with no area cannot draw.","element":{"id":"e1","type":"Label","automationId":"Title"},"limitations":[]}]}""",
                        var p when p.StartsWith("/api/v1/diagnostics/problems", StringComparison.Ordinal) =>
                            """{"enabled":true,"revision":1,"count":0,"evicted":0,"problems":[]}""",
                        var p when p.StartsWith("/api/v1/logs", StringComparison.Ordinal) =>
                            """[{"t":"2026-07-29T10:00:00Z","l":"info","c":"App","m":"started"}]""",
                        var p when p.StartsWith("/api/v1/network/requests", StringComparison.Ordinal) =>
                            "[]",
                        var p when p.StartsWith("/api/v1/device/", StringComparison.Ordinal) =>
                            """{"manufacturer":"Contoso","model":"Surface","width":100,"height":200}""",
                        _ => "{}",
                    };

                    await WriteAsync(stream, "application/json", Encoding.UTF8.GetBytes(json), ct);
                }
                catch
                {
                    // The inspector treats transport failures as "agent unavailable".
                }
            }
        }

        private static async Task<string?> ReadRequestPath(NetworkStream stream, CancellationToken ct)
        {
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer, ct);
            if (read <= 0) return null;
            var text = Encoding.UTF8.GetString(buffer, 0, read);
            var firstLine = text.Split("\r\n")[0].Split(' ');
            return firstLine.Length >= 2 ? firstLine[1] : null;
        }

        private static async Task WriteAsync(NetworkStream stream, string contentType, byte[] body, CancellationToken ct)
        {
            var header = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
            await stream.WriteAsync(body, ct);
            await stream.FlushAsync(ct);
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
