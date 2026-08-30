namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Trusted observations about the selector that was active before a failed lookup. A repair may
/// replace a selector only when this evidence proves that the old selector was uniquely resolved
/// in a prior trusted run.
/// </summary>
public sealed class MauiRepairPriorSelectorResolution
{
    public string? RunId { get; set; }
    public bool WasUniquelyResolved { get; set; }
    public bool TrustedRun { get; set; }
    public string? Trust { get; set; }
    public FlowSelector? ActiveSelector { get; set; }
    public MauiElementFingerprint? Fingerprint { get; set; }
}

/// <summary>
/// Pure input to the repair eligibility evaluator. Hosts populate current checkpoint and trust
/// facts; the evaluator neither contacts an agent nor reads a workspace.
/// </summary>
public sealed class MauiFlowRepairEligibilityInput
{
    public MauiFlowRunReport? Run { get; set; }
    public MauiTestPlan? Plan { get; set; }
    public MauiFlowReplayEligibilityDecision? ReplayEligibility { get; set; }
    public MauiFlowCheckpoint? ExpectedCheckpoint { get; set; }
    public MauiFlowCheckpoint? CurrentCheckpoint { get; set; }
    public bool? BeforeDispatch { get; set; }
    public bool IsCurrentLocalRun { get; set; }
    public string? ArtifactTrust { get; set; }
    public MauiArtifactTrustRecord? ImportedArtifact { get; set; }
    public MauiRepairPriorSelectorResolution? PriorActiveSelectorResolution { get; set; }
    public MauiElementFingerprint? TargetFingerprint { get; set; }
    public List<string> AdditionalFailureCodes { get; set; } = [];
}

/// <summary>A non-silent explanation for an allowed or denied repair decision.</summary>
public sealed class MauiFlowRepairEligibilityReason
{
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public bool Blocking { get; init; } = true;
}

/// <summary>Deterministic, fully explained repair admission result.</summary>
public sealed class MauiFlowRepairEligibilityDecision
{
    public bool Eligible { get; set; }
    public string? FailureCode { get; set; }
    public string? SourceRunId { get; set; }
    public string? SourceStepId { get; set; }
    public MauiFlowCheckpoint? ExpectedCheckpoint { get; set; }
    public MauiFlowCheckpoint? CurrentCheckpoint { get; set; }
    public string? Trust { get; set; }
    public List<MauiFlowRepairEligibilityReason> Reasons { get; } = [];
}

/// <summary>
/// The sole policy gate for human-approved flow selector repair. It is intentionally pure and
/// fail-closed: callers receive every ineligibility reason instead of a generic downgrade.
/// </summary>
public static class MauiFlowRepairEligibilityEvaluator
{
    private static readonly (string Name, Func<MauiFlowCheckpoint, string?> Get)[] CheckpointFields =
    [
        ("app-build", static value => value.AppBuildFingerprint),
        ("agent-instance", static value => value.AgentInstanceId),
        ("seed", static value => value.SeedFingerprint),
        ("backend-state", static value => value.BackendStateFingerprint),
        ("route", static value => value.Route),
        ("window", static value => value.Window),
        ("modal", static value => value.Modal),
        ("locale", static value => value.Locale),
        ("theme", static value => value.Theme),
        ("orientation", static value => value.Orientation),
        ("display", static value => value.DisplayProfile),
        ("collection-item-key", static value => value.CollectionItemKey),
    ];

    private static readonly HashSet<string> BlockingFailureCodes = new(StringComparer.Ordinal)
    {
        MauiFlowFailureClasses.FlowInvalid,
        MauiFlowFailureClasses.SchemaUnsupported,
        MauiFlowFailureClasses.LeaseConflict,
        MauiFlowFailureClasses.LeaseLost,
        MauiFlowFailureClasses.Cancelled,
        MauiFlowFailureClasses.Timeout,
        MauiFlowFailureClasses.UnknownCompletion,
        MauiFlowFailureClasses.Infrastructure,
        MauiFlowFailureClasses.Transport,
        MauiFlowFailureClasses.AgentDisconnected,
        MauiFlowFailureClasses.CapabilityMissing,
        MauiFlowFailureClasses.SecretUnavailable,
        MauiFlowFailureClasses.UnsafeValue,
        MauiFlowFailureClasses.PreconditionUnsatisfied,
        MauiFlowFailureClasses.RouteStateDrift,
        MauiFlowFailureClasses.LocatorAmbiguous,
        MauiFlowFailureClasses.NotVisible,
        MauiFlowFailureClasses.Disabled,
        MauiFlowFailureClasses.UnstableBounds,
        MauiFlowFailureClasses.ActionRejected,
        MauiFlowFailureClasses.DriveFailed,
        MauiFlowFailureClasses.AssertionFailed,
        MauiFlowFailureClasses.ResetFailed,
        MauiFlowFailureClasses.WorkflowCommandConflict,
    };

