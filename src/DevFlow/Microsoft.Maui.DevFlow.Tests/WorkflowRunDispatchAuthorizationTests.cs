using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Authorization for a device-mutating workflow run is a precondition of the coordinator, not of
/// the HTTP route that happens to be calling it. These tests pin that boundary so a second or third
/// dispatch surface cannot reintroduce an unauthorized path the way a route-level guard allows.
/// </summary>
public class WorkflowRunDispatchAuthorizationTests
{
    private const string BrokerTicket = "broker-issued-ticket";

    [Fact]
    public void Start_WithoutAConfiguredAuthorizer_RefusesEveryOriginAndTakesNoLease()
    {
        var leases = new RecordingLeaseRegistry();
        var executed = false;
        using var coordinator = new WorkflowRunCoordinator(
            leases,
            (_, _) =>
            {
                executed = true;
                return Task.FromResult(PassingReport());
            });

        foreach (var origin in Enum.GetValues<WorkflowRunDispatchOrigin>())
        {
            var result = coordinator.Start(
                Request("unconfigured", $"unconfigured-{origin}"),
                Target(),
                static () => true,
                leaseHandoff: new WorkflowRunLeaseHandoff("inspector-lease", "web", "Inspector"),
                dispatchOrigin: origin,
                dispatchTicket: BrokerTicket);

            Assert.False(result.Ok);
            Assert.Equal(403, result.StatusCode);
            Assert.Contains("was not configured to authorize", result.Error!, StringComparison.Ordinal);
        }

        Assert.Empty(leases.Actions);
        Assert.False(executed);
    }

