using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;
using System.Security.Cryptography;

namespace Microsoft.Maui.Cli.DevFlow.Evidence;

/// <summary>Optional CLI adapter for failure-only workflow evidence. The Flows layer only knows its callback contract.</summary>
internal sealed class FlowReplayEvidenceCapture : IFlowRunEvidenceCapture
{
    private readonly AgentClient _client;
    private readonly string? _outputPath;
    private readonly string? _projectHint;
    private readonly string _source;
    private readonly bool _includeScreenshot;

    public FlowReplayEvidenceCapture(
        AgentClient client,
        string? outputPath,
        string? projectHint,
        string source,
        bool includeScreenshot = false)
    {
        _client = client;
        _outputPath = outputPath;
        _projectHint = projectHint;
        _source = source;
        _includeScreenshot = includeScreenshot;
    }

    public string? CapturedPath { get; private set; }
    public MauiFlowRunEvidenceContext? CapturedRunContext { get; private set; }
    public MauiFlowArtifactReference? CapturedArtifact { get; private set; }

    /// <summary>
    /// The evidence kinds the last bundle actually contains, read from the written manifest rather
    /// than from what was requested — an entry that was excluded by policy or by a device error
    /// must not be reported as collected.
    /// </summary>
    public IReadOnlyCollection<string> CapturedEvidenceKinds { get; private set; } = [];

    public async Task CaptureOnFailureAsync(
        MauiFlow flow,
        FlowStep failedStep,
        FlowStepResult result,
        CancellationToken cancellationToken)
    {
        if (CapturedPath is not null)
            return;
        var captured = await CaptureAsync(flow, null, cancellationToken);
        if (captured.Ok)
            SetCaptured(captured, null);
    }

    public async Task CaptureOnRunFailureAsync(
        MauiFlowRunEvidenceContext context,
        CancellationToken cancellationToken)
    {
        if (CapturedPath is not null)
            return;
        CapturedRunContext = context;
        var captured = await CaptureAsync(context.Flow, context, cancellationToken);
        if (captured.Ok)
            SetCaptured(captured, context);
    }

    private Task<EvidenceCaptureResult> CaptureAsync(
        MauiFlow flow,
        MauiFlowRunEvidenceContext? context,
        CancellationToken cancellationToken)
        => EvidenceCapture.CaptureAsync(_client, new EvidenceRequest
        {
            // Screenshots stay an explicit opt-in: raw pixels are never redacted, so only a caller
            // that asked for them gets them. Everything else follows the normal redaction policy.
            IncludeScreenshot = _includeScreenshot,
            OutputPath = _outputPath,
            ProjectHint = _projectHint,
            Source = _source,
            WorkflowMarkdown = FlowMarkdown.Serialize(flow),
            FlowRun = context is null ? null : new EvidenceFlowRunLink
            {
                RunId = context.Report.RunId,
                FailedStepId = context.Report.DivergenceStepId,
                FailureCode = context.Report.Failure?.Code,
                ReportDigest = context.ReportDigest,
                ReportPath = context.ReportPath,
                ReportReference = context.ReportPath is null
                    ? $"run:{context.Report.RunId}"
                    : $"flow-run:{context.Report.RunId}",
                CaptureCompleteness = _includeScreenshot
                    ? "failure-only-redacted+screenshot"
                    : "failure-only-redacted",
            }
        }, cancellationToken);

    private void SetCaptured(EvidenceCaptureResult captured, MauiFlowRunEvidenceContext? context)
    {
        CapturedPath = captured.Path;
        CapturedEvidenceKinds = DescribeCapturedKinds(captured);
        if (string.IsNullOrWhiteSpace(captured.Path))
            return;

        string? digest = null;
        try
        {
            digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(captured.Path))).ToLowerInvariant();
        }
        catch
        {
            // The evidence path itself remains useful even if a concurrent cleanup prevented a
            // post-write digest read.
        }
        CapturedArtifact = new MauiFlowArtifactReference
        {
            ArtifactId = $"evidence-{context?.Report.RunId ?? "replay"}",
            Kind = "mauitrace",
            Path = captured.Path,
            Digest = digest,
            MediaType = "application/vnd.maui.evidence+zip",
            Redacted = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Maps the manifest entries that were written into the neutral evidence-kind vocabulary the
    /// run report speaks. A missing manifest yields nothing, because an unverifiable claim of
    /// collection is worse than an honest gap.
    /// </summary>
    private static IReadOnlyCollection<string> DescribeCapturedKinds(EvidenceCaptureResult captured)
    {
        if (!captured.Ok || captured.Manifest is null)
            return [];

        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in captured.Manifest.Entries)
        {
            switch (entry.Name)
            {
                case EvidenceFormat.TreeEntry:
                    kinds.Add(MauiFlowEvidenceKinds.VisualTree);
                    break;
                case EvidenceFormat.LogsEntry:
                    kinds.Add(MauiFlowEvidenceKinds.Logs);
                    break;
                case EvidenceFormat.ScreenshotEntry:
                    kinds.Add(MauiFlowEvidenceKinds.Screenshot);
                    break;
            }
        }
        if (captured.Manifest.Screenshot.Included != true)
            kinds.Remove(MauiFlowEvidenceKinds.Screenshot);
        return kinds;
    }
}
