using Microsoft.Maui.Cli.DevFlow;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// CLI processes are separate lease holders by default; MAUI_DEVFLOW_LEASE_ID opts separate
/// commands in to one shared lease identity.
/// </summary>
public class DevFlowCliMutationLeaseIdTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveCliMutationLeaseId_WithoutSharedId_IsUniquePerProcess(string? sharedLeaseId)
    {
        Assert.NotEqual(
            DevFlowCommands.ResolveCliMutationLeaseId(sharedLeaseId),
            DevFlowCommands.ResolveCliMutationLeaseId(sharedLeaseId));
    }

    [Fact]
    public void ResolveCliMutationLeaseId_WithSharedId_IsReusedAcrossProcesses()
    {
        var first = DevFlowCommands.ResolveCliMutationLeaseId(" my-script ");
        var second = DevFlowCommands.ResolveCliMutationLeaseId("my-script");

        Assert.Equal(first, second);
        Assert.NotEqual(first, DevFlowCommands.ResolveCliMutationLeaseId("other-script"));
    }
}
