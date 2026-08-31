using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Diagnostics;

internal static class LayoutDiagnosticsCoordinator
{
    public static async Task<LayoutDiagnosticsReport?> ScanAsync(
        AgentClient client,
        string profile = "agent",
        string? elementId = null,
        int? window = null,
        int? maxElements = null,
        string? suppressionMode = null,
        string? policyStartPath = null,
        CancellationToken cancellationToken = default)    {
        var request = CreateRequest(
            profile,
            elementId,
            window,
            maxElements,
            suppressionMode,
            policyStartPath);
        var report = await client.AnalyzeLayoutAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        if (report is not null)
            return report;

        if (!profile.Equals("agent", StringComparison.OrdinalIgnoreCase))
            return null;

        var legacy = await client.GetLayoutDiagnosticsAsync(elementId, window, maxElements)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        legacy?.Coverage.Limitations.Add(
            "This report came from an older compatibility endpoint; active-page scope, native/Blazor evidence, stability, and suppression guarantees may be unavailable.");
        return legacy;
    }

    /// <summary>
    /// Builds the scan request, resolving project suppression policy from
    /// <paramref name="policyStartPath"/> only.
    ///
    /// A null <paramref name="policyStartPath"/> means "no project root is known", and that case
    /// loads the user policy alone. An empty or whitespace value is rejected outright rather than
    /// forwarded, because the loader reads a blank start path as the working directory. It
    /// deliberately does not probe the current working directory: the broker and the MCP server
    /// run wherever an editor launched them, which is frequently a different repository from the
    /// app under inspection, so a CWD probe silently applies one project's reviewed suppressions
    /// to another project's findings. Callers that genuinely are the user's shell — the CLI
    /// command — pass their working directory explicitly.
    ///
    /// The user-wide policy in <c>~/.mauidevflow/layout-diagnostics.json</c> is unaffected: it is
    /// disclosed, machine-scoped, and stays included for every profile except <c>ci</c>.
    /// </summary>
    internal static LayoutInspectionRequest CreateRequest(
        string profile = "agent",
        string? elementId = null,
        int? window = null,
        int? maxElements = null,
        string? suppressionMode = null,
        string? policyStartPath = null)
    {
        // A blank policy root is rejected, not normalized away. Empty or whitespace is a caller
        // mistake, not "no root": the loader reads a blank start path as "probe my working
        // directory", which silently applies an unrelated project's reviewed suppressions to these
        // findings — the exact failure this parameter exists to prevent. Failing loudly at the
        // boundary keeps that bug from reaching a report. Null keeps its documented meaning.
        if (policyStartPath is not null && string.IsNullOrWhiteSpace(policyStartPath))
        {
            throw new ArgumentException(
                "policyStartPath must be a real project root or null; a blank value would resolve to this process's working directory.",
                nameof(policyStartPath));
        }

        var includeUserPolicy = !profile.Equals("ci", StringComparison.OrdinalIgnoreCase);
        var mode = suppressionMode ??
            (profile.Equals("strict", StringComparison.OrdinalIgnoreCase)
                ? LayoutSuppressionModes.Ignore
                : LayoutSuppressionModes.Report);
        var policy = mode.Equals(LayoutSuppressionModes.Off, StringComparison.OrdinalIgnoreCase)
            ? new LayoutDiagnosticsPolicy()
            : policyStartPath is null
                ? includeUserPolicy
                    ? LayoutDiagnosticsPolicyLoader.LoadUserPolicy()
                    : new LayoutDiagnosticsPolicy()
                : LayoutDiagnosticsPolicyLoader.Load(policyStartPath, includeUserPolicy);

        return new LayoutInspectionRequest
        {
            Profile = profile,
            Scope = new LayoutInspectionScope
            {
                Mode = profile is "exhaustive" or "ci"
                    ? LayoutScopeModes.AllWindows
                    : LayoutScopeModes.ActivePage,
                RootElementId = string.IsNullOrWhiteSpace(elementId) ? null : elementId,
                Window = window,
            },
            MaxElements = maxElements,
            SuppressionMode = mode,
            Suppressions = policy.Suppressions.ToList(),
        };
    }
}
