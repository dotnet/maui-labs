using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

public static class LayoutDiagnosticRules
{
    public const string ElementClipped = "layout.element-clipped";
    public const string ElementOutsideWindow = "layout.element-outside-window";
    public const string ContentOverflow = "layout.content-overflow";
    public const string TextNotFullyRendered = "layout.text-not-fully-rendered";
    public const string InteractionOccluded = "layout.interaction-occluded";
    public const string VisualOccluded = "layout.visual-occluded";
    public const string GeometricOverlap = "layout.geometric-overlap";
    public const string AccessibilityVisibilityMismatch = "layout.accessibility-visibility-mismatch";
    public const string VisibleZeroArea = "layout.visible-zero-area";
    public const string ConstraintViolation = "layout.constraint-violation";
    public const string OutsideWindow = ElementOutsideWindow;
    public const string DesiredSizeConstrained = "layout.desired-size-constrained";
    public const string ChildOutsideParent = "layout.child-outside-parent";
}

public static class LayoutScopeModes
{
    public const string ActivePage = "activePage";
    public const string AllWindows = "allWindows";
}

public static class LayoutSuppressionModes
{
    public const string Report = "report";
    public const string Ignore = "ignore";
    public const string Off = "off";
}

/// <summary>
/// Wire versions this client sends and understands.
///
/// The version a request declares and the version a response carries are deliberately different
/// numbers. A newer client is routinely pointed at an older app: the agent ships inside the app
/// under inspection, while the Driver, CLI, MCP server, and Inspector ship with the tooling and are
/// updated independently. An agent validates the declared request version and rejects an unknown
/// one with HTTP 400, so declaring the newest version would make every scan against an app built
/// against the previous package fail outright.
/// </summary>
public static class LayoutDiagnosticsWire
{
    /// <summary>
    /// Version every request declares. <c>2.0</c> is the newest version that both <c>2.0</c> and
    /// <c>2.1</c> agents accept, and <c>2.1</c> added nothing a request can express: the change
    /// removed response fields and redefined <c>suppressionKey</c>. Declaring <c>2.1</c> would buy
    /// no capability and would be rejected by a <c>2.0</c> agent.
    /// </summary>
    public const string RequestSchemaVersion = "2.0";

    /// <summary>Newest response version this client understands in full.</summary>
    public const string ResponseSchemaVersion = "2.1";

    /// <summary>Response versions this client can read without losing meaning.</summary>
    public static readonly IReadOnlyList<string> UnderstoodResponseSchemaVersions =
        ["1.0", "2.0", "2.1"];

    /// <summary>
    /// True when a report's declared version is one this client understands. A <c>2.0</c> response
    /// may still carry the text members <c>2.1</c> removed; this client never reads them, so an
    /// older response is understood, not trusted for text.
    /// </summary>
    public static bool IsUnderstoodResponseSchemaVersion(string? schemaVersion)
        => schemaVersion is not null &&
            UnderstoodResponseSchemaVersions.Contains(schemaVersion, StringComparer.OrdinalIgnoreCase);
}

public class LayoutInspectionRequest
{
    /// <summary>
    /// Payload shape version this request declares. Defaults to
    /// <see cref="LayoutDiagnosticsWire.RequestSchemaVersion"/>, the newest version every shipped
    /// agent accepts, so a current client keeps working against an app built against the previous
    /// package. Responses may declare <c>2.1</c>, which this client understands.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = LayoutDiagnosticsWire.RequestSchemaVersion;

    [JsonPropertyName("scope")]
    public LayoutInspectionScope Scope { get; set; } = new();

    [JsonPropertyName("profile")]
    public string Profile { get; set; } = "agent";

    [JsonPropertyName("rules")]
    public List<string>? Rules { get; set; }

    [JsonPropertyName("minimumSeverity")]
    public string MinimumSeverity { get; set; } = "info";

    [JsonPropertyName("includeEvidence")]
    public bool IncludeEvidence { get; set; } = true;

    [JsonPropertyName("includePasses")]
    public bool IncludePasses { get; set; }

    [JsonPropertyName("stability")]
    public LayoutStabilityOptions Stability { get; set; } = new();

    [JsonPropertyName("occlusion")]
    public LayoutOcclusionOptions Occlusion { get; set; } = new();

    [JsonPropertyName("privacy")]
    public LayoutPrivacyOptions Privacy { get; set; } = new();

    [JsonPropertyName("suppressions")]
    public List<LayoutSuppression> Suppressions { get; set; } = [];

    [JsonPropertyName("suppressionMode")]
    public string SuppressionMode { get; set; } = LayoutSuppressionModes.Report;

