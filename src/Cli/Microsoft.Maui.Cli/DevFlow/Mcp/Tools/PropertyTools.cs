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
		using var agent = await session.GetAgentClientAsync(agentPort);
		var value = await agent.GetPropertyAsync(elementId, property);
		return value ?? $"Property '{property}' not found on element '{elementId}'.";
	}

	[McpServerTool(Name = "maui_set_property"), Description("Set a property value on a UI element at runtime when DevFlow can do so without silently replacing a binding or dynamic resource.")]
	public static async Task<string> SetProperty(
		McpAgentSession session,
		[Description("Element ID from the visual tree")] string elementId,
		[Description("Property name (e.g., 'Text', 'IsVisible', 'BackgroundColor')")] string property,
		[Description("New value for the property")] string value,
		[Description("Allow replacing an existing binding, dynamic resource, or unknown value source for this session. Defaults to false and should be enabled only with explicit user approval.")] bool allowUnsafe = false,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		using var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.SetPropertyDetailedAsync(elementId, property, value, allowUnsafe);
		if (!result.Success)
			return result.Error ?? $"Failed to set property '{property}' on element '{elementId}'.";

		var warning = string.IsNullOrWhiteSpace(result.Warning) ? "" : $" Warning: {result.Warning}";
		return $"Set '{property}' = '{result.Value ?? value}' on element '{elementId}'.{warning}";
	}
}
