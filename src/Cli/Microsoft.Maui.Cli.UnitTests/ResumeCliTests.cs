using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class ResumeCliTests
{
    [Fact]
    public async Task Resume_ExplicitAgentPort_SelectsTheMatchingAgent()
    {
        var cli = new CliTestHarness(9223);
        string? selectedAgent = null;
        DevFlowCommands.ResolveRunningBrokerPortAsync = () => Task.FromResult<int?>(19223);
        DevFlowCommands.ListBrokerAgentsAsync = _ => Task.FromResult<AgentRegistration[]?>([
            new AgentRegistration { Id = "first", Port = 11111, AppName = "First" },
            new AgentRegistration { Id = "second", Port = 22222, AppName = "Second" }
        ]);
        DevFlowCommands.ControlBrokerCheckpointAsync = (_, agentId, _) =>
        {
            selectedAgent = agentId;
            return Task.FromResult(new RouteCheckpointStatus
            {
                Ok = true,
                Connected = true
            });
        };
        try
        {
            var result = await cli.InvokeRawAsync(
                "devflow", "resume", "status", "--agent-port", "22222", "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("second", selectedAgent);
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }
}