    [JsonPropertyName("elementId")]
    public string? ElementId { get; set; }

    [JsonPropertyName("window")]
    public int? Window { get; set; }

    [JsonPropertyName("maxElements")]
    public int? MaxElements { get; set; }
}

public sealed class LayoutDiagnosticsRequest : LayoutInspectionRequest
{
}

public class LayoutInspectionScope
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = LayoutScopeModes.ActivePage;

    [JsonPropertyName("rootElementId")]
    public string? RootElementId { get; set; }

    [JsonPropertyName("window")]
    public int? Window { get; set; }

    [JsonPropertyName("includeDescendants")]
    public bool IncludeDescendants { get; set; } = true;

    [JsonPropertyName("includeNativeElements")]
    public bool IncludeNativeElements { get; set; } = true;

    [JsonPropertyName("includeBlazorElements")]
    public bool IncludeBlazorElements { get; set; } = true;

    [JsonPropertyName("maxDepth")]
    public int MaxDepth { get; set; }
}

public class LayoutStabilityOptions
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "wait";

    [JsonPropertyName("stableFrames")]
    public int StableFrames { get; set; } = 2;

    [JsonPropertyName("quietPeriodMs")]
    public int QuietPeriodMs { get; set; } = 100;

    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; set; } = 2500;

    [JsonPropertyName("allowActiveAnimations")]
    public bool AllowActiveAnimations { get; set; }
}

public class LayoutOcclusionOptions
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "interactiveTargets";

    [JsonPropertyName("maxSamplesPerElement")]
    public int MaxSamplesPerElement { get; set; } = 81;

    [JsonPropertyName("coverageError")]
    public double CoverageError { get; set; } = 0.05;

    [JsonPropertyName("minimumOverlapRatio")]
    public double MinimumOverlapRatio { get; set; } = 0.02;
}

public class LayoutPrivacyOptions
{
    /// <summary>
    /// The only accepted value is <c>none</c>. This subsystem never reads element text or values,
    /// so a request that asks for text length or content is rejected rather than silently
    /// downgraded — a knob that cannot be honoured must not look like one that can.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "none";
}

public class LayoutSuppression
{
    [JsonPropertyName("ruleId")]
    public string? RuleId { get; set; }

    [JsonPropertyName("elementId")]
    public string? ElementId { get; set; }

    [JsonPropertyName("automationId")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("elementType")]
    public string? ElementType { get; set; }

    [JsonPropertyName("relatedElementId")]
    public string? RelatedElementId { get; set; }

    [JsonPropertyName("relatedAutomationId")]
    public string? RelatedAutomationId { get; set; }

    [JsonPropertyName("sourceFile")]
    public string? SourceFile { get; set; }

    [JsonPropertyName("sourceLineStart")]
    public int? SourceLineStart { get; set; }

    [JsonPropertyName("sourceLineEnd")]
    public int? SourceLineEnd { get; set; }

    [JsonPropertyName("fingerprint")]
    public string? Fingerprint { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// Client-side view of the agent's on-demand layout diagnostics report
/// (<c>GET|POST /api/v1/ui/diagnostics/layout</c>).
///
/// The report is deliberately narrow and truthful: it describes managed MAUI layout state only.
/// It never asserts clipping, occlusion, text truncation, or an accessibility mismatch, and an
/// absent finding is never a guarantee of correctness — see <see cref="LayoutCoverage"/>.
/// </summary>
public class LayoutDiagnosticsReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "";

    [JsonPropertyName("ruleSetVersion")]
    public string RuleSetVersion { get; set; } = "";

    [JsonPropertyName("snapshot")]
    public LayoutSnapshotInfo Snapshot { get; set; } = new();

    [JsonPropertyName("capturedUtc")]
    public string CapturedUtc { get; set; } = "";

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "unknown";

    [JsonPropertyName("scope")]
    public LayoutScope Scope { get; set; } = new();

    [JsonPropertyName("coverage")]
    public LayoutCoverage Coverage { get; set; } = new();

    [JsonPropertyName("summary")]
    public LayoutSummary Summary { get; set; } = new();

    [JsonPropertyName("findings")]
    public List<LayoutFinding> Findings { get; set; } = [];

    [JsonPropertyName("systemEvidence")]
    public LayoutSystemEvidence? SystemEvidence { get; set; }
}

public class LayoutInspectionResult : LayoutDiagnosticsReport
{
}

public class LayoutScope
{
    [JsonPropertyName("rootElementId")]
    public string? RootElementId { get; set; }

