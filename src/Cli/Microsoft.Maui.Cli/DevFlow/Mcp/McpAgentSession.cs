using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Mcp;

public class McpAgentSession
{
	public int? DefaultAgentPort { get; set; }
	public string DefaultAgentHost { get; set; } = "localhost";

	public async Task<AgentClient> GetAgentClientAsync(int? agentPort = null)
	{
		var port = agentPort ?? DefaultAgentPort ?? await ResolveAgentPortAsync();
		return new AgentClient(DefaultAgentHost, port);
	}

	public async Task<int> GetBrokerPortAsync()
	{
		var port = await BrokerClient.EnsureBrokerRunningAsync();
		return port ?? BrokerServer.DefaultPort;
	}

	public async Task<AgentRegistration[]?> ListAgentsAsync()
	{
		var brokerPort = await GetBrokerPortAsync();
		return await BrokerClient.ListAgentsAsync(brokerPort);
	}

	private async Task<int> ResolveAgentPortAsync()
	{
		return await BrokerClient.ResolveAgentPortForProjectAsync()
			?? BrokerClient.ReadConfigPort()
			?? 9223;
	}
}
