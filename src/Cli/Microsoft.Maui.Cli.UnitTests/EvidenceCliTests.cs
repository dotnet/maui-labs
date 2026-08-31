using System.Text.Json;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Exercises <c>maui devflow evidence</c> end to end against the mock agent: the preview plan, the
/// atomic capture (including the explicit-overwrite rule), and the regenerated offline report.
/// </summary>
[Collection("CLI")]
public class EvidenceCliTests : IDisposable
{
    private readonly string _root = Path.Combine(
        AppContext.BaseDirectory, "evidence-cli-tests", Guid.NewGuid().ToString("N"));

    public EvidenceCliTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
        GC.SuppressFinalize(this);
    }

    private static async Task<(MockAgentServer server, CliTestHarness cli)> CreateFixturesAsync()
    {
        var server = new MockAgentServer();
        await server.StartAsync();
        return (server, new CliTestHarness(server.Port));
    }

    [Fact]
    public async Task EvidencePreview_ReportsInclusionsExclusionsAndLimits()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "evidence", "preview", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal("cli", json.GetProperty("source").GetString());
        Assert.True(json.GetProperty("redactionVersion").GetInt32() >= 1);
        Assert.False(json.GetProperty("screenshot").GetProperty("requested").GetBoolean());
        Assert.True(json.GetProperty("neverIncluded").GetArrayLength() > 0);

        var included = json.GetProperty("included").EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();
        Assert.Contains("manifest.json", included);
        Assert.Contains("tree.json", included);
        Assert.Contains("environment.json", included);
        Assert.DoesNotContain("screenshot.png", included);

        var excluded = json.GetProperty("excluded").EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();
        Assert.Contains("screenshot.png", excluded);
        Assert.EndsWith(".mauitrace", json.GetProperty("outputPath").GetString()!, StringComparison.Ordinal);
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/tree");
        // A preview must never take a screenshot.
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/ui/screenshot");
    }

    [Fact]
    public async Task EvidenceInspectTrust_ReadsOnlyRedactedBoundedProjection()
    {
        const string secret = "inspect-trust-secret";
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;
        var input = Path.Combine(_root, "flow-run.json");
        await File.WriteAllTextAsync(input, $$"""
            {
              "schema": 1,
              "runId": "embedded-local-looking-run",
              "flowDigest": "flow",
              "failure": {
                "failureId": "embedded-failure",
                "code": "locator-not-found",
                "class": "locator-not-found",
                "stepId": "tap-save",
                "message": "{{secret}}"
              },
              "steps": [],
              "events": [],
              "artifacts": []
            }
            """);

        var result = await cli.InvokeRawAsync(
            "devflow", "evidence", "inspect-trust", input, "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(secret, result.StdOut, StringComparison.Ordinal);
        var json = result.ParseJsonOutput();
        Assert.Equal("imported-artifact", json.GetProperty("identity").GetProperty("namespace").GetString());
        Assert.Equal("untrusted", json.GetProperty("verification").GetProperty("state").GetString());
        Assert.Empty(server.RecordedRequests);
    }

    [Fact]
    public async Task EvidenceVerifyAppleQa_ReportsBoundedUntrustedDiagnosticImportWithoutContactingAnAgent()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;
        var archive = Path.Combine(_root, "apple-qa-return.zip");
        await File.WriteAllBytesAsync(archive, CreateAppleQaReturnArchive());

        var result = await cli.InvokeRawAsync(
            "devflow", "evidence", "verify-apple-qa", archive, "--import-diagnostics", "--json");

        Assert.True(
            result.ExitCode == 0,
            $"verify-apple-qa failed: stdout={result.StdOut}; stderr={result.StdErr}");
        var json = result.ParseJsonOutput();
        Assert.True(json.GetProperty("ok").GetBoolean());
        Assert.Equal("ios", json.GetProperty("platform").GetString());
        Assert.False(json.GetProperty("executed").GetBoolean());
        Assert.False(json.GetProperty("rawContentRetained").GetBoolean());
        Assert.False(json.GetProperty("repairProposalAuthority").GetBoolean());
        var imported = Assert.Single(json.GetProperty("importedDiagnostics").EnumerateArray());
        Assert.Equal("imported-artifact", imported.GetProperty("identity").GetProperty("namespace").GetString());
        Assert.Equal("untrusted", imported.GetProperty("verification").GetProperty("state").GetString());
        Assert.Empty(server.RecordedRequests);
    }

    [Fact]
    public async Task EvidenceCapture_WritesABundleAndReportsItsManifest()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;
        var output = Path.Combine(_root, "capture.mauitrace");

        var result = await cli.InvokeAsync("devflow", "evidence", "capture", "--output", output, "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.True(json.GetProperty("ok").GetBoolean());
        Assert.True(File.Exists(output));
        Assert.Equal(Path.GetFullPath(output), json.GetProperty("path").GetString());

        var manifest = json.GetProperty("manifest");
        Assert.Equal("maui-devflow-evidence", manifest.GetProperty("schema").GetString());
        Assert.Equal("cli", manifest.GetProperty("source").GetString());
        Assert.False(manifest.GetProperty("screenshot").GetProperty("included").GetBoolean());
        Assert.True(manifest.GetProperty("entries").GetArrayLength() > 0);
    }

    [Fact]
    public async Task EvidencePreviewAndCapture_RejectAnUnreachableAgent()
    {
        var port = GetUnusedPort();
        var cli = new CliTestHarness(port);
        var output = Path.Combine(_root, "unreachable.mauitrace");

        var preview = await cli.InvokeAsync("devflow", "evidence", "preview", "--json");
        var capture = await cli.InvokeAsync(
            "devflow", "evidence", "capture", "--output", output, "--json");

        Assert.Equal(1, preview.ExitCode);
        Assert.Equal(1, capture.ExitCode);
        Assert.Contains("No DevFlow agent responded", preview.StdErr, StringComparison.Ordinal);
        Assert.Contains($"localhost:{port}", preview.StdErr, StringComparison.Ordinal);
        Assert.Contains("maui devflow agent status", preview.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("maui devflow status", preview.StdErr, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task EvidenceJsonError_DoesNotIncludeAHumanAgentLabel()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;
        var output = Path.Combine(_root, "existing-json.mauitrace");
        File.WriteAllText(output, "existing");
        DevFlowCommands.ResolveRunningBrokerPortAsync = () => Task.FromResult<int?>(19223);
        DevFlowCommands.ListBrokerAgentsAsync = _ => Task.FromResult<AgentRegistration[]?>([
            new AgentRegistration
            {
                Id = "target",
                AppName = "Target",
                Platform = "Windows",
                Tfm = "net10.0-windows",
                Port = server.Port
            },
            new AgentRegistration
            {
                Id = "other",
                AppName = "Other",
                Platform = "Android",
                Tfm = "net10.0-android",
                Port = server.Port + 1
            }
        ]);
        try
        {
            var result = await cli.InvokeAsync(
                "devflow", "evidence", "capture", "--output", output, "--json");

            Assert.Equal(1, result.ExitCode);
            using var error = JsonDocument.Parse(result.StdErr);
            Assert.Equal("InvocationError", error.RootElement.GetProperty("type").GetString());
            Assert.DoesNotContain("target:", result.StdErr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }

    [Fact]
    public async Task EvidenceCapture_RefusesToOverwriteWithoutTheFlag()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;
        var output = Path.Combine(_root, "existing.mauitrace");
        File.WriteAllText(output, "original");

        var refused = await cli.InvokeAsync("devflow", "evidence", "capture", "--output", output, "--json");

        Assert.Equal(1, refused.ExitCode);
        using var error = JsonDocument.Parse(refused.StdErr);
        Assert.Equal("InvocationError", error.RootElement.GetProperty("type").GetString());
        Assert.Contains("already exists", error.RootElement.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("original", File.ReadAllText(output));

        var overwritten = await cli.InvokeAsync("devflow", "evidence", "capture", "--output", output, "--overwrite", "--json");

        Assert.Equal(0, overwritten.ExitCode);
        Assert.NotEqual("original", File.ReadAllText(output));
    }

    [Fact]
    public async Task EvidenceCapture_RejectsANonMauitraceExtension()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync(
            "devflow", "evidence", "capture", "--output", Path.Combine(_root, "bundle.zip"), "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(".mauitrace", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceCapture_RejectsAMissingWorkflowFile()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync(
            "devflow", "evidence", "capture",
            "--output", Path.Combine(_root, "workflow.mauitrace"),
            "--workflow", Path.Combine(_root, "missing.md"),
            "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Workflow file not found", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceView_GeneratesAStaticReportFromTheBundle()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;
        var bundle = Path.Combine(_root, "view.mauitrace");
        var report = Path.Combine(_root, "report.html");
        Assert.Equal(0, (await cli.InvokeAsync("devflow", "evidence", "capture", "--output", bundle, "--json")).ExitCode);

        var result = await cli.InvokeRawAsync(
            "devflow", "evidence", "view", bundle, "--no-open", "--output-report", report, "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.True(json.GetProperty("ok").GetBoolean());
        Assert.False(json.GetProperty("opened").GetBoolean());
        Assert.True(File.Exists(report));

        var html = File.ReadAllText(report);
        Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
        Assert.Contains("script-src 'none'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvidenceView_RefusesToOverwriteAReportWithoutTheFlag()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;
        var bundle = Path.Combine(_root, "view-overwrite.mauitrace");
        var report = Path.Combine(_root, "report.html");
        Assert.Equal(0, (await cli.InvokeAsync(
            "devflow", "evidence", "capture", "--output", bundle, "--json")).ExitCode);
        File.WriteAllText(report, "original");

        var refused = await cli.InvokeRawAsync(
            "devflow", "evidence", "view", bundle, "--no-open", "--output-report", report, "--json");
        var overwritten = await cli.InvokeRawAsync(
            "devflow", "evidence", "view", bundle, "--no-open", "--output-report", report, "--overwrite", "--json");

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("already exists", refused.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, overwritten.ExitCode);
        Assert.Contains("Content-Security-Policy", File.ReadAllText(report), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceView_RejectsAMissingBundle()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeRawAsync(
            "devflow", "evidence", "view", Path.Combine(_root, "nope.mauitrace"), "--no-open", "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Bundle not found", result.StdErr, StringComparison.Ordinal);
    }

    private static int GetUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static byte[] CreateAppleQaReturnArchive()
    {
        const string prefix = "artifacts/devflow/run-1/ios";
        var flowPath = $"{prefix}/apple-flow-runs/tier-one/flow-run.json";
        var flowRun = System.Text.Encoding.UTF8.GetBytes("""
            {
              "schema": 1,
              "runId": "foreign-run-id",
              "flowDigest": "flow",
              "target": { "platform": "ios" },
              "outcome": { "status": "failed", "terminal": true },
              "steps": [],
              "events": [],
              "artifacts": []
            }
            """);
        var digest = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(flowRun)).ToLowerInvariant();
        var hashA = "sha256:" + new string('a', 64);
        var manifest = $$"""
            {
              "schema": 1,
              "kind": "devflow-flow-qa",
              "repository": { "commit": "commit" },
              "workflow": { "runId": "run-1" },
              "experimental": false,
              "backend": null,
              "officialCoverage": true,
              "macCatalystEquivalent": null,
              "testing": { "packageVersion": "testing" },
              "platform": {
                "name": "ios",
                "host": {
                  "xcode": "Xcode 26.3",
                  "runtime": "iOS 18.0",
                  "deviceEvidence": {
                    "kind": "simulator",
                    "realDevice": false,
                    "deviceIdFingerprint": "{{hashA}}",
                    "profile": "iPhone"
                  }
                }
              },
              "app": {
                "project": "samples/DevFlow.Sample/DevFlow.Sample.csproj",
                "sourceDigest": "{{hashA}}",
                "buildFingerprint": "{{hashA}}",
                "packageDigest": "{{hashA}}"
              },
              "flows": [
                { "path": "samples/DevFlow.Sample/maui-tests/tier-one.md", "sha256": "{{hashA}}" }
              ],
              "appleQa": {
                "schema": 1,
                "kind": "devflow-apple-flow-qa",
                "platform": "ios",
                "experimental": false,
                "backend": null,
                "officialCoverage": true,
                "macCatalystEquivalent": null,
                "spike": {
                  "status": "proved",
                  "foregroundProof": true,
                  "authenticatedTransport": true,
                  "receipt": true,
                  "cancellation": true,
                  "parity": true
                },
                "checkpoint": {
                  "resetFingerprint": "{{hashA}}",
                  "seedFingerprint": "{{hashA}}",
                  "backendStateFingerprint": "{{hashA}}"
                },
                "apple": {
                  "xcodeVersion": "Xcode 26.3",
                  "simulatorRuntime": "iOS 18.0",
                  "simulatorDeviceFingerprint": "{{hashA}}",
                  "simulatorDeviceProfile": "iPhone"
                },
                "flows": [
                  {
                    "name": "tier-one",
                    "firstAttempt": {
                      "status": "failed",
                      "report": "{{flowPath}}",
                      "reportDigest": "{{hashA}}"
                    },
                    "cleanAttempts": [
                      {
                        "status": "failed",
                        "report": "{{flowPath}}",
                        "reportDigest": "{{hashA}}"
                      }
                    ]
                  }
                ]
              },
              "artifacts": [
                {
                  "kind": "flow-run-report",
                  "path": "{{flowPath}}",
                  "sha256": "{{digest}}",
                  "sizeBytes": {{flowRun.Length}},
                  "redacted": true
                }
              ],
              "omissions": []
            }
            """;

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteArchiveEntry(zip, $"{prefix}/manifest.json", System.Text.Encoding.UTF8.GetBytes(manifest));
            WriteArchiveEntry(zip, flowPath, flowRun);
        }
        return output.ToArray();
    }

    private static void WriteArchiveEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(content);
    }
}
