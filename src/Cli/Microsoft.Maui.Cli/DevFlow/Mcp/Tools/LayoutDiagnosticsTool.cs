using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class LayoutDiagnosticsTool
{
    [McpServerTool(Name = "maui_layout_diagnostics"),
     Description("Inspect the current rendered MAUI UI for clipped or off-window elements, lost content overflow, text that is not fully rendered, geometric overlap, and visual or interaction occlusion. Use after navigation, XAML/layout edits, window resize, orientation, theme, font-scale, localization, or platform changes. This returns native layout evidence that screenshots cannot provide; use maui_element or maui_tree with the returned IDs for follow-up.")]
    public static async Task<string> Inspect(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
        [Description("Inspection profile: agent (high signal), strict, exhaustive, or ci")] string profile = "agent",
        [Description("Root element ID to inspect (default: the whole realized tree)")] string? rootElementId = null,
        [Description("Comma-separated rule IDs to run, or omit for all rules")] string? checks = null,
        [Description("Minimum severity: info, minor, moderate, serious, or critical")] string minimumSeverity = "minor",
        [Description("Include full geometry, clip-chain, text, and overlap evidence (default: compact agent response)")] bool includeEvidence = false,
        [Description("Maximum findings to return in the agent response (1-500)")] int maxFindings = 100,
        [Description("Wait for stable geometry before analyzing")] bool waitForStable = true,
        [Description("Stability timeout in milliseconds")] int timeoutMs = 2500,
        CancellationToken cancellationToken = default)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var policy = LayoutDiagnosticsPolicyLoader.Load();
        var request = new LayoutInspectionRequest
        {
            Profile = profile,
            MinimumSeverity = minimumSeverity,
            IncludeEvidence = includeEvidence,
            Scope = new LayoutInspectionScope { RootElementId = rootElementId },
            Stability = new LayoutStabilityOptions
            {
                Mode = waitForStable ? "wait" : "immediate",
                TimeoutMs = timeoutMs
            },
            Rules = string.IsNullOrWhiteSpace(checks)
                ? null
                : checks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Suppressions = policy.Suppressions.ToList()
        };
        LayoutInspectionResult? result;
        try
        {
            result = await agent.AnalyzeLayoutAsync(
                request,
                cancellationToken);
        }
        catch (LayoutDiagnosticsException ex)
        {
            throw new McpException(
                ex.ErrorType switch
                {
                    "layout-diagnostics-busy" =>
                        $"{ex.Message} Retry after the active scan completes.",
                    "layout-diagnostics-unavailable"
                        or "layout-diagnostics-not-ready" =>
                        $"{ex.Message} Verify the app is running and the agent port is reachable.",
                    _ => ex.Message
                });
        }
        if (result is null)
        {
            throw new McpException(
                "The connected agent does not advertise ui.layoutDiagnostics. "
                + "Update the in-app DevFlow agent package and enable layout diagnostics.");
        }

        return SerializeResult(result, includeEvidence, profile, maxFindings);
    }

    internal static string SerializeResult(
        LayoutInspectionResult result,
        bool includeEvidence,
        string profile,
        int maxFindings)
    {
        maxFindings = Math.Clamp(maxFindings, 1, 500);
        if (includeEvidence || profile.Equals("exhaustive", StringComparison.OrdinalIgnoreCase))
        {
            result.Findings = result.Findings.Take(maxFindings).ToList();
            return CliJson.SerializeUntyped(result, indented: false);
        }

        var findings = result.Findings
            .Where(finding => !finding.Suppressed && finding.Outcome != "pass")
            .Take(maxFindings)
            .Select(CompactLayoutFinding.From)
            .ToList();
        return CliJson.SerializeUntyped(new CompactLayoutDiagnosticsResult
        {
            SchemaVersion = result.SchemaVersion,
            RuleSetVersion = result.RuleSetVersion,
            Snapshot = result.Snapshot,
            Summary = result.Summary,
            Coverage = new CompactLayoutCoverage
            {
                Overall = result.Coverage.Overall,
                Rules = result.Coverage.Rules,
                OpaqueSubtrees = result.Coverage.OpaqueSubtrees,
                Limitations = result.Coverage.Limitations
            },
            ReturnedFindings = findings.Count,
            Findings = findings
        }, indented: false);
    }
}

internal sealed class CompactLayoutDiagnosticsResult
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = string.Empty;

    [JsonPropertyName("ruleSetVersion")]
    public string RuleSetVersion { get; set; } = string.Empty;

    [JsonPropertyName("snapshot")]
    public LayoutSnapshotInfo Snapshot { get; set; } = new();

    [JsonPropertyName("summary")]
    public LayoutInspectionSummary Summary { get; set; } = new();

    [JsonPropertyName("coverage")]
    public CompactLayoutCoverage Coverage { get; set; } = new();

    [JsonPropertyName("returnedFindings")]
    public int ReturnedFindings { get; set; }

    [JsonPropertyName("findings")]
    public List<CompactLayoutFinding> Findings { get; set; } = [];
}

internal sealed class CompactLayoutCoverage
{
    [JsonPropertyName("overall")]
    public string Overall { get; set; } = string.Empty;

    [JsonPropertyName("rules")]
    public List<LayoutRuleSupportInfo> Rules { get; set; } = [];

    [JsonPropertyName("opaqueSubtrees")]
    public List<LayoutElementReference> OpaqueSubtrees { get; set; } = [];

    [JsonPropertyName("limitations")]
    public List<string> Limitations { get; set; } = [];
}

internal sealed class CompactLayoutFinding
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("suppressionKey")]
    public string SuppressionKey { get; set; } = string.Empty;

    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = string.Empty;

    [JsonPropertyName("subtype")]
    public string? Subtype { get; set; }

    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = string.Empty;

    [JsonPropertyName("actionability")]
    public string Actionability { get; set; } = string.Empty;

    [JsonPropertyName("element")]
    public LayoutElementReference Element { get; set; } = new();

    [JsonPropertyName("relatedElements")]
    public List<LayoutRelatedElement> RelatedElements { get; set; } = [];

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("fixCategories")]
    public List<string> FixCategories { get; set; } = [];

    public static CompactLayoutFinding From(LayoutFinding finding) => new()
    {
        Id = finding.Id,
        SuppressionKey = finding.SuppressionKey,
        RuleId = finding.RuleId,
        Subtype = finding.Subtype,
        Outcome = finding.Outcome,
        Severity = finding.Severity,
        Confidence = finding.Confidence,
        Actionability = finding.Actionability,
        Element = finding.Element,
        RelatedElements = finding.RelatedElements,
        Message = finding.Message,
        FixCategories = finding.FixCategories
    };
}
