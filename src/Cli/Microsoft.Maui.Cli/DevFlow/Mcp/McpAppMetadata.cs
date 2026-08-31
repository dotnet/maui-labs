using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Microsoft.Maui.Cli.DevFlow.Mcp;

internal static class McpAppMetadata
{
	internal const string ExtensionId = "io.modelcontextprotocol/ui";
	internal const string MimeType = "text/html;profile=mcp-app";
	internal const string ResourceUri = "ui://maui-devflow/compact-view/v1";

	internal static JsonObject ToolMeta(params string[] visibility)
		=> ToJsonObject(new UiEnvelope<ToolUiMetadata>(
			new(ResourceUri, visibility)));

#pragma warning disable MCPEXP001
	internal static bool IsNegotiated(ClientCapabilities? capabilities)
	{
		if (capabilities?.Extensions is null ||
			!capabilities.Extensions.TryGetValue(ExtensionId, out var settings))
		{
			return false;
		}

		var typed = JsonSerializer.Deserialize<UiClientCapabilities>(
			JsonSerializer.Serialize(settings, McpJsonUtilities.DefaultOptions),
			McpJsonUtilities.DefaultOptions);
		return typed?.MimeTypes.Contains(MimeType, StringComparer.Ordinal) == true;
	}
#pragma warning restore MCPEXP001

	internal static CallToolResult Result<T>(
		string text,
		T structured,
		bool includeUi)
		=> new()
		{
			Content = [new TextContentBlock { Text = text }],
			StructuredContent = JsonSerializer.SerializeToElement(
				structured,
				McpJsonUtilities.DefaultOptions),
			Meta = includeUi ? ToolMeta("model", "app") : null,
		};

	private static JsonObject ToJsonObject<T>(T value)
		=> JsonSerializer.SerializeToNode(
			value,
			McpJsonUtilities.DefaultOptions)!.AsObject();

	private sealed record UiClientCapabilities(string[] MimeTypes);

	private sealed record UiEnvelope<T>(T Ui);

	private sealed record ToolUiMetadata(
		string ResourceUri,
		string[] Visibility);

}