    /// <summary>Evaluates every repair prerequisite without modifying a flow or running a replay.</summary>
    public static MauiFlowRepairEligibilityDecision Evaluate(MauiFlowRepairEligibilityInput? input)
    {
        input ??= new MauiFlowRepairEligibilityInput();
        var report = input.Run;
        var failure = report?.Failure;
        var failedStep = FindFailedStep(report, failure);
        var expected = input.ExpectedCheckpoint ?? failedStep?.ExpectedCheckpoint;
        var current = MergeCurrentCheckpoint(
            input.CurrentCheckpoint ?? failedStep?.ObservedCheckpoint,
            report?.Target);
        var trust = input.ArtifactTrust ?? input.ImportedArtifact?.Verification?.State;
        var decision = new MauiFlowRepairEligibilityDecision
        {
            FailureCode = failure?.Class ?? failure?.Code,
            SourceRunId = report?.RunId,
            SourceStepId = failure?.StepId ?? failedStep?.StepId,
            ExpectedCheckpoint = expected,
            CurrentCheckpoint = current,
            Trust = input.IsCurrentLocalRun ? "current-local-run" : trust,
        };

        if (!string.Equals(
                decision.FailureCode,
                MauiFlowFailureClasses.LocatorNotFound,
                StringComparison.Ordinal))
        {
            Add(
                decision,
                "failure-not-locator-not-found",
                "Only a primary locator-not-found failure can create a selector repair proposal.");
        }

        var beforeDispatch = input.BeforeDispatch ??
            (failedStep?.Dispatch is null &&
             !string.Equals(failedStep?.CompletionCertainty, "unknown", StringComparison.Ordinal) &&
             string.Equals(failure?.Phase, "resolution", StringComparison.Ordinal));
        if (beforeDispatch != true)
        {
            Add(
                decision,
                "failure-not-pre-dispatch",
                "The failed lookup was not proven to occur before action dispatch.");
        }

        CompareCheckpoint(decision, expected, current);
        VerifyTrust(decision, input, trust);
        VerifyOwnedCleanup(decision, report);
        VerifyEvidenceComplete(decision, report);
        VerifyFailureSafety(decision, report, failure, failedStep, input.AdditionalFailureCodes);
        VerifyReplaySafety(decision, input.Plan, report, input.ReplayEligibility);
        VerifyIndependentOracle(decision, input.Plan, report);
        VerifyPriorResolution(decision, input.PriorActiveSelectorResolution);
        VerifyTargetScope(
            decision,
            input.TargetFingerprint ?? input.PriorActiveSelectorResolution?.Fingerprint ?? failedStep?.Fingerprint);

        decision.Eligible = decision.Reasons.All(static reason => !reason.Blocking);
        return decision;
    }

    private static MauiFlowStepAttempt? FindFailedStep(MauiFlowRunReport? report, MauiFlowFailure? failure)
    {
        if (report is null)
            return null;
        var stepId = failure?.StepId ?? report.DivergenceStepId;
        return string.IsNullOrWhiteSpace(stepId)
            ? null
            : report.Steps.FirstOrDefault(step => string.Equals(step.StepId, stepId, StringComparison.Ordinal));
    }

