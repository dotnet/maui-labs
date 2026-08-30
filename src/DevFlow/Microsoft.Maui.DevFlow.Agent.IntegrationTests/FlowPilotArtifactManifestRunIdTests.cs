using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

/// <summary>
/// The producer and <c>eng/devflow/Finalize-DevFlowFlowPilotManifest.ps1</c> write the same
/// <c>artifacts[]</c> entries for the same tree, and the finalizer re-derives every run id it
/// publishes. A rule the two disagreed on would make the finalized manifest contradict the one the
/// test process published for the same files, and a consumer that ties a report to an attempt
/// cannot tell which of them to believe.
/// </summary>
public sealed class FlowPilotArtifactManifestRunIdTests : IDisposable
{
    readonly string _root = Path.Combine(
        Path.GetTempPath(), "devflow-run-id-tests", Guid.NewGuid().ToString("N"));

    public FlowPilotArtifactManifestRunIdTests() => Directory.CreateDirectory(_root);

    /// <summary>
    /// The shipped layout puts run-independent evidence in fixed directories directly below the
    /// artifact root. Crediting "host-diagnostics/summary.json" to a run named "host-diagnostics"
    /// invents an attempt no consumer can match to anything in the manifest.
    /// </summary>
    [Fact]
    public void DirectRootChild_IsNotCreditedToARunNamedAfterItsDirectory()
    {
        var artifactRoot = Path.Combine(_root, "artifacts");
        var manifest = Create(artifactRoot);
        var report = Report(artifactRoot, "run-1");
        manifest.RecordCleanAttempt(Flow(), SourcePath(), 1, report);

        manifest.RecordArtifact(WriteArtifact(artifactRoot, Path.Combine("host-diagnostics", "summary.json")));
        manifest.RecordArtifact(WriteArtifact(artifactRoot, Path.Combine("run-1", "failure.mauitrace")));
        manifest.RecordArtifact(WriteArtifact(artifactRoot, "top-level.json"));

        var artifacts = WriteAndRead(manifest);

        Assert.Null(RunIdFor(artifacts, "host-diagnostics/summary.json"));
        Assert.Null(RunIdFor(artifacts, "top-level.json"));
        Assert.Equal("run-1", RunIdFor(artifacts, "run-1/failure.mauitrace"));
    }

    /// <summary>
    /// A directory name is not evidence. It is published as a run id only when it is a bounded,
    /// plain identifier, which is the same rule the finalizer applies before it republishes one.
    /// </summary>
    [Theory]
    [InlineData("..evil")]
    [InlineData("-leading-dash")]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    public void RunIdShapedDirectory_IsRefusedWhenItIsNotAPlainIdentifier(string directory)
    {
        var artifactRoot = Path.Combine(_root, "artifacts");
        var manifest = Create(artifactRoot);
        manifest.RecordCleanAttempt(Flow(), SourcePath(), 1, Report(artifactRoot, directory));
        manifest.RecordArtifact(WriteArtifact(artifactRoot, Path.Combine(directory, "failure.mauitrace")));

        var artifacts = WriteAndRead(manifest);

        Assert.Null(RunIdFor(artifacts, $"{directory.Replace('\\', '/')}/failure.mauitrace"));
    }

