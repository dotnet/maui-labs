using Microsoft.Maui.DevFlow.Logging;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Regression fence for the framework-neutral half of DevFlow.
///
/// DevFlow must run inside plain .NET Android / iOS / Mac Catalyst / macOS apps that have no MAUI
/// reference at all. These tests fail the build the moment a MAUI type sneaks back into an assembly
/// that is supposed to stay neutral.
/// </summary>
public class MauiFreeAssemblyTests
{
    [Fact]
    public void Logging_DoesNotReferenceMaui()
        => AssemblyReferenceGuard.AssertMauiFree(typeof(FileLogWriter).Assembly.Location);

    [Fact]
    public void Logging_ProjectDeclaresNoMaui()
        => AssemblyReferenceGuard.AssertProjectDeclaresNoMaui(Path.Combine(
            TestRepo.Root,
            "src", "DevFlow", "Microsoft.Maui.DevFlow.Logging", "Microsoft.Maui.DevFlow.Logging.csproj"));

    [Fact]
    public void Abstractions_DoesNotReferenceMaui()
        => AssemblyReferenceGuard.AssertMauiFree(typeof(AgentOptions).Assembly.Location);

    [Fact]
    public void Abstractions_ProjectDeclaresNoMaui()
        => AssemblyReferenceGuard.AssertProjectDeclaresNoMaui(Path.Combine(
            TestRepo.Root,
            "src", "DevFlow", "Microsoft.Maui.DevFlow.Agent.Abstractions",
            "Microsoft.Maui.DevFlow.Agent.Abstractions.csproj"));

    [Fact]
    public void Abstractions_HostsTheNeutralAgentService()
    {
        // DevFlowAgentService is the routing core every backend derives from. If it drifts back
        // into the MAUI assembly, native apps lose the ability to host an agent at all.
        var neutral = typeof(DevFlowAgentService).Assembly.GetName().Name;

        Assert.Equal("Microsoft.Maui.DevFlow.Agent.Abstractions", neutral);
        Assert.True(
            typeof(DevFlowAgentService).IsAssignableFrom(typeof(MauiDevFlowAgentService)),
            "MauiDevFlowAgentService must remain a DevFlowAgentService backend.");
    }

    [Fact]
    public void Guard_DetectsMauiReferences_InAMauiAssembly()
    {
        // Sanity check: the guard must actually be capable of failing. Agent.Core is the MAUI
        // backend, so it is expected to reference MAUI — if this comes back empty the guard is
        // silently passing everywhere.
        var agentCore = Path.Combine(AppContext.BaseDirectory, "Microsoft.Maui.DevFlow.Agent.Core.dll");

        Assert.True(File.Exists(agentCore), $"Expected Agent.Core next to the tests: {agentCore}");
        Assert.NotEmpty(AssemblyReferenceGuard.GetMauiReferences(agentCore));
    }

    [Fact]
    public void Guard_TreatsDevFlowAssembliesAsNeutral()
    {
        // Microsoft.Maui.DevFlow.* shares the product prefix but is not MAUI itself.
        var logging = typeof(FileLogWriter).Assembly.Location;

        Assert.Contains(
            AssemblyReferenceGuard.GetReferencedAssemblyNames(logging),
            name => name.StartsWith("System.", StringComparison.Ordinal) ||
                    name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal));
        Assert.Empty(AssemblyReferenceGuard.GetMauiReferences(logging));
    }
}