    [JsonPropertyName("window")]
    public int? Window { get; set; }

    [JsonPropertyName("maxElements")]
    public int MaxElements { get; set; }

    [JsonPropertyName("elementsExamined")]
    public int ElementsExamined { get; set; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }

    [JsonPropertyName("windowBounds")]
    public LayoutRect? WindowBounds { get; set; }
}

public class LayoutCoverage
{
    /// <summary><c>full</c>, <c>partial</c>, or <c>unavailable</c>.</summary>
    [JsonPropertyName("overall")]
    public string Overall { get; set; } = "unavailable";

    [JsonPropertyName("rules")]
    public List<LayoutRuleCoverage> Rules { get; set; } = [];

    [JsonPropertyName("opaqueSubtrees")]
    public List<LayoutElementReference> OpaqueSubtrees { get; set; } = [];

    [JsonPropertyName("limitations")]
    public List<string> Limitations { get; set; } = [];

    [JsonPropertyName("neverCaptured")]
    public List<string> NeverCaptured { get; set; } = [];
}

public class LayoutRuleCoverage
{
    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = "";

    [JsonPropertyName("support")]
    public string Support { get; set; } = "unavailable";

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "medium";

    [JsonPropertyName("evaluated")]
    public int Evaluated { get; set; }

    [JsonPropertyName("skipped")]
    public int Skipped { get; set; }

    [JsonPropertyName("limitations")]
    public List<string> Limitations { get; set; } = [];
}

public class LayoutSummary
{
    [JsonPropertyName("violations")]
    public int Violations { get; set; }

    [JsonPropertyName("observations")]
    public int Observations { get; set; }

    [JsonPropertyName("incomplete")]
    public int Incomplete { get; set; }

    [JsonPropertyName("passes")]
    public int Passes { get; set; }

    [JsonPropertyName("notApplicable")]
    public int NotApplicable { get; set; }

    [JsonPropertyName("suppressed")]
    public int Suppressed { get; set; }

    [JsonPropertyName("generatedFindings")]
    public int GeneratedFindings { get; set; }

    [JsonPropertyName("filteredFindings")]
    public int FilteredFindings { get; set; }

    [JsonPropertyName("activeFindings")]
    public int ActiveFindings { get; set; }

    [JsonPropertyName("omittedFindings")]
    public int OmittedFindings { get; set; }
}

public class LayoutFinding
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("suppressionKey")]
    public string SuppressionKey { get; set; } = "";

    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = "";

    [JsonPropertyName("subtype")]
    public string? Subtype { get; set; }

    /// <summary><c>violation</c>, <c>observation</c>, or <c>incomplete</c>.</summary>
    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = "observation";

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "medium";

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "info";

    [JsonPropertyName("actionability")]
    public string Actionability { get; set; } = "review";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = "";

    [JsonPropertyName("element")]
    public LayoutElementReference? Element { get; set; }

    [JsonPropertyName("parent")]
    public LayoutElementReference? Parent { get; set; }

    [JsonPropertyName("relatedElements")]
    public List<LayoutRelatedElement> RelatedElements { get; set; } = [];

    [JsonPropertyName("fixCategories")]
    public List<string> FixCategories { get; set; } = [];

    [JsonPropertyName("evidence")]
    public LayoutFindingEvidence? Evidence { get; set; }

    [JsonPropertyName("limitations")]
    public List<string> Limitations { get; set; } = [];

    [JsonPropertyName("suppressed")]
    public bool Suppressed { get; set; }

    [JsonPropertyName("wouldSuppress")]
    public bool WouldSuppress { get; set; }

    [JsonPropertyName("suppressionReason")]
    public string? SuppressionReason { get; set; }
}

public class LayoutElementReference
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("automationId")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("interactive")]
    public bool Interactive { get; set; }

    [JsonPropertyName("sourceFile")]
    public string? SourceFile { get; set; }

    [JsonPropertyName("sourceLine")]
    public int? SourceLine { get; set; }

    [JsonPropertyName("sourceColumn")]
    public int? SourceColumn { get; set; }
}

public class LayoutFindingEvidence
{
    [JsonPropertyName("fullRegion")]
    public LayoutRegionInfo? FullRegion { get; set; }

    [JsonPropertyName("visibleRegion")]
    public LayoutRegionInfo? VisibleRegion { get; set; }

    [JsonPropertyName("contentRegion")]
    public LayoutRegionInfo? ContentRegion { get; set; }

    [JsonPropertyName("lostAreaRatio")]
    public double? LostAreaRatio { get; set; }

