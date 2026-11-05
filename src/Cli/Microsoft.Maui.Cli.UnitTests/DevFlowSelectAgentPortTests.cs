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

    // ----- Issue #343 follow-up: remote --agent-host must skip the local-broker ambiguity -----

    [Fact]
    public void RemoteHost_AmbiguousLocalAgents_DoesNotRefuse_UsesDefaultPort()
    {
        // The same agent set returns the refusal sentinel for a local host...
        AgentRegistration[] agents =
        [
            Agent("a", "/src/App.csproj", "net10.0-ios", "iOS", 7000),
            Agent("b", "/src/Other.csproj", "net10.0-android", "Android", 7001)
        ];
        Assert.Equal(0, DevFlowCommands.SelectAgentPort(agents, csprojPath: null, configPort: null));

        // ...but the local broker is irrelevant for an explicit remote host, so it must resolve
        // to the default rather than refusing (the agent count on THIS machine doesn't apply).
        Assert.Equal(9223, DevFlowCommands.SelectAgentPort("10.0.0.5", agents, csprojPath: null, configPort: null));
    }

    [Fact]
    public void RemoteHost_AmbiguousLocalAgents_WithConfigPort_UsesConfigPort()
    {
        AgentRegistration[] agents =
        [
            Agent("a", "/src/App.csproj", "net10.0-ios", "iOS", 7000),
            Agent("b", "/src/Other.csproj", "net10.0-android", "Android", 7001)
        ];

        Assert.Equal(5500, DevFlowCommands.SelectAgentPort("remote.example.com", agents, csprojPath: null, configPort: 5500));
    }

    [Fact]
    public void RemoteHost_IgnoresLocalCsprojMatch()
    {
        // Even a unique local csproj match must not bind a remote target to a local agent's port.
        AgentRegistration[] agents =
        [
            Agent("a", "/src/App.csproj", "net10.0-ios", "iOS", 7000)
        ];

        Assert.Equal(9223, DevFlowCommands.SelectAgentPort("192.168.1.20", agents, csprojPath: "/src/App.csproj", configPort: null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost")]
    [InlineData("LocalHost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    public void IsLocalAgentHost_LoopbackVariants_AreLocal(string? host)
        => Assert.True(DevFlowCommands.IsLocalAgentHost(host));

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.20")]
    [InlineData("remote.example.com")]
    [InlineData("device.local")]
    public void IsLocalAgentHost_RemoteHosts_AreNotLocal(string host)
        => Assert.False(DevFlowCommands.IsLocalAgentHost(host));
}

/// <summary>
/// Unit tests for <see cref="BrokerClient.BuildMultiAgentTargetingMessage"/>, the guidance
/// message shared by the CLI guard and the MCP <c>McpAgentSession</c> (issue #343).
/// </summary>
public class BrokerMultiAgentMessageTests
{
    private static AgentRegistration Agent(string id, string platform, string tfm, string appName, int port)
        => new()
        {
            Id = id,
            Project = $"/src/{id}.csproj",
            Tfm = tfm,
            Platform = platform,
            AppName = appName,
            Port = port,
            Version = "0.1.0-preview"
        };

    [Fact]
    public void Message_DefaultHint_MentionsMultipleAndAgentPortFlag()
    {
        AgentRegistration[] agents =
        [
            Agent("a", "iOS", "net10.0-ios", "FirstApp", 7000),
            Agent("b", "MacCatalyst", "net10.0-maccatalyst", "SecondApp", 7001)
        ];

        var message = BrokerClient.BuildMultiAgentTargetingMessage(agents);

        Assert.Contains("Multiple", message);
        Assert.Contains("--agent-port", message);
        Assert.Contains("--agent-port 7000  FirstApp (iOS net10.0-ios)", message);
        Assert.Contains("--agent-port 7001  SecondApp (MacCatalyst net10.0-maccatalyst)", message);
    }

    [Fact]
    public void Message_McpHint_UsesAgentPortParameterName()
    {
        AgentRegistration[] agents =
        [
            Agent("a", "iOS", "net10.0-ios", "FirstApp", 7000)
        ];

        var message = BrokerClient.BuildMultiAgentTargetingMessage(agents, optionHint: "agentPort");

        Assert.Contains("Multiple", message);
        Assert.Contains("Re-run with agentPort <port>", message);
        Assert.Contains("agentPort 7000  FirstApp", message);
        Assert.DoesNotContain("--agent-port", message);
    }

    [Fact]
    public void Message_OrdersAgentsByPort()
    {
        AgentRegistration[] agents =
        [
            Agent("b", "MacCatalyst", "net10.0-maccatalyst", "SecondApp", 7001),
            Agent("a", "iOS", "net10.0-ios", "FirstApp", 7000)
        ];

        var message = BrokerClient.BuildMultiAgentTargetingMessage(agents);

        Assert.True(message.IndexOf("7000", StringComparison.Ordinal) < message.IndexOf("7001", StringComparison.Ordinal));
    }

    [Fact]
    public void Message_EmptyAgents_ReturnsBaseGuidanceOnly()
    {
        var message = BrokerClient.BuildMultiAgentTargetingMessage([]);

        Assert.Contains("Multiple", message);
        Assert.Contains("--agent-port", message);
        Assert.DoesNotContain(Environment.NewLine, message);
    }
}
