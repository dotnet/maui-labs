using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.DevFlow.Evidence;

// ── manifest.json ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The bundle's self-description: what schema/redaction rules produced it, what it contains,
/// what was deliberately left out, and the bounds that were applied.
/// </summary>
internal sealed class EvidenceManifest
{
    public string Schema { get; set; } = EvidenceFormat.SchemaId;
    public int FormatVersion { get; set; } = EvidenceFormat.Version;
    public int RedactionVersion { get; set; } = EvidenceRedaction.Version;
    public string CapturedUtc { get; set; } = "";
    /// <summary>Which surface produced the bundle: <c>cli</c>, <c>mcp</c>, or <c>inspector</c>.</summary>
    public string Source { get; set; } = "cli";
    public EvidenceToolInfo Tool { get; set; } = new();
    public EvidenceAppInfo? App { get; set; }
    public EvidencePlatformInfo? Platform { get; set; }
    public List<string> Capabilities { get; set; } = [];
    public List<EvidenceEntryInfo> Entries { get; set; } = [];
    public List<EvidenceExclusion> Excluded { get; set; } = [];
    public List<string> NeverIncluded { get; set; } = [];
    public EvidenceCounts Counts { get; set; } = new();
    public EvidenceLimits Limits { get; set; } = new();
    public EvidenceScreenshotStatus Screenshot { get; set; } = new();
    public EvidenceCheckpointInfo? Checkpoint { get; set; }
    /// <summary>Additive link to the structured flow run that produced failure evidence.</summary>
    public EvidenceFlowRunLink? FlowRun { get; set; }
    public string? SelectedElementId { get; set; }
    public List<string> Warnings { get; set; } = [];
}

internal sealed class EvidenceToolInfo
{
    public string Name { get; set; } = "maui devflow evidence";
    public string Version { get; set; } = "";
}

internal sealed class EvidenceAppInfo
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Build { get; set; }
    public string? PackageId { get; set; }
}

internal sealed class EvidencePlatformInfo
{
    public string? Name { get; set; }
    public string? DeviceType { get; set; }
    public string? Idiom { get; set; }
    public string? AgentVersion { get; set; }
    public string? Framework { get; set; }
    public string? FrameworkVersion { get; set; }
}

/// <summary>One included entry, with its size and content hash so a reader can verify integrity.</summary>
internal sealed class EvidenceEntryInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int? Count { get; set; }
    public long Bytes { get; set; }
    public string? Sha256 { get; set; }
}

internal sealed class EvidenceExclusion
{
    public EvidenceExclusion() { }

    public EvidenceExclusion(string name, string reason)
    {
        Name = name;
        Reason = reason;
    }

    public string Name { get; set; } = "";
    public string Reason { get; set; } = "";
}

internal sealed class EvidenceCounts
{
    public int TreeElements { get; set; }
    public int Problems { get; set; }
    public int Logs { get; set; }
    public int NetworkRequests { get; set; }
    public int LayoutFindings { get; set; }
    public int LayoutViolations { get; set; }
    public long WorkflowBytes { get; set; }
    public long ScreenshotBytes { get; set; }
}

internal sealed class EvidenceLimits
{
    public int TreeElements { get; set; } = EvidenceFormat.MaxTreeElements;
    public int TreeDepth { get; set; } = EvidenceFormat.MaxTreeDepth;
    public int Logs { get; set; } = EvidenceFormat.DefaultLogLimit;
    public int Network { get; set; } = EvidenceFormat.DefaultNetworkLimit;
    public int Problems { get; set; } = EvidenceFormat.MaxProblems;
    public int LayoutElements { get; set; } = EvidenceFormat.MaxLayoutElements;
    public int LayoutFindings { get; set; } = EvidenceFormat.MaxLayoutFindings;
    public int LogMessageChars { get; set; } = EvidenceFormat.MaxLogMessageChars;
    public long WorkflowBytes { get; set; } = EvidenceFormat.MaxWorkflowBytes;
}

internal sealed class EvidenceScreenshotStatus
{
    /// <summary>True when the caller explicitly opted in.</summary>
    public bool Requested { get; set; }
    public bool Included { get; set; }
    public string? OmittedReason { get; set; }
}