    [JsonPropertyName("overflowInsetsPhysicalPixels")]
    public LayoutOverflowInsets? OverflowInsetsPhysicalPixels { get; set; }

    [JsonPropertyName("clipChain")]
    public List<LayoutClipContribution> ClipChain { get; set; } = [];

    [JsonPropertyName("text")]
    public LayoutTextEvidence? Text { get; set; }

    [JsonPropertyName("overlap")]
    public LayoutOverlapEvidence? Overlap { get; set; }

    [JsonPropertyName("frame")]
    public LayoutRect? Frame { get; set; }

    [JsonPropertyName("windowBounds")]
    public LayoutRect? WindowBounds { get; set; }

    [JsonPropertyName("parentWindowBounds")]
    public LayoutRect? ParentWindowBounds { get; set; }

    [JsonPropertyName("desiredSize")]
    public LayoutSize? DesiredSize { get; set; }

    [JsonPropertyName("explicitWidth")]
    public double? ExplicitWidth { get; set; }

    [JsonPropertyName("explicitHeight")]
    public double? ExplicitHeight { get; set; }

    [JsonPropertyName("constraint")]
    public string? Constraint { get; set; }

    [JsonPropertyName("constraintValue")]
    public double? ConstraintValue { get; set; }

    [JsonPropertyName("actualValue")]
    public double? ActualValue { get; set; }

    [JsonPropertyName("overflowWidth")]
    public double? OverflowWidth { get; set; }

    [JsonPropertyName("overflowHeight")]
    public double? OverflowHeight { get; set; }

    [JsonPropertyName("affectedElements")]
    public int? AffectedElements { get; set; }

    [JsonPropertyName("limitations")]
    public List<string> Limitations { get; set; } = [];
}

public class LayoutRect
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

public class LayoutSize
{
    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

public class LayoutSnapshotInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("capturedAt")]
    public string CapturedAt { get; set; } = "";

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "unknown";

    [JsonPropertyName("treeRevision")]
    public string TreeRevision { get; set; } = "";

    [JsonPropertyName("diagnosticsRevision")]
    public string DiagnosticsRevision { get; set; } = "";

    [JsonPropertyName("stable")]
    public bool Stable { get; set; }

    [JsonPropertyName("stabilityReason")]
    public string? StabilityReason { get; set; }

    [JsonPropertyName("nodeCount")]
    public int NodeCount { get; set; }

    [JsonPropertyName("windows")]
    public List<LayoutWindowInfo> Windows { get; set; } = [];
}

public class LayoutWindowInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("logicalUnit")]
    public string LogicalUnit { get; set; } = "dip";

    [JsonPropertyName("scale")]
    public double Scale { get; set; } = 1;

    [JsonPropertyName("origin")]
    public string Origin { get; set; } = "client-top-left";

    [JsonPropertyName("bounds")]
    public LayoutRect? Bounds { get; set; }
}

public class LayoutRuleCatalog
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = LayoutDiagnosticsWire.ResponseSchemaVersion;

    [JsonPropertyName("ruleSetVersion")]
    public string RuleSetVersion { get; set; } = LayoutDiagnosticsWire.ResponseSchemaVersion;

    [JsonPropertyName("profiles")]
    public string[] Profiles { get; set; } = [];

    [JsonPropertyName("rules")]
    public List<LayoutRuleCoverage> Rules { get; set; } = [];
}

public class LayoutRelatedElement
{
    [JsonPropertyName("relation")]
    public string Relation { get; set; } = "";

    [JsonPropertyName("element")]
    public LayoutElementReference Element { get; set; } = new();
}

public class LayoutRegionInfo
{
    [JsonPropertyName("bounds")]
    public LayoutRect Bounds { get; set; } = new();

    [JsonPropertyName("points")]
    public List<LayoutPointInfo> Points { get; set; } = [];

    [JsonPropertyName("area")]
    public double Area { get; set; }

    [JsonPropertyName("precision")]
    public string Precision { get; set; } = "unknown";
}

