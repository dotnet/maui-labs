using System.ComponentModel;
using Microsoft.Maui.Cli.DevFlow.Evidence;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

/// <summary>
/// MCP tools for the on-demand <c>.mauitrace</c> evidence bundle: a small, redacted, shareable
/// snapshot of a bug (structure, diagnostics, bounded logs, network summaries).
/// </summary>
[McpServerToolType]
public sealed class EvidenceTools
{
    [McpServerTool(Name = "maui_evidence_preview"),
     Description("Preview exactly what a DevFlow evidence bundle (.mauitrace) would contain for the running app, WITHOUT writing any file. " +
                 "Returns the included entries with item counts, the excluded entries with reasons, the data classes that are never captured, " +
                 "the applied limits, the screenshot status, the redaction ruleset version, and the path a capture would write to. " +
                 "Always call this before maui_evidence_capture when a human will share the bundle, so they can see what leaves the machine.")]
    public static async Task<CallToolResult> Preview(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
        [Description("Whether the capture would include a screenshot. Screenshots are opt-in because they can show on-screen data. Default: false")] bool includeScreenshot = false,
        [Description("Element id to highlight in the bundle. Only metadata is captured — never element text or property values")] string? elementId = null,
        [Description("Maximum log entries to include (1-500, default 200)")] int? logLimit = null,
        [Description("Maximum network request summaries to include (1-500, default 100)")] int? networkLimit = null,
        [Description("Bundle path a capture would use, echoed back in the plan. Default: ./maui-traces/<app>-<timestamp>.mauitrace under the project root")] string? outputPath = null,
        RequestContext<CallToolRequestParams>? requestContext = null,
        CancellationToken cancellationToken = default)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var appProjectRoot = await session.TryGetAgentProjectRootAsync(agentPort);
        var plan = await EvidenceCapture.PreviewAsync(agent, new EvidenceRequest
        {
            IncludeScreenshot = includeScreenshot,
            SelectedElementId = elementId,
            LogLimit = logLimit ?? EvidenceFormat.DefaultLogLimit,
            NetworkLimit = networkLimit ?? EvidenceFormat.DefaultNetworkLimit,
            OutputPath = outputPath,
            Source = "mcp",
            // The connected app's project root steers the two things that are about the app: which
            // reviewed suppressions apply, and which root its source paths are rewritten against.
            // It deliberately does not steer the destination. This server is started by an editor
            // and routinely runs in a different repository from the app, so the destination stays
            // exactly where it was — the caller's outputPath, or this process's working directory
            // — because moving a tool's default output location is a separate, visible behaviour
            // change that has nothing to do with reading the app's project.
            LayoutPolicyStartPath = appProjectRoot,
            SourcePathRoot = appProjectRoot,
        }, cancellationToken);

        var text = EvidenceJson.Serialize(plan, indented: true);
        return McpAppMetadata.Result(
            text,
            new
            {
                kind = "mauiEvidencePreview",
                instruction = "This is a bounded preview. It does not capture or upload evidence.",
                plan,
            },
            McpAppMetadata.IsNegotiated(requestContext?.Server.ClientCapabilities));
    }

    [McpServerTool(Name = "maui_evidence_capture"),
     Description("Capture a DevFlow evidence bundle (.mauitrace) from the running app and write it atomically to disk. " +
                 "The bundle holds a redacted, bounded snapshot: app/device/platform metadata, the visual tree STRUCTURE (no element text or property values), " +
                 "binding/property problems, recent log entries with secrets and absolute paths scrubbed, and HTTP request summaries without headers, bodies, or query values. " +
                 "Screenshots are opt-in. Preferences, secure storage, geolocation, file contents, and view-model object graphs are never captured. " +
                 "Returns the written path plus the manifest summary describing what was and was not included.")]
    public static async Task<string> Capture(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
        [Description("Bundle path to write. Must end in .mauitrace. Default: ./maui-traces/<app>-<timestamp>.mauitrace under the project root")] string? outputPath = null,
        [Description("Overwrite the file if it already exists. Default: false (the capture fails instead)")] bool overwrite = false,
        [Description("Include a screenshot of the app. Off by default — only enable when the human explicitly agreed, because a screenshot can show on-screen data")] bool includeScreenshot = false,
        [Description("Path to a Markdown (.md) file describing the reproduction steps to embed in the bundle (max 1 MB). Its secrets and absolute paths are scrubbed, but it may still quote text and values from the steps — only attach one the human asked for")] string? workflowFile = null,
        [Description("Element id to highlight in the bundle. Only metadata is captured — never element text or property values")] string? elementId = null,
        [Description("Maximum log entries to include (1-500, default 200)")] int? logLimit = null,
        [Description("Maximum network request summaries to include (1-500, default 100)")] int? networkLimit = null,
        CancellationToken cancellationToken = default)
    {
        string? workflow = null;
        if (!string.IsNullOrWhiteSpace(workflowFile))
        {
            var read = EvidenceCommands.ReadWorkflowFile(workflowFile!);
            if (read.Error is not null)
                return EvidenceJson.Serialize(new EvidenceCaptureResult { Ok = false, Error = read.Error }, indented: true);
            workflow = read.Text;
        }

        using var agent = await session.GetAgentClientAsync(agentPort);
        var appProjectRoot = await session.TryGetAgentProjectRootAsync(agentPort);
        var result = await EvidenceCapture.CaptureAsync(agent, new EvidenceRequest
        {
            IncludeScreenshot = includeScreenshot,
            Overwrite = overwrite,
            OutputPath = outputPath,
            WorkflowMarkdown = workflow,
            SelectedElementId = elementId,
            LogLimit = logLimit ?? EvidenceFormat.DefaultLogLimit,
            NetworkLimit = networkLimit ?? EvidenceFormat.DefaultNetworkLimit,
            Source = "mcp",
            // Policy lookup and source-path rewriting only — see the preview tool above. The
            // bundle's destination is unchanged by the layout layer.
            LayoutPolicyStartPath = appProjectRoot,
            SourcePathRoot = appProjectRoot,
        }, cancellationToken);

        return EvidenceJson.Serialize(result, indented: true);
    }
}
