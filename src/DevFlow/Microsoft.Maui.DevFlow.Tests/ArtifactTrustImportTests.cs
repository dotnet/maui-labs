using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public class ArtifactTrustImportTests
{
    private const string RuntimeProfile = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void ImportFlowRun_GeneratesFreshImportedIdentityAndRedactsHostileContent()
    {
        const string secret = "CorrectHorseBatteryStaple";
        var importer = new ArtifactTrustImportService(new FixedTimeProvider(DateTimeOffset.UtcNow));

        var result = importer.Import(
            Encoding.UTF8.GetBytes(FlowRunJson(secret)),
            ArtifactTrustImportKinds.FlowRun);

        Assert.True(result.Ok, result.Error);
        var artifact = Assert.IsType<MauiArtifactTrustRecord>(result.Artifact);
        Assert.True(artifact.Identity!.IsValid);
        Assert.NotEqual("run_local-looking", artifact.Identity.Id);
        Assert.Equal(MauiArtifactIdentityNamespaces.ImportedArtifact, artifact.Identity.Namespace);
        Assert.Equal(MauiArtifactTrustStates.Untrusted, artifact.Verification.State);
        Assert.Equal(MauiFlowFailureClasses.LocatorNotFound, artifact.Projection!.Failure!.Code);
        Assert.DoesNotContain(
            secret,
            JsonSerializer.Serialize(artifact, MauiTestingJsonContext.Default.MauiArtifactTrustRecord),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "run_local-looking",
            JsonSerializer.Serialize(artifact, MauiTestingJsonContext.Default.MauiArtifactTrustRecord),
            StringComparison.Ordinal);
        Assert.Contains(artifact.Projection.Omissions, omission => omission.Field == "raw-content");
        Assert.Contains(artifact.Projection.Omissions, omission => omission.Field == "embedded-identifiers");
    }

    [Fact]
    public void ImportFlowRun_RejectsOversizedAndDeepJsonWithoutPersistingInput()
    {
        var importer = new ArtifactTrustImportService();
        var oversized = new byte[ArtifactTrustImportService.MaxFlowRunBytes + 1];
        var nested = Encoding.UTF8.GetBytes(new string('[', ArtifactTrustImportService.MaxJsonDepth + 1) + "0" +
                                              new string(']', ArtifactTrustImportService.MaxJsonDepth + 1));

        var oversizedResult = importer.Import(oversized, ArtifactTrustImportKinds.FlowRun);
        var nestedResult = importer.Import(nested, ArtifactTrustImportKinds.FlowRun);

        Assert.False(oversizedResult.Ok);
        Assert.Null(oversizedResult.Artifact);
        Assert.False(nestedResult.Ok);
        Assert.Null(nestedResult.Artifact);
    }

    /// <summary>
    /// The imported cleanup flag is three-valued on purpose. An artifact written before the
    /// two-axis outcome contract records no cleanup facts at all, and answering <c>false</c> for it
    /// would assert on the producing host's behalf that it tore itself down — a claim the artifact
    /// never made, about the very state a reproduction would replay against.
    /// </summary>
    [Theory]
    // A legacy producer: the field does not exist, so the answer is unknown, not "clean".
    [InlineData(null, null)]
    // An explicit null carries no more information than an absent field.
    [InlineData(""" , "secondaryFailures": null """, null)]
    // The current producer stating there was nothing to report.
    [InlineData(""" , "secondaryFailures": [] """, false)]
    [InlineData(
        """ , "secondaryFailures": [{ "phase": "cleanup", "code": "cleanup-failed" }] """,
        true)]
    // Malformed shapes keep the existing refusal: this is the untrusted read path, and an artifact
    // that cannot be understood must not be read as clean.
    [InlineData(""" , "secondaryFailures": {} """, true)]
    [InlineData(""" , "secondaryFailures": "cleanup-failed" """, true)]
    [InlineData(""" , "secondaryFailures": 0 """, true)]
    public void ImportFlowRun_OwnedCleanupIsUnknownWhenTheArtifactDoesNotSayOtherwise(
        string? secondaryFailures,
        bool? expected)
    {
        var importer = new ArtifactTrustImportService();

        var result = importer.Import(
            Encoding.UTF8.GetBytes(MinimalFlowRunJson(secondaryFailures)),
            ArtifactTrustImportKinds.FlowRun);

        Assert.True(result.Ok, result.Error);
        var artifact = Assert.IsType<MauiArtifactTrustRecord>(result.Artifact);
        Assert.Equal(expected, artifact.Projection!.OwnedCleanupIncomplete);
    }

    /// <summary>
    /// Only a positive answer refuses a reproduction. An unknown one must behave exactly like a
    /// clean one here, or every artifact produced before this contract would become unusable.
    /// </summary>
    [Fact]
    public void ImportFlowRun_UnknownOwnedCleanupIsNotTreatedAsARefusal()
    {
        var importer = new ArtifactTrustImportService();

        var legacy = importer.Import(
            Encoding.UTF8.GetBytes(MinimalFlowRunJson(null)),
            ArtifactTrustImportKinds.FlowRun);
        var incomplete = importer.Import(
            Encoding.UTF8.GetBytes(MinimalFlowRunJson(
                """ , "secondaryFailures": [{ "phase": "cleanup", "code": "cleanup-failed" }] """)),
            ArtifactTrustImportKinds.FlowRun);

        var legacyProjection = Assert.IsType<MauiArtifactTrustRecord>(legacy.Artifact).Projection!;
        var incompleteProjection = Assert.IsType<MauiArtifactTrustRecord>(incomplete.Artifact).Projection!;
        Assert.NotEqual(true, legacyProjection.OwnedCleanupIncomplete);
        Assert.Equal(true, incompleteProjection.OwnedCleanupIncomplete);
        // The rest of the projection is identical, so the flag is the only thing that changed.
        Assert.Equal(legacyProjection.Outcome, incompleteProjection.Outcome);
        Assert.Equal(legacyProjection.Failure?.Code, incompleteProjection.Failure?.Code);
    }

    [Fact]
    public void ImportEvidenceV1_UsesExistingBoundedReaderAndDoesNotTrustEntryHashesAsProvenance()
    {
        var importer = new ArtifactTrustImportService();

        var result = importer.Import(CreateMinimalEvidenceBundle(), ArtifactTrustImportKinds.Evidence);

        Assert.True(result.Ok, result.Error);
        var artifact = Assert.IsType<MauiArtifactTrustRecord>(result.Artifact);
        Assert.Equal("mauitrace", artifact.ArtifactKind);
        Assert.True(artifact.Integrity!.InternalHashesVerified);
        Assert.Equal(MauiArtifactTrustStates.Untrusted, artifact.Verification.State);
        Assert.Contains(
            artifact.Verification.Reasons,
            reason => reason.Code == "internal-hashes-integrity-only");
        Assert.All(
            artifact.Projection!.EmbeddedIdentifierDigests,
            digest => Assert.DoesNotContain("run-", digest, StringComparison.Ordinal));
    }

    [Fact]
    public void ImportEvidence_RejectsZipBombSizedManifest()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("manifest.json", CompressionLevel.SmallestSize);
            using var stream = entry.Open();
            stream.Write(new byte[1_048_577]);
        }

        var result = new ArtifactTrustImportService().Import(
            output.ToArray(),
            ArtifactTrustImportKinds.Evidence);

        Assert.False(result.Ok);
        Assert.Null(result.Artifact);
    }

    [Fact]
    public void Store_RequiresCapabilityTokenAndEvictsBoundedOldestProjection()
    {
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var importer = new ArtifactTrustImportService(clock);
        var store = new ArtifactTrustStore(
            new ArtifactTrustStoreOptions
            {
                MaxRetainedArtifacts = 1,
                Retention = TimeSpan.FromMinutes(1),
            },
            clock);
        var first = importer.Import(Encoding.UTF8.GetBytes(FlowRunJson("first")), ArtifactTrustImportKinds.FlowRun);
        var second = importer.Import(Encoding.UTF8.GetBytes(FlowRunJson("second")), ArtifactTrustImportKinds.FlowRun);

        var firstAdded = store.Add(first.Artifact!);
        var denied = store.GetStatus(first.Artifact!.Identity!.Id!, capabilityToken: null);
        var allowed = store.GetSafeProjection(first.Artifact.Identity.Id!, firstAdded.CapabilityToken);
        var secondAdded = store.Add(second.Artifact!);
        var evicted = store.GetStatus(first.Artifact.Identity.Id!, firstAdded.CapabilityToken);
        var retained = store.GetStatus(second.Artifact!.Identity!.Id!, secondAdded.CapabilityToken);

        Assert.True(firstAdded.Ok);
        Assert.Equal(403, denied.StatusCode);
        Assert.Equal(200, allowed.StatusCode);
        Assert.NotNull(allowed.Projection);
        Assert.True(secondAdded.Ok);
        Assert.Equal(404, evicted.StatusCode);
        Assert.Equal(200, retained.StatusCode);
        Assert.False(retained.Status!.RawContentRetained);
    }

    [Fact]
    public void Store_ExpiresRecordsWithoutRetainingRawInput()
    {
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var importer = new ArtifactTrustImportService(clock);
        var store = new ArtifactTrustStore(
            new ArtifactTrustStoreOptions
            {
                Retention = TimeSpan.FromSeconds(1),
            },
            clock);
        var imported = importer.Import(Encoding.UTF8.GetBytes(FlowRunJson("secret")), ArtifactTrustImportKinds.FlowRun);
        var added = store.Add(imported.Artifact!);

        clock.Advance(TimeSpan.FromSeconds(2));
        var expired = store.GetSafeProjection(imported.Artifact!.Identity!.Id!, added.CapabilityToken);

        Assert.Equal(404, expired.StatusCode);
        Assert.Null(expired.Projection);
    }

    [Fact]
    public void Store_BindsOnlyMatchingNewLocalRunBeforeEnablingProposalPolicy()
    {
        var importedAt = DateTimeOffset.UtcNow;
        var clock = new FixedTimeProvider(importedAt);
        var importer = new ArtifactTrustImportService(clock);
        var imported = importer.Import(
            Encoding.UTF8.GetBytes(FlowRunJson("secret")),
            ArtifactTrustImportKinds.FlowRun);
        var store = new ArtifactTrustStore(clock: clock);
        var added = store.Add(imported.Artifact!);
        clock.Advance(TimeSpan.FromSeconds(1));

        var binding = store.BindLocalReproduction(
            imported.Artifact!.Identity!.Id!,
            added.CapabilityToken,
            new MauiLocalReproductionFacts
            {
                LocalRunId = "run_local_new",
                IsNewLocalRun = true,
                StartedAt = clock.GetUtcNow(),
                FlowDigest = "flow-current",
                AppBuildFingerprint = "build-current",
                AppSourceFingerprint = "source-current",
                PackageDigest = "package-current",
                Platform = "android",
                DeviceProfile = "pixel",
                RuntimeProfileFingerprint = RuntimeProfile,
                Failure = new MauiLocalFailureFacts
                {
                    Code = MauiFlowFailureClasses.LocatorNotFound,
                    Class = MauiFlowFailureClasses.LocatorNotFound,
                    StepId = "tap-save",
                    ExpectedCheckpoint = new MauiFlowCheckpoint
                    {
                        AppBuildFingerprint = "build-current",
                        Route = "/todos",
                    },
                    ObservedCheckpoint = new MauiFlowCheckpoint
                    {
                        AppBuildFingerprint = "build-current",
                        Route = "/todos",
                    },
                },
            },
            new MauiLocalReproductionExpectation
            {
                FlowDigest = "flow-current",
                AppBuildFingerprint = "build-current",
                AppSourceFingerprint = "source-current",
                PackageDigest = "package-current",
                Platform = "android",
                DeviceProfile = "pixel",
                RuntimeProfileFingerprint = RuntimeProfile,
            });

        Assert.Equal(200, binding.StatusCode);
        Assert.True(
            binding.Evaluation!.Binding.Matched,
            string.Join(
                "; ",
                binding.Evaluation.Verification.Reasons.Select(static reason => reason.Code)));
        Assert.Equal(MauiArtifactTrustStates.LocallyReproduced, binding.Status!.Verification!.State);
        Assert.Equal(
            200,
            store.GetRepairTrust(
                imported.Artifact.Identity.Id!,
                added.CapabilityToken,
                "run_local_new").StatusCode);
        Assert.Equal(
            409,
            store.GetRepairTrust(
                imported.Artifact.Identity.Id!,
                added.CapabilityToken,
                "run_attacker_supplied").StatusCode);
        Assert.Equal(
            403,
            store.GetRepairTrust(
                imported.Artifact.Identity.Id!,
                "wrong-capability",
                "run_local_new").StatusCode);

        var record = new MauiArtifactTrustRecord
        {
            Identity = imported.Artifact.Identity,
            Verification = binding.Status.Verification!,
            LocalReproduction = binding.Evaluation.Binding,
        };
        Assert.True(MauiArtifactProposalPolicy.CanCreateProposal(record).Allowed);
    }

    [Fact]
    public void AppleQaVerifier_VerifiesManifestHashesAndImportsOnlyUntrustedDiagnostics()
    {
        var root = CreateTestDirectory();
        var archivePath = Path.Combine(root, "returned-apple-qa.zip");
        try
        {
            File.WriteAllBytes(archivePath, CreateAppleQaArchive(includeDirectories: true));

            var result = new AppleQaArtifactVerifier().Verify(archivePath, importDiagnostics: true);

            Assert.True(result.Ok, result.Error);
            Assert.Equal("ios", result.Platform);
            Assert.False(result.Executed);
            Assert.False(result.RawContentRetained);
            Assert.False(result.RepairProposalAuthority);
            Assert.Equal(2, result.VerifiedArtifacts.Count);
            Assert.Equal(2, result.ImportedDiagnostics.Count);
            Assert.All(result.ImportedDiagnostics, artifact =>
            {
                Assert.Equal(MauiArtifactTrustStates.Untrusted, artifact.Verification.State);
                Assert.True(artifact.Identity!.IsValid);
                Assert.False(MauiArtifactProposalPolicy.CanCreateProposal(artifact).Allowed);
            });

            var extracted = Path.Combine(root, "extracted-return");
            ZipFile.ExtractToDirectory(archivePath, extracted);
            var directoryResult = new AppleQaArtifactVerifier().Verify(extracted, importDiagnostics: true);
            Assert.True(directoryResult.Ok, directoryResult.Error);
            Assert.Equal(2, directoryResult.ImportedDiagnostics.Count);
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Theory]
    [InlineData("traversal")]
    [InlineData("duplicate")]
    [InlineData("symlink")]
    [InlineData("bomb")]
    [InlineData("count")]
    public void AppleQaVerifier_RejectsHostileArchiveEntries(string scenario)
    {
        var root = CreateTestDirectory();
        var archivePath = Path.Combine(root, $"{scenario}.zip");
        try
        {
            using (var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                switch (scenario)
                {
                    case "traversal":
                        WriteArchiveEntry(archive, "../outside.json", "{}"u8.ToArray());
                        break;
                    case "duplicate":
                        WriteArchiveEntry(archive, "artifacts/devflow/run-1/ios/manifest.json", "{}"u8.ToArray());
                        WriteArchiveEntry(archive, "artifacts/devflow/run-1/ios/manifest.json", "{}"u8.ToArray());
                        break;
                    case "symlink":
                    {
                        var entry = archive.CreateEntry("artifacts/devflow/run-1/ios/host-diagnostics/link.txt");
                        entry.ExternalAttributes = unchecked((int)0xA0000000);
                        using var writer = entry.Open();
                        writer.Write("target"u8);
                        break;
                    }
                    case "bomb":
                    {
                        var entry = archive.CreateEntry(
                            "artifacts/devflow/run-1/ios/host-diagnostics/bomb.txt",
                            CompressionLevel.SmallestSize);
                        using var writer = entry.Open();
                        writer.Write(new byte[1_048_576]);
                        break;
                    }
                    case "count":
                        for (var index = 0; index <= AppleQaArtifactVerifier.MaxEntryCount; index++)
                        {
                            WriteArchiveEntry(
                                archive,
                                $"artifacts/devflow/run-1/ios/host-diagnostics/entry-{index}.txt",
                                "x"u8.ToArray());
                        }
                        break;
                }
            }

            var result = new AppleQaArtifactVerifier().Verify(archivePath);

            Assert.False(result.Ok);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void AppleQaVerifier_RejectsManifestHashMismatchAndOversizedDirectoryEntry()
    {
        var root = CreateTestDirectory();
        var archivePath = Path.Combine(root, "hash-mismatch.zip");
        var directory = Path.Combine(root, "directory-return");
        try
        {
            File.WriteAllBytes(archivePath, CreateAppleQaArchive(mismatchFlowHash: true));
            var mismatch = new AppleQaArtifactVerifier().Verify(archivePath);

            Assert.False(mismatch.Ok);
            Assert.Contains("hash", mismatch.Error!, StringComparison.OrdinalIgnoreCase);

            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{}");
            using (var oversized = new FileStream(
                Path.Combine(directory, "host-diagnostics.txt"),
                FileMode.CreateNew,
                FileAccess.Write))
            {
                oversized.SetLength(AppleQaArtifactVerifier.MaxEntryBytes + 1);
            }
            var oversizedResult = new AppleQaArtifactVerifier().Verify(directory);

            Assert.False(oversizedResult.Ok);
            Assert.Contains("oversized", oversizedResult.Error!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    private static string FlowRunJson(string secret)
        => $$"""
            {
              "schema": 1,
              "runId": "run_local-looking",
              "flowDigest": "flow-current",
              "target": {
                "appBuildFingerprint": "build-current",
                "appSourceFingerprint": "source-current",
                "packageDigest": "package-current",
                "platform": "android",
                "deviceProfile": "pixel",
                "runtimeProfileFingerprint": "{{RuntimeProfile}}"
              },
              "startedAt": "2026-08-01T08:00:00Z",
              "endedAt": "2026-08-01T08:00:01Z",
              "outcome": { "status": "failed", "terminal": true },
              "failure": {
                "failureId": "failure-local-looking",
                "class": "locator-not-found",
                "code": "locator-not-found",
                "stepId": "tap-save",
                "message": "{{secret}}"
              },
              "steps": [
                {
                  "stepId": "tap-save",
                  "expectedCheckpoint": {
                    "appBuildFingerprint": "build-current",
                    "route": "/todos"
                  },
                  "observedCheckpoint": {
                    "appBuildFingerprint": "build-current",
                    "route": "/todos"
                  }
                }
              ],
              "events": [],
              "artifacts": []
            }
            """;

    private static string MinimalFlowRunJson(string? secondaryFailures)
        => $$"""
            {
              "schema": 1,
              "runId": "run_imported",
              "flowDigest": "flow-current",
              "startedAt": "2026-08-01T08:00:00Z",
              "endedAt": "2026-08-01T08:00:01Z",
              "outcome": { "status": "failed", "terminal": true },
              "failure": { "class": "locator-not-found", "code": "locator-not-found" }{{secondaryFailures}}
            }
            """;

    private static byte[] CreateMinimalEvidenceBundle()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("manifest.json", CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1_024, leaveOpen: false);
            writer.Write("""
                {
                  "schema": "maui-devflow-evidence",
                  "formatVersion": 1,
                  "capturedUtc": "2026-08-01T08:00:00Z",
                  "entries": []
                }
                """);
        }

        return output.ToArray();
    }

    private static byte[] CreateAppleQaArchive(
        bool mismatchFlowHash = false,
        bool includeDirectories = false)
    {
        const string prefix = "artifacts/devflow/run-1/ios";
        var flowRun = Encoding.UTF8.GetBytes(FlowRunJson("apple-return-secret"));
        var trace = CreateMinimalEvidenceBundle();
        var flowPath = $"{prefix}/apple-flow-runs/tier-one/flow-run.json";
        var tracePath = $"{prefix}/tier-one-attempt-1/failure.mauitrace";
        var manifest = CreateAppleQaManifest(
            flowPath,
            tracePath,
            Hash(flowRun),
            Hash(trace),
            flowRun.Length,
            trace.Length,
            mismatchFlowHash);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (includeDirectories)
            {
                foreach (var directory in new[]
                         {
                             "artifacts/",
                             "artifacts/devflow/",
                             "artifacts/devflow/run-1/",
                             $"{prefix}/",
                             $"{prefix}/apple-flow-runs/",
                             $"{prefix}/apple-flow-runs/tier-one/",
                             $"{prefix}/tier-one-attempt-1/",
                         })
                {
                    archive.CreateEntry(directory);
                }
            }
            WriteArchiveEntry(archive, $"{prefix}/manifest.json", Encoding.UTF8.GetBytes(manifest));
            WriteArchiveEntry(archive, flowPath, flowRun);
            WriteArchiveEntry(archive, tracePath, trace);
        }
        return output.ToArray();
    }

    private static string CreateAppleQaManifest(
        string flowPath,
        string tracePath,
        string flowDigest,
        string traceDigest,
        int flowLength,
        int traceLength,
        bool mismatchFlowHash)
    {
        var hashA = "sha256:" + new string('a', 64);
        var hashB = "sha256:" + new string('b', 64);
        var manifestFlowDigest = mismatchFlowHash ? "sha256:" + new string('c', 64) : flowDigest;
        return $$"""
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
                "buildFingerprint": "{{hashB}}",
                "packageDigest": "{{hashB}}"
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
                  "seedFingerprint": "{{hashB}}",
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
                      "status": "passed",
                      "report": "{{flowPath}}",
                      "reportDigest": "{{hashB}}"
                    },
                    "cleanAttempts": [
                      {
                        "status": "passed",
                        "report": "{{flowPath}}",
                        "reportDigest": "{{hashB}}"
                      }
                    ]
                  }
                ]
              },
              "artifacts": [
                {
                  "kind": "flow-run-report",
                  "path": "{{flowPath}}",
                  "sha256": "{{manifestFlowDigest}}",
                  "sizeBytes": {{flowLength}},
                  "redacted": true
                },
                {
                  "kind": "mauitrace",
                  "path": "{{tracePath}}",
                  "sha256": "{{traceDigest}}",
                  "sizeBytes": {{traceLength}},
                  "redacted": true
                }
              ],
              "omissions": []
            }
            """;
    }

    private static void WriteArchiveEntry(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static string Hash(byte[] bytes)
        => "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "apple-qa-artifact-verifier-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTestDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
