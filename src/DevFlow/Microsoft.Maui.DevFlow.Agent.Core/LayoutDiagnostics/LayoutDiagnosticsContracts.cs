using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics;

/// <summary>
/// Wire constants for the on-demand layout diagnostics report.
///
/// The contract is shared by the managed baseline and the richer native collectors. Individual
/// rules advertise their actual support in every report; unavailable native evidence is reported
/// as incomplete coverage rather than being mistaken for a pass.
/// </summary>
public static class LayoutDiagnosticsFormat
{
    /// <summary>Payload shape version. Bump when a field is removed or changes meaning.</summary>
    public const string SchemaVersion = "2.0";

    /// <summary>Rule semantics version. Bump when a rule's outcome or threshold changes.</summary>
    public const string RuleSetVersion = "2.1";

    /// <summary>Hard cap on elements examined in one report, regardless of the request.</summary>
    public const int MaxElements = 5_000;

    /// <summary>Default element budget when the caller does not ask for one.</summary>
    public const int DefaultMaxElements = 2_000;
    public const int MaxFindings = 500;

    /// <summary>
    /// Geometry tolerance in device-independent units. Layout arithmetic accumulates rounding on
    /// every platform, so a difference under this is treated as "equal" by every rule.
    /// </summary>
    public const double Tolerance = 0.5;

    /// <summary>
    /// Relative slack applied on top of <see cref="Tolerance"/> before a desired-size overflow is
    /// considered material, so a 1% measure/arrange rounding difference is not reported.
    /// </summary>
    public const double RelativeTolerance = 0.02;

    /// <summary>Data classes this subsystem never reads, surfaced verbatim in every report.</summary>
    public static readonly IReadOnlyList<string> NeverCaptured =
    [
        "Element Text/Value content",
        "Native and framework property dictionaries",
        "BindingContext / view-model object graphs",
        "Platform accessibility or automation tree data",
        "WebView raw text, input values, and application state",
    ];
}

/// <summary>Stable rule identifiers. Also the ordering key for deterministic report output.</summary>
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

    /// <summary>A visible, realized element was allocated no drawable area.</summary>
    public const string VisibleZeroArea = "layout.visible-zero-area";

    /// <summary>A finite actual dimension falls outside a declared minimum/maximum request.</summary>
    public const string ConstraintViolation = "layout.constraint-violation";

    /// <summary>A visible element with positive area lies entirely outside the known window.</summary>
    public const string OutsideWindow = ElementOutsideWindow;

    /// <summary>The element's measured desired size materially exceeds its arranged size.</summary>
    public const string DesiredSizeConstrained = "layout.desired-size-constrained";

    /// <summary>A child's arranged rectangle is not contained by its parent's rectangle.</summary>
    public const string ChildOutsideParent = "layout.child-outside-parent";

    /// <summary>Rules supported by the managed-only baseline collector.</summary>
    public static readonly IReadOnlyList<string> Managed =
    [
        VisibleZeroArea,
        ConstraintViolation,
        OutsideWindow,
        DesiredSizeConstrained,
        ChildOutsideParent,
    ];

    /// <summary>Every rule in stable report order.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        ElementClipped,
        ElementOutsideWindow,
        ContentOverflow,
        TextNotFullyRendered,
        InteractionOccluded,
        VisualOccluded,
        GeometricOverlap,
        AccessibilityVisibilityMismatch,
        VisibleZeroArea,
        ConstraintViolation,
        DesiredSizeConstrained,
        ChildOutsideParent,
    ];

    internal static int OrderOf(string ruleId)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i], ruleId, StringComparison.Ordinal))
                return i;
        }
        return All.Count;
    }
}

/// <summary>What a finding asserts. Absence of a finding is never an assertion of correctness.</summary>
public static class LayoutOutcomes
{
    /// <summary>An observable defect: the reported geometry cannot be correct as authored.</summary>
    public const string Violation = "violation";

    /// <summary>Something worth a human look that may be entirely intentional.</summary>
    public const string Observation = "observation";

    /// <summary>The rule could not be evaluated because the required geometry was unavailable.</summary>
    public const string Incomplete = "incomplete";

    public const string Pass = "pass";

    public const string NotApplicable = "notApplicable";
}

/// <summary>How much the report trusts a finding or a rule's coverage.</summary>
public static class LayoutConfidence
{
    public const string Exact = "exact";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
}

