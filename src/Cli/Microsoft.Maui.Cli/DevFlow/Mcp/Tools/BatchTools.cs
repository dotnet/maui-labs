using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class BatchTools
{
	[McpServerTool(Name = "maui_batch"), Description("""
		Execute multiple UI actions in a single request. Actions run sequentially and are not transactional.
		Earlier actions are applied even if a later action fails.
		The 'actionsJson' parameter must be a JSON array of action objects.
		Each action object must have an "action" (or "type") field specifying the operation.

		Supported actions and their fields:
		- {"action":"tap", "elementId":"<id>"}
		- {"action":"fill", "elementId":"<id>", "text":"<value>"}
		- {"action":"clear", "elementId":"<id>"}
		- {"action":"key", "key":"enter", "elementId":"<id>"}
		- {"action":"focus", "elementId":"<id>"}
		- {"action":"scroll", "elementId":"<id>", "deltaX":0, "deltaY":200}
		- {"action":"gesture", "type":"swipe", "elementId":"<id>", "direction":"up"}
		- {"action":"navigate", "route":"//page"}
		- {"action":"back"}

		Note: The backend accepts both "action" and "type" fields. For gesture actions,
		use "action":"gesture" with a separate "type" field for the gesture kind.

		Example: [{"action":"fill","elementId":"entry1","text":"hello"},{"action":"tap","elementId":"btn1"}]
		""")]
	public static async Task<string> Batch(
		McpAgentSession session,
		[Description("JSON array of action objects. Each must have an 'action' or 'type' field (see tool description for schema)")] string actionsJson,
		[Description("If true, continue executing remaining actions after a failure (default: false)")] bool continueOnError = false,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		JsonArray parsed;
		try
		{
			var node = JsonNode.Parse(actionsJson);
			if (node is not JsonArray array)
				return "Invalid input: 'actionsJson' must be a JSON array, not " + (node?.GetValueKind().ToString() ?? "null") + ".";

			parsed = array;
		}
		catch (JsonException ex)
		{
			return $"Invalid JSON in 'actionsJson': {ex.Message}";
		}

		if (parsed.Count == 0)
			return "Empty actions array — nothing to execute.";

		var actions = new List<JsonObject>();
		for (int i = 0; i < parsed.Count; i++)
		{
			if (parsed[i] is not JsonObject obj)
				return $"Invalid action at index {i}: expected a JSON object, got {parsed[i]?.GetValueKind().ToString() ?? "null"}.";

			if (obj["action"] == null && obj["type"] == null)
				return $"Invalid action at index {i}: must have an 'action' or 'type' field (e.g., 'tap', 'fill', 'navigate').";

			actions.Add(obj);
		}

		try
		{
			var agent = await session.GetAgentClientAsync(agentPort);
			var result = await agent.BatchAsync(actions, continueOnError);
			return CliJson.SerializeUntyped(result, indented: false);
		}
		catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
		{
			return $"Batch request failed: {ex.Message}. Verify the app is running and the agent supports the batch endpoint.";
		}
	}
}