    [Fact]
    public void RunIdLongerThanTheBound_IsRefused()
    {
        var artifactRoot = Path.Combine(_root, "artifacts");
        var oversized = new string('r', 97);
        var manifest = Create(artifactRoot);
        manifest.RecordCleanAttempt(Flow(), SourcePath(), 1, Report(artifactRoot, oversized));
        manifest.RecordArtifact(WriteArtifact(artifactRoot, Path.Combine(oversized, "failure.mauitrace")));

        var artifacts = WriteAndRead(manifest);

        Assert.Null(RunIdFor(artifacts, $"{oversized}/failure.mauitrace"));

        // The bound itself, not the length of this particular name: one character shorter is
        // published exactly as recorded.
        var accepted = new string('r', 96);
        var second = Create(Path.Combine(_root, "artifacts-accepted"));
        second.RecordCleanAttempt(Flow(), SourcePath(), 1, Report(Path.Combine(_root, "artifacts-accepted"), accepted));
        second.RecordArtifact(
            WriteArtifact(Path.Combine(_root, "artifacts-accepted"), Path.Combine(accepted, "failure.mauitrace")));
        Assert.Equal(accepted, RunIdFor(WriteAndRead(second), $"{accepted}/failure.mauitrace"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    static string? RunIdFor(IReadOnlyList<JsonElement> artifacts, string path)
    {
        var artifact = artifacts.Single(candidate =>
            candidate.GetProperty("path").GetString()!.EndsWith(path, StringComparison.Ordinal));
        return artifact.TryGetProperty("runId", out var runId) ? runId.GetString() : null;
    }

    static IReadOnlyList<JsonElement> WriteAndRead(FlowPilotArtifactManifest manifest)
    {
        var write = manifest.WriteAtomic();
        Assert.True(write.Ok, write.Error);
        using var document = JsonDocument.Parse(File.ReadAllText(write.Path!));
        return document.RootElement.GetProperty("artifacts").EnumerateArray()
            .Select(static artifact => artifact.Clone())
            .ToArray();
    }

    static MauiFlowArtifactReference WriteArtifact(string artifactRoot, string relativePath)
    {
        var path = Path.Combine(artifactRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        return new MauiFlowArtifactReference
        {
            Kind = "host-diagnostics",
            Path = path,
            MediaType = "application/json",
            Redacted = true,
        };
    }

    FlowPilotArtifactManifest Create(string artifactRoot)
        => FlowPilotArtifactManifest.Create(new FlowPilotManifestOptions
        {
            ArtifactRoot = artifactRoot,
            RepositoryRoot = _root,
            Platform = "android",
            PackageId = "com.companyname.mauitodo",
            RepositoryCommit = "0123456789abcdef0123456789abcdef01234567",
            WorkflowRunId = "run-id-tests",
            AndroidApiLevel = "35",
            DeviceEvidenceKind = "emulator",
            RealDevice = false,
        });

    static MauiFlow Flow() => new()
    {
        Name = "run-id-flow",
        Steps =
        [
            new FlowStep
            {
                Seq = 1,
                Action = FlowActions.Assert,
                Asserts =
                [
                    new FlowAssert
                    {
                        Kind = "exists",
                        Selector = new FlowSelector { AutomationId = "AddButton" },
                        Verify = true,
                    },
                ],
            },
        ],
    };

    static MauiFlowRunReport Report(string artifactRoot, string runId)
    {
        var runDirectory = Path.Combine(artifactRoot, runId);
        Directory.CreateDirectory(runDirectory);
        var reportPath = Path.Combine(runDirectory, "flow-run.json");
        File.WriteAllText(reportPath, $"{{\"runId\":\"{runId}\"}}");

        return new MauiFlowRunReport
        {
            RunId = runId,
            FlowDigest = "run-id-flow-digest",
            ReportPath = reportPath,
            ReportDigest = "sha256:run-id-report",
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.Passed,
                Terminal = true,
                Verified = true,
            },
            Target = new MauiFlowRunTarget
            {
                Platform = "android",
                AppId = "com.companyname.mauitodo",
                AppBuildFingerprint = "sha256:apk",
                DeviceId = "emulator-5554",
                DeviceProfile = "1080x1920",
                AgentInstanceId = "agent-1",
            },
            Reset = new MauiFlowResetResult
            {
                ResetIdentity = "android-pm-clear-v1",
                SeedFingerprint = "seed",
                BackendStateFingerprint = "backend",
            },
        };
    }

    static string SourcePath()
        => Path.Combine(
            AppFixtureBase.FindRepoRoot(),
            "samples",
            "DevFlow.Sample",
            "maui-tests",
            "native-baseline.md");
}
