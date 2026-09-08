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
}

public sealed class LayoutInspectionRequest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("scope")]
    public LayoutInspectionScope Scope { get; set; } = new();

    [JsonPropertyName("profile")]
    public string Profile { get; set; } = "agent";

    [JsonPropertyName("rules")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Rules { get; set; }

    [JsonPropertyName("minimumSeverity")]
    public string MinimumSeverity { get; set; } = "minor";

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
}

public sealed class LayoutInspectionScope
{
    [JsonPropertyName("rootElementId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RootElementId { get; set; }

    [JsonPropertyName("window")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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

public sealed class LayoutStabilityOptions
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

public sealed class LayoutOcclusionOptions
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

public sealed class LayoutPrivacyOptions
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "none";
}

public sealed class LayoutSuppression
{
    [JsonPropertyName("ruleId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuleId { get; set; }

    [JsonPropertyName("elementId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ElementId { get; set; }

    [JsonPropertyName("automationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Fingerprint { get; set; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}

public sealed class LayoutInspectionResult
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("ruleSetVersion")]
    public string RuleSetVersion { get; set; } = "1.0";

    [JsonPropertyName("snapshot")]
    public LayoutSnapshotInfo Snapshot { get; set; } = new();

    [JsonPropertyName("coverage")]
    public LayoutCoverageInfo Coverage { get; set; } = new();

    [JsonPropertyName("summary")]
    public LayoutInspectionSummary Summary { get; set; } = new();

    [JsonPropertyName("findings")]
    public List<LayoutFinding> Findings { get; set; } = [];
}

public sealed class LayoutSnapshotInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("capturedAt")]
    public string CapturedAt { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "unknown";

    [JsonPropertyName("treeRevision")]
    public string TreeRevision { get; set; } = string.Empty;

    [JsonPropertyName("diagnosticsRevision")]
    public string DiagnosticsRevision { get; set; } = string.Empty;

    [JsonPropertyName("stable")]
    public bool Stable { get; set; }

    [JsonPropertyName("stabilityReason")]
    public string? StabilityReason { get; set; }

    [JsonPropertyName("nodeCount")]
    public int NodeCount { get; set; }

    [JsonPropertyName("windows")]
    public List<LayoutWindowInfo> Windows { get; set; } = [];
}

public sealed class LayoutWindowInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("logicalUnit")]
    public string LogicalUnit { get; set; } = "dip";

    [JsonPropertyName("scale")]
    public double Scale { get; set; } = 1;

    [JsonPropertyName("origin")]
    public string Origin { get; set; } = "client-top-left";

    [JsonPropertyName("bounds")]
    public LayoutRectInfo? Bounds { get; set; }
}

public sealed class LayoutCoverageInfo
{
    [JsonPropertyName("overall")]
    public string Overall { get; set; } = "partial";

    [JsonPropertyName("rules")]
    public List<LayoutRuleSupportInfo> Rules { get; set; } = [];

    [JsonPropertyName("opaqueSubtrees")]
    public List<LayoutElementReference> OpaqueSubtrees { get; set; } = [];

    [JsonPropertyName("limitations")]
    public List<string> Limitations { get; set; } = [];
}

public sealed class LayoutRuleSupportInfo
{
    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = string.Empty;

    [JsonPropertyName("support")]
    public string Support { get; set; } = "partial";

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "medium";

    [JsonPropertyName("limitations")]
    public List<string> Limitations { get; set; } = [];
}

public sealed class LayoutRuleCatalog
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("ruleSetVersion")]
    public string RuleSetVersion { get; set; } = "1.0";

    [JsonPropertyName("profiles")]
    public string[] Profiles { get; set; } = [];

    [JsonPropertyName("rules")]
    public List<LayoutRuleSupportInfo> Rules { get; set; } = [];
}

public sealed class LayoutInspectionSummary
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
}

public sealed class LayoutFinding
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

    [JsonPropertyName("evidence")]
    public LayoutFindingEvidence? Evidence { get; set; }

    [JsonPropertyName("suppressed")]
    public bool Suppressed { get; set; }

    [JsonPropertyName("suppressionReason")]
    public string? SuppressionReason { get; set; }
}

public sealed class LayoutElementReference
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

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

public sealed class LayoutRelatedElement
{
    [JsonPropertyName("relation")]
    public string Relation { get; set; } = string.Empty;

    [JsonPropertyName("element")]
    public LayoutElementReference Element { get; set; } = new();
}

public sealed class LayoutFindingEvidence
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

    [JsonPropertyName("limitations")]
    public List<string> Limitations { get; set; } = [];
}

public sealed class LayoutRegionInfo
{
    [JsonPropertyName("bounds")]
    public LayoutRectInfo Bounds { get; set; } = new();

    [JsonPropertyName("points")]
    public List<LayoutPointInfo> Points { get; set; } = [];

    [JsonPropertyName("area")]
    public double Area { get; set; }

    [JsonPropertyName("precision")]
    public string Precision { get; set; } = "unknown";
}

public sealed class LayoutPointInfo
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public sealed class LayoutRectInfo
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

public sealed class LayoutClipContribution
{
    [JsonPropertyName("clipperElementId")]
    public string? ClipperElementId { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("precision")]
    public string Precision { get; set; } = string.Empty;

    [JsonPropertyName("areaBefore")]
    public double AreaBefore { get; set; }

    [JsonPropertyName("areaAfter")]
    public double AreaAfter { get; set; }

    [JsonPropertyName("lostAreaRatio")]
    public double LostAreaRatio { get; set; }

    [JsonPropertyName("region")]
    public LayoutRegionInfo? Region { get; set; }
}

public sealed class LayoutOverflowInsets
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

public sealed class LayoutTextEvidence
{
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("isTruncated")]
    public bool? IsTruncated { get; set; }

    [JsonPropertyName("textLength")]
    public int? TextLength { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

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

public sealed class LayoutOverlapEvidence
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

public sealed class LayoutDiagnosticsException : Exception
{
    public LayoutDiagnosticsException(
        int statusCode,
        string message,
        string? errorType = null,
        bool retryable = false)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorType = errorType;
        Retryable = retryable;
    }

    public LayoutDiagnosticsException(
        int statusCode,
        string message,
        string? errorType,
        bool retryable,
        Exception innerException)
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
