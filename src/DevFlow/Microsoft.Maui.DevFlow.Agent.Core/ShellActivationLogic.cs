using Microsoft.Maui.Controls;

namespace Microsoft.Maui.DevFlow.Agent.Core;

internal static class ShellActivationLogic
{
    internal static string NormalizeShellRoute(string? route)
        => string.IsNullOrWhiteSpace(route)
            ? string.Empty
            : route.Trim().StartsWith("//", StringComparison.Ordinal)
                ? route.Trim()
                : "//" + route.Trim().TrimStart('/');

    internal static string? ResolveShellItemRoute(ShellItem item)
    {
        var route = item is BaseShellItem bsi ? NormalizeShellRoute(bsi.Route) : string.Empty;
        return string.IsNullOrWhiteSpace(route) ? null : route;
    }

    internal static string? ResolveShellSectionRoute(ShellSection section)
    {
        var route = NormalizeShellRoute(section.Route);
        return string.IsNullOrWhiteSpace(route) ? null : route;
    }
}
