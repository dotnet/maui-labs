using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal sealed record FlowReproductionRequest
{
    public required string ImportedArtifactPath { get; init; }
    public string? ImportedArtifactKind { get; init; }
    public required FlowExecutionRequest Execution { get; init; }
}

internal sealed record FlowReproductionResult
{
    public required FlowExecutionResult LocalExecution { get; init; }
    public required MauiLocalReproductionReport Report { get; init; }
    public required string ReportPath { get; init; }
    public bool Ok => Report.Matched;
}

internal interface IFlowReproductionCoordinator
{
    Task<FlowReproductionResult> ReproduceAsync(
        FlowReproductionRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class FlowReproductionCoordinator : IFlowReproductionCoordinator
{
    private static readonly HashSet<string> InfrastructureFailureCodes = new(StringComparer.Ordinal)
    {
        MauiFlowFailureClasses.Infrastructure,
        MauiFlowFailureClasses.Transport,
        MauiFlowFailureClasses.AgentDisconnected,
        MauiFlowFailureClasses.LeaseConflict,
        MauiFlowFailureClasses.LeaseLost,
        MauiFlowFailureClasses.Cancelled,
        MauiFlowFailureClasses.Timeout,
        MauiFlowFailureClasses.UnknownCompletion,
        MauiFlowRunOutcomes.InfrastructureError,
        MauiFlowRunOutcomes.UnknownCompletion,
        MauiFlowRunOutcomes.Cancelled,
        MauiFlowRunOutcomes.TimedOut,
        MauiFlowRunOutcomes.LeaseLost,
        MauiFlowRunOutcomes.Orphaned,
    };

    private readonly IFlowExecutionCoordinator _executionCoordinator;
    private readonly IArtifactTrustImporter _artifactImporter;
    private readonly CommittedFlowBundleLoader _bundleLoader;
    private readonly ImmutableExecutionOutputWriter _outputWriter;
    private readonly TimeProvider _clock;

    public FlowReproductionCoordinator(
        IFlowExecutionCoordinator executionCoordinator,
        IArtifactTrustImporter artifactImporter,
        CommittedFlowBundleLoader bundleLoader,
        ImmutableExecutionOutputWriter outputWriter,
        TimeProvider? clock = null)
    {
        _executionCoordinator = executionCoordinator ?? throw new ArgumentNullException(nameof(executionCoordinator));
        _artifactImporter = artifactImporter ?? throw new ArgumentNullException(nameof(artifactImporter));
        _bundleLoader = bundleLoader ?? throw new ArgumentNullException(nameof(bundleLoader));
        _outputWriter = outputWriter ?? throw new ArgumentNullException(nameof(outputWriter));
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<FlowReproductionResult> ReproduceAsync(
        FlowReproductionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Execution);
        if (string.IsNullOrWhiteSpace(request.Execution.OutputDirectory))
        {
            throw FlowExecutionException.Invalid(
                "reproduction-output-required",
                "Flow reproduce requires a new or empty --output directory.");
        }

        var bundle = await _bundleLoader.LoadAsync(
            request.Execution.FlowPath,
            request.Execution.PlanPath,
            cancellationToken).ConfigureAwait(false);
        var artifactKind = ResolveArtifactKind(
            request.ImportedArtifactPath,
            request.ImportedArtifactKind);
        var imported = ImportArtifact(
            request.ImportedArtifactPath,
            artifactKind,
            cancellationToken);
        if (!imported.Ok || imported.Artifact is null)
        {
            throw FlowExecutionException.Invalid(
                "imported-artifact-invalid",
                imported.Error ?? "The imported diagnostic artifact is unsupported.");
        }
        await EnrichImportedRuntimeProfileAsync(
            imported.Artifact,
            request.ImportedArtifactPath,
            artifactKind,
            cancellationToken).ConfigureAwait(false);

        var outputRoot = Path.GetFullPath(request.Execution.OutputDirectory);
        if (Directory.Exists(outputRoot) &&
            (File.GetAttributes(outputRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw FlowExecutionException.Invalid(
                "reproduction-output-reparse-point",
                "The reproduction output directory cannot be a symbolic link or reparse point.");
        }
        var localExecution = await _executionCoordinator.RunAsync(
            request.Execution with { OutputDirectory = outputRoot },
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(localExecution.OutputDirectory) ||
            !PathsEqual(localExecution.OutputDirectory, outputRoot))
        {
            throw FlowExecutionException.Infrastructure(
                "local-execution-output-mismatch",
                "The local execution did not return the requested confined output directory.");
        }
        ExecutionPathSafety.ValidateOutputDirectory(outputRoot);

        var manifestPath = Path.Combine(outputRoot, ExecutionManifestWriter.FileName);
        var reportPath = Path.Combine(outputRoot, MauiFlowRunReportSerializer.FileName);
        var manifestBytes = await BoundedExecutionJsonReader.ReadAsync(
            manifestPath,
            "local execution manifest",
            CancellationToken.None).ConfigureAwait(false);
        var reportBytes = await BoundedExecutionJsonReader.ReadAsync(
            reportPath,
            "local flow run report",
            CancellationToken.None).ConfigureAwait(false);
        var manifest = DeserializeManifest(manifestBytes);
        var report = DeserializeReport(reportBytes);
        MauiFlowRunReportSerializer.ApplyLimits(report, new MauiFlowRunReportLimits());
        string? localBindingFailure = null;
        try
        {
            FlowTriageCoordinator.ValidateEvidenceBinding(manifest, report, reportBytes);
        }
        catch (FlowExecutionException ex)
        {
            localBindingFailure = ex.Code;
        }

        var localFacts = CreateLocalFacts(report, manifest);
        var expectation = new MauiLocalReproductionExpectation
        {
            FlowDigest = bundle.FlowDigest,
            AppBuildFingerprint = localFacts.AppBuildFingerprint,
            AppSourceFingerprint = localFacts.AppSourceFingerprint,
            PackageDigest = localFacts.PackageDigest,
            Platform = localFacts.Platform,
            DeviceProfile = localFacts.DeviceProfile,
            RuntimeProfileFingerprint = localFacts.RuntimeProfileFingerprint,
        };
        var evaluation = MauiArtifactTrustEvaluator.EvaluateLocalReproduction(
            imported.Artifact,
            localFacts,
            expectation,
            _clock.GetUtcNow());
        ApplySignedPackageIdentityLimitation(
            evaluation,
            imported.Artifact,
            localFacts);
        ApplyHostRefusals(
            evaluation,
            imported.Artifact,
            localExecution,
            report,
            manifest,
            bundle.Plan,
            bundle.Flow,
            localBindingFailure);

        var manifestDigest = Digest(manifestBytes);
        var localReportDigest = Digest(reportBytes);
        var reproductionReport = MauiLocalReproductionReportSerializer.CreateSafeProjection(
            CreateReport(
                imported.Artifact,
                localExecution,
                report,
                manifest,
                evaluation,
                manifestDigest,
                localReportDigest,
                manifestBytes.LongLength,
                reportBytes.LongLength));
        var reproductionFile = new ExecutionOutputFile(
            MauiLocalReproductionReportSerializer.FileName,
            MauiLocalReproductionReportSerializer.SerializeToUtf8Bytes(reproductionReport));
        await _outputWriter.WriteAsync(
            outputRoot,
            [reproductionFile],
            CancellationToken.None).ConfigureAwait(false);

        return new FlowReproductionResult
        {
            LocalExecution = localExecution,
            Report = reproductionReport,
            ReportPath = Path.Combine(outputRoot, MauiLocalReproductionReportSerializer.FileName),
        };
    }

    private ArtifactTrustImportResult ImportArtifact(
        string path,
        string kind,
        CancellationToken cancellationToken)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return ArtifactTrustImportResult.Failure("The imported artifact path is invalid.");
        }

        if (!File.Exists(fullPath))
            return ArtifactTrustImportResult.Failure("The imported artifact does not exist.");
        var info = new FileInfo(fullPath);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            return ArtifactTrustImportResult.Failure("The imported artifact cannot be a symbolic link or reparse point.");

        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return _artifactImporter.Import(
                stream,
                kind,
                policy: null,
                verifiedProvenance: null,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ArtifactTrustImportResult.Failure("The imported artifact could not be opened.");
        }
    }

    private static async Task EnrichImportedRuntimeProfileAsync(
        MauiArtifactTrustRecord artifact,
        string importedArtifactPath,
        string artifactKind,
        CancellationToken cancellationToken)
    {
        var projection = artifact.Projection;
        if (projection is null)
            return;
        projection.RuntimeProfileFingerprint = MauiArtifactTrustRedactor.NormalizeFingerprint(
            projection.RuntimeProfileFingerprint);
        if (projection.RuntimeProfileFingerprint is not null ||
            !string.Equals(artifactKind, ArtifactTrustImportKinds.FlowRun, StringComparison.Ordinal))
        {
            return;
        }

        string reportPath;
        try
        {
            reportPath = Path.GetFullPath(importedArtifactPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return;
        }
        var directory = Path.GetDirectoryName(reportPath);
        if (string.IsNullOrWhiteSpace(directory))
            return;
        var manifestPath = Path.Combine(directory, ExecutionManifestWriter.FileName);
        if (!File.Exists(manifestPath))
            return;

        try
        {
            var reportBytes = await BoundedExecutionJsonReader.ReadAsync(
                reportPath,
                "imported flow run report",
                cancellationToken).ConfigureAwait(false);
            var manifestBytes = await BoundedExecutionJsonReader.ReadAsync(
                manifestPath,
                "imported execution manifest",
                cancellationToken).ConfigureAwait(false);
            var report = DeserializeReport(reportBytes);
            var manifest = DeserializeManifest(manifestBytes);
            FlowTriageCoordinator.ValidateEvidenceBinding(manifest, report, reportBytes);
            projection.RuntimeProfileFingerprint =
                MauiTestExecutionRuntimeProfile.CreateFingerprint(
                    manifest.Build,
                    manifest.Device);
        }
        catch (FlowExecutionException)
        {
            projection.RuntimeProfileFingerprint = null;
        }
    }

    private static string ResolveArtifactKind(string path, string? requestedKind)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw FlowExecutionException.Invalid(
                "imported-artifact-path-missing",
                "Flow reproduce requires --import <flow-run.json|artifact.mauitrace>.");
        }

        if (!string.IsNullOrWhiteSpace(requestedKind))
        {
            var normalized = requestedKind.Trim().ToLowerInvariant();
            if (ArtifactTrustImportKinds.IsKnown(normalized))
                return normalized;
            throw FlowExecutionException.Unsupported(
                "imported-artifact-kind-unsupported",
                "The imported artifact kind must be flow-run or mauitrace.");
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => ArtifactTrustImportKinds.FlowRun,
            ".mauitrace" => ArtifactTrustImportKinds.Evidence,
            _ => throw FlowExecutionException.Unsupported(
                "imported-artifact-kind-unsupported",
                "Use --kind flow-run or --kind mauitrace when the imported artifact extension is not .json or .mauitrace."),
        };
    }

    private static MauiTestExecutionManifest DeserializeManifest(byte[] bytes)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize(
                bytes,
                MauiTestingJsonContext.Default.MauiTestExecutionManifest);
            if (manifest?.Schema != 1)
            {
                throw FlowExecutionException.Infrastructure(
                    "local-manifest-schema-invalid",
                    "The production coordinator wrote an unsupported local execution manifest.");
            }
            return manifest;
        }
        catch (JsonException)
        {
            throw FlowExecutionException.Infrastructure(
                "local-manifest-invalid",
                "The production coordinator wrote an invalid local execution manifest.");
        }
    }

    private static MauiFlowRunReport DeserializeReport(byte[] bytes)
    {
        try
        {
            var report = JsonSerializer.Deserialize(
                bytes,
                MauiTestingJsonContext.Default.MauiFlowRunReport);
            if (report?.Schema != 1)
            {
                throw FlowExecutionException.Infrastructure(
                    "local-report-schema-invalid",
                    "The production coordinator wrote an unsupported local flow run report.");
            }
            return report;
        }
        catch (JsonException)
        {
            throw FlowExecutionException.Infrastructure(
                "local-report-invalid",
                "The production coordinator wrote an invalid local flow run report.");
        }
    }

    private static MauiLocalReproductionFacts CreateLocalFacts(
        MauiFlowRunReport report,
        MauiTestExecutionManifest manifest)
    {
        var failure = report.Failure;
        var failedStep = FindFailedStep(report);
        return new MauiLocalReproductionFacts
        {
            LocalRunId = report.RunId ?? manifest.RunId,
            IsNewLocalRun = true,
            StartedAt = report.StartedAt ?? manifest.Lifecycle?.StartedAt,
            FlowDigest = report.FlowDigest ?? manifest.FlowDigest,
            AppBuildFingerprint = report.Target?.AppBuildFingerprint ?? manifest.Build?.AppBuildFingerprint,
            AppSourceFingerprint = report.Target?.AppSourceFingerprint ?? manifest.Build?.AppSourceFingerprint,
            PackageDigest = report.Target?.PackageDigest ?? manifest.Build?.PackageDigest,
            NormalizedPayloadDigest = report.Target?.NormalizedPayloadDigest,
            Platform = report.Target?.Platform ?? manifest.Device?.Platform,
            DeviceProfile = report.Target?.DeviceProfile ?? manifest.Device?.Profile,
            RuntimeProfileFingerprint = MauiTestExecutionRuntimeProfile.CreateFingerprint(
                manifest.Build,
                manifest.Device),
            Failure = failure is null
                ? null
                : new MauiLocalFailureFacts
                {
                    Code = failure.Code,
                    Class = failure.Class,
                    StepId = failure.StepId,
                    ExpectedCheckpoint = CloneCheckpoint(failedStep?.ExpectedCheckpoint),
                    ObservedCheckpoint = CloneCheckpoint(failedStep?.ObservedCheckpoint),
                },
        };
    }

    private static void ApplyHostRefusals(
        MauiLocalReproductionEvaluation evaluation,
        MauiArtifactTrustRecord imported,
        FlowExecutionResult localExecution,
        MauiFlowRunReport report,
        MauiTestExecutionManifest manifest,
        MauiTestPlan plan,
        MauiFlow flow,
        string? localBindingFailure)
    {
        var refusals = new SortedSet<string>(StringComparer.Ordinal);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var importedOutcome = imported.Projection?.Outcome;
        if (string.IsNullOrWhiteSpace(importedOutcome))
            missing.Add("imported.outcome");
        else if (InfrastructureFailureCodes.Contains(importedOutcome))
            refusals.Add("imported-infrastructure-or-unknown-completion");
        else if (!string.Equals(importedOutcome, MauiFlowRunOutcomes.Failed, StringComparison.Ordinal))
            refusals.Add("imported-outcome-not-reproducible");

        if (InfrastructureFailureCodes.Contains(imported.Projection?.Failure?.Code ?? string.Empty) ||
            InfrastructureFailureCodes.Contains(imported.Projection?.Failure?.Class ?? string.Empty))
        {
            refusals.Add("imported-infrastructure-or-unknown-completion");
        }

        // The imported run's verdict may look reproducible, but a source host that could not tear
        // itself down did not leave the target in the state its evidence describes.
        if (imported.Projection?.OwnedCleanupIncomplete == true)
            refusals.Add("imported-owned-cleanup-incomplete");

        switch (localExecution.ExitCategory)
        {
            case FlowExecutionExitCategories.InfrastructureFailure:
                refusals.Add("local-infrastructure-failure");
                break;
            case FlowExecutionExitCategories.UnknownCompletion:
                refusals.Add("local-unknown-completion");
                break;
            case FlowExecutionExitCategories.Unsupported:
                refusals.Add("local-target-unsupported");
                break;
            case FlowExecutionExitCategories.InvalidConfiguration:
                refusals.Add("local-configuration-invalid");
                break;
            case FlowExecutionExitCategories.Unverified:
                refusals.Add("local-run-unverified");
                break;
            case FlowExecutionExitCategories.TestFailure:
                break;
            default:
                refusals.Add("local-lifecycle-outcome-unsupported");
                break;
        }

        if (report.Outcome?.Terminal != true || manifest.Outcome?.Terminal != true)
            missing.Add("local.terminalOutcome");
        // The local run's own verdict may be reproducible, but a target the host could not tear
        // down is not the target the next step would replay against.
        if ((report.SecondaryFailures?.Count ?? 0) > 0)
            refusals.Add("local-owned-cleanup-incomplete");
        if (string.IsNullOrWhiteSpace(report.RunId) || string.IsNullOrWhiteSpace(manifest.RunId))
            missing.Add("local.runId");
        else if (!string.Equals(report.RunId, manifest.RunId, StringComparison.Ordinal))
            refusals.Add("local-run-id-mismatch");
        if (!MauiFlowRunReportSerializer.Validate(report).IsValid)
            refusals.Add("local-report-invalid");
        // Local reproduction exists to turn diagnostic evidence into repair authority. A truncated
        // report reached a size or cardinality limit and dropped steps, oracles, or replay
        // eligibility to fit it, so an absent disqualifying fact here is not proof of its absence.
        // Advertising the Inspector repair review off that would hand a human a repair decision the
        // evidence cannot support, and the repair eligibility gate refuses the same report anyway.
        if (report.Truncated == true)
            refusals.Add("local-report-truncated");
        if (localBindingFailure is not null)
            refusals.Add("local-report-manifest-binding-invalid");

        var localTriage = MauiFlowTriageAnalyzer.Analyze(new MauiFlowTriageInput
        {
            Flow = flow,
            Report = report,
            Manifest = manifest,
            Plan = plan,
            ReplayEligibility = report.ReplayEligibility,
            ExpectedCheckpoint = report.Preconditions?.Expected,
            IsCurrentLocalRun = true,
        });
        if (!string.Equals(
                localTriage.Evidence.State,
                MauiFlowTriageEvidenceStates.Sufficient,
                StringComparison.Ordinal))
        {
            refusals.Add("local-evidence-insufficient");
            foreach (var fact in localTriage.Evidence.MissingFacts)
                missing.Add("local." + fact);
        }

        if (IsRefusedLifecycleOutcome(report.Outcome?.Status) ||
            IsRefusedLifecycleOutcome(report.Failure?.Class) ||
            IsRefusedLifecycleOutcome(report.Failure?.Code) ||
            IsRefusedLifecycleOutcome(manifest.Outcome?.Status) ||
            manifest.Outcome?.UnknownCompletion == true ||
            (manifest.Lifecycle?.Stages ?? []).Any(stage =>
                IsRefusedLifecycleOutcome(stage.Status) ||
                IsRefusedLifecycleOutcome(stage.DetailCode) ||
                IsRefusedLifecycleOutcome(stage.Name)))
        {
            refusals.Add("local-lifecycle-outcome-refused");
        }

        var failureCode = report.Failure?.Code ?? imported.Projection?.Failure?.Code;
        var failureClass = report.Failure?.Class ?? imported.Projection?.Failure?.Class;
        if ((string.Equals(failureCode, MauiFlowFailureClasses.LocatorNotFound, StringComparison.Ordinal) ||
             string.Equals(failureClass, MauiFlowFailureClasses.LocatorNotFound, StringComparison.Ordinal)) &&
            !HasRequiredIndependentOracle(plan))
        {
            refusals.Add("independent-oracle-absent");
        }

        if (refusals.Count == 0 && missing.Count == 0)
            return;

        var importedState = imported.Verification?.State;
        var importState = MauiArtifactTrustStates.IsKnown(importedState)
            ? importedState!
            : MauiArtifactTrustStates.Untrusted;
        evaluation.Verification.State = importState;
        evaluation.Binding.Matched = false;
        evaluation.Verification.Reasons.RemoveAll(reason =>
            string.Equals(reason.Code, "locally-reproduced", StringComparison.Ordinal));
        if (evaluation.Verification.Reasons.All(reason =>
                !string.Equals(reason.Code, "local-reproduction-not-established", StringComparison.Ordinal)))
        {
            evaluation.Verification.Reasons.Add(new MauiArtifactTrustReason
            {
                Code = "local-reproduction-not-established",
                Message = "The host refused to establish local reproduction trust.",
                Blocking = true,
            });
        }
        foreach (var code in refusals)
        {
            if (evaluation.Verification.Reasons.All(reason =>
                    !string.Equals(reason.Code, code, StringComparison.Ordinal)))
            {
                evaluation.Verification.Reasons.Add(new MauiArtifactTrustReason
                {
                    Code = code,
                    Message = HostRefusalMessage(code),
                    Blocking = true,
                });
            }
        }
        foreach (var field in missing)
        {
            if (evaluation.Verification.Omissions.All(omission =>
                    !string.Equals(omission.Field, field, StringComparison.Ordinal)))
            {
                evaluation.Verification.Omissions.Add(new MauiArtifactTrustOmission
                {
                    Field = field,
                    Reason = "A required bounded reproduction fact was absent.",
                });
            }
            var code = "missing-" + field;
            if (evaluation.Verification.Reasons.All(reason =>
                    !string.Equals(reason.Code, code, StringComparison.Ordinal)))
            {
                evaluation.Verification.Reasons.Add(new MauiArtifactTrustReason
                {
                    Code = code,
                    Message = "A required bounded reproduction fact was absent.",
                    Blocking = true,
                });
            }
        }
        evaluation.Binding.Verification = evaluation.Verification;
    }

    private static void ApplySignedPackageIdentityLimitation(
        MauiLocalReproductionEvaluation evaluation,
        MauiArtifactTrustRecord imported,
        MauiLocalReproductionFacts localFacts)
    {
        var reasons = evaluation.Verification.Reasons;
        var signedOccurrenceDigestMismatch = reasons.Any(reason =>
            string.Equals(reason.Code, "packageDigest-mismatch", StringComparison.Ordinal));
        if (!signedOccurrenceDigestMismatch ||
            !IsSigningVolatileTarget(localFacts.Platform) ||
            !ImportedFingerprintMatches(imported.Projection?.FlowFingerprint, localFacts.FlowDigest) ||
            !ImportedFingerprintMatches(imported.Projection?.AppSourceFingerprint, localFacts.AppSourceFingerprint) ||
            !ImportedFingerprintMatches(imported.Projection?.PlatformFingerprint, localFacts.Platform) ||
            !PrecomputedFingerprintMatches(
                imported.Projection?.RuntimeProfileFingerprint,
                localFacts.RuntimeProfileFingerprint))
        {
            return;
        }

        reasons.RemoveAll(reason =>
            reason.Code is "packageDigest-mismatch" or "appBuildFingerprint-mismatch");
        if (reasons.All(reason =>
                !string.Equals(
                    reason.Code,
                    "signed-occurrence-artifact-differs",
                    StringComparison.Ordinal)))
        {
            reasons.Add(new MauiArtifactTrustReason
            {
                Code = "signed-occurrence-artifact-differs",
                Message = "The signed package digest identifies this occurrence artifact and differs from the imported occurrence.",
                Blocking = false,
            });
        }
        var importedNormalizedPayload = imported.Projection?.NormalizedPayloadFingerprint;
        var localNormalizedPayload = localFacts.NormalizedPayloadDigest;
        // The refusal has to name which of the two situations actually holds. Reporting an absent
        // identity while both sides published one would misdescribe a real payload difference as a
        // gap in DevFlow's own evidence, and send a reader looking for the wrong defect.
        if (string.IsNullOrWhiteSpace(importedNormalizedPayload) ||
            string.IsNullOrWhiteSpace(localNormalizedPayload))
        {
            if (evaluation.Verification.Omissions.All(omission =>
                    !string.Equals(omission.Field, "normalizedPayloadDigest", StringComparison.Ordinal)))
            {
                evaluation.Verification.Omissions.Add(new MauiArtifactTrustOmission
                {
                    Field = "normalizedPayloadDigest",
                    Reason = "A signing-insensitive normalized payload digest is not available for this occurrence artifact.",
                });
            }
            if (reasons.All(reason =>
                    !string.Equals(
                        reason.Code,
                        "normalized-payload-identity-unavailable",
                        StringComparison.Ordinal)))
            {
                reasons.Add(new MauiArtifactTrustReason
                {
                    Code = "normalized-payload-identity-unavailable",
                    Message = "Source identity and target profile matched, but signed occurrence artifacts differed and no signing-insensitive normalized payload identity is available.",
                    Blocking = true,
                });
            }
        }
        else if (!ImportedFingerprintMatches(importedNormalizedPayload, localNormalizedPayload))
        {
            if (reasons.All(reason =>
                    !string.Equals(
                        reason.Code,
                        "normalized-payload-identity-differs",
                        StringComparison.Ordinal)))
            {
                reasons.Add(new MauiArtifactTrustReason
                {
                    Code = "normalized-payload-identity-differs",
                    // The normalization excludes JAR/APK v1 signature material only, so on a target
                    // whose signing artefacts take another shape this states the digests differ and
                    // says nothing about where.
                    Message = "Source identity and target profile matched, but the two occurrences carry different normalized payload digests.",
                    Blocking = true,
                });
            }
        }
        else
        {
            if (reasons.All(reason =>
                    !string.Equals(
                        reason.Code,
                        "normalized-payload-identity-unproven",
                        StringComparison.Ordinal)))
            {
                // Two occurrences agreeing here is not yet evidence that this platform can produce a
                // stable payload identity, so it is recorded as an observation and still refuses.
                reasons.Add(new MauiArtifactTrustReason
                {
                    Code = "normalized-payload-identity-unproven",
                    Message = "The two occurrences carry the same normalized payload digest, but a normalized payload digest has not been established as a cross-occurrence identity on this platform.",
                    Blocking = true,
                });
            }
        }
        evaluation.Binding.Matched = false;
        evaluation.Binding.Verification = evaluation.Verification;
    }

    private static bool ImportedFingerprintMatches(string? importedFingerprint, string? localValue)
        => !string.IsNullOrWhiteSpace(importedFingerprint) &&
           !string.IsNullOrWhiteSpace(localValue) &&
           string.Equals(
               importedFingerprint,
               MauiArtifactTrustRedactor.Fingerprint(localValue),
               StringComparison.Ordinal);

    private static bool PrecomputedFingerprintMatches(string? imported, string? local)
    {
        var importedFingerprint = MauiArtifactTrustRedactor.NormalizeFingerprint(imported);
        var localFingerprint = MauiArtifactTrustRedactor.NormalizeFingerprint(local);
        return importedFingerprint is not null &&
            string.Equals(importedFingerprint, localFingerprint, StringComparison.Ordinal);
    }

    private static bool IsSigningVolatileTarget(string? platform)
        => platform?.Trim().ToLowerInvariant() is
            "android" or "ios" or "ios-simulator" or
            "maccatalyst" or "mac-catalyst" or
            "macos" or "appkit" or
            "windows" or "winui" or "wpf";

    private static MauiLocalReproductionReport CreateReport(
        MauiArtifactTrustRecord imported,
        FlowExecutionResult localExecution,
        MauiFlowRunReport report,
        MauiTestExecutionManifest manifest,
        MauiLocalReproductionEvaluation evaluation,
        string manifestDigest,
        string reportDigest,
        long manifestSize,
        long reportSize)
    {
        var localFacts = CreateLocalFacts(report, manifest);
        var localFailure = localFacts.Failure;
        var artifacts = new List<MauiTestExecutionArtifact>
        {
            new()
            {
                ArtifactId = "local-execution-manifest",
                Role = "execution-manifest",
                Kind = "execution-manifest",
                MediaType = "application/json",
                Digest = manifestDigest,
                SizeBytes = manifestSize,
                RelativePath = ExecutionManifestWriter.FileName,
                Redacted = true,
            },
            new()
            {
                ArtifactId = "local-flow-run-report",
                Role = "semantic-report",
                Kind = "flow-run-report",
                MediaType = "application/json",
                Digest = reportDigest,
                SizeBytes = reportSize,
                RelativePath = MauiFlowRunReportSerializer.FileName,
                Redacted = true,
            },
        };
        artifacts.AddRange(manifest.Artifacts ?? []);

        var importedState = imported.Verification?.State;
        var importState = MauiArtifactTrustStates.IsKnown(importedState)
            ? importedState!
            : MauiArtifactTrustStates.Untrusted;
        return new MauiLocalReproductionReport
        {
            ImportedArtifact = imported.Identity,
            ImportedArtifactKind = imported.ArtifactKind,
            ImportedArtifactDigest = imported.Integrity?.ArtifactDigest,
            ImportTrustState = importState,
            LocalRunId = report.RunId ?? manifest.RunId,
            LocalExitCategory = localExecution.ExitCategory,
            LocalManifestDigest = manifestDigest,
            LocalReportDigest = reportDigest,
            Matched = evaluation.Binding.Matched == true,
            TrustState = evaluation.Verification.State,
            ReasonCodes = evaluation.Verification.Reasons
                .Select(static reason => reason.Code)
                .Where(static code => !string.IsNullOrWhiteSpace(code))
                .Cast<string>()
                .ToList(),
            MissingFacts = evaluation.Verification.Omissions
                .Select(static omission => omission.Field)
                .Where(static field => !string.IsNullOrWhiteSpace(field))
                .Cast<string>()
                .ToList(),
            ImportedFailureFingerprint = MauiArtifactTrustRedactor.Fingerprint(
                imported.Projection?.Failure?.FailureKey),
            ImportedStepFingerprint = imported.Projection?.Failure?.StepFingerprint,
            ImportedRuntimeProfileFingerprint =
                imported.Projection?.RuntimeProfileFingerprint,
            LocalFailureFingerprint = FailureFingerprint(localFailure),
            LocalStepFingerprint = MauiArtifactTrustRedactor.Fingerprint(localFailure?.StepId),
            LocalRuntimeProfileFingerprint = localFacts.RuntimeProfileFingerprint,
            LocalExpectedCheckpointFingerprint = MauiArtifactTrustRedactor.CheckpointFingerprint(
                localFailure?.ExpectedCheckpoint),
            LocalObservedCheckpointFingerprint = MauiArtifactTrustRedactor.CheckpointFingerprint(
                localFailure?.ObservedCheckpoint),
            LocalArtifacts = artifacts,
        };
    }

    private static MauiFlowStepAttempt? FindFailedStep(MauiFlowRunReport report)
    {
        var stepId = report.Failure?.StepId ?? report.DivergenceStepId;
        return report.Steps.FirstOrDefault(step =>
                string.Equals(step.StepId, stepId, StringComparison.Ordinal))
            ?? report.Steps.FirstOrDefault(static step => !string.IsNullOrWhiteSpace(step.FailureClass));
    }

    private static MauiFlowCheckpoint? CloneCheckpoint(MauiFlowCheckpoint? checkpoint)
        => checkpoint is null
            ? null
            : new MauiFlowCheckpoint
            {
                AppBuildFingerprint = checkpoint.AppBuildFingerprint,
                AgentInstanceId = checkpoint.AgentInstanceId,
                SeedFingerprint = checkpoint.SeedFingerprint,
                BackendStateFingerprint = checkpoint.BackendStateFingerprint,
                Route = checkpoint.Route,
                Window = checkpoint.Window,
                Modal = checkpoint.Modal,
                Locale = checkpoint.Locale,
                Theme = checkpoint.Theme,
                Orientation = checkpoint.Orientation,
                DisplayProfile = checkpoint.DisplayProfile,
                CollectionItemKey = checkpoint.CollectionItemKey,
            };

    private static string? FailureFingerprint(MauiLocalFailureFacts? failure)
    {
        if (failure is null)
            return null;
        var material = string.Join(
            "\u001f",
            MauiArtifactTrustRedactor.SafeFailureCode(failure.Code) ?? string.Empty,
            MauiArtifactTrustRedactor.SafeFailureCode(failure.Class) ?? string.Empty,
            MauiArtifactTrustRedactor.Fingerprint(failure.StepId) ?? string.Empty,
            MauiArtifactTrustRedactor.CheckpointFingerprint(failure.ExpectedCheckpoint) ?? string.Empty,
            MauiArtifactTrustRedactor.CheckpointFingerprint(failure.ObservedCheckpoint) ?? string.Empty);
        return MauiArtifactTrustRedactor.Fingerprint(material);
    }

    private static bool HasRequiredIndependentOracle(MauiTestPlan plan)
        => plan.IndependentBusinessOracles
            .Concat(plan.BusinessOracles.Select(static oracle => new MauiIndependentBusinessOracleDeclaration
            {
                OracleId = oracle.OracleId,
                Required = oracle.Required,
                Independent = oracle.Independent,
            }))
            .Any(static oracle =>
                oracle.Required &&
                oracle.Independent &&
                !string.IsNullOrWhiteSpace(oracle.OracleId));

    private static string HostRefusalMessage(string code)
        => code switch
        {
            "imported-infrastructure-or-unknown-completion" =>
                "Infrastructure and unknown-completion evidence is diagnostic-only.",
            "local-infrastructure-failure" =>
                "The new local execution ended in infrastructure failure.",
            "local-unknown-completion" =>
                "The new local execution has unknown mutation completion.",
            "local-target-unsupported" =>
                "The selected local target is unsupported.",
            "local-configuration-invalid" =>
                "The committed flow, plan, project, or target configuration is invalid.",
            "local-run-unverified" =>
                "The new local run lacks required independent verification.",
            "local-run-id-mismatch" =>
                "The local manifest and report do not identify the same run.",
            "local-report-invalid" =>
                "The local flow report failed its bounded contract validation.",
            "local-report-truncated" =>
                "The local flow report was truncated, so its evidence is incomplete.",
            "local-report-manifest-binding-invalid" =>
                "The local flow report and execution manifest were not fully cross-bound.",
            "local-evidence-insufficient" =>
                "The local execution evidence was insufficient for deterministic triage.",
            "local-lifecycle-outcome-refused" =>
                "Lease, cancellation, timeout, orphan, or unknown lifecycle outcomes cannot establish reproduction trust.",
            "local-lifecycle-outcome-unsupported" =>
                "An unknown local lifecycle exit category cannot establish reproduction trust.",
            "imported-outcome-not-reproducible" =>
                "Only a bounded imported failure outcome can be reproduced.",
            "independent-oracle-absent" =>
                "A selector-repair handoff requires a declared independent business oracle.",
            _ => "The local reproduction was refused by a fail-closed host policy.",
        };

    private static bool IsRefusedLifecycleOutcome(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var normalized = value.Trim().ToLowerInvariant();
        return InfrastructureFailureCodes.Contains(normalized) ||
            normalized.Contains("lease", StringComparison.Ordinal) ||
            normalized.Contains("cancel", StringComparison.Ordinal) ||
            normalized.Contains("timeout", StringComparison.Ordinal) ||
            normalized.Contains("timed-out", StringComparison.Ordinal) ||
            normalized.Contains("orphan", StringComparison.Ordinal) ||
            normalized.Contains("abandon", StringComparison.Ordinal) ||
            normalized.Contains("unknown", StringComparison.Ordinal);
    }

    private static string Digest(ReadOnlySpan<byte> bytes)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool PathsEqual(string first, string second)
        => string.Equals(
            Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
