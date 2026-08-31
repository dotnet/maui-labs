using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Android;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;
using ModelContextProtocol;
using Microsoft.Maui.Cli.DevFlow.Inspector;

namespace Microsoft.Maui.Cli.DevFlow.Mcp;

public class McpAgentSession
{
	int? _defaultAgentPort;
	readonly string _mutationLeaseId = Guid.NewGuid().ToString("N");
	readonly object _testRunGate = new();
	readonly Dictionary<string, string> _testRunCapabilities = new(StringComparer.Ordinal);

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

	/// <summary>Resolves the broker registration selected by an MCP tool without using its port as state.</summary>
	public async Task<AgentRegistration> GetSelectedBrokerAgentAsync(int? agentPort = null)
	{
		var brokerPort = await GetBrokerPortAsync();
		var agents = await BrokerClient.ListAgentsAsync(brokerPort);
		var selectedPort = agentPort ?? DefaultAgentPort;
		var agent = selectedPort.HasValue
			? agents?.FirstOrDefault(candidate => candidate.Port == selectedPort.Value)
			: agents is null ? null : BrokerClient.ResolveAgent(agents);
		if (agent is null)
			throw new McpException("Select exactly one connected agent with agentPort before using this tool.");
		return agent;
	}

	/// <summary>Reads the canonical broker-produced activeVisual tree for the selected agent.</summary>
	public async Task<List<ElementInfo>?> GetInspectorTreeAsync(int? agentPort = null)
	{
		var brokerPort = await GetBrokerPortAsync();
		var agent = await GetSelectedBrokerAgentAsync(agentPort);
		return await InspectorSnapshotClient.GetActiveVisualTreeAsync(brokerPort, agent.Id);
	}

	/// <summary>
	/// Resolves an exact broker registration for the restricted test-agent profile. Unlike legacy
	/// MCP helpers, this never selects a default, project match, port, or most-recent agent.
	/// </summary>
	public async Task<AgentRegistration> ResolveTestAgentAsync(MauiTestAgentTarget? target)
	{
		if (target is null ||
			string.IsNullOrWhiteSpace(target.AgentId) ||
			string.IsNullOrWhiteSpace(target.AgentInstanceId))
		{
			throw new McpException("The restricted test-agent profile requires explicit agentId and agentInstanceId.");
		}

		var agents = await ListAgentsAsync();
		var agent = agents?.FirstOrDefault(candidate =>
			string.Equals(candidate.Id, target.AgentId, StringComparison.Ordinal) &&
			string.Equals(candidate.InstanceId, target.AgentInstanceId, StringComparison.Ordinal));
		if (agent is null)
		{
			throw new McpException(
				"The requested test-agent target is stale or unavailable. Refresh maui_test_agents and obtain a new approval.");
		}

		if (DefaultAgentHost.Equals("localhost", StringComparison.OrdinalIgnoreCase))
			await TryEnsureAndroidForwardingForAgentPortAsync(agent.Port, ensureBrokerReverse: false);
		return agent;
	}

	/// <summary>Creates a read-only direct client after exact target resolution.</summary>
	public async Task<AgentClient> GetTestAgentClientAsync(MauiTestAgentTarget? target)
	{
		var agent = await ResolveTestAgentAsync(target);
		return new AgentClient(DefaultAgentHost, agent.Port)
		{
			AutoAcquireMutationLease = false,
			MutationLeaseId = _mutationLeaseId,
			MutationLeaseHolderKind = "test-agent-read",
			MutationLeaseLabel = "Restricted test-agent read",
		};
	}

	/// <summary>Stores a run capability only for this MCP process and its authoring session.</summary>
	public void RememberTestRunCapability(string sessionId, string runId, string capabilityToken)
	{
		if (string.IsNullOrWhiteSpace(sessionId) ||
			string.IsNullOrWhiteSpace(runId) ||
			string.IsNullOrWhiteSpace(capabilityToken))
		{
			return;
		}

		lock (_testRunGate)
			_testRunCapabilities[BuildTestRunKey(sessionId, runId)] = capabilityToken;
	}

	/// <summary>Gets an in-memory run capability; it is never recovered from broker storage.</summary>
	public bool TryGetTestRunCapability(string sessionId, string runId, out string? capabilityToken)
	{
		lock (_testRunGate)
			return _testRunCapabilities.TryGetValue(BuildTestRunKey(sessionId, runId), out capabilityToken);
	}

	static string BuildTestRunKey(string sessionId, string runId) => $"{sessionId}\n{runId}";

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
