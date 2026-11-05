namespace Microsoft.Maui.DevFlow.Agent.Core;

internal static partial class LayoutDiagnosticsEngine
{
    private static bool IsBaselineRule(string rule)
        => rule is LayoutDiagnosticRules.ConstraintViolation
            or LayoutDiagnosticRules.DesiredSizeConstrained
            or LayoutDiagnosticRules.ChildOutsideParent;

    private static void AnalyzeBaselineRules(
        LayoutInspectionResult result,
        LayoutInspectionRequest request,
        HashSet<string> enabledRules,
        LayoutNodeSnapshot node,
        IReadOnlyDictionary<string, LayoutNodeSnapshot> nodesById,
        HashSet<string> detectedRules)
    {
        if (enabledRules.Contains(LayoutDiagnosticRules.ConstraintViolation) && CanCheckConstraints(node))
        {
            var sizing = node.Sizing!;
            var conflicts = new List<string>();
            var outsideLimits = new List<string>();
            CheckAxis("Width", sizing.ArrangedWidth, sizing.MinimumWidth, sizing.MaximumWidth, node.WindowScale, conflicts, outsideLimits);
            CheckAxis("Height", sizing.ArrangedHeight, sizing.MinimumHeight, sizing.MaximumHeight, node.WindowScale, conflicts, outsideLimits);
            if (conflicts.Count > 0 || outsideLimits.Count > 0)
            {
                var finding = BaselineFinding(node, LayoutDiagnosticRules.ConstraintViolation);
                finding.Subtype = conflicts.Count > 0
                    ? outsideLimits.Count > 0 ? "mixed-constraints" : "conflicting-limits"
                    : "arranged-outside-limits";
                finding.Outcome = "violation";
                finding.Severity = "moderate";
                finding.Confidence = "high";
                finding.Actionability = "review";
                finding.Message = string.Join(" ", conflicts.Concat(outsideLimits));
                finding.FixCategories = ["adjust-layout-constraints", "increase-host-space"];
                AddBaselineFinding(result, request, finding, detectedRules);
            }
        }

        if (enabledRules.Contains(LayoutDiagnosticRules.DesiredSizeConstrained) && CanCheckDesiredSize(node))
        {
            var sizing = node.Sizing!;
            var widthConstrained = sizing.DesiredWidth is { } width
                && ExceedsPixel(width - sizing.ArrangedWidth, node.WindowScale);
            var heightConstrained = sizing.DesiredHeight is { } height
                && ExceedsPixel(height - sizing.ArrangedHeight, node.WindowScale);
            if (widthConstrained || heightConstrained)
            {
                var finding = BaselineFinding(node, LayoutDiagnosticRules.DesiredSizeConstrained);
                finding.Subtype = widthConstrained && heightConstrained ? "both-axes" : widthConstrained ? "width" : "height";
                finding.Message = FormattableString.Invariant(
                    $"The last measured MAUI desired size exceeds the arranged size ({sizing.ArrangedWidth:0.##} x {sizing.ArrangedHeight:0.##} logical units). This does not prove content is clipped.");
                finding.Confidence = "medium";
                finding.Evidence!.Limitations.Add(
                    "DesiredSize is the last constrained measurement, not an unconstrained content measurement. No remeasurement was performed.");
                finding.FixCategories = ["review-layout-sizing"];
                AddBaselineFinding(result, request, finding, detectedRules);
            }
        }

        if (enabledRules.Contains(LayoutDiagnosticRules.ChildOutsideParent)
            && TryGetLayoutParent(node, nodesById, out var parent))
        {
            var bounds = node.ParentRelativeBounds!;
            var localChild = LayoutRegionMath.FromRect(bounds);
            var localParent = LayoutRegionMath.FromRect(
                0, 0, parent!.Sizing!.ArrangedWidth, parent.Sizing.ArrangedHeight);
            var overflow = LayoutRegionMath.GetOverflowInsets(localChild, localParent, node.WindowScale);
            if (LayoutRegionMath.HasMeaningfulOverflow(overflow))
            {
                var finding = BaselineFinding(node, LayoutDiagnosticRules.ChildOutsideParent);
                finding.Subtype = "arranged-frame-overflow";
                finding.Confidence = "high";
                finding.Message = FormattableString.Invariant(
                    $"The child's arranged frame extends outside its direct layout parent (left {overflow.Left:0.##}, top {overflow.Top:0.##}, right {overflow.Right:0.##}, bottom {overflow.Bottom:0.##} untransformed layout pixels at window density). This may be intentional; clipping and reachability are checked separately.");
                finding.RelatedElements.Add(Related("layout-parent", parent));
                finding.Evidence!.ParentRegion = parent.FullRegion;
                finding.Evidence.LayoutOverflowInsetsPhysicalPixels = overflow;
                finding.FixCategories = ["review-layout-sizing"];
                AddBaselineFinding(result, request, finding, detectedRules);
            }
        }
    }

