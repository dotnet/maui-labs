namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Deterministic provider-neutral triage. It reuses the canonical failure classifier and repair
/// eligibility evaluator, performs no I/O, and never turns imported evidence into repair authority.
/// </summary>
public static class MauiFlowTriageAnalyzer
{
    private static readonly HashSet<string> StepScopedFailures = new(StringComparer.Ordinal)
    {
        MauiFlowFailureClasses.LocatorNotFound,
        MauiFlowFailureClasses.LocatorAmbiguous,
        MauiFlowFailureClasses.NotVisible,
        MauiFlowFailureClasses.Disabled,
        MauiFlowFailureClasses.UnstableBounds,
        MauiFlowFailureClasses.ActionRejected,
        MauiFlowFailureClasses.DriveFailed,
        MauiFlowFailureClasses.UnknownCompletion,
        MauiFlowFailureClasses.WorkflowCommandConflict,
        MauiFlowFailureClasses.AssertionFailed,
        MauiFlowFailureClasses.SecretUnavailable,
        MauiFlowFailureClasses.UnsafeValue,
    };

    public static MauiFlowTriage Analyze(MauiFlowTriageInput? input)
    {
        input ??= new MauiFlowTriageInput();
        var report = input.Report;
        var manifest = MauiTestExecutionManifestSerializer.CreateSafeProjection(input.Manifest);
        var failedStep = FindFailedStep(report);
        var classification = Classify(report, failedStep, input);
        var fingerprints = MauiFlowIncidentFingerprint.Create(
            input.Flow,
            report,
            manifest,
            classification);
        var missingFacts = FindMissingFacts(input, manifest, classification, fingerprints);
        var sufficient = missingFacts.Count == 0;
        var imported = input.ImportedEvidence || input.ImportedArtifact is not null;
        var repairDecision = MauiFlowRepairEligibilityEvaluator.Evaluate(new MauiFlowRepairEligibilityInput
        {
            Run = report,
            Plan = input.Plan,
            ReplayEligibility = input.ReplayEligibility ?? report?.ReplayEligibility,
            ExpectedCheckpoint = input.ExpectedCheckpoint,
            CurrentCheckpoint = input.CurrentCheckpoint,
            BeforeDispatch = input.BeforeDispatch,
            IsCurrentLocalRun = input.IsCurrentLocalRun && !imported,
            ArtifactTrust = input.ArtifactTrust,
            ImportedArtifact = input.ImportedArtifact,
            PriorActiveSelectorResolution = input.PriorActiveSelectorResolution,
            TargetFingerprint = input.TargetFingerprint,
            AdditionalFailureCodes = input.AdditionalFailureCodes?.ToList() ?? [],
        });
        var repairEligible = sufficient &&
            !imported &&
            classification.RepairEligible &&
            repairDecision.Eligible;
        var actions = AllowedActions(classification, sufficient, imported, repairEligible);
        var code = MauiTestingContractRedactor.SafeCode(report?.Failure?.Code) ??
            classification.Code;
        var triage = new MauiFlowTriage
        {
            TestIdentityFingerprint = fingerprints.TestIdentityFingerprint,
            IncidentFingerprint = fingerprints.IncidentFingerprint,
            OccurrenceFingerprint = fingerprints.OccurrenceFingerprint,
            Classification = new MauiFlowTriageClassification
            {
                FailureClass = classification.FailureClass,
                Code = code,
                Category = classification.Category,
                Phase = classification.Phase,
                Disposition = MauiFlowFailureClassifier.Project(classification.FailureClass),
            },
            Evidence = new MauiFlowTriageEvidenceSufficiency
            {
                State = sufficient
                    ? MauiFlowTriageEvidenceStates.Sufficient
                    : MauiFlowTriageEvidenceStates.Insufficient,
                MissingFacts = missingFacts,
            },
            Retryable = classification.Retryable,
            RepairEligible = repairEligible,
            AllowedNextActions = actions,
            LocalReproductionRequired = imported,
            LocalReproductionReason = imported ? "imported-evidence-diagnostic-only" : null,
            ImportedEvidence = imported,
            SummaryCode = code,
            Summary = MauiFlowTriageText.Summary(classification.FailureClass),
            RepairEligibilityCodes = repairDecision.Reasons
                .Select(static reason => reason.Code)
                .Concat(imported ? ["imported-evidence-diagnostic-only"] : [])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToList(),
            Execution = new MauiFlowTriageExecutionFacts
            {
                ManifestId = manifest.ManifestId,
                RunId = manifest.RunId ?? MauiTestingContractRedactor.SafeCode(report?.RunId),
                FlowId = manifest.FlowId ?? MauiTestingContractRedactor.SafeCode(report?.FlowId),
                Host = manifest.Host,
                Build = manifest.Build,
                Device = manifest.Device,
                Lifecycle = manifest.Lifecycle,
                Outcome = ProjectOutcome(report, manifest.Outcome),
                Artifacts = manifest.Artifacts,
            },
        };
        return MauiFlowTriageSerializer.CreateSafeProjection(triage);
    }

