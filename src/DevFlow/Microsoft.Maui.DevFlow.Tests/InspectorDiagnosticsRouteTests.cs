using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Web Inspector coverage for the on-demand diagnostics routes.
///
/// The layout scan is a read (available during a replay); the performance session is not (starting
/// a profiler mid-replay would perturb the run being replayed). Both expose more than the visible
/// tree, so both must be read-token gated.
/// </summary>
public class InspectorDiagnosticsRouteTests
{
    [Fact]
    public void DiagnosticsRoutesAreTokenGated()
    {
        Assert.True(InspectorServer.IsTokenGatedPath("/api/diagnostics/layout"));
        Assert.True(InspectorServer.IsTokenGatedPath("/api/diagnostics/suppress"));
        Assert.True(InspectorServer.IsTokenGatedPath("/api/diagnostics/suppression/apply"));
        Assert.True(InspectorServer.IsTokenGatedPath("/api/diagnostics/unsuppress"));
        Assert.True(InspectorServer.IsTokenGatedPath("/api/performance/start"));
        Assert.True(InspectorServer.IsTokenGatedPath("/api/performance/snapshot"));
        Assert.True(InspectorServer.IsTokenGatedPath("/api/performance/stop"));
    }

    [Fact]
    public void LayoutScanIsAReadThatStaysAvailableDuringReplay()
    {
        Assert.False(InspectorServer.IsMutation("/api/diagnostics/layout"));
        Assert.False(InspectorServer.IsBlockedDuringReplay("/api/diagnostics/layout"));
        Assert.False(InspectorServer.IsMutation("/api/diagnostics/suppress"));
        Assert.False(InspectorServer.IsBlockedDuringReplay("/api/diagnostics/suppress"));
    }

    [Fact]
    public void PerformanceSessionControlIsBlockedDuringReplay()
    {
        Assert.True(InspectorServer.IsBlockedDuringReplay("/api/performance/start"));
        Assert.True(InspectorServer.IsBlockedDuringReplay("/api/performance/stop"));
        // Reading the current numbers does not change agent state.
        Assert.False(InspectorServer.IsBlockedDuringReplay("/api/performance/snapshot"));
        // Neither claims the UI writer lease: they never touch the app's UI.
        Assert.False(InspectorServer.IsMutation("/api/performance/start"));
        Assert.False(InspectorServer.IsMutation("/api/performance/stop"));
    }