public class LayoutPointInfo
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public class LayoutClipContribution
{
    [JsonPropertyName("clipperElementId")]
    public string? ClipperElementId { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "unknown-platform-clip";

    [JsonPropertyName("precision")]
    public string Precision { get; set; } = "unknown";

    [JsonPropertyName("areaBefore")]
    public double AreaBefore { get; set; }

    [JsonPropertyName("areaAfter")]
    public double AreaAfter { get; set; }

    [JsonPropertyName("lostAreaRatio")]
    public double LostAreaRatio { get; set; }

    [JsonPropertyName("region")]
    public LayoutRegionInfo? Region { get; set; }
}

public class LayoutOverflowInsets
{
    [JsonPropertyName("left")]
    public double Left { get; set; }

    [JsonPropertyName("top")]
    public double Top { get; set; }

    [JsonPropertyName("right")]
    public double Right { get; set; }

    [JsonPropertyName("bottom")]
    public double Bottom { get; set; }
}

/// <summary>
/// Text-shape evidence. It carries measurement geometry only: there is no member for text content
/// or text length, so a report structurally cannot imply that text was read.
/// </summary>
public class LayoutTextEvidence
{
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("isTruncated")]
    public bool? IsTruncated { get; set; }

    [JsonPropertyName("renderedLineCount")]
    public int? RenderedLineCount { get; set; }

    [JsonPropertyName("maximumLines")]
    public int? MaximumLines { get; set; }

    [JsonPropertyName("ellipsisCount")]
    public int? EllipsisCount { get; set; }

    [JsonPropertyName("contentWidth")]
    public double? ContentWidth { get; set; }

    [JsonPropertyName("contentHeight")]
    public double? ContentHeight { get; set; }

    [JsonPropertyName("availableWidth")]
    public double? AvailableWidth { get; set; }

    [JsonPropertyName("availableHeight")]
    public double? AvailableHeight { get; set; }

    [JsonPropertyName("autoShrunk")]
    public bool? AutoShrunk { get; set; }

    [JsonPropertyName("measurementSource")]
    public string MeasurementSource { get; set; } = "unknown";
}

public class LayoutOverlapEvidence
{
    [JsonPropertyName("intersectionRegion")]
    public LayoutRegionInfo? IntersectionRegion { get; set; }

    [JsonPropertyName("overlapAreaRatio")]
    public double OverlapAreaRatio { get; set; }

    [JsonPropertyName("blockedAreaLowerBound")]
    public double? BlockedAreaLowerBound { get; set; }

    [JsonPropertyName("blockedAreaUpperBound")]
    public double? BlockedAreaUpperBound { get; set; }

    [JsonPropertyName("sampleCount")]
    public int SampleCount { get; set; }
}

/// <summary>
/// Machine-readable <c>reason</c> tokens a DevFlow agent returns with a refused layout scan, and
/// the values <see cref="LayoutDiagnosticsException.ErrorType"/> carries.
///
/// <para>These are the classification surface. The exception's <see cref="Exception.Message"/> is
/// agent-authored text that can carry element ids, source paths, and control text, so a caller
/// deciding what a failure <em>means</em> compares these tokens instead of reading that text. The
/// tokens live here, beside the exception that surfaces them, so the producer and every consumer
/// share one definition rather than repeating a literal.</para>
/// </summary>
public static class LayoutDiagnosticsErrorTypes
{
    /// <summary>The scope element the request named does not exist in the live tree.</summary>
    public const string ElementNotFound = "layout-diagnostics-element-not-found";
}

public sealed class LayoutDiagnosticsException : Exception
{
    public LayoutDiagnosticsException(
        int statusCode,
        string message,
        string? errorType = null,
        bool retryable = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorType = errorType;
        Retryable = retryable;
    }

    public int StatusCode { get; }

    public string? ErrorType { get; }

    public bool Retryable { get; }
}

public class LayoutSystemEvidence
{
    [JsonPropertyName("status")] public string Status { get; set; } = "unavailable";
    [JsonPropertyName("deviceId")] public string? DeviceId { get; set; }
    [JsonPropertyName("capturedAt")] public string? CapturedAt { get; set; }
    [JsonPropertyName("captureSkewMs")] public double? CaptureSkewMs { get; set; }
    [JsonPropertyName("geometryStable")] public bool GeometryStable { get; set; }
    [JsonPropertyName("foregroundOwner")] public string? ForegroundOwner { get; set; }
    [JsonPropertyName("keyboardVisible")] public bool? KeyboardVisible { get; set; }
    [JsonPropertyName("screenshotCaptured")] public bool ScreenshotCaptured { get; set; }
    [JsonPropertyName("screenshotDigest")] public string? ScreenshotDigest { get; set; }
    [JsonPropertyName("elements")] public List<LayoutSystemElement> Elements { get; set; } = [];
    [JsonPropertyName("limitations")] public List<string> Limitations { get; set; } = [];
}

public class LayoutSystemElement
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("packageId")] public string? PackageId { get; set; }
    [JsonPropertyName("interactive")] public bool Interactive { get; set; }
    [JsonPropertyName("bounds")] public LayoutRect? Bounds { get; set; }
}
