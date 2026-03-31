using System.ComponentModel;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class AssertTool
{
	[McpServerTool(Name = "maui_assert"), Description("Assert that a UI element's property equals an expected value. Returns PASS/FAIL with actual vs expected. Use maui_tree to discover element IDs and property names.")]
	public static async Task<string> Assert(
		McpAgentSession session,
		[Description("Property name to check (e.g. Text, IsVisible, IsEnabled)")] string propertyName,
		[Description("Expected property value")] string expectedValue,
		[Description("Element ID from the visual tree (use either this or automationId)")] string? elementId = null,
		[Description("AutomationId to resolve the element")] string? automationId = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);

		var resolvedId = elementId;
		if (resolvedId is null && automationId is not null)
		{
			var results = await agent.QueryAsync(automationId: automationId);
			if (results.Count == 0)
				return $"FAIL: No element found with AutomationId '{automationId}'";
			resolvedId = results[0].Id;
		}

		if (resolvedId is null)
			return "FAIL: Either elementId or automationId must be provided";

		var actualValue = await agent.GetPropertyAsync(resolvedId, propertyName);
		var passed = string.Equals(actualValue, expectedValue, StringComparison.Ordinal);

		return passed
			? $"PASS: {propertyName} == \"{expectedValue}\""
			: $"FAIL: {propertyName} expected \"{expectedValue}\" but got \"{actualValue ?? "(null)"}\"";
	}
}
