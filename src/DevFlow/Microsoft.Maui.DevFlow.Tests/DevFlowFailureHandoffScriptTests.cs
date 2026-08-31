using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed partial class DevFlowFailureHandoffScriptTests : IDisposable
{
    private const string Repository = "dotnet/maui-labs";
    private const string WorkflowName = "DevFlow Integration Tests";
    private const string WorkflowPath = ".github/workflows/devflow-integration.yml";
    private const string SourceEvent = "workflow_dispatch";
    private const string HeadRef = "main";
    private const string CommitSha = "0123456789abcdef0123456789abcdef01234567";
    private const long RunId = 123456789;
    private const int RunAttempt = 2;
    private const int PullRequestNumber = 0;
    private const string RawSentinel = "RAW-SOURCE-TEXT-MUST-NOT-LEAK";
    private static readonly string[] QualificationGateIds =
    [
        "android-device-overhead",
        "android-tier1-first-attempts",
        "confidence-calibration",
        "corpus-contract",
        "deterministic-host-performance",
        "independent-review",
        "preview-safety-flags",
        "privacy-security-escapes",
        "repair-precision",
        "required-evidence",
        "selector-stability",
        "zero-false-heals",
    ];

    private readonly string _repositoryRoot = FindRepositoryRoot();
    private readonly string _testRoot;

    public DevFlowFailureHandoffScriptTests()
    {
        _testRoot = Path.Combine(
            _repositoryRoot,
            "artifacts",
            "TestResults",
            "devflow-failure-handoff-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public async Task QualifiedPhysicalFailure_CreatesConsumerCompatibleArchive()
    {
        var inputs = CreateInputs("qualified");

        var result = await RunProducerAsync(inputs.ManifestPath, inputs.QualificationPath, "qualified");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("created", result.Json.GetProperty("status").GetString());
        var archivePath = ArchivePath("qualified");
        Assert.True(File.Exists(archivePath));
        Assert.Equal(
            ["handoff.json", "manifest.json"],
            Directory.GetFiles(StagingPath("qualified"))
                .Select(static path => Path.GetFileName(path)!)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray());

        using var archive = ZipFile.OpenRead(archivePath);
        Assert.Equal(
            ["manifest.json", "handoff.json"],
            archive.Entries.Select(static entry => entry.FullName).ToArray());
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(StagingPath("qualified"), "manifest.json")),
            ReadEntryBytes(archive, "manifest.json"));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(StagingPath("qualified"), "handoff.json")),
            ReadEntryBytes(archive, "handoff.json"));

        var manifestText = ReadEntry(archive, "manifest.json");
        var handoffText = ReadEntry(archive, "handoff.json");
        using var manifest = JsonDocument.Parse(manifestText);
        using var handoff = JsonDocument.Parse(handoffText);
        Assert.Equal("devflow-ci-failure-manifest", manifest.RootElement.GetProperty("schema").GetString());
        Assert.Equal("devflow-ci-failure-handoff", handoff.RootElement.GetProperty("schema").GetString());
        Assert.Equal(Repository, handoff.RootElement.GetProperty("provenance").GetProperty("repository").GetString());
        Assert.Equal(CommitSha, handoff.RootElement.GetProperty("provenance").GetProperty("commitSha").GetString());
        Assert.Equal(SourceEvent, handoff.RootElement.GetProperty("provenance").GetProperty("sourceEvent").GetString());
        Assert.Equal(Repository, handoff.RootElement.GetProperty("provenance").GetProperty("headRepository").GetString());
        Assert.Equal(
            $"sha256:{Hash(Encoding.UTF8.GetBytes(HeadRef))}",
            handoff.RootElement.GetProperty("provenance").GetProperty("headRefSha256").GetString());
        Assert.Equal("qualified", handoff.RootElement.GetProperty("qualification").GetString());
        Assert.Equal("failure", handoff.RootElement.GetProperty("outcome").GetString());

        var declaredHash = manifest.RootElement.GetProperty("entries")[0].GetProperty("sha256").GetString();
        Assert.Equal($"sha256:{Hash(Encoding.UTF8.GetBytes(handoffText))}", declaredHash);
    }

    [Fact]
    public async Task NotQualifiedEmulator_SkipsWithoutArchive()
    {
        var inputs = CreateInputs(
            "emulator",
            platform: "android",
            deviceKind: "emulator",
            realDevice: false,
            qualificationStatus: "not-qualified");

        var result = await RunProducerAsync(
            inputs.ManifestPath,
            inputs.QualificationPath,
            "emulator",
            laneKind: "android-emulator-pilot");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("qualification-not-qualified", result.Json.GetProperty("reason").GetString());
        Assert.False(File.Exists(ArchivePath("emulator")));
    }

    [Fact]
    public async Task PassingFirstAttempt_SkipsWithoutArchive()
    {
        var inputs = CreateInputs("pass", outcome: "passed");

        var result = await RunProducerAsync(inputs.ManifestPath, inputs.QualificationPath, "pass");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("source-pass", result.Json.GetProperty("reason").GetString());
        Assert.False(File.Exists(ArchivePath("pass")));
    }

    [Fact]
    public async Task MultipleFailureCandidates_SkipsAsAmbiguous()
    {
        var inputs = CreateInputs("ambiguous", failureCount: 2);

        var result = await RunProducerAsync(inputs.ManifestPath, inputs.QualificationPath, "ambiguous");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("ambiguous-incidents", result.Json.GetProperty("reason").GetString());
        Assert.False(File.Exists(ArchivePath("ambiguous")));
    }

    [Fact]
    public async Task MalformedSource_SkipsWithoutReadingOrCopyingRawContent()
    {
        var inputs = CreateInputs("malformed");
        File.WriteAllText(
            inputs.ManifestPath,
            $"{{\"message\":\"{RawSentinel}\"}}",
            new UTF8Encoding(false));

        var result = await RunProducerAsync(
            inputs.ManifestPath,
            inputs.QualificationPath,
            "malformed");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("source-manifest-schema-invalid", result.Json.GetProperty("reason").GetString());
        Assert.DoesNotContain(RawSentinel, result.StandardOutput, StringComparison.Ordinal);
        Assert.False(File.Exists(ArchivePath("malformed")));
    }

    [Fact]
    public async Task SourceProvenanceMismatch_SkipsWithoutArchive()
    {
        var inputs = CreateInputs("provenance", sourceCommit: new string('f', 40));

        var result = await RunProducerAsync(inputs.ManifestPath, inputs.QualificationPath, "provenance");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("source-provenance-mismatch", result.Json.GetProperty("reason").GetString());
        Assert.False(File.Exists(ArchivePath("provenance")));
    }

    [Fact]
    public async Task MissingExplicitOfficialCoverage_SkipsWithoutArchive()
    {
        var inputs = CreateInputs("missing-official");
        MutateJson(
            inputs.ManifestPath,
            root => root["platform"]!.AsObject().Remove("officialCoverage"));

        var result = await RunProducerAsync(
            inputs.ManifestPath,
            inputs.QualificationPath,
            "missing-official");

        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("source-platform-invalid", result.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ExperimentalPlatformCannotQualifyAsOfficial()
    {
        var inputs = CreateInputs("experimental-platform");
        MutateJson(
            inputs.ManifestPath,
            root => root["platform"]!["experimental"] = true);

        var result = await RunProducerAsync(
            inputs.ManifestPath,
            inputs.QualificationPath,
            "experimental-platform");

        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("source-platform-invalid", result.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ObjectMasqueradingAsArray_IsRejected()
    {
        var inputs = CreateInputs("object-array");
        MutateJson(inputs.ManifestPath, root => root["flows"] = new JsonObject());

        var result = await RunProducerAsync(
            inputs.ManifestPath,
            inputs.QualificationPath,
            "object-array");

        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("source-flows-invalid", result.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task FirstAttemptWithHiddenDifference_IsRejectedByCanonicalDigest()
    {
        var inputs = CreateInputs("attempt-tamper");
        MutateJson(
            inputs.ManifestPath,
            root => root["flows"]![0]!["firstAttempt"]!["unexpected"] = "tampered");

        var result = await RunProducerAsync(
            inputs.ManifestPath,
            inputs.QualificationPath,
            "attempt-tamper");

        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("source-first-attempt-not-immutable", result.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task FailureMixedWithUnknownCompletion_IsRejected()
    {
        var inputs = CreateInputs("mixed", failureCount: 2);
        MutateJson(
            inputs.ManifestPath,
            root =>
            {
                var flow = root["flows"]![1]!.AsObject();
                foreach (var attempt in new[]
                         {
                             flow["firstAttempt"]!.AsObject(),
                             flow["cleanAttempts"]![0]!.AsObject(),
                         })
                {
                    attempt["outcome"] = "unknown-completion";
                    attempt["failureClass"] = "unknown-completion";
                    attempt["failureCode"] = "unknown-completion";
                }
            });
        RewriteQualificationManifestDigest(inputs.ManifestPath, inputs.QualificationPath);

        var result = await RunProducerAsync(inputs.ManifestPath, inputs.QualificationPath, "mixed");

        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("mixed-unresolved-outcomes", result.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ForgedPassWithoutFullQualificationContract_IsRejected()
    {
        var inputs = CreateInputs("forged-qualification");
        File.WriteAllText(
            inputs.QualificationPath,
            """{"schema":1,"kind":"maui-preview-qualification","contractVersion":"preview-qualification-v1","status":"pass","platform":"android"}""",
            new UTF8Encoding(false));

        var result = await RunProducerAsync(
            inputs.ManifestPath,
            inputs.QualificationPath,
            "forged-qualification");

        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("qualification-schema-invalid", result.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task QualificationObjectMasqueradingAsGateArray_IsRejected()
    {
        var inputs = CreateInputs("qualification-object-array");
        MutateJson(inputs.QualificationPath, root => root["gates"] = new JsonObject());

        var result = await RunProducerAsync(
            inputs.ManifestPath,
            inputs.QualificationPath,
            "qualification-object-array");

        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("qualification-schema-invalid", result.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task QualificationBoundToDifferentManifest_IsRejected()
    {
        var inputs = CreateInputs("manifest-binding");
        MutateJson(
            inputs.QualificationPath,
            root => root["artifactRefs"]![0]!["digest"] = $"sha256:{new string('f', 64)}");

        var result = await RunProducerAsync(
            inputs.ManifestPath,
            inputs.QualificationPath,
            "manifest-binding");

        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("qualification-manifest-unbound", result.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task QualificationFingerprintMismatch_IsRejected()
    {
        var inputs = CreateInputs("fingerprint-mismatch");
        MutateJson(
            inputs.QualificationPath,
            root => root["fingerprints"]!["repositoryCommit"] = Digest("different-commit"));

        var result = await RunProducerAsync(
            inputs.ManifestPath,
            inputs.QualificationPath,
            "fingerprint-mismatch");

        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("qualification-fingerprints-mismatch", result.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task PassingReviewGateCannotHidePendingReview()
    {
        var inputs = CreateInputs("review-mismatch");
        MutateJson(
            inputs.QualificationPath,
            root => root["review"]!["independentReviewStatus"] = "pending");

        var result = await RunProducerAsync(
            inputs.ManifestPath,
            inputs.QualificationPath,
            "review-mismatch");

        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("qualification-review-invalid", result.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task PassingGateWithFailingMetric_IsRejected()
    {
        var inputs = CreateInputs("metric-forgery");
        MutateJson(
            inputs.QualificationPath,
            root =>
            {
                root["metrics"]!["falseHeals"]!["numerator"] = 1;
                root["metrics"]!["falseHeals"]!["value"] = 1d / 300d;
            });

        var result = await RunProducerAsync(
            inputs.ManifestPath,
            inputs.QualificationPath,
            "metric-forgery");

        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("qualification-metrics-invalid", result.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ForcedRedactedFlagWithoutSelectedEvidenceBinding_IsRejected()
    {
        var inputs = CreateInputs("forced-redacted");
        MutateJson(
            inputs.ManifestPath,
            root =>
            {
                var report = root["artifacts"]![0]!.AsObject();
                report["kind"] = "host-diagnostic";
                report["redacted"] = true;
            });
        RewriteQualificationManifestDigest(inputs.ManifestPath, inputs.QualificationPath);

        var result = await RunProducerAsync(
            inputs.ManifestPath,
            inputs.QualificationPath,
            "forced-redacted");

        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("source-evidence-insufficient", result.Json.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("artifact-enumeration")]
    [InlineData("artifact-inherited")]
    public async Task IncompleteArtifactInventoryOmission_IsRejected(string kind)
    {
        var inputs = CreateInputs($"incomplete-{kind}");
        MutateJson(
            inputs.ManifestPath,
            root =>
            {
                root["omissions"] = new JsonArray(
                    new JsonObject
                    {
                        ["kind"] = kind,
                        ["reason"] = "The finalizer could not verify the complete artifact inventory.",
                    });
            });
        RewriteQualificationManifestDigest(inputs.ManifestPath, inputs.QualificationPath);

        var result = await RunProducerAsync(
            inputs.ManifestPath,
            inputs.QualificationPath,
            $"incomplete-{kind}");

        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("source-evidence-insufficient", result.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task OutOfRangeArtifactSize_IsRejected()
    {
        var inputs = CreateInputs("artifact-size");
        MutateJson(
            inputs.ManifestPath,
            root => root["artifacts"]![0]!["sizeBytes"] = 1_099_511_627_777L);

        var result = await RunProducerAsync(
            inputs.ManifestPath,
            inputs.QualificationPath,
            "artifact-size");

        Assert.Equal("skipped", result.Json.GetProperty("status").GetString());
        Assert.Equal("source-artifacts-invalid", result.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task IdenticalEvidence_ProducesDeterministicArchiveBytesAndHashes()
    {
        var inputs = CreateInputs("deterministic");

        var first = await RunProducerAsync(inputs.ManifestPath, inputs.QualificationPath, "deterministic-one");
        var second = await RunProducerAsync(inputs.ManifestPath, inputs.QualificationPath, "deterministic-two");

        Assert.Equal("created", first.Json.GetProperty("status").GetString());
        Assert.Equal("created", second.Json.GetProperty("status").GetString());
        Assert.Equal(
            first.Json.GetProperty("archiveSha256").GetString(),
            second.Json.GetProperty("archiveSha256").GetString());
        Assert.Equal(
            first.Json.GetProperty("handoffSha256").GetString(),
            second.Json.GetProperty("handoffSha256").GetString());
        Assert.Equal(
            File.ReadAllBytes(ArchivePath("deterministic-one")),
            File.ReadAllBytes(ArchivePath("deterministic-two")));
    }

    [Fact]
    public async Task ArchiveAndResult_NeverLeakRawSourceText()
    {
        var inputs = CreateInputs("redaction", includeRawSourceText: true);

        var result = await RunProducerAsync(inputs.ManifestPath, inputs.QualificationPath, "redaction");

        Assert.Equal("created", result.Json.GetProperty("status").GetString());
        Assert.DoesNotContain(RawSentinel, result.StandardOutput, StringComparison.Ordinal);
        using var archive = ZipFile.OpenRead(ArchivePath("redaction"));
        var contents = string.Join(
            "\n",
            archive.Entries.Select(entry => ReadEntry(archive, entry.FullName)));
        Assert.DoesNotContain(RawSentinel, contents, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationWorkflow_WiresNoOpEmulatorProducerAndExactArtifactName()
    {
        var workflowPath = Path.Combine(_repositoryRoot, ".github", "workflows", "devflow-integration.yml");
        var workflow = File.ReadAllText(workflowPath);
        var yaml = new YamlStream();
        using (var reader = new StringReader(workflow))
            yaml.Load(reader);

        Assert.NotNull(Assert.Single(yaml.Documents).RootNode);
        Assert.Contains("New-DevFlowFailureHandoff.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("-LaneKind android-emulator-pilot", workflow, StringComparison.Ordinal);
        Assert.Contains("-SourceEvent $env:DEVFLOW_FAILURE_HANDOFF_SOURCE_EVENT", workflow, StringComparison.Ordinal);
        Assert.Contains("-HeadRepository $env:DEVFLOW_FAILURE_HANDOFF_HEAD_REPOSITORY", workflow, StringComparison.Ordinal);
        Assert.Contains("-HeadRef $env:DEVFLOW_FAILURE_HANDOFF_HEAD_REF", workflow, StringComparison.Ordinal);
        Assert.Contains("if: always()", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("reactivecircus/android-emulator-runner@v2", workflow, StringComparison.Ordinal);
        Assert.Equal(
            5,
            workflow.Split(
                    "reactivecircus/android-emulator-runner@a421e43855164a8197daf9d8d40fe71c6996bb0d # v2.38.0",
                    StringSplitOptions.None)
                .Length - 1);
        Assert.Contains(
            "name: devflow-failure-handoff-${{ github.run_id }}-${{ github.run_attempt }}",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "devflow-failure-handoff-${{ github.run_id }}-${{ github.run_attempt }}/manifest.json",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Upload canonical DevFlow failure handoff entries",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "devflow-failure-handoff-${{ github.run_id }}-${{ github.run_attempt }}/handoff.json",
            workflow,
            StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    private (string ManifestPath, string QualificationPath) CreateInputs(
        string directoryName,
        string platform = "android",
        string deviceKind = "physical-device",
        bool realDevice = true,
        string outcome = "failed",
        int failureCount = 1,
        string qualificationStatus = "pass",
        string sourceCommit = CommitSha,
        bool includeRawSourceText = false)
    {
        var directory = Path.Combine(_testRoot, directoryName);
        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, "manifest.json");
        var qualificationPath = Path.Combine(directory, "qualification.json");
        var flowCount = Math.Max(failureCount, 1);
        var buildFingerprint = Digest("app-build");
        var packageDigest = Digest("app-package");
        var deviceFingerprint = Digest("physical-device");
        var agentInstanceId = "agent-instance-1";
        var flowFixtures = Enumerable.Range(0, flowCount)
            .Select(index =>
            {
                var runId = $"flow-{index}-attempt-1";
                var reportPath =
                    $"artifacts/devflow/{RunId}-{RunAttempt}/{platform}/{runId}/flow-run.json";
                var tracePath =
                    $"artifacts/devflow/{RunId}-{RunAttempt}/{platform}/{runId}/failure.mauitrace";
                var reportDigest = Digest($"report-identity-{index}");
                var reportBytesDigest = Digest($"report-bytes-{index}");
                var traceBytesDigest = Digest($"trace-bytes-{index}");
                var firstAttempt = new
                {
                    runId,
                    runKind = "clean",
                    repetition = 1,
                    outcome,
                    verified = false,
                    reportPath,
                    reportDigest,
                    failureClass = outcome == "passed" ? null : "assertion-failed",
                    failureCode = outcome == "passed" ? null : "assertion-failed",
                    failurePhase = outcome == "passed" ? null : "assertion",
                    resetFingerprint = Digest("reset"),
                    seedFingerprint = Digest("seed"),
                    backendStateFingerprint = Digest("backend"),
                    appBuildFingerprint = buildFingerprint,
                    agentInstanceId,
                };
                var flow = new
                {
                    name = includeRawSourceText ? RawSentinel : "safe-name-not-emitted",
                    sourcePath = includeRawSourceText ? RawSentinel : "safe-path-not-emitted",
                    digest = Digest($"flow-{index}"),
                    tier = "tier-1",
                    firstAttempt,
                    cleanAttempts = new[] { firstAttempt },
                    diagnosticReruns = Array.Empty<object>(),
                };
                var artifacts = new object[]
                {
                    new
                    {
                        kind = "flow-run-report",
                        path = reportPath,
                        sha256 = reportBytesDigest,
                        sizeBytes = 123,
                        mediaType = "application/json",
                        redacted = true,
                        runId,
                    },
                    new
                    {
                        kind = "mauitrace",
                        path = tracePath,
                        sha256 = traceBytesDigest,
                        sizeBytes = 456,
                        mediaType = "application/vnd.maui.evidence+zip",
                        redacted = true,
                        runId,
                    },
                };
                return new FlowFixture(
                    flow,
                    (string)flow.digest,
                    runId,
                    reportPath,
                    reportDigest,
                    reportBytesDigest,
                    tracePath,
                    traceBytesDigest,
                    (string)firstAttempt.seedFingerprint,
                    (string)firstAttempt.backendStateFingerprint,
                    artifacts);
            })
            .ToArray();

        var manifest = new
        {
            schema = 1,
            kind = "devflow-flow-pilot",
            generatedAt = DateTimeOffset.UnixEpoch,
            repository = new { commit = sourceCommit },
            workflow = new { runId = $"{RunId}-{RunAttempt}", attempt = RunAttempt.ToString(), name = WorkflowName },
            testing = new { packageVersion = "1.2.3" },
            platform = new
            {
                name = platform,
                officialCoverage = true,
                experimental = false,
                deviceEvidence = new { kind = deviceKind, realDevice },
                deviceId = deviceFingerprint,
                deviceProfile = "physical-test-device",
                agentInstanceId,
                androidSdk = platform == "android" ? new { apiLevel = "35" } : null,
                runtime = platform == "android" ? null : "runtime-1",
            },
            app = new
            {
                packageId = "com.example.devflow",
                buildFingerprint,
                packageDigest,
            },
            validationErrors = Array.Empty<string>(),
            artifacts = flowFixtures.SelectMany(static fixture => fixture.Artifacts).ToArray(),
            flows = flowFixtures.Select(static fixture => fixture.Flow).ToArray(),
            privacy = new
            {
                excludedByDefault = new[] { "screenshots", "source", "raw-model-context" },
            },
            truncated = false,
            truncation = new { maxArtifacts = 256, omittedArtifacts = 0 },
            omissions = Array.Empty<object>(),
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest), new UTF8Encoding(false));
        WriteQualification(
            qualificationPath,
            manifestPath,
            platform,
            deviceKind,
            realDevice,
            sourceCommit,
            deviceFingerprint,
            buildFingerprint,
            packageDigest,
            flowFixtures,
            qualificationStatus);
        return (manifestPath, qualificationPath);
    }

    private static void WriteQualification(
        string path,
        string manifestPath,
        string platform,
        string deviceKind,
        bool realDevice,
        string repositoryCommit,
        string deviceFingerprint,
        string buildFingerprint,
        string packageDigest,
        IReadOnlyList<FlowFixture> flows,
        string status)
    {
        var gateStatus = status switch
        {
            "pass" => "pass",
            "fail" => "fail",
            _ => "not-qualified",
        };
        var gates = QualificationGateIds
            .Select((gate, index) => new
            {
                gateId = gate,
                status = index == 0 ? gateStatus : "pass",
                message = "Deterministic qualification gate result.",
                reasonCodes = index == 0 && gateStatus != "pass"
                    ? new[] { "qualification-not-passed" }
                    : Array.Empty<string>(),
                artifactRefs = Array.Empty<string>(),
            })
            .ToArray();
        var reportArtifacts = flows.SelectMany(flow => new object[]
        {
            new
            {
                kind = "report",
                digest = flow.ReportDigest,
                reference = Fingerprint(flow.ReportPath),
                redacted = true,
            },
            new
            {
                kind = "flow-run-report",
                digest = flow.ReportBytesDigest,
                reference = Fingerprint(flow.ReportPath),
                redacted = true,
            },
            new
            {
                kind = "mauitrace",
                digest = flow.TraceBytesDigest,
                reference = Fingerprint(flow.TracePath),
                redacted = true,
            },
        });
        var qualification = new
        {
            schema = 1,
            kind = "maui-preview-qualification",
            contractVersion = "preview-qualification-v1",
            generatedAt = DateTimeOffset.UnixEpoch,
            status,
            platform,
            fingerprints = new
            {
                corpusVersion = Digest("corpus-version"),
                corpusFingerprint = Digest("corpus"),
                repositoryCommit = Fingerprint(repositoryCommit),
                testingPackageVersion = Fingerprint("1.2.3"),
                packageId = Fingerprint("com.example.devflow"),
                packageFingerprint = packageDigest,
                toolVersion = Digest("tool-version"),
                toolFingerprint = Digest("tool"),
                policyVersion = Fingerprint("preview-qualification-policy-v1"),
                policyFingerprint = Digest("policy"),
            },
            profiles = new[]
            {
                new
                {
                    platform,
                    scope = Fingerprint("physical-device-flow-qa"),
                    deviceEvidenceKind = deviceKind,
                    realDevice,
                    deviceFingerprint,
                    runtimeFingerprint = Fingerprint(platform == "android" ? "35" : "runtime-1"),
                    buildFingerprint,
                    packageFingerprint = packageDigest,
                    seedFingerprint = Fingerprint(flows[0].SeedFingerprint),
                    backendStateFingerprint = Fingerprint(flows[0].BackendStateFingerprint),
                    firstAttemptMode = Fingerprint("manifest-first-attempt"),
                },
            },
            featureFlags = new
            {
                schema = 1,
                policyVersion = "preview-flags-v1",
                workbenchEnabled = false,
                agentAuthoringEnabled = false,
                repairProposalsEnabled = false,
                sourceProposalsEnabled = false,
                traceImportExportEnabled = false,
                autoApplyRepair = false,
                autoApplySource = false,
                modelProviderEnabled = false,
                telemetryEgressEnabled = false,
                requiredPullRequestGate = false,
                killSwitches = Array.Empty<string>(),
            },
            review = new
            {
                planId = Digest("plan"),
                planRevision = 1,
                planReviewStatus = "approved",
                rubberDuckReviewStatus = "approved",
                independentReviewStatus = "approved",
                reviewedAt = DateTimeOffset.UnixEpoch,
                reviewerFingerprints = new[] { Digest("reviewer") },
                artifactRefs = new[] { Digest("review-artifact") },
            },
            corpus = new
            {
                version = Digest("corpus-version"),
                manifestFingerprint = Digest("corpus"),
                staticOnly = true,
                manifestValid = true,
                caseSchemaValid = true,
                curatedCases = 100,
                generatedCases = 300,
                deviceBackedCases = 100,
                mutationSeed = 20260802,
                generatorVersion = Digest("generator"),
                errors = Array.Empty<string>(),
            },
            metrics = new
            {
                recordingValidity = Rate(100, 100, true),
                selectorStability = Rate(100, 100, true),
                repairPrecision = Rate(100, 100, false),
                repairRecall = Rate(100, 100, false),
                falseHeals = Rate(0, 300, false),
                abstention = Rate(300, 300, false),
                humanDecisionOutcomes = new
                {
                    approved = 1,
                    rejected = 0,
                    expired = 0,
                    abstained = 300,
                    unresolved = 0,
                },
                calibration = new
                {
                    state = "not-applicable",
                    probabilityLikeConfidenceDisplayed = false,
                    sampleCount = 0,
                    ece = (double?)null,
                    brier = (double?)null,
                    buckets = Array.Empty<object>(),
                },
                timeToDiagnosis = Duration("time-to-diagnosis"),
                traceReportSize = new
                {
                    state = "measured",
                    expectedReportCount = flows.Count,
                    reportPresent = flows.Count,
                    reportSchemaValid = flows.Count,
                    reportComplete = flows.Count,
                    reportCompleteness = 1d,
                    traceSampleCount = flows.Count,
                    reportP50Bytes = 123d,
                    reportP95Bytes = 123d,
                    traceP50Bytes = 456d,
                    traceP95Bytes = 456d,
                    missingReason = (string?)null,
                },
                runtimeOverhead = new
                {
                    hostOperations = new[] { Duration("parse") },
                    deviceOverhead = Duration("android-device-overhead"),
                },
                flakeFirstAttemptStability = new
                {
                    state = "measured",
                    stability = Rate(99 * flows.Count, 100 * flows.Count, true),
                    flows = flows.Select(flow => new
                    {
                        flowId = flow.FlowDigest,
                        cleanFirstAttempts = 100,
                        passedFirstAttempts = 99,
                        stability = 0.99d,
                        realDeviceEvidence = true,
                    }).ToArray(),
                    diagnosticRerunsIgnored = 0,
                    infrastructureExclusions = Array.Empty<object>(),
                },
                privacySecurityEscapes = new
                {
                    state = "measured",
                    testCount = 18,
                    escapeCount = 0,
                    canaryScanPassed = true,
                    caseIds = new[] { Digest("security-case") },
                    missingReason = (string?)null,
                },
            },
            thresholds = new
            {
                policyVersion = Fingerprint("preview-qualification-policy-v1"),
                confidenceLevel = 0.95d,
                minimumRepairPrecision = 0.95d,
                minimumRepairEvaluations = 100,
                minimumNoRepairEvaluations = 300,
                maximumFalseHeals = 0,
                minimumSelectorStability = 0.99d,
                minimumSelectorObservations = 100,
                maximumCalibrationEce = 0.05d,
                minimumCleanFirstAttemptsPerTier1Flow = 100,
                minimumFirstAttemptStability = 0.99d,
                hostOperationP95BudgetMs = 250d,
                requireRealAndroidDeviceEvidence = true,
                requireRecordedReviews = true,
            },
            gates,
            reasons = gateStatus == "pass"
                ? Array.Empty<object>()
                : new object[]
                {
                    new
                    {
                        code = "qualification-not-passed",
                        severity = gateStatus == "fail" ? "error" : "warning",
                        message = "Qualification did not pass.",
                    },
                },
            artifactRefs = new object[]
                {
                    new
                    {
                        kind = "flow-pilot-manifest",
                        digest = $"sha256:{Hash(File.ReadAllBytes(manifestPath))}",
                        reference = "unknown",
                        redacted = true,
                    },
                }
                .Concat(reportArtifacts)
                .ToArray(),
            exclusions = Array.Empty<object>(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(qualification), new UTF8Encoding(false));
    }

    private static object Rate(int numerator, int denominator, bool independentDeviceRuns)
    {
        var value = denominator == 0 ? 0d : numerator / (double)denominator;
        var interval = Wilson(numerator, denominator);
        return new
        {
            state = denominator == 0 ? "missing" : "measured",
            numerator,
            denominator,
            value,
            confidenceInterval = new
            {
                method = "wilson-95",
                confidenceLevel = 0.95d,
                lower = interval.Lower,
                upper = interval.Upper,
            },
            sampleSources = independentDeviceRuns ? new[] { "device-backed" } : new[] { "curated" },
            independentDeviceRuns,
            exclusions = Array.Empty<object>(),
        };
    }

    private static (double Lower, double Upper) Wilson(int successes, int trials)
    {
        if (trials == 0)
            return (0, 0);

        const double z = 1.959963984540054;
        var p = successes / (double)trials;
        var z2 = z * z;
        var denominator = 1 + (z2 / trials);
        var center = (p + (z2 / (2 * trials))) / denominator;
        var margin = z * Math.Sqrt(
            (p * (1 - p) / trials) + (z2 / (4d * trials * trials))) / denominator;
        return (Math.Clamp(center - margin, 0, 1), Math.Clamp(center + margin, 0, 1));
    }

    private static object Duration(string operation) => new
    {
        state = "measured",
        operation,
        sampleCount = 10,
        p50Ms = 1d,
        p95Ms = 2d,
        maxMs = 3d,
        missingReason = (string?)null,
    };

    private static void MutateJson(string path, Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        mutation(root);
        File.WriteAllText(path, root.ToJsonString(), new UTF8Encoding(false));
    }

    private static void RewriteQualificationManifestDigest(
        string manifestPath,
        string qualificationPath)
    {
        MutateJson(
            qualificationPath,
            root =>
            {
                var manifestReference = root["artifactRefs"]!
                    .AsArray()
                    .Select(static node => node!.AsObject())
                    .Single(reference => reference["kind"]!.GetValue<string>() == "flow-pilot-manifest");
                manifestReference["digest"] = $"sha256:{Hash(File.ReadAllBytes(manifestPath))}";
            });
    }

    private static string Digest(string value) =>
        $"sha256:{Hash(Encoding.UTF8.GetBytes(value))}";

    private static string Fingerprint(string value) =>
        value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value.Length == 71
            ? value.ToLowerInvariant()
            : Digest(value);

    private sealed record FlowFixture(
        object Flow,
        string FlowDigest,
        string RunId,
        string ReportPath,
        string ReportDigest,
        string ReportBytesDigest,
        string TracePath,
        string TraceBytesDigest,
        string SeedFingerprint,
        string BackendStateFingerprint,
        object[] Artifacts);

    private async Task<(int ExitCode, string StandardOutput, JsonElement Json)> RunProducerAsync(
        string manifestPath,
        string qualificationPath,
        string outputName,
        string laneKind = "physical-device-flow-qa")
    {
        var outputDirectory = Path.Combine(_testRoot, outputName, "output");
        var script = Path.Combine(_repositoryRoot, "eng", "devflow", "New-DevFlowFailureHandoff.ps1");
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
        AddArgument("-SourceManifestPath");
        AddArgument(manifestPath);
        AddArgument("-QualificationPath");
        AddArgument(qualificationPath);
        AddArgument("-OutputDirectory");
        AddArgument(outputDirectory);
        AddArgument("-Repository");
        AddArgument(Repository);
        AddArgument("-WorkflowName");
        AddArgument(WorkflowName);
        AddArgument("-WorkflowPath");
        AddArgument(WorkflowPath);
        AddArgument("-SourceEvent");
        AddArgument(SourceEvent);
        AddArgument("-HeadRepository");
        AddArgument(Repository);
        AddArgument("-HeadRef");
        AddArgument(HeadRef);
        AddArgument("-RunId");
        AddArgument(RunId.ToString());
        AddArgument("-RunAttempt");
        AddArgument(RunAttempt.ToString());
        AddArgument("-CommitSha");
        AddArgument(CommitSha);
        AddArgument("-PullRequestNumber");
        AddArgument(PullRequestNumber.ToString());
        AddArgument("-LaneKind");
        AddArgument(laneKind);

        process.Start();
        // Both pipes are drained at once. Reading one to the end first deadlocks as soon as the
        // other fills its buffer, which is exactly what a chatty failure does.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var jsonLine = stdout.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(static line => line.StartsWith("{", StringComparison.Ordinal));
        Assert.True(jsonLine is not null, $"No JSON result. stdout={stdout}; stderr={stderr}");
        using var document = JsonDocument.Parse(jsonLine);
        return (process.ExitCode, stdout, document.RootElement.Clone());

        void AddArgument(string value) => process.StartInfo.ArgumentList.Add(value);
    }

    private string ArchivePath(string outputName) =>
        Path.Combine(
            _testRoot,
            outputName,
            "output",
            $"devflow-failure-handoff-{RunId}-{RunAttempt}.zip");

    private string StagingPath(string outputName) =>
        Path.Combine(
            _testRoot,
            outputName,
            "output",
            $"devflow-failure-handoff-{RunId}-{RunAttempt}");

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open(), new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    private static byte[] ReadEntryBytes(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        Assert.NotNull(entry);
        using var stream = entry!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the maui-labs repository root.");
    }
}
