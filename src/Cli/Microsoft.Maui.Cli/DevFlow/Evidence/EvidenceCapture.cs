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
        await EnsureAgentReachableAsync(client, ct);
        var utcNow = request.UtcNow ?? DateTime.UtcNow;
        var roots = ResolveRoots(request);

        var bundle = await EvidenceBuilder.BuildAsync(
            new AgentEvidenceDataSource(client, request.LayoutPolicyStartPath),
            BuildOptions(request, roots, utcNow, previewOnly: true),
            ct);
        ct.ThrowIfCancellationRequested();

        var destination = EvidencePaths.ValidateOutputPath(
            request.OutputPath, roots.OutputRoot, bundle.Manifest.App?.Name, utcNow);
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
        await EnsureAgentReachableAsync(client, ct);
        var utcNow = request.UtcNow ?? DateTime.UtcNow;
        var roots = ResolveRoots(request);

        var bundle = await EvidenceBuilder.BuildAsync(
            new AgentEvidenceDataSource(client, request.LayoutPolicyStartPath),
            BuildOptions(request, roots, utcNow, previewOnly: false),
            ct);
        ct.ThrowIfCancellationRequested();

        var destination = EvidencePaths.ValidateOutputPath(
            request.OutputPath, roots.OutputRoot, bundle.Manifest.App?.Name, utcNow);
        if (!destination.Ok)
            return new EvidenceCaptureResult { Ok = false, Error = destination.Error, Plan = bundle.Plan };

        bundle.Plan.OutputPath = destination.Path;

        ct.ThrowIfCancellationRequested();
        var write = EvidenceBundleWriter.Write(
            bundle,
            destination.Path!,
            request.Overwrite,
            ct);
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
        await EnsureAgentReachableAsync(client, ct);
        var utcNow = request.UtcNow ?? DateTime.UtcNow;
        var roots = ResolveRoots(request);
        var bundle = await EvidenceBuilder.BuildAsync(
            new AgentEvidenceDataSource(client, request.LayoutPolicyStartPath),
            BuildOptions(request, roots, utcNow, previewOnly: false),
            ct);
        ct.ThrowIfCancellationRequested();
        return (bundle, EvidenceBundleWriter.ToBytes(bundle, ct));
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
            var report = EvidencePaths.ValidateReportPath(reportPath);
            if (!report.Ok)
                return new EvidenceViewResult { Ok = false, Error = report.Error, Bundle = input.Path };
            target = report.Path!;
        }

        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
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
        EvidenceRoots roots,
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
            SourcePathRoot = roots.SourcePathRoot,
            DestinationRoot = roots.OutputRoot,
            ToolVersion = ToolVersion,
            UtcNow = utcNow,
        };

    /// <summary>
    /// The two roots a capture needs, resolved independently.
    ///
    /// <para><b>OutputRoot</b> decides only where a bundle lands when the caller named no
    /// <c>--output</c>. It keeps the behaviour it had before the layout layer existed — the
    /// caller's own <c>ProjectRoot</c>/<c>ProjectHint</c>, otherwise a probe upward from this
    /// process's working directory — because silently relocating a tool's default output is a
    /// visible change a caller cannot see coming.</para>
    ///
    /// <para><b>SourcePathRoot</b> decides only how absolute source paths are rewritten. A broker
    /// or MCP server is started by an editor and routinely runs in a different repository from the
    /// running app, so the working-directory probe is the wrong root for the app's paths: every
    /// path would fall through to the bare-file-name policy and lose the folder structure that
    /// makes a finding locatable. A caller that knows the connected app's project supplies it here
    /// and gets project-relative paths without moving the bundle.</para>
    /// </summary>
    private readonly record struct EvidenceRoots(string? OutputRoot, string? SourcePathRoot);

    private static EvidenceRoots ResolveRoots(EvidenceRequest request)
    {
        var outputRoot = request.ProjectRoot ?? EvidencePaths.FindProjectRoot(request.ProjectHint);
        return new EvidenceRoots(outputRoot, ResolveSourcePathRoot(request.SourcePathRoot, outputRoot));
    }

    /// <summary>
    /// Picks the widest root that still describes the app's checkout.
    ///
    /// <para>Falling back to the destination root keeps every caller that never set the new field
    /// on exactly the behaviour it had: one root, used for both jobs.</para>
    ///
    /// <para>When both are known, the destination root wins <em>only</em> if it already encloses
    /// the app's project. That is the single-repository case — an editor's working directory is the
    /// repository and the agent registered one project inside it — and narrowing to the project
    /// would drop every file in a sibling project, shared library, or linked folder to a bare file
    /// name, which is the same loss this field exists to prevent. When neither encloses the other
    /// the two are separate checkouts, and only the app's own root can relativize its paths.</para>
    ///
    /// <para>"Widest" stops at a checkout. A bare volume root — <c>C:\</c>, <c>/</c>, a bare UNC
    /// share — encloses everything on the machine, so it is discarded rather than used: it would
    /// make every absolute path in the bundle "relative", publishing the user's directory layout
    /// instead of reducing it to a file name. The discard runs before the enclosure test as well,
    /// because a bare destination root encloses any project and would otherwise win outright.
    /// Discarding both leaves the root unset, which is the file-name-only policy.</para>
    /// </summary>
    private static string? ResolveSourcePathRoot(string? requested, string? outputRoot)
    {
        var shareableOutputRoot = EvidenceRedaction.IsShareableSourceRoot(outputRoot)
            ? outputRoot
            : null;
        if (!EvidenceRedaction.IsShareableSourceRoot(requested))
            return shareableOutputRoot;
        return Encloses(shareableOutputRoot, requested!) ? shareableOutputRoot : requested;
    }

    private static bool Encloses(string? outer, string inner)
    {
        if (string.IsNullOrWhiteSpace(outer))
            return false;
        try
        {
            var root = Path.GetFullPath(outer!);
            var candidate = Path.GetFullPath(inner);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(root, candidate, comparison))
                return true;
            var prefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            return candidate.StartsWith(prefix, comparison);
        }
        catch
        {
            // An unparseable root cannot be shown to enclose anything, so the app's own root wins.
            return false;
        }
    }

    private static async Task EnsureAgentReachableAsync(
        AgentClient client,
        CancellationToken ct)
    {
        var status = await client.GetStatusAsync().WaitAsync(ct);
        if (status is null)
        {
            throw new InvalidOperationException(
                $"No DevFlow agent responded at {client.BaseUrl}. "
                + "Start the app or select a reachable agent port.");
        }
    }
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

    /// <summary>
    /// Root that absolute source paths are rewritten against, when the caller knows the app's
    /// project and the destination root does not already describe the same checkout.
    ///
    /// It is deliberately separate from <see cref="ProjectRoot"/>/<see cref="ProjectHint"/>, which
    /// steer the default destination and nothing else. A broker or MCP server started by an editor
    /// runs in a different repository from the app it inspects, so the destination root is the
    /// wrong root for the app's source paths: without this the paths fall back to the bare
    /// file-name policy. Setting it never changes where a bundle is written, and it never narrows
    /// the rewrite: when the destination root already encloses this one — the single-repository
    /// case — the wider root is kept so sibling projects and shared libraries stay relative.
    /// Null keeps the pre-existing behaviour of normalizing against the destination root, and a
    /// bare volume root is treated as null: it encloses the whole machine, so accepting one would
    /// publish the absolute directory layout instead of reducing it to a file name.
    /// </summary>
    public string? SourcePathRoot { get; init; }

    /// <summary>
    /// Project root the layout suppression policy is resolved from. It is deliberately separate
    /// from <see cref="ProjectRoot"/>, which only steers the default destination and may fall back
    /// to a working-directory probe: reading a project's reviewed suppressions must be pinned to
    /// the app under inspection. Null loads the disclosed user-wide policy only.
    /// </summary>
    public string? LayoutPolicyStartPath { get; init; }
    public DateTime? UtcNow { get; init; }
}
