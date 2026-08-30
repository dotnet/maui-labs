using System.ComponentModel;
using Microsoft.Maui.Cli.DevFlow.Diagnostics;
using Microsoft.Maui.DevFlow.Driver;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class LayoutDiagnosticsTool
{
    [McpServerTool(Name = "maui_layout_diagnostics"),
     Description(
         "Run a single, explicit, read-only layout scan of the running MAUI app and return typed findings. " +
         "Combines managed layout state with capability-gated native and same-origin Blazor evidence. " +
         "Clipping requires an authoritative visible region, text truncation requires live platform text-layout " +
         "state, interaction occlusion reports sampled input routing, and geometric overlap never implies visual " +
         "coverage. Unsupported or opaque evidence is returned as 'incomplete' — never as a pass. " +
         "Read the 'coverage' and 'limitations' fields before drawing conclusions. There is no watch mode: " +
         "call this again after you change the UI.")]
    public static async Task<CallToolResult> GetLayoutDiagnostics(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
        [Description("Restrict the scan to this element id and its descendants")] string? elementId = null,
        [Description("0-based window index (optional; defaults to every window)")] int? window = null,
        [Description("Element budget for the scan (default: 2000, maximum: 5000)")] int? maxElements = null,
        RequestContext<CallToolRequestParams>? requestContext = null)
    {
        if (window is < 0)
            throw new ArgumentOutOfRangeException(nameof(window), "Window must be zero or greater.");
        if (maxElements is < 1 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(maxElements), "Element budget must be between 1 and 5000.");

        using var agent = await session.GetAgentClientAsync(agentPort);
        // Suppression policy belongs to the app under inspection, so it is resolved from the
        // project the selected agent registered with — never from this server's working directory.
        var policyStartPath = await session.TryGetAgentProjectRootAsync(agentPort);
        var report = await LayoutDiagnosticsCoordinator.ScanAsync(
            agent,
            elementId: elementId,
            window: window,
            maxElements: maxElements,
            policyStartPath: policyStartPath);
        var text = report is null
            ? "Layout diagnostics are unavailable on the connected agent, or the requested element does not exist."
            : CliJson.SerializeUntyped(report, indented: false);
        return McpAppMetadata.Result(
            text,
            new
            {
                kind = "mauiLayoutDiagnostics",
                available = report is not null,
                instruction = "Read coverage and limitations before drawing conclusions.",
                summary = report?.Summary,
                scope = report?.Scope,
                coverage = report?.Coverage,
                systemEvidence = report?.SystemEvidence,
                findingCount = report?.Findings.Count ?? 0,
            },
            McpAppMetadata.IsNegotiated(requestContext?.Server.ClientCapabilities));
    }
}
