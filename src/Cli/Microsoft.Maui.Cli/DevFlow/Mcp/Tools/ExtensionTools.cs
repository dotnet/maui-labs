using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class ExtensionTools
{
    [McpServerTool(Name = "maui_extension_list"), Description("List all extensions registered on the connected DevFlow agent.")]
    public static async Task<string> ListExtensions(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        using var client = await session.GetAgentClientAsync(agentPort);
        var extensions = await client.GetExtensionsAsync();
        return CliJson.SerializeUntyped(extensions, indented: false);
    }

    [McpServerTool(Name = "maui_extension_call"), Description("Call an extension tool on the connected DevFlow agent.")]
    public static async Task<string> CallExtension(
        McpAgentSession session,
        [Description("Extension namespace, e.g. com.example.diagnostics")] string extensionNamespace,
        [Description("Tool name within the extension")] string toolName,
        [Description("Optional JSON parameters for the tool")] string? parameters = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        using var client = await session.GetAgentClientAsync(agentPort);
        var extensions = await client.GetExtensionsAsync();
        if (!extensions.TryGetValue(extensionNamespace, out var extension))
            return $"Extension '{extensionNamespace}' not found.";

        var tool = extension.Tools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.Ordinal));
        if (tool == null)
            return $"Tool '{toolName}' not found in extension '{extensionNamespace}'.";

        JsonElement? parameterJson = null;
        if (!string.IsNullOrWhiteSpace(parameters))
            parameterJson = CliJson.ParseElement(parameters);

        return await client.CallExtensionToolAsync(tool.Method, tool.Path, parameterJson);
    }
}
