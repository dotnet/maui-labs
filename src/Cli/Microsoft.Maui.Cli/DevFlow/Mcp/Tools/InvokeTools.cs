using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class InvokeTools
{
	[McpServerTool(Name = "maui_list_actions"), Description("""
		List all registered DevFlow Actions — named shortcuts the app developer has exposed
		for automation. Each action has a name, description, and typed parameters.

		Actions are methods annotated with [DevFlowAction] in the app's code. They're designed
		to be called by AI agents to quickly set up app state (e.g., login, seed data,
		navigate to a specific screen) without stepping through the UI manually.

		Call this tool early when starting a DevFlow session — available actions can
		dramatically reduce the number of steps needed to reach a desired app state.
		""")]
	public static async Task<string> ListActions(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.ListActionsAsync();
		return CliJson.SerializeUntyped(result, indented: false);
	}

	[McpServerTool(Name = "maui_invoke_action"), Description("""
		Invoke a registered DevFlow Action by name. Actions are named shortcuts the app
		developer has exposed — use maui_list_actions first to discover what's available.

		Arguments are passed as a JSON array matching the action's parameter order.
		Parameters with default values can be omitted. Supported types: string, bool,
		int, long, float, double, decimal, enum values (by name), and arrays of these.

		Example: To invoke "login-test-user" with email and password:
		  actionName: "login-test-user"
		  argsJson: '["alice@example.com", "secret123"]'

		Example: To invoke "seed-catalog" with just a count (using default for other params):
		  actionName: "seed-catalog"
		  argsJson: '[100]'
		""")]
	public static async Task<string> InvokeAction(
		McpAgentSession session,
		[Description("Name of the DevFlow Action to invoke (from maui_list_actions)")] string actionName,
		[Description("JSON array of arguments matching the action's parameter order. Omit trailing optional params. Example: '[\"hello\", 42, true]'")] string? argsJson = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		JsonArray? args = null;
		if (!string.IsNullOrWhiteSpace(argsJson))
		{
			try
			{
				var node = JsonNode.Parse(argsJson);
				if (node is not JsonArray array)
					return $"Invalid argsJson: expected a JSON array, got {node?.GetValueKind().ToString() ?? "null"}.";
				args = array;
			}
			catch (JsonException ex)
			{
				return $"Invalid JSON in argsJson: {ex.Message}";
			}
		}

		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.InvokeActionAsync(actionName, args);

		if (result == null)
			return $"Failed to invoke action '{actionName}'. Verify the app is running and the agent supports invoke.";

		return result.Success
			? $"Action '{actionName}' completed.{(result.ReturnValue != null ? $" Result: {result.ReturnValue}" : "")}"
			: $"Action '{actionName}' failed: {result.Error}";
	}

}
