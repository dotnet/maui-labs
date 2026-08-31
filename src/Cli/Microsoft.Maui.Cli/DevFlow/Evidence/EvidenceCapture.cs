using System.Reflection;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Evidence;

/// <summary>
/// The one entry point every surface uses (CLI, MCP, Web Inspector) so preview, capture, and view
/// behave identically no matter who asked. Surfaces differ only in how they render the result.
/// </summary>
internal static class EvidenceCapture
{
    private static string? s_toolVersion;

    public static string ToolVersion =>
        s_toolVersion ??= Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Collects and projects evidence without writing anything.</summary>
    public static async Task<EvidencePlan> PreviewAsync(
        AgentClient client,
        EvidenceRequest request,
        CancellationToken ct = default)
    {
        var utcNow = request.UtcNow ?? DateTime.UtcNow;
        var projectRoot = request.ProjectRoot ?? EvidencePaths.FindProjectRoot(request.ProjectHint);

        var bundle = await EvidenceBuilder.BuildAsync(
            new AgentEvidenceDataSource(client),
            BuildOptions(request, projectRoot, utcNow, previewOnly: true),
            ct);

        var destination = EvidencePaths.ValidateOutputPath(
            request.OutputPath, projectRoot, bundle.Manifest.App?.Name, utcNow);
        bundle.Plan.OutputPath = destination.Path;
        if (destination.Error is not null)
            bundle.Plan.Warnings.Add(destination.Error);

        return bundle.Plan;
    }

    /// <summary>Collects, projects, and atomically writes the bundle to disk.</summary>
    public static async Task<EvidenceCaptureResult> CaptureAsync(
        AgentClient client,
        EvidenceRequest request,
        CancellationToken ct = default)
    {
        var utcNow = request.UtcNow ?? DateTime.UtcNow;
        var projectRoot = request.ProjectRoot ?? EvidencePaths.FindProjectRoot(request.ProjectHint);

        var bundle = await EvidenceBuilder.BuildAsync(
            new AgentEvidenceDataSource(client),
            BuildOptions(request, projectRoot, utcNow, previewOnly: false),
            ct);

        var destination = EvidencePaths.ValidateOutputPath(
            request.OutputPath, projectRoot, bundle.Manifest.App?.Name, utcNow);
        if (!destination.Ok)
            return new EvidenceCaptureResult { Ok = false, Error = destination.Error, Plan = bundle.Plan };

        bundle.Plan.OutputPath = destination.Path;

        var write = EvidenceBundleWriter.Write(bundle, destination.Path!, request.Overwrite);
        if (!write.Ok)
            return new EvidenceCaptureResult { Ok = false, Error = write.Error, Plan = bundle.Plan };

        return new EvidenceCaptureResult
        {
            Ok = true,
            Path = write.Path,
            Bytes = write.Bytes,
            Manifest = bundle.Manifest,
            Plan = bundle.Plan,
        };
    }

    /// <summary>Collects and projects a bundle without touching the filesystem (Inspector download).</summary>
    public static async Task<(EvidenceBundle Bundle, byte[] Bytes)> CaptureToBytesAsync(
        AgentClient client,
        EvidenceRequest request,
        CancellationToken ct = default)
    {
        var utcNow = request.UtcNow ?? DateTime.UtcNow;
        var projectRoot = request.ProjectRoot ?? EvidencePaths.FindProjectRoot(request.ProjectHint);
        var bundle = await EvidenceBuilder.BuildAsync(
            new AgentEvidenceDataSource(client),
            BuildOptions(request, projectRoot, utcNow, previewOnly: false),
            ct);
        return (bundle, EvidenceBundleWriter.ToBytes(bundle));
    }

