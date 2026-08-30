using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Execution;
using Microsoft.Maui.DevFlow.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public sealed class FlowReproductionCoordinatorTests
{
    private static readonly DateTimeOffset ImportedAt =
        new(2026, 8, 11, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset LocalStartedAt =
        ImportedAt.AddMinutes(1);

    // A real invocation's manifest spans build + install + launch + replay + cleanup, while the
    // flow-run report spans only the replay. The two windows are therefore never equal. The fake
    // below reproduces that shape so triage tests exercise the relation the shipped CLI produces.
    private static readonly DateTimeOffset ManifestStartedAt = LocalStartedAt.AddMinutes(-3);
    private static readonly DateTimeOffset ManifestEndedAt = LocalStartedAt.AddSeconds(12);

    [Fact]
    public async Task Reproduce_ExactMatch_WritesDiagnosticOnlyHandoff()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var imported = CreateImported(bundle.FlowDigest);
        var execution = new FakeExecutionCoordinator(bundle.FlowDigest);
        var coordinator = CreateCoordinator(imported, execution);

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.True(result.Ok);
        Assert.Equal(MauiArtifactTrustStates.LocallyReproduced, result.Report.TrustState);
        Assert.Equal("open-inspector-workbench-repair-review", result.Report.ReviewAction);
        Assert.False(result.Report.BrokerBindingPersisted);
        Assert.False(result.Report.ApprovalGranted);
        Assert.False(result.Report.ProposalCreated);
        Assert.Equal(
            result.Report.ImportedRuntimeProfileFingerprint,
            result.Report.LocalRuntimeProfileFingerprint);
        Assert.True(File.Exists(result.ReportPath));
        Assert.Single(execution.Requests);
        Assert.Contains(
            result.Report.LocalArtifacts,
            artifact => artifact.RelativePath == ExecutionManifestWriter.FileName);
        Assert.Contains(
            result.Report.LocalArtifacts,
            artifact => artifact.RelativePath == MauiFlowRunReportSerializer.FileName);
    }

    [Fact]
    public async Task Reproduce_FlowMismatch_FailsClosed()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var imported = CreateImported("different-flow");
        var coordinator = CreateCoordinator(
            imported,
            new FakeExecutionCoordinator(bundle.FlowDigest));

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.False(result.Ok);
        Assert.Equal(MauiArtifactTrustStates.Untrusted, result.Report.TrustState);
        Assert.Contains("flowDigest-mismatch", result.Report.ReasonCodes);
    }

    [Fact]
    public async Task Reproduce_PlatformMismatch_FailsClosed()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var imported = CreateImported(bundle.FlowDigest);
        imported.Projection!.PlatformFingerprint = MauiArtifactTrustRedactor.Fingerprint("ios");
        var coordinator = CreateCoordinator(
            imported,
            new FakeExecutionCoordinator(bundle.FlowDigest));

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.False(result.Ok);
        Assert.Contains("platform-mismatch", result.Report.ReasonCodes);
    }

    [Fact]
    public async Task Reproduce_SignedOccurrenceDigestDriftReportsNormalizedPayloadLimitation()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var imported = CreateImported(bundle.FlowDigest);
        imported.Projection!.AppBuildFingerprint =
            MauiArtifactTrustRedactor.Fingerprint("signed-build-occurrence-a");
        imported.Projection.PackageFingerprint =
            MauiArtifactTrustRedactor.Fingerprint("signed-package-occurrence-a");
        var coordinator = CreateCoordinator(
            imported,
            new FakeExecutionCoordinator(bundle.FlowDigest));

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.False(result.Ok);
        Assert.Contains("signed-occurrence-artifact-differs", result.Report.ReasonCodes);
        Assert.Contains("normalized-payload-identity-unavailable", result.Report.ReasonCodes);
        Assert.Contains("normalizedPayloadDigest", result.Report.MissingFacts);
        Assert.DoesNotContain("packageDigest-mismatch", result.Report.ReasonCodes);
        Assert.DoesNotContain("appBuildFingerprint-mismatch", result.Report.ReasonCodes);
    }

    [Fact]
    public async Task Reproduce_NormalizedPayloadDigestDiffers_NamesADifferenceNotAnAbsence()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var imported = CreateImported(bundle.FlowDigest);
        imported.Projection!.AppBuildFingerprint =
            MauiArtifactTrustRedactor.Fingerprint("signed-build-occurrence-a");
        imported.Projection.PackageFingerprint =
            MauiArtifactTrustRedactor.Fingerprint("signed-package-occurrence-a");
        imported.Projection.NormalizedPayloadFingerprint =
            MauiArtifactTrustRedactor.Fingerprint("sha256:" + new string('a', 64));
        var coordinator = CreateCoordinator(
            imported,
            new FakeExecutionCoordinator(bundle.FlowDigest)
            {
                NormalizedPayloadDigest = "sha256:" + new string('b', 64),
            });

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.False(result.Ok);
        Assert.Contains("signed-occurrence-artifact-differs", result.Report.ReasonCodes);
        Assert.Contains("normalized-payload-identity-differs", result.Report.ReasonCodes);
        Assert.DoesNotContain("normalized-payload-identity-unavailable", result.Report.ReasonCodes);
        Assert.DoesNotContain("normalizedPayloadDigest", result.Report.MissingFacts);
    }

    [Fact]
    public async Task Reproduce_NormalizedPayloadDigestAgrees_StillRefusesBecauseItIsNotAnIdentity()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var payload = "sha256:" + new string('a', 64);
        var imported = CreateImported(bundle.FlowDigest);
        imported.Projection!.AppBuildFingerprint =
            MauiArtifactTrustRedactor.Fingerprint("signed-build-occurrence-a");
        imported.Projection.PackageFingerprint =
            MauiArtifactTrustRedactor.Fingerprint("signed-package-occurrence-a");
        imported.Projection.NormalizedPayloadFingerprint =
            MauiArtifactTrustRedactor.Fingerprint(payload);
        var coordinator = CreateCoordinator(
            imported,
            new FakeExecutionCoordinator(bundle.FlowDigest) { NormalizedPayloadDigest = payload });

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.False(result.Ok);
        Assert.False(result.Report.Matched);
        Assert.Contains("normalized-payload-identity-unproven", result.Report.ReasonCodes);
        Assert.DoesNotContain("normalized-payload-identity-unavailable", result.Report.ReasonCodes);
        Assert.DoesNotContain("normalized-payload-identity-differs", result.Report.ReasonCodes);
    }

    [Fact]
    public async Task Reproduce_MissingImportedFact_IsInsufficientNotMismatch()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var imported = CreateImported(bundle.FlowDigest);
        imported.Projection!.AppSourceFingerprint = null;
        var coordinator = CreateCoordinator(
            imported,
            new FakeExecutionCoordinator(bundle.FlowDigest));

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.False(result.Ok);
        Assert.Contains("imported.appSourceFingerprint", result.Report.MissingFacts);
        Assert.Contains("missing-imported.appSourceFingerprint", result.Report.ReasonCodes);
        Assert.DoesNotContain("appSourceFingerprint-mismatch", result.Report.ReasonCodes);
    }

    [Fact]
    public async Task Reproduce_AttestedMismatch_RemainsDiagnosticOnly()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var imported = CreateImported("different-flow");
        imported.Verification.State = MauiArtifactTrustStates.Attested;
        var coordinator = CreateCoordinator(
            imported,
            new FakeExecutionCoordinator(bundle.FlowDigest));

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.False(result.Ok);
        Assert.Equal(MauiArtifactTrustStates.Attested, result.Report.ImportTrustState);
        Assert.Equal(MauiArtifactTrustStates.Attested, result.Report.TrustState);
        Assert.False(result.Report.BrokerBindingPersisted);
        Assert.False(result.Report.ApprovalGranted);
        Assert.False(result.Report.ProposalCreated);
    }

    [Fact]
    public async Task Reproduce_InfrastructureFailure_RefusesTrustUpgrade()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var imported = CreateImported(bundle.FlowDigest);
        var coordinator = CreateCoordinator(
            imported,
            new FakeExecutionCoordinator(
                bundle.FlowDigest,
                FlowExecutionExitCategories.InfrastructureFailure));

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.False(result.Ok);
        Assert.Equal(MauiArtifactTrustStates.Untrusted, result.Report.TrustState);
        Assert.Contains("local-infrastructure-failure", result.Report.ReasonCodes);
    }

    [Theory]
    [InlineData(MauiFlowRunOutcomes.Cancelled, MauiFlowFailureClasses.Cancelled)]
    [InlineData(MauiFlowRunOutcomes.TimedOut, MauiFlowFailureClasses.Timeout)]
    [InlineData(MauiFlowRunOutcomes.LeaseLost, MauiFlowFailureClasses.LeaseLost)]
    [InlineData(MauiFlowRunOutcomes.Orphaned, MauiFlowFailureClasses.AgentDisconnected)]
    [InlineData(MauiFlowRunOutcomes.UnknownCompletion, MauiFlowFailureClasses.UnknownCompletion)]
    public async Task Reproduce_LocalLifecycleOutcome_RefusesTrustUpgrade(
        string outcome,
        string failureClass)
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var coordinator = CreateCoordinator(
            CreateImported(bundle.FlowDigest),
            new FakeExecutionCoordinator(bundle.FlowDigest)
            {
                ForcedOutcomeStatus = outcome,
                ForcedFailureClass = failureClass,
            });

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.False(result.Ok);
        Assert.Contains("local-lifecycle-outcome-refused", result.Report.ReasonCodes);
    }

    [Theory]
    [InlineData(MauiFlowRunOutcomes.Cancelled)]
    [InlineData(MauiFlowRunOutcomes.TimedOut)]
    [InlineData(MauiFlowRunOutcomes.LeaseLost)]
    [InlineData(MauiFlowRunOutcomes.Orphaned)]
    [InlineData(MauiFlowRunOutcomes.UnknownCompletion)]
    public async Task Reproduce_ImportedLifecycleOutcome_RefusesTrustUpgrade(string outcome)
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var imported = CreateImported(bundle.FlowDigest);
        imported.Projection!.Outcome = outcome;
        var coordinator = CreateCoordinator(
            imported,
            new FakeExecutionCoordinator(bundle.FlowDigest));

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.False(result.Ok);
        Assert.Contains(
            "imported-infrastructure-or-unknown-completion",
            result.Report.ReasonCodes);
    }

    [Fact]
    public async Task Reproduce_CheckpointAgentOccurrenceChangesButCanonicalRuntimeMatches()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var coordinator = CreateCoordinator(
            CreateImported(bundle.FlowDigest),
            new FakeExecutionCoordinator(bundle.FlowDigest)
            {
                AgentInstanceId = "different-local-agent",
            });

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task Reproduce_RuntimeProfileMismatch_RefusesTrustUpgrade()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var coordinator = CreateCoordinator(
            CreateImported(bundle.FlowDigest),
            new FakeExecutionCoordinator(bundle.FlowDigest)
            {
                RuntimeKind = "physical",
            });

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.False(result.Ok);
        Assert.Contains("runtimeProfile-mismatch", result.Report.ReasonCodes);
    }

    [Fact]
    public async Task Reproduce_CleanupInfrastructureAfterPrimaryTestFailure_RefusesTrustUpgrade()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var imported = CreateImported(bundle.FlowDigest);
        var execution = new FakeExecutionCoordinator(
            bundle.FlowDigest,
            FlowExecutionExitCategories.InfrastructureFailure)
        {
            PrimaryTestFailureWithInfrastructureExit = true,
        };
        var coordinator = CreateCoordinator(imported, execution);

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.False(result.Ok);
        Assert.Equal(MauiArtifactTrustStates.Untrusted, result.Report.TrustState);
        Assert.Contains("local-infrastructure-failure", result.Report.ReasonCodes);
    }

    [Fact]
    public async Task Reproduce_AbsentIndependentOracle_RefusesSelectorRepairHandoff()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle(includeIndependentOracle: false);
        var coordinator = CreateCoordinator(
            CreateImported(bundle.FlowDigest),
            new FakeExecutionCoordinator(bundle.FlowDigest));

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.False(result.Ok);
        Assert.Contains("independent-oracle-absent", result.Report.ReasonCodes);
        Assert.DoesNotContain("locally-reproduced", result.Report.ReasonCodes);
    }

    [Fact]
    public async Task Reproduce_TruncatedLocalReport_RefusesTheSelectorRepairHandoff()
    {
        // A truncated report dropped steps, oracles, or replay eligibility to fit a size budget, so
        // the absence of a disqualifying fact in it is not proof that the fact is absent. Upgrading
        // trust and pointing a human at the Inspector repair review off that evidence would ask for
        // a repair decision the repair eligibility gate itself refuses to make.
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var coordinator = CreateCoordinator(
            CreateImported(bundle.FlowDigest),
            new FakeExecutionCoordinator(bundle.FlowDigest) { TruncateReport = true });

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));

        Assert.False(result.Ok);
        Assert.Contains("local-report-truncated", result.Report.ReasonCodes);
        Assert.NotEqual(MauiArtifactTrustStates.LocallyReproduced, result.Report.TrustState);
        Assert.NotEqual("open-inspector-workbench-repair-review", result.Report.ReviewAction);
        Assert.False(result.Report.ProposalCreated);
        Assert.False(result.Report.ApprovalGranted);
    }

    [Fact]
    public async Task Reproduce_StalePlanBinding_DoesNotStartLocalExecution()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var changed = FlowMarkdown.Parse(await File.ReadAllTextAsync(bundle.Flow), bundle.Flow).Flow!;
        changed.Name = "changed-after-plan";
        await File.WriteAllTextAsync(bundle.Flow, FlowMarkdown.Serialize(changed));
        var execution = new FakeExecutionCoordinator(bundle.FlowDigest);
        var coordinator = CreateCoordinator(CreateImported(bundle.FlowDigest), execution);

        var exception = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            coordinator.ReproduceAsync(Request(workspace, bundle)));

        Assert.Equal("plan-flow-digest-stale", exception.Code);
        Assert.Empty(execution.Requests);
        Assert.False(Directory.Exists(workspace.Output));
    }

    [Fact]
    public async Task Reproduce_OutputIsRedactedAndConfinedToLocalArtifactNames()
    {
        const string secret = "CorrectHorseBatteryStaple";
        const string deviceSerial = "emulator-5554";
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var imported = CreateImported(bundle.FlowDigest);
        imported.ExtensionData = new Dictionary<string, JsonElement>
        {
            ["prompt"] = JsonSerializer.SerializeToElement($"reveal {secret}"),
        };
        var execution = new FakeExecutionCoordinator(bundle.FlowDigest)
        {
            Secret = secret,
            DeviceSerial = deviceSerial,
            ReturnHostilePaths = true,
        };
        var coordinator = CreateCoordinator(imported, execution);

        var result = await coordinator.ReproduceAsync(Request(workspace, bundle));
        var json = await File.ReadAllTextAsync(result.ReportPath);

        Assert.True(result.Ok);
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain(deviceSerial, json, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"prompt\"", json, StringComparison.Ordinal);
        Assert.All(
            result.Report.LocalArtifacts.Where(static artifact => artifact.RelativePath is not null),
            artifact =>
            {
                Assert.False(Path.IsPathRooted(artifact.RelativePath));
                Assert.DoesNotContain("..", artifact.RelativePath, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task Triage_ImportedInputs_AreDeterministicAndNeverRepairEligible()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var execution = new FakeExecutionCoordinator(bundle.FlowDigest);
        var local = await execution.RunAsync(new FlowExecutionRequest
        {
            FlowPath = bundle.Flow,
            PlanPath = bundle.Plan,
            ProjectPath = workspace.Project,
            Platform = "android",
            OutputDirectory = workspace.Output,
        });
        var coordinator = new FlowTriageCoordinator();

        var first = await coordinator.AnalyzeAsync(new FlowTriageRequest
        {
            ManifestPath = local.ManifestPath!,
            ReportPath = local.ReportPath!,
            Format = FlowTriageOutputFormats.Json,
        });
        var second = await coordinator.AnalyzeAsync(new FlowTriageRequest
        {
            ManifestPath = local.ManifestPath!,
            ReportPath = local.ReportPath!,
            Format = FlowTriageOutputFormats.Json,
        });
        var markdown = await coordinator.AnalyzeAsync(new FlowTriageRequest
        {
            ManifestPath = local.ManifestPath!,
            ReportPath = local.ReportPath!,
            Format = FlowTriageOutputFormats.Markdown,
        });

        Assert.Equal(first.Content, second.Content);
        Assert.True(first.Triage.ImportedEvidence);
        Assert.Equal(MauiFlowTriageEvidenceStates.Sufficient, first.Triage.Evidence.State);
        Assert.False(first.Triage.RepairEligible);
        Assert.True(first.Triage.LocalReproductionRequired);
        Assert.Contains(MauiFlowTriageNextActions.ReproduceLocally, first.Triage.AllowedNextActions);
        Assert.Contains("# MAUI DevFlow triage", Encoding.UTF8.GetString(markdown.Content), StringComparison.Ordinal);
        var markdownText = Encoding.UTF8.GetString(markdown.Content);
        Assert.Contains("- Class: `locator-not-found`", markdownText, StringComparison.Ordinal);
        Assert.Contains("- Code: `locator-not-found`", markdownText, StringComparison.Ordinal);
        Assert.Contains("- Category: `selector`", markdownText, StringComparison.Ordinal);
        Assert.Contains("- Phase: `resolution`", markdownText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Triage_ManifestWindowWiderThanReportWindow_IsAcceptedByLifecycleContainment()
    {
        // Regression: the coordinator used to require manifest.Lifecycle.StartedAt/EndedAt to equal
        // report.StartedAt/EndedAt. The manifest spans the whole invocation (build, install, launch,
        // replay, cleanup) while the report spans only the replay, so the two are never equal and
        // every real `flow run` output was unusable by `flow triage`.
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var local = await new FakeExecutionCoordinator(bundle.FlowDigest).RunAsync(new FlowExecutionRequest
        {
            FlowPath = bundle.Flow,
            PlanPath = bundle.Plan,
            ProjectPath = workspace.Project,
            Platform = "android",
            OutputDirectory = workspace.Output,
        });

        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(local.ManifestPath!))!.AsObject();
        var report = JsonNode.Parse(await File.ReadAllTextAsync(local.ReportPath!))!.AsObject();
        var manifestStarted = DateTimeOffset.Parse(
            manifest["lifecycle"]!["startedAt"]!.GetValue<string>(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        var manifestEnded = DateTimeOffset.Parse(
            manifest["lifecycle"]!["endedAt"]!.GetValue<string>(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        var reportStarted = DateTimeOffset.Parse(
            report["startedAt"]!.GetValue<string>(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        var reportEnded = DateTimeOffset.Parse(
            report["endedAt"]!.GetValue<string>(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        Assert.True(manifestStarted < reportStarted, "fixture must model a manifest that starts first");
        Assert.True(manifestEnded > reportEnded, "fixture must model a manifest that ends last");

        var result = await new FlowTriageCoordinator().AnalyzeAsync(new FlowTriageRequest
        {
            ManifestPath = local.ManifestPath!,
            ReportPath = local.ReportPath!,
            Format = FlowTriageOutputFormats.Json,
        });

        Assert.Equal(MauiFlowTriageEvidenceStates.Sufficient, result.Triage.Evidence.State);
        Assert.False(result.Triage.RepairEligible);
    }

    [Theory]
    // Manifest starts after the replay began -> the replay is not inside this invocation.
    [InlineData(1, 12, "triage-started-at-mismatch")]
    // Manifest ends before the replay finished -> same.
    [InlineData(-180, 2, "triage-ended-at-mismatch")]
    public async Task Triage_ReportWindowOutsideManifestWindow_IsStillRejected(
        int manifestStartOffsetSeconds,
        int manifestEndOffsetSeconds,
        string expectedCode)
    {
        // Containment replaced equality, but the report still has to describe work the manifest
        // actually covers. A replay that began before the invocation, or ended after it, is not
        // evidence from that invocation. The fake report runs LocalStartedAt .. +5s.
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var local = await new FakeExecutionCoordinator(bundle.FlowDigest).RunAsync(new FlowExecutionRequest
        {
            FlowPath = bundle.Flow,
            PlanPath = bundle.Plan,
            ProjectPath = workspace.Project,
            Platform = "android",
            OutputDirectory = workspace.Output,
        });
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(local.ManifestPath!))!.AsObject();
        manifest["lifecycle"]!["startedAt"] = LocalStartedAt
            .AddSeconds(manifestStartOffsetSeconds)
            .ToString("O", CultureInfo.InvariantCulture);
        manifest["lifecycle"]!["endedAt"] = LocalStartedAt
            .AddSeconds(manifestEndOffsetSeconds)
            .ToString("O", CultureInfo.InvariantCulture);
        await File.WriteAllTextAsync(local.ManifestPath!, manifest.ToJsonString());

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            new FlowTriageCoordinator().AnalyzeAsync(new FlowTriageRequest
            {
                ManifestPath = local.ManifestPath!,
                ReportPath = local.ReportPath!,
            }));

        Assert.Equal(expectedCode, failure.Code);
    }

    [Fact]
    public async Task Triage_InvertedManifestWindow_IsRejectedAsMalformed()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var local = await new FakeExecutionCoordinator(bundle.FlowDigest).RunAsync(new FlowExecutionRequest
        {
            FlowPath = bundle.Flow,
            PlanPath = bundle.Plan,
            ProjectPath = workspace.Project,
            Platform = "android",
            OutputDirectory = workspace.Output,
        });
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(local.ManifestPath!))!.AsObject();
        manifest["lifecycle"]!["startedAt"] = ManifestEndedAt.ToString("O", CultureInfo.InvariantCulture);
        manifest["lifecycle"]!["endedAt"] = ManifestStartedAt.ToString("O", CultureInfo.InvariantCulture);
        await File.WriteAllTextAsync(local.ManifestPath!, manifest.ToJsonString());

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            new FlowTriageCoordinator().AnalyzeAsync(new FlowTriageRequest
            {
                ManifestPath = local.ManifestPath!,
                ReportPath = local.ReportPath!,
            }));

        Assert.Equal("triage-lifecycle-window-invalid", failure.Code);
    }

    [Fact]
    public async Task Triage_TamperedReportBytesAreRejectedByManifestArtifactBinding()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var local = await new FakeExecutionCoordinator(bundle.FlowDigest).RunAsync(new FlowExecutionRequest
        {
            FlowPath = bundle.Flow,
            PlanPath = bundle.Plan,
            ProjectPath = workspace.Project,
            Platform = "android",
            OutputDirectory = workspace.Output,
        });
        var report = JsonNode.Parse(await File.ReadAllTextAsync(local.ReportPath!))!.AsObject();
        report["legacyFlowIdentity"] = "tampered-flow.md";
        await File.WriteAllTextAsync(local.ReportPath!, report.ToJsonString());

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            new FlowTriageCoordinator().AnalyzeAsync(new FlowTriageRequest
            {
                ManifestPath = local.ManifestPath!,
                ReportPath = local.ReportPath!,
            }));

        Assert.Equal("triage-report-artifact-binding-mismatch", failure.Code);
    }

    [Fact]
    public async Task Triage_RunIdMismatchIsRejectedBeforeEvidenceIsSufficient()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var local = await new FakeExecutionCoordinator(bundle.FlowDigest).RunAsync(new FlowExecutionRequest
        {
            FlowPath = bundle.Flow,
            PlanPath = bundle.Plan,
            ProjectPath = workspace.Project,
            Platform = "android",
            OutputDirectory = workspace.Output,
        });
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(local.ManifestPath!))!.AsObject();
        manifest["runId"] = "different-run";
        await File.WriteAllTextAsync(local.ManifestPath!, manifest.ToJsonString());

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            new FlowTriageCoordinator().AnalyzeAsync(new FlowTriageRequest
            {
                ManifestPath = local.ManifestPath!,
                ReportPath = local.ReportPath!,
            }));

        Assert.Equal("triage-run-id-mismatch", failure.Code);
    }

    [Fact]
    public async Task Triage_FlowDigestMismatchIsRejectedBeforeEvidenceIsSufficient()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var local = await new FakeExecutionCoordinator(bundle.FlowDigest).RunAsync(new FlowExecutionRequest
        {
            FlowPath = bundle.Flow,
            PlanPath = bundle.Plan,
            ProjectPath = workspace.Project,
            Platform = "android",
            OutputDirectory = workspace.Output,
        });
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(local.ManifestPath!))!.AsObject();
        manifest["flowDigest"] = "different-flow";
        await File.WriteAllTextAsync(local.ManifestPath!, manifest.ToJsonString());

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            new FlowTriageCoordinator().AnalyzeAsync(new FlowTriageRequest
            {
                ManifestPath = local.ManifestPath!,
                ReportPath = local.ReportPath!,
            }));

        Assert.Equal("triage-flow-digest-mismatch", failure.Code);
    }

    [Fact]
    public async Task Triage_DuplicatedAppIdentityMismatchIsRejected()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var local = await new FakeExecutionCoordinator(bundle.FlowDigest).RunAsync(new FlowExecutionRequest
        {
            FlowPath = bundle.Flow,
            PlanPath = bundle.Plan,
            ProjectPath = workspace.Project,
            Platform = "android",
            OutputDirectory = workspace.Output,
        });
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(local.ManifestPath!))!.AsObject();
        manifest["build"]!["appId"] = "different.app";
        await File.WriteAllTextAsync(local.ManifestPath!, manifest.ToJsonString());

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            new FlowTriageCoordinator().AnalyzeAsync(new FlowTriageRequest
            {
                ManifestPath = local.ManifestPath!,
                ReportPath = local.ReportPath!,
            }));

        Assert.Equal("triage-app-id-mismatch", failure.Code);
    }

    [Fact]
    public async Task Triage_FingerprintParityMismatchIsInsufficient()
    {
        using var workspace = new ReproductionWorkspace();
        var bundle = workspace.WriteBundle();
        var local = await new FakeExecutionCoordinator(bundle.FlowDigest).RunAsync(new FlowExecutionRequest
        {
            FlowPath = bundle.Flow,
            PlanPath = bundle.Plan,
            ProjectPath = workspace.Project,
            Platform = "android",
            OutputDirectory = workspace.Output,
        });
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(local.ManifestPath!))!.AsObject();
        manifest["incidentFingerprint"] = "sha256:" + new string('f', 64);
        await File.WriteAllTextAsync(local.ManifestPath!, manifest.ToJsonString());

        var result = await new FlowTriageCoordinator().AnalyzeAsync(new FlowTriageRequest
        {
            ManifestPath = local.ManifestPath!,
            ReportPath = local.ReportPath!,
        });

        Assert.Equal(MauiFlowTriageEvidenceStates.Insufficient, result.Triage.Evidence.State);
        Assert.Contains("incident-fingerprint-match", result.Triage.Evidence.MissingFacts);
    }

    [Fact]
    public async Task Triage_PreflightFailureWithoutFlowBuildOrDevice_IsSufficient()
    {
        using var workspace = new ReproductionWorkspace();
        var startedAt = LocalStartedAt;
        var report = new MauiFlowRunReport
        {
            RunId = "run-preflight",
            StartedAt = startedAt,
            EndedAt = startedAt.AddSeconds(1),
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.Failed,
                Terminal = true,
                Verified = false,
            },
            Failure = new MauiFlowFailure
            {
                Class = MauiFlowFailureClasses.FlowInvalid,
                Code = "project-path-missing",
                Category = "flow",
                Phase = "validation",
                Retryable = false,
            },
        };
        var reportFile = new FlowRunReportWriter().Create(report);
        var manifest = new MauiTestExecutionManifest
        {
            ManifestId = "manifest-preflight",
            RunId = report.RunId,
            Host = new MauiTestExecutionHostFacts { Os = "windows" },
            Lifecycle = new MauiTestExecutionLifecycleFacts
            {
                StartedAt = startedAt,
                EndedAt = report.EndedAt,
                Stages =
                [
                    new MauiTestExecutionLifecycleStage
                    {
                        Sequence = 1,
                        Name = "validate-request",
                        Status = "failed",
                        DetailCode = "project-path-missing",
                    },
                ],
            },
            Outcome = new MauiTestExecutionOutcomeFacts
            {
                Status = MauiFlowRunOutcomes.Failed,
                ExitCategory = FlowExecutionExitCategories.InvalidConfiguration,
                Terminal = true,
                Verified = false,
                UnknownCompletion = false,
            },
            Artifacts =
            [
                new MauiTestExecutionArtifact
                {
                    ArtifactId = "flow-run",
                    Role = "semantic-report",
                    Kind = "flow-run-report",
                    MediaType = "application/json",
                    Digest = reportFile.Digest,
                    SizeBytes = reportFile.SizeBytes,
                    RelativePath = reportFile.FileName,
                    Redacted = true,
                },
            ],
        };
        var manifestFile = new ExecutionManifestWriter().Create(manifest);
        await new ImmutableExecutionOutputWriter().WriteAsync(
            workspace.Output,
            [reportFile, manifestFile]);

        var result = await new FlowTriageCoordinator().AnalyzeAsync(new FlowTriageRequest
        {
            ManifestPath = Path.Combine(workspace.Output, manifestFile.FileName),
            ReportPath = Path.Combine(workspace.Output, reportFile.FileName),
        });

        Assert.Equal(MauiFlowTriageEvidenceStates.Sufficient, result.Triage.Evidence.State);
    }

    private static FlowReproductionCoordinator CreateCoordinator(
        MauiArtifactTrustRecord artifact,
        IFlowExecutionCoordinator execution)
        => new(
            execution,
            new FakeArtifactImporter(artifact),
            new CommittedFlowBundleLoader(),
            new ImmutableExecutionOutputWriter(),
            new FixedTimeProvider(ImportedAt.AddMinutes(2)));

    private static FlowReproductionRequest Request(
        ReproductionWorkspace workspace,
        (string Flow, string Plan, string FlowDigest) bundle)
        => new()
        {
            ImportedArtifactPath = workspace.ImportedArtifact,
            Execution = new FlowExecutionRequest
            {
                FlowPath = bundle.Flow,
                PlanPath = bundle.Plan,
                ProjectPath = workspace.Project,
                Platform = "android",
                TargetFramework = "net10.0-android",
                Configuration = "Debug",
                DeviceSerial = "emulator-5554",
                OutputDirectory = workspace.Output,
                CleanupPolicy = FlowExecutionCleanupPolicies.Stop,
            },
        };

    private static MauiArtifactTrustRecord CreateImported(string flowDigest)
    {
        var checkpoint = Checkpoint();
        return new MauiArtifactTrustRecord
        {
            Identity = MauiImportedArtifactIdentity.Create(),
            ArtifactKind = ArtifactTrustImportKinds.FlowRun,
            ImportedAt = ImportedAt,
            Integrity = new MauiArtifactIntegrityVerification
            {
                ArtifactDigest = new string('a', 64),
                Verified = true,
                IntegrityOnly = true,
            },
            Verification = new MauiArtifactTrustVerificationResult
            {
                State = MauiArtifactTrustStates.Untrusted,
            },
            Projection = new MauiImportedArtifactSafeProjection
            {
                Kind = ArtifactTrustImportKinds.FlowRun,
                SourceSchema = "flow-run-report-v1",
                FlowFingerprint = MauiArtifactTrustRedactor.Fingerprint(flowDigest),
                AppBuildFingerprint = MauiArtifactTrustRedactor.Fingerprint("build-current"),
                AppSourceFingerprint = MauiArtifactTrustRedactor.Fingerprint("source-current"),
                PackageFingerprint = MauiArtifactTrustRedactor.Fingerprint("package-current"),
                PlatformFingerprint = MauiArtifactTrustRedactor.Fingerprint("android"),
                DeviceProfileFingerprint = MauiArtifactTrustRedactor.Fingerprint("emulator"),
                RuntimeProfileFingerprint = RuntimeProfileFingerprint(),
                Outcome = MauiFlowRunOutcomes.Failed,
                Failure = new MauiImportedFailureProjection
                {
                    FailureKey = "if_" + new string('b', 64),
                    Code = MauiFlowFailureClasses.LocatorNotFound,
                    Class = MauiFlowFailureClasses.LocatorNotFound,
                    StepFingerprint = MauiArtifactTrustRedactor.Fingerprint("tap-save"),
                    ExpectedCheckpointFingerprint = MauiArtifactTrustRedactor.CheckpointFingerprint(checkpoint),
                    ObservedCheckpointFingerprint = MauiArtifactTrustRedactor.CheckpointFingerprint(checkpoint),
                },
            },
        };
    }

    private static MauiFlowCheckpoint Checkpoint() => new()
    {
        AppBuildFingerprint = "build-current",
        AgentInstanceId = "agent-current",
        SeedFingerprint = "seed-current",
        BackendStateFingerprint = "backend-current",
        Route = "/checkout",
        Window = "main",
        Modal = "none",
        Locale = "en-US",
        Theme = "light",
        Orientation = "portrait",
        DisplayProfile = "emulator",
        CollectionItemKey = "order-current",
    };

    private sealed class FakeArtifactImporter(MauiArtifactTrustRecord artifact)
        : IArtifactTrustImporter
    {
        public ArtifactTrustImportResult Import(
            Stream input,
            string artifactKind,
            MauiArtifactTrustPolicy? policy = null,
            MauiArtifactVerifiedProvenanceFacts? verifiedProvenance = null,
            CancellationToken cancellationToken = default)
            => new()
            {
                Ok = true,
                Artifact = artifact,
                BytesRead = input.CanSeek ? input.Length : 0,
            };
    }

    private sealed class FakeExecutionCoordinator(
        string flowDigest,
        string exitCategory = FlowExecutionExitCategories.TestFailure)
        : IFlowExecutionCoordinator
    {
        public List<FlowExecutionRequest> Requests { get; } = [];
        public string? Secret { get; init; }
        public string? DeviceSerial { get; init; }
        public bool ReturnHostilePaths { get; init; }
        public bool PrimaryTestFailureWithInfrastructureExit { get; init; }
        public string? ForcedOutcomeStatus { get; init; }
        public string? ForcedFailureClass { get; init; }
        public string? LifecycleDetailCode { get; init; }
        public string RuntimeKind { get; init; } = "emulator";
        public string AgentInstanceId { get; init; } = "agent-current";
        public string? NormalizedPayloadDigest { get; init; }
        public bool TruncateReport { get; init; }

        public async Task<FlowExecutionResult> RunAsync(
            FlowExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var output = Path.GetFullPath(request.OutputDirectory!);
            var checkpoint = Checkpoint();
            checkpoint.AgentInstanceId = AgentInstanceId;
            var infrastructure =
                exitCategory == FlowExecutionExitCategories.InfrastructureFailure &&
                !PrimaryTestFailureWithInfrastructureExit;
            var outcomeStatus = ForcedOutcomeStatus ??
                (infrastructure
                    ? MauiFlowRunOutcomes.InfrastructureError
                    : MauiFlowRunOutcomes.Failed);
            var failureClass = ForcedFailureClass ??
                (infrastructure
                    ? MauiFlowFailureClasses.Infrastructure
                    : MauiFlowFailureClasses.LocatorNotFound);
            var report = new MauiFlowRunReport
            {
                RunId = "run-local",
                FlowId = "checkout-flow",
                FlowDigest = flowDigest,
                SideEffectPolicy = MauiFlowSideEffectPolicies.None,
                StartedAt = LocalStartedAt,
                EndedAt = LocalStartedAt.AddSeconds(5),
                Target = new MauiFlowRunTarget
                {
                    Platform = "android",
                    DeviceId = DeviceSerial,
                    DeviceProfile = "emulator",
                    AppId = "com.example.app",
                    AppBuildFingerprint = "build-current",
                    AppSourceFingerprint = "source-current",
                    PackageDigest = "package-current",
                    NormalizedPayloadDigest = NormalizedPayloadDigest,
                },
                Outcome = new MauiFlowRunOutcome
                {
                    Status = outcomeStatus,
                    Terminal = true,
                    Verified = false,
                    Summary = Secret,
                },
                Failure = new MauiFlowFailure
                {
                    FailureId = "failure-local",
                    Code = failureClass,
                    Class = failureClass,
                    Category = infrastructure ? "infrastructure" : "selector",
                    Phase = infrastructure ? "execution" : "resolution",
                    StepId = infrastructure ? null : "tap-save",
                    Message = Secret,
                    RepairEligible = false,
                },
                Steps = infrastructure
                    ? []
                    :
                    [
                        new MauiFlowStepAttempt
                        {
                            StepId = "tap-save",
                            Sequence = 1,
                            Action = FlowActions.Tap,
                            FailureClass = MauiFlowFailureClasses.LocatorNotFound,
                            ExpectedCheckpoint = checkpoint,
                            ObservedCheckpoint = checkpoint,
                        },
                    ],
            };
            if (TruncateReport)
            {
                report.Truncated = true;
                report.TruncationReason = "The report was reduced to satisfy the JSON size limit.";
            }
            var reportFile = new FlowRunReportWriter().Create(report);
            var junitFile = new JUnitFlowExecutionWriter().Create(report, exitCategory, exitCategory);
            var manifest = new MauiTestExecutionManifest
            {
                ManifestId = "manifest-local",
                RunId = "run-local",
                FlowId = "checkout-flow",
                FlowDigest = flowDigest,
                Host = new MauiTestExecutionHostFacts
                {
                    Os = "windows",
                    Architecture = "x64",
                    Runtime = "net10.0",
                    RunnerKind = "unit-test",
                },
                Build = new MauiTestExecutionBuildFacts
                {
                    ProjectFingerprint = new string('c', 64),
                    SourceRevision = "0123456789abcdef0123456789abcdef01234567",
                    TargetFramework = "net10.0-android",
                    Configuration = "Debug",
                    AppId = "com.example.app",
                    AppBuildFingerprint = "build-current",
                    AppSourceFingerprint = "source-current",
                    PackageDigest = "package-current",
                },
                Device = new MauiTestExecutionDeviceFacts
                {
                    Platform = "android",
                    RuntimeKind = RuntimeKind,
                    Profile = "emulator",
                    Architecture = "x64",
                    ApiLevel = "35",
                    OsVersion = "35",
                    FormFactor = "phone",
                    DeviceIdentityFingerprint = DeviceSerial,
                },
                Lifecycle = new MauiTestExecutionLifecycleFacts
                {
                    StartedAt = ManifestStartedAt,
                    EndedAt = ManifestEndedAt,
                    CleanupPolicy = FlowExecutionCleanupPolicies.Stop,
                    CleanupCompleted = true,
                    Stages = LifecycleDetailCode is null
                        ? []
                        :
                        [
                            new MauiTestExecutionLifecycleStage
                            {
                                Sequence = 1,
                                Name = "replay",
                                Status = "failed",
                                DetailCode = LifecycleDetailCode,
                            },
                        ],
                },
                Outcome = new MauiTestExecutionOutcomeFacts
                {
                    Status = report.Outcome.Status,
                    ExitCategory = exitCategory,
                    Terminal = true,
                    Verified = false,
                    UnknownCompletion = outcomeStatus is
                        MauiFlowRunOutcomes.UnknownCompletion or
                        MauiFlowRunOutcomes.Cancelled or
                        MauiFlowRunOutcomes.TimedOut or
                        MauiFlowRunOutcomes.LeaseLost or
                        MauiFlowRunOutcomes.Orphaned,
                },
                Artifacts =
                [
                    new MauiTestExecutionArtifact
                    {
                        ArtifactId = "flow-run",
                        Role = "semantic-report",
                        Kind = "flow-run-report",
                        MediaType = "application/json",
                        Digest = reportFile.Digest,
                        SizeBytes = reportFile.SizeBytes,
                        RelativePath = MauiFlowRunReportSerializer.FileName,
                        Redacted = true,
                    },
                    new MauiTestExecutionArtifact
                    {
                        ArtifactId = "hostile",
                        Kind = "raw-log",
                        Digest = new string('d', 64),
                        RelativePath = @"C:\Users\person\raw.log",
                        Redacted = false,
                    },
                ],
            };
            var parsedFlow = FlowMarkdown.Parse(
                await File.ReadAllTextAsync(request.FlowPath, cancellationToken),
                request.FlowPath).Flow;
            var triage = MauiFlowTriageAnalyzer.Analyze(new MauiFlowTriageInput
            {
                Flow = parsedFlow,
                Report = report,
                Manifest = manifest,
                IsCurrentLocalRun = true,
            });
            manifest.TestIdentityFingerprint = triage.TestIdentityFingerprint;
            manifest.IncidentFingerprint = triage.IncidentFingerprint;
            manifest.OccurrenceFingerprint = triage.OccurrenceFingerprint;
            var manifestFile = new ExecutionManifestWriter().Create(manifest);
            await new ImmutableExecutionOutputWriter().WriteAsync(
                output,
                [reportFile, junitFile, manifestFile],
                cancellationToken);
            return new FlowExecutionResult
            {
                ExitCategory = exitCategory,
                Message = Secret,
                OutputDirectory = output,
                ManifestPath = ReturnHostilePaths ? @"C:\Users\person\outside-manifest.json" : Path.Combine(output, manifestFile.FileName),
                ReportPath = ReturnHostilePaths ? @"C:\Users\person\outside-report.json" : Path.Combine(output, reportFile.FileName),
                JUnitPath = ReturnHostilePaths ? @"C:\Users\person\outside.xml" : Path.Combine(output, junitFile.FileName),
                Report = report,
                Manifest = manifest,
            };
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

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
                Profile = "emulator",
                Architecture = "x64",
                ApiLevel = "35",
                OsVersion = "35",
                FormFactor = "phone",
            })!;

    private sealed class ReproductionWorkspace : IDisposable
    {
        public ReproductionWorkspace()
        {
            Root = Path.Combine(
                FindRepositoryRoot(),
                "artifacts",
                "TestResults",
                "flow-reproduction-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Project = Path.Combine(Root, "App.csproj");
            ImportedArtifact = Path.Combine(Root, "imported-flow-run.json");
            File.WriteAllText(Project, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(ImportedArtifact, "{}");
        }

        public string Root { get; }
        public string Project { get; }
        public string ImportedArtifact { get; }
        public string Output => Path.Combine(Root, "output");

        public (string Flow, string Plan, string FlowDigest) WriteBundle(
            bool includeIndependentOracle = true)
        {
            const string flowFileName = "checkout.md";
            var flow = new MauiFlow
            {
                Name = "checkout",
                App = "App",
                Platform = "android",
                Steps =
                [
                    new FlowStep
                    {
                        Seq = 1,
                        StepId = "tap-save",
                        Action = FlowActions.Tap,
                        Target = new FlowSelector { AutomationId = "SaveButton" },
                    },
                ],
            };
            var digest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow);
            var plan = new MauiTestPlan
            {
                PlanId = "plan-checkout",
                Revision = 1,
                Flow = new MauiFlowReference
                {
                    Path = flowFileName,
                    Digest = digest,
                },
                Goal = "Reproduce checkout selector failure.",
                Reset = new MauiTestResetRequirement
                {
                    Required = false,
                },
                RequiredPlatforms = ["android"],
                SideEffectPolicy = MauiFlowSideEffectPolicies.None,
                IndependentBusinessOracles = includeIndependentOracle
                    ?
                    [
                        new MauiIndependentBusinessOracleDeclaration
                        {
                            OracleId = "order-saved",
                            Required = true,
                            Independent = true,
                        },
                    ]
                    : [],
                Provenance = new MauiActorProvenance
                {
                    ActorKind = "human",
                    Channel = "unit-test",
                },
            };
            var flowPath = Path.Combine(Root, flowFileName);
            var planPath = Path.Combine(Root, "checkout.maui-plan.json");
            File.WriteAllText(flowPath, FlowMarkdown.Serialize(flow));
            File.WriteAllText(
                planPath,
                JsonSerializer.Serialize(plan, MauiTestingJsonContext.Default.MauiTestPlan));
            return (flowPath, planPath, digest);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }

        private static string FindRepositoryRoot()
        {
            for (var current = new DirectoryInfo(Environment.CurrentDirectory);
                 current is not null;
                 current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, "MauiLabs.slnx")))
                    return current.FullName;
            }
            throw new InvalidOperationException("Repository root not found.");
        }
    }
}
