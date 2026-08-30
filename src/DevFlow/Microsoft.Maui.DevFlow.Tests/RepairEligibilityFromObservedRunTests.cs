using Microsoft.Maui.DevFlow.Testing;
using Xunit;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// A real emulator run of `drifted-assert-after-commit` produced every fact the repair gate asks
/// for — locator-not-found on an action, resolution phase, no dispatch receipt, matching
/// checkpoints, independent oracle verified — and still reported `repairEligible: false`. These pin
/// the classifier against those exact observed facts so the gate can be trusted either way.
/// </summary>
public class RepairEligibilityFromObservedRunTests
{
    private static MauiFlowCheckpoint Expected() => new()
    {
        // What `flow run` now seals onto the expected checkpoint after preconditions verify: the
        // plan's declared route/window plus the run-identity facts a plan author cannot know.
        AppBuildFingerprint = "1.0:1",
        AgentInstanceId = "930a7a61f72fadf3e9e7cebcafd27f39",
        Route = "//native",
        Window = "window-0",
        Locale = "en-US",
        Theme = "Light",
        Orientation = "Portrait",
    };

    private static MauiFlowCheckpoint Observed() => new()
    {
        AppBuildFingerprint = "1.0:1",
        AgentInstanceId = "930a7a61f72fadf3e9e7cebcafd27f39",
        Route = "//native",
        Window = "window-0",
        Locale = "en-US",
        Theme = "Light",
        Orientation = "Portrait",
    };

    /// <summary>The exact facts the runner built for step 3 of the observed emulator run.</summary>
    private static MauiFlowFailureFacts ObservedRunFacts() => new()
    {
        LegacyFailureKind = FlowFailureKinds.NotFound,
        // Step 3 is a tap with no assertions, so no assertion selector drifted.
        AssertionTargetResolution = null,
        BeforeDispatch = true,
        CheckpointVerified = true,
        CheckpointMatches = true,
        RouteMatches = true,
    };

    [Fact]
    public void ActionSelectorDriftAfterCommit_IsRepairEligible()
    {
        var classification = MauiFlowFailureClassifier.Classify(ObservedRunFacts());

        Assert.Equal(MauiFlowFailureClasses.LocatorNotFound, classification.FailureClass);
        Assert.True(
            classification.RepairEligible,
            "An action selector that drifted after the business outcome verified is the one shape " +
            "selector repair exists for.");
    }

    [Fact]
    public void AssertionSelectorDrift_IsNeverRepairEligible()
    {
        var facts = ObservedRunFacts();
        facts.AssertionTargetResolution = FlowFailureKinds.NotFound;

        var classification = MauiFlowFailureClassifier.Classify(facts);

        // Re-pointing an assertion would change what the test checks. That must never be automatic.
        Assert.False(classification.RepairEligible);
    }

    [Fact]
    public void CheckpointDrift_IsNeverRepairEligible()
    {
        var facts = ObservedRunFacts();
        facts.CheckpointMatches = false;

        var classification = MauiFlowFailureClassifier.Classify(facts);

        Assert.False(classification.RepairEligible);
    }

    [Fact]
    public void CheckpointsFromTheObservedRun_Match()
    {
        // `flow run` seals the run-identity facts onto the expected checkpoint after preconditions
        // verify, so the checkpoint comparison no longer reports every one of them as missing.
        var input = new MauiFlowRepairEligibilityInput
        {
            Run = new MauiFlowRunReport
            {
                Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Failed },
                Failure = new MauiFlowFailure
                {
                    Class = MauiFlowFailureClasses.LocatorNotFound,
                    StepId = "3",
                },
                Steps =
                [
                    new MauiFlowStepAttempt
                    {
                        StepId = "3",
                        ExpectedCheckpoint = Expected(),
                        ObservedCheckpoint = Observed(),
                    },
                ],
            },
            ExpectedCheckpoint = Expected(),
            CurrentCheckpoint = Observed(),
        };

        var decision = MauiFlowRepairEligibilityEvaluator.Evaluate(input);