    private static MauiFlowCheckpoint? MergeCurrentCheckpoint(
        MauiFlowCheckpoint? observed,
        MauiFlowRunTarget? target)
    {
        if (observed is null && target is null)
            return null;

        return new MauiFlowCheckpoint
        {
            AppBuildFingerprint = observed?.AppBuildFingerprint ?? target?.AppBuildFingerprint,
            AgentInstanceId = observed?.AgentInstanceId ?? target?.AgentInstanceId,
            SeedFingerprint = observed?.SeedFingerprint,
            BackendStateFingerprint = observed?.BackendStateFingerprint,
            Route = observed?.Route,
            Window = observed?.Window,
            Modal = observed?.Modal,
            Locale = observed?.Locale ?? target?.Locale,
            Theme = observed?.Theme ?? target?.Theme,
            Orientation = observed?.Orientation ?? target?.Orientation,
            DisplayProfile = observed?.DisplayProfile ?? target?.DisplayProfile,
            CollectionItemKey = observed?.CollectionItemKey,
        };
    }

    private static void CompareCheckpoint(
        MauiFlowRepairEligibilityDecision decision,
        MauiFlowCheckpoint? expected,
        MauiFlowCheckpoint? current)
    {
        if (expected is null)
        {
            Add(decision, "checkpoint-expected-missing", "The failed step has no expected pre-step checkpoint.");
            return;
        }
        if (current is null)
        {
            Add(decision, "checkpoint-current-missing", "The current target has no observed pre-step checkpoint.");
            return;
        }

        foreach (var (name, get) in CheckpointFields)
        {
            var expectedValue = get(expected);
            var currentValue = get(current);
            if (string.IsNullOrWhiteSpace(expectedValue))
            {
                Add(
                    decision,
                    "checkpoint-" + name + "-expected-missing",
                    $"The expected {name} checkpoint fact is missing.");
            }
            else if (string.IsNullOrWhiteSpace(currentValue))
            {
                Add(
                    decision,
                    "checkpoint-" + name + "-current-missing",
                    $"The current {name} checkpoint fact is missing.");
            }
            else if (!string.Equals(expectedValue, currentValue, StringComparison.Ordinal))
            {
                Add(
                    decision,
                    "checkpoint-" + name + "-mismatch",
                    $"The current {name} checkpoint does not match the recorded pre-step checkpoint.");
            }
        }
    }

    private static void VerifyTrust(
        MauiFlowRepairEligibilityDecision decision,
        MauiFlowRepairEligibilityInput input,
        string? trust)
    {
        if (input.IsCurrentLocalRun)
            return;

        if (!string.Equals(trust, MauiArtifactTrustStates.LocallyReproduced, StringComparison.Ordinal))
        {
            Add(
                decision,
                "artifact-not-locally-reproduced",
                "Captured or attested evidence is diagnostic-only until a matching local reproduction is recorded.");
            return;
        }

        if (input.ImportedArtifact is not null &&
            MauiFlowRepairProposalTrustPolicy.CanCreateProposal(input.ImportedArtifact).Allowed != true)
        {
            Add(
                decision,
                "artifact-local-reproduction-binding-required",
                "The locally-reproduced imported artifact has no valid binding to a distinct local run.");
        }
    }

    /// <summary>
    /// Refuses a repair proposed from a run whose owned teardown did not complete.
    /// </summary>
    /// <remarks>
    /// A repair is validated by replaying against the same target, and the checkpoint facts that
    /// admit it were recorded before a cleanup the host could not finish. Leftover installed
    /// packages, a still-running app, or an undeleted build root mean the state the replay would
    /// start from is not the state the evidence describes. This is deliberately a separate refusal
    /// from the failure-class gate: the run's own failure may be perfectly repairable, and saying
    /// so plainly is what stops this from being read as a misclassification.
    /// </remarks>
    private static void VerifyOwnedCleanup(
        MauiFlowRepairEligibilityDecision decision,
        MauiFlowRunReport? report)
    {
        if ((report?.SecondaryFailures?.Count ?? 0) == 0)
            return;

        Add(
            decision,
            "owned-cleanup-incomplete",
            "Owned execution cleanup did not complete, so the run's target state cannot be trusted for repair validation.");
    }