public static class LayoutSeverity
{
    public const string Info = "info";
    public const string Minor = "minor";
    public const string Moderate = "moderate";
    public const string Serious = "serious";
    public const string Critical = "critical";

    public static readonly IReadOnlyList<string> All =
        [Info, Minor, Moderate, Serious, Critical];
}

public static class LayoutActionability
{
    public const string Informational = "informational";
    public const string Review = "review";
    public const string Fix = "fix";
}

/// <summary>How completely a rule could be evaluated over the captured scope.</summary>
public static class LayoutRuleSupport
{
    /// <summary>Every element in scope had the geometry the rule needs.</summary>
    public const string Full = "full";

    /// <summary>Some elements lacked the geometry the rule needs; those are reported incomplete.</summary>
    public const string Partial = "partial";

    /// <summary>No element in scope had the geometry the rule needs.</summary>
    public const string Unavailable = "unavailable";
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

/// <summary>The rich request accepted by <c>/api/v1/ui/diagnostics/layout</c>.</summary>
public class LayoutInspectionRequest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = LayoutDiagnosticsFormat.SchemaVersion;

    [JsonPropertyName("scope")]
    public LayoutInspectionScope Scope { get; set; } = new();

    [JsonPropertyName("profile")]
    public string Profile { get; set; } = "agent";

