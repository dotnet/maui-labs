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

    /// <summary>
    /// Whether the optional Mobile Canvas device layer may be advertised to a model.
    /// <para>
    /// This is the CLI's half of the same decision the VS Code extension makes with
    /// <c>mauiDevflow.registerMobileCanvasMcpServer</c>. Both are off by default and for the same
    /// reason: the companion is a separately installed, experimental binary that neither surface
    /// ships, so offering its tools unasked advertises a capability that is usually not present —
    /// and an absent tool in the inventory costs every other tool some of the model's attention.
    /// </para>
    /// </summary>
    internal static bool IsMobileCanvasEnabled(MauiPreviewFeatureFlags flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        return flags.IsEnabled("mobile-canvas");
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
