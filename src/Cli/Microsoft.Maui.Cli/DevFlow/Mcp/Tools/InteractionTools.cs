using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class InteractionTools
{
	[McpServerTool(Name = "maui_tap"), Description("Tap a UI element by its visual tree ID. Use maui_tree to discover element IDs.")]
	public static async Task<string> Tap(
		McpAgentSession session,
		[Description("Element ID from the visual tree")] string elementId,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
		[Description("Capture epoch from maui_tree or maui_hittest; stale epochs are rejected")] long? captureEpoch = null,
		[Description("Native registry generation from maui_tree or maui_hittest")] long? registryGeneration = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.TapResultAsync(elementId, captureEpoch, registryGeneration);
		return McpActionResult.RequireSuccess(
			result,
			$"Tapped element '{elementId}' successfully.",
			$"Failed to tap element '{elementId}'. Element may not exist or is not tappable.");
	}

	[McpServerTool(Name = "maui_fill"), Description("Fill text into an Entry, Editor, or SearchBar element. Replaces existing text.")]
	public static async Task<string> Fill(
		McpAgentSession session,
		[Description("Element ID from the visual tree")] string elementId,
		[Description("Text to fill into the element")] string text,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
		[Description("Capture epoch from maui_tree or maui_hittest; stale epochs are rejected")] long? captureEpoch = null,
		[Description("Native registry generation from maui_tree or maui_hittest")] long? registryGeneration = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.FillResultAsync(
			elementId,
			text,
			captureEpoch,
			registryGeneration);
		return McpActionResult.RequireSuccess(
			result,
			$"Filled element '{elementId}' with text.",
			$"Failed to fill element '{elementId}'. Element may not exist or is not a text input.");
	}

	[McpServerTool(Name = "maui_clear"), Description("Clear text from an Entry, Editor, or SearchBar element.")]
	public static async Task<string> Clear(
		McpAgentSession session,
		[Description("Element ID from the visual tree")] string elementId,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
		[Description("Capture epoch from maui_tree or maui_hittest; stale epochs are rejected")] long? captureEpoch = null,
		[Description("Native registry generation from maui_tree or maui_hittest")] long? registryGeneration = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.ClearResultAsync(elementId, captureEpoch, registryGeneration);
		return McpActionResult.RequireSuccess(
			result,
			$"Cleared element '{elementId}' successfully.",
			$"Failed to clear element '{elementId}'.");
	}

	[McpServerTool(Name = "maui_key"), Description("Send a key press to an element. Supported keys for Entry/Editor/SearchBar: 'enter' (submit or newline), 'backspace' (delete last character). Use 'text' parameter to type characters. For reliable behavior, provide an element ID; omitting it may have no effect depending on the agent/platform implementation.")]
	public static async Task<string> Key(
		McpAgentSession session,
		[Description("Key to press: 'enter', 'return', 'backspace', 'delete'")] string key,
		[Description("Target element ID. Optional, but omitting it may result in no action; provide an element ID for reliable behavior.")] string? elementId = null,
		[Description("Text to type character by character into the element")] string? text = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
		[Description("Capture epoch from maui_tree or maui_hittest; stale epochs are rejected")] long? captureEpoch = null,
		[Description("Native registry generation from maui_tree or maui_hittest")] long? registryGeneration = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.KeyResultAsync(
			key,
			elementId,
			text,
			captureEpoch,
			registryGeneration);
		var successMessage = elementId is not null
			? $"Sent key '{key}' to element '{elementId}'."
			: $"Sent key '{key}' without a target element; it may have had no effect.";
		return McpActionResult.RequireSuccess(
			result,
			successMessage,
			$"Failed to send key '{key}'. The target element may not support keyboard input, or no target element was provided.");
	}

	private static readonly string[] ValidGestureTypes = ["tap", "doubletap", "longpress", "swipe", "pan", "pinch", "rotate", "actions"];
	private static readonly string[] ValidGestureDirections = ["up", "down", "left", "right"];

	[McpServerTool(Name = "maui_gesture"), Description(
		"Perform a touch gesture on the app. Types: 'pinch' (zoom — use 'scale', e.g. 2.0 to zoom in, 0.5 to zoom out), " +
		"'rotate' (use 'rotation' in degrees), 'pan' (drag — use deltaX/deltaY, or direction + distance), " +
		"'swipe' (flick — requires direction), 'doubletap', 'longpress', 'tap', and 'actions'. " +
		"Actions uses 'sourcesJson', a JSON array of synchronized W3C-style touch pointer tracks. " +
		"Use maui_tap for simple taps and maui_scroll for scrolling a list; this tool is for real gestures. " +
		"Each gesture is first sent to a matching MAUI gesture recognizer on the element or its ancestors, and if there is " +
		"none it is injected natively at the platform view — which is how pinch-to-zoom works on Maps, WebViews and other " +
		"controls that handle gestures internally. Use maui_tree first to find the element ID; the 'gestures' field on each " +
		"element lists the recognizers it has. Omitting elementId aims non-tap gestures at the current page; tap requires an element ID.")]
	public static async Task<string> Gesture(
		McpAgentSession session,
		[Description("Gesture type: 'pinch', 'rotate', 'pan', 'swipe', 'doubletap', 'longpress', 'tap', or 'actions'")] string type,
		[Description("Target element ID from maui_tree. Required for tap; other gestures target the current page when omitted.")] string? elementId = null,
		[Description("Direction for swipe/pan: 'up', 'down', 'left', or 'right'. Required for swipe.")] string? direction = null,
		[Description("Travel distance in device-independent pixels for swipe/pan when using 'direction'. Defaults to 120.")] double? distance = null,
		[Description("Gesture duration in milliseconds. Longer durations read as drags, shorter ones as flicks. Defaults to 200.")] int? durationMs = null,
		[Description("Pinch factor: 2.0 zooms in 2x, 0.5 zooms out by half. Required for pinch; defaults to 1.5.")] double? scale = null,
		[Description("Rotation in degrees, positive is clockwise. Used by 'rotate'; defaults to 90.")] double? rotation = null,
		[Description("Explicit horizontal pan distance in device-independent pixels. Overrides direction/distance.")] double? deltaX = null,
		[Description("Explicit vertical pan distance in device-independent pixels. Overrides direction/distance.")] double? deltaY = null,
		[Description("Gesture focal point X, element-relative from 0 (left) to 1 (right). Defaults to 0.5 (centre).")] double? originX = null,
		[Description("Gesture focal point Y, element-relative from 0 (top) to 1 (bottom). Defaults to 0.5 (centre).")] double? originY = null,
		[Description("Number of interpolation steps between gesture start and end. More steps are smoother. Defaults to 10.")] int? steps = null,
		[Description("For actions: JSON array of synchronized touch sources. Each source has id, pointerType='touch', and actions containing pointerMove, pointerDown, pointerUp, or pause.")] string? sourcesJson = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
		[Description("Capture epoch from maui_tree or maui_hittest; stale epochs are rejected")] long? captureEpoch = null,
		[Description("Native registry generation from maui_tree or maui_hittest")] long? registryGeneration = null)
	{
		var normalizedType = (type ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "").Replace("_", "");
		if (normalizedType == "zoom") normalizedType = "pinch";
		if (normalizedType == "drag") normalizedType = "pan";

		if (Array.IndexOf(ValidGestureTypes, normalizedType) < 0)
			return $"Unsupported gesture type '{type}'. Supported types: {string.Join(", ", ValidGestureTypes)}.";

		var normalizedDirection = direction?.Trim().ToLowerInvariant();
		if (normalizedDirection is not null && Array.IndexOf(ValidGestureDirections, normalizedDirection) < 0)
			return $"Unsupported direction '{direction}'. Supported directions: {string.Join(", ", ValidGestureDirections)}.";

		if (normalizedType == "swipe" && string.IsNullOrEmpty(normalizedDirection))
			return "Swipe gesture requires a 'direction' parameter ('up', 'down', 'left', 'right').";

		if (normalizedType == "tap" && string.IsNullOrWhiteSpace(elementId))
			return "Tap gesture requires an 'elementId'. Use maui_tree or maui_query to resolve the target first.";

		if (normalizedType == "pan" && string.IsNullOrEmpty(normalizedDirection) && deltaX is null && deltaY is null)
			return "Pan gesture requires either a 'direction' or an explicit 'deltaX'/'deltaY' vector.";

		if (normalizedType == "pinch" && scale is <= 0)
			return "Pinch 'scale' must be greater than 0 — use 2.0 to zoom in or 0.5 to zoom out.";

		JsonArray? sources = null;
		if (normalizedType == "actions")
		{
			if (string.IsNullOrWhiteSpace(sourcesJson))
				return "Actions gesture requires 'sourcesJson'.";
			try
			{
				sources = JsonNode.Parse(sourcesJson) as JsonArray;
				if (sources == null)
					return "Invalid sourcesJson: expected a JSON array.";
			}
			catch (JsonException ex)
			{
				return $"Invalid sourcesJson: {ex.Message}";
			}
		}
		else if (!string.IsNullOrWhiteSpace(sourcesJson))
		{
			return "sourcesJson is only valid for the actions gesture.";
		}

		var agent = await session.GetAgentClientAsync(agentPort);
		var result = sources != null
			? await agent.PointerActionsAsync(
				sources, elementId, captureEpoch, registryGeneration)
			: await agent.GestureDetailedAsync(
				normalizedType, elementId, normalizedDirection, distance, durationMs,
				scale, rotation, deltaX, deltaY, originX, originY, steps,
				captureEpoch, registryGeneration);

		var target = elementId is not null ? $" on element '{elementId}'" : " on the current page";

		if (!result.Success)
			return $"Failed to perform {normalizedType} gesture{target}. {result.Error}".TrimEnd();

		// Naming the tier matters: a "recognizer" hit proves the app's own handler ran, while
		// "native" means the platform control absorbed it — different things to assert against.
		var how = result.HandledBy switch
		{
			"action" => result.Detail ?? "handled by the DevFlow action pipeline",
			"recognizer" => $"handled by the app's MAUI gesture recognizer ({result.Detail})",
			"native" => $"injected natively on {result.Platform} ({result.Detail})",
			"scroll" => $"fell back to a scroll ({result.Detail})",
			_ => result.Detail ?? result.HandledBy ?? "handled"
		};
		return $"Performed {normalizedType} gesture{target} — {how}.";
	}

	[McpServerTool(Name = "maui_scroll"), Description("Scroll a ScrollView, CollectionView, or ListView. Supports delta-based scrolling, scrolling to an item index, or scrolling an element into view.")]
	public static async Task<string> Scroll(
		McpAgentSession session,
		[Description("Element ID of the scroll container, or element to scroll into view")] string? elementId = null,
		[Description("Horizontal scroll delta in pixels")] double? x = null,
		[Description("Vertical scroll delta in pixels")] double? y = null,
		[Description("Whether to animate the scroll (default: true)")] bool? animated = null,
		[Description("Window index for multi-window apps")] int? window = null,
		[Description("Item index to scroll to (for CollectionView/ListView)")] int? itemIndex = null,
		[Description("Group index for grouped CollectionView")] int? groupIndex = null,
		[Description("Scroll position: MakeVisible (default), Start, Center, End")] string? scrollToPosition = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
		[Description("Capture epoch from maui_tree or maui_hittest; stale epochs are rejected")] long? captureEpoch = null,
		[Description("Native registry generation from maui_tree or maui_hittest")] long? registryGeneration = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.ScrollResultAsync(
			elementId,
			x ?? 0,
			y ?? 0,
			animated ?? true,
			window,
			itemIndex,
			groupIndex,
			scrollToPosition,
			captureEpoch,
			registryGeneration);
		var successMessage = elementId is not null
			? $"Scrolled element '{elementId}' successfully."
			: "Scrolled successfully.";
		return McpActionResult.RequireSuccess(
			result,
			successMessage,
			$"Failed to scroll element '{elementId}'. Element may not be a ScrollView.");
	}
}
