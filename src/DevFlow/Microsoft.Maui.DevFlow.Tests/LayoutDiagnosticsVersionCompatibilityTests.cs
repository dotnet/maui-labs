using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Diagnostics;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Mixed-version coverage for the layout diagnostics wire contract.
///
/// The agent ships inside the app under inspection; the Driver, CLI, MCP server, and Inspector ship
/// with the tooling and are updated independently. A current client is therefore routinely pointed
/// at an app built against the previous package, and an agent rejects an unknown request
/// <c>schemaVersion</c> with HTTP 400. These tests hold both halves of the resulting rule: a new
/// client declares the version every shipped agent accepts, and it still reads the newest response.
/// </summary>
public sealed class LayoutDiagnosticsVersionCompatibilityTests
{
    [Fact]
    public void NewClients_DeclareTheRequestVersionEveryShippedAgentAccepts()
    {
        Assert.Equal("2.0", LayoutDiagnosticsWire.RequestSchemaVersion);
        Assert.Equal("2.0", new LayoutInspectionRequest().SchemaVersion);
        Assert.Equal("2.0", new LayoutDiagnosticsRequest().SchemaVersion);
        // The response version is deliberately ahead of the request version, and stays ahead.
        Assert.Equal("2.1", LayoutDiagnosticsWire.ResponseSchemaVersion);
    }

    [Fact]
    public void EveryUnderstoodResponseVersionIsRecognised()
    {
        Assert.All(
            LayoutDiagnosticsWire.UnderstoodResponseSchemaVersions,
            version => Assert.True(LayoutDiagnosticsWire.IsUnderstoodResponseSchemaVersion(version)));
        Assert.False(LayoutDiagnosticsWire.IsUnderstoodResponseSchemaVersion("3.0"));
        Assert.False(LayoutDiagnosticsWire.IsUnderstoodResponseSchemaVersion(null));
    }

    [Fact]
    public void TheSharedCoordinatorRequestIsAcceptableToA20Agent()
    {
        var request = LayoutDiagnosticsCoordinator.CreateRequest(
            suppressionMode: LayoutSuppressionModes.Off);

        Assert.Contains(request.SchemaVersion, FakeLayoutAgent.VersionsA20AgentAccepts);
    }

    [Fact]
    public async Task ADefaultRequestFromANewClientIsAcceptedByA20Agent()
    {
        await using var agent = FakeLayoutAgent.Version20();
        using var client = new AgentClient("127.0.0.1", agent.Port);

        var report = await client.AnalyzeLayoutAsync(new LayoutInspectionRequest());

        Assert.NotNull(report);
        Assert.Equal("2.0", report!.SchemaVersion);
        using var sent = JsonDocument.Parse(agent.LastRequestBody);
        Assert.Equal("2.0", sent.RootElement.GetProperty("schemaVersion").GetString());
    }

