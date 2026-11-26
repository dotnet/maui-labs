using Microsoft.Maui.Cli.DevFlow;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Separate one-shot CLI invocations from the same user must share a lease identity so that
/// consecutive commands are not refused as "another DevFlow session".
/// </summary>
public class DevFlowCliMutationLeaseIdTests
{
    [Fact]
    public void CreateCliMutationLeaseId_IsStableForTheSameUserAndMachine()
    {
        var first = DevFlowCommands.CreateCliMutationLeaseId("alice", "mac-1");
        var second = DevFlowCommands.CreateCliMutationLeaseId("alice", "mac-1");

        Assert.Equal(first, second);
        Assert.StartsWith("maui-cli-", first);
    }

    [Fact]
    public void CreateCliMutationLeaseId_DiffersForAnotherUser()
    {
        Assert.NotEqual(
            DevFlowCommands.CreateCliMutationLeaseId("alice", "mac-1"),
            DevFlowCommands.CreateCliMutationLeaseId("bob", "mac-1"));
    }
}