    private static void CheckAxis(
        string axis,
        double arranged,
        double? minimum,
        double? maximum,
        double scale,
        List<string> conflicts,
        List<string> outsideLimits)
    {
        if (IsKnownDimension(minimum) && IsKnownDimension(maximum) && minimum > maximum)
        {
            conflicts.Add(FormattableString.Invariant(
                $"{axis} minimum {minimum:0.##} exceeds maximum {maximum:0.##} logical units."));
            return;
        }

        if (IsKnownDimension(minimum) && ExceedsPixel(minimum!.Value - arranged, scale))
            outsideLimits.Add(FormattableString.Invariant(
                $"Arranged {axis.ToLowerInvariant()} {arranged:0.##} is below minimum {minimum:0.##} logical units."));
        if (IsKnownDimension(maximum) && ExceedsPixel(arranged - maximum!.Value, scale))
            outsideLimits.Add(FormattableString.Invariant(
                $"Arranged {axis.ToLowerInvariant()} {arranged:0.##} exceeds maximum {maximum:0.##} logical units."));
    }

    private static bool CanCheckConstraints(LayoutNodeSnapshot node)
        => HasSizing(node) && node.Sizing is { } sizing
            && (IsKnownDimension(sizing.MinimumWidth) || IsKnownDimension(sizing.MinimumHeight)
                || IsKnownDimension(sizing.MaximumWidth) || IsKnownDimension(sizing.MaximumHeight));

    private static bool CanCheckDesiredSize(LayoutNodeSnapshot node)
        => HasSizing(node) && !node.IsScrollable
            && node.Sizing is { ArrangedWidth: > 0, ArrangedHeight: > 0 } sizing
            && IsKnownDimension(sizing.DesiredWidth) && IsKnownDimension(sizing.DesiredHeight);

    private static bool TryGetLayoutParent(
        LayoutNodeSnapshot node,
        IReadOnlyDictionary<string, LayoutNodeSnapshot> nodesById,
        out LayoutNodeSnapshot? parent)
    {
        parent = null;
        return HasSizing(node) && !node.HasVisualTransform && !node.HasNegativeMargin
            && node.ParentRelativeBounds is { Width: > 0, Height: > 0 } bounds
            && double.IsFinite(bounds.X) && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height)
            && node.Element.ParentId is { } parentId
            && nodesById.TryGetValue(parentId, out parent)
            && HasSizing(parent) && parent.IsLayoutContainer && !parent.IsScrollable
            && !parent.HasVisualTransform && !parent.HasNegativeMargin
            && parent.WindowId == node.WindowId
            && parent.Sizing is { ArrangedWidth: > 0, ArrangedHeight: > 0 };
    }

    private static bool HasSizing(LayoutNodeSnapshot node)
        => node.IsRendered && node.GeometryAvailable && node.Sizing is { } sizing
            && IsKnownDimension(sizing.ArrangedWidth) && IsKnownDimension(sizing.ArrangedHeight);

    private static bool IsKnownDimension(double? value)
        => value is { } number && double.IsFinite(number) && number >= 0;

    private static bool ExceedsPixel(double difference, double scale)
        => difference * (double.IsFinite(scale) && scale > 0 ? scale : 1) >= 1;

    private static LayoutFinding BaselineFinding(LayoutNodeSnapshot node, string rule)
        => new()
        {
            RuleId = rule,
            Outcome = "observation",
            Severity = "info",
            Actionability = "informational",
            Element = ToReference(node),
            Evidence = new LayoutFindingEvidence
            {
                FullRegion = node.FullRegion,
                VisibleRegion = node.VisibleRegion,
                Sizing = node.Sizing,
                Limitations = node.HasVisualTransform || node.HasTransformedAncestor
                    ? node.Limitations.Append(
                        "Sizing and layout-overflow measurements exclude visual transforms; rendered highlight regions include them and may differ.")
                        .ToList()
                    : node.Limitations.ToList()
            }
        };

    private static void AddBaselineFinding(
        LayoutInspectionResult result,
        LayoutInspectionRequest request,
        LayoutFinding finding,
        HashSet<string> detectedRules)
    {
        detectedRules.Add(finding.RuleId);
        AddFinding(result, request, finding);
    }
}
