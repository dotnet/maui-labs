using Microsoft.Maui.Cli.DevFlow.Broker;

namespace Microsoft.Maui.DevFlow.Tests;

public class PreRunResetMutationLeaseTests
{
    [Fact]
    public void TryAcquire_WhenNoWriterExists_ClaimsTransactionalLeaseAndReleasesIt()
    {
        var leases = new MutationLeaseRegistry();

        using (var reset = PreRunResetMutationLease.TryAcquire(leases, "agent"))
        {
            Assert.NotNull(reset);
            var held = Status(leases, reset.LeaseId, reset.TransactionId);
            Assert.True(held.YouHold);
            Assert.Equal(reset.TransactionId, held.TransactionId);
            Assert.Equal("repair-validation", held.HolderKind);
            Assert.Equal("app-state-reset", held.Label);
        }

        Assert.False(Status(leases, "probe").HeldByOther);
    }

    [Fact]
    public void TryAcquire_AdoptsIdleTrustedInspectorLeaseAndReleasesIt()
    {
        var leases = new MutationLeaseRegistry();
        Assert.True(leases.Control(
            "agent", "claim", "inspector", "vscode", "VS Code Inspector", false).YouHold);

        using (var reset = PreRunResetMutationLease.TryAcquire(leases, "agent"))
        {
            Assert.NotNull(reset);
            Assert.False(Status(leases, "inspector").YouHold);
            Assert.True(Status(leases, reset.LeaseId, reset.TransactionId).YouHold);
            Assert.True(reset.PrepareHandoff());

            var ready = Status(leases, reset.LeaseId, reset.TransactionId);
            Assert.True(ready.YouHold);
            Assert.Null(ready.TransactionId);
            Assert.Equal(reset.LeaseId, reset.Handoff?.LeaseId);
        }

        Assert.False(Status(leases, "probe").HeldByOther);
    }

    [Fact]
    public void TryAcquire_RefusesTrustedInspectorLeaseWithActiveTransaction()
    {
        var leases = new MutationLeaseRegistry();
        Assert.True(leases.Control(
            "agent", "claim", "inspector", "vscode", "VS Code Inspector", false).YouHold);
        Assert.Equal(
            "human-transaction",
            leases.Control(
                "agent",
                "begin",
                "inspector",
                "vscode",
                "VS Code Inspector",
                false,
                "human-transaction").TransactionId);

        Assert.Null(PreRunResetMutationLease.TryAcquire(leases, "agent"));

        var held = Status(leases, "inspector", "human-transaction");
        Assert.True(held.YouHold);
        Assert.Equal("human-transaction", held.TransactionId);
    }

    [Fact]
    public void TryAcquire_RefusesIdleUntrustedLease()
    {
        var leases = new MutationLeaseRegistry();
        Assert.True(leases.Control(
            "agent", "claim", "other", "cli", "Some CLI", false).YouHold);

        Assert.Null(PreRunResetMutationLease.TryAcquire(leases, "agent"));

        var held = Status(leases, "other");
        Assert.True(held.YouHold);
        Assert.Equal("cli", held.HolderKind);
    }

    [Fact]
    public void PrepareHandoff_TransfersContinuouslyToWorkflowRun()
    {
        var leases = new MutationLeaseRegistry();
        using var reset = PreRunResetMutationLease.TryAcquire(leases, "agent");
        Assert.NotNull(reset);
        Assert.True(reset.PrepareHandoff());

        var run = leases.TransferAndBegin(
            "agent",
            reset.Handoff!.LeaseId,
            "run-lease",
            "run-transaction",
            "workflow-run",
            "run-1");

        Assert.True(run.YouHold);
        Assert.Equal("run-transaction", run.TransactionId);

        reset.Dispose();
        Assert.True(Status(leases, "run-lease", "run-transaction").YouHold);
    }

    private static MutationLeaseSnapshot Status(
        MutationLeaseRegistry leases,
        string leaseId,
        string? transactionId = null)
        => leases.Control(
            "agent",
            "status",
            leaseId,
            holderKind: null,
            label: null,
            force: false,
            transactionId);
}
