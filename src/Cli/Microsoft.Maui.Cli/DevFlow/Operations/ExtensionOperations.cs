using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed class ExtensionOperations(AgentClient client)
{
    internal Task<Dictionary<string, ExtensionDescriptor>> ListAsync() =>
        client.GetExtensionsAsync();

    internal async Task<ExtensionDescriptor?> DescribeAsync(string ns)
    {
        var extensions = await client.GetExtensionsAsync();

        return extensions.GetValueOrDefault(ns);
    }

    internal async Task<(string? Result, string? Error)> CallAsync(string ns, string toolName, string? parameters)
    {
        var extension = await DescribeAsync(ns);
        if (extension is null)
            return (null, $"Extension '{ns}' was not found.");

        var tool = extension.Tools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.Ordinal));
        if (tool is null)
            return (null, $"Tool '{toolName}' was not found in extension '{ns}'.");

        JsonElement? parameterJson = string.IsNullOrWhiteSpace(parameters) ? null : CliJson.ParseElement(parameters);

        return (await client.CallExtensionToolAsync(tool.Method, tool.Path, parameterJson), null);
    }
}
