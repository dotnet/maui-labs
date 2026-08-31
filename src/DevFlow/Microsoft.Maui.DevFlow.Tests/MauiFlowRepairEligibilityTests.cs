using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class MauiFlowRepairEligibilityTests
{
    [Fact]
    public void SelectorMaterial_UnscopedSelector_PreservesLegacyDigestInput()
    {
        var selector = new FlowSelector { AutomationId = "SaveButton" };

        var material = MauiFlowRepairPatchBuilder.SelectorMaterial(selector);

        Assert.Equal(string.Join("\u001f", "SaveButton", null, null, null, null), material);
    }

    [Fact]
    public void Evaluate_PreDispatchUniqueLocalLocatorDrift_AllowsRepair()
    {
        var checkpoint = Checkpoint();
        var fingerprint = Fingerprint();
        var report = new MauiFlowRunReport
        {
            RunId = "run-local",
            Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Failed, Terminal = true },
            Failure = new MauiFlowFailure
            {
                FailureId = "failure-1",
                Code = MauiFlowFailureClasses.LocatorNotFound,
                Class = MauiFlowFailureClasses.LocatorNotFound,
                Phase = "resolution",
                StepId = "1",
            },
            Steps =
            [
                new MauiFlowStepAttempt
                {
                    StepId = "1",
                    ExpectedCheckpoint = checkpoint,
                    ObservedCheckpoint = checkpoint,
                    Fingerprint = fingerprint,
                },
            ],
        };

        var decision = MauiFlowRepairEligibilityEvaluator.Evaluate(new MauiFlowRepairEligibilityInput
        {
            Run = report,
            Plan = RepairablePlan(),
            ExpectedCheckpoint = checkpoint,
            CurrentCheckpoint = checkpoint,
            BeforeDispatch = true,
            IsCurrentLocalRun = true,
            PriorActiveSelectorResolution = new MauiRepairPriorSelectorResolution
            {
                RunId = "run-trusted",
                TrustedRun = true,
                WasUniquelyResolved = true,
                ActiveSelector = new FlowSelector { AutomationId = "old-save" },
                Fingerprint = fingerprint,
            },
            TargetFingerprint = fingerprint,
        });

        Assert.True(decision.Eligible);
        Assert.Empty(decision.Reasons);
    }

    [Fact]
    public void Evaluate_PreDispatchLocatorAmbiguous_NeverAllowsRepair()
    {
        var checkpoint = Checkpoint();
        var fingerprint = Fingerprint();
        var report = new MauiFlowRunReport
        {
            RunId = "run-local",
            Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Failed, Terminal = true },
            Failure = new MauiFlowFailure
            {
                FailureId = "failure-ambiguous",
                Code = MauiFlowFailureClasses.LocatorAmbiguous,
                Class = MauiFlowFailureClasses.LocatorAmbiguous,
                Phase = "resolution",
                StepId = "1",
            },
            Steps =
            [
                new MauiFlowStepAttempt
                {
                    StepId = "1",
                    ExpectedCheckpoint = checkpoint,
                    ObservedCheckpoint = checkpoint,
                    Fingerprint = fingerprint,
                    FailureClass = MauiFlowFailureClasses.LocatorAmbiguous,
                },
            ],
        };

        var decision = MauiFlowRepairEligibilityEvaluator.Evaluate(new MauiFlowRepairEligibilityInput
        {
            Run = report,
            Plan = RepairablePlan(),
            ExpectedCheckpoint = checkpoint,
            CurrentCheckpoint = checkpoint,
            BeforeDispatch = true,
            IsCurrentLocalRun = true,
            PriorActiveSelectorResolution = Prior(fingerprint),
            TargetFingerprint = fingerprint,
        });

        Assert.False(decision.Eligible);
        Assert.Contains(decision.Reasons, reason => reason.Code == "failure-not-locator-not-found");
        Assert.Contains(decision.Reasons, reason => reason.Code == "blocking-failure-locator-ambiguous");
    }

    [Fact]
    public void Evaluate_CheckpointTrustAndAssertionFailure_ReturnsEveryExplicitReason()
    {
        var expected = Checkpoint();
        var current = Checkpoint();
        current.Theme = "dark";
        var decision = MauiFlowRepairEligibilityEvaluator.Evaluate(new MauiFlowRepairEligibilityInput
        {
            Run = new MauiFlowRunReport
            {
                Failure = new MauiFlowFailure
                {
                    Code = MauiFlowFailureClasses.LocatorNotFound,
                    Class = MauiFlowFailureClasses.LocatorNotFound,
                    Phase = "resolution",
                    StepId = "1",
                },
                Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Failed },
                Steps =
                [
                    new MauiFlowStepAttempt
                    {
                        StepId = "1",
                        ExpectedCheckpoint = expected,
                        ObservedCheckpoint = current,
                        FailureClass = MauiFlowFailureClasses.AssertionFailed,
                    },
                ],
            },
            Plan = new MauiTestPlan { SideEffectPolicy = MauiFlowSideEffectPolicies.NonReplayable },
            ExpectedCheckpoint = expected,
            CurrentCheckpoint = current,
            BeforeDispatch = false,
            ArtifactTrust = MauiArtifactTrustStates.Attested,
            PriorActiveSelectorResolution = new MauiRepairPriorSelectorResolution(),
            TargetFingerprint = new MauiElementFingerprint
            {
                Collection = new MauiCollectionIdentity { Virtualized = true },
            },
        });

        Assert.False(decision.Eligible);
        Assert.Contains(decision.Reasons, reason => reason.Code == "failure-not-pre-dispatch");
        Assert.Contains(decision.Reasons, reason => reason.Code == "checkpoint-theme-mismatch");
        Assert.Contains(decision.Reasons, reason => reason.Code == "artifact-not-locally-reproduced");
        Assert.Contains(decision.Reasons, reason => reason.Code == "blocking-failure-assertion-failed");
        Assert.Contains(decision.Reasons, reason => reason.Code == "side-effect-policy-repair-prohibited");
        Assert.Contains(decision.Reasons, reason => reason.Code == "independent-oracle-absent");
        Assert.Contains(decision.Reasons, reason => reason.Code == "prior-selector-not-unique");
        Assert.Contains(decision.Reasons, reason => reason.Code == "target-virtualized-unscoped");
    }

    [Fact]
    public void Generate_TrustedUniqueAutomationId_ProducesSelectorOnlyPatch()
    {
        var fingerprint = Fingerprint();
        var flow = new MauiFlow
        {
            Name = "repair",
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    StepId = "save-order",
                    Action = FlowActions.Tap,
                    Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "old-save" } },
                    Value = "unchanged",
                    Asserts =
                    [
                        new FlowAssert
                        {
                            Kind = "exists",
                            Verify = true,
                            Selector = new FlowSelector { AutomationId = "old-save" },
                        },
                    ],
                },
            ],
        };
        var candidate = Candidate("new-save", 0.91, fingerprint);
        var eligibility = new MauiFlowRepairEligibilityDecision { Eligible = true, FailureCode = MauiFlowFailureClasses.LocatorNotFound };

        var result = MauiFlowRepairProposalGenerator.Generate(new MauiFlowRepairProposalGenerationInput
        {
            Eligibility = eligibility,
            Flow = flow,
            BaseFlow = new MauiFlowReference { Path = "repair.md", Digest = new string('a', 64) },
            SourceRunId = "run-local",
            SourceStepId = "1",
            SourceFailureId = "failure-1",
            SourceFailureCode = MauiFlowFailureClasses.LocatorNotFound,
            PriorFingerprint = fingerprint,
            PriorActiveSelectorResolution = Prior(fingerprint),
            SelectorHealthCandidates = [candidate],
            CurrentResolutions =
            [
                new MauiRepairCandidateResolution
                {
                    CandidateId = candidate.CandidateId,
                    MatchCount = 1,
                    SemanticFingerprintMatches = true,
                    CurrentFingerprint = fingerprint,
                    EvidenceRefs = ["live:resolution"],
                },
            ],
            Trust = "current-local-run",
        });

        var proposal = Assert.Single(result.Proposals);
        Assert.Equal("old-save", proposal.OldSelector!.AutomationId);
        Assert.Equal("new-save", proposal.ProposedSelector!.AutomationId);
        Assert.True(proposal.Patch!.SelectorOnly);
        Assert.True(proposal.UnchangedAssertionsProof!.Unchanged);
        Assert.True(proposal.UnchangedAssertionsProof.ActionsUnchanged);
        Assert.True(proposal.UnchangedAssertionsProof.ValuesUnchanged);
        Assert.True(proposal.UnchangedAssertionsProof.OrderUnchanged);
        Assert.Contains("live:resolution", proposal.Candidate!.EvidenceRefs);
        Assert.Equal(MauiSelectorHealthRules.Uncalibrated, proposal.Candidate.CalibrationStatus);
        Assert.Equal("save-order", proposal.SourceStepId);
        Assert.Equal("save-order", proposal.Diff!.StepId);
    }

    [Fact]
    public void Build_IntegerStepSequence_CannotBeOverriddenByNumericStableId()
    {
        var flow = new MauiFlow
        {
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    StepId = "first-step",
                    Action = FlowActions.Tap,
                    Target = new FlowSelector { AutomationId = "first-old" },
                },
                new FlowStep
                {
                    Seq = 2,
                    StepId = "1",
                    Action = FlowActions.Tap,
                    Target = new FlowSelector { AutomationId = "second-old" },
                },
            ],
        };

        var result = MauiFlowRepairPatchBuilder.Build(
            flow,
            stepSequence: 1,
            proposedSelector: new FlowSelector { AutomationId = "first-new" });

        Assert.True(result.Ok, result.Error);
        Assert.Equal("first-new", result.PatchedFlow!.Steps[0].Target!.AutomationId);
        Assert.Equal("second-old", result.PatchedFlow.Steps[1].Target!.AutomationId);
        Assert.Equal("first-step", result.Diff!.StepId);
    }

    [Fact]
    public void Generate_CloseCandidates_AbstainsWithoutChangingTheFlow()
    {
        var fingerprint = Fingerprint();
        var flow = new MauiFlow
        {
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Target = new FlowSelector { AutomationId = "old-save" },
                },
            ],
        };
        var first = Candidate("new-save-one", 0.91, fingerprint, rank: 1);
        var second = Candidate("new-save-two", 0.90, fingerprint, rank: 2);

        var result = MauiFlowRepairProposalGenerator.Generate(new MauiFlowRepairProposalGenerationInput
        {
            Eligibility = new MauiFlowRepairEligibilityDecision { Eligible = true },
            Flow = flow,
            BaseFlow = new MauiFlowReference { Path = "repair.md", Digest = new string('a', 64) },
            SourceRunId = "run-local",
            SourceStepId = "1",
            SourceFailureCode = MauiFlowFailureClasses.LocatorNotFound,
            PriorFingerprint = fingerprint,
            PriorActiveSelectorResolution = Prior(fingerprint),
            SelectorHealthCandidates = [first, second],
            CurrentResolutions =
            [
                Resolution(first, fingerprint),
                Resolution(second, fingerprint),
            ],
        });

        Assert.Empty(result.Proposals);
        Assert.Contains(result.Abstentions, item => item.Code == "candidate-scores-too-close");
        Assert.Equal("old-save", flow.Steps[0].Target!.AutomationId);
    }

    [Fact]
    public void Generate_DuplicateCurrentCandidateResolution_AbstainsWithoutSelectingByOrder()
    {
        var fingerprint = Fingerprint();
        var candidate = Candidate("new-save", .9, fingerprint);
        var result = MauiFlowRepairProposalGenerator.Generate(new MauiFlowRepairProposalGenerationInput
        {
            Eligibility = new MauiFlowRepairEligibilityDecision { Eligible = true },
            Flow = new MauiFlow
            {
                Steps =
                [
                    new FlowStep
                    {
                        Seq = 1,
                        Action = FlowActions.Tap,
                        Target = new FlowSelector { AutomationId = "old-save" },
                    },
                ],
            },
            BaseFlow = new MauiFlowReference { Path = "repair.md", Digest = new string('a', 64) },
            SourceRunId = "run-local",
            SourceStepId = "1",
            SourceFailureCode = MauiFlowFailureClasses.LocatorNotFound,
            PriorFingerprint = fingerprint,
            PriorActiveSelectorResolution = Prior(fingerprint),
            SelectorHealthCandidates = [candidate],
            CurrentResolutions =
            [
                Resolution(candidate, fingerprint),
                Resolution(candidate, fingerprint),
            ],
        });

        Assert.Empty(result.Proposals);
        Assert.Contains(result.Abstentions, item =>
            item.Code == "candidate-current-resolution-duplicate" &&
            item.CandidateId == candidate.CandidateId);
    }

    [Fact]
    public void Generate_PlanRepairPolicy_NarrowsAllowedCandidateKinds()
    {
        var fingerprint = Fingerprint();
        var candidate = Candidate("new-save", .9, fingerprint);
        var result = MauiFlowRepairProposalGenerator.Generate(new MauiFlowRepairProposalGenerationInput
        {
            Eligibility = new MauiFlowRepairEligibilityDecision { Eligible = true },
            Plan = new MauiTestPlan
            {
                RepairPolicy = new MauiFlowRepairPolicy
                {
                    AllowedCandidateKinds = ["exact-text"],
                    AllowedRiskFlags = ["localization"],
                },
            },
            Flow = new MauiFlow
            {
                Steps =
                [
                    new FlowStep
                    {
                        Seq = 1,
                        Action = FlowActions.Tap,
                        Target = new FlowSelector { AutomationId = "old-save" },
                    },
                ],
            },
            BaseFlow = new MauiFlowReference { Path = "repair.md", Digest = new string('a', 64) },
            SourceRunId = "run-local",
            SourceStepId = "1",
            SourceFailureCode = MauiFlowFailureClasses.LocatorNotFound,
            PriorFingerprint = fingerprint,
            PriorActiveSelectorResolution = Prior(fingerprint),
            SelectorHealthCandidates = [candidate],
            CurrentResolutions = [Resolution(candidate, fingerprint)],
        });

        Assert.Empty(result.Proposals);
        Assert.Contains(result.Abstentions, item => item.Code == "candidate-kind-disallowed");
    }

    private static MauiRepairCandidateResolution Resolution(MauiSelectorCandidate candidate, MauiElementFingerprint fingerprint)
        => new()
        {
            CandidateId = candidate.CandidateId,
            MatchCount = 1,
            SemanticFingerprintMatches = true,
            CurrentFingerprint = fingerprint,
        };

    private static MauiRepairPriorSelectorResolution Prior(MauiElementFingerprint fingerprint) => new()
    {
        RunId = "prior-run",
        TrustedRun = true,
        WasUniquelyResolved = true,
        ActiveSelector = new FlowSelector { AutomationId = "old-save" },
        Fingerprint = fingerprint,
    };

    private static MauiSelectorCandidate Candidate(
        string automationId,
        double score,
        MauiElementFingerprint fingerprint,
        int rank = 1)
        => new()
        {
            CandidateId = "candidate-" + automationId,
            Rank = rank,
            Priority = rank,
            Selector = new FlowSelector { AutomationId = automationId },
            SelectorDescriptor = new MauiSelectorCandidateSelector
            {
                Kind = "automation-id",
                AutomationId = automationId,
            },
            Score = score,
            Scores = new MauiSelectorCandidateScores { DeterministicRankScore = score },
            Unique = true,
            Validation = new MauiSelectorCandidateValidation
            {
                Unique = true,
                MatchCount = 1,
                Accepted = true,
                PlatformState = "validated",
            },
            CalibrationStatus = MauiSelectorHealthRules.Uncalibrated,
            Calibration = new MauiSelectorCandidateCalibration(),
            Fingerprint = fingerprint,
        };

    private static MauiTestPlan RepairablePlan() => new()
    {
        SideEffectPolicy = MauiFlowSideEffectPolicies.None,
        IndependentBusinessOracles =
        [
            new MauiIndependentBusinessOracleDeclaration
            {
                OracleId = "order-saved",
                Required = true,
                Independent = true,
            },
        ],
    };

    private static MauiFlowCheckpoint Checkpoint() => new()
    {
        AppBuildFingerprint = "build-1",
        AgentInstanceId = "agent-1",
        SeedFingerprint = "seed-1",
        BackendStateFingerprint = "backend-1",
        Route = "/checkout",
        Window = "main",
        Modal = "none",
        Locale = "en-US",
        Theme = "light",
        Orientation = "portrait",
        DisplayProfile = "320x640@2",
        CollectionItemKey = "order-42",
    };

    private static MauiElementFingerprint Fingerprint() => new()
    {
        FingerprintId = "fp-test",
        Context = new MauiElementFingerprintContext
        {
            AppId = "com.example.app",
            AppBuild = "build-1",
            Platform = "android",
            Route = "/checkout",
            Window = "main",
            Modal = "none",
            Locale = "en-US",
            Theme = "light",
            Orientation = "portrait",
            DisplayProfile = "320x640@2",
        },
        Managed = new MauiManagedElementIdentity
        {
            Type = "Button",
            FullType = "Microsoft.Maui.Controls.Button",
            Role = "button",
            Traits = ["interactive"],
        },
        Topology = new MauiTopologySignature
        {
            AncestorHash = "ancestor",
            SiblingHash = "sibling",
        },
    };
}
