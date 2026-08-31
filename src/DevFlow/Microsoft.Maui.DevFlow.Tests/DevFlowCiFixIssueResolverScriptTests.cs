using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class DevFlowCiFixIssueResolverScriptTests : IDisposable
{
    private const string Repository = "dotnet/maui-labs";
    private const int IssueNumber = 42;
    private const long RunId = 123456789;
    private const int RunAttempt = 1;
    private const long HandoffArtifactId = 7001;
    private const long EvidenceArtifactId = 7002;
    private const string CommitSha = "0123456789abcdef0123456789abcdef01234567";
    private const string TestIdentity =
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Fingerprint =
        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly string _repositoryRoot = FindRepositoryRoot();
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "devflow-ci-fix-issue-resolver-tests",
        Guid.NewGuid().ToString("N"));

    public DevFlowCiFixIssueResolverScriptTests()
        => Directory.CreateDirectory(_testRoot);

    [Fact]
    public async Task Resolver_TrustedPublisherIssue_ReturnsExactLocalHandoff()
    {
        var fixture = WriteFixture();

        var result = await RunResolverAsync(fixture);

        Assert.True(result.ExitCode == 0, result.Json.ToString());
        Assert.True(result.Json.GetProperty("ok").GetBoolean());
        Assert.Equal(Repository, result.Json.GetProperty("repository").GetString());
        Assert.Equal(IssueNumber, result.Json.GetProperty("issueNumber").GetInt32());
        Assert.Equal(RunId, result.Json.GetProperty("runId").GetInt64());
        Assert.Equal(RunAttempt, result.Json.GetProperty("runAttempt").GetInt32());
        Assert.Equal("android", result.Json.GetProperty("platform").GetString());
        Assert.Equal(TestIdentity, result.Json.GetProperty("testIdentity").GetString());
        Assert.Equal("issue-body", result.Json.GetProperty("occurrenceSource").GetString());
        Assert.True(result.Json.GetProperty("evidenceAvailable").GetBoolean());
        Assert.Equal(
            $"devflow-failure-handoff-{RunId}-{RunAttempt}",
            result.Json.GetProperty("handoffArtifactName").GetString());
        Assert.Equal(
            $"devflow-flow-evidence-android-{RunId}-{RunAttempt}",
            result.Json.GetProperty("evidenceArtifactName").GetString());
    }

    [Fact]
    public async Task Resolver_FullIssueUrl_InfersRepository()
    {
        var fixture = WriteFixture();

        var result = await RunResolverAsync(
            fixture,
            issue: $"https://github.com/{Repository}/issues/{IssueNumber}",
            includeRepository: false);

        Assert.True(result.ExitCode == 0, result.Json.ToString());
        Assert.True(result.Json.GetProperty("ok").GetBoolean());
        Assert.Equal(Repository, result.Json.GetProperty("repository").GetString());
    }

    [Fact]
    public async Task Resolver_AcceptsIssueBodyProducedByPublisher()
    {
        var publisherBody = await CreatePublisherIssueBodyAsync();
        var fixture = WriteFixture(issueTransform: issue => issue["body"] = publisherBody);

        var result = await RunResolverAsync(fixture);

        Assert.True(result.ExitCode == 0, result.Json.ToString());
        Assert.Equal(Fingerprint, result.Json.GetProperty("fingerprint").GetString());
        Assert.Equal(TestIdentity, result.Json.GetProperty("testIdentity").GetString());
    }

    [Fact]
    public async Task Resolver_TrustedRecurrence_UsesNewestOccurrence()
    {
        const long recurrenceRunId = RunId + 1;
        const long recurrenceHandoffArtifactId = HandoffArtifactId + 10;
        const long recurrenceEvidenceArtifactId = EvidenceArtifactId + 10;
        const string recurrenceCommit = "1123456789abcdef0123456789abcdef01234567";
        var fixture = WriteFixture(
            runTransform: run =>
            {
                run["id"] = recurrenceRunId;
                run["head_sha"] = recurrenceCommit;
            },
            artifactsTransform: artifacts =>
            {
                artifacts["artifacts"] = JsonSerializer.SerializeToNode(new[]
                {
                    new
                    {
                        id = recurrenceHandoffArtifactId,
                        name = $"devflow-failure-handoff-{recurrenceRunId}-{RunAttempt}",
                        expired = false,
                    },
                    new
                    {
                        id = recurrenceEvidenceArtifactId,
                        name = $"devflow-flow-evidence-android-{recurrenceRunId}-{RunAttempt}",
                        expired = false,
                    },
                });
            },
            commentsTransform: comments =>
            {
                var payload = string.Join(
                    "\n",
                    "",
                    "## Recurrence",
                    "",
                    $"- Run: [#{recurrenceRunId} attempt {RunAttempt}](https://github.com/{Repository}/actions/runs/{recurrenceRunId}/attempts/{RunAttempt})",
                    $"- Commit: `{recurrenceCommit}`",
                    "- Category/platform: `test-failure` / `android`",
                    $"- Test identity: `{TestIdentity}`",
                    "- Evidence sufficiency: `sufficient`",
                    $"- Artifact: [download](https://github.com/{Repository}/actions/runs/{recurrenceRunId}/artifacts/{recurrenceHandoffArtifactId})",
                    "- Handoff entry: `sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc`",
                    "- Downloaded ZIP: `sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd`",
                    "",
                    "Verify the retained ZIP with the local handoff command in the issue body before using it. This recurrence grants no repair authority.");
                comments.Add(JsonSerializer.SerializeToNode(new
                {
                    user = new { login = "github-actions[bot]", type = "Bot" },
                    body =
                        $"<!-- devflow-ci-failure-occurrence:v1 run={recurrenceRunId} attempt={RunAttempt} body={Hash(payload)} -->\n{payload}",
                }));
            });
        var runDirectory = Path.Combine(_testRoot, "recurrence-runs");
        var artifactsDirectory = Path.Combine(_testRoot, "recurrence-artifacts");
        Directory.CreateDirectory(runDirectory);
        Directory.CreateDirectory(artifactsDirectory);
        File.Copy(
            fixture.RunJsonPath,
            Path.Combine(runDirectory, $"run-{recurrenceRunId}-{RunAttempt}.json"));
        File.Copy(
            fixture.ArtifactsJsonPath,
            Path.Combine(artifactsDirectory, $"artifacts-{recurrenceRunId}-{RunAttempt}.json"));

        var result = await RunResolverAsync(
            fixture,
            runJsonDirectory: runDirectory,
            artifactsJsonDirectory: artifactsDirectory);

        Assert.True(result.ExitCode == 0, result.Json.ToString());
        Assert.Equal(recurrenceRunId, result.Json.GetProperty("runId").GetInt64());
        Assert.Equal(recurrenceCommit, result.Json.GetProperty("commitSha").GetString());
        Assert.Equal("recurrence-comment", result.Json.GetProperty("occurrenceSource").GetString());
        Assert.Equal(recurrenceHandoffArtifactId, result.Json.GetProperty("handoffArtifactId").GetInt64());
    }

    [Fact]
    public async Task Resolver_ExpiredNewestRecurrence_FallsBackToRetainedIssueOccurrence()
    {
        const long recurrenceRunId = RunId + 1;
        const long recurrenceHandoffArtifactId = HandoffArtifactId + 20;
        const string recurrenceCommit = "2123456789abcdef0123456789abcdef01234567";
        var fixture = WriteFixture(commentsTransform: comments =>
        {
            var payload = RecurrencePayload(
                recurrenceRunId,
                recurrenceCommit,
                recurrenceHandoffArtifactId);
            comments.Add(JsonSerializer.SerializeToNode(new
            {
                user = new { login = "github-actions[bot]", type = "Bot" },
                body =
                    $"<!-- devflow-ci-failure-occurrence:v1 run={recurrenceRunId} attempt={RunAttempt} body={Hash(payload)} -->\n{payload}",
            }));
        });
        var runDirectory = Path.Combine(_testRoot, "fallback-runs");
        var artifactsDirectory = Path.Combine(_testRoot, "fallback-artifacts");
        Directory.CreateDirectory(runDirectory);
        Directory.CreateDirectory(artifactsDirectory);
        File.Copy(
            fixture.RunJsonPath,
            Path.Combine(runDirectory, $"run-{RunId}-{RunAttempt}.json"));
        File.Copy(
            fixture.ArtifactsJsonPath,
            Path.Combine(artifactsDirectory, $"artifacts-{RunId}-{RunAttempt}.json"));

        var recurrenceRun = JsonNode.Parse(File.ReadAllText(fixture.RunJsonPath))!.AsObject();
        recurrenceRun["id"] = recurrenceRunId;
        recurrenceRun["head_sha"] = recurrenceCommit;
        WriteJson(
            Path.Combine(runDirectory, $"run-{recurrenceRunId}-{RunAttempt}.json"),
            recurrenceRun,
            absolutePath: true);
        var recurrenceArtifacts = JsonSerializer.SerializeToNode(new
        {
            total_count = 1,
            artifacts = new[]
            {
                new
                {
                    id = recurrenceHandoffArtifactId,
                    name = $"devflow-failure-handoff-{recurrenceRunId}-{RunAttempt}",
                    expired = true,
                },
            },
        })!;
        WriteJson(
            Path.Combine(artifactsDirectory, $"artifacts-{recurrenceRunId}-{RunAttempt}.json"),
            recurrenceArtifacts,
            absolutePath: true);

        var result = await RunResolverAsync(
            fixture,
            runJsonDirectory: runDirectory,
            artifactsJsonDirectory: artifactsDirectory);

        Assert.True(result.ExitCode == 0, result.Json.ToString());
        Assert.Equal(RunId, result.Json.GetProperty("runId").GetInt64());
        Assert.Equal("issue-body", result.Json.GetProperty("occurrenceSource").GetString());
    }

    [Fact]
    public async Task Resolver_TamperedIssueBody_RefusesBeforeUsingRunFacts()
    {
        var fixture = WriteFixture(issueTransform: issue =>
        {
            issue["body"] = issue["body"]!.GetValue<string>() + "\nuntrusted addition";
        });

        var result = await RunResolverAsync(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.Json.GetProperty("ok").GetBoolean());
        Assert.Equal("issue-body-digest-mismatch", result.Json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Resolver_NonPublisherAuthor_RefusesIssue()
    {
        var fixture = WriteFixture(issueTransform: issue =>
        {
            issue["user"] = JsonSerializer.SerializeToNode(new { login = "octocat", type = "User" });
        });

        var result = await RunResolverAsync(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("issue-author-untrusted", result.Json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Resolver_WorkflowRunFromAnotherBranch_RefusesIssue()
    {
        var fixture = WriteFixture(runTransform: run => run["head_branch"] = "feature/untrusted");

        var result = await RunResolverAsync(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("workflow-run-metadata-mismatch", result.Json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Resolver_ExpiredEvidenceArtifact_RefusesIssue()
    {
        var fixture = WriteFixture(artifactsTransform: artifacts =>
        {
            var values = artifacts["artifacts"]!.AsArray();
            values[1]!["expired"] = true;
        });

        var result = await RunResolverAsync(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("artifact-unavailable", result.Json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Resolver_MissingEvidenceArtifact_ReturnsHonestHandoffOnlyResult()
    {
        var fixture = WriteFixture(artifactsTransform: artifacts =>
        {
            artifacts["total_count"] = 1;
            artifacts["artifacts"] = JsonSerializer.SerializeToNode(new[]
            {
                new
                {
                    id = HandoffArtifactId,
                    name = $"devflow-failure-handoff-{RunId}-{RunAttempt}",
                    expired = false,
                },
            });
        });

        var result = await RunResolverAsync(fixture);

        Assert.True(result.ExitCode == 0, result.Json.ToString());
        Assert.False(result.Json.GetProperty("evidenceAvailable").GetBoolean());
        Assert.Equal(0, result.Json.GetProperty("evidenceArtifactId").GetInt64());
    }

    [Fact]
    public async Task Resolver_MacOSIssue_MapsAppKitEvidenceArtifactName()
    {
        var fixture = WriteFixture(platform: "macos");

        var result = await RunResolverAsync(fixture);

        Assert.True(result.ExitCode == 0, result.Json.ToString());
        Assert.Equal("macos", result.Json.GetProperty("platform").GetString());
        Assert.Equal(
            $"devflow-flow-evidence-macos-appkit-{RunId}-{RunAttempt}",
            result.Json.GetProperty("evidenceArtifactName").GetString());
    }

    [Fact]
    public async Task Resolver_ClosedIssue_RefusesLocalFix()
    {
        var fixture = WriteFixture(issueTransform: issue => issue["state"] = "closed");

        var result = await RunResolverAsync(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("issue-not-open", result.Json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Resolver_OfflineJsonInputsWithoutFixtureGate_AreRefused()
    {
        var fixture = WriteFixture();

        var result = await RunResolverAsync(fixture, enableOfflineFixture: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("offline-fixture-not-enabled", result.Json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Resolver_DemoLabelledIssue_ResolvesAsNonqualifiedDemo()
    {
        var fixture = WriteFixture(lane: "demo");

        var result = await RunResolverAsync(fixture);

        Assert.True(result.ExitCode == 0, result.Json.ToString());
        Assert.True(result.Json.GetProperty("ok").GetBoolean());
        Assert.Equal("demo", result.Json.GetProperty("lane").GetString());
        Assert.True(result.Json.GetProperty("demo").GetBoolean());
        Assert.Equal("not-qualified", result.Json.GetProperty("qualification").GetString());
        Assert.Equal("none", result.Json.GetProperty("repairAuthority").GetString());
        Assert.Equal("workflow_dispatch", result.Json.GetProperty("sourceEvent").GetString());
        Assert.Equal(
            $"devflow-demo-handoff-{RunId}-{RunAttempt}",
            result.Json.GetProperty("handoffArtifactName").GetString());
        Assert.Equal(
            $"devflow-demo-evidence-android-{RunId}-{RunAttempt}",
            result.Json.GetProperty("evidenceArtifactName").GetString());
        Assert.True(result.Json.GetProperty("evidenceAvailable").GetBoolean());
    }

    [Fact]
    public async Task Resolver_ProductionIssue_ReportsTheProductionLane()
    {
        var fixture = WriteFixture();

        var result = await RunResolverAsync(fixture);

        Assert.True(result.ExitCode == 0, result.Json.ToString());
        Assert.Equal("production", result.Json.GetProperty("lane").GetString());
        Assert.False(result.Json.GetProperty("demo").GetBoolean());
        Assert.Equal("qualified", result.Json.GetProperty("qualification").GetString());
    }

    [Fact]
    public async Task Resolver_AcceptsDemoIssueBodyProducedByPublisher()
    {
        var publisherBody = await CreatePublisherIssueBodyAsync("demo");
        var fixture = WriteFixture(lane: "demo", issueTransform: issue => issue["body"] = publisherBody);

        var result = await RunResolverAsync(fixture);

        Assert.True(result.ExitCode == 0, result.Json.ToString());
        Assert.True(result.Json.GetProperty("demo").GetBoolean());
        Assert.Equal(Fingerprint, result.Json.GetProperty("fingerprint").GetString());
        Assert.Equal(TestIdentity, result.Json.GetProperty("testIdentity").GetString());
    }

    [Fact]
    public async Task Resolver_BothLaneLabels_RefusesRatherThanGuessing()
    {
        var fixture = WriteFixture(lane: "demo", issueTransform: issue =>
        {
            issue["labels"] = JsonSerializer.SerializeToNode(new[]
            {
                new { name = "devflow-ci-failure" },
                new { name = "devflow-ci-failure-demo" },
            });
        });

        var result = await RunResolverAsync(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("issue-label-ambiguous", result.Json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Resolver_NoLaneLabel_RefusesRatherThanGuessing()
    {
        var fixture = WriteFixture(issueTransform: issue =>
        {
            issue["labels"] = JsonSerializer.SerializeToNode(new[] { new { name = "bug" } });
        });

        var result = await RunResolverAsync(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("issue-label-missing", result.Json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Resolver_DemoLabelWithProductionBody_IsRefused()
    {
        var production = WriteFixture();
        var productionBody = JsonNode.Parse(File.ReadAllText(production.IssueJsonPath))!
            .AsObject()["body"]!.GetValue<string>();
        var fixture = WriteFixture(lane: "demo", issueTransform: issue => issue["body"] = productionBody);

        var result = await RunResolverAsync(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("issue-marker-invalid", result.Json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Resolver_ProductionLabelWithDemoBody_IsRefused()
    {
        var demo = WriteFixture(lane: "demo");
        var demoBody = JsonNode.Parse(File.ReadAllText(demo.IssueJsonPath))!
            .AsObject()["body"]!.GetValue<string>();
        var fixture = WriteFixture(issueTransform: issue => issue["body"] = demoBody);

        var result = await RunResolverAsync(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("issue-marker-invalid", result.Json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Resolver_TamperedDemoIssueBody_RefusesBeforeUsingRunFacts()
    {
        var fixture = WriteFixture(lane: "demo", issueTransform: issue =>
        {
            issue["body"] = issue["body"]!.GetValue<string>() + "\nuntrusted addition";
        });

        var result = await RunResolverAsync(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("issue-body-digest-mismatch", result.Json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Resolver_DemoIssueWithProductionArtifactNames_IsRefused()
    {
        var fixture = WriteFixture(lane: "demo", artifactsTransform: artifacts =>
        {
            artifacts["artifacts"] = JsonSerializer.SerializeToNode(new[]
            {
                new
                {
                    id = HandoffArtifactId,
                    name = $"devflow-failure-handoff-{RunId}-{RunAttempt}",
                    expired = false,
                },
                new
                {
                    id = EvidenceArtifactId,
                    name = $"devflow-flow-evidence-android-{RunId}-{RunAttempt}",
                    expired = false,
                },
            });
        });

        var result = await RunResolverAsync(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("artifact-match-invalid", result.Json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Resolver_DemoIssueFromANonDispatchRun_IsRefused()
    {
        var fixture = WriteFixture(lane: "demo", runTransform: run => run["event"] = "schedule");

        var result = await RunResolverAsync(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("workflow-run-metadata-mismatch", result.Json.GetProperty("error").GetString());
    }

    [Fact]
    public void Resolver_MirrorsAreByteIdentical()
    {
        var canonical = Path.Combine(
            _repositoryRoot,
            "plugins",
            "dotnet-maui",
            "skills",
            "maui-devflow-ci-fix",
            "scripts",
            "Resolve-DevFlowCiFailureIssue.ps1");
        var mirror = Path.Combine(
            _repositoryRoot,
            ".github",
            "skills",
            "maui-devflow-ci-fix",
            "scripts",
            "Resolve-DevFlowCiFailureIssue.ps1");

        Assert.Equal(File.ReadAllBytes(canonical), File.ReadAllBytes(mirror));
    }

    [Fact]
    public void Resolver_ValidatesTheReferencedWorkflowAttempt()
    {
        var script = File.ReadAllText(Path.Combine(
            _repositoryRoot,
            "plugins",
            "dotnet-maui",
            "skills",
            "maui-devflow-ci-fix",
            "scripts",
            "Resolve-DevFlowCiFailureIssue.ps1"));

        Assert.Contains(
            "/actions/runs/$candidateRunId/attempts/$candidateRunAttempt",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "/actions/runs/$runId/attempts/$runAttempt",
            script,
            StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    private ResolverFixture WriteFixture(
        Action<System.Text.Json.Nodes.JsonObject>? issueTransform = null,
        Action<System.Text.Json.Nodes.JsonObject>? runTransform = null,
        Action<System.Text.Json.Nodes.JsonObject>? artifactsTransform = null,
        Action<System.Text.Json.Nodes.JsonArray>? commentsTransform = null,
        string platform = "android",
        string lane = "production")
    {
        var demo = lane == "demo";
        var markerPrefix = demo ? "devflow-ci-failure-demo" : "devflow-ci-failure";
        var titlePrefix = demo ? "[DevFlow CI DEMO - NOT QUALIFIED]" : "[DevFlow CI]";
        var firstHeading = demo ? "## Demo handoff (not qualified)" : "## Verified handoff";
        var label = demo ? "devflow-ci-failure-demo" : "devflow-ci-failure";
        var sourceEvent = demo ? "workflow_dispatch" : "schedule";
        var dataSuffix = demo
            ? " lane=demo-emulator-showcase device=emulator qualification=not-qualified"
            : string.Empty;
        var handoffArtifactName = demo
            ? $"devflow-demo-handoff-{RunId}-{RunAttempt}"
            : $"devflow-failure-handoff-{RunId}-{RunAttempt}";
        var evidenceArtifactName = demo
            ? $"devflow-demo-evidence-android-{RunId}-{RunAttempt}"
            : $"devflow-flow-evidence-{EvidenceArtifactPlatform(platform)}-{RunId}-{RunAttempt}";

        var payload = string.Join(
            "\n",
            $"<!-- {markerPrefix}-occurrence:v1 run={RunId} attempt={RunAttempt} -->",
            $"<!-- {markerPrefix}-data:v1 category=test-failure platform={platform} testIdentity={TestIdentity} evidence=sufficient{dataSuffix} -->",
            "",
            firstHeading,
            "",
            $"Run {RunId}.",
            $"- Source event: `{sourceEvent}`",
            $"- Commit: `{CommitSha}`",
            "",
            "## Evidence",
            "",
            "Sufficient.",
            "",
            "## Artifact handoff",
            "",
            $"- Download: [retained workflow artifact](https://github.com/{Repository}/actions/runs/{RunId}/artifacts/{HandoffArtifactId})",
            "",
            "## Local handoff",
            "",
            "Use the fixed local workflow.");
        var bodyDigest = Hash(payload);
        var body =
            $"<!-- {markerPrefix}:v1 fingerprint={Fingerprint} body={bodyDigest} -->\n{payload}";
        var title = $"{titlePrefix} test-failure on {platform} ({TestIdentity.Substring(7, 12)})";

        var issue = JsonSerializer.SerializeToNode(new
        {
            number = IssueNumber,
            title,
            body,
            state = "open",
            user = new { login = "github-actions[bot]", type = "Bot" },
            labels = new[] { new { name = label } },
        })!.AsObject();
        issueTransform?.Invoke(issue);

        var repository = JsonSerializer.SerializeToNode(new
        {
            full_name = Repository,
            default_branch = "main",
        })!.AsObject();

        var run = JsonSerializer.SerializeToNode(new
        {
            id = RunId,
            run_attempt = RunAttempt,
            name = "DevFlow Integration Tests",
            path = ".github/workflows/devflow-integration.yml",
            @event = sourceEvent,
            head_branch = "main",
            head_sha = CommitSha,
            head_repository = new { full_name = Repository },
            conclusion = "failure",
            pull_requests = Array.Empty<object>(),
        })!.AsObject();
        runTransform?.Invoke(run);

        var artifacts = JsonSerializer.SerializeToNode(new
        {
            total_count = 2,
            artifacts = new[]
            {
                new
                {
                    id = HandoffArtifactId,
                    name = handoffArtifactName,
                    expired = false,
                },
                new
                {
                    id = EvidenceArtifactId,
                    name = evidenceArtifactName,
                    expired = false,
                },
            },
        })!.AsObject();
        artifactsTransform?.Invoke(artifacts);
        var comments = new System.Text.Json.Nodes.JsonArray();
        commentsTransform?.Invoke(comments);

        var suffix = demo ? "-demo" : string.Empty;
        return new ResolverFixture(
            WriteJson($"issue{suffix}.json", issue),
            WriteJson($"repository{suffix}.json", repository),
            WriteJson($"run{suffix}.json", run),
            WriteJson($"artifacts{suffix}.json", artifacts),
            WriteJson($"comments{suffix}.json", comments));
    }

    private string WriteJson(
        string name,
        System.Text.Json.Nodes.JsonNode value,
        bool absolutePath = false)
    {
        var path = absolutePath ? name : Path.Combine(_testRoot, name);
        File.WriteAllText(path, value.ToJsonString());
        return path;
    }

    private async Task<ResolverResult> RunResolverAsync(
        ResolverFixture fixture,
        string? issue = null,
        bool includeRepository = true,
        string? runJsonDirectory = null,
        string? artifactsJsonDirectory = null,
        bool enableOfflineFixture = true)
    {
        var script = Path.Combine(
            _repositoryRoot,
            "plugins",
            "dotnet-maui",
            "skills",
            "maui-devflow-ci-fix",
            "scripts",
            "Resolve-DevFlowCiFailureIssue.ps1");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh")
            {
                WorkingDirectory = _repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        AddArgument("-NoLogo");
        AddArgument("-NoProfile");
        AddArgument("-File");
        AddArgument(script);
        AddArgument("-Issue");
        AddArgument(issue ?? IssueNumber.ToString());
        if (includeRepository)
        {
            AddArgument("-Repository");
            AddArgument(Repository);
        }
        AddArgument("-IssueJsonPath");
        AddArgument(fixture.IssueJsonPath);
        AddArgument("-RepositoryJsonPath");
        AddArgument(fixture.RepositoryJsonPath);
        if (runJsonDirectory is null)
        {
            AddArgument("-RunJsonPath");
            AddArgument(fixture.RunJsonPath);
        }
        else
        {
            AddArgument("-RunJsonDirectory");
            AddArgument(runJsonDirectory);
        }
        if (artifactsJsonDirectory is null)
        {
            AddArgument("-ArtifactsJsonPath");
            AddArgument(fixture.ArtifactsJsonPath);
        }
        else
        {
            AddArgument("-ArtifactsJsonDirectory");
            AddArgument(artifactsJsonDirectory);
        }
        AddArgument("-CommentsJsonPath");
        AddArgument(fixture.CommentsJsonPath);
        if (enableOfflineFixture)
        {
            AddArgument("-OfflineFixture");
            process.StartInfo.Environment["DEVFLOW_CI_FIX_TEST_FIXTURES"] = "1";
        }
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await standardOutput;
        var error = await standardError;
        var jsonLine = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Last();

        return new ResolverResult(
            process.ExitCode,
            JsonDocument.Parse(jsonLine).RootElement.Clone(),
            output,
            error);

        void AddArgument(string value) => process.StartInfo.ArgumentList.Add(value);
    }

    private async Task<string> CreatePublisherIssueBodyAsync(string lane = "production")
    {
        var probePath = Path.Combine(_testRoot, $"publisher-body-{lane}.ps1");
        var publisherPath = Path.Combine(
            _repositoryRoot,
            "eng",
            "devflow",
            "Publish-DevFlowFailureIssue.ps1");
        File.WriteAllText(
            probePath,
            """
            param([string] $PublisherPath, [string] $Lane)

            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile(
                $PublisherPath,
                [ref] $tokens,
                [ref] $errors)
            $wanted = @(
                'Get-LanePublisherProfile',
                'Get-Sha256Bytes',
                'Get-Sha256Text',
                'Get-RunUrl',
                'Get-OccurrenceMarker',
                'Get-IssueDataMarker',
                'New-IssueBody')
            foreach ($definition in $ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                            $node.Name -in $wanted
                    }, $true)) {
                . ([scriptblock]::Create($definition.Extent.Text))
            }

            $Repository = 'dotnet/maui-labs'
            $WorkflowName = 'DevFlow Integration Tests'
            $WorkflowPath = '.github/workflows/devflow-integration.yml'
            $SourceEvent = if ($Lane -eq 'demo') { 'workflow_dispatch' } else { 'schedule' }
            $HeadRepository = $Repository
            $HeadRef = 'main'
            $DefaultBranch = 'main'
            $WorkflowConclusion = 'failure'
            [Int64] $RunId = 123456789
            [Int32] $RunAttempt = 1
            $CommitSha = '0123456789abcdef0123456789abcdef01234567'
            [Int32] $PullRequestNumber = 0
            $laneProfile = Get-LanePublisherProfile $Lane
            $markerPrefix = [string] $laneProfile['markerPrefix']
            $handoff = [ordered]@{
                category = 'test-failure'
                platform = 'android'
                testIdentitySha256 = 'sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
                evidenceSufficiency = 'sufficient'
            }
            if ($laneProfile['demo']) {
                $handoff['qualification'] = 'not-qualified'
                $handoff['laneKind'] = 'demo-emulator-showcase'
                $handoff['deviceEvidenceKind'] = 'emulator'
                $handoff['repairAuthority'] = 'none'
            }
            $body = New-IssueBody `
                -Handoff $handoff `
                -Fingerprint 'sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' `
                -ArchiveSha256 'sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc' `
                -HandoffSha256 'sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd' `
                -ArtifactId 7001
            [ordered]@{ body = $body } | ConvertTo-Json -Compress
            """,
            new UTF8Encoding(false));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh")
            {
                WorkingDirectory = _repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        foreach (var argument in new[]
                 {
                     "-NoLogo",
                     "-NoProfile",
                     "-File",
                     probePath,
                     "-PublisherPath",
                     publisherPath,
                     "-Lane",
                     lane,
                 })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var standardOutput = await output;
        var standardError = await error;

        Assert.True(
            process.ExitCode == 0,
            $"Publisher body probe failed. stdout={standardOutput}; stderr={standardError}");
        using var json = JsonDocument.Parse(standardOutput);
        return json.RootElement.GetProperty("body").GetString()
            ?? throw new InvalidOperationException("Publisher returned no issue body.");
    }

    private static string Hash(string value)
        => "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string RecurrencePayload(
        long runId,
        string commitSha,
        long handoffArtifactId)
        => string.Join(
            "\n",
            "",
            "## Recurrence",
            "",
            $"- Run: [#{runId} attempt {RunAttempt}](https://github.com/{Repository}/actions/runs/{runId}/attempts/{RunAttempt})",
            $"- Commit: `{commitSha}`",
            "- Category/platform: `test-failure` / `android`",
            $"- Test identity: `{TestIdentity}`",
            "- Evidence sufficiency: `sufficient`",
            $"- Artifact: [download](https://github.com/{Repository}/actions/runs/{runId}/artifacts/{handoffArtifactId})",
            "- Handoff entry: `sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc`",
            "- Downloaded ZIP: `sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd`",
            "",
            "Verify the retained ZIP with the local handoff command in the issue body before using it. This recurrence grants no repair authority.");

    private static string EvidenceArtifactPlatform(string platform)
        => platform == "macos" ? "macos-appkit" : platform;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private sealed record ResolverFixture(
        string IssueJsonPath,
        string RepositoryJsonPath,
        string RunJsonPath,
        string ArtifactsJsonPath,
        string CommentsJsonPath);

    private sealed record ResolverResult(
        int ExitCode,
        JsonElement Json,
        string StandardOutput,
        string StandardError);
}
