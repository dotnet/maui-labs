using Microsoft.Maui.DevFlow.Testing;
using Xunit;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Truncation and repair authority disagreed. The serializer's terminal-summary tier discards the
/// business oracles and the replay decision to fit a size budget, but left
/// <c>failure.repairEligible</c> at whatever the run had already concluded — so the emitted report
/// claimed a repair-eligible failure with no replay eligibility to justify it, which its own
/// contract validator rejects. The repair gate meanwhile read that gutted report as if every fact
/// it could not find were genuinely absent. These pin both halves: the gate refuses truncated
/// evidence outright, and the serializer cannot emit a report that contradicts itself.
/// </summary>
public class TruncatedRunReportRepairTests
{
    /// <summary>
    /// A report that satisfies every repair prerequisite before truncation. Without this control a
    /// test that merely observes ineligibility proves nothing, because the same answer comes back
    /// for a missing checkpoint or an unproven prior resolution.
    /// </summary>
    private static MauiFlowRunReport RepairableRun()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "run-truncation",
            FlowId = "checkout-flow",
            FlowDigest = new string('b', 64),
            StartedAt = DateTimeOffset.UnixEpoch,
            EndedAt = DateTimeOffset.UnixEpoch.AddSeconds(5),
            SideEffectPolicy = MauiFlowSideEffectPolicies.None,
            DivergenceStepId = "tap-save",
            Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Failed, Terminal = true },
            ReplayEligibility = new MauiFlowReplayEligibilityDecision
            {
                SideEffectPolicy = MauiFlowSideEffectPolicies.None,
                RepairValidationAllowed = true,
                RepairEligibility = true,
            },
            BusinessOracles =
            [
                new MauiIndependentBusinessOracleResult
                {
                    OracleId = "todo-ledger-record",
                    Independent = true,
                    Succeeded = true,
                },
            ],
            Failure = MauiFlowFailureClassifier.ToFailure(
                MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
                {
                    LegacyFailureKind = FlowFailureKinds.NotFound,
                    BeforeDispatch = true,
                    CheckpointVerified = true,
                    CheckpointMatches = true,
                    RouteMatches = true,
                }),
                "failure-run-truncation",
                FlowFailureKinds.NotFound,
                "tap-save",
                DateTimeOffset.UnixEpoch),
            Steps =
            [
                new MauiFlowStepAttempt
                {
                    StepId = "tap-save",
                    Sequence = 1,
                    Action = FlowActions.Tap,
                    FailureClass = MauiFlowFailureClasses.LocatorNotFound,
                    ExpectedCheckpoint = Checkpoint(),
                    ObservedCheckpoint = Checkpoint(),
                },
            ],
        };
        MauiFlowFailureClassifier.ApplyRepairEligibilityGate(report);
        return report;
    }

    /// <summary>
    /// Pads the report until it cannot fit the JSON budget, so <see cref="MauiFlowRunReportSerializer.ApplyLimits"/>
    /// takes its terminal-summary tier rather than the cheaper first reduction. That tier is the one
    /// that clears the business oracles and the replay decision.
    /// </summary>
    private static void ForceTerminalSummaryTruncation(MauiFlowRunReport report)
    {
        report.Steps = Enumerable.Range(1, 400)
            .Select(index => new MauiFlowStepAttempt
            {
                StepId = index == 400 ? "tap-save" : $"step-{index}",
                Sequence = index,
                Action = new string('a', 256),
                Intent = new string('i', 1024),
                ExpectedCheckpoint = Checkpoint(),
                ObservedCheckpoint = Checkpoint(),
            })
            .ToList();
        MauiFlowRunReportSerializer.ApplyLimits(
            report,
            new MauiFlowRunReportLimits { MaxJsonBytes = 16 * 1024 });
    }

    private static MauiFlowCheckpoint Checkpoint() => new()
    {
        AppBuildFingerprint = "1.0:1",
        AgentInstanceId = "agent-1",
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

    private static MauiElementFingerprint Fingerprint() => new() { FingerprintId = "fp-todo-save" };

    private static MauiTestPlan Plan() => new()
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

    private static MauiFlowRepairEligibilityInput WithPlan(MauiFlowRunReport run) => new()
    {
        Run = run,
        Plan = Plan(),
        ReplayEligibility = run.ReplayEligibility,
        ExpectedCheckpoint = Checkpoint(),
        CurrentCheckpoint = Checkpoint(),
        BeforeDispatch = true,
        IsCurrentLocalRun = true,
        PriorActiveSelectorResolution = new MauiRepairPriorSelectorResolution
        {
            RunId = "run-0",
            TrustedRun = true,
            WasUniquelyResolved = true,
            ActiveSelector = new FlowSelector { AutomationId = "save-todo" },
            Fingerprint = Fingerprint(),
        },
        TargetFingerprint = Fingerprint(),
    };

    /// <summary>
    /// The planless caller is `flow triage`, which is given only the manifest and the report. It
    /// must reach the same refusal, because the plan is not what makes truncated evidence unsafe.
    /// </summary>
    private static MauiFlowRepairEligibilityInput WithoutPlan(MauiFlowRunReport run)
    {
        var input = WithPlan(run);
        input.Plan = null;
        return input;
    }

    [Fact]
    public void UntruncatedRun_IsStillRepairEligible()
    {
        var decision = MauiFlowRepairEligibilityEvaluator.Evaluate(WithPlan(RepairableRun()));

        Assert.True(decision.Eligible);
        Assert.Empty(decision.Reasons);
    }

    [Fact]
    public void TruncatedRun_IsRefusedWithPlan()
    {
        var run = RepairableRun();
        run.Truncated = true;

        var decision = MauiFlowRepairEligibilityEvaluator.Evaluate(WithPlan(run));

        Assert.False(decision.Eligible);
        var reason = Assert.Single(decision.Reasons);
        Assert.Equal("run-evidence-truncated", reason.Code);
        Assert.True(reason.Blocking);
    }

    [Fact]
    public void TruncatedRun_IsRefusedWithoutPlan()
    {
        var run = RepairableRun();
        run.Truncated = true;

        var decision = MauiFlowRepairEligibilityEvaluator.Evaluate(WithoutPlan(run));

        Assert.False(decision.Eligible);
        Assert.Contains(
            "run-evidence-truncated",
            decision.Reasons.Select(static reason => reason.Code));
    }

    [Fact]
    public void TerminalSummaryTruncation_ClearsFailureRepairEligibilityAndStaysValid()
    {
        var report = RepairableRun();
        Assert.True(report.Failure!.RepairEligible);

        ForceTerminalSummaryTruncation(report);

        Assert.True(report.Truncated);
        Assert.False(report.ReplayEligibility!.RepairEligibility);
        Assert.False(report.ReplayEligibility.RepairValidationAllowed);
        Assert.Empty(report.BusinessOracles);
        Assert.False(report.Failure.RepairEligible);
        // The classifier's own verdict is a separate fact and must survive the narrowing, or a
        // reader cannot tell a suppressed repair from a symptom that was never repairable.
        Assert.True(report.Failure.ClassifierRepairEligible);

        var validation = MauiFlowRunReportSerializer.Validate(report);
        Assert.True(
            validation.IsValid,
            validation.Errors.Count == 0 ? "" : string.Join("; ", validation.Errors));
    }

    /// <summary>
    /// The same stale-conjunction defect, on the other invariant the discarded replay decision
    /// carried. A verified passing run that reaches the terminal-summary tier used to emit a report
    /// claiming <c>verified: true</c> with no <c>runVerificationAllowed</c> to support it, which its
    /// own validator rejects — and a truncated pass is far more common than a truncated repairable
    /// failure, so it is the shape most likely to reach a consumer.
    /// </summary>
    [Fact]
    public void TerminalSummaryTruncation_KeepsAVerifiedPassingRunValid()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "run-verified-truncation",
            FlowId = "checkout-flow",
            FlowDigest = new string('b', 64),
            StartedAt = DateTimeOffset.UnixEpoch,
            EndedAt = DateTimeOffset.UnixEpoch.AddSeconds(5),
            SideEffectPolicy = MauiFlowSideEffectPolicies.None,
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.Passed,
                Terminal = true,
                Verified = true,
            },
            Verification = new MauiFlowRunVerification { Verified = true },
            ReplayEligibility = new MauiFlowReplayEligibilityDecision
            {
                SideEffectPolicy = MauiFlowSideEffectPolicies.None,
                RunVerificationAllowed = true,
                OrdinaryReplayAllowed = true,
            },
        };
        ForceTerminalSummaryTruncation(report);

        Assert.True(report.Truncated);
        Assert.Single(report.Steps);
        Assert.True(report.Outcome!.Verified);
        Assert.True(report.ReplayEligibility!.RunVerificationAllowed);
        Assert.Equal(MauiFlowSideEffectPolicies.None, report.ReplayEligibility.SideEffectPolicy);

        var validation = MauiFlowRunReportSerializer.Validate(report);
        Assert.True(
            validation.IsValid,
            validation.Errors.Count == 0 ? "" : string.Join("; ", validation.Errors));
    }

    /// <summary>
    /// A cardinality trim sets the same <c>truncated</c> flag as the size reductions, and it is
    /// reachable on any long flow because the runner emits two events per step against a 128-event
    /// cap. Refusing it too is deliberate: <c>truncated</c> is the report's own declaration that it
    /// is not complete, and a selector repair rewrites a committed test, so this gate treats
    /// self-declared incompleteness as disqualifying rather than trying to rank which omissions are
    /// harmless. The cost is a lost convenience on long flows, which still get full selector
    /// evidence to inspect by hand; the alternative is admitting a repair on the strength of facts
    /// the report never claimed to carry.
    /// </summary>
    [Fact]
    public void EventOnlyTrim_IsAlsoRefused()
    {
        var run = RepairableRun();
        run.Events = Enumerable.Range(1, 4)
            .Select(index => new MauiFlowRunEvent
            {
                Sequence = index,
                Kind = "step-started",
                Message = "step",
            })
            .ToList();
        MauiFlowRunReportSerializer.ApplyLimits(run, new MauiFlowRunReportLimits { MaxEvents = 2 });

        Assert.True(run.Truncated);
        Assert.Contains(run.Omissions, omission => omission.Kind == "events");
        Assert.Contains(
            "run-evidence-truncated",
            MauiFlowRepairEligibilityEvaluator.Evaluate(WithPlan(run))
                .Reasons.Select(static reason => reason.Code));
    }

    [Fact]
    public void TerminalSummaryTruncation_ProducesAReportTheGateRefuses()
    {
        var report = RepairableRun();
        ForceTerminalSummaryTruncation(report);

        var withPlan = MauiFlowRepairEligibilityEvaluator.Evaluate(WithPlan(report));
        var withoutPlan = MauiFlowRepairEligibilityEvaluator.Evaluate(WithoutPlan(report));

        Assert.False(withPlan.Eligible);
        Assert.False(withoutPlan.Eligible);
        Assert.Contains(
            "run-evidence-truncated",
            withPlan.Reasons.Select(static reason => reason.Code));
        Assert.Contains(
            "run-evidence-truncated",
            withoutPlan.Reasons.Select(static reason => reason.Code));
    }

    [Fact]
    public void TruncatedRun_TriageDoesNotAdvertiseSelectorRepair()
    {
        var report = RepairableRun();
        ForceTerminalSummaryTruncation(report);

        var triage = MauiFlowTriageAnalyzer.Analyze(new MauiFlowTriageInput
        {
            Report = report,
            Plan = Plan(),
            ExpectedCheckpoint = Checkpoint(),
            CurrentCheckpoint = Checkpoint(),
            BeforeDispatch = true,
            IsCurrentLocalRun = true,
            PriorActiveSelectorResolution = new MauiRepairPriorSelectorResolution
            {
                RunId = "run-0",
                TrustedRun = true,
                WasUniquelyResolved = true,
                ActiveSelector = new FlowSelector { AutomationId = "save-todo" },
                Fingerprint = Fingerprint(),
            },
            TargetFingerprint = Fingerprint(),
        });

        Assert.False(triage.RepairEligible);
        Assert.Contains("run-evidence-truncated", triage.RepairEligibilityCodes);
        Assert.DoesNotContain(
            MauiFlowTriageNextActions.ReviewSelectorRepair,
            triage.AllowedNextActions);
    }

    [Fact]
    public void TruncatedRun_CannotGenerateARepairProposal()
    {
        var report = RepairableRun();
        report.Truncated = true;
        var eligibility = MauiFlowRepairEligibilityEvaluator.Evaluate(WithPlan(report));

        var generation = MauiFlowRepairProposalGenerator.Generate(new MauiFlowRepairProposalGenerationInput
        {
            Eligibility = eligibility,
            Plan = Plan(),
            SourceRunId = report.RunId,
            SourceStepId = "tap-save",
            SourceFailureCode = MauiFlowFailureClasses.LocatorNotFound,
            Flow = new MauiFlow
            {
                Name = "checkout-flow",
                Steps =
                [
                    new FlowStep
                    {
                        Seq = 1,
                        StepId = "tap-save",
                        Action = FlowActions.Tap,
                        Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "save-todo" } },
                    },
                ],
            },
            BaseFlow = new MauiFlowReference
            {
                Path = "flows/checkout-flow.md",
                Digest = new string('a', 64),
            },
            PriorFingerprint = Fingerprint(),
            PriorActiveSelectorResolution = new MauiRepairPriorSelectorResolution
            {
                RunId = "run-0",
                TrustedRun = true,
                WasUniquelyResolved = true,
                ActiveSelector = new FlowSelector { AutomationId = "save-todo" },
                Fingerprint = Fingerprint(),
            },
            Trust = "current-local-run",
        });

        Assert.True(generation.Abstained);
        Assert.Empty(generation.Proposals);
        Assert.Contains(
            "repair-ineligible",
            generation.Abstentions.Select(static abstention => abstention.Code));
    }
}
