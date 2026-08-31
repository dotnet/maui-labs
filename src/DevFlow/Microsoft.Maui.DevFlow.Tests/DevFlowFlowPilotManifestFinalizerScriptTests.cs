using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Covers <c>eng/devflow/Finalize-DevFlowFlowPilotManifest.ps1</c>. The finalizer is the last hand
/// to touch a flow-pilot manifest before CI publishes it, so what it refuses to record matters as
/// much as what it records: a consumer resolves every artifact path against its own checkout.
/// </summary>
public sealed class DevFlowFlowPilotManifestFinalizerScriptTests : IDisposable
{
    private const string RepositoryCommit = "0123456789abcdef0123456789abcdef01234567";

    private readonly string _repositoryRoot = FindRepositoryRoot();
    private readonly string _testRoot;

    public DevFlowFlowPilotManifestFinalizerScriptTests()
    {
        _testRoot = Path.Combine(
            _repositoryRoot,
            "artifacts",
            "TestResults",
            "devflow-finalizer-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public async Task RunId_IsRecordedOnlyForADirectoryThatNamesAKnownRun()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        WriteFile(Path.Combine(artifactRoot, "top-level.json"), "{}");
        WriteFile(
            Path.Combine(artifactRoot, "flow-1-attempt-1", "flow-run.json"),
            """{"runId":"flow-1-attempt-1"}""");
        WriteFile(
            Path.Combine(artifactRoot, "flow-1-attempt-1", "host-diagnostics", "failure.mauitrace"),
            "trace");
        // The shipped layout: run-independent host evidence sits directly below the artifact root.
        WriteFile(Path.Combine(artifactRoot, "host-diagnostics", "summary.json"), """{"schema":1}""");

        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        File.WriteAllText(
            manifestPath,
            """
            {
              "schema": 1,
              "kind": "devflow-flow-pilot",
              "flows": [
                { "name": "flow-1", "firstAttempt": { "runId": "flow-1-attempt-1", "outcome": "passed" } }
              ],
              "artifacts": []
            }
            """);

        var result = await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot);
        Assert.Equal(0, result.ExitCode);

        var artifacts = ReadArtifacts(manifestPath);
        Assert.Equal(
            "flow-1-attempt-1",
            RunIdFor(artifacts, "artifacts-root/flow-1-attempt-1/flow-run.json"));
        Assert.Equal(
            "flow-1-attempt-1",
            RunIdFor(artifacts, "artifacts-root/flow-1-attempt-1/host-diagnostics/failure.mauitrace"));
        // "host-diagnostics" is a directory, not a run. Naming it as one invents an attempt that
        // no consumer can match to anything in the manifest.
        Assert.Null(RunIdFor(artifacts, "artifacts-root/host-diagnostics/summary.json"));
        Assert.Null(RunIdFor(artifacts, "artifacts-root/top-level.json"));
    }

