using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Android;
using Microsoft.Maui.DevFlow.Driver;
using ModelContextProtocol;

namespace Microsoft.Maui.Cli.DevFlow.Mcp;

public class McpAgentSession
{
	int? _defaultAgentPort;
	readonly string _mutationLeaseId = Guid.NewGuid().ToString("N");

	public int? DefaultAgentPort
	{
		get => _defaultAgentPort;
		set
		{
			_defaultAgentPort = value;
			if (DefaultAgent?.Port != value)
				DefaultAgent = null;
		}
	}
	public string DefaultAgentHost { get; set; } = "localhost";
	AgentRegistration? DefaultAgent { get; set; }

	/// <summary>Creates an agent client owned by the caller. Dispose it after the tool call completes.</summary>
	public async Task<AgentClient> GetAgentClientAsync(int? agentPort = null)
	{
		var selectedPort = agentPort ?? DefaultAgentPort;
		var port = selectedPort ?? await ResolveAgentPortAsync();
		if (selectedPort.HasValue && DefaultAgentHost.Equals("localhost", StringComparison.OrdinalIgnoreCase))
			await TryEnsureAndroidForwardingForAgentPortAsync(port, ensureBrokerReverse: false);
		return new AgentClient(DefaultAgentHost, port)
		{
			MutationLeaseId = _mutationLeaseId,
			MutationLeaseHolderKind = "mcp",
			MutationLeaseLabel = "MCP client"
		};
	}

	public void SetDefaultAgent(AgentRegistration agent)
	{
		DefaultAgent = agent;
		DefaultAgentPort = agent.Port;
	}

	public async Task SetDefaultAgentPortAsync(int agentPort)
	{
		DefaultAgent = await FindAgentByPortAsync(agentPort);
		DefaultAgentPort = agentPort;
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
		var agent = await BrokerClient.ResolveAgentForProjectAsync();
		if (agent != null)
		{
			if (IsAndroidAgent(agent))
				await TryEnsureAndroidForwardingAsync([agent.Port], ensureBrokerReverse: true);
			return agent.Port;
		}

		// No single agent could be resolved. Distinguish the safe "no broker / single app"
		// path from genuine ambiguity (broker reports >1 agents). In the ambiguous case,
		// refuse instead of silently defaulting to a port and targeting an arbitrary app
		// (issue #343). An explicit agentPort or a selected default agent bypasses this.
		var configPort = BrokerClient.ReadConfigPort();
		if (!configPort.HasValue)
		{
			var brokerPort = BrokerClient.ReadBrokerPortPublic();
			if (brokerPort.HasValue)
			{
				var agents = await BrokerClient.ListAgentsAsync(brokerPort.Value);
				if (agents is { Length: > 1 })
					throw new McpException(BrokerClient.BuildMultiAgentTargetingMessage(agents, optionHint: "agentPort"));
			}
		}

		return configPort ?? 9223;
	}

	async Task TryEnsureAndroidForwardingForAgentPortAsync(int agentPort, bool ensureBrokerReverse)
	{
		var agent = DefaultAgent?.Port == agentPort
			? DefaultAgent
			: await FindAgentByPortAsync(agentPort);

		if (agent is not null && IsAndroidAgent(agent))
			await TryEnsureAndroidForwardingAsync([agentPort], ensureBrokerReverse);
	}

	static async Task<AgentRegistration?> FindAgentByPortAsync(int agentPort)
	{
		var brokerPort = BrokerClient.ReadBrokerPortPublic() ?? BrokerServer.DefaultPort;
		var agents = await BrokerClient.ListAgentsAsync(brokerPort);
		return agents?.FirstOrDefault(a => a.Port == agentPort);
	}

	static async Task TryEnsureAndroidForwardingAsync(int[] agentPorts, bool ensureBrokerReverse)
	{
		if (!AndroidDevFlowPortForwarder.IsAdbLikelyAvailable())
			return;

		try
		{
			await AndroidDevFlowPortForwarder.CreateDefault().EnsureAsync(new AndroidDevFlowForwardingRequest
			{
				AgentPorts = agentPorts,
				EnsureBrokerReverse = ensureBrokerReverse,
				BrokerPort = BrokerClient.ReadBrokerPortPublic() ?? Broker.BrokerServer.DefaultPort,
				Repair = true
			});
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[DevFlow Android forwarding] {ex.Message}");
		}
	}

	static bool IsAndroidAgent(AgentRegistration agent)
		=> agent.Platform.Contains("Android", StringComparison.OrdinalIgnoreCase)
		   || agent.Tfm.Contains("-android", StringComparison.OrdinalIgnoreCase);
}