    [Fact]
    public void Start_WhenTheAuthorizerDenies_RefusesBeforeFlowValidationOrIdempotencyState()
    {
        var leases = new RecordingLeaseRegistry();
        using var coordinator = new WorkflowRunCoordinator(
            leases,
            static (_, _) => Task.FromResult(PassingReport()),
            authorizeDispatch: static _ => WorkflowRunDispatchDecision.Deny("no human said yes"));

        // A flow the coordinator would otherwise reject with 400: authorization has to win, or the
        // status code alone would tell a caller whether an unauthorized request was well formed.
        var invalid = new MauiFlow
        {
            Name = "invalid",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.SetProperty,
                    Args = new FlowStepArgs
                    {
                        Selector = new FlowSelector { AutomationId = "label" },
                        Value = "hello"
                    }
                }
            }
        };

        var denied = coordinator.Start(
            new WorkflowRunStartRequest
            {
                AgentId = "agent",
                AgentInstanceId = "instance",
                IdempotencyKey = "denied-key",
                Flow = invalid
            },
            Target(),
            static () => true);

        Assert.False(denied.Ok);
        Assert.Equal(403, denied.StatusCode);
        Assert.Equal("no human said yes", denied.Error);
        Assert.Null(denied.Errors);
        Assert.Empty(leases.Actions);

        // The refused dispatch left no idempotency entry behind, so a later authorized run may
        // still use the key it never got to claim.
        using var allowing = new WorkflowRunCoordinator(
            new RecordingLeaseRegistry(),
            static (_, _) => Task.FromResult(PassingReport()),
            authorizeDispatch: AllowAll);
        Assert.True(allowing.Start(Request("retry", "denied-key"), Target(), static () => true).Ok);
    }

    [Fact]
    public void Start_DefaultsToTheStrictestOriginAndAuthorizesAgainstTheBrokerCanonicalTarget()
    {
        WorkflowRunDispatch? seen = null;
        using var coordinator = new WorkflowRunCoordinator(
            new RecordingLeaseRegistry(),
            static (_, _) => Task.FromResult(PassingReport()),
            authorizeDispatch: dispatch =>
            {
                seen = dispatch;
                return WorkflowRunDispatchDecision.Deny("recorded");
            });

        // Client-supplied ids on the request are exactly what an attacker controls, so the
        // authorizer has to be handed the broker's own view of who is being driven.
        var request = Request("defaults", "defaults-key");
        request.AgentId = "attacker-chosen-agent";
        request.AgentInstanceId = "attacker-chosen-instance";
        request.AuthorizationId = "auth-1";

        coordinator.Start(request, Target(), static () => true);

        Assert.NotNull(seen);
        Assert.Equal(WorkflowRunDispatchOrigin.TestAgentGrant, seen!.Origin);
        Assert.Equal("agent", seen.AgentId);
        Assert.Equal("instance", seen.AgentInstanceId);
        Assert.Equal("auth-1", seen.AuthorizationId);
        Assert.Null(seen.DispatchTicket);
        Assert.Null(seen.LeaseHandoff);
    }

    [Fact]
    public void Start_ExposesDeclaredDeviceMutationsToTheDispatchAuthorizer()
    {
        WorkflowRunDispatch? seen = null;
        using var coordinator = new WorkflowRunCoordinator(
            new RecordingLeaseRegistry(),
            static (_, _) => Task.FromResult(PassingReport()),
            authorizeDispatch: dispatch =>
            {
                seen = dispatch;
                return WorkflowRunDispatchDecision.Deny("recorded");
            });
        var request = Request("device-extension", "device-extension-key");
        request.Flow!.ExtensionData = new Dictionary<string, JsonElement>
        {
            ["deviceSteps"] = JsonDocument.Parse(
                """[{"afterStep":0,"action":"tap","x":1,"y":2}]""").RootElement.Clone(),
        };

        coordinator.Start(request, Target(), static () => true);

        Assert.NotNull(seen);
        Assert.True(seen!.HasDeviceExtensions);
    }

    [Fact]
    public void BrokerPolicy_RejectsDeviceExtensionsDuringRepairValidation()
    {
        using var broker = new BrokerServer(TestPorts.Reserve());
        var decision = broker.DecideWorkflowRunDispatch(new WorkflowRunDispatch(
            WorkflowRunDispatchOrigin.RepairValidation,
            "agent",
            "instance",
            AuthorizationId: "authorization",
            DispatchTicket: "ticket",
            LeaseHandoff: null,
            Steps: [],
            HasDeviceExtensions: true));

        Assert.False(decision.Allowed);
        Assert.Contains("device", decision.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Start_RecordsTheBrokerAuthorizationDecisionOnTheRunJournal()
    {
        using var coordinator = new WorkflowRunCoordinator(
            new RecordingLeaseRegistry(),
            static (_, _) => Task.FromResult(PassingReport()),
            authorizeDispatch: static _ => WorkflowRunDispatchDecision.Allow("inspector-workbench-lease"));

        var started = coordinator.Start(
            Request("audited", "audited-key"),
            Target(),
            static () => true,
            dispatchOrigin: WorkflowRunDispatchOrigin.InspectorWorkbench,
            dispatchTicket: BrokerTicket);

        Assert.True(started.Ok);
        var snapshot = await WaitForTerminalAsync(coordinator, started);
        var audit = Assert.Single(
            snapshot.Events,
            entry => string.Equals(entry.Kind, "dispatch-authorized", StringComparison.Ordinal));
        Assert.Contains("inspector-workbench-lease", audit.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectorStart_WithoutABrokerIssuedTicket_IsRefusedByTheCoordinator()
    {
        var leases = new RecordingLeaseRegistry();
        using var coordinator = new WorkflowRunCoordinator(
            leases,
            static (_, _) => Task.FromResult(PassingReport()),
            authorizeDispatch: TicketBoundInspectorAuthorizer);

        var services = InspectorServices(coordinator, "ticket-this-broker-never-issued");
        var refused = services.Start(
            Request("inspector", "inspector-forged-key"),
            leaseHandoff: new WorkflowRunLeaseHandoff("inspector-lease", "web", "Inspector"));

        Assert.False(refused.Ok);
        Assert.Equal(403, refused.StatusCode);
        Assert.Contains("dispatch ticket", refused.Error!, StringComparison.Ordinal);
        Assert.Empty(leases.Actions);
    }

    [Fact]
    public void InspectorStart_WithoutTheMutationLease_IsRefusedByTheCoordinator()
    {
        var leases = new RecordingLeaseRegistry();
        using var coordinator = new WorkflowRunCoordinator(
            leases,
            static (_, _) => Task.FromResult(PassingReport()),
            authorizeDispatch: TicketBoundInspectorAuthorizer);

        var services = InspectorServices(coordinator, BrokerTicket);
        var refused = services.Start(Request("inspector", "inspector-no-lease-key"));

        Assert.False(refused.Ok);
        Assert.Equal(403, refused.StatusCode);
        Assert.Contains("mutation lease", refused.Error!, StringComparison.Ordinal);
        Assert.Empty(leases.Actions);
    }

    [Fact]
    public async Task InspectorStart_WithTheBrokerIssuedTicketAndLease_IsAuthorizedAndAudited()
    {
        using var coordinator = new WorkflowRunCoordinator(
            new RecordingLeaseRegistry(),
            static (_, _) => Task.FromResult(PassingReport()),
            authorizeDispatch: TicketBoundInspectorAuthorizer);

        var services = InspectorServices(coordinator, BrokerTicket);
        var started = services.Start(
            Request("inspector", "inspector-authorized-key"),
            leaseHandoff: new WorkflowRunLeaseHandoff("inspector-lease", "web", "Inspector"));

        Assert.True(started.Ok);
        var snapshot = await WaitForTerminalAsync(coordinator, started);
        Assert.Contains(
            snapshot.Events,
            entry => string.Equals(entry.Kind, "dispatch-authorized", StringComparison.Ordinal) &&
                entry.Message.Contains("inspector-workbench-lease", StringComparison.Ordinal));
    }

    /// <summary>
    /// Mirrors the broker's Inspector rule: a ticket the broker minted for this exact agent
    /// instance, plus the mutation lease the Inspector already holds over the app.
    /// </summary>
    private static WorkflowRunDispatchDecision TicketBoundInspectorAuthorizer(WorkflowRunDispatch dispatch)
    {
        if (dispatch.Origin != WorkflowRunDispatchOrigin.InspectorWorkbench)
            return WorkflowRunDispatchDecision.Deny("Only the Inspector workbench origin is allowed here.");
        if (!string.Equals(dispatch.DispatchTicket, BrokerTicket, StringComparison.Ordinal))
            return WorkflowRunDispatchDecision.Deny("A broker-issued dispatch ticket is required.");
        return dispatch.LeaseHandoff is null
            ? WorkflowRunDispatchDecision.Deny("The Inspector must already hold this app's mutation lease.")
            : WorkflowRunDispatchDecision.Allow("inspector-workbench-lease");
    }

    private static WorkflowRunDispatchDecision AllowAll(WorkflowRunDispatch dispatch)
        => WorkflowRunDispatchDecision.Allow("test-allow-all");
    private static InspectorWorkflowServices InspectorServices(
        WorkflowRunCoordinator coordinator,
        string dispatchTicket)
        => new(
            coordinator,
            new ArtifactTrustImportService(),
            new ArtifactTrustStore(),
            new WorkflowRepairProposalStore(),
            new WorkflowXamlSourceProposalStore(),
            new WorkflowCSharpSourceProposalStore(),
            Target(),
            dispatchTicket,
            static () => true,
            CancellationToken.None);

    private static async Task<WorkflowRunSnapshot> WaitForTerminalAsync(
        WorkflowRunCoordinator coordinator,
        WorkflowRunStartResult start)
        => await coordinator.WaitForTerminalAsync(
            start.Run!.RunId,
            start.CapabilityToken!,
            new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

    private static WorkflowRunTarget Target() => new("agent", "instance", 12345, "test", "Test app");

    private static FlowReplayReport PassingReport() => new()
    {
        Ok = true,
        Name = "flow",
        Total = 1,
        Passed = 1
    };

    private static WorkflowRunStartRequest Request(string name, string idempotencyKey) => new()
    {
        AgentId = "agent",
        AgentInstanceId = "instance",
        IdempotencyKey = idempotencyKey,
        Flow = new MauiFlow
        {
            Name = name,
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Assert,
                    Asserts = new()
                    {
                        new FlowAssert
                        {
                            Kind = "exists",
                            Verify = false,
                            Selector = new FlowSelector { AutomationId = "label" }
                        }
                    }
                }
            }
        }
    };

    private sealed class RecordingLeaseRegistry : IWorkflowMutationLeaseRegistry
    {
        private readonly ConcurrentQueue<string> _actions = new();

        public IReadOnlyCollection<string> Actions => _actions.ToArray();

        public MutationLeaseSnapshot Control(
            string agentId,
            string action,
            string? leaseId,
            string? holderKind,
            string? label,
            bool force,
            string? transactionId)
        {
            _actions.Enqueue(action);
            return new MutationLeaseSnapshot(
                Allowed: true,
                YouHold: true,
                HeldByOther: false,
                LeaseId: leaseId,
                TransactionId: transactionId,
                HolderKind: holderKind,
                Label: label,
                ExpiresInMs: 10_000)
            {
                AuthorityEpoch = 1
            };
        }


        public MutationLeaseSnapshot TryAdoptIdleLease(
            string agentId,
            string targetLeaseId,
            string transactionId,
            string? holderKind,
            string? label,
            IReadOnlyCollection<string> adoptableHolderKinds)
            => new(false, false, true, null, null, "vscode", "VS Code Inspector", 5_000);

        public MutationLeaseSnapshot TransferAndBegin(
            string agentId,
            string sourceLeaseId,
            string targetLeaseId,
            string transactionId,
            string? holderKind,
            string? label)
        {
            _actions.Enqueue("transfer");
            return new MutationLeaseSnapshot(
                Allowed: true,
                YouHold: true,
                HeldByOther: false,
                LeaseId: targetLeaseId,
                TransactionId: transactionId,
                HolderKind: holderKind,
                Label: label,
                ExpiresInMs: 10_000)
            {
                AuthorityEpoch = 2
            };
        }
    }
}

/// <summary>
/// Pins the broker's own dispatch rule rather than a test double of it, so a regression in the
/// authorizer itself — an origin that stops requiring its ticket, or an unrecognized origin that
/// starts being allowed — fails here instead of shipping.
/// </summary>
public class BrokerWorkflowRunDispatchAuthorizerTests
{
    [Fact]
    public void InspectorDispatch_NeedsBothTheBrokerTicketAndTheHeldLease()
    {
        using var broker = new BrokerServer(45911, TimeSpan.FromMinutes(1));
        var ticket = broker.ComputeWorkflowRunDispatchTicket(
            "agent",
            "instance",
            WorkflowRunDispatchOrigin.InspectorWorkbench);

        var withoutTicket = broker.AuthorizeWorkflowRunDispatch(
            Dispatch(WorkflowRunDispatchOrigin.InspectorWorkbench, ticket: null, lease: Lease()));
        Assert.False(withoutTicket.Allowed);
        Assert.Contains("dispatch ticket", withoutTicket.Error!, StringComparison.Ordinal);

        var withoutLease = broker.AuthorizeWorkflowRunDispatch(
            Dispatch(WorkflowRunDispatchOrigin.InspectorWorkbench, ticket, lease: null));
        Assert.False(withoutLease.Allowed);
        Assert.Contains("mutation lease", withoutLease.Error!, StringComparison.Ordinal);

        var allowed = broker.AuthorizeWorkflowRunDispatch(
            Dispatch(WorkflowRunDispatchOrigin.InspectorWorkbench, ticket, Lease()));
        Assert.True(allowed.Allowed);
        Assert.Equal("inspector-workbench-lease", allowed.AuditReason);
    }

    [Fact]
    public void ATicketIsUsableOnlyForTheOriginAgentAndBrokerItWasIssuedFor()
    {
        using var broker = new BrokerServer(45912, TimeSpan.FromMinutes(1));
        var repairTicket = broker.ComputeWorkflowRunDispatchTicket(
            "agent",
            "instance",
            WorkflowRunDispatchOrigin.RepairValidation);

        // RepairValidation needs no lease, so a ticket that also satisfied the Inspector rule would
        // let an Inspector-scoped credential start a run without holding the app.
        var crossOrigin = broker.AuthorizeWorkflowRunDispatch(
            Dispatch(WorkflowRunDispatchOrigin.InspectorWorkbench, repairTicket, Lease()));
        Assert.False(crossOrigin.Allowed);

        var otherInstance = broker.AuthorizeWorkflowRunDispatch(new WorkflowRunDispatch(
            WorkflowRunDispatchOrigin.RepairValidation,
            "agent",
            "another-instance",
            AuthorizationId: null,
            repairTicket,
            LeaseHandoff: null));
        Assert.False(otherInstance.Allowed);

        using var otherBroker = new BrokerServer(45913, TimeSpan.FromMinutes(1));
        var otherBrokerVerdict = otherBroker.AuthorizeWorkflowRunDispatch(
            Dispatch(WorkflowRunDispatchOrigin.RepairValidation, repairTicket, lease: null));
        Assert.False(otherBrokerVerdict.Allowed);

        Assert.True(broker.AuthorizeWorkflowRunDispatch(
            Dispatch(WorkflowRunDispatchOrigin.RepairValidation, repairTicket, lease: null)).Allowed);
    }

    [Fact]
    public void TestAgentDispatchWithoutAHumanGrantIsRefused()
    {
        using var broker = new BrokerServer(45914, TimeSpan.FromMinutes(1));

        var refused = broker.AuthorizeWorkflowRunDispatch(new WorkflowRunDispatch(
            WorkflowRunDispatchOrigin.TestAgentGrant,
            "agent",
            "instance",
            AuthorizationId: "not-a-real-authorization",
            DispatchTicket: broker.ComputeWorkflowRunDispatchTicket(
                "agent",
                "instance",
                WorkflowRunDispatchOrigin.TestAgentGrant),
            LeaseHandoff: Lease()));

        Assert.False(refused.Allowed);
        Assert.Contains("authorization is required", refused.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOriginTheBrokerDoesNotRecognizeIsRefused()
    {
        using var broker = new BrokerServer(45915, TimeSpan.FromMinutes(1));

        var refused = broker.AuthorizeWorkflowRunDispatch(new WorkflowRunDispatch(
            (WorkflowRunDispatchOrigin)9999,
            "agent",
            "instance",
            AuthorizationId: null,
            DispatchTicket: null,
            LeaseHandoff: Lease()));

        Assert.False(refused.Allowed);
        Assert.Contains("not one this broker authorizes", refused.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDispatchTicketIsKeptOutOfDiagnosticText()
    {
        var rendered = Dispatch(
            WorkflowRunDispatchOrigin.InspectorWorkbench,
            "ticket-value-that-must-not-be-printed",
            Lease()).ToString();

        Assert.DoesNotContain("ticket-value-that-must-not-be-printed", rendered, StringComparison.Ordinal);
        Assert.Contains("[redacted]", rendered, StringComparison.Ordinal);
    }

    private static WorkflowRunDispatch Dispatch(
        WorkflowRunDispatchOrigin origin,
        string? ticket,
        WorkflowRunLeaseHandoff? lease) => new(
            origin,
            "agent",
            "instance",
            AuthorizationId: null,
            ticket,
            lease);

    private static WorkflowRunLeaseHandoff Lease() => new("inspector-lease", "web", "Inspector");
}
