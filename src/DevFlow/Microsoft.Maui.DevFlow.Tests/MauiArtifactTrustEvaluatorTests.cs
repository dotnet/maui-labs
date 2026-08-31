using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public class MauiArtifactTrustEvaluatorTests
{
    private const string NormalizedPayload = "sha256:" + "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";

    [Fact]
    public void EvaluateImport_InternalHashesAndEmbeddedIds_DoNotUpgradeDefaultTrust()
    {
        var integrity = new MauiArtifactIntegrityVerification
        {
            ArtifactDigest = "a" + new string('1', 63),
            Verified = true,
            InternalHashesPresent = true,
            InternalHashesVerified = true,
        };

        var result = MauiArtifactTrustEvaluator.EvaluateImport(
            policy: null,
            integrity,
            new MauiArtifactVerifiedProvenanceFacts
            {
                Verified = true,
                VerifiedBy = "embedded-claim",
                VerifiedAt = DateTimeOffset.UtcNow,
                Subject = new MauiArtifactProvenanceSubject
                {
                    Repository = "owner/repo",
                    Workflow = "build",
                    Commit = "deadbeef",
                    Issuer = "untrusted-content",
                    ArtifactDigest = integrity.ArtifactDigest,
                },
            });

        Assert.Equal(MauiArtifactTrustStates.Untrusted, result.State);
        Assert.Contains(result.Reasons, reason => reason.Code == "internal-hashes-integrity-only");
        Assert.Contains(result.Reasons, reason => reason.Code == "attestation-not-configured");
    }

    [Fact]
    public void EvaluateImport_VerifiedFactsMatchingConfiguredPolicy_Attests()
    {
        var integrity = new MauiArtifactIntegrityVerification
        {
            ArtifactDigest = new string('a', 64),
            Verified = true,
        };
        var expected = CreateSubject(integrity.ArtifactDigest);
        var policy = new MauiArtifactTrustPolicy
        {
            AllowAttestation = true,
            PolicyVersion = "trust-v1",
            ExpectedProvenance = expected,
        };
        var facts = new MauiArtifactVerifiedProvenanceFacts
        {
            Verified = true,
            VerifiedBy = "local-attestation-verifier",
            VerifiedAt = DateTimeOffset.UtcNow,
            Subject = CreateSubject(integrity.ArtifactDigest),
        };

        var result = MauiArtifactTrustEvaluator.EvaluateImport(policy, integrity, facts);

        Assert.Equal(MauiArtifactTrustStates.Attested, result.State);
        Assert.Contains(result.Reasons, reason => reason.Code == "attested");
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("workflow")]
    [InlineData("commit")]
    [InlineData("artifactDigest")]
    public void EvaluateImport_MismatchedRequiredProvenance_RemainsUntrusted(string mismatch)
    {
        var integrity = new MauiArtifactIntegrityVerification
        {
            ArtifactDigest = new string('b', 64),
            Verified = true,
        };
        var subject = CreateSubject(integrity.ArtifactDigest);
        switch (mismatch)
        {
            case "repository":
                subject.Repository = "other/repo";
                break;
            case "workflow":
                subject.Workflow = "release";
                break;
            case "commit":
                subject.Commit = "different";
                break;
            case "artifactDigest":
                subject.ArtifactDigest = new string('c', 64);
                break;
        }

        var result = MauiArtifactTrustEvaluator.EvaluateImport(
            new MauiArtifactTrustPolicy
            {
                AllowAttestation = true,
                ExpectedProvenance = CreateSubject(integrity.ArtifactDigest),
            },
            integrity,
            new MauiArtifactVerifiedProvenanceFacts
            {
                Verified = true,
                VerifiedBy = "verifier",
                VerifiedAt = DateTimeOffset.UtcNow,
                Subject = subject,
            });

        Assert.Equal(MauiArtifactTrustStates.Untrusted, result.State);
        Assert.Contains(result.Reasons, reason => reason.Code == mismatch + "-mismatch");
    }

    [Fact]
    public void EvaluateLocalReproduction_MatchingNewLocalRun_TransitionsToLocallyReproduced()
    {
        var imported = CreateImportedFailure();
        var local = CreateMatchingLocalRun(imported.ImportedAt!.Value);
        var expected = CreateExpectation();

        var evaluation = MauiArtifactTrustEvaluator.EvaluateLocalReproduction(imported, local, expected);

        Assert.Equal(MauiArtifactTrustStates.LocallyReproduced, evaluation.Verification.State);
        Assert.True(evaluation.Binding.Matched);
        Assert.Equal(imported.Identity!.Id, evaluation.Binding.ImportedArtifact!.Id);
        Assert.Equal(local.LocalRunId, evaluation.Binding.LocalRunId);
    }

    [Fact]
    public void EvaluateLocalReproduction_SignedOccurrenceDiffers_RefusesEvenWithAMatchingNormalizedPayload()
    {
        var imported = CreateImportedFailure();
        imported.Projection!.PackageFingerprint = MauiArtifactTrustRedactor.Fingerprint("package-occurrence-a");
        imported.Projection.NormalizedPayloadFingerprint = MauiArtifactTrustRedactor.Fingerprint(NormalizedPayload);
        var local = CreateMatchingLocalRun(imported.ImportedAt!.Value);
        local.NormalizedPayloadDigest = NormalizedPayload;
        var expected = CreateExpectation();

        var evaluation = MauiArtifactTrustEvaluator.EvaluateLocalReproduction(imported, local, expected);

        Assert.NotEqual(MauiArtifactTrustStates.LocallyReproduced, evaluation.Verification.State);
        Assert.False(evaluation.Binding.Matched);
        Assert.Contains(evaluation.Verification.Reasons, reason => reason.Code == "packageDigest-mismatch");
        Assert.Contains(evaluation.Verification.Reasons, reason => reason.Code == "failure-correspondence-same");
        Assert.DoesNotContain(evaluation.Verification.Reasons, reason => reason.Code == "normalized-payload-identity-matched");
    }

    [Fact]
    public void EvaluateLocalReproduction_SignedOccurrenceDiffers_WithoutNormalizedPayload_StillRefuses()
    {
        var imported = CreateImportedFailure();
        imported.Projection!.PackageFingerprint = MauiArtifactTrustRedactor.Fingerprint("package-occurrence-a");
        var local = CreateMatchingLocalRun(imported.ImportedAt!.Value);
        var expected = CreateExpectation();

        var evaluation = MauiArtifactTrustEvaluator.EvaluateLocalReproduction(imported, local, expected);

        Assert.NotEqual(MauiArtifactTrustStates.LocallyReproduced, evaluation.Verification.State);
        Assert.False(evaluation.Binding.Matched);
        Assert.DoesNotContain(evaluation.Verification.Reasons, reason => reason.Code == "normalized-payload-identity-matched");
    }

    [Fact]
    public void EvaluateLocalReproduction_NormalizedPayloadDigest_IsCarriedButNeverRescuesAMismatch()
    {
        var imported = CreateImportedFailure();
        imported.Projection!.PackageFingerprint = MauiArtifactTrustRedactor.Fingerprint("package-occurrence-a");
        imported.Projection.NormalizedPayloadFingerprint = MauiArtifactTrustRedactor.Fingerprint(NormalizedPayload);
        imported.Projection.FlowFingerprint = MauiArtifactTrustRedactor.Fingerprint("flow-other");
        var local = CreateMatchingLocalRun(imported.ImportedAt!.Value);
        local.NormalizedPayloadDigest = NormalizedPayload;
        var expected = CreateExpectation();

        var evaluation = MauiArtifactTrustEvaluator.EvaluateLocalReproduction(imported, local, expected);

        Assert.False(evaluation.Binding.Matched);
        Assert.Contains(evaluation.Verification.Reasons, reason => reason.Code == "flowDigest-mismatch");
    }

    [Fact]
    public void EvaluateLocalReproduction_MalformedNormalizedPayloadFingerprint_IsRejected()
    {
        var imported = CreateImportedFailure();
        imported.Projection!.NormalizedPayloadFingerprint = "not-a-fingerprint";
        var local = CreateMatchingLocalRun(imported.ImportedAt!.Value);
        var expected = CreateExpectation();

        var evaluation = MauiArtifactTrustEvaluator.EvaluateLocalReproduction(imported, local, expected);

        Assert.False(evaluation.Binding.Matched);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("step")]
    [InlineData("checkpoint")]
    public void EvaluateLocalReproduction_FailureMismatch_DoesNotUpgradeTrust(string mismatch)
    {
        var imported = CreateImportedFailure();
        var local = CreateMatchingLocalRun(imported.ImportedAt!.Value);
        switch (mismatch)
        {
            case "code":
                local.Failure!.Code = MauiFlowFailureClasses.AssertionFailed;
                break;
            case "step":
                local.Failure!.StepId = "different-step";
                break;
            case "checkpoint":
                local.Failure!.ObservedCheckpoint!.Route = "/different";
                break;
        }

        var evaluation = MauiArtifactTrustEvaluator.EvaluateLocalReproduction(
            imported,
            local,
            CreateExpectation());

        Assert.Equal(MauiArtifactTrustStates.Untrusted, evaluation.Verification.State);
        Assert.False(evaluation.Binding.Matched);
        Assert.Contains(
            evaluation.Verification.Reasons,
            reason => reason.Code is "failureCode-mismatch" or "failureStep-mismatch" or "observedCheckpoint-mismatch");
    }

    [Fact]
    public void EvaluateLocalReproduction_MissingImportedFact_IsInsufficientNotMismatch()
    {
        var imported = CreateImportedFailure();
        imported.Projection!.AppSourceFingerprint = null;

        var evaluation = MauiArtifactTrustEvaluator.EvaluateLocalReproduction(
            imported,
            CreateMatchingLocalRun(imported.ImportedAt!.Value),
            CreateExpectation());

        Assert.False(evaluation.Binding.Matched);
        Assert.Contains(
            evaluation.Verification.Omissions,
            omission => omission.Field == "imported.appSourceFingerprint");
        Assert.Contains(
            evaluation.Verification.Reasons,
            reason => reason.Code == "missing-imported.appSourceFingerprint");
        Assert.DoesNotContain(
            evaluation.Verification.Reasons,
            reason => reason.Code == "appSourceFingerprint-mismatch");
    }

    [Fact]
    public void EvaluateLocalReproduction_CheckpointIgnoresOccurrenceAgentInstance()
    {
        var imported = CreateImportedFailure();
        var local = CreateMatchingLocalRun(imported.ImportedAt!.Value);
        local.Failure!.ExpectedCheckpoint!.AgentInstanceId = "new-agent-instance";
        local.Failure.ObservedCheckpoint!.AgentInstanceId = "new-agent-instance";

        var evaluation = MauiArtifactTrustEvaluator.EvaluateLocalReproduction(
            imported,
            local,
            CreateExpectation());

        Assert.True(evaluation.Binding.Matched);
        Assert.Equal(MauiArtifactTrustStates.LocallyReproduced, evaluation.Verification.State);
    }

    [Fact]
    public void EvaluateLocalReproduction_ExactDeviceLabelMayDifferWhenCanonicalRuntimeMatches()
    {
        var imported = CreateImportedFailure();
        var local = CreateMatchingLocalRun(imported.ImportedAt!.Value);
        local.DeviceProfile = "different-device-label";

        var evaluation = MauiArtifactTrustEvaluator.EvaluateLocalReproduction(
            imported,
            local,
            CreateExpectation());

        Assert.True(evaluation.Binding.Matched);
    }

    [Fact]
    public void EvaluateLocalReproduction_RuntimeProfileMismatch_FailsClosed()
    {
        var imported = CreateImportedFailure();
        var local = CreateMatchingLocalRun(imported.ImportedAt!.Value);
        local.RuntimeProfileFingerprint = MauiTestExecutionRuntimeProfile.CreateFingerprint(
            new MauiTestExecutionBuildFacts { TargetFramework = "net10.0-android" },
            new MauiTestExecutionDeviceFacts
            {
                Platform = "android",
                RuntimeKind = "physical",
                Profile = "pixel",
                Architecture = "arm64",
                ApiLevel = "35",
                OsVersion = "15",
                FormFactor = "phone",
            });

        var evaluation = MauiArtifactTrustEvaluator.EvaluateLocalReproduction(
            imported,
            local,
            CreateExpectation());

        Assert.False(evaluation.Binding.Matched);
        Assert.Contains(
            evaluation.Verification.Reasons,
            reason => reason.Code == "runtimeProfile-mismatch");
    }

    [Fact]
    public void EvaluateLocalReproduction_MauiTraceUnavailableFacts_AreMissingNotMismatched()
    {
        var imported = CreateImportedFailure();
        imported.ArtifactKind = "mauitrace";
        imported.Projection!.Kind = "mauitrace";
        imported.Projection.DeviceProfileFingerprint =
            MauiArtifactTrustRedactor.Fingerprint("||");
        imported.Projection.RuntimeProfileFingerprint = null;

        var evaluation = MauiArtifactTrustEvaluator.EvaluateLocalReproduction(
            imported,
            CreateMatchingLocalRun(imported.ImportedAt!.Value),
            CreateExpectation());

        Assert.False(evaluation.Binding.Matched);
        Assert.Contains(
            evaluation.Verification.Omissions,
            omission => omission.Field == "imported.appBuildFingerprint");
        Assert.Contains(
            evaluation.Verification.Omissions,
            omission => omission.Field == "imported.runtimeProfile");
        Assert.DoesNotContain(
            evaluation.Verification.Reasons,
            reason => reason.Code is
                "appBuildFingerprint-mismatch" or
                "packageDigest-mismatch" or
                "deviceProfile-mismatch");
    }

    [Fact]
    public void EvaluateLocalReproduction_MalformedImportedFingerprint_IsRejected()
    {
        var imported = CreateImportedFailure();
        imported.Projection!.Failure!.StepFingerprint = "sha256:not-a-digest";

        var evaluation = MauiArtifactTrustEvaluator.EvaluateLocalReproduction(
            imported,
            CreateMatchingLocalRun(imported.ImportedAt!.Value),
            CreateExpectation());

        Assert.False(evaluation.Binding.Matched);
        Assert.Contains(
            evaluation.Verification.Reasons,
            reason => reason.Code == "imported-projection-identifiers-invalid");
    }

    [Fact]
    public void CanCreateProposal_RequiresLocalReproduction_NotEmbeddedLocalLookingIds()
    {
        var identity = MauiImportedArtifactIdentity.Create();
        var artifact = new MauiArtifactTrustRecord
        {
            Identity = identity,
            Verification = new MauiArtifactTrustVerificationResult
            {
                State = MauiArtifactTrustStates.Untrusted,
            },
            Projection = new MauiImportedArtifactSafeProjection
            {
                EmbeddedIdentifierDigests =
                [
                    MauiArtifactTrustRedactor.Fingerprint("run_looks_local")!,
                    MauiArtifactTrustRedactor.Fingerprint("flow_looks_local")!,
                    MauiArtifactTrustRedactor.Fingerprint("proposal_looks_local")!,
                ],
            },
            LocalReproduction = new MauiLocalReproductionBinding
            {
                ImportedArtifact = identity,
                LocalRunId = "run_looks_local",
                Matched = true,
                Verification = new MauiArtifactTrustVerificationResult
                {
                    State = MauiArtifactTrustStates.LocallyReproduced,
                },
            },
        };

        Assert.False(MauiArtifactProposalPolicy.CanCreateProposal(artifact).Allowed);
        Assert.False(MauiFlowRepairProposalTrustPolicy.CanCreateProposal(artifact).Allowed);
        Assert.False(MauiSourceProposalTrustPolicy.CanCreateProposal(artifact).Allowed);

        artifact.Verification.State = MauiArtifactTrustStates.Attested;

        Assert.False(MauiArtifactProposalPolicy.CanCreateProposal(artifact).Allowed);

        artifact.Verification.State = MauiArtifactTrustStates.LocallyReproduced;

        Assert.True(MauiArtifactProposalPolicy.CanCreateProposal(artifact).Allowed);
    }

    private static MauiArtifactProvenanceSubject CreateSubject(string? artifactDigest)
        => new()
        {
            Repository = "dotnet/maui-labs",
            Workflow = ".github/workflows/ci-devflow.yml",
            Commit = "0123456789abcdef",
            Issuer = "https://issuer.example.test",
            ArtifactDigest = artifactDigest,
            AppDigest = "app-digest",
            PackageDigest = "package-digest",
            FlowDigest = "flow-digest",
            Platform = "android",
            DeviceProfile = "pixel",
            PolicyVersion = "trust-v1",
        };

    private static MauiArtifactTrustRecord CreateImportedFailure()
    {
        var expectedCheckpoint = CreateCheckpoint();
        var observedCheckpoint = CreateCheckpoint();
        var projection = new MauiImportedArtifactSafeProjection
        {
            Kind = "flow-run",
            FlowFingerprint = MauiArtifactTrustRedactor.Fingerprint("flow-current"),
            AppBuildFingerprint = MauiArtifactTrustRedactor.Fingerprint("build-current"),
            AppSourceFingerprint = MauiArtifactTrustRedactor.Fingerprint("source-current"),
            PackageFingerprint = MauiArtifactTrustRedactor.Fingerprint("package-current"),
            PlatformFingerprint = MauiArtifactTrustRedactor.Fingerprint("android"),
            DeviceProfileFingerprint = MauiArtifactTrustRedactor.Fingerprint("pixel"),
            RuntimeProfileFingerprint = RuntimeProfileFingerprint(),
            Failure = new MauiImportedFailureProjection
            {
                FailureKey = "if_" + new string('f', 64),
                Code = MauiFlowFailureClasses.LocatorNotFound,
                Class = MauiFlowFailureClasses.LocatorNotFound,
                StepFingerprint = MauiArtifactTrustRedactor.Fingerprint("tap-save"),
                ExpectedCheckpointFingerprint = MauiArtifactTrustRedactor.CheckpointFingerprint(expectedCheckpoint),
                ObservedCheckpointFingerprint = MauiArtifactTrustRedactor.CheckpointFingerprint(observedCheckpoint),
            },
        };

        return new MauiArtifactTrustRecord
        {
            Identity = MauiImportedArtifactIdentity.Create(),
            ArtifactKind = "flow-run",
            ImportedAt = DateTimeOffset.UtcNow,
            Projection = projection,
            Verification = new MauiArtifactTrustVerificationResult
            {
                State = MauiArtifactTrustStates.Untrusted,
            },
        };
    }

    private static MauiLocalReproductionFacts CreateMatchingLocalRun(DateTimeOffset importedAt)
        => new()
        {
            LocalRunId = "run_local_reproduction",
            IsNewLocalRun = true,
            StartedAt = importedAt.AddSeconds(1),
            FlowDigest = "flow-current",
            AppBuildFingerprint = "build-current",
            AppSourceFingerprint = "source-current",
            PackageDigest = "package-current",
            Platform = "android",
            DeviceProfile = "pixel",
            RuntimeProfileFingerprint = RuntimeProfileFingerprint(),
            Failure = new MauiLocalFailureFacts
            {
                Code = MauiFlowFailureClasses.LocatorNotFound,
                Class = MauiFlowFailureClasses.LocatorNotFound,
                StepId = "tap-save",
                ExpectedCheckpoint = CreateCheckpoint(),
                ObservedCheckpoint = CreateCheckpoint(),
            },
        };

    private static MauiLocalReproductionExpectation CreateExpectation()
        => new()
        {
            FlowDigest = "flow-current",
            AppBuildFingerprint = "build-current",
            AppSourceFingerprint = "source-current",
            PackageDigest = "package-current",
            Platform = "android",
            DeviceProfile = "pixel",
            RuntimeProfileFingerprint = RuntimeProfileFingerprint(),
        };

    private static string RuntimeProfileFingerprint()
        => MauiTestExecutionRuntimeProfile.CreateFingerprint(
            new MauiTestExecutionBuildFacts
            {
                TargetFramework = "net10.0-android",
            },
            new MauiTestExecutionDeviceFacts
            {
                Platform = "android",
                RuntimeKind = "emulator",
                Profile = "pixel",
                Architecture = "arm64",
                ApiLevel = "35",
                OsVersion = "15",
                FormFactor = "phone",
            })!;

    private static MauiFlowCheckpoint CreateCheckpoint()
        => new()
        {
            AppBuildFingerprint = "build-current",
            AgentInstanceId = "agent-instance",
            SeedFingerprint = "seed",
            BackendStateFingerprint = "backend",
            Route = "/todos",
            Window = "main",
            Modal = "none",
            Locale = "en-US",
            Theme = "light",
            Orientation = "portrait",
            DisplayProfile = "pixel",
            CollectionItemKey = "todo-1",
        };
}