    /// <summary>
    /// Refuses a repair proposed from a report that was truncated to satisfy a size or cardinality
    /// limit.
    /// </summary>
    /// <remarks>
    /// Every other gate here reads the report as if absence were evidence of absence. Truncation
    /// breaks that: the serializer drops steps, events, business oracles, and replay eligibility to
    /// fit a budget, so a failed oracle or a blocking failure class can be missing from the very
    /// list this evaluator scans. Admitting a repair from such a report would mean admitting it
    /// because the disqualifying evidence was discarded, which is the precise opposite of the
    /// fail-closed posture the rest of this gate keeps. The refusal is its own code so the reader is
    /// told the evidence is incomplete rather than that some specific prerequisite was unmet.
    /// </remarks>
    private static void VerifyEvidenceComplete(
        MauiFlowRepairEligibilityDecision decision,
        MauiFlowRunReport? report)
    {
        if (report?.Truncated != true)
            return;

        Add(
            decision,
            "run-evidence-truncated",
            "The run report was truncated, so the evidence a selector repair depends on is incomplete.");
    }

    private static void VerifyFailureSafety(
        MauiFlowRepairEligibilityDecision decision,
        MauiFlowRunReport? report,
        MauiFlowFailure? failure,
        MauiFlowStepAttempt? failedStep,
        IEnumerable<string> additionalCodes)
    {
        var candidates = new List<string?>
        {
            failure?.Code,
            failure?.Class,
            failedStep?.FailureClass,
            report?.Outcome?.Status,
            failedStep?.CompletionCertainty == "unknown"
                ? MauiFlowFailureClasses.UnknownCompletion
                : null,
        };
        if (report is not null)
        {
            candidates.AddRange(report.Steps.Select(static step => step.FailureClass));
            candidates.AddRange(report.Steps
                .Where(static step => string.Equals(step.CompletionCertainty, "unknown", StringComparison.Ordinal))
                .Select(static _ => MauiFlowFailureClasses.UnknownCompletion));
        }
        candidates.AddRange(additionalCodes);

        foreach (var code in candidates
                     .Where(static code => !string.IsNullOrWhiteSpace(code))
                     .Select(static code => code!.Trim().ToLowerInvariant())
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(static code => code, StringComparer.Ordinal))
        {
            if (BlockingFailureCodes.Contains(code) ||
                code is MauiFlowRunOutcomes.UnknownCompletion or MauiFlowRunOutcomes.InfrastructureError)
            {
                Add(
                    decision,
                    "blocking-failure-" + code,
                    $"Repair is prohibited while the run contains '{code}'.");
            }
        }
    }

    /// <remarks>
    /// A caller that holds the plan supplies it. A caller working from bounded evidence — CLI
    /// triage, for instance, whose inputs are deliberately just the manifest and the report — does
    /// not. The run report records the policy the run was actually admitted under, so falling back
    /// to it stops the decision from asserting "no replayable policy is declared" about a run whose
    /// own report names one. The fallback narrows nothing: an absent report policy still reads as
    /// unspecified and still blocks.
    /// </remarks>
    private static void VerifyReplaySafety(
        MauiFlowRepairEligibilityDecision decision,
        MauiTestPlan? plan,
        MauiFlowRunReport? report,
        MauiFlowReplayEligibilityDecision? replay)
    {
        var policy = plan is not null
            ? plan.ParsedSideEffectPolicy
            : MauiFlowSideEffectPolicies.Parse(report?.SideEffectPolicy);
        if (policy is MauiFlowSideEffectPolicy.Unspecified or MauiFlowSideEffectPolicy.NonReplayable)
        {
            Add(
                decision,
                "side-effect-policy-repair-prohibited",
                "A repair requires a declared replayable side-effect policy (none, app-state-resettable, test-tenant-resettable, or compensated).");
        }

        if (replay is not null && replay.RepairValidationAllowed != true)
        {
            Add(
                decision,
                "replay-safety-repair-prohibited",
                "The flow reset, compensation, completion, or oracle facts do not permit a repair-validation replay.");
        }
    }

