using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Execution;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Pins the attester's refusals. Every assertion here is a fail-closed path: the attester exists to
/// decline attesting facts nobody established, so a refusal quietly turning into an attestation is
/// the failure mode worth catching.
/// </summary>
public sealed class WorkflowRepairLifecycleResetAttesterTests
{
    private const string Strategy = "test-reset";

    [Fact]
    public async Task AttestAsync_WithoutAReviewedPlan_Refuses()
    {
        var owner = new StubOwner();
        var attestation = await Attest(owner, Request(plan: null));

        Assert.False(attestation!.Succeeded);
        Assert.Equal("repair-reviewed-plan-unavailable", attestation.FailureCode);
        Assert.Equal(0, owner.ResetCount);
    }

    [Fact]
    public async Task AttestAsync_WhenThePlanDeclaresNoResetStrategy_RefusesBeforeResetting()
    {
        var owner = new StubOwner();
        var attestation = await Attest(owner, Request(Plan(strategy: null)));

        Assert.False(attestation!.Succeeded);
        Assert.Equal("repair-reset-strategy-undeclared", attestation.FailureCode);
        // Nothing destructive may run for a reset no reviewer named.
        Assert.Equal(0, owner.ResetCount);
    }

    [Fact]
    public async Task AttestAsync_WhenTheOwnerAppliesADifferentStrategy_Refuses()
    {
        var owner = new StubOwner { Strategy = "some-other-reset" };
        var attestation = await Attest(owner, Request(Plan()));

        Assert.False(attestation!.Succeeded);
        Assert.Equal("repair-reset-strategy-unattested", attestation.FailureCode);
    }

    [Fact]
    public async Task AttestAsync_PassesTheDeclaredStrategyToTheOwnerSoItCanRefuseUpFront()
    {
        var owner = new StubOwner();
        await Attest(owner, Request(Plan()));

        Assert.Equal(Strategy, owner.LastRequest?.RequiredStrategy);
    }

    [Fact]
    public async Task AttestAsync_WhenThePlanDeclaresItsSeedUnderTheResetRequirement_PinsThatSeed()
    {
        var owner = new StubOwner();
        var plan = Plan(appStateSeed: new MauiFlowAppStateSeedFingerprint { SeedId = "seed-from-reset" });

        await Attest(owner, Request(plan));

        Assert.Equal("seed-from-reset", owner.LastRequest?.ExpectedSeedIdentity);
    }

    [Fact]
    public async Task AttestAsync_WhenThePlanRequiresABackendSeedDeclaredOnlyOnTheReset_Refuses()
    {
        var owner = new StubOwner();
        var plan = Plan(backendSeed: new MauiFlowBackendTestDataSeedFingerprint { SeedId = "backend-seed" });

        var attestation = await Attest(owner, Request(plan));

        Assert.False(attestation!.Succeeded);
        Assert.Equal("repair-backend-seed-unattested", attestation.FailureCode);
        Assert.True(owner.LastRequest?.RequiresBackendSeed);
    }

    [Fact]
    public async Task AttestAsync_WhenTheOwnerContradictsItselfAboutTheBackend_Refuses()
    {
        // "I seeded the backend" and "I applied no backend" cannot both be true.
        var owner = new StubOwner { BackendTestDataSucceeded = true };
        var attestation = await Attest(owner, Request(Plan()));

        Assert.False(attestation!.Succeeded);
        Assert.Equal("repair-backend-seed-unattested", attestation.FailureCode);
    }

    [Fact]
    public async Task AttestAsync_WhenNoBackendIsAppliedAndNoneIsRequired_AttestsTheBackendStepAsSatisfied()
    {
        var owner = new StubOwner();
        var attestation = await Attest(owner, Request(Plan()));

        Assert.True(attestation!.Succeeded);
        Assert.True(attestation.Reset!.BackendTestDataSucceeded);
        Assert.Equal(
            FlowLifecycleResetFingerprints.NoBackendApplied,
            attestation.Reset.BackendStateFingerprint);
        Assert.Equal(Strategy, attestation.Reset.Strategy);
    }

    [Fact]
    public async Task AttestAsync_WhenThePlanPinsACollectionItemTheOwnerDidNotSeed_Refuses()
    {
        var owner = new StubOwner();
        var plan = Plan(collectionItemKey: "order-42");

        var attestation = await Attest(owner, Request(plan));

        Assert.False(attestation!.Succeeded);
        Assert.Equal("repair-collection-item-unattested", attestation.FailureCode);
    }