    /// <summary>
    /// Validates a bundle and regenerates a static HTML report from its parsed contents.
    /// The bundle's own bytes are never rendered or executed.
    /// </summary>
    public static EvidenceViewResult View(
        string bundlePath,
        string? reportPath,
        bool open,
        DateTime? utcNow = null,
        bool overwrite = false)
    {
        var now = utcNow ?? DateTime.UtcNow;

        var input = EvidencePaths.ValidateInputPath(bundlePath);
        if (!input.Ok)
            return new EvidenceViewResult { Ok = false, Error = input.Error, Bundle = bundlePath };

        var read = EvidenceBundleReader.Read(input.Path!);
        if (!read.Ok)
            return new EvidenceViewResult { Ok = false, Error = read.Error, Bundle = input.Path };

        string target;
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            target = EvidencePaths.CreateReportPath(now);
        }
        else
        {
            try
            {
                target = Path.GetFullPath(reportPath!);
                var directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
            {
                return new EvidenceViewResult { Ok = false, Error = "Report path is not a valid file path.", Bundle = input.Path };
            }
        }

        var temporary = target + $".{Guid.NewGuid():N}.tmp";
        try
        {
            if (!overwrite && File.Exists(target))
            {
                return new EvidenceViewResult
                {
                    Ok = false,
                    Error = $"Report file already exists: {target}. Pass --overwrite to replace it.",
                    Bundle = input.Path
                };
            }
            File.WriteAllText(temporary, EvidenceReportRenderer.Render(read));
            File.Move(temporary, target, overwrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!overwrite && File.Exists(target))
            {
                return new EvidenceViewResult
                {
                    Ok = false,
                    Error = $"Report file already exists: {target}. Pass --overwrite to replace it.",
                    Bundle = input.Path
                };
            }
            return new EvidenceViewResult { Ok = false, Error = $"Could not write the report: {ex.Message}", Bundle = input.Path };
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }

        return new EvidenceViewResult
        {
            Ok = true,
            Bundle = input.Path,
            Report = target,
            Opened = open && EvidenceReportLauncher.TryOpen(target),
            Manifest = read.Manifest,
            Entries = read.Entries,
            Warnings = read.Warnings,
        };
    }

    private static EvidenceCaptureOptions BuildOptions(
        EvidenceRequest request,
        string? projectRoot,
        DateTime utcNow,
        bool previewOnly) => new()
        {
            IncludeScreenshot = request.IncludeScreenshot,
            PreviewOnly = previewOnly,
            WorkflowMarkdown = request.WorkflowMarkdown,
            FlowRun = request.FlowRun,
            CheckpointRoute = request.CheckpointRoute,
            CheckpointSavedUtc = request.CheckpointSavedUtc,
            CheckpointLastRestoreKind = request.CheckpointLastRestoreKind,
            SelectedElementId = request.SelectedElementId,
            LogLimit = request.LogLimit,
            NetworkLimit = request.NetworkLimit,
            Source = request.Source,
            ProjectRoot = projectRoot,
            ToolVersion = ToolVersion,
            UtcNow = utcNow,
        };
}

/// <summary>Caller-facing capture request shared by the CLI, MCP tools, and the Web Inspector.</summary>
internal sealed class EvidenceRequest
{
    public bool IncludeScreenshot { get; init; }
    public bool Overwrite { get; init; }
    public string? OutputPath { get; init; }
    public string? WorkflowMarkdown { get; init; }
    /// <summary>Metadata-only flow-run-report linkage for failure evidence.</summary>
    public EvidenceFlowRunLink? FlowRun { get; init; }
    public string? CheckpointRoute { get; init; }
    public DateTimeOffset? CheckpointSavedUtc { get; init; }
    public string? CheckpointLastRestoreKind { get; init; }
    public string? SelectedElementId { get; init; }
    public int LogLimit { get; init; } = EvidenceFormat.DefaultLogLimit;
    public int NetworkLimit { get; init; } = EvidenceFormat.DefaultNetworkLimit;
    public string Source { get; init; } = "cli";
    /// <summary>Known project path or directory (the Inspector passes its --project value).</summary>
    public string? ProjectHint { get; init; }
    /// <summary>Pre-resolved project root; skips discovery when set.</summary>
    public string? ProjectRoot { get; init; }
    public DateTime? UtcNow { get; init; }
}