    /// <remarks>
    /// <para>
    /// A declaration proves an oracle was required; only a result proves what it observed. Reading
    /// the plan alone let a run through whose required independent oracle was present in the report
    /// and had failed — exactly the app-regression shape a selector repair must never be proposed
    /// for, because the symptom is indistinguishable from repairable drift. Any recorded oracle
    /// failure therefore blocks under its own code, in both the plan and the planless path: a
    /// business fact the run itself reports as unmet is not made safe by the plan calling that
    /// oracle optional, or by the host clearing the result's independence flag.
    /// </para>
    /// <para>
    /// A declared oracle with no result is not evidence of failure, and this gate does not
    /// manufacture one; the caller's replay-safety decision owns unevaluated verification and
    /// refuses it as <c>replay-safety-repair-prohibited</c>. That means a caller who supplies no
    /// replay decision has asserted nothing about it, and this gate does not assert it either —
    /// naming an unevaluated oracle "failed" here would report unfinished verification as a
    /// regression.
    /// </para>
    /// <para>
    /// Without the plan, the run report's recorded oracle results are the only evidence that a
    /// required independent oracle existed and was evaluated. Only a result the run host marked
    /// independent and successful counts as that evidence, so this cannot turn an unverified run
    /// into a repairable one.
    /// </para>
    /// </remarks>
    private static void VerifyIndependentOracle(
        MauiFlowRepairEligibilityDecision decision,
        MauiTestPlan? plan,
        MauiFlowRunReport? report)
    {
        var results = (report?.BusinessOracles ?? [])
            .Where(static oracle => oracle is not null)
            .ToList();

        var failed = results.FirstOrDefault(static oracle => oracle.Succeeded == false);
        if (failed is not null)
        {
            var oracleId = string.IsNullOrWhiteSpace(failed.OracleId)
                ? "(unnamed)"
                : failed.OracleId.Trim();
            Add(
                decision,
                "independent-oracle-failed",
                $"Business oracle '{oracleId}' reported failure in this run, so the failure cannot " +
                "be attributed to selector drift.");
        }

        var declared = plan is not null
            ? plan.IndependentBusinessOracles
                .Where(static oracle => oracle is not null)
                .Concat(plan.BusinessOracles
                    .Where(static oracle => oracle is not null)
                    .Select(static oracle => new MauiIndependentBusinessOracleDeclaration
                    {
                        OracleId = oracle.OracleId,
                        Required = oracle.Required,
                        Independent = oracle.Independent,
                    }))
                .Any(static oracle =>
                    oracle.Required &&
                    oracle.Independent &&
                    !string.IsNullOrWhiteSpace(oracle.OracleId))
            : results.Any(static oracle =>
                oracle is { Independent: true, Succeeded: true } &&
                !string.IsNullOrWhiteSpace(oracle.OracleId));
        if (!declared)
        {
            Add(
                decision,
                "independent-oracle-absent",
                "An independent required business oracle must be declared before a selector repair can be proposed.");
        }
    }

    private static void VerifyPriorResolution(
        MauiFlowRepairEligibilityDecision decision,
        MauiRepairPriorSelectorResolution? prior)
    {
        if (prior?.WasUniquelyResolved != true)
        {
            Add(
                decision,
                "prior-selector-not-unique",
                "The active selector was not previously proven to resolve uniquely.");
        }
        if (prior?.TrustedRun != true ||
            string.IsNullOrWhiteSpace(prior.RunId) ||
            string.IsNullOrWhiteSpace(prior.Fingerprint?.FingerprintId))
        {
            Add(
                decision,
                "prior-selector-not-trusted",
                "The active selector lacks a prior trusted unique-resolution fingerprint.");
        }
    }

    private static void VerifyTargetScope(
        MauiFlowRepairEligibilityDecision decision,
        MauiElementFingerprint? fingerprint)
    {
        var collection = fingerprint?.Collection;
        if (collection is null)
            return;

        var scoped = !string.IsNullOrWhiteSpace(collection.Scope) &&
            !string.IsNullOrWhiteSpace(collection.ItemKey);
        if (collection.Virtualized == true && !scoped)
        {
            Add(
                decision,
                "target-virtualized-unscoped",
                "An unscoped virtualized row cannot receive a selector repair.");
        }
        if (!string.IsNullOrWhiteSpace(collection.TemplateKind) && !scoped)
        {
            Add(
                decision,
                "target-template-unscoped",
                "An unscoped template row cannot receive a selector repair.");
        }
    }

    private static void Add(MauiFlowRepairEligibilityDecision decision, string code, string message)
    {
        if (decision.Reasons.Any(reason => string.Equals(reason.Code, code, StringComparison.Ordinal)))
            return;
        decision.Reasons.Add(new MauiFlowRepairEligibilityReason { Code = code, Message = message });
    }
}
