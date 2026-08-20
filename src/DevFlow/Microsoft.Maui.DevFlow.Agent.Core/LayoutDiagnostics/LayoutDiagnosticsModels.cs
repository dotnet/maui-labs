using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core;

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

    public static readonly string[] All =
    [
        ElementClipped,
        ElementOutsideWindow,
        ContentOverflow,
        TextNotFullyRendered,
        InteractionOccluded,
        VisualOccluded,
        GeometricOverlap,
        AccessibilityVisibilityMismatch,
        VisibleZeroArea
    ];
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ElementType { get; set; }

    [JsonPropertyName("relatedElementId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RelatedElementId { get; set; }

    [JsonPropertyName("relatedAutomationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RelatedAutomationId { get; set; }

    [JsonPropertyName("sourceFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceFile { get; set; }

    [JsonPropertyName("sourceLineStart")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SourceLineStart { get; set; }

    [JsonPropertyName("sourceLineEnd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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

    /// <summary>
    /// Finding ids already handed out during this analysis. Kept out of the wire contract; it
    /// exists so id assignment stays O(1) instead of rescanning every previous finding.
    /// </summary>
    [JsonIgnore]
    internal HashSet<string> AssignedFindingIds { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LayoutSnapshotInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("capturedAt")]
    public string CapturedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "unknown";

    [JsonPropertyName("treeRevision")]
    public string TreeRevision { get; set; } = string.Empty;

    [JsonPropertyName("diagnosticsRevision")]
    public string DiagnosticsRevision { get; set; } = string.Empty;

    [JsonPropertyName("stable")]
    public bool Stable { get; set; }

    [JsonPropertyName("stabilityReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StabilityReason { get; set; }

    [JsonPropertyName("nodeCount")]
    public int NodeCount { get; set; }

    [JsonPropertyName("windows")]
    public List<LayoutWindowInfo> Windows { get; set; } = [];
}

public sealed class LayoutWindowInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "window-0";

    [JsonPropertyName("logicalUnit")]
    public string LogicalUnit { get; set; } = "dip";

    [JsonPropertyName("scale")]
    public double Scale { get; set; } = 1;

    [JsonPropertyName("origin")]
    public string Origin { get; set; } = "client-top-left";

    [JsonPropertyName("bounds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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
    public string[] Profiles { get; set; } = ["agent", "strict", "exhaustive", "ci"];

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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subtype { get; set; }

    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = "observation";

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "info";

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "medium";

    [JsonPropertyName("actionability")]
    public string Actionability { get; set; } = "review";

    [JsonPropertyName("element")]
    public LayoutElementReference Element { get; set; } = new();

    [JsonPropertyName("relatedElements")]
    public List<LayoutRelatedElement> RelatedElements { get; set; } = [];

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("fixCategories")]
    public List<string> FixCategories { get; set; } = [];

    [JsonPropertyName("evidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutFindingEvidence? Evidence { get; set; }

    [JsonPropertyName("suppressed")]
    public bool Suppressed { get; set; }

    [JsonPropertyName("suppressionReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SuppressionReason { get; set; }
}

public sealed class LayoutElementReference
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("parentId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("automationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AutomationId { get; set; }

    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; set; }

    [JsonPropertyName("interactive")]
    public bool Interactive { get; set; }

    [JsonPropertyName("sourceFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceFile { get; set; }

    [JsonPropertyName("sourceLine")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SourceLine { get; set; }

    [JsonPropertyName("sourceColumn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutRegionInfo? FullRegion { get; set; }

    [JsonPropertyName("visibleRegion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutRegionInfo? VisibleRegion { get; set; }

    [JsonPropertyName("contentRegion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutRegionInfo? ContentRegion { get; set; }

    [JsonPropertyName("lostAreaRatio")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LostAreaRatio { get; set; }

    [JsonPropertyName("overflowInsetsPhysicalPixels")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutOverflowInsets? OverflowInsetsPhysicalPixels { get; set; }

    [JsonPropertyName("clipChain")]
    public List<LayoutClipContribution> ClipChain { get; set; } = [];

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutTextEvidence? Text { get; set; }

    [JsonPropertyName("overlap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    [JsonPropertyName("isTruncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsTruncated { get; set; }

    [JsonPropertyName("textLength")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TextLength { get; set; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("renderedLineCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RenderedLineCount { get; set; }

    [JsonPropertyName("maximumLines")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaximumLines { get; set; }

    [JsonPropertyName("ellipsisCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EllipsisCount { get; set; }

    [JsonPropertyName("contentWidth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ContentWidth { get; set; }

    [JsonPropertyName("contentHeight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ContentHeight { get; set; }

    [JsonPropertyName("availableWidth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? AvailableWidth { get; set; }

    [JsonPropertyName("availableHeight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? AvailableHeight { get; set; }

    [JsonPropertyName("autoShrunk")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AutoShrunk { get; set; }

    [JsonPropertyName("measurementSource")]
    public string MeasurementSource { get; set; } = "unknown";
}

public sealed class LayoutOverlapEvidence
{
    [JsonPropertyName("intersectionRegion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutRegionInfo? IntersectionRegion { get; set; }

    [JsonPropertyName("overlapAreaRatio")]
    public double OverlapAreaRatio { get; set; }

    [JsonPropertyName("blockedAreaLowerBound")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? BlockedAreaLowerBound { get; set; }

    [JsonPropertyName("blockedAreaUpperBound")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? BlockedAreaUpperBound { get; set; }

    [JsonPropertyName("sampleCount")]
    public int SampleCount { get; set; }
}

/// <summary>
/// Platform-derived metrics used by the cross-platform diagnostics engine.
/// This type is public so platform agent assemblies can override the collector hook.
/// </summary>
public sealed class LayoutPlatformMetrics
{
    public LayoutRegionInfo? FullRegion { get; set; }
    public LayoutRegionInfo? NativeVisibleRegion { get; set; }
    public string NativeVisibleKind { get; set; } = "unknown-platform-clip";
    public List<LayoutPlatformClip> SelfClips { get; set; } = [];
    public LayoutRegionInfo? ContentRegion { get; set; }
    public LayoutRegionInfo? SelfClipRegion { get; set; }
    public LayoutRegionInfo? DescendantClipRegion { get; set; }
    public string? DescendantClipKind { get; set; }
    public LayoutTextEvidence? Text { get; set; }
    public bool IsHitTestVisible { get; set; } = true;
    public bool IsOpaque { get; set; }
    public bool IsCoverageOpaque { get; set; }
    public bool GeometryAvailable { get; set; } = true;
    public bool IsScrollable { get; set; }
    public bool HasActiveAnimation { get; set; }
    public bool? AccessibilityVisible { get; set; }
    public int ZIndex { get; set; }
    public string Precision { get; set; } = "exactRect";
    public List<string> Limitations { get; set; } = [];
    public string? InteractionOccluderId { get; set; }
    public double? InteractionBlockedLowerBound { get; set; }
    public double? InteractionBlockedUpperBound { get; set; }
    public int InteractionSampleCount { get; set; }
}

public sealed class LayoutPlatformClip
{
    public string? ClipperElementId { get; set; }
    public string Kind { get; set; } = "unknown-platform-clip";
    public LayoutRegionInfo Region { get; set; } = new();
}