/// <summary>
/// Metadata-only linkage to <c>flow-run.json</c>. It does not add an archive entry and does not
/// retain flow input values, command payloads, or screenshots.
/// </summary>
internal sealed class EvidenceFlowRunLink
{
    public string? RunId { get; set; }
    public string? FailedStepId { get; set; }
    public string? FailureCode { get; set; }
    public string? ReportDigest { get; set; }
    public string? ReportPath { get; set; }
    public string? ReportReference { get; set; }
    public string? CaptureCompleteness { get; set; }
}

// ── environment.json ─────────────────────────────────────────────────────────────────────────

internal sealed class EvidenceEnvironment
{
    public string CapturedUtc { get; set; } = "";
    public EvidenceAppInfo? App { get; set; }
    public EvidencePlatformInfo? Platform { get; set; }
    public EvidenceDeviceInfo? Device { get; set; }
    public EvidenceDisplayInfo? Display { get; set; }
    public List<string> Capabilities { get; set; } = [];
    public string? Route { get; set; }
    public EvidenceCheckpointInfo? Checkpoint { get; set; }
}

/// <summary>Privacy-projected resume metadata; route query values are always scrubbed.</summary>
internal sealed class EvidenceCheckpointInfo
{
    public bool Saved { get; set; }
    public string? Route { get; set; }
    public string? SavedUtc { get; set; }
    public string? LastRestoreKind { get; set; }
}

/// <summary>
/// A safe subset of the agent's device report. Device <em>name</em> is deliberately omitted —
/// it is frequently personal ("Alex's iPhone").
/// </summary>
internal sealed class EvidenceDeviceInfo
{
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Platform { get; set; }
    public string? OsVersion { get; set; }
    public string? Idiom { get; set; }
    public string? DeviceType { get; set; }
    public string? Architecture { get; set; }
}

internal sealed class EvidenceDisplayInfo
{
    public double? Width { get; set; }
    public double? Height { get; set; }
    public double? Density { get; set; }
    public string? Orientation { get; set; }
    public string? Rotation { get; set; }
    public double? RefreshRate { get; set; }
}

// ── tree.json ────────────────────────────────────────────────────────────────────────────────

internal sealed class EvidenceTreeDocument
{
    public int Count { get; set; }
    public bool Truncated { get; set; }
    public int MaxDepth { get; set; }
    public List<EvidenceTreeNode> Roots { get; set; } = [];
}

/// <summary>
/// Structural metadata for one element. Text, Value, native/framework property dictionaries and
/// absolute source paths are intentionally absent — see <see cref="EvidenceBuilder"/>.
/// </summary>
internal sealed class EvidenceTreeNode
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Framework { get; set; }
    public string? AutomationId { get; set; }
    public string? Role { get; set; }
    public bool Visible { get; set; }
    public bool Enabled { get; set; }
    public bool Focused { get; set; }
    public bool? Selected { get; set; }
    public EvidenceBounds? Bounds { get; set; }
    public string? SourceFile { get; set; }
    public int? SourceLine { get; set; }
    public int? SourceColumn { get; set; }
    public string? SourceHash { get; set; }
    public int ChildCount { get; set; }
    public List<EvidenceTreeNode>? Children { get; set; }
}

internal sealed class EvidenceBounds
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

// ── layout.json ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The evidence-safe projection of a layout diagnostics report: rule outcomes, coverage, and
/// element identity/geometry. Findings reference elements by id, type, automation id, and
/// project-relative source location — never by text, value, or property dictionary.
/// </summary>
internal sealed class EvidenceLayoutDocument
{
    public string SchemaVersion { get; set; } = "";
    public string RuleSetVersion { get; set; } = "";
    public string? CapturedUtc { get; set; }
    public string? Platform { get; set; }
    public string? SnapshotId { get; set; }
    public string? TreeRevision { get; set; }
    public string? DiagnosticsRevision { get; set; }
    public bool Stable { get; set; }
    public int ElementsExamined { get; set; }
    public bool Truncated { get; set; }
    public int Violations { get; set; }
    public int Observations { get; set; }
    public int Incomplete { get; set; }
    public int Passes { get; set; }
    public int NotApplicable { get; set; }
    public int Suppressed { get; set; }
    public string Coverage { get; set; } = "unavailable";
    public int FindingCount { get; set; }
    public bool FindingsTruncated { get; set; }
    public List<EvidenceLayoutRule> Rules { get; set; } = [];
    public List<EvidenceLayoutFinding> Findings { get; set; } = [];
    public List<string> Limitations { get; set; } = [];
    public List<string> NeverCaptured { get; set; } = [];
    public EvidenceLayoutSystemEvidence? SystemEvidence { get; set; }
}