    [Fact]
    public async Task LayoutRoute_WithoutTheReadToken_IsForbidden()
    {
        await using var agent = new FakeDiagnosticsAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = new HttpClient();
            var response = await http.PostAsync($"http://127.0.0.1:{port}/api/diagnostics/layout", Json("{}"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task LayoutRoute_ReturnsTheReportTheTabRenders()
    {
        await using var agent = new FakeDiagnosticsAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = CreateTokenClient(inspector);
            var response = await http.PostAsync($"http://127.0.0.1:{port}/api/diagnostics/layout",
                Json("""
                    {
                      "schemaVersion": "2.0",
                      "profile": "strict",
                      "rules": ["layout.visible-zero-area"],
                      "scope": { "rootElementId": "e1" },
                      "maxElements": 50
                    }
                    """));
            var rawBody = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, rawBody);
            using var body = JsonDocument.Parse(rawBody);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(body.RootElement.GetProperty("ok").GetBoolean());
            var report = body.RootElement.GetProperty("report");
            Assert.Equal(1, report.GetProperty("summary").GetProperty("violations").GetInt32());
            Assert.NotEmpty(report.GetProperty("coverage").GetProperty("limitations").EnumerateArray());
            Assert.Equal("/api/v1/ui/diagnostics/layout", agent.LastLayoutQuery);
            using var forwarded = JsonDocument.Parse(agent.LastLayoutBody);
            Assert.Equal(50, forwarded.RootElement.GetProperty("maxElements").GetInt32());
            Assert.Equal("strict", forwarded.RootElement.GetProperty("profile").GetString());
            Assert.Equal(
                "e1",
                forwarded.RootElement.GetProperty("scope").GetProperty("rootElementId").GetString());
            Assert.Equal(
                "layout.visible-zero-area",
                forwarded.RootElement.GetProperty("rules")[0].GetString());
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task SuppressRoute_RequiresTrustedWorkspaceApproval()
    {
        var projectRoot = Path.Combine(
            Path.GetTempPath(),
            $"devflow-layout-policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var configPath = Path.Combine(projectRoot, ".mauidevflow");
        await File.WriteAllTextAsync(
            configPath,
            """{"port":9223,"custom":{"keep":true}}""");

        await using var agent = new FakeDiagnosticsAgent();
        var port = FreePort();
        var inspector = new InspectorServer(
            port,
            "127.0.0.1",
            agent.Port,
            embedToken: null,
            agentId: "agent-1",
            appName: null,
            platform: null,
            project: projectRoot,
            sessionId: null,
            agentInstanceId: "instance-1",
            trustedHostApprovalVerifier: token => token == "host-token");
        inspector.Start();
        try
        {
            using var http = CreateTokenClient(inspector);
            var scan = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/diagnostics/layout",
                Json("""{"schemaVersion":"2.0","rules":["layout.visible-zero-area"]}"""));
            Assert.True(scan.IsSuccessStatusCode, await scan.Content.ReadAsStringAsync());

            var suppress = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/diagnostics/suppress",
                Json("""{"findingId":"layout.visible-zero-area:e1:area","reason":"Intentional test fixture"}"""));
            var responseBody = await suppress.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Conflict, suppress.StatusCode);
            using var proposal = JsonDocument.Parse(responseBody);
            Assert.Equal(
                "WorkspaceApprovalRequired",
                proposal.RootElement.GetProperty("type").GetString());
            Assert.Equal(
                "layout.visible-zero-area:e1:area",
                proposal.RootElement.GetProperty("proposal").GetProperty("suppressionKey").GetString());
            var proposalId = proposal.RootElement.GetProperty("proposal").GetProperty("proposalId").GetString();
            var expectedPolicyDigest = proposal.RootElement.GetProperty("proposal").GetProperty("expectedPolicyDigest").GetString();
            var proposalDigest = proposal.RootElement.GetProperty("proposal").GetProperty("proposalDigest").GetString();
            Assert.False(string.IsNullOrWhiteSpace(proposalId));

            using var host = new HttpClient();
            host.DefaultRequestHeaders.Add("X-DevFlow-Host-Approval-Token", "host-token");
            var issue = await host.PostAsync(
                $"http://127.0.0.1:{port}/api/workbench/approval-confirmations/issue",
                Json($$"""{"action":"layout-policy-mutation","subjectId":"{{proposalId}}","contentDigest":"{{expectedPolicyDigest}}","patchDigest":"{{proposalDigest}}"}"""));
            var issueBody = await issue.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Created, issue.StatusCode);
            using var confirmation = JsonDocument.Parse(issueBody);
            var capability = confirmation.RootElement.GetProperty("confirmationCapability").GetString();
            var review = confirmation.RootElement.GetProperty("layoutPolicyReview");
            Assert.Equal("Intentional test fixture", review.GetProperty("reason").GetString());
            Assert.Equal("layout.visible-zero-area:e1:area", review.GetProperty("suppressionKey").GetString());
            Assert.Equal(configPath, review.GetProperty("policyPath").GetString());
            Assert.False(string.IsNullOrWhiteSpace(capability));

            var apply = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/diagnostics/suppression/apply",
                Json($$"""{"proposalId":"{{proposalId}}","confirmationCapability":"{{capability}}"}"""));
            Assert.True(apply.IsSuccessStatusCode, await apply.Content.ReadAsStringAsync());
            var replay = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/diagnostics/suppression/apply",
                Json($$"""{"proposalId":"{{proposalId}}","confirmationCapability":"{{capability}}"}"""));
            Assert.False(replay.IsSuccessStatusCode);

            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
            Assert.Equal(9223, config.RootElement.GetProperty("port").GetInt32());
            Assert.True(config.RootElement.GetProperty("custom").GetProperty("keep").GetBoolean());
            var saved = Assert.Single(
                config.RootElement.GetProperty("layoutDiagnostics").GetProperty("suppressions").EnumerateArray());
            Assert.Equal("layout.visible-zero-area:e1:area", saved.GetProperty("fingerprint").GetString());
        }
        finally
        {
            await inspector.StopAsync();
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PerformanceSnapshotRoute_ReturnsATriageSummaryWithTaintMetadata()
    {
        await using var agent = new FakeDiagnosticsAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = CreateTokenClient(inspector);
            var response = await http.PostAsync($"http://127.0.0.1:{port}/api/performance/snapshot", Json("{}"));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var summary = body.RootElement.GetProperty("summary");
            Assert.Equal("debug", summary.GetProperty("capability").GetProperty("mode").GetString());
            Assert.False(summary.GetProperty("capability").GetProperty("lowPerturbation").GetBoolean());
            // Estimated frame timings must never surface as a frame rate.
            Assert.False(summary.GetProperty("frames").GetProperty("supported").GetBoolean());
            Assert.True(summary.GetProperty("loss").GetProperty("anyLoss").GetBoolean());
            Assert.NotEmpty(summary.GetProperty("warnings").EnumerateArray());
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task StopAsync_StopsOnlyTheInspectorOwnedProfilerSession()
    {
        await using var agent = new FakeDiagnosticsAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        var client = (AgentClient)typeof(InspectorServer)
            .GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(inspector)!;
        client.AutoAcquireMutationLease = false;
        inspector.SetPerformanceOwnership(
            "s1", "qa-stop-token", "browser-lease", "web-inspector", "Browser");
        inspector.Start();

        await inspector.StopAsync();
        inspector.Dispose();

        Assert.Contains("/api/v1/profiler/sessions/s1", agent.LastProfilerStopPath, StringComparison.Ordinal);
        Assert.Equal("browser-lease", agent.LastProfilerStopLease);
    }

    [Fact]
    public async Task TransientStopFailure_PreservesInspectorProfilerOwnership()
    {
        await using var agent = new FakeDiagnosticsAgent { FailProfilerStop = true };
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        var client = (AgentClient)typeof(InspectorServer)
            .GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(inspector)!;
        client.AutoAcquireMutationLease = false;
        inspector.SetPerformanceOwnership(
            "s1", "qa-stop-token", "browser-lease", "web-inspector", "Browser");
        inspector.Start();
        try
        {
            using var http = CreateTokenClient(inspector);
            var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/performance/stop",
                Json("{}"));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.False(body.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("s1", inspector.OwnedPerformanceSessionId);
        }
        finally
        {
            agent.FailProfilerStop = false;
            await inspector.StopAsync();
            inspector.Dispose();
        }
    }

    [Fact]
    public async Task TransientSnapshotFailure_PreservesInspectorProfilerOwnership()
    {
        await using var agent = new FakeDiagnosticsAgent { FailProfilerStatus = true };
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        var client = (AgentClient)typeof(InspectorServer)
            .GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(inspector)!;
        client.AutoAcquireMutationLease = false;
        inspector.SetPerformanceOwnership(
            "s1", "qa-stop-token", "browser-lease", "web-inspector", "Browser");
        inspector.Start();
        try
        {
            using var http = CreateTokenClient(inspector);
            var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/performance/snapshot",
                Json("{}"));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.False(body.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("s1", inspector.OwnedPerformanceSessionId);
        }
        finally
        {
            agent.FailProfilerStatus = false;
            await inspector.StopAsync();
            inspector.Dispose();
        }
    }

    [Fact]
    public async Task ReplacedSnapshotSession_ClearsInspectorProfilerCapability()
    {
        await using var agent = new FakeDiagnosticsAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        var client = (AgentClient)typeof(InspectorServer)
            .GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(inspector)!;
        client.AutoAcquireMutationLease = false;
        inspector.SetPerformanceOwnership(
            "s1", "qa-stop-token", "browser-lease", "web-inspector", "Browser");
        inspector.Start();
        try
        {
            using var http = CreateTokenClient(inspector);
            var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/performance/snapshot",
                Json("{}"));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.False(body.RootElement.GetProperty("ok").GetBoolean());
            Assert.Null(inspector.OwnedPerformanceSessionId);
            Assert.Null(inspector.OwnedPerformanceStopToken);
        }
        finally
        {
            await inspector.StopAsync();
            inspector.Dispose();
        }
    }

    [Fact]
    public async Task LayoutRoute_PinsTextPrivacyRegardlessOfWhatThePageAsksFor()
    {
        await using var agent = new FakeDiagnosticsAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = CreateTokenClient(inspector);
            // The body arrives from a browser page, so a page asking for text length or content
            // must not become a request the agent is asked to honour.
            var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/diagnostics/layout",
                Json("""{"schemaVersion":"2.0","privacy":{"text":"length"}}"""));

            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            using var forwarded = JsonDocument.Parse(agent.LastLayoutBody);
            Assert.Equal(
                "none",
                forwarded.RootElement.GetProperty("privacy").GetProperty("text").GetString());
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public void StalePerformanceCompletion_DoesNotClearNewerOwnership()
    {
        using var inspector = new InspectorServer(FreePort(), "127.0.0.1", FreePort());
        inspector.SetPerformanceOwnership(
            "old", "old-token", "old-lease", "web-inspector", "Old");
        inspector.SetPerformanceOwnership(
            "new", "new-token", "new-lease", "web-inspector", "New");

        Assert.False(inspector.TryClearPerformanceOwnership("old", "old-token"));
        Assert.Equal("new", inspector.OwnedPerformanceSessionId);
        Assert.Equal("new-token", inspector.OwnedPerformanceStopToken);
        Assert.True(inspector.TryClearPerformanceOwnership("new", "new-token"));
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

    /// <summary>Loopback agent answering the layout and profiler reads the diagnostics tabs make.</summary>
    private sealed class FakeDiagnosticsAgent : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private string _lastLayoutQuery = "";
        private string _lastLayoutBody = "";
        private string _lastProfilerStopPath = "";
        private string _lastProfilerStopLease = "";

        public FakeDiagnosticsAgent()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _loop = Loop(_cts.Token);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public bool FailProfilerStop { get; set; }
        public bool FailProfilerStatus { get; set; }
        public string LastLayoutQuery => Volatile.Read(ref _lastLayoutQuery);
        public string LastLayoutBody => Volatile.Read(ref _lastLayoutBody);
        public string LastProfilerStopPath => Volatile.Read(ref _lastProfilerStopPath);
        public string LastProfilerStopLease => Volatile.Read(ref _lastProfilerStopLease);

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
                    var request = await ReadRequest(stream, ct);
                    if (request.Path is null) return;
                    var path = request.Path;

                    if (path.StartsWith("/api/v1/ui/diagnostics/layout", StringComparison.Ordinal))
                    {
                        Volatile.Write(ref _lastLayoutQuery, path);
                        Volatile.Write(ref _lastLayoutBody, request.Body ?? "");
                    }
                    if (path.StartsWith("/api/v1/profiler/sessions/", StringComparison.Ordinal) &&
                        !path.Contains("/samples", StringComparison.Ordinal))
                    {
                        Volatile.Write(ref _lastProfilerStopPath, path);
                        Volatile.Write(ref _lastProfilerStopLease, request.LeaseId ?? "");
                    }

                    var json = path switch
                    {
                        var p when p.StartsWith("/api/v1/ui/diagnostics/layout", StringComparison.Ordinal) => LayoutReport,
                        var p when p.StartsWith("/api/v1/profiler/capabilities", StringComparison.Ordinal) => ProfilerCapabilities,
                        var p when p.Contains("/samples", StringComparison.Ordinal) => ProfilerBatch,
                        var p when p.StartsWith("/api/v1/profiler/hotspots", StringComparison.Ordinal) => "[]",
                        var p when p.StartsWith("/api/v1/profiler/sessions/", StringComparison.Ordinal) =>
                            FailProfilerStop
                                ? "{}"
                                : """{"session":{"sessionId":"s1","startedAtUtc":"2026-07-29T10:00:00Z","sampleIntervalMs":250,"isActive":false}}""",
                        var p when p.StartsWith("/api/v1/agent/status", StringComparison.Ordinal) =>
                            FailProfilerStatus ? "not-json" : AgentStatus,
                        _ => "{}",
                    };

                    await WriteAsync(stream, Encoding.UTF8.GetBytes(json), ct);
                }
                catch
                {
                    // Transport failures surface as "agent unavailable" in the inspector.
                }
            }
        }

        private const string LayoutReport = """
            {"schemaVersion":"2.0","ruleSetVersion":"2.1",
             "snapshot":{"id":"snapshot-1","capturedAt":"2026-07-29T10:00:00.000Z","platform":"Windows","treeRevision":"tree-1","diagnosticsRevision":"diagnostics-1","stable":true,"nodeCount":1,"windows":[]},
             "capturedUtc":"2026-07-29T10:00:00.000Z","platform":"Windows",
             "scope":{"maxElements":50,"elementsExamined":1,"truncated":false},
             "coverage":{"overall":"partial","rules":[{"ruleId":"layout.visible-zero-area","support":"full","confidence":"high","evaluated":1,"skipped":0,"limitations":[]}],
             "limitations":["Managed MAUI layout state only."],"neverCaptured":["Element Text/Value content"]},
             "summary":{"violations":1,"observations":0,"incomplete":0},
             "findings":[{"id":"layout.visible-zero-area:e1:area","suppressionKey":"layout.visible-zero-area:e1:area","ruleId":"layout.visible-zero-area","outcome":"violation","confidence":"high",
             "message":"Label was arranged with no area.","explanation":"A realized element with no area cannot draw.",
             "element":{"id":"e1","type":"Label"},"limitations":[]}]}
            """;

        private const string ProfilerCapabilities = """
            {"available":true,"featureEnabled":true,"platform":"Windows","managedMemorySupported":true,
             "gcSupported":true,"cpuPercentSupported":true,"threadCountSupported":true,
             "frameTimingsEstimated":true,"nativeFrameTimingsSupported":false}
            """;

        private const string ProfilerBatch = """
            {"sessionId":"s1","isActive":true,
             "samples":[{"tsUtc":"2026-07-29T10:00:00Z","managedBytes":100,"gc0":0,"gc1":0,"gc2":0,"frameSource":"estimated","frameQuality":"low"}],
             "markers":[],"spans":[],
             "sampleMetadata":{"oldestCursor":5,"latestCursor":6,"lostCount":5,"availableCount":1},
             "markerMetadata":{"lostCount":0},"spanMetadata":{"lostCount":0}}
            """;

        private const string AgentStatus = """
            {"agent":{"version":"0.1.0","mode":"debug","readOnly":false},"device":{"platform":"Windows"},"app":{"name":"Sample"}}
            """;

        private static async Task<(string? Path, string? LeaseId, string? Body)> ReadRequest(
            NetworkStream stream,
            CancellationToken ct)
        {
            var buffer = new byte[8192];
            using var collected = new MemoryStream();
            var headerEnd = -1;
            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read <= 0) return (null, null, null);
                collected.Write(buffer, 0, read);
                headerEnd = Encoding.UTF8.GetString(collected.GetBuffer(), 0, (int)collected.Length)
                    .IndexOf("\r\n\r\n", StringComparison.Ordinal);
            }

            var headerText = Encoding.UTF8.GetString(collected.GetBuffer(), 0, headerEnd);
            var contentLength = headerText.Split("\r\n")
                .FirstOrDefault(line => line.StartsWith(
                    "Content-Length:",
                    StringComparison.OrdinalIgnoreCase))
                ?.Split(':', 2)[1]
                .Trim();
            var bodyLength = int.TryParse(contentLength, out var parsedLength) ? parsedLength : 0;
            var bodyStart = headerEnd + 4;
            while (collected.Length - bodyStart < bodyLength)
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read <= 0) break;
                collected.Write(buffer, 0, read);
            }

            var text = Encoding.UTF8.GetString(collected.GetBuffer(), 0, (int)collected.Length);
            var firstLine = headerText.Split("\r\n")[0].Split(' ');
            var lease = headerText.Split("\r\n")
                .FirstOrDefault(line => line.StartsWith(
                    "X-DevFlow-Lease:",
                    StringComparison.OrdinalIgnoreCase))
                ?.Split(':', 2)[1]
                .Trim();
            var body = bodyLength > 0 && text.Length >= bodyStart
                ? text.Substring(bodyStart, Math.Min(bodyLength, text.Length - bodyStart))
                : null;
            return (firstLine.Length >= 2 ? firstLine[1] : null, lease, body);
        }

        private static async Task WriteAsync(NetworkStream stream, byte[] body, CancellationToken ct)
        {
            var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
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
