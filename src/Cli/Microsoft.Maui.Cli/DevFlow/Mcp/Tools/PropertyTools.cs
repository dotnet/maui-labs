using System.ComponentModel;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class PropertyTools
{
	[McpServerTool(Name = "maui_get_property"), Description("Get the value of a property on a UI element (e.g., Text, IsVisible, BackgroundColor, SelectedIndex).")]
	public static async Task<string> GetProperty(
		McpAgentSession session,
		[Description("Element ID from the visual tree")] string elementId,
		[Description("Property name (e.g., 'Text', 'IsVisible', 'BackgroundColor')")] string property,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var value = await agent.GetPropertyAsync(elementId, property);
		return value ?? $"Property '{property}' not found on element '{elementId}'.";
	}

	[McpServerTool(Name = "maui_set_property"), Description("Set a property value on a UI element at runtime (e.g., Text, IsVisible, BackgroundColor, SelectedIndex).")]
	public static async Task<string> SetProperty(
		McpAgentSession session,
		[Description("Element ID from the visual tree")] string elementId,
		[Description("Property name (e.g., 'Text', 'IsVisible', 'BackgroundColor')")] string property,
		[Description("New value for the property")] string value,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.SetPropertyAsync(elementId, property, value);
		return success
			? $"Set '{property}' = '{value}' on element '{elementId}'."
			: $"Failed to set property '{property}' on element '{elementId}'.";
	}
}