internal sealed class EvidenceLayoutSystemEvidence
{
    public string Status { get; set; } = "unavailable";
    public string? DeviceId { get; set; }
    public string? CapturedUtc { get; set; }
    public double? CaptureSkewMs { get; set; }
    public bool GeometryStable { get; set; }
    public string? ForegroundOwner { get; set; }
    public bool? KeyboardVisible { get; set; }
    public bool ScreenshotCaptured { get; set; }
    public string? ScreenshotDigest { get; set; }
    public int ExternalElementCount { get; set; }
    public List<string> Limitations { get; set; } = [];
}

internal sealed class EvidenceLayoutRule
{
    public string RuleId { get; set; } = "";
    public string Support { get; set; } = "unavailable";
    public string Confidence { get; set; } = "medium";
    public int Evaluated { get; set; }
    public int Skipped { get; set; }
}

internal sealed class EvidenceLayoutFinding
{
    public string Id { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string Outcome { get; set; } = "observation";
    public string Confidence { get; set; } = "medium";
    public string Severity { get; set; } = "info";
    public string Actionability { get; set; } = "review";
    public string? SuppressionKey { get; set; }
    public bool Suppressed { get; set; }
    public string? SuppressionReason { get; set; }
    public string Message { get; set; } = "";
    public string Explanation { get; set; } = "";
    public string? ElementId { get; set; }
    public string? ElementType { get; set; }
    public string? AutomationId { get; set; }
    public string? SourceFile { get; set; }
    public int? SourceLine { get; set; }
    public int? SourceColumn { get; set; }
    public EvidenceBounds? Bounds { get; set; }
    public List<string> RelatedElementIds { get; set; } = [];
    public List<string> FixCategories { get; set; } = [];
    public List<string> Limitations { get; set; } = [];
}

// ── problems.json ────────────────────────────────────────────────────────────────────────────

internal sealed class EvidenceProblemDocument
{
    public bool Enabled { get; set; }
    public long Revision { get; set; }
    public int Count { get; set; }
    public long Evicted { get; set; }
    public List<EvidenceProblem> Problems { get; set; } = [];
}

internal sealed class EvidenceProblem
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Severity { get; set; } = "";
    public string? Code { get; set; }
    public string Message { get; set; } = "";
    public int Count { get; set; }
    public string? FirstSeenUtc { get; set; }
    public string? LastSeenUtc { get; set; }
    public string? ElementId { get; set; }
    public string? ElementType { get; set; }
    public string? Property { get; set; }
    public string? BindingType { get; set; }
    public string? BindingPath { get; set; }
    public string? BindingMode { get; set; }
    public string? SourceType { get; set; }
    public string? ConverterType { get; set; }
    public string? SourceFile { get; set; }
    public int? SourceLine { get; set; }
    public int? SourceColumn { get; set; }
}

// ── logs.json ────────────────────────────────────────────────────────────────────────────────

internal sealed class EvidenceLogDocument
{
    public int Count { get; set; }
    public int Limit { get; set; }
    public bool Truncated { get; set; }
    public List<EvidenceLogEntry> Entries { get; set; } = [];
}

internal sealed class EvidenceLogEntry
{
    public string? Timestamp { get; set; }
    public string? Level { get; set; }
    public string? Category { get; set; }
    public string Message { get; set; } = "";
    public string? Exception { get; set; }
    public string? Source { get; set; }
}

// ── network.json ─────────────────────────────────────────────────────────────────────────────

internal sealed class EvidenceNetworkDocument
{
    public int Count { get; set; }
    public int Limit { get; set; }
    public List<EvidenceNetworkEntry> Requests { get; set; } = [];
}

/// <summary>
/// Summary metadata only: no headers, no bodies, and no query-string VALUES (parameter names are
/// kept because they describe the call shape without carrying secrets).
/// </summary>
internal sealed class EvidenceNetworkEntry
{
    public int Sequence { get; set; }
    public string? Timestamp { get; set; }
    public string Method { get; set; } = "";
    public string? Host { get; set; }
    public string? Path { get; set; }
    public List<string>? QueryKeys { get; set; }
    public int? StatusCode { get; set; }
    public string? StatusText { get; set; }
    public long DurationMs { get; set; }
    public long? RequestBytes { get; set; }
    public long? ResponseBytes { get; set; }
    public string? RequestContentType { get; set; }
    public string? ResponseContentType { get; set; }
    public string? Error { get; set; }
}

