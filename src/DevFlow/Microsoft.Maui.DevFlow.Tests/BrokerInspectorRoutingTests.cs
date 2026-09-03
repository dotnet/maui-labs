using Microsoft.Maui.Cli.DevFlow.Broker;
using System.Collections.Concurrent;
using AgentBrokerRegistration = Microsoft.Maui.DevFlow.Agent.Core.BrokerRegistration;

namespace Microsoft.Maui.DevFlow.Tests;

public class BrokerInspectorRoutingTests
{
    [Fact]
    public void ResolveInspectorAgent_ExactId_ReturnsAgent()
    {
        var expected = new object();
        var agents = new Dictionary<string, object>
        {
            ["android-agent"] = expected
        };

        var actual = BrokerServer.ResolveInspectorAgent(agents, "android-agent");

        Assert.Same(expected, actual);
    }

    [Fact]
    public void ResolveInspectorAgent_MissingId_DoesNotFallbackToSoleAgent()
    {
        var agents = new Dictionary<string, object>
        {
            ["windows-agent"] = new object()
        };

        var actual = BrokerServer.ResolveInspectorAgent(agents, "android-agent");

        Assert.Null(actual);
    }

    [Fact]
    public void ResolveInspectorAgent_DefaultId_ReturnsSoleAgent()
    {
        var expected = new object();
        var agents = new Dictionary<string, object>
        {
            ["windows-agent"] = expected
        };

        var actual = BrokerServer.ResolveInspectorAgent(agents, "default");

        Assert.Same(expected, actual);
    }

    [Fact]
    public void ResolveInspectorAgent_DefaultId_WithMultipleAgents_ReturnsNull()
    {
        var agents = new Dictionary<string, object>
        {
            ["windows-agent"] = new object(),
            ["android-agent"] = new object()
        };

        var actual = BrokerServer.ResolveInspectorAgent(agents, "default");

        Assert.Null(actual);
    }

    [Fact]
    public async Task ReplaceConnection_ConcurrentReplacements_ReturnOnlySupersededValues()
    {
        var initial = new object();
        var connections = new ConcurrentDictionary<string, object>();
        connections["agent"] = initial;
        var replacements = Enumerable.Range(0, 32).Select(_ => new object()).ToArray();

        var superseded = await Task.WhenAll(replacements.Select(replacement => Task.Run(() =>
            BrokerServer.ReplaceConnection(connections, "agent", replacement))));

        var current = connections["agent"];
        Assert.Contains(current, replacements);
        Assert.DoesNotContain(current, superseded);
        Assert.Equal(replacements.Length, superseded.Distinct().Count());
        Assert.Contains(initial, superseded);
    }

    [Fact]
    public void AgentIds_SameBuildDifferentProcesses_CanCoexist()
    {
        const string project = "DevFlow.Sample";
        const string tfm = "net10.0-android";
        const string sessionId = "sample-session";

        var first = AgentRegistration.ComputeId(project, tfm, sessionId, processId: 1001);
        var second = AgentRegistration.ComputeId(project, tfm, sessionId, processId: 1002);

        Assert.NotEqual(first, second);
        Assert.Equal(first, AgentBrokerRegistration.ComputeId(project, tfm, sessionId, processId: 1001));
        Assert.Equal(second, AgentBrokerRegistration.ComputeId(project, tfm, sessionId, processId: 1002));

        var agents = new Dictionary<string, object>
        {
            [first] = new object(),
            [second] = new object()
        };
        Assert.Equal(2, agents.Count);
    }

    [Fact]
    public void AgentIds_WithoutProcessIdentity_UseLegacyId()
    {
        const string project = "DevFlow.Sample";
        const string tfm = "net10.0-windows10.0.19041.0";

        var legacy = AgentRegistration.ComputeId(project, tfm);

        Assert.Equal(legacy, AgentRegistration.ComputeId(project, tfm, "session", processId: null));
        Assert.Equal(legacy, AgentBrokerRegistration.ComputeId(project, tfm, "session", processId: 0));
    }
}