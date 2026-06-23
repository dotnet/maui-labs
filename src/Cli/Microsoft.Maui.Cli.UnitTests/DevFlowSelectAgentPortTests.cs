using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Unit tests for <see cref="DevFlowCommands.SelectAgentPort"/>, the pure agent-port
/// selection used by the <c>--agent-port</c> default value factory. Covers the truth table
/// for issue #343: ambiguous multi-agent targeting must resolve to the sentinel <c>0</c>
/// instead of silently defaulting to an arbitrary port.
/// </summary>
public class DevFlowSelectAgentPortTests
{
    private static AgentRegistration Agent(string id, string project, string tfm, string platform, int port)
        => new()
        {
            Id = id,
            Project = project,
            Tfm = tfm,
            Platform = platform,
            AppName = id,
            Port = port,
            Version = "0.1.0-preview"
        };

    [Fact]
    public void NoBrokerAgents_NullConfig_FallsBackToDefaultPort()
    {
        Assert.Equal(9223, DevFlowCommands.SelectAgentPort(agents: null, csprojPath: null, configPort: null));
    }

    [Fact]
    public void NoBrokerAgents_WithConfigPort_UsesConfigPort()
    {
        Assert.Equal(5000, DevFlowCommands.SelectAgentPort(agents: null, csprojPath: null, configPort: 5000));
    }

    [Fact]
    public void EmptyBrokerAgents_NullConfig_FallsBackToDefaultPort()
    {
        Assert.Equal(9223, DevFlowCommands.SelectAgentPort(agents: [], csprojPath: null, configPort: null));
    }

    [Fact]
    public void SingleAgent_AutoSelectsItsPort()
    {
        AgentRegistration[] agents = [Agent("a", "/src/App.csproj", "net10.0-ios", "iOS", 7000)];

        Assert.Equal(7000, DevFlowCommands.SelectAgentPort(agents, csprojPath: null, configPort: null));
    }

    [Fact]
    public void MultipleAgents_CsprojMatch_SelectsMatchedPort()
    {
        AgentRegistration[] agents =
        [
            Agent("a", "/src/App.csproj", "net10.0-ios", "iOS", 7000),
            Agent("b", "/src/Other.csproj", "net10.0-maccatalyst", "MacCatalyst", 7001)
        ];

        Assert.Equal(7001, DevFlowCommands.SelectAgentPort(agents, csprojPath: "/src/Other.csproj", configPort: null));
    }

    [Fact]
    public void MultipleAgents_NoMatch_WithConfigPort_UsesConfigPort()
    {
        AgentRegistration[] agents =
        [
            Agent("a", "/src/App.csproj", "net10.0-ios", "iOS", 7000),
            Agent("b", "/src/Other.csproj", "net10.0-maccatalyst", "MacCatalyst", 7001)
        ];

        Assert.Equal(5000, DevFlowCommands.SelectAgentPort(agents, csprojPath: null, configPort: 5000));
    }

    [Fact]
    public void MultipleAgents_NoMatch_NoConfigPort_ReturnsSentinelZero()
    {
        AgentRegistration[] agents =
        [
            Agent("a", "/src/App.csproj", "net10.0-ios", "iOS", 7000),
            Agent("b", "/src/Other.csproj", "net10.0-maccatalyst", "MacCatalyst", 7001)
        ];

        Assert.Equal(0, DevFlowCommands.SelectAgentPort(agents, csprojPath: null, configPort: null));
    }

    [Fact]
    public void MultipleAgents_CsprojMatchesNone_NoConfig_ReturnsSentinelZero()
    {
        AgentRegistration[] agents =
        [
            Agent("a", "/src/App.csproj", "net10.0-ios", "iOS", 7000),
            Agent("b", "/src/Other.csproj", "net10.0-maccatalyst", "MacCatalyst", 7001)
        ];

        Assert.Equal(0, DevFlowCommands.SelectAgentPort(agents, csprojPath: "/src/Unrelated.csproj", configPort: null));
    }
}
