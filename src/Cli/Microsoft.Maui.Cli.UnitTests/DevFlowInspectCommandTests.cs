using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class DevFlowInspectCommandTests
{
    [Fact]
    public async Task Inspect_NoLaunch_UsesExplicitAgentAndStartupHints()
    {
        var cli = new CliTestHarness(7101);
        ConfigureBroker([Agent("ios-app", 7101)]);
        var browserOpened = false;
        DevFlowCommands.LaunchInspectorUrl = _ =>
        {
            browserOpened = true;
            return true;
        };

        try
        {
            var result = await cli.InvokeAsync(
                "devflow", "inspect", "--agent", "ios-app", "--no-launch",
                "--test", "maui-tests/login flow.md", "--trace", "artifacts/flow-run.json", "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.False(browserOpened);
            var json = result.ParseJsonOutput();
            Assert.Equal("ios-app", json.GetProperty("agentId").GetString());
            Assert.False(json.GetProperty("launched").GetBoolean());
            Assert.Equal("maui-tests/login flow.md", json.GetProperty("testHint").GetString());
            Assert.Equal("artifacts/flow-run.json", json.GetProperty("traceHint").GetString());
            var url = json.GetProperty("url").GetString();
            Assert.Contains("/inspector/ios-app/", url);
            Assert.Contains("test=maui-tests%2Flogin%20flow.md", url);
            Assert.Contains("trace=artifacts%2Fflow-run.json", url);
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }

    [Fact]
    public async Task Inspect_DefaultLaunch_UsesSelectedBrowserUrl()
    {
        var cli = new CliTestHarness(7102);
        ConfigureBroker([Agent("android-app", 7102)]);
        string? openedUrl = null;
        DevFlowCommands.LaunchInspectorUrl = url =>
        {
            openedUrl = url;
            return true;
        };

        try
        {
            var result = await cli.InvokeAsync("devflow", "inspect", "--agent", "android-app", "--no-json");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(openedUrl, result.StdOut);
            Assert.Contains("/inspector/android-app/", openedUrl);
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }

    [Fact]
    public async Task Inspect_AmbiguousAgents_FailsClosed()
    {
        var cli = new CliTestHarness(7103);
        ConfigureBroker([Agent("first-app", 7103), Agent("second-app", 7104)]);

        try
        {
            var result = await cli.InvokeAsync("devflow", "inspect", "--no-launch", "--json");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("More than one DevFlow agent", result.StdErr);
            Assert.Contains("--agent", result.StdErr);
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }

    [Fact]
    public async Task Inspect_StaleExplicitAgent_FailsClosed()
    {
        var cli = new CliTestHarness(7105);
        ConfigureBroker([Agent("connected-app", 7105)]);

        try
        {
            var result = await cli.InvokeAsync("devflow", "inspect", "--agent", "stale-app", "--no-launch", "--json");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("stale", result.StdErr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }

    private static void ConfigureBroker(AgentRegistration[] agents)
    {
        DevFlowCommands.EnsureBrokerRunningAsync = () => Task.FromResult<int?>(19223);
        DevFlowCommands.ListBrokerAgentsAsync = _ => Task.FromResult<AgentRegistration[]?>(agents);
    }

    private static AgentRegistration Agent(string id, int port) => new()
    {
        Id = id,
        Port = port,
        Project = $"C:\\projects\\{id}\\{id}.csproj",
        Tfm = "net10.0-android",
        Platform = "Android",
        AppName = id,
    };
}
