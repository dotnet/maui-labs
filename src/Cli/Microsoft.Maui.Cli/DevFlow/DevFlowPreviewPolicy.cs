using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow;

internal static class DevFlowPreviewPolicy
{
    private static readonly HashSet<string> SafeDisabledTestAgentRoutes =
    [
        "/api/test-agent/sessions/status",
        "/api/test-agent/sessions/abandon",
        "/api/test-agent/mutations/complete",
        "/api/test-agent/audit",
        "/api/test-agent/runs/validate",
    ];

    internal static bool IsAgentAuthoringEnabled(MauiPreviewFeatureFlags flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        return flags.IsEnabled("agent-authoring");
    }

    internal static bool IsBrokerTestAgentRouteEnabled(
        MauiPreviewFeatureFlags flags,
        string path)
    {
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (IsAgentAuthoringEnabled(flags))
            return true;

        return SafeDisabledTestAgentRoutes.Contains(path.TrimEnd('/').ToLowerInvariant());
    }

    internal static bool IsInspectorAgentRequestRouteEnabled(
        MauiPreviewFeatureFlags flags,
        string path)
    {
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (IsAgentAuthoringEnabled(flags))
            return true;

        var normalized = path.TrimEnd('/');
        if (string.Equals(
                normalized,
                "/api/workbench/agent-requests",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalized.EndsWith("/reject", StringComparison.OrdinalIgnoreCase);
    }
}