    /// <summary>
    /// An infrastructure failure can leave a run directory the manifest never recorded an attempt
    /// for. The report inside it names its own run, so that directory is still run-scoped.
    /// </summary>
    [Fact]
    public async Task RunId_IsAlsoTakenFromADiscoveredReportThatNamesItsOwnRun()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        WriteFile(
            Path.Combine(artifactRoot, "flow-2-attempt-1", "flow-run.json"),
            """{"runId":"flow-2-attempt-1"}""");
        WriteFile(Path.Combine(artifactRoot, "flow-2-attempt-1", "failure.mauitrace"), "trace");
        WriteFile(Path.Combine(artifactRoot, "unrelated", "notes.txt"), "notes");

        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        var result = await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot);
        Assert.Equal(0, result.ExitCode);

        var artifacts = ReadArtifacts(manifestPath);
        Assert.Equal(
            "flow-2-attempt-1",
            RunIdFor(artifacts, "artifacts-root/flow-2-attempt-1/failure.mauitrace"));
        Assert.Null(RunIdFor(artifacts, "artifacts-root/unrelated/notes.txt"));
    }

    /// <summary>
    /// The manifest is finalized more than once against the same tree, and the files it describes
    /// are rewritten in between - the flow-run report and the host diagnostic summary always are.
    /// A digest inherited from the earlier pass describes bytes a consumer can no longer read, and
    /// a consumer that verifies the manifest refuses the whole run over it. The size has to move
    /// with the digest: a hash of the new bytes published beside the old length is a pair nothing
    /// on disk matches.
    /// </summary>
    [Fact]
    public async Task SecondPass_RepublishesNoDigestFromTheFirstOne()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        var flowRun = Path.Combine(artifactRoot, "flow-1-attempt-1", "flow-run.json");
        var hostSummary = Path.Combine(artifactRoot, "host-diagnostics", "summary.json");
        WriteFile(flowRun, """{"runId":"flow-1-attempt-1","status":"running"}""");
        WriteFile(hostSummary, """{"schema":1,"status":"running"}""");
        WriteFile(Path.Combine(artifactRoot, "flow-1-attempt-1", "failure.mauitrace"), "trace");

        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        Assert.Equal(0, (await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot)).ExitCode);
        var firstPass = ReadArtifacts(manifestPath).ToDictionary(
            static artifact => artifact.GetProperty("path").GetString()!,
            static artifact => (
                Sha256: artifact.GetProperty("sha256").GetString()!,
                SizeBytes: artifact.GetProperty("sizeBytes").GetInt64()));

        // Both rewritten files change length as well as content, which is what a second pass over a
        // live run actually does.
        File.WriteAllText(
            flowRun,
            """{"runId":"flow-1-attempt-1","status":"failed","failure":{"class":"assertion-failed"}}""",
            new UTF8Encoding(false));
        File.WriteAllText(hostSummary, """{"schema":1}""", new UTF8Encoding(false));

        Assert.Equal(0, (await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot)).ExitCode);
        var artifacts = ReadArtifacts(manifestPath);

        AssertEveryDigestMatchesTheBytesOnDisk(artifacts);
        foreach (var path in new[]
                 {
                     "artifacts-root/flow-1-attempt-1/flow-run.json",
                     "artifacts-root/host-diagnostics/summary.json",
                 })
        {
            Assert.NotEqual(firstPass[path].Sha256, DigestFor(artifacts, path));
            Assert.NotEqual(firstPass[path].SizeBytes, SizeFor(artifacts, path));
        }

        // Re-hashing must not duplicate the reference it replaced.
        Assert.Equal(
            artifacts.Count,
            artifacts.Select(static artifact => artifact.GetProperty("path").GetString()).Distinct().Count());
    }

    /// <summary>
    /// The same defect reached from the other side: the test process records the digests it saw,
    /// the shell rewrites those files, and the finalizer runs once. Only re-hashing from the live
    /// file keeps the published manifest true - and the producer's own account of the evidence,
    /// which this script never watched being written, has to survive that refresh.
    /// </summary>
    [Fact]
    public async Task InheritedDigestForALiveFile_IsReplacedRatherThanRepublished()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        var flowRun = Path.Combine(artifactRoot, "run-1", "flow-run.json");
        WriteFile(flowRun, """{"runId":"run-1","status":"failed"}""");
        var unredacted = Path.Combine(artifactRoot, "run-1", "raw-console.log");
        WriteFile(unredacted, "console output the producer declared unredacted");

        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        File.WriteAllText(
            manifestPath,
            """
            {
              "schema": 1,
              "kind": "devflow-flow-pilot",
              "flows": [
                { "name": "flow-1", "firstAttempt": { "runId": "run-1", "outcome": "failed" } }
              ],
              "artifacts": [
                {
                  "kind": "flow-run-report",
                  "path": "artifacts-root/run-1/flow-run.json",
                  "sha256": "sha256:0000000000000000000000000000000000000000000000000000000000000000",
                  "sizeBytes": 1,
                  "redacted": true
                },
                {
                  "kind": "android-host-diagnostics",
                  "path": "artifacts-root/run-1/raw-console.log",
                  "sha256": "sha256:0000000000000000000000000000000000000000000000000000000000000000",
                  "sizeBytes": 1,
                  "mediaType": "text/plain",
                  "redacted": false,
                  "runId": "a-run-nothing-recorded"
                }
              ]
            }
            """);

        var result = await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot);
        Assert.Equal(0, result.ExitCode);

        var artifacts = ReadArtifacts(manifestPath);
        var reference = Assert.Single(
            artifacts,
            artifact => artifact.GetProperty("path").GetString() == "artifacts-root/run-1/flow-run.json");
        Assert.Equal(Sha256Of(flowRun), reference.GetProperty("sha256").GetString());
        Assert.Equal(new FileInfo(flowRun).Length, reference.GetProperty("sizeBytes").GetInt64());
        Assert.Equal("run-1", reference.GetProperty("runId").GetString());

        // The producer said these bytes were not redacted and named their kind and media type.
        // Restating them as redacted would publish a claim about evidence this script never saw
        // being written; the run id it could not resolve is the one fact it does replace.
        var declared = Assert.Single(
            artifacts,
            artifact => artifact.GetProperty("path").GetString() == "artifacts-root/run-1/raw-console.log");
        Assert.False(declared.GetProperty("redacted").GetBoolean());
        Assert.Equal("android-host-diagnostics", declared.GetProperty("kind").GetString());
        Assert.Equal("text/plain", declared.GetProperty("mediaType").GetString());
        Assert.Equal("run-1", declared.GetProperty("runId").GetString());
        AssertEveryDigestMatchesTheBytesOnDisk(artifacts);
    }

    [Fact]
    public async Task InheritedRunIdWithDifferentCasing_IsDroppedWhenTheLivePathIsNotRunScoped()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        var diagnostic = Path.Combine(artifactRoot, "host-diagnostics", "summary.json");
        WriteFile(diagnostic, """{"schema":1}""");

        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        File.WriteAllText(
            manifestPath,
            """
            {
              "schema": 1,
              "kind": "devflow-flow-pilot",
              "flows": [],
              "artifacts": [
                {
                  "kind": "host-diagnostic",
                  "path": "artifacts-root/host-diagnostics/summary.json",
                  "sha256": "sha256:0000000000000000000000000000000000000000000000000000000000000000",
                  "sizeBytes": 1,
                  "redacted": true,
                  "RunId": "forged-run"
                }
              ]
            }
            """);

        var result = await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot);
        Assert.Equal(0, result.ExitCode);

        var reference = Assert.Single(
            ReadArtifacts(manifestPath),
            artifact => artifact.GetProperty("path").GetString() ==
                "artifacts-root/host-diagnostics/summary.json");
        Assert.DoesNotContain(
            reference.EnumerateObject(),
            property => string.Equals(property.Name, "runId", StringComparison.OrdinalIgnoreCase));
        AssertEveryDigestMatchesTheBytesOnDisk([reference]);
    }

    /// <summary>
    /// A run id is republished as evidence and read back as a directory name, so an unbounded or
    /// path-shaped one is refused rather than sanitized into a value that no longer names the run
    /// it came from. The report is the untrusted side of this: it is a file, and this script did
    /// not write it.
    /// </summary>
    [Fact]
    public async Task RunId_FromADiscoveredReport_IsRefusedWhenItIsNotABoundedIdentifier()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        var oversized = new string('r', 97);
        WriteFile(
            Path.Combine(artifactRoot, oversized, "flow-run.json"),
            $$"""{"runId":"{{oversized}}"}""");
        WriteFile(Path.Combine(artifactRoot, oversized, "failure.mauitrace"), "trace");
        WriteFile(
            Path.Combine(artifactRoot, "..evil", "flow-run.json"),
            """{"runId":"..evil"}""");
        WriteFile(Path.Combine(artifactRoot, "..evil", "notes.txt"), "notes");
        WriteFile(
            Path.Combine(artifactRoot, "run-ok", "flow-run.json"),
            """{"runId":"run-ok"}""");
        WriteFile(Path.Combine(artifactRoot, "run-ok", "kept.mauitrace"), "trace");

        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        Assert.Equal(0, (await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot)).ExitCode);

        var artifacts = ReadArtifacts(manifestPath);
        Assert.Null(RunIdFor(artifacts, $"artifacts-root/{oversized}/failure.mauitrace"));
        Assert.Null(RunIdFor(artifacts, "artifacts-root/..evil/notes.txt"));
        Assert.Equal("run-ok", RunIdFor(artifacts, "artifacts-root/run-ok/kept.mauitrace"));
    }

    /// <summary>
    /// Finalizing the same tree again must restate this script's own accounting, not add to it.
    /// Appending meant one dropped reference was reported once per finalization and the omission
    /// list grew with every pass, so the counts a consumer reads described the number of runs
    /// rather than the number of omissions.
    /// </summary>
    [Fact]
    public async Task RepeatedFinalization_DoesNotGrowItsOwnOmissionsOrCounts()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        for (var index = 0; index < 260; index++)
            WriteFile(Path.Combine(artifactRoot, "run-1", $"file-{index:D3}.json"), "{}");

        // The test process records omissions of the same kinds about references that never reached
        // disk. This script cannot rediscover those, so it must not treat them as its own.
        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        File.WriteAllText(
            manifestPath,
            """
            {
              "schema": 1,
              "kind": "devflow-flow-pilot",
              "artifacts": [],
              "truncated": true,
              "truncation": { "maxArtifacts": 256, "omittedArtifacts": 4 },
              "omissions": [
                {
                  "kind": "artifact-limit",
                  "reason": "Only the first 256 artifact references were hashed.",
                  "omittedArtifacts": 4
                },
                {
                  "kind": "artifact-path",
                  "reason": "An artifact reference did not include a path."
                }
              ]
            }
            """);

        Assert.Equal(0, (await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot)).ExitCode);
        var first = ReadArtifactAccounting(manifestPath);

        Assert.Equal(0, (await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot)).ExitCode);
        var second = ReadArtifactAccounting(manifestPath);

        Assert.Equal(first.Omissions, second.Omissions);
        Assert.Equal(first.OmittedArtifacts, second.OmittedArtifacts);
        Assert.Equal(first.RecordedArtifacts, second.RecordedArtifacts);

        // One from the test process, one from this pass - and still exactly that after the second
        // finalization, rather than one more per run.
        Assert.Equal(2, second.Omissions.Count(static kind => kind == "artifact-limit"));
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var omissions = document.RootElement.GetProperty("omissions").EnumerateArray().ToArray();
        Assert.Contains(
            omissions,
            omission => omission.GetProperty("reason").GetString() == "An artifact reference did not include a path.");
        Assert.Single(
            omissions,
            omission => omission.TryGetProperty("source", out var source) &&
                source.GetString() == "finalizer" &&
                omission.GetProperty("kind").GetString() == "artifact-limit");
    }

    /// <summary>
    /// A reference this pass cannot see is the one case where the recorded digest is all there is.
    /// It is kept, because dropping it would erase evidence a consumer may hold, and it is
    /// declared unverified rather than presented as checked.
    /// </summary>
    [Fact]
    public async Task InheritedReferenceWithNoLiveFile_IsRetainedAndReportedUnverified()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        WriteFile(Path.Combine(artifactRoot, "run-1", "flow-run.json"), """{"runId":"run-1"}""");

        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        File.WriteAllText(
            manifestPath,
            """
            {
              "schema": 1,
              "kind": "devflow-flow-pilot",
              "artifacts": [
                {
                  "kind": "mauitrace",
                  "path": "elsewhere/imported.mauitrace",
                  "sha256": "sha256:1111111111111111111111111111111111111111111111111111111111111111",
                  "sizeBytes": 7,
                  "redacted": true
                }
              ]
            }
            """);

        var result = await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot);
        Assert.Equal(0, result.ExitCode);

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var retained = document.RootElement.GetProperty("artifacts").EnumerateArray()
            .Single(static artifact => artifact.GetProperty("path").GetString() == "elsewhere/imported.mauitrace");
        Assert.Equal(
            "sha256:1111111111111111111111111111111111111111111111111111111111111111",
            retained.GetProperty("sha256").GetString());
        var omission = document.RootElement.GetProperty("omissions").EnumerateArray()
            .Single(static item => item.GetProperty("kind").GetString() == "artifact-inherited");
        Assert.Equal(1, omission.GetProperty("unverifiedArtifacts").GetInt32());
    }

    /// <summary>
    /// Every field this script reads out of the manifest, and out of any <c>flow-run.json</c> it
    /// discovers, was written by another process. Under strict mode, indexing a scalar or an array
    /// with a name is a terminating error, so a merely odd file used to end finalization - which
    /// replaces the whole pilot manifest with a generic one. A malformed field is skipped instead.
    /// </summary>
    [Fact]
    public async Task MalformedManifestAndReportShapes_DoNotEndFinalization()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        WriteFile(Path.Combine(artifactRoot, "run-1", "flow-run.json"), """{"runId":"run-1","hostQa":"not-an-object"}""");
        WriteFile(Path.Combine(artifactRoot, "odd", "flow-run.json"), """["not","an","object"]""");
        WriteFile(Path.Combine(artifactRoot, "run-1", "failure.mauitrace"), "trace");

        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        File.WriteAllText(
            manifestPath,
            """
            {
              "schema": 1,
              "kind": "devflow-flow-pilot",
              "flows": ["not-an-object", { "name": "flow-1", "firstAttempt": "not-an-object" }],
              "truncation": 7,
              "artifacts": ["not-an-object"],
              "omissions": ["not-an-object"]
            }
            """);

        var result = await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot);
        Assert.Equal(0, result.ExitCode);

        var artifacts = ReadArtifacts(manifestPath);
        AssertEveryDigestMatchesTheBytesOnDisk(artifacts);
        Assert.Equal("run-1", RunIdFor(artifacts, "artifacts-root/run-1/failure.mauitrace"));
        Assert.Null(RunIdFor(artifacts, "artifacts-root/odd/flow-run.json"));

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var omission = document.RootElement.GetProperty("omissions").EnumerateArray()
            .Single(static item => item.GetProperty("kind").GetString() == "artifact-path");
        Assert.Equal(1, omission.GetProperty("omittedArtifacts").GetInt32());
    }

    /// <summary>
    /// The finalized manifest is read beside a <c>flow-run.json</c> that carries the same four
    /// artifact facts, so it has to publish them too rather than leave a consumer to derive them.
    /// </summary>
    [Fact]
    public async Task ArtifactSummary_DescribesTheInventoryTheManifestPublishes()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        for (var index = 0; index < 260; index++)
            WriteFile(Path.Combine(artifactRoot, "run-1", $"file-{index:D3}.json"), "{}");

        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        Assert.Equal(0, (await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot)).ExitCode);

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var summary = root.GetProperty("artifactSummary");
        Assert.Equal(256, summary.GetProperty("maxArtifacts").GetInt32());
        Assert.Equal(
            root.GetProperty("artifacts").GetArrayLength(),
            summary.GetProperty("recordedArtifacts").GetInt32());
        Assert.Equal(
            root.GetProperty("truncation").GetProperty("omittedArtifacts").GetInt32(),
            summary.GetProperty("omittedArtifacts").GetInt32());
        Assert.Equal(root.GetProperty("truncated").GetBoolean(), summary.GetProperty("truncated").GetBoolean());
        Assert.True(summary.GetProperty("truncated").GetBoolean());
    }

    /// <summary>
    /// The cap is not the only way a reference leaves the inventory. A summary that counted the
    /// cap alone published "omittedArtifacts: 0" for a run whose unresolvable references were
    /// missing from the list beside it, which reads as a complete inventory.
    /// </summary>
    [Fact]
    public async Task ArtifactSummary_CountsEveryReferenceItExcluded()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        WriteFile(Path.Combine(artifactRoot, "run-1", "flow-run.json"), """{"runId":"run-1"}""");

        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        File.WriteAllText(
            manifestPath,
            """
            {
              "schema": 1,
              "kind": "devflow-flow-pilot",
              "artifacts": [
                {
                  "kind": "mauitrace",
                  "path": "artifacts-root\\run-1\\windows-style.mauitrace",
                  "sha256": "sha256:2222222222222222222222222222222222222222222222222222222222222222",
                  "sizeBytes": 3,
                  "redacted": true
                },
                {
                  "kind": "mauitrace",
                  "path": "/etc/passwd",
                  "sha256": "sha256:3333333333333333333333333333333333333333333333333333333333333333",
                  "sizeBytes": 3,
                  "redacted": true
                }
              ]
            }
            """);

        Assert.Equal(
            0,
            (await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot)).ExitCode);

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var summary = root.GetProperty("artifactSummary");
        Assert.False(summary.GetProperty("truncated").GetBoolean());
        Assert.Equal(0, root.GetProperty("truncation").GetProperty("omittedArtifacts").GetInt32());
        Assert.Equal(2, summary.GetProperty("omittedArtifacts").GetInt32());
        var omission = root.GetProperty("omissions").EnumerateArray()
            .Single(static item => item.GetProperty("kind").GetString() == "artifact-path");
        Assert.Equal(2, omission.GetProperty("omittedArtifacts").GetInt32());
    }

    [Fact]
    public async Task ArtifactPaths_AreAlwaysRepositoryRelative()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        WriteFile(Path.Combine(artifactRoot, "run-1", "flow-run.json"), "{}");

        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        var result = await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot);
        Assert.Equal(0, result.ExitCode);

        var artifacts = ReadArtifacts(manifestPath);
        Assert.NotEmpty(artifacts);
        Assert.All(artifacts, artifact =>
        {
            var path = artifact.GetProperty("path").GetString()!;
            Assert.False(Path.IsPathRooted(path), $"'{path}' is rooted.");
            Assert.DoesNotContain('\\', path);
            Assert.DoesNotContain(':', path);
            Assert.False(path.StartsWith("../", StringComparison.Ordinal), $"'{path}' escapes the root.");
        });
    }

    [Fact]
    public async Task ArtifactRootOutsideTheRepository_IsRejectedWithoutRecordingAnything()
    {
        var repositoryRoot = Path.Combine(_testRoot, "inside");
        var outsideRoot = Path.Combine(_testRoot, "outside");
        Directory.CreateDirectory(repositoryRoot);
        WriteFile(Path.Combine(outsideRoot, "run-1", "flow-run.json"), "{}");

        var manifestPath = Path.Combine(repositoryRoot, "manifest.json");
        var result = await RunFinalizerAsync(manifestPath, repositoryRoot, outsideRoot);
        Assert.Equal(0, result.ExitCode);

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.Empty(document.RootElement.GetProperty("artifacts").EnumerateArray());
        Assert.Contains(
            "artifact-root",
            document.RootElement.GetProperty("omissions").EnumerateArray()
                .Select(static omission => omission.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task InheritedAbsoluteArtifactPath_IsDroppedRatherThanRepublished()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        Directory.CreateDirectory(artifactRoot);
        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        var absolutePath = Path.Combine(_testRoot, "elsewhere", "leaked.json").Replace('\\', '/');
        File.WriteAllText(
            manifestPath,
            $$"""
            {
              "schema": 1,
              "kind": "devflow-flow-pilot",
              "artifacts": [
                { "kind": "json", "path": "{{absolutePath}}", "sha256": "sha256:0", "sizeBytes": 2 }
              ]
            }
            """);

        var result = await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot);
        Assert.Equal(0, result.ExitCode);

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            document.RootElement.GetProperty("artifacts").EnumerateArray(),
            artifact => artifact.GetProperty("path").GetString() == absolutePath);
        Assert.Contains(
            "artifact-path",
            document.RootElement.GetProperty("omissions").EnumerateArray()
                .Select(static omission => omission.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task EveryArtifactPastTheCap_IsCounted()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        Directory.CreateDirectory(artifactRoot);
        for (var index = 0; index < 260; index++)
            WriteFile(Path.Combine(artifactRoot, "run-1", $"file-{index:D3}.json"), "{}");

        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        var result = await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot);
        Assert.Equal(0, result.ExitCode);

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.Equal(256, document.RootElement.GetProperty("artifacts").GetArrayLength());
        Assert.True(document.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal(
            4,
            document.RootElement.GetProperty("truncation").GetProperty("omittedArtifacts").GetInt32());
        var limit = document.RootElement.GetProperty("omissions").EnumerateArray()
            .Single(static omission => omission.GetProperty("kind").GetString() == "artifact-limit");
        Assert.Equal(4, limit.GetProperty("omittedArtifacts").GetInt32());
    }

    /// <summary>
    /// A safe root is not a safe file. Whichever kind of link the host allows to be created, a
    /// finalized manifest must never publish a hash for bytes that live outside the repository.
    /// </summary>
    [Fact]
    public async Task LinkedArtifactFile_IsNeverHashedIntoTheManifest()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        var outsideRoot = Path.Combine(_testRoot, "outside");
        WriteFile(Path.Combine(artifactRoot, "run-1", "flow-run.json"), "{\"schema\":1}");
        WriteFile(Path.Combine(outsideRoot, "outside.json"), "{\"outside\":true}");

        var linkedFile = Path.Combine(artifactRoot, "run-1", "linked.json");
        var linkedFileCreated = TryCreateFileLink(linkedFile, Path.Combine(outsideRoot, "outside.json"));
        var junction = Path.Combine(artifactRoot, "run-1", "linked-directory");
        var junctionCreated = !linkedFileCreated && TryCreateDirectoryLink(junction, outsideRoot);
        Assert.True(
            linkedFileCreated || junctionCreated,
            "Neither a file link nor a directory link could be created, so this platform cannot be covered.");

        try
        {
            var manifestPath = Path.Combine(artifactRoot, "manifest.json");
            var result = await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot);
            Assert.Equal(0, result.ExitCode);

            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var paths = document.RootElement.GetProperty("artifacts").EnumerateArray()
                .Select(static artifact => artifact.GetProperty("path").GetString()!)
                .ToArray();
            Assert.Contains("artifacts-root/run-1/flow-run.json", paths);
            Assert.DoesNotContain(paths, static path => path.Contains("linked", StringComparison.Ordinal));
            Assert.DoesNotContain(paths, static path => path.Contains("outside", StringComparison.Ordinal));

            if (linkedFileCreated)
            {
                // A link the finalizer really enumerated has to be reported, not silently dropped.
                var omission = document.RootElement.GetProperty("omissions").EnumerateArray()
                    .Single(static item => item.GetProperty("kind").GetString() == "artifact-link");
                Assert.Equal(1, omission.GetProperty("omittedArtifacts").GetInt32());
            }
        }
        finally
        {
            // A reparse point is unlinked, never recursed into, or the cleanup would follow it out
            // of the test directory - including when an assertion above failed.
            if (junctionCreated && Directory.Exists(junction))
                Directory.Delete(junction);
            if (linkedFileCreated && File.Exists(linkedFile))
                File.Delete(linkedFile);
        }
    }

    /// <summary>
    /// The guard the file loop uses, exercised directly against a real reparse point. Creating a
    /// file symbolic link needs a privilege Windows does not grant by default, so the platform's
    /// own link kind is used to keep this check non-vacuous everywhere.
    /// </summary>
    [Fact]
    public async Task ReparsePointGuard_RecognizesARealLinkOnThisPlatform()
    {
        var repositoryRoot = Path.Combine(_testRoot, "repo");
        var target = Path.Combine(_testRoot, "target");
        Directory.CreateDirectory(repositoryRoot);
        WriteFile(Path.Combine(target, "outside.json"), "{}");

        var link = Path.Combine(repositoryRoot, "linked-directory");
        Assert.True(TryCreateDirectoryLink(link, target), "This platform created no link to test.");
        try
        {
            const string probe = """
                param([string] $ScriptPath, [string] $RepositoryRoot, [string] $LinkedPath, [string] $PlainPath)

                Set-StrictMode -Version Latest
                $ErrorActionPreference = 'Stop'

                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref] $tokens, [ref] $errors)
                foreach ($definition in $ast.FindAll({
                            param($node)
                            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                                $node.Name -in @('Test-PathTraversesReparsePoint', 'Get-CanonicalPath')
                        }, $true)) {
                    . ([scriptblock]::Create($definition.Extent.Text))
                }

                [ordered]@{
                    linked = [bool] (Test-PathTraversesReparsePoint (Join-Path $LinkedPath 'outside.json'))
                    plain = [bool] (Test-PathTraversesReparsePoint $PlainPath)
                } | ConvertTo-Json -Compress
                """;

            WriteFile(Path.Combine(repositoryRoot, "plain.json"), "{}");
            var result = await RunProbeAsync(
                probe,
                "-ScriptPath",
                Path.Combine(_repositoryRoot, "eng", "devflow", "Finalize-DevFlowFlowPilotManifest.ps1"),
                "-RepositoryRoot",
                repositoryRoot,
                "-LinkedPath",
                link,
                "-PlainPath",
                Path.Combine(repositoryRoot, "plain.json"));

            Assert.Equal(0, result.ExitCode);
            using var document = JsonDocument.Parse(result.StandardOutput.Trim().Split('\n').Last().Trim());
            Assert.True(document.RootElement.GetProperty("linked").GetBoolean());
            Assert.False(document.RootElement.GetProperty("plain").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
        }
    }

    private async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProbeAsync(
        string probe,
        params string[] arguments)
    {
        var probePath = Path.Combine(_testRoot, $"probe-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(probePath, probe, new UTF8Encoding(false));
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
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(probePath);
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    /// <summary>
    /// The artifact cap is a published fact: the manifest reports it, and a consumer decides
    /// whether the evidence is complete from it. Every producer that writes one has to agree.
    /// </summary>
    [Fact]
    public void ArtifactCap_IsTheSameInEveryProducer()
    {
        var finalizer = File.ReadAllText(
            Path.Combine(_repositoryRoot, "eng", "devflow", "Finalize-DevFlowFlowPilotManifest.ps1"));
        var powerShellHost = File.ReadAllText(
            Path.Combine(_repositoryRoot, "eng", "devflow", "Run-DevFlowFlowQa.ps1"));
        var shellHost = File.ReadAllText(
            Path.Combine(_repositoryRoot, "eng", "devflow", "Run-DevFlowFlowQa.sh"));
        var producer = File.ReadAllText(Path.Combine(
            _repositoryRoot,
            "src",
            "DevFlow",
            "Microsoft.Maui.DevFlow.Agent.IntegrationTests",
            "Fixtures",
            "FlowPilotArtifactManifest.cs"));

        var caps = new[]
        {
            CapturedNumber(finalizer, @"\$maxArtifacts\s*=\s*(\d+)"),
            CapturedNumber(powerShellHost, @"\$MaxArtifactRecords\s*=\s*(\d+)"),
            CapturedNumber(shellHost, @"MAX_ARTIFACTS=(\d+)"),
            CapturedNumber(producer, @"const int MaxArtifacts\s*=\s*(\d+)"),
        };

        Assert.All(caps, cap => Assert.Equal(caps[0], cap));
        Assert.Equal(256, caps[0]);
    }

    private static int CapturedNumber(string source, string pattern)
    {
        var match = System.Text.RegularExpressions.Regex.Match(source, pattern);
        Assert.True(match.Success, $"No match for '{pattern}'.");
        return int.Parse(match.Groups[1].Value);
    }

    private static bool TryCreateFileLink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return File.Exists(linkPath);
        }
        catch (Exception)
        {
            // Windows refuses symbolic links without Developer Mode or elevation.
            return false;
        }
    }

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return Directory.Exists(linkPath);
        }
        catch (Exception)
        {
            // A junction needs no privilege on Windows and is a reparse point all the same.
        }

        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/c", "mklink", "/J", linkPath, targetPath },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
                return false;
            // Drained before waiting: a redirected pipe that fills would otherwise block the exit.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit(10_000);
            Task.WaitAll([stdout, stderr], 5_000);
            return Directory.Exists(linkPath);
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Fact]
    public async Task UnknownDeviceEvidenceKind_IsRefused()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        Directory.CreateDirectory(artifactRoot);
        var manifestPath = Path.Combine(artifactRoot, "manifest.json");

        var result = await RunFinalizerAsync(
            manifestPath,
            _testRoot,
            artifactRoot,
            deviceEvidenceKind: "unknown");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("DeviceEvidenceKind", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManifestNeverDigestsItself()
    {
        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        WriteFile(Path.Combine(artifactRoot, "run-1", "flow-run.json"), "{\"schema\":1}");
        var manifestPath = Path.Combine(artifactRoot, "manifest.json");

        Assert.Equal(0, (await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot)).ExitCode);
        Assert.Equal(0, (await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot)).ExitCode);

        var artifacts = ReadArtifacts(manifestPath);
        Assert.DoesNotContain(
            artifacts,
            artifact => artifact.GetProperty("path").GetString()!.EndsWith("manifest.json", StringComparison.Ordinal));

        // Every remaining digest must still describe the bytes on disk after the final write.
        foreach (var artifact in artifacts)
        {
            var path = Path.Combine(_testRoot, artifact.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(
                $"sha256:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()}",
                artifact.GetProperty("sha256").GetString());
        }
    }

    [Fact]
    public void Finalizer_DeclaresItsMinimumPowerShellVersion()
    {
        var script = File.ReadAllText(
            Path.Combine(_repositoryRoot, "eng", "devflow", "Finalize-DevFlowFlowPilotManifest.ps1"));
        Assert.StartsWith("#Requires -Version 7.3", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// A directory this pass cannot read is not an empty directory. Suppressing the enumeration
    /// error published a partial inventory as a complete one, and any inherited reference whose
    /// file lives in the unreadable directory was reported as gone rather than as unread. Making a
    /// directory unreadable is only done through POSIX modes here; a Windows host asserts the same
    /// rule statically rather than editing the ACLs of the machine it runs on.
    /// </summary>
    [Fact]
    public async Task UnreadableArtifactDirectory_IsReportedRatherThanSilentlySkipped()
    {
        var script = File.ReadAllText(
            Path.Combine(_repositoryRoot, "eng", "devflow", "Finalize-DevFlowFlowPilotManifest.ps1"));
        // Enumeration is the one place the script suppresses errors, and it may only do so while
        // binding them to a variable it then counts and publishes.
        Assert.Contains("-ErrorAction SilentlyContinue -ErrorVariable rootErrors", script, StringComparison.Ordinal);
        Assert.Contains("kind = 'artifact-enumeration'", script, StringComparison.Ordinal);

        var artifactRoot = Path.Combine(_testRoot, "artifacts-root");
        WriteFile(Path.Combine(artifactRoot, "run-1", "flow-run.json"), """{"runId":"run-1"}""");
        var locked = Path.Combine(artifactRoot, "locked");
        WriteFile(Path.Combine(locked, "hidden.mauitrace"), "trace");

        if (OperatingSystem.IsWindows() || !TryDenyDirectoryRead(locked))
        {
            RestoreDirectoryRead(locked);
            Console.WriteLine(
                "This host cannot make a directory unreadable without changing machine state, " +
                "so only the static rule was asserted.");
            return;
        }

        try
        {
            var manifestPath = Path.Combine(artifactRoot, "manifest.json");
            Assert.Equal(0, (await RunFinalizerAsync(manifestPath, _testRoot, artifactRoot)).ExitCode);

            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            var omission = root.GetProperty("omissions").EnumerateArray()
                .Single(static item => item.GetProperty("kind").GetString() == "artifact-enumeration");
            Assert.True(omission.GetProperty("enumerationErrors").GetInt32() > 0);
            Assert.True(
                root.GetProperty("artifactSummary").GetProperty("omittedArtifacts").GetInt32() > 0,
                "An inventory that could not be fully read must not report zero omissions.");
            Assert.DoesNotContain(
                root.GetProperty("artifacts").EnumerateArray()
                    .Select(static artifact => artifact.GetProperty("path").GetString()),
                path => path!.Contains("hidden.mauitrace", StringComparison.Ordinal));
        }
        finally
        {
            RestoreDirectoryRead(locked);
        }
    }

    private static bool TryDenyDirectoryRead(string directory)
    {
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.None);
            Directory.EnumerateFileSystemEntries(directory).ToList();
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Restoring is always safe: the directory is inside this test's own throwaway root, and a host
    // that never applied the mode (Windows, or a run as root) simply has nothing to undo.
    private static void RestoreDirectoryRead(string directory)
    {
        if (OperatingSystem.IsWindows() || !Directory.Exists(directory))
            return;

        try
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception)
        {
            // Cleanup of the throwaway root reports anything that really is stuck.
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    private static string? RunIdFor(IReadOnlyList<JsonElement> artifacts, string path)
    {
        var artifact = artifacts.Single(candidate => candidate.GetProperty("path").GetString() == path);
        return artifact.TryGetProperty("runId", out var runId) ? runId.GetString() : null;
    }

    private static string DigestFor(IReadOnlyList<JsonElement> artifacts, string path)
        => artifacts.Single(candidate => candidate.GetProperty("path").GetString() == path)
            .GetProperty("sha256").GetString()!;

    private static long SizeFor(IReadOnlyList<JsonElement> artifacts, string path)
        => artifacts.Single(candidate => candidate.GetProperty("path").GetString() == path)
            .GetProperty("sizeBytes").GetInt64();

    private static (string[] Omissions, int OmittedArtifacts, int RecordedArtifacts) ReadArtifactAccounting(
        string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        return (
            root.GetProperty("omissions").EnumerateArray()
                .Select(static omission => omission.GetProperty("kind").GetString()!)
                .ToArray(),
            root.GetProperty("artifactSummary").GetProperty("omittedArtifacts").GetInt32(),
            root.GetProperty("artifactSummary").GetProperty("recordedArtifacts").GetInt32());
    }

    private void AssertEveryDigestMatchesTheBytesOnDisk(IReadOnlyList<JsonElement> artifacts)
    {
        Assert.NotEmpty(artifacts);
        foreach (var artifact in artifacts)
        {
            var path = Path.Combine(
                _testRoot,
                artifact.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"'{path}' is published but absent.");
            Assert.Equal(Sha256Of(path), artifact.GetProperty("sha256").GetString());
            Assert.Equal(new FileInfo(path).Length, artifact.GetProperty("sizeBytes").GetInt64());
        }
    }

    private static string Sha256Of(string path)
        => $"sha256:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()}";

    private static IReadOnlyList<JsonElement> ReadArtifacts(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return document.RootElement.GetProperty("artifacts").EnumerateArray()
            .Select(static artifact => artifact.Clone())
            .ToArray();
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private async Task<(int ExitCode, string StandardOutput, string StandardError)> RunFinalizerAsync(
        string manifestPath,
        string repositoryRoot,
        string artifactRoot,
        string deviceEvidenceKind = "emulator")
    {
        var script = Path.Combine(_repositoryRoot, "eng", "devflow", "Finalize-DevFlowFlowPilotManifest.ps1");
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
                     script,
                     "-ManifestPath",
                     manifestPath,
                     "-RepositoryRoot",
                     repositoryRoot,
                     "-ArtifactRoots",
                     artifactRoot,
                     "-RepositoryCommit",
                     RepositoryCommit,
                     "-WorkflowRunId",
                     "finalizer-tests",
                     "-DeviceEvidenceKind",
                     deviceEvidenceKind,
                 })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        // Both pipes are drained at once. Reading one to the end first deadlocks as soon as the
        // other fills its buffer, which is exactly what a failing finalizer does.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

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
