using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class EditingTools
{
	[McpServerTool(Name = "maui_add_element"), Description("Add a view to the running app's visual tree from a XAML snippet (e.g. '<Label Text=\"Hello\" />'). Layouts insert at the given index; single-content parents such as ContentPage or Border must be empty. Changes the running app only, not source files.")]
	public static async Task<string> AddElement(
		McpAgentSession session,
		[Description("Element ID of the parent layout or content container, from maui_tree")] string parentId,
		[Description("XAML for exactly one view. The default MAUI and x: namespaces are supplied automatically")] string xaml,
		[Description("Position among the parent's children; omit to append")] int? index = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
		[Description("Capture epoch from maui_tree; stale epochs are rejected")] long? captureEpoch = null,
		[Description("Native registry generation from maui_tree")] long? registryGeneration = null)
	{
		using var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.AddElementAsync(parentId, xaml, index, captureEpoch, registryGeneration);
		return RequireSuccess(result, $"Added {result.Element?.Type} '{result.Element?.Id}' to '{parentId}'{IndexText(result.Index)}.", $"Failed to add element to '{parentId}'.");
	}

	[McpServerTool(Name = "maui_remove_element"), Description("Remove a view from its parent in the running app. Changes the running app only, not source files.")]
	public static async Task<string> RemoveElement(
		McpAgentSession session,
		[Description("Element ID from the visual tree")] string elementId,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
		[Description("Capture epoch from maui_tree; stale epochs are rejected")] long? captureEpoch = null,
		[Description("Native registry generation from maui_tree")] long? registryGeneration = null)
	{
		using var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.RemoveElementAsync(elementId, captureEpoch, registryGeneration);
		return RequireSuccess(result, $"Removed '{elementId}' from '{result.ParentId}'.", $"Failed to remove element '{elementId}'.");
	}

	[McpServerTool(Name = "maui_move_element"), Description("Move a view to another parent, or reorder it within its current parent. Changes the running app only, not source files.")]
	public static async Task<string> MoveElement(
		McpAgentSession session,
		[Description("Element ID of the view to move")] string elementId,
		[Description("Element ID of the new parent; may be the current parent to reorder")] string parentId,
		[Description("The element's final position among the new parent's children; omit to append")] int? index = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
		[Description("Capture epoch from maui_tree; stale epochs are rejected")] long? captureEpoch = null,
		[Description("Native registry generation from maui_tree")] long? registryGeneration = null)
	{
		using var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.MoveElementAsync(elementId, parentId, index, captureEpoch, registryGeneration);
		return RequireSuccess(result, $"Moved '{elementId}' to '{parentId}'{IndexText(result.Index)}.", $"Failed to move element '{elementId}'.");
	}

	[McpServerTool(Name = "maui_reload_xaml"), Description("Hot reload a page or view without an IDE: re-inflate every live instance of the XAML's x:Class (or one instance) from new XAML text. Pass the XAML inline or a path to a .xaml file on this machine.")]
	public static async Task<string> ReloadXaml(
		McpAgentSession session,
		[Description("Full XAML document including x:Class. Provide this or filePath")] string? xaml = null,
		[Description("Path to a .xaml file to read and reload. Provide this or xaml")] string? filePath = null,
		[Description("Reload only this live instance instead of every instance of the class")] string? elementId = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		if (string.IsNullOrWhiteSpace(xaml) == string.IsNullOrWhiteSpace(filePath))
			throw new McpException("Provide exactly one of xaml or filePath.");
		if (filePath is not null)
		{
			if (!File.Exists(filePath))
				throw new McpException($"File not found: {filePath}");
			xaml = await File.ReadAllTextAsync(filePath);
		}

		using var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.ReloadXamlAsync(xaml!, elementId: elementId, sourceFile: filePath);
		if (result.Success)
			return $"Reloaded {result.Reloaded} live instance(s) of {result.ClassName}.";

		var message = $"XAML reload failed: {result.Error}";
		if (result.Details?.Line is { } line)
			message += $" (line {line}, column {result.Details.Column})";
		throw new McpException(message);
	}

	[McpServerTool(Name = "maui_highlight_element"), Description("Draw a selection outline around an element inside the running app, or clear it. Useful to show a person which element you mean.")]
	public static async Task<string> HighlightElement(
		McpAgentSession session,
		[Description("Element ID to highlight; omit to clear the highlight")] string? elementId = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		using var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.HighlightElementAsync(elementId);
		return McpActionResult.RequireSuccess(
			result,
			elementId is null ? "Cleared highlight." : $"Highlighted '{elementId}'.",
			elementId is null ? "Failed to clear highlight." : $"Failed to highlight '{elementId}'.");
	}

	static string IndexText(int? index) => index is { } i ? $" at index {i}" : "";

	static string RequireSuccess(ElementEditResult result, string successMessage, string failureMessage)
	{
		if (result.Success)
			return successMessage;

		var message = failureMessage;
		if (!string.IsNullOrWhiteSpace(result.Error))
			message += $" {result.Error}";
		if (!string.IsNullOrWhiteSpace(result.Reason))
			message += $" Reason: {result.Reason}.";
		if (result.Reason is "stale-capture-epoch")
			message += " Capture a fresh tree and retry.";
		throw new McpException(message);
	}
}