        // Seed and backend facts stay legitimately absent on a plan that declares no reset, so this
        // asserts only that the facts the run *can* establish are no longer reported missing.
        Assert.DoesNotContain(
            decision.Reasons,
            reason => reason.Code is "checkpoint-app-build-expected-missing"
                or "checkpoint-agent-instance-expected-missing"
                or "checkpoint-locale-expected-missing"
                or "checkpoint-theme-expected-missing"
                or "checkpoint-orientation-expected-missing");
    }

    /// <summary>
    /// The observed run reported `replayEligibility.repairEligibility: true` and
    /// `failure.repairEligible: false` in the same artifact. The cause was ordering, not
    /// classification: the runner materialised the conjunction while the run's required independent
    /// oracle had not been evaluated yet, and the post-run decision that lifted replay eligibility
    /// never revisited the failure.
    /// </summary>
    private static MauiFlowRunReport ReportWithClassifiedFailure(bool repairEligibility) => new()
    {
        RunId = "run-1",
        Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Failed },
        ReplayEligibility = new MauiFlowReplayEligibilityDecision
        {
            SideEffectPolicy = MauiFlowSideEffectPolicies.None,
            RepairValidationAllowed = repairEligibility,
            RepairEligibility = repairEligibility,
        },
        Failure = MauiFlowFailureClassifier.ToFailure(
            MauiFlowFailureClassifier.Classify(ObservedRunFacts()),
            "failure-run-1",
            FlowFailureKinds.NotFound,
            "3",
            DateTimeOffset.UnixEpoch),
    };

    [Fact]
    public void AssertionFailure_IsNeverRepairEligible()
    {
        // A hard assertion that caught an app change is the one symptom that must never be
        // repaired into agreement with the regression it detected, whatever the oracles later say.
        var facts = ObservedRunFacts();
        facts.LegacyFailureKind = FlowFailureKinds.Assertion;

        var classification = MauiFlowFailureClassifier.Classify(facts);

        Assert.Equal(MauiFlowFailureClasses.AssertionFailed, classification.FailureClass);
        Assert.False(classification.RepairEligible);
    }

    /// <summary>
    /// The control for the two tests below. Every repair prerequisite is satisfied here — pre-
    /// dispatch locator-not-found, a complete matching checkpoint, a current local run, a
    /// replayable policy, a prior trusted unique resolution of the active selector, and a required
    /// independent oracle that succeeded — so eligibility genuinely turns on the oracle result and
    /// nothing else. Without this control, a test that merely observes `Eligible == false` proves
    /// nothing: the same answer comes back for a missing prior resolution or an incomplete
    /// checkpoint.
    /// </summary>
    [Fact]
    public void SelectorDriftWithEveryPrerequisiteSatisfied_IsRepairEligible()
    {
        var decision = MauiFlowRepairEligibilityEvaluator.Evaluate(
            RepairableInput(RunWithOracle(succeeded: true)));

        Assert.True(decision.Eligible);
        Assert.Empty(decision.Reasons);
    }

    [Fact]
    public void SelectorDriftWithAFailedBusinessOracle_IsNotRepairEligible()
    {
        // The dangerous shape: the symptom looks exactly like repairable action-selector drift, but
        // the independent oracle says the app never performed the business outcome. A regression
        // can present as drift, so the oracle -- not the symptom -- has to decide. The classifier
        // still reads this as repairable, which is precisely why the gate needs its own check.
        var run = RunWithOracle(succeeded: false);

        var decision = MauiFlowRepairEligibilityEvaluator.Evaluate(RepairableInput(run));

        Assert.True(
            run.Failure!.ClassifierRepairEligible,
            "The symptom alone still reads as repairable drift; that is why the oracle check matters.");
        Assert.False(decision.Eligible);
        var reason = Assert.Single(decision.Reasons);
        Assert.Equal("independent-oracle-failed", reason.Code);
        Assert.Contains("todo-ledger-record", reason.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectorDriftWithADeclaredButUnevaluatedOracle_IsHeldByReplaySafety()
    {
        // A required oracle that produced no result is not a failed oracle, and calling it one
        // would report unfinished verification as a regression. The caller's replay-safety
        // decision owns that case; when it says repair validation is not allowed, the gate must
        // refuse on that ground and must not invent an oracle verdict of its own.
        var run = RunWithOracle(succeeded: true);
        run.BusinessOracles = [];
        run.ReplayEligibility!.RepairValidationAllowed = false;

        var decision = MauiFlowRepairEligibilityEvaluator.Evaluate(RepairableInput(run));

        var codes = decision.Reasons.Select(static reason => reason.Code).ToList();
        Assert.False(decision.Eligible);
        Assert.Contains("replay-safety-repair-prohibited", codes);
        Assert.DoesNotContain("independent-oracle-failed", codes);
        Assert.DoesNotContain("independent-oracle-absent", codes);
    }

    [Fact]
    public void SelectorDriftWithAFailedOracleThePlanCallsOptional_IsStillNotRepairEligible()
    {
        // A plan can declare an oracle non-required or non-independent; the run still reports that
        // the app did not produce the business outcome. Reading the declaration instead of the
        // result would make a regression repairable by editing the plan, which is exactly the
        // wrong direction of authority.
        var run = RunWithOracle(succeeded: false);
        run.BusinessOracles[0].Independent = false;
        var input = RepairableInput(run);
        input.Plan!.IndependentBusinessOracles[0].OracleId = "unrelated-ledger-check";

        var decision = MauiFlowRepairEligibilityEvaluator.Evaluate(input);

        Assert.False(decision.Eligible);
        Assert.Contains(
            "independent-oracle-failed",
            decision.Reasons.Select(static reason => reason.Code));
    }

    [Fact]
    public void RepairEligibilityGate_RestoresTheClassifierVerdictWhenOraclesLaterSucceed()
    {
        // The run starts with a required oracle that has not been evaluated, so replay safety is
        // provisionally false and the gate suppresses the classifier's verdict.
        var report = ReportWithClassifiedFailure(repairEligibility: false);
        MauiFlowFailureClassifier.ApplyRepairEligibilityGate(report);

        Assert.False(report.Failure!.RepairEligible);
        Assert.True(
            report.Failure.ClassifierRepairEligible,
            "Suppressing the conjunction must not erase what the classifier concluded.");

        // The oracle then succeeds and the host installs the real decision. Re-applying the gate
        // must restore eligibility rather than leave the report contradicting itself.
        report.ReplayEligibility!.RepairEligibility = true;
        MauiFlowFailureClassifier.ApplyRepairEligibilityGate(report);

        Assert.True(report.Failure.RepairEligible);
        Assert.DoesNotContain(
            MauiFlowRunReportSerializer.Validate(report).Errors,
            error => error.Contains("repair", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RepairEligibilityGate_NeverManufacturesEligibilityTheClassifierRefused()
    {
        var facts = ObservedRunFacts();
        facts.AssertionTargetResolution = FlowFailureKinds.NotFound;
        var report = ReportWithClassifiedFailure(repairEligibility: true);
        report.Failure = MauiFlowFailureClassifier.ToFailure(
            MauiFlowFailureClassifier.Classify(facts),
            "failure-run-1",
            FlowFailureKinds.NotFound,
            "3",
            DateTimeOffset.UnixEpoch);

        MauiFlowFailureClassifier.ApplyRepairEligibilityGate(report);

        // Replay safety permits repairing from this run, but the symptom is assertion drift, which
        // is never automatically repairable. The permissive half must not win.
        Assert.False(report.Failure.RepairEligible);
        Assert.False(report.Failure.ClassifierRepairEligible);
    }

    [Fact]
    public void RepairEligibilityGate_IsIdempotentForReportsWithoutTheClassifierVerdict()
    {
        // A report produced before the classifier verdict was recorded separately carries only the
        // conjunction. Re-gating it must not promote an unknown verdict to eligible.
        var report = ReportWithClassifiedFailure(repairEligibility: true);
        report.Failure!.ClassifierRepairEligible = null;
        report.Failure.RepairEligible = false;

        MauiFlowFailureClassifier.ApplyRepairEligibilityGate(report);
        MauiFlowFailureClassifier.ApplyRepairEligibilityGate(report);

        Assert.False(report.Failure.RepairEligible);
    }

    private static MauiFlowRepairEligibilityInput PlanlessInput(MauiFlowRunReport run) => new()
    {
        Run = run,
        ReplayEligibility = run.ReplayEligibility,
        ExpectedCheckpoint = Expected(),
        CurrentCheckpoint = Observed(),
    };

    /// <summary>
    /// A run whose only variable is the outcome of the required independent oracle. Everything else
    /// is the drifted-selector shape the repair gate is designed to admit.
    /// </summary>
    private static MauiFlowRunReport RunWithOracle(bool succeeded)
    {
        var run = ReportWithClassifiedFailure(repairEligibility: true);
        run.SideEffectPolicy = MauiFlowSideEffectPolicies.None;
        run.BusinessOracles =
        [
            new MauiIndependentBusinessOracleResult
            {
                OracleId = "todo-ledger-record",
                Independent = true,
                Succeeded = succeeded,
                Message = succeeded
                    ? "The app committed the declared business record to its private storage."
                    : "The app did not commit the declared business record to its private storage.",
            },
        ];
        run.ReplayEligibility!.RepairValidationAllowed = true;
        return run;
    }

    /// <summary>
    /// Supplies every non-oracle prerequisite the gate demands, so a refusal can only come from the
    /// oracle facts.
    /// </summary>
    private static MauiFlowRepairEligibilityInput RepairableInput(MauiFlowRunReport run)
    {
        var fingerprint = new MauiElementFingerprint { FingerprintId = "fp-todo-save" };
        return new MauiFlowRepairEligibilityInput
        {
            Run = run,
            Plan = PlanRequiringTheLedgerOracle(),
            ReplayEligibility = run.ReplayEligibility,
            ExpectedCheckpoint = CompleteCheckpoint(),
            CurrentCheckpoint = CompleteCheckpoint(),
            BeforeDispatch = true,
            IsCurrentLocalRun = true,
            PriorActiveSelectorResolution = new MauiRepairPriorSelectorResolution
            {
                RunId = "run-0",
                TrustedRun = true,
                WasUniquelyResolved = true,
                ActiveSelector = new FlowSelector { AutomationId = "save-todo" },
                Fingerprint = fingerprint,
            },
            TargetFingerprint = fingerprint,
        };
    }

    private static MauiTestPlan PlanRequiringTheLedgerOracle() => new()
    {
        SideEffectPolicy = MauiFlowSideEffectPolicies.None,
        IndependentBusinessOracles =
        [
            new MauiIndependentBusinessOracleDeclaration
            {
                OracleId = "todo-ledger-record",
                Required = true,
                Independent = true,
            },
        ],
    };

    /// <summary>
    /// Every checkpoint fact the gate compares, so the control case is not silently refused for a
    /// missing seed or backend fingerprint instead of for the reason under test.
    /// </summary>
    private static MauiFlowCheckpoint CompleteCheckpoint() => new()
    {
        AppBuildFingerprint = "1.0:1",
        AgentInstanceId = "930a7a61f72fadf3e9e7cebcafd27f39",
        SeedFingerprint = "seed-1",
        BackendStateFingerprint = "backend-1",
        Route = "//native",
        Window = "window-0",
        Modal = "none",
        Locale = "en-US",
        Theme = "Light",
        Orientation = "Portrait",
        DisplayProfile = "1080x2400@2.75",
        CollectionItemKey = "todo-1",
    };

    /// <summary>
    /// `maui devflow flow triage` is given only the manifest and the report, never the plan. It
    /// reported `side-effect-policy-repair-prohibited` and `independent-oracle-absent` for a run
    /// whose own report recorded policy `none` and a succeeded independent oracle, which reads as
    /// two hard defects in a flow that has neither.
    /// </summary>
    [Fact]
    public void PlanlessEvaluation_ReadsPolicyAndOracleFactsFromTheRunReport()
    {
        var run = ReportWithClassifiedFailure(repairEligibility: true);
        run.SideEffectPolicy = MauiFlowSideEffectPolicies.None;
        run.BusinessOracles =
        [
            new MauiIndependentBusinessOracleResult
            {
                OracleId = "todo-ledger-record",
                Independent = true,
                Succeeded = true,
            },
        ];
        run.ReplayEligibility!.RepairValidationAllowed = true;

        var codes = MauiFlowRepairEligibilityEvaluator.Evaluate(PlanlessInput(run))
            .Reasons.Select(static reason => reason.Code).ToList();

        Assert.DoesNotContain("side-effect-policy-repair-prohibited", codes);
        Assert.DoesNotContain("independent-oracle-absent", codes);
    }

    [Fact]
    public void PlanlessEvaluation_StillBlocksWhenTheReportItselfCarriesNoSuchEvidence()
    {
        // The fallback must read evidence, not assume it. A run with no recorded policy and an
        // oracle that did not succeed stays blocked for exactly those two reasons.
        var run = ReportWithClassifiedFailure(repairEligibility: false);
        run.SideEffectPolicy = null;
        run.BusinessOracles =
        [
            new MauiIndependentBusinessOracleResult
            {
                OracleId = "todo-ledger-record",
                Independent = true,
                Succeeded = false,
            },
        ];

        var codes = MauiFlowRepairEligibilityEvaluator.Evaluate(PlanlessInput(run))
            .Reasons.Select(static reason => reason.Code).ToList();

        Assert.Contains("side-effect-policy-repair-prohibited", codes);
        Assert.Contains("independent-oracle-absent", codes);
    }
}
