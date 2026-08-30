using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class BrokerWorkflowRepairValidationHostTests
{
    [Fact]
    public async Task HardResetAsync_WithoutALifecycleAttester_FailsClosedInsteadOfEchoingTheRequest()
    {
        var flow = Flow();
        var proposal = Proposal(flow);
        var host = new BrokerWorkflowRepairValidationHost(
            observeCheckpoint: _ => Task.FromResult<MauiFlowCheckpoint?>(Checkpoint()),
            restoreRoute: (_, _) => Task.FromResult(true),
            replay: (_, _, _, _) => Task.FromResult<WorkflowRepairTransientReplayOutcome?>(null),
            resetAttester: null);

        var reset = await host.HardResetAsync(Request(proposal, flow), CancellationToken.None);

        Assert.False(reset.Succeeded);
        Assert.Equal("repair-reset-attester-unavailable", reset.FailureCode);
        Assert.Null(reset.ObservedCheckpoint);
    }

    [Fact]
    public async Task HardResetAsync_MergesAttestedAndObservedFactsIntoAllTwelveCheckpointFields()
    {
        var flow = Flow();
        var proposal = Proposal(flow);
        var classified = Checkpoint();
        var routes = new List<string>();
        var host = new BrokerWorkflowRepairValidationHost(
            observeCheckpoint: _ => Task.FromResult<MauiFlowCheckpoint?>(new MauiFlowCheckpoint
            {
                AppBuildFingerprint = classified.AppBuildFingerprint,
                AgentInstanceId = classified.AgentInstanceId,
                Route = classified.Route,
                Window = classified.Window,
                Modal = classified.Modal,
                Locale = classified.Locale,
                Theme = classified.Theme,
                Orientation = classified.Orientation,
                DisplayProfile = classified.DisplayProfile,
            }),
            restoreRoute: (route, _) =>
            {
                routes.Add(route);
                return Task.FromResult(true);
            },
            replay: (_, _, _, _) => Task.FromResult<WorkflowRepairTransientReplayOutcome?>(null),
            resetAttester: new FakeResetAttester(classified));

        var reset = await host.HardResetAsync(Request(proposal, flow), CancellationToken.None);

        Assert.True(reset.Succeeded);
        Assert.Equal("//home", Assert.Single(routes));
        var observed = Assert.IsType<MauiFlowCheckpoint>(reset.ObservedCheckpoint);
        Assert.Equal(classified.AppBuildFingerprint, observed.AppBuildFingerprint);
        Assert.Equal(classified.AgentInstanceId, observed.AgentInstanceId);
        Assert.Equal(classified.SeedFingerprint, observed.SeedFingerprint);
        Assert.Equal(classified.BackendStateFingerprint, observed.BackendStateFingerprint);
        Assert.Equal(classified.Route, observed.Route);
        Assert.Equal(classified.Window, observed.Window);
        Assert.Equal(classified.Modal, observed.Modal);
        Assert.Equal(classified.Locale, observed.Locale);
        Assert.Equal(classified.Theme, observed.Theme);
        Assert.Equal(classified.Orientation, observed.Orientation);
        Assert.Equal(classified.DisplayProfile, observed.DisplayProfile);
        Assert.Equal(classified.CollectionItemKey, observed.CollectionItemKey);

        // The service's own 12-field comparison is the contract this host must satisfy.
        var record = await new WorkflowRepairValidationService(
                new LifecycleOnlyHost(host))
            .ValidateAsync(Request(proposal, flow), CancellationToken.None);
        Assert.DoesNotContain("post-reset-checkpoint-mismatch", record.FailureFacts);
        Assert.DoesNotContain("hard-reset-failed", record.FailureFacts);
    }

    [Fact]
    public async Task HardResetAsync_WhenAnAttestedFactIsMissing_FailsTheServiceCheckpointComparison()
    {
        var flow = Flow();
        var proposal = Proposal(flow);
        var classified = Checkpoint();
        var host = new BrokerWorkflowRepairValidationHost(
            observeCheckpoint: _ => Task.FromResult<MauiFlowCheckpoint?>(classified),
            restoreRoute: (_, _) => Task.FromResult(true),
            replay: (_, _, _, _) => Task.FromResult<WorkflowRepairTransientReplayOutcome?>(null),
            resetAttester: new FakeResetAttester(classified) { SeedFingerprint = null });

        var record = await new WorkflowRepairValidationService(new LifecycleOnlyHost(host))
            .ValidateAsync(Request(proposal, flow), CancellationToken.None);

        Assert.False(record.Passed);
        Assert.Contains("post-reset-checkpoint-mismatch", record.FailureFacts);
    }

    [Fact]
    public async Task ReplayAsync_RunsAClonedFlowAndNeverMutatesOrPersistsTheSourceFlow()
    {
        var flow = Flow();
        var proposal = Proposal(flow);
        var sourceDigestBefore = MauiFlowRunReportSerializer.ComputeFlowDigest(flow);
        var sourceJsonBefore = JsonSerializer.Serialize(flow, MauiFlowJsonContext.Default.MauiFlow);
        MauiFlow? replayed = null;
        var host = new BrokerWorkflowRepairValidationHost(
            observeCheckpoint: _ => Task.FromResult<MauiFlowCheckpoint?>(Checkpoint()),
            restoreRoute: (_, _) => Task.FromResult(true),
            replay: (transient, _, _, _) =>
            {
                replayed = transient;
                return Task.FromResult<WorkflowRepairTransientReplayOutcome?>(
                    new WorkflowRepairTransientReplayOutcome
                    {
                        RunId = "transient-run",
                        Report = PassingReport(proposal),
                    });
            },
            resetAttester: new FakeResetAttester(Checkpoint()));

        await host.HardResetAsync(Request(proposal, flow), CancellationToken.None);
        var replay = await host.ReplayWithInMemorySelectorOverrideAsync(
            Request(proposal, flow),
            CancellationToken.None);

        Assert.True(replay.ReachedFailedStep);
        Assert.Equal(1, replay.CandidateMatchCount);
        Assert.NotNull(replayed);
        Assert.NotSame(flow, replayed);

        // The clone carries the proposed selector; the workspace flow keeps the drifted one.
        Assert.Equal("new-save", replayed!.Steps[0].Args!.Selector!.AutomationId);
        Assert.Equal("old-save", flow.Steps[0].Args!.Selector!.AutomationId);
        Assert.Equal(sourceDigestBefore, MauiFlowRunReportSerializer.ComputeFlowDigest(flow));
        Assert.Equal(
            sourceJsonBefore,
            JsonSerializer.Serialize(flow, MauiFlowJsonContext.Default.MauiFlow));
    }

    [Fact]
    public async Task ReplayAsync_WhenTheSourceFlowDigestDoesNotMatchTheProposal_FailsClosed()
    {
        var flow = Flow();
        var proposal = Proposal(flow);
        var drifted = Flow();
        drifted.Steps[0].Args!.Selector = new FlowSelector { AutomationId = "drifted-save" };
        var replayCalls = 0;
        var host = new BrokerWorkflowRepairValidationHost(
            observeCheckpoint: _ => Task.FromResult<MauiFlowCheckpoint?>(Checkpoint()),
            restoreRoute: (_, _) => Task.FromResult(true),
            replay: (_, _, _, _) =>
            {
                replayCalls++;
                return Task.FromResult<WorkflowRepairTransientReplayOutcome?>(null);
            },
            resetAttester: new FakeResetAttester(Checkpoint()));

        var replay = await host.ReplayWithInMemorySelectorOverrideAsync(
            Request(proposal, drifted),
            CancellationToken.None);

        Assert.False(replay.Passed);
        Assert.Equal("repair-source-flow-digest-mismatch", replay.FailureCode);
        Assert.Equal(0, replayCalls);
    }

    [Fact]
    public async Task ReplayAsync_AmbiguousCandidateResolution_FailsValidationWithoutApplying()
    {
        var flow = Flow();
        var proposal = Proposal(flow);
        var report = PassingReport(proposal);
        report.Steps[0].TargetResolution!.MatchCount = 2;
        var host = new BrokerWorkflowRepairValidationHost(
            observeCheckpoint: _ => Task.FromResult<MauiFlowCheckpoint?>(Checkpoint()),
            restoreRoute: (_, _) => Task.FromResult(true),
            replay: (_, _, _, _) => Task.FromResult<WorkflowRepairTransientReplayOutcome?>(
                new WorkflowRepairTransientReplayOutcome { RunId = "ambiguous-run", Report = report }),
            resetAttester: new FakeResetAttester(Checkpoint()));

        await host.HardResetAsync(Request(proposal, flow), CancellationToken.None);
        var replay = await host.ReplayWithInMemorySelectorOverrideAsync(
            Request(proposal, flow),
            CancellationToken.None);
        Assert.Equal(2, replay.CandidateMatchCount);
        Assert.False(replay.Passed);

        var record = await new WorkflowRepairValidationService(host)
            .ValidateAsync(Request(proposal, flow), CancellationToken.None);
        Assert.False(record.Passed);
        Assert.Contains("candidate-not-uniquely-resolved", record.FailureFacts);
    }

    [Fact]
    public async Task ReplayAsync_TruncatesDownstreamStepsWhenContinuationIsNotAllowed()
    {
        var flow = Flow();
        flow.Steps.Add(new FlowStep
        {
            Seq = 2,
            StepId = "downstream-step",
            Action = FlowActions.Tap,
            Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "confirm" } },
        });
        var proposal = Proposal(flow);
        MauiFlow? replayed = null;
        var host = new BrokerWorkflowRepairValidationHost(
            observeCheckpoint: _ => Task.FromResult<MauiFlowCheckpoint?>(Checkpoint()),
            restoreRoute: (_, _) => Task.FromResult(true),
            replay: (transient, _, _, _) =>
            {
                replayed = transient;
                return Task.FromResult<WorkflowRepairTransientReplayOutcome?>(
                    new WorkflowRepairTransientReplayOutcome
                    {
                        RunId = "bounded-run",
                        Report = PassingReport(proposal),
                    });
            },
            resetAttester: new FakeResetAttester(Checkpoint()));

        await host.HardResetAsync(Request(proposal, flow), CancellationToken.None);
        var replay = await host.ReplayWithInMemorySelectorOverrideAsync(
            Request(proposal, flow),
            CancellationToken.None);

        Assert.False(replay.ContinuedDownstream);
        Assert.Equal("stable-save-step", Assert.Single(replayed!.Steps).StepId);
        Assert.Equal(2, flow.Steps.Count);
    }

    [Fact]
    public async Task ReplayAsync_WithoutTheHostsOwnPairedResetAttestation_FailsClosed()
    {
        var flow = Flow();
        var proposal = Proposal(flow);
        var replayCalls = 0;
        var host = new BrokerWorkflowRepairValidationHost(
            observeCheckpoint: _ => Task.FromResult<MauiFlowCheckpoint?>(Checkpoint()),
            restoreRoute: (_, _) => Task.FromResult(true),
            replay: (_, _, _, _) =>
            {
                replayCalls++;
                return Task.FromResult<WorkflowRepairTransientReplayOutcome?>(null);
            },
            resetAttester: new FakeResetAttester(Checkpoint()));

        var replay = await host.ReplayWithInMemorySelectorOverrideAsync(
            Request(proposal, flow),
            CancellationToken.None);

        Assert.False(replay.Passed);
        Assert.Equal("repair-reset-attestation-required", replay.FailureCode);
        Assert.Equal(0, replayCalls);
    }

    [Fact]
    public async Task ReplayAsync_AdmitsTheTransientRunAgainstTheAttestedResetAndReportsAPass()
    {
        var flow = Flow();
        var proposal = Proposal(flow);
        var classified = Checkpoint();
        MauiFlowRunContext? admitted = null;
        var host = new BrokerWorkflowRepairValidationHost(
            observeCheckpoint: _ => Task.FromResult<MauiFlowCheckpoint?>(Checkpoint()),
            restoreRoute: (_, _) => Task.FromResult(true),
            replay: (_, _, context, _) =>
            {
                admitted = context;
                return Task.FromResult<WorkflowRepairTransientReplayOutcome?>(
                    new WorkflowRepairTransientReplayOutcome
                    {
                        RunId = "verified-run",
                        Report = PassingReport(proposal),
                    });
            },
            resetAttester: new FakeResetAttester(classified));

        var record = await new WorkflowRepairValidationService(host)
            .ValidateAsync(Request(proposal, flow), CancellationToken.None);

        Assert.True(record.Passed);
        Assert.Empty(record.FailureFacts);
        Assert.NotNull(admitted);
        Assert.Equal(MauiFlowReplayIntents.RepairValidation, admitted!.Intent);
        // The run is admitted against what the host itself reset and observed, not the request.
        Assert.Equal(classified.SeedFingerprint, admitted.Reset?.SeedFingerprint);
        Assert.Equal(classified.BackendStateFingerprint, admitted.Reset?.BackendStateFingerprint);
        Assert.Equal(classified.Route, admitted.Preconditions?.Expected?.Route);
        Assert.Equal(classified.Route, admitted.Preconditions?.Observed?.Route);
        Assert.True(Assert.Single(admitted.BusinessOracles).Independent);
    }

    private static WorkflowRepairTransientValidationRequest Request(
        MauiFlowRepairProposal proposal,
        MauiFlow sourceFlow) => new()
        {
            Proposal = proposal,
            InMemorySelectorOverrideOnly = true,
            Eligibility = new MauiFlowRepairEligibilityDecision
            {
                Eligible = true,
                CurrentCheckpoint = Checkpoint(),
            },
            ClassifiedCheckpoint = Checkpoint(),
            ReplaySafety = new MauiFlowReplayEligibilityDecision
            {
                SideEffectPolicy = MauiFlowSideEffectPolicies.None,
                RepairValidationAllowed = true,
                RepairEligibility = true,
                RunVerificationAllowed = true,
                DownstreamContinuationAllowed = false,
            },
            SourceFlow = sourceFlow,
        };

    private static MauiFlowRunReport PassingReport(MauiFlowRepairProposal proposal) => new()
    {
        RunId = "transient-run",
        Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Passed },
        Verification = new MauiFlowRunVerification { Verified = true },
        ReplayEligibility = new MauiFlowReplayEligibilityDecision { RunVerificationAllowed = true },
        Steps =
        [
            new MauiFlowStepAttempt
            {
                StepId = proposal.SourceStepId,
                Sequence = 1,
                TargetResolution = new MauiFlowTargetResolution { MatchCount = 1 },
                Fingerprint = Fingerprint(),
                Assertions =
                [
                    new MauiFlowAssertionResult { Kind = "exists", Passed = true },
                ],
            },
        ],
    };

    private static MauiFlowCheckpoint Checkpoint() => new()
    {
        AppBuildFingerprint = "build-a",
        AgentInstanceId = "instance-a",
        SeedFingerprint = "seed-a",
        BackendStateFingerprint = "backend-a",
        Route = "//home",
        Window = "main",
        Modal = "none",
        Locale = "en-US",
        Theme = "light",
        Orientation = "portrait",
        DisplayProfile = "phone",
        CollectionItemKey = "none",
    };

    private static MauiFlowRepairProposal Proposal(MauiFlow flow)
    {
        var fingerprint = Fingerprint();
        var generated = MauiFlowRepairProposalGenerator.Generate(new MauiFlowRepairProposalGenerationInput
        {
            Eligibility = new MauiFlowRepairEligibilityDecision
            {
                Eligible = true,
                FailureCode = MauiFlowFailureClasses.LocatorNotFound,
            },
            Flow = flow,
            BaseFlow = new MauiFlowReference
            {
                Path = "repair.md",
                FlowId = "flow-repair",
                Digest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow),
                Revision = 1,
            },
            SourceRunId = "run-local",
            SourceStepId = "stable-save-step",
            SourceFailureId = "failure-1",
            SourceFailureCode = MauiFlowFailureClasses.LocatorNotFound,
            PriorFingerprint = fingerprint,
            PriorActiveSelectorResolution = new MauiRepairPriorSelectorResolution
            {
                RunId = "prior-run",
                TrustedRun = true,
                WasUniquelyResolved = true,
                ActiveSelector = new FlowSelector { AutomationId = "old-save" },
                Fingerprint = fingerprint,
            },
            SelectorHealthCandidates =
            [
                new MauiSelectorCandidate
                {
                    CandidateId = "candidate-new-save",
                    Rank = 1,
                    Priority = 1,
                    Selector = new FlowSelector { AutomationId = "new-save" },
                    SelectorDescriptor = new MauiSelectorCandidateSelector
                    {
                        Kind = "automation-id",
                        AutomationId = "new-save",
                    },
                    Score = .9,
                    Scores = new MauiSelectorCandidateScores { DeterministicRankScore = .9 },
                    Unique = true,
                    Validation = new MauiSelectorCandidateValidation
                    {
                        Unique = true,
                        MatchCount = 1,
                        Accepted = true,
                        PlatformState = "validated",
                    },
                    Fingerprint = fingerprint,
                },
            ],
            CurrentResolutions =
            [
                new MauiRepairCandidateResolution
                {
                    CandidateId = "candidate-new-save",
                    MatchCount = 1,
                    SemanticFingerprintMatches = true,
                    CurrentFingerprint = fingerprint,
                },
            ],
            Trust = "current-local-run",
        });
        return Assert.Single(generated.Proposals);
    }

    private static MauiFlow Flow() => new()
    {
        Name = "repair",
        Steps =
        [
            new FlowStep
            {
                Seq = 1,
                StepId = "stable-save-step",
                Action = FlowActions.Tap,
                Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "old-save" } },
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

    private static MauiElementFingerprint Fingerprint() => new()
    {
        FingerprintId = "fp",
        Context = new MauiElementFingerprintContext
        {
            AppId = "com.example.app",
            AppBuild = "build-1",
            Platform = "android",
            Route = "//home",
            Window = "main",
            Modal = "none",
            Locale = "en-US",
            Theme = "light",
            Orientation = "portrait",
            DisplayProfile = "phone",
        },
        Managed = new MauiManagedElementIdentity
        {
            Type = "Button",
            FullType = "Microsoft.Maui.Controls.Button",
            Role = "button",
        },
        Topology = new MauiTopologySignature { AncestorHash = "ancestor", SiblingHash = "sibling" },
    };

    private sealed class FakeResetAttester : IWorkflowRepairResetAttester
    {
        private readonly MauiFlowCheckpoint _attested;

        public FakeResetAttester(MauiFlowCheckpoint attested)
        {
            _attested = attested;
            SeedFingerprint = attested.SeedFingerprint;
        }

        public string? SeedFingerprint { get; set; }

        public Task<WorkflowRepairAttestedState?> ObserveAttestedStateAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<WorkflowRepairAttestedState?>(new WorkflowRepairAttestedState
            {
                SeedFingerprint = SeedFingerprint ?? string.Empty,
                BackendStateFingerprint = _attested.BackendStateFingerprint ?? string.Empty,
                CollectionItemKey = _attested.CollectionItemKey ?? string.Empty,
            });

        public Task<WorkflowRepairResetAttestation?> AttestAsync(
            WorkflowRepairTransientValidationRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult<WorkflowRepairResetAttestation?>(new WorkflowRepairResetAttestation
            {
                Succeeded = true,
                Reset = new MauiFlowResetResult
                {
                    Requested = true,
                    Succeeded = true,
                    AppStateSucceeded = true,
                    BackendTestDataSucceeded = true,
                    Strategy = "fake-lifecycle-reset",
                    SeedFingerprint = SeedFingerprint,
                    BackendStateFingerprint = _attested.BackendStateFingerprint,
                },
                CollectionItemKey = _attested.CollectionItemKey,
                BusinessOracles =
                [
                    new MauiIndependentBusinessOracleResult
                    {
                        OracleId = "fake-oracle",
                        Independent = true,
                        Succeeded = true,
                    },
                ],
                EvidenceIds = ["reset-evidence-1"],
            });
    }

    /// <summary>Isolates the reset half so a checkpoint assertion is not masked by replay facts.</summary>
    private sealed class LifecycleOnlyHost : IWorkflowRepairValidationHost
    {
        private readonly IWorkflowRepairValidationHost _inner;

        public LifecycleOnlyHost(IWorkflowRepairValidationHost inner) => _inner = inner;

        public Task<WorkflowRepairLifecycleValidation> HardResetAsync(
            WorkflowRepairTransientValidationRequest request,
            CancellationToken cancellationToken)
            => _inner.HardResetAsync(request, cancellationToken);

        public Task<WorkflowRepairReplayValidation> ReplayWithInMemorySelectorOverrideAsync(
            WorkflowRepairTransientValidationRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(new WorkflowRepairReplayValidation { FailureCode = "not-exercised" });
    }
}