    private static MauiFlowFailureClassification Classify(
        MauiFlowRunReport? report,
        MauiFlowStepAttempt? failedStep,
        MauiFlowTriageInput input)
    {
        if (string.Equals(report?.Outcome?.Status, MauiFlowRunOutcomes.Passed, StringComparison.Ordinal))
        {
            return new MauiFlowFailureClassification
            {
                FailureClass = "passed",
                Code = "passed",
                Category = "outcome",
                Phase = "verification",
            };
        }

        var expected = input.ExpectedCheckpoint ?? failedStep?.ExpectedCheckpoint;
        var current = input.CurrentCheckpoint ?? failedStep?.ObservedCheckpoint;
        var completion = failedStep?.CompletionCertainty;
        var canonicalFailureClass = report?.Failure?.Class ?? failedStep?.FailureClass;
        return MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
        {
            TerminalOutcome = string.IsNullOrWhiteSpace(canonicalFailureClass)
                ? report?.Outcome?.Status
                : null,
            FailureClass = canonicalFailureClass ?? report?.Failure?.Code,
            LegacyFailureKind = report?.Failure?.LegacyKind,
            AssertionTargetResolution = MauiFlowFailureClassifier.AssertionTargetResolutionOf(
                FindNamedFailedStep(report)),
            AppProcessExited = report?.AppProcess?.ProcessExited,
            AppExitCode = report?.AppProcess?.ExitCode,
            AppExitReason = report?.AppProcess?.ExitReason,
            CrashLogPresent = report?.AppProcess?.CrashLogPresent,
            CompletionCertain = string.Equals(completion, "unknown", StringComparison.Ordinal)
                ? false
                : completion is null ? null : true,
            BeforeDispatch = input.BeforeDispatch ??
                (failedStep is not null &&
                 failedStep.Dispatch is null &&
                 string.Equals(report?.Failure?.Phase, "resolution", StringComparison.Ordinal)),
            CheckpointVerified = expected is not null && current is not null,
            CheckpointMatches = CheckpointsMatch(expected, current),
            RouteMatches = RoutesMatch(expected, current),
        });
    }

    private static List<string> FindMissingFacts(
        MauiFlowTriageInput input,
        MauiTestExecutionManifest manifest,
        MauiFlowFailureClassification classification,
        MauiFlowFingerprintSet fingerprints)
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var report = input.Report;
        var rawManifest = input.Manifest;
        var lifecycle = rawManifest?.Lifecycle;
        var flowLoaded = HasPassedLifecycleStage(lifecycle, "load-workflow") ||
            !string.IsNullOrWhiteSpace(report?.FlowDigest) ||
            input.Flow is not null;
        var artifactResolved = HasPassedLifecycleStage(lifecycle, "resolve-artifact") ||
            !string.IsNullOrWhiteSpace(report?.Target?.AppBuildFingerprint);
        var deviceLaunched = HasPassedLifecycleStage(lifecycle, "platform-launch") ||
            !string.IsNullOrWhiteSpace(report?.Target?.Platform);

        if (input.Manifest is null)
            missing.Add("execution-manifest");
        if (report is null)
            missing.Add("flow-run-report");
        if (input.Manifest?.Schema != 1)
            missing.Add("execution-manifest-schema");
        if (report?.Schema != 1)
            missing.Add("flow-run-report-schema");
        if (string.IsNullOrWhiteSpace(input.Manifest?.RunId))
            missing.Add("manifest-run-id");
        if (string.IsNullOrWhiteSpace(report?.RunId))
            missing.Add("report-run-id");
        if (!string.IsNullOrWhiteSpace(input.Manifest?.RunId) &&
            !string.IsNullOrWhiteSpace(report?.RunId) &&
            !string.Equals(input.Manifest.RunId, report.RunId, StringComparison.Ordinal))
        {
            missing.Add("run-id-match");
        }
        if (flowLoaded && string.IsNullOrWhiteSpace(rawManifest?.FlowDigest))
            missing.Add("manifest-flow-digest");
        if (flowLoaded && string.IsNullOrWhiteSpace(report?.FlowDigest))
            missing.Add("report-flow-digest");
        if (flowLoaded &&
            !string.IsNullOrWhiteSpace(rawManifest?.FlowId) !=
            !string.IsNullOrWhiteSpace(report?.FlowId))
        {
            missing.Add("flow-id-binding");
        }
        if (flowLoaded &&
            rawManifest?.FlowRevision.HasValue != report?.FlowRevision.HasValue)
        {
            missing.Add("flow-revision-binding");
        }
        AddExactMatch(
            missing,
            "flow-id-match",
            rawManifest?.FlowId,
            report?.FlowId);
        if (rawManifest?.FlowRevision is { } manifestRevision &&
            report?.FlowRevision is { } reportRevision &&
            manifestRevision != reportRevision)
        {
            missing.Add("flow-revision-match");
        }
        AddFingerprintMatch(
            missing,
            "flow-digest-match",
            rawManifest?.FlowDigest,
            report?.FlowDigest,
            "flow");
        if (string.IsNullOrWhiteSpace(report?.Outcome?.Status))
            missing.Add("terminal-outcome");
        if (string.IsNullOrWhiteSpace(rawManifest?.Outcome?.Status))
            missing.Add("manifest-terminal-outcome");
        if (report?.Outcome?.Terminal != true)
            missing.Add("terminal-state");
        AddExactMatch(
            missing,
            "outcome-status-match",
            rawManifest?.Outcome?.Status,
            report?.Outcome?.Status);
        if (rawManifest?.Outcome?.Terminal is { } manifestTerminal &&
            report?.Outcome?.Terminal is { } reportTerminal &&
            manifestTerminal != reportTerminal)
        {
            missing.Add("outcome-terminal-match");
        }
        var reportVerified = report?.Verification?.Verified ?? report?.Outcome?.Verified;
        if (manifest.Outcome?.Verified is { } manifestVerified &&
            reportVerified is { } canonicalVerified &&
            manifestVerified != canonicalVerified)
        {
            missing.Add("verification-status-match");
        }
        if (report?.Outcome?.Verified is { } outcomeVerified &&
            report?.Verification?.Verified is { } verificationVerified &&
            outcomeVerified != verificationVerified)
        {
            missing.Add("report-verification-status-match");
        }
        var expectedUnknownCompletion = IsUnknownLifecycleOutcome(report?.Outcome?.Status);
        if (rawManifest?.Outcome?.UnknownCompletion is { } manifestUnknown &&
            manifestUnknown != expectedUnknownCompletion)
        {
            missing.Add("unknown-completion-match");
        }
        if (HasContradictoryOutcome(report))
            missing.Add("outcome-failure-consistency");
        var expectedExitCategory = ExpectedExitCategory(report);
        var reportExitCategory = ReportExitCategory(report);
        AddExactMatch(
            missing,
            "report-exit-category-match",
            rawManifest?.Outcome?.ExitCategory,
            reportExitCategory);
        if (!string.IsNullOrWhiteSpace(rawManifest?.Outcome?.ExitCategory) &&
            !string.IsNullOrWhiteSpace(expectedExitCategory) &&
            !string.Equals(
                rawManifest.Outcome.ExitCategory,
                expectedExitCategory,
                StringComparison.Ordinal))
        {
            missing.Add("exit-category-match");
        }
        // The manifest mirrors the report's owned-cleanup facts. If the pair disagrees, the two
        // artifacts describe different occurrences and neither can be trusted about the other.
        if (!SecondaryFailuresMatch(rawManifest?.Outcome?.SecondaryFailures, report?.SecondaryFailures))
            missing.Add("secondary-failure-match");
        if (manifest.Lifecycle?.StartedAt is null)
            missing.Add("manifest-lifecycle-started-at");
        if (report?.StartedAt is null)
            missing.Add("report-lifecycle-started-at");
        if (manifest.Lifecycle?.EndedAt is null)
            missing.Add("manifest-lifecycle-ended-at");
        if (report?.EndedAt is null)
            missing.Add("report-lifecycle-ended-at");
        // The manifest lifecycle spans the whole invocation (build, install, launch, replay,
        // cleanup); the flow-run report spans only the replay. Requiring the two windows to be
        // equal was never satisfiable by a real run, so every genuine invocation was scored
        // `insufficient` here. What the evidence actually has to show is that the replay happened
        // inside the invocation that produced the manifest, which is containment. Identity is
        // still proven separately by run id, flow digest, app/build fingerprints and the manifest
        // artifact entry that commits to the report bytes.
        if (rawManifest?.Lifecycle?.StartedAt is { } manifestStarted &&
            report?.StartedAt is { } reportStarted &&
            reportStarted.ToUniversalTime() < manifestStarted.ToUniversalTime())
        {
            missing.Add("lifecycle-started-at-match");
        }
        if (rawManifest?.Lifecycle?.EndedAt is { } manifestEnded &&
            report?.EndedAt is { } reportEnded &&
            reportEnded.ToUniversalTime() > manifestEnded.ToUniversalTime())
        {
            missing.Add("lifecycle-ended-at-match");
        }
        if ((rawManifest?.Lifecycle?.StartedAt is { } lifecycleStarted &&
             rawManifest.Lifecycle.EndedAt is { } lifecycleEnded &&
             lifecycleEnded < lifecycleStarted) ||
            (report?.StartedAt is { } runStarted &&
             report.EndedAt is { } runEnded &&
             runEnded < runStarted))
        {
            missing.Add("lifecycle-time-order");
        }
        if (!HasValidLifecycleSequence(rawManifest?.Lifecycle))
            missing.Add("lifecycle-stage-sequence");
        if (string.IsNullOrWhiteSpace(manifest.Host?.Os))
            missing.Add("host-os");
        if (artifactResolved &&
            string.IsNullOrWhiteSpace(manifest.Build?.AppBuildFingerprint))
            missing.Add("app-build-fingerprint");
        if (artifactResolved &&
            string.IsNullOrWhiteSpace(report?.Target?.AppBuildFingerprint))
            missing.Add("report-app-build-fingerprint");
        if (artifactResolved &&
            string.IsNullOrWhiteSpace(manifest.Build?.AppId))
            missing.Add("manifest-app-id");
        if (artifactResolved &&
            string.IsNullOrWhiteSpace(report?.Target?.AppId))
            missing.Add("report-app-id");
        AddPresenceBinding(
            missing,
            "app-source-fingerprint-binding",
            rawManifest?.Build?.AppSourceFingerprint,
            report?.Target?.AppSourceFingerprint);
        AddPresenceBinding(
            missing,
            "package-digest-binding",
            rawManifest?.Build?.PackageDigest,
            report?.Target?.PackageDigest);
        if (deviceLaunched &&
            string.IsNullOrWhiteSpace(manifest.Device?.Platform))
            missing.Add("device-platform");
        if (deviceLaunched &&
            string.IsNullOrWhiteSpace(report?.Target?.Platform))
            missing.Add("report-device-platform");
        if (deviceLaunched &&
            string.IsNullOrWhiteSpace(manifest.Device?.Profile))
            missing.Add("manifest-device-profile");
        if (deviceLaunched &&
            string.IsNullOrWhiteSpace(report?.Target?.DeviceProfile))
            missing.Add("report-device-profile");
        if (flowLoaded &&
            string.IsNullOrWhiteSpace(fingerprints.TestIdentityFingerprint))
            missing.Add("test-identity");
        if (flowLoaded &&
            !string.Equals(classification.FailureClass, "passed", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(fingerprints.IncidentFingerprint))
        {
            missing.Add("incident-identity");
        }
        if (flowLoaded &&
            !string.Equals(classification.FailureClass, "passed", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(fingerprints.OccurrenceFingerprint))
            missing.Add("occurrence-identity");
        if ((input.ImportedEvidence || rawManifest?.TestIdentityFingerprint is not null) &&
            !FingerprintMatches(
                rawManifest?.TestIdentityFingerprint,
                fingerprints.TestIdentityFingerprint))
        {
            missing.Add("test-identity-fingerprint-match");
        }
        if (input.ImportedEvidence &&
            flowLoaded &&
            string.IsNullOrWhiteSpace(rawManifest?.TestIdentityFingerprint))
        {
            missing.Add("manifest-test-identity-fingerprint");
        }
        if ((input.ImportedEvidence || rawManifest?.IncidentFingerprint is not null) &&
            !FingerprintMatches(
                rawManifest?.IncidentFingerprint,
                fingerprints.IncidentFingerprint))
        {
            missing.Add("incident-fingerprint-match");
        }
        if (input.ImportedEvidence &&
            flowLoaded &&
            !string.Equals(classification.FailureClass, "passed", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(rawManifest?.IncidentFingerprint))
        {
            missing.Add("manifest-incident-fingerprint");
        }
        if ((input.ImportedEvidence || rawManifest?.OccurrenceFingerprint is not null) &&
            !FingerprintMatches(
                rawManifest?.OccurrenceFingerprint,
                fingerprints.OccurrenceFingerprint))
        {
            missing.Add("occurrence-fingerprint-match");
        }
        if (input.ImportedEvidence &&
            flowLoaded &&
            !string.Equals(classification.FailureClass, "passed", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(rawManifest?.OccurrenceFingerprint))
        {
            missing.Add("manifest-occurrence-fingerprint");
        }

        AddExactMatch(
            missing,
            "app-id-match",
            rawManifest?.Build?.AppId,
            report?.Target?.AppId);
        AddFingerprintMatch(
            missing,
            "app-build-fingerprint-match",
            rawManifest?.Build?.AppBuildFingerprint,
            report?.Target?.AppBuildFingerprint,
            "app-build");
        AddFingerprintMatch(
            missing,
            "app-source-fingerprint-match",
            rawManifest?.Build?.AppSourceFingerprint,
            report?.Target?.AppSourceFingerprint,
            "app-source");
        AddFingerprintMatch(
            missing,
            "package-digest-match",
            rawManifest?.Build?.PackageDigest,
            report?.Target?.PackageDigest,
            "package");
        AddExactMatch(
            missing,
            "device-platform-match",
            rawManifest?.Device?.Platform,
            report?.Target?.Platform);
        AddDeviceProfileMatch(
            missing,
            rawManifest?.Device?.Profile,
            report?.Target?.DeviceProfile);

        var reportArtifact = (manifest.Artifacts ?? []).FirstOrDefault(static artifact =>
            string.Equals(artifact.Kind, "flow-run-report", StringComparison.Ordinal));
        if (reportArtifact is null || string.IsNullOrWhiteSpace(reportArtifact.Digest))
            missing.Add("flow-run-report-artifact");
        if (reportArtifact?.Redacted != true)
            missing.Add("flow-run-report-redaction");
        var reportIdentityArtifact = (report?.Artifacts ?? []).FirstOrDefault(static artifact =>
            string.Equals(artifact.Kind, "flow-run-report", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(report?.ReportDigest) &&
            !string.IsNullOrWhiteSpace(reportIdentityArtifact?.Digest) &&
            !string.Equals(
                MauiTestingContractRedactor.Fingerprint(report.ReportDigest, "report"),
                MauiTestingContractRedactor.Fingerprint(reportIdentityArtifact.Digest, "report"),
                StringComparison.Ordinal))
        {
            missing.Add("flow-run-report-digest-match");
        }
        if (!string.Equals(classification.FailureClass, "passed", StringComparison.Ordinal) &&
            report?.Failure is null &&
            (report?.Steps ?? []).All(static step => string.IsNullOrWhiteSpace(step.FailureClass)))
        {
            missing.Add("failure-classification");
        }
        if (StepScopedFailures.Contains(classification.FailureClass) &&
            string.IsNullOrWhiteSpace(report?.Failure?.StepId ?? report?.DivergenceStepId))
        {
            missing.Add("failure-step");
        }
        if (report?.Failure?.Retryable is { } reportedRetryable &&
            reportedRetryable != classification.Retryable)
        {
            missing.Add("failure-retryability-match");
        }
        if (report is not null && !MauiFlowRunReportSerializer.Validate(report).IsValid)
            missing.Add("flow-run-report-valid");

        return missing.ToList();
    }

    private static void AddExactMatch(
        ISet<string> missing,
        string code,
        string? manifestValue,
        string? reportValue)
    {
        if (!string.IsNullOrWhiteSpace(manifestValue) &&
            !string.IsNullOrWhiteSpace(reportValue) &&
            !string.Equals(
                manifestValue.Trim(),
                reportValue.Trim(),
                StringComparison.Ordinal))
        {
            missing.Add(code);
        }
    }

    private static void AddPresenceBinding(
        ISet<string> missing,
        string code,
        string? manifestValue,
        string? reportValue)
    {
        if (!string.IsNullOrWhiteSpace(manifestValue) !=
            !string.IsNullOrWhiteSpace(reportValue))
        {
            missing.Add(code);
        }
    }

    private static void AddFingerprintMatch(
        ISet<string> missing,
        string code,
        string? manifestValue,
        string? reportValue,
        string domain)
    {
        if (!string.IsNullOrWhiteSpace(manifestValue) &&
            !string.IsNullOrWhiteSpace(reportValue) &&
            !string.Equals(
                MauiTestingContractRedactor.Fingerprint(manifestValue, domain),
                MauiTestingContractRedactor.Fingerprint(reportValue, domain),
                StringComparison.Ordinal))
        {
            missing.Add(code);
        }
    }

    private static void AddDeviceProfileMatch(
        ISet<string> missing,
        string? manifestValue,
        string? reportValue)
    {
        if (!string.IsNullOrWhiteSpace(manifestValue) &&
            !string.IsNullOrWhiteSpace(reportValue) &&
            !string.Equals(
                MauiTestingContractRedactor.SafeDeviceProfile(manifestValue),
                MauiTestingContractRedactor.SafeDeviceProfile(reportValue),
                StringComparison.Ordinal))
        {
            missing.Add("device-profile-match");
        }
    }

    private static bool FingerprintMatches(string? stored, string? computed)
        => string.Equals(
            MauiTestingContractRedactor.NormalizeFingerprint(stored),
            MauiTestingContractRedactor.NormalizeFingerprint(computed),
            StringComparison.Ordinal);

    private static bool HasPassedLifecycleStage(
        MauiTestExecutionLifecycleFacts? lifecycle,
        string name)
        => (lifecycle?.Stages ?? []).Any(stage =>
            stage is not null &&
            string.Equals(stage.Name, name, StringComparison.Ordinal) &&
            string.Equals(stage.Status, "passed", StringComparison.Ordinal));

    private static bool HasValidLifecycleSequence(MauiTestExecutionLifecycleFacts? lifecycle)
    {
        if (lifecycle is null || lifecycle.Stages.Count == 0)
            return true;
        var expected = 1;
        foreach (var stage in lifecycle.Stages)
        {
            if (stage is null ||
                stage.Sequence != expected ||
                string.IsNullOrWhiteSpace(stage.Name) ||
                string.IsNullOrWhiteSpace(stage.Status))
            {
                return false;
            }
            expected++;
        }
        return true;
    }

    private static bool HasContradictoryOutcome(MauiFlowRunReport? report)
    {
        if (report?.Outcome is null)
            return false;
        var passed = string.Equals(
            report.Outcome.Status,
            MauiFlowRunOutcomes.Passed,
            StringComparison.Ordinal);
        if (passed && report.Failure is not null)
            return true;
        if (!passed &&
            report.Outcome.Terminal == true &&
            report.Failure is null)
        {
            return true;
        }
        if (report.Outcome.Verified == true && !passed)
            return true;
        return report.Verification?.Verified == true && !passed;
    }

    private static bool IsUnknownLifecycleOutcome(string? status)
        => status is
            MauiFlowRunOutcomes.UnknownCompletion or
            MauiFlowRunOutcomes.Cancelled or
            MauiFlowRunOutcomes.TimedOut or
            MauiFlowRunOutcomes.LeaseLost or
            MauiFlowRunOutcomes.Orphaned;

    private static bool SecondaryFailuresMatch(
        IEnumerable<MauiFlowSecondaryFailure>? manifestFailures,
        IEnumerable<MauiFlowSecondaryFailure>? reportFailures)
    {
        static string[] Project(IEnumerable<MauiFlowSecondaryFailure>? failures)
            => MauiFlowRunReportSerializer.NormalizeSecondaryFailures(failures)
                .Select(static failure => string.Join(
                    "\u001f",
                    failure.Phase,
                    failure.Code,
                    failure.Class,
                    failure.Retryable?.ToString() ?? "unstated"))
                .ToArray();

        return Project(manifestFailures).SequenceEqual(Project(reportFailures), StringComparer.Ordinal);
    }

    /// <summary>
    /// The exit category a correct runner must have reported for this report.
    /// </summary>
    /// <remarks>
    /// This mirrors <c>FlowExecutionCoordinator.ClassifyReport</c> composed with its overall
    /// exit-category rule, and the two must stay in lockstep: any divergence scores every genuine
    /// run's evidence as insufficient on an <c>exit-category-match</c> the runner cannot satisfy.
    /// An owned cleanup failure promotes only a passing or unverified run, for the same reason the
    /// runner refuses to promote the rest — a retryable infrastructure label on a real test failure
    /// or on a fail-closed refusal would invite exactly the retry those verdicts exist to prevent.
    /// </remarks>
    private static string? ExpectedExitCategory(MauiFlowRunReport? report)
    {
        var primary = ExpectedPrimaryExitCategory(report);
        return primary is "pass" or "unverified" && (report?.SecondaryFailures?.Count ?? 0) > 0
            ? "infrastructure-failure"
            : primary;
    }

    private static string? ExpectedPrimaryExitCategory(MauiFlowRunReport? report)
    {
        if (report?.Outcome is null)
            return null;
        if (string.Equals(report.Outcome.Status, MauiFlowRunOutcomes.Passed, StringComparison.Ordinal))
        {
            return report.Verification?.Verified == true || report.Outcome.Verified == true
                ? "pass"
                : "unverified";
        }
        if (IsUnknownLifecycleOutcome(report.Outcome.Status) ||
            report.Failure?.Class is
                MauiFlowFailureClasses.UnknownCompletion or
                MauiFlowFailureClasses.LeaseConflict or
                MauiFlowFailureClasses.LeaseLost or
                MauiFlowFailureClasses.Cancelled or
                MauiFlowFailureClasses.Timeout)
        {
            return "unknown-completion";
        }
        // Mirrors FlowExecutionCoordinator.ClassifyReport: a proven app crash is the app's fault,
        // not the harness's. Placed after the unknown-completion branch so an unknown mutation is
        // never relaxed, and before the infrastructure branch so a crash is not filed as an
        // environment problem. The two must stay in lockstep or every crashed run scores its
        // evidence as insufficient on an "exit-category-match" the runner cannot satisfy.
        if (report.Failure?.Class is MauiFlowFailureClasses.AppCrash)
            return "test-failure";
        if (string.Equals(
                report.Outcome.Status,
                MauiFlowRunOutcomes.InfrastructureError,
                StringComparison.Ordinal) ||
            report.Failure?.Class is
                MauiFlowFailureClasses.Infrastructure or
                MauiFlowFailureClasses.Transport or
                MauiFlowFailureClasses.AgentDisconnected)
        {
            return "infrastructure-failure";
        }
        if (report.Failure?.Class is
            MauiFlowFailureClasses.FlowInvalid or
            MauiFlowFailureClasses.SchemaUnsupported or
            MauiFlowFailureClasses.CapabilityMissing or
            MauiFlowFailureClasses.PreconditionUnsatisfied or
            MauiFlowFailureClasses.ResetFailed)
        {
            return "invalid-configuration";
        }
        return "test-failure";
    }

    private static string? ReportExitCategory(MauiFlowRunReport? report)
        => report?.ExtensionData is not null &&
           report.ExtensionData.TryGetValue("exitCategory", out var value) &&
           value.ValueKind == System.Text.Json.JsonValueKind.String
            ? MauiTestingContractRedactor.SafeCode(value.GetString())
            : null;

    private static List<string> AllowedActions(
        MauiFlowFailureClassification classification,
        bool sufficient,
        bool imported,
        bool repairEligible)
    {
        var actions = new HashSet<string>(StringComparer.Ordinal);
        if (!sufficient)
            actions.Add(MauiFlowTriageNextActions.CollectEvidence);
        if (imported)
            actions.Add(MauiFlowTriageNextActions.ReproduceLocally);
        if (sufficient && classification.Retryable && !imported)
            actions.Add(MauiFlowTriageNextActions.RetryRun);

        switch (classification.FailureClass)
        {
            case "passed":
                break;
            case MauiFlowFailureClasses.FlowInvalid:
            case MauiFlowFailureClasses.SchemaUnsupported:
                actions.Add(MauiFlowTriageNextActions.CorrectFlow);
                break;
            case MauiFlowFailureClasses.CapabilityMissing:
                actions.Add(MauiFlowTriageNextActions.SelectSupportedTarget);
                break;
            case MauiFlowFailureClasses.PreconditionUnsatisfied:
            case MauiFlowFailureClasses.RouteStateDrift:
            case MauiFlowFailureClasses.ResetFailed:
                actions.Add(MauiFlowTriageNextActions.RestorePreconditions);
                break;
            case MauiFlowFailureClasses.LocatorNotFound:
                actions.Add(repairEligible
                    ? MauiFlowTriageNextActions.ReviewSelectorRepair
                    : MauiFlowTriageNextActions.InspectSelectorEvidence);
                break;
            case MauiFlowFailureClasses.LocatorAmbiguous:
                actions.Add(MauiFlowTriageNextActions.InspectSelectorEvidence);
                break;
            case MauiFlowFailureClasses.NotVisible:
            case MauiFlowFailureClasses.Disabled:
            case MauiFlowFailureClasses.UnstableBounds:
                actions.Add(MauiFlowTriageNextActions.InspectActionability);
                break;
            case MauiFlowFailureClasses.AssertionFailed:
                actions.Add(MauiFlowTriageNextActions.InvestigateAppRegression);
                break;
            case MauiFlowFailureClasses.SecretUnavailable:
            case MauiFlowFailureClasses.UnsafeValue:
                actions.Add(MauiFlowTriageNextActions.ProvideSensitiveInput);
                break;
            case MauiFlowFailureClasses.UnknownCompletion:
            case MauiFlowFailureClasses.WorkflowCommandConflict:
                actions.Add(MauiFlowTriageNextActions.FenceUnknownCompletion);
                break;
            case MauiFlowFailureClasses.Transport:
            case MauiFlowFailureClasses.AgentDisconnected:
            case MauiFlowFailureClasses.Infrastructure:
                actions.Add(MauiFlowTriageNextActions.InspectInfrastructure);
                break;
            default:
                actions.Add(MauiFlowTriageNextActions.InvestigateFailure);
                break;
        }

        return actions.OrderBy(static value => value, StringComparer.Ordinal).ToList();
    }

    private static MauiFlowStepAttempt? FindFailedStep(MauiFlowRunReport? report)
    {
        if (report is null)
            return null;
        var stepId = report.Failure?.StepId ?? report.DivergenceStepId;
        return (report.Steps ?? []).FirstOrDefault(step =>
                string.Equals(step.StepId, stepId, StringComparison.Ordinal))
            ?? (report.Steps ?? []).FirstOrDefault(static step => !string.IsNullOrWhiteSpace(step.FailureClass));
    }

    /// <summary>
    /// The step the report itself names as the failure, with no first-failed-step fallback.
    /// Assertion-target drift is read only from here: crediting this failure with a different
    /// step's unresolved assertion selector would invent a signal the report does not carry.
    /// </summary>
    private static MauiFlowStepAttempt? FindNamedFailedStep(MauiFlowRunReport? report)
    {
        var stepId = report?.Failure?.StepId ?? report?.DivergenceStepId;
        return string.IsNullOrWhiteSpace(stepId)
            ? null
            : (report?.Steps ?? []).FirstOrDefault(step =>
                string.Equals(step.StepId, stepId, StringComparison.Ordinal));
    }

    private static MauiTestExecutionOutcomeFacts? ProjectOutcome(
        MauiFlowRunReport? report,
        MauiTestExecutionOutcomeFacts? manifestOutcome)
        => report?.Outcome is null ? manifestOutcome : new MauiTestExecutionOutcomeFacts
        {
            Status = MauiTestingContractRedactor.SafeCode(report.Outcome.Status),
            ExitCategory = manifestOutcome?.ExitCategory ??
                MauiTestingContractRedactor.SafeCode(report.Failure?.Category),
            Terminal = report.Outcome.Terminal,
            Verified = report.Verification?.Verified ?? report.Outcome.Verified,
            UnknownCompletion = IsUnknownLifecycleOutcome(report.Outcome.Status),
            SecondaryFailures = MauiFlowRunReportSerializer.NormalizeSecondaryFailures(
                report.SecondaryFailures),
        };

    private static bool? CheckpointsMatch(MauiFlowCheckpoint? expected, MauiFlowCheckpoint? observed)
    {
        if (expected is null || observed is null)
            return null;
        return Matches(expected.AppBuildFingerprint, observed.AppBuildFingerprint) &&
            Matches(expected.AgentInstanceId, observed.AgentInstanceId) &&
            Matches(expected.SeedFingerprint, observed.SeedFingerprint) &&
            Matches(expected.BackendStateFingerprint, observed.BackendStateFingerprint) &&
            Matches(expected.Locale, observed.Locale) &&
            Matches(expected.Theme, observed.Theme) &&
            Matches(expected.Orientation, observed.Orientation) &&
            Matches(expected.DisplayProfile, observed.DisplayProfile) &&
            Matches(expected.CollectionItemKey, observed.CollectionItemKey);
    }

    private static bool? RoutesMatch(MauiFlowCheckpoint? expected, MauiFlowCheckpoint? observed)
    {
        if (expected?.Route is null || observed?.Route is null)
            return null;
        return string.Equals(expected.Route, observed.Route, StringComparison.Ordinal);
    }

    private static bool Matches(string? expected, string? observed)
        => expected is null || string.Equals(expected, observed, StringComparison.Ordinal);
}