    [Fact]
    public async Task AttestAsync_WhenTheOwnerReportsAFailedAppStateReset_Refuses()
    {
        var owner = new StubOwner { AppStateSucceeded = false };
        var attestation = await Attest(owner, Request(Plan()));

        Assert.False(attestation!.Succeeded);
        Assert.Equal("repair-app-state-reset-unattested", attestation.FailureCode);
    }

    [Fact]
    public async Task ObserveAttestedStateAsync_WithoutAnAppliedState_ReportsNothingRatherThanADefault()
    {
        var owner = new StubOwner { Applied = null };
        var observed = await new WorkflowRepairLifecycleResetAttester(owner)
            .ObserveAttestedStateAsync(CancellationToken.None);

        Assert.Null(observed);
    }

    private static Task<WorkflowRepairResetAttestation?> Attest(
        StubOwner owner,
        WorkflowRepairTransientValidationRequest request)
        => new WorkflowRepairLifecycleResetAttester(owner).AttestAsync(request, CancellationToken.None);

    private static MauiTestPlan Plan(
        string? strategy = Strategy,
        MauiFlowAppStateSeedFingerprint? appStateSeed = null,
        MauiFlowBackendTestDataSeedFingerprint? backendSeed = null,
        string? collectionItemKey = null)
        => new()
        {
            PlanId = "plan-attester",
            Revision = 1,
            Reset = new MauiTestResetRequirement
            {
                Required = true,
                Strategy = strategy,
                AppStateSeed = appStateSeed,
                BackendTestDataSeed = backendSeed,
            },
            Checkpoint = collectionItemKey is null
                ? null
                : new MauiFlowCheckpointRequirements { CollectionItemKey = collectionItemKey },
        };

    private static WorkflowRepairTransientValidationRequest Request(MauiTestPlan? plan)
        => new()
        {
            Proposal = new MauiFlowRepairProposal { ProposalId = "proposal-attester" },
            InMemorySelectorOverrideOnly = true,
            SourcePlan = plan,
        };

    private sealed class StubOwner : IFlowLifecycleResetOwner
    {
        public string OwnerId => "stub-lifecycle-owner";
        public int ResetCount { get; private set; }
        public FlowLifecycleResetRequest? LastRequest { get; private set; }
        public string Strategy { get; init; } = WorkflowRepairLifecycleResetAttesterTests.Strategy;
        public bool AppStateSucceeded { get; init; } = true;
        public bool BackendTestDataSucceeded { get; init; }
        public FlowLifecycleAppliedState? Applied { get; init; } = BuildState();

        public Task<FlowLifecycleAppliedState?> GetAppliedStateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Applied);

        public Task<FlowLifecycleResetOutcome> ResetAsync(
            FlowLifecycleResetRequest request,
            CancellationToken cancellationToken = default)
        {
            ResetCount++;
            LastRequest = request;
            return Task.FromResult(new FlowLifecycleResetOutcome
            {
                Succeeded = true,
                Applied = BuildState() with
                {
                    Strategy = Strategy,
                    AppStateSucceeded = AppStateSucceeded,
                    BackendTestDataSucceeded = BackendTestDataSucceeded,
                },
                EvidenceIds = ["stub-reset"],
            });
        }

        private static FlowLifecycleAppliedState BuildState()
        {
            var resetIdentity = FlowLifecycleResetFingerprints.ResetIdentity(
                "stub-lifecycle-owner",
                WorkflowRepairLifecycleResetAttesterTests.Strategy,
                "com.example.attester",
                "device-1");
            return new FlowLifecycleAppliedState
            {
                Strategy = WorkflowRepairLifecycleResetAttesterTests.Strategy,
                ResetIdentity = resetIdentity,
                SeedFingerprint = FlowLifecycleResetFingerprints.SeedFingerprint(
                    resetIdentity,
                    "build-attester",
                    seedIdentity: null),
                BackendStateFingerprint = FlowLifecycleResetFingerprints.NoBackendApplied,
                CollectionItemKey = FlowLifecycleResetFingerprints.NoCollectionItem,
                AppStateSucceeded = true,
            };
        }
    }
}
