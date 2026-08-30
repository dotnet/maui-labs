using Microsoft.Maui.Cli.DevFlow.Broker;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The broker state file is a single machine-wide path that carries the owner-only native-host
/// approval token. Every broker on the machine shares it, so an ephemeral broker (the test suite
/// starts thousands, and a second IDE MCP server starts its own) must never overwrite or delete the
/// entry belonging to the long-running broker a developer is actually using. Doing so silently
/// invalidates that broker's approval token and makes every later approval fail.
/// </summary>
public class BrokerStateOwnershipTests
{
    private static BrokerState State(int pid, int port) => new() { Pid = pid, Port = port };

    [Fact]
    public void MayPublish_NoExistingState_ClaimsTheSlot()
    {
        Assert.True(BrokerServer.MayPublishBrokerState(existing: null, ourPort: 19223, _ => true));
    }

    [Fact]
    public void MayPublish_EphemeralBrokerDoesNotClobberRunningBroker()
    {
        var running = State(pid: 32100, port: 19223);

        Assert.False(BrokerServer.MayPublishBrokerState(running, ourPort: 52750, pid => pid == 32100));
    }

    [Fact]
    public void MayPublish_TakesOverOnceTheOwningBrokerIsGone()
    {
        var stale = State(pid: 32100, port: 19223);

        Assert.True(BrokerServer.MayPublishBrokerState(stale, ourPort: 52750, _ => false));
    }

    [Fact]
    public void MayPublish_SamePortRepublishesAfterRestart()
    {
        var previous = State(pid: 32100, port: 19223);

        Assert.True(BrokerServer.MayPublishBrokerState(previous, ourPort: 19223, _ => true));
    }

    [Fact]
    public void MayDelete_OnlyRetractsAnExactSelfMatch()
    {
        var mine = State(pid: 4242, port: 19223);

        Assert.True(BrokerServer.MayDeleteBrokerState(mine, ourPort: 19223, ourPid: 4242));
    }

    [Fact]
    public void MayDelete_EphemeralBrokerLeavesTheRunningBrokerRegistered()
    {
        var running = State(pid: 32100, port: 19223);

        Assert.False(BrokerServer.MayDeleteBrokerState(running, ourPort: 52750, ourPid: 3808));
    }

    [Fact]
    public void MayDelete_DoesNotRetractAfterAnotherProcessTookOurPort()
    {
        var takenOver = State(pid: 99999, port: 19223);

        Assert.False(BrokerServer.MayDeleteBrokerState(takenOver, ourPort: 19223, ourPid: 4242));
    }
}