    /// <summary>
    /// The regression this whole rule exists to prevent: a client that declares the newest version
    /// is refused outright by the previous agent, and every scan fails with a 400.
    /// </summary>
    [Fact]
    public async Task DeclaringTheNewestVersionWouldBeRejectedByA20Agent()
    {
        await using var agent = FakeLayoutAgent.Version20();
        using var client = new AgentClient("127.0.0.1", agent.Port);

        var error = await Assert.ThrowsAsync<LayoutDiagnosticsException>(() =>
            client.AnalyzeLayoutAsync(new LayoutInspectionRequest { SchemaVersion = "2.1" }));

        Assert.Equal(400, error.StatusCode);
        Assert.Contains("schemaVersion", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A21ResponseIsParsedByANewClient()
    {
        await using var agent = FakeLayoutAgent.Version21();
        using var client = new AgentClient("127.0.0.1", agent.Port);

        var report = await client.AnalyzeLayoutAsync(new LayoutInspectionRequest());

        Assert.NotNull(report);
        Assert.Equal("2.1", report!.SchemaVersion);
        Assert.Equal("2.1", report.RuleSetVersion);
        Assert.True(LayoutDiagnosticsWire.IsUnderstoodResponseSchemaVersion(report.SchemaVersion));
        var finding = Assert.Single(report.Findings);
        Assert.Equal("layout.visible-zero-area", finding.RuleId);
        Assert.Equal("restart-stable-key", finding.SuppressionKey);
        Assert.True(finding.Evidence?.Text?.IsTruncated);
    }

    /// <summary>
    /// A 2.0 agent may still send the text members 2.1 removed. The typed client has no member that
    /// can hold them, so they are dropped at the boundary rather than travelling any further.
    /// </summary>
    [Fact]
    public async Task A20ResponseCannotReintroduceTextOrTextLength()
    {
        await using var agent = FakeLayoutAgent.Version20();
        using var client = new AgentClient("127.0.0.1", agent.Port);

        var report = await client.AnalyzeLayoutAsync(new LayoutInspectionRequest());

        var textEvidence = Assert.Single(report!.Findings).Evidence?.Text;
        Assert.NotNull(textEvidence);
        var members = typeof(LayoutTextEvidence).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain("Text", members);
        Assert.DoesNotContain("TextLength", members);
        Assert.DoesNotContain("legacy captured text", JsonSerializer.Serialize(report), StringComparison.Ordinal);
    }

    /// <summary>
    /// Minimal agent stub that validates the declared request version exactly the way a shipped
    /// agent does, so "would a 2.0 agent accept this?" is answered by a refusal, not by a comment.
    /// </summary>
    private sealed class FakeLayoutAgent : IAsyncDisposable
    {
        internal static readonly string[] VersionsA20AgentAccepts = ["1.0", "2.0"];
        private static readonly string[] VersionsA21AgentAccepts = ["1.0", "2.0", "2.1"];

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private readonly string[] _acceptedVersions;
        private readonly string _report;
        private string _lastRequestBody = "";

        private FakeLayoutAgent(string[] acceptedVersions, string report)
        {
            _acceptedVersions = acceptedVersions;
            _report = report;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _loop = Loop(_cts.Token);
        }

        public static FakeLayoutAgent Version20() => new(VersionsA20AgentAccepts, Report20);
        public static FakeLayoutAgent Version21() => new(VersionsA21AgentAccepts, Report21);

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public string LastRequestBody => Volatile.Read(ref _lastRequestBody);

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
                    var (path, body) = await ReadRequest(stream, ct);
                    if (path is null) return;
                    if (!path.StartsWith("/api/v1/ui/diagnostics/layout", StringComparison.Ordinal))
                    {
                        await WriteAsync(stream, 404, "{\"error\":\"not found\"}", ct);
                        return;
                    }

                    Volatile.Write(ref _lastRequestBody, body ?? "");
                    var declared = "2.0";
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        using var request = JsonDocument.Parse(body!);
                        declared = request.RootElement.TryGetProperty("schemaVersion", out var value)
                            ? value.GetString() ?? ""
                            : "2.0";
                    }

                    if (!_acceptedVersions.Contains(declared, StringComparer.OrdinalIgnoreCase))
                    {
                        await WriteAsync(
                            stream,
                            400,
                            $$"""{"error":"schemaVersion must be '{{_acceptedVersions[^1]}}'.","reason":"layout-diagnostics-invalid-request"}""",
                            ct);
                        return;
                    }

                    await WriteAsync(stream, 200, _report, ct);
                }
                catch
                {
                    // Transport failures surface to the client as an unavailable agent.
                }
            }
        }

        /// <summary>A 2.0 report, still carrying the text members 2.1 removed.</summary>
        private const string Report20 = """
            {"schemaVersion":"2.0","ruleSetVersion":"2.0",
             "snapshot":{"id":"s1","capturedAt":"2026-08-27T10:00:00.000Z","platform":"Windows","treeRevision":"t1","diagnosticsRevision":"d1","stable":true,"nodeCount":1,"windows":[]},
             "capturedUtc":"2026-08-27T10:00:00.000Z","platform":"Windows",
             "scope":{"maxElements":2000,"elementsExamined":1,"truncated":false},
             "coverage":{"overall":"partial","rules":[],"limitations":["Managed MAUI layout state only."],"neverCaptured":["Element Text/Value content"]},
             "summary":{"violations":1,"observations":0,"incomplete":0},
             "findings":[{"id":"f1","suppressionKey":"legacy-key","ruleId":"layout.visible-zero-area","outcome":"violation","confidence":"high",
              "severity":"serious","actionability":"fix","message":"Label was arranged with no area.","explanation":"A realized element with no area cannot draw.",
              "element":{"id":"e1","type":"Label"},
              "evidence":{"text":{"kind":"label","isTruncated":true,"textLength":20,"text":"legacy captured text","measurementSource":"native"}},
              "relatedElements":[],"fixCategories":[],"limitations":[],"suppressed":false}]}
            """;

        private const string Report21 = """
            {"schemaVersion":"2.1","ruleSetVersion":"2.1",
             "snapshot":{"id":"s1","capturedAt":"2026-08-27T10:00:00.000Z","platform":"Windows","treeRevision":"t1","diagnosticsRevision":"d1","stable":true,"nodeCount":1,"windows":[]},
             "capturedUtc":"2026-08-27T10:00:00.000Z","platform":"Windows",
             "scope":{"maxElements":2000,"elementsExamined":1,"truncated":false},
             "coverage":{"overall":"partial","rules":[],"limitations":["Managed MAUI layout state only."],"neverCaptured":["Element Text/Value content"]},
             "summary":{"violations":1,"observations":0,"incomplete":0},
             "findings":[{"id":"f1","suppressionKey":"restart-stable-key","ruleId":"layout.visible-zero-area","outcome":"violation","confidence":"high",
              "severity":"serious","actionability":"fix","message":"Label was arranged with no area.","explanation":"A realized element with no area cannot draw.",
              "element":{"id":"e1","type":"Label"},
              "evidence":{"text":{"kind":"label","isTruncated":true,"measurementSource":"native"}},
              "relatedElements":[],"fixCategories":[],"limitations":[],"suppressed":false}]}
            """;

        private static async Task<(string? Path, string? Body)> ReadRequest(
            NetworkStream stream,
            CancellationToken ct)
        {
            var buffer = new byte[8192];
            using var collected = new MemoryStream();
            var headerEnd = -1;
            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read <= 0) return (null, null);
                collected.Write(buffer, 0, read);
                headerEnd = Encoding.UTF8.GetString(collected.GetBuffer(), 0, (int)collected.Length)
                    .IndexOf("\r\n\r\n", StringComparison.Ordinal);
            }

            var headerText = Encoding.UTF8.GetString(collected.GetBuffer(), 0, headerEnd);
            var contentLength = headerText.Split("\r\n")
                .FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                ?.Split(':', 2)[1]
                .Trim();
            var bodyLength = int.TryParse(contentLength, out var parsed) ? parsed : 0;
            var bodyStart = headerEnd + 4;
            while (collected.Length - bodyStart < bodyLength)
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read <= 0) break;
                collected.Write(buffer, 0, read);
            }

            var text = Encoding.UTF8.GetString(collected.GetBuffer(), 0, (int)collected.Length);
            var firstLine = headerText.Split("\r\n")[0].Split(' ');
            var body = bodyLength > 0 && text.Length >= bodyStart
                ? text.Substring(bodyStart, Math.Min(bodyLength, text.Length - bodyStart))
                : null;
            return (firstLine.Length >= 2 ? firstLine[1] : null, body);
        }

        private static async Task WriteAsync(
            NetworkStream stream,
            int statusCode,
            string body,
            CancellationToken ct)
        {
            var payload = Encoding.UTF8.GetBytes(body);
            var reason = statusCode switch { 200 => "OK", 400 => "Bad Request", _ => "Not Found" };
            var header =
                $"HTTP/1.1 {statusCode} {reason}\r\nContent-Type: application/json\r\n" +
                $"Content-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
            await stream.WriteAsync(payload, ct);
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