    [JsonPropertyName("rules")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Rules { get; set; }

    [JsonPropertyName("minimumSeverity")]
    public string MinimumSeverity { get; set; } = LayoutSeverity.Info;

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

    /// <summary>Legacy flat alias for <see cref="LayoutInspectionScope.RootElementId"/>.</summary>
    [JsonPropertyName("elementId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ElementId { get; set; }

    /// <summary>Legacy flat alias for <see cref="LayoutInspectionScope.Window"/>.</summary>
    [JsonPropertyName("window")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Window { get; set; }

    /// <summary>Legacy element budget retained for existing callers.</summary>
    [JsonPropertyName("maxElements")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxElements { get; set; }
}

/// <summary>Compatibility name retained for code compiled against the managed-only contract.</summary>
public sealed class LayoutDiagnosticsRequest : LayoutInspectionRequest
{
}

public sealed class LayoutInspectionScope
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = LayoutScopeModes.ActivePage;

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

/// <summary>A rectangle in the coordinate space named by its owner.</summary>
public sealed class LayoutRect
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonIgnore]
    public double Right => X + Width;

    [JsonIgnore]
    public double Bottom => Y + Height;

    [JsonIgnore]
    public bool HasPositiveArea => Width > 0 && Height > 0;
}

public sealed class LayoutSize
{
    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

public sealed class LayoutThickness
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
/// One element's managed layout state, captured during a single UI-thread tree walk.
/// Text and values are deliberately absent — this type carries geometry and identity only.
/// </summary>
public sealed class LayoutElementSnapshot
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("parentId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentId { get; set; }

    [JsonPropertyName("windowId")]
    public string WindowId { get; set; } = "window-0";

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

    /// <summary>Arranged rectangle in the parent's coordinate space (MAUI <c>Frame</c>).</summary>
    [JsonPropertyName("frame")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutRect? Frame { get; set; }

    /// <summary>Arranged rectangle in window coordinates, when the platform can resolve it.</summary>
    [JsonPropertyName("windowBounds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutRect? WindowBounds { get; set; }

    /// <summary>Measured size from the last measure pass, when the platform reports one.</summary>
    [JsonPropertyName("desiredSize")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutSize? DesiredSize { get; set; }

    [JsonPropertyName("explicitWidth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ExplicitWidth { get; set; }

    [JsonPropertyName("explicitHeight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ExplicitHeight { get; set; }

    [JsonPropertyName("minimumWidth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MinimumWidth { get; set; }

    [JsonPropertyName("minimumHeight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MinimumHeight { get; set; }

    [JsonPropertyName("maximumWidth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MaximumWidth { get; set; }

    [JsonPropertyName("maximumHeight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MaximumHeight { get; set; }

    [JsonPropertyName("margin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutThickness? Margin { get; set; }

    [JsonPropertyName("padding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutThickness? Padding { get; set; }

    [JsonPropertyName("zIndex")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ZIndex { get; set; }

    /// <summary>Effective tree visibility, including hidden or transparent ancestors.</summary>
    [JsonPropertyName("isVisible")]
    public bool IsVisible { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1;

    /// <summary>
    /// True when the element has a platform handler, so its arranged geometry reflects a completed
    /// layout pass. Unrealized elements are excluded from every geometry rule.
    /// </summary>
    [JsonPropertyName("isRealized")]
    public bool IsRealized { get; set; }

    /// <summary>True when managed layout state could be read at all (a runtime object was found).</summary>
    [JsonPropertyName("hasLayoutState")]
    public bool HasLayoutState { get; set; }

    [JsonPropertyName("fullRegion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutRegionInfo? FullRegion { get; set; }

    [JsonPropertyName("visibleRegion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutRegionInfo? VisibleRegion { get; set; }

    [JsonPropertyName("contentRegion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutRegionInfo? ContentRegion { get; set; }

    [JsonPropertyName("textEvidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutTextEvidence? TextEvidence { get; set; }

    [JsonPropertyName("hitTestSampleCount")]
    public int HitTestSampleCount { get; set; }

    [JsonPropertyName("blockedHitTestSampleCount")]
    public int BlockedHitTestSampleCount { get; set; }

    [JsonPropertyName("platformEvidenceLimitations")]
    public List<string> PlatformEvidenceLimitations { get; set; } = [];
}

/// <summary>What the scan actually looked at.</summary>
public sealed class LayoutDiagnosticsScope
{
    [JsonPropertyName("rootElementId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RootElementId { get; set; }

    [JsonPropertyName("window")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Window { get; set; }

    [JsonPropertyName("maxElements")]
    public int MaxElements { get; set; }

    [JsonPropertyName("elementsExamined")]
    public int ElementsExamined { get; set; }

    /// <summary>True when the element budget stopped the walk before the tree was exhausted.</summary>
    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }

    /// <summary>Window rectangle used by window-relative rules, when it is known.</summary>
    [JsonPropertyName("windowBounds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutRect? WindowBounds { get; set; }
}

public sealed class LayoutRuleCoverage
{
    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = string.Empty;

    [JsonPropertyName("support")]
    public string Support { get; set; } = LayoutRuleSupport.Unavailable;

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = LayoutConfidence.Medium;

    [JsonPropertyName("evaluated")]
    public int Evaluated { get; set; }

    [JsonPropertyName("skipped")]
    public int Skipped { get; set; }

    [JsonPropertyName("limitations")]
    public List<string> Limitations { get; set; } = [];
}

public sealed class LayoutDiagnosticsCoverage
{
    /// <summary><c>full</c>, <c>partial</c>, or <c>unavailable</c> across every rule.</summary>
    [JsonPropertyName("overall")]
    public string Overall { get; set; } = LayoutRuleSupport.Unavailable;

    [JsonPropertyName("rules")]
    public List<LayoutRuleCoverage> Rules { get; set; } = [];

    [JsonPropertyName("opaqueSubtrees")]
    public List<LayoutElementReference> OpaqueSubtrees { get; set; } = [];

    /// <summary>Everything this report structurally cannot tell you.</summary>
    [JsonPropertyName("limitations")]
    public List<string> Limitations { get; set; } = [];

    [JsonPropertyName("neverCaptured")]
    public List<string> NeverCaptured { get; set; } = [];
}

public sealed class LayoutDiagnosticsSummary
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

/// <summary>A reference to the element a finding is about (identity and source only).</summary>
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

/// <summary>The measurements a finding is derived from, so a reader can check the arithmetic.</summary>
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

    [JsonPropertyName("frame")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutRect? Frame { get; set; }

    [JsonPropertyName("windowBounds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutRect? WindowBounds { get; set; }

    [JsonPropertyName("parentWindowBounds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutRect? ParentWindowBounds { get; set; }

    [JsonPropertyName("desiredSize")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutSize? DesiredSize { get; set; }

    [JsonPropertyName("explicitWidth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ExplicitWidth { get; set; }

    [JsonPropertyName("explicitHeight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ExplicitHeight { get; set; }

    [JsonPropertyName("constraint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Constraint { get; set; }

    [JsonPropertyName("constraintValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ConstraintValue { get; set; }

    [JsonPropertyName("actualValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ActualValue { get; set; }

    [JsonPropertyName("overflowWidth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? OverflowWidth { get; set; }

    [JsonPropertyName("overflowHeight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? OverflowHeight { get; set; }

    [JsonPropertyName("affectedElements")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AffectedElements { get; set; }

    [JsonPropertyName("limitations")]
    public List<string> Limitations { get; set; } = [];
}

public sealed class LayoutFinding
{
    /// <summary>Stable within one report; derived from rule + element + axis.</summary>
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
    public string Outcome { get; set; } = LayoutOutcomes.Observation;

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = LayoutConfidence.Medium;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = LayoutSeverity.Info;

    [JsonPropertyName("actionability")]
    public string Actionability { get; set; } = LayoutActionability.Review;

    /// <summary>The observed fact, stated without inference.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Why it may or may not be a bug, including the benign explanations.</summary>
    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;

    [JsonPropertyName("element")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutElementReference? Element { get; set; }

    [JsonPropertyName("parent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutElementReference? Parent { get; set; }

    [JsonPropertyName("relatedElements")]
    public List<LayoutRelatedElement> RelatedElements { get; set; } = [];

    [JsonPropertyName("fixCategories")]
    public List<string> FixCategories { get; set; } = [];

    [JsonPropertyName("evidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutFindingEvidence? Evidence { get; set; }

    [JsonPropertyName("limitations")]
    public List<string> Limitations { get; set; } = [];

    [JsonPropertyName("suppressed")]
    public bool Suppressed { get; set; }

    [JsonPropertyName("wouldSuppress")]
    public bool WouldSuppress { get; set; }

    [JsonPropertyName("suppressionReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SuppressionReason { get; set; }
}

/// <summary>The complete, self-describing layout diagnostics report.</summary>
public class LayoutDiagnosticsReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = LayoutDiagnosticsFormat.SchemaVersion;

    [JsonPropertyName("ruleSetVersion")]
    public string RuleSetVersion { get; set; } = LayoutDiagnosticsFormat.RuleSetVersion;

    [JsonPropertyName("snapshot")]
    public LayoutSnapshotInfo Snapshot { get; set; } = new();

    [JsonPropertyName("capturedUtc")]
    public string CapturedUtc { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "unknown";

    [JsonPropertyName("scope")]
    public LayoutDiagnosticsScope Scope { get; set; } = new();

    [JsonPropertyName("coverage")]
    public LayoutDiagnosticsCoverage Coverage { get; set; } = new();

    [JsonPropertyName("summary")]
    public LayoutDiagnosticsSummary Summary { get; set; } = new();

    [JsonPropertyName("findings")]
    public List<LayoutFinding> Findings { get; set; } = [];

    [JsonPropertyName("systemEvidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutSystemEvidence? SystemEvidence { get; set; }
}

public sealed class LayoutInspectionResult : LayoutDiagnosticsReport
{
}

public sealed class LayoutSystemEvidence
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

public sealed class LayoutSystemElement
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("packageId")] public string? PackageId { get; set; }
    [JsonPropertyName("interactive")] public bool Interactive { get; set; }
    [JsonPropertyName("bounds")] public LayoutRect? Bounds { get; set; }
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
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("logicalUnit")]
    public string LogicalUnit { get; set; } = "dip";

    [JsonPropertyName("scale")]
    public double Scale { get; set; } = 1;

    [JsonPropertyName("origin")]
    public string Origin { get; set; } = "client-top-left";

    [JsonPropertyName("bounds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LayoutRect? Bounds { get; set; }
}

public sealed class LayoutRuleCatalog
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = LayoutDiagnosticsFormat.SchemaVersion;

    [JsonPropertyName("ruleSetVersion")]
    public string RuleSetVersion { get; set; } = LayoutDiagnosticsFormat.RuleSetVersion;

    [JsonPropertyName("profiles")]
    public string[] Profiles { get; set; } = ["agent", "strict", "exhaustive", "ci"];

    [JsonPropertyName("rules")]
    public List<LayoutRuleCoverage> Rules { get; set; } = [];
}

public sealed class LayoutRelatedElement
{
    [JsonPropertyName("relation")]
    public string Relation { get; set; } = string.Empty;

    [JsonPropertyName("element")]
    public LayoutElementReference Element { get; set; } = new();
}

public sealed class LayoutRegionInfo
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

public sealed class LayoutPointInfo
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
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