// ── preview / plan ───────────────────────────────────────────────────────────────────────────

/// <summary>
/// The "what will be shared" contract. Rendered before any bundle leaves the machine —
/// the Inspector confirmation dialog, <c>evidence preview</c>, and the MCP preview tool all
/// present this exact object.
/// </summary>
internal sealed class EvidencePlan
{
    public bool Ok { get; set; } = true;
    public string Schema { get; set; } = EvidenceFormat.SchemaId;
    public int FormatVersion { get; set; } = EvidenceFormat.Version;
    public int RedactionVersion { get; set; } = EvidenceRedaction.Version;
    public string Source { get; set; } = "cli";
    public string GeneratedUtc { get; set; } = "";
    public EvidenceAppInfo? App { get; set; }
    public EvidencePlatformInfo? Platform { get; set; }
    public List<EvidenceEntryInfo> Included { get; set; } = [];
    public List<EvidenceExclusion> Excluded { get; set; } = [];
    public List<string> NeverIncluded { get; set; } = [];
    public EvidenceScreenshotStatus Screenshot { get; set; } = new();
    public EvidenceCounts Counts { get; set; } = new();
    public EvidenceLimits Limits { get; set; } = new();
    public List<string> Warnings { get; set; } = [];
    public string SuggestedFileName { get; set; } = "";
    /// <summary>Where <c>evidence capture</c> would write with the current options (CLI/MCP only).</summary>
    public string? OutputPath { get; set; }
    public long EstimatedBytes { get; set; }
    public string? SelectedElementId { get; set; }
}

// ── command results ──────────────────────────────────────────────────────────────────────────

internal sealed class EvidenceCaptureResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? Path { get; set; }
    public long Bytes { get; set; }
    public EvidenceManifest? Manifest { get; set; }
    public EvidencePlan? Plan { get; set; }
}

internal sealed class EvidenceViewResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? Bundle { get; set; }
    public string? Report { get; set; }
    public bool Opened { get; set; }
    public EvidenceManifest? Manifest { get; set; }
    public List<string> Entries { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>Web Inspector preview envelope (mirrors the other <c>/api/*</c> responses).</summary>
internal sealed class EvidencePreviewResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public EvidencePlan? Plan { get; set; }
}

// ── serialization ────────────────────────────────────────────────────────────────────────────

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EvidenceManifest))]
[JsonSerializable(typeof(EvidenceFlowRunLink))]
[JsonSerializable(typeof(EvidenceEnvironment))]
[JsonSerializable(typeof(EvidenceTreeDocument))]
[JsonSerializable(typeof(EvidenceLayoutDocument))]
[JsonSerializable(typeof(EvidenceProblemDocument))]
[JsonSerializable(typeof(EvidenceLogDocument))]
[JsonSerializable(typeof(EvidenceNetworkDocument))]
[JsonSerializable(typeof(EvidencePlan))]
[JsonSerializable(typeof(EvidenceCaptureResult))]
[JsonSerializable(typeof(EvidenceViewResult))]
[JsonSerializable(typeof(EvidencePreviewResponse))]
internal sealed partial class EvidenceJsonContext : JsonSerializerContext;

/// <summary>Source-generated (AOT-safe) JSON helpers for every evidence payload.</summary>
internal static class EvidenceJson
{
    // The visual tree nests two JSON levels per element level, so the serializer depth must stay
    // comfortably above 2 × EvidenceFormat.MaxTreeDepth on both the write and read paths —
    // otherwise a deep (but legitimate) tree would silently fail to serialize.
    internal const int MaxJsonDepth = 256;

    private static readonly JsonSerializerOptions Compact =
        new(EvidenceJsonContext.Default.Options) { MaxDepth = MaxJsonDepth };
    private static readonly JsonSerializerOptions Pretty =
        new(EvidenceJsonContext.Default.Options) { WriteIndented = true, MaxDepth = MaxJsonDepth };

    public static string Serialize<T>(T value, bool indented = false)
        => JsonSerializer.Serialize(value, typeof(T), indented ? Pretty : Compact);

    public static byte[] SerializeToUtf8<T>(T value, bool indented = true)
        => JsonSerializer.SerializeToUtf8Bytes(value, typeof(T), indented ? Pretty : Compact);

    public static T? Deserialize<T>(string json) where T : class
        => (T?)JsonSerializer.Deserialize(json, typeof(T), Compact);
}
