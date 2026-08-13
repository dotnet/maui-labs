using GitHub.Copilot;

namespace Microsoft.Maui.CopilotSdk;

/// <summary>
/// The default, safe permission policy used when the caller does not supply
/// <see cref="CopilotSdkConfiguration.PermissionHandler"/>.
/// </summary>
/// <remarks>
/// The policy approves <em>only</em> tool invocations that target one of the caller-supplied tools
/// (matched by name). Every other request — file writes, shell commands, network access, memory
/// writes, extension management, and so on — is denied. This guarantees the adapter never grants
/// blanket ambient file or shell access.
/// </remarks>
internal static class CopilotSafePermissionPolicy
{
    public static CopilotSdkPermissionHandler Create(IReadOnlySet<string> allowedToolNames)
    {
        return (request, _) =>
        {
            var toolName = request is PermissionRequestCustomTool custom
                ? custom.ToolName
                : null;
            var decision = toolName is not null && allowedToolNames.Contains(toolName)
                ? CopilotSdkPermissionDecision.Approve
                : CopilotSdkPermissionDecision.Deny;
            return new ValueTask<CopilotSdkPermissionDecision>(decision);
        };
    }

}
