using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Maui.DevFlow.Agent.Core;

internal sealed class LayoutCaptureSnapshot
{
    public List<LayoutNodeSnapshot> Nodes { get; } = [];
    public List<LayoutWindowInfo> Windows { get; } = [];
    public List<string> Limitations { get; } = [];
    public List<string> IncompleteReasons { get; } = [];
    public bool HasActiveAnimations { get; set; }

    public string GeometryHash => VisualTreeRevision.ComputeFlat(
        Nodes
            .OrderBy(node => node.TreeOrder)
            .Select(node => node.Element));

    public string StabilityHash => ComputeHash(includeBlazor: true, useElementBounds: false);
    public string DiagnosticsHash => ComputeDiagnosticsHash();

    public void MarkIncomplete(string reason)
    {
        Limitations.Add(reason);
        IncompleteReasons.Add(reason);
    }

    private string ComputeHash(bool includeBlazor, bool useElementBounds)
    {
        var builder = new StringBuilder();
        foreach (var node in Nodes
            .Where(node => includeBlazor
                || !node.Element.Framework.Equals("blazor", StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.TreeOrder))
        {
            LayoutRectInfo bounds;
            if (useElementBounds)
            {
                var sourceBounds = node.Element.WindowBounds ?? node.Element.Bounds;
                if (sourceBounds is null)
                    continue;
                bounds = new LayoutRectInfo
                {
                    X = sourceBounds.X,
                    Y = sourceBounds.Y,
                    Width = sourceBounds.Width,
                    Height = sourceBounds.Height
                };
            }
            else
            {
                bounds = node.FullRegion.Bounds;
            }
            builder.Append(node.Element.Id).Append('|')
                .Append(bounds.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(bounds.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(bounds.Width.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(bounds.Height.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private string ComputeDiagnosticsHash()
    {
        var builder = new StringBuilder();
        foreach (var node in Nodes.OrderBy(node => node.TreeOrder))
        {
            builder.Append(node.Element.Id).Append('|')
                .Append(node.Element.ParentId).Append('|')
                .Append(node.Element.Framework).Append('|');
            AppendRegion(builder, node.FullRegion);
            AppendRegion(builder, node.VisibleRegion);
            AppendRegion(builder, node.ContentRegion);
            builder.Append(node.ZIndex).Append('|')
            .Append(node.IsRendered).Append('|')
            .Append(node.IsInteractive).Append('|')
                .Append(node.IsHitTestVisible).Append('|')
                .Append(node.IsOpaque).Append('|')
                .Append(node.IsCoverageOpaque).Append('|')
                .Append(node.GeometryAvailable).Append('|')
                .Append(node.IsScrollable).Append('|')
                .Append(node.AccessibilityVisible).Append('|')
                .Append(node.Text?.Kind).Append('|')
                .Append(node.Text?.IsTruncated).Append('|')
                .Append(node.Text?.TextLength).Append('|')
                .Append(node.Text?.RenderedLineCount).Append('|')
                .Append(node.Text?.MaximumLines).Append('|')
                .Append(node.InteractionOccluderId).Append('|');
            AppendNullableDouble(builder, node.Text?.ContentWidth);
            AppendNullableDouble(builder, node.Text?.ContentHeight);
            AppendNullableDouble(builder, node.Text?.AvailableWidth);
            AppendNullableDouble(builder, node.Text?.AvailableHeight);
            AppendNullableDouble(builder, node.InteractionBlockedLowerBound);
            AppendNullableDouble(builder, node.InteractionBlockedUpperBound);
            foreach (var clip in node.ClipChain)
            {
                builder.Append(clip.ClipperElementId).Append(':')
                    .Append(clip.Kind).Append(':')
                    .Append(clip.LostAreaRatio.ToString(
                        "R",
                        CultureInfo.InvariantCulture))
                    .Append('|');
                AppendRegion(builder, clip.Region);
            }
            builder.Append(';');
        }

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static void AppendRegion(
        StringBuilder builder,
        LayoutRegionInfo? region)
    {
        if (region is null)
        {
            builder.Append("null|");
            return;
        }

        foreach (var point in region.Points)
        {
            builder.Append(point.X.ToString(
                    "R",
                    CultureInfo.InvariantCulture))
                .Append(',')
                .Append(point.Y.ToString(
                    "R",
                    CultureInfo.InvariantCulture))
                .Append(';');
        }
        builder.Append('|');
    }

    private static void AppendNullableDouble(
        StringBuilder builder,
        double? value)
    {
        if (value is { } number)
            builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
        builder.Append('|');
    }
}

internal sealed class LayoutNodeSnapshot
{
    public ElementInfo Element { get; set; } = new();
    public LayoutRegionInfo LayoutRegion { get; set; } = new();
    public LayoutRegionInfo FullRegion { get; set; } = new();
    public LayoutRegionInfo VisibleRegion { get; set; } = new();
    public LayoutRegionInfo? ContentRegion { get; set; }
    public LayoutRegionInfo? DescendantClipRegion { get; set; }
    public string? DescendantClipKind { get; set; }
    public List<LayoutClipContribution> ClipChain { get; set; } = [];
    public LayoutTextEvidence? Text { get; set; }
    public List<string> Limitations { get; set; } = [];
    public string WindowId { get; set; } = "window-0";
    public int TreeOrder { get; set; }
    public int ZIndex { get; set; }
    public double WindowScale { get; set; } = 1;
    public bool IsInteractive { get; set; }
    public bool IsRendered { get; set; } = true;
    public bool IsHitTestVisible { get; set; } = true;
    public bool IsOpaque { get; set; }
    public bool IsCoverageOpaque { get; set; }
    public bool GeometryAvailable { get; set; } = true;
    public bool IsScrollable { get; set; }
    public bool IsInsideScrollableViewport { get; set; }
    public bool? AccessibilityVisible { get; set; }
    public bool HasActiveAnimation { get; set; }
    public string? InteractionOccluderId { get; set; }
    public double? InteractionBlockedLowerBound { get; set; }
    public double? InteractionBlockedUpperBound { get; set; }
    public int InteractionSampleCount { get; set; }
}

internal static class LayoutRegionMath
{
    private const double Epsilon = 0.000001;

    public static LayoutRegionInfo Empty(string precision = "exactPolygon") => new()
    {
        Bounds = new LayoutRectInfo(),
        Points = [],
        Area = 0,
        Precision = precision
    };

    public static LayoutRegionInfo FromBounds(BoundsInfo bounds, string precision = "exactRect")
        => FromRect(bounds.X, bounds.Y, bounds.Width, bounds.Height, precision);

    public static LayoutRegionInfo FromRect(LayoutRectInfo bounds, string precision = "exactRect")
        => FromRect(bounds.X, bounds.Y, bounds.Width, bounds.Height, precision);

    public static LayoutRegionInfo FromRect(double x, double y, double width, double height, string precision = "exactRect")
    {
        width = NormalizeDimension(width);
        height = NormalizeDimension(height);
        if (width <= 0 || height <= 0)
        {
            return new LayoutRegionInfo
            {
                Bounds = new LayoutRectInfo
                {
                    X = double.IsFinite(x) ? x : 0,
                    Y = double.IsFinite(y) ? y : 0,
                    Width = width,
                    Height = height
                },
                Area = 0,
                Precision = precision
            };
        }
        var points = new List<LayoutPointInfo>
            {
                new() { X = x, Y = y },
                new() { X = x + width, Y = y },
                new() { X = x + width, Y = y + height },
                new() { X = x, Y = y + height }
            };
        return FromPoints(points, precision);
    }

    public static LayoutRegionInfo FromPoints(IEnumerable<LayoutPointInfo> points, string precision = "exactPolygon")
    {
        var normalized = points
            .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
            .Select(point => new LayoutPointInfo { X = point.X, Y = point.Y })
            .ToList();

        if (normalized.Count < 3)
            return Empty(precision);

        var minX = normalized.Min(point => point.X);
        var minY = normalized.Min(point => point.Y);
        var maxX = normalized.Max(point => point.X);
        var maxY = normalized.Max(point => point.Y);
        var area = PolygonArea(normalized);

        return new LayoutRegionInfo
        {
            Bounds = new LayoutRectInfo
            {
                X = minX,
                Y = minY,
                Width = Math.Max(0, maxX - minX),
                Height = Math.Max(0, maxY - minY)
            },
            Points = normalized,
            Area = area,
            Precision = precision
        };
    }

    public static LayoutRegionInfo Intersect(LayoutRegionInfo first, LayoutRegionInfo second)
    {
        if (first.Area <= Epsilon || second.Area <= Epsilon || !BoundsOverlap(first.Bounds, second.Bounds))
            return Empty(CombinePrecision(first.Precision, second.Precision));

        if (first.Points.Count >= 3 && second.Points.Count >= 3)
        {
            var output = first.Points
                .Select(point => new LayoutPointInfo { X = point.X, Y = point.Y })
                .ToList();
            var clip = second.Points;
            var clipIsCounterClockwise = SignedPolygonArea(clip) >= 0;

            for (var edgeIndex = 0; edgeIndex < clip.Count && output.Count > 0; edgeIndex++)
            {
                var edgeStart = clip[edgeIndex];
                var edgeEnd = clip[(edgeIndex + 1) % clip.Count];
                var input = output;
                output = [];
                var previous = input[^1];

                foreach (var current in input)
                {
                    var currentInside = IsInside(current, edgeStart, edgeEnd, clipIsCounterClockwise);
                    var previousInside = IsInside(previous, edgeStart, edgeEnd, clipIsCounterClockwise);

                    if (currentInside)
                    {
                        if (!previousInside)
                            output.Add(LineIntersection(previous, current, edgeStart, edgeEnd));
                        output.Add(new LayoutPointInfo { X = current.X, Y = current.Y });
                    }
                    else if (previousInside)
                    {
                        output.Add(LineIntersection(previous, current, edgeStart, edgeEnd));
                    }

                    previous = current;
                }
            }

            return FromPoints(output, CombinePrecision(first.Precision, second.Precision));
        }

        var left = Math.Max(first.Bounds.X, second.Bounds.X);
        var top = Math.Max(first.Bounds.Y, second.Bounds.Y);
        var right = Math.Min(first.Bounds.X + first.Bounds.Width, second.Bounds.X + second.Bounds.Width);
        var bottom = Math.Min(first.Bounds.Y + first.Bounds.Height, second.Bounds.Y + second.Bounds.Height);
        return FromRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), "conservativeBounds");
    }

    public static bool Contains(LayoutRegionInfo region, LayoutPointInfo point)
    {
        if (region.Area <= Epsilon)
            return false;

        if (region.Points.Count < 3)
        {
            return point.X >= region.Bounds.X && point.X <= region.Bounds.X + region.Bounds.Width
                && point.Y >= region.Bounds.Y && point.Y <= region.Bounds.Y + region.Bounds.Height;
        }

        var inside = false;
        for (int current = 0, previous = region.Points.Count - 1; current < region.Points.Count; previous = current++)
        {
            var currentPoint = region.Points[current];
            var previousPoint = region.Points[previous];
            var intersects = ((currentPoint.Y > point.Y) != (previousPoint.Y > point.Y))
                && point.X < (previousPoint.X - currentPoint.X) * (point.Y - currentPoint.Y)
                    / ((previousPoint.Y - currentPoint.Y) + Epsilon) + currentPoint.X;
            if (intersects)
                inside = !inside;
        }
        return inside;
    }

    public static LayoutPointInfo Center(LayoutRegionInfo region) => new()
    {
        X = region.Bounds.X + region.Bounds.Width / 2,
        Y = region.Bounds.Y + region.Bounds.Height / 2
    };

    public static LayoutOverflowInsets GetOverflowInsets(LayoutRegionInfo content, LayoutRegionInfo host, double scale)
    {
        scale = scale > 0 && double.IsFinite(scale) ? scale : 1;
        return new LayoutOverflowInsets
        {
            Left = Math.Max(0, host.Bounds.X - content.Bounds.X) * scale,
            Top = Math.Max(0, host.Bounds.Y - content.Bounds.Y) * scale,
            Right = Math.Max(0,
                content.Bounds.X + content.Bounds.Width - host.Bounds.X - host.Bounds.Width) * scale,
            Bottom = Math.Max(0,
                content.Bounds.Y + content.Bounds.Height - host.Bounds.Y - host.Bounds.Height) * scale
        };
    }

    public static bool HasMeaningfulOverflow(LayoutOverflowInsets insets)
        => Math.Max(Math.Max(insets.Left, insets.Top), Math.Max(insets.Right, insets.Bottom)) >= 1;

    private static bool BoundsOverlap(LayoutRectInfo first, LayoutRectInfo second)
        => first.X < second.X + second.Width
            && first.X + first.Width > second.X
            && first.Y < second.Y + second.Height
            && first.Y + first.Height > second.Y;

    private static double NormalizeDimension(double value)
        => double.IsFinite(value) && value > 0 ? value : 0;

    private static double PolygonArea(IReadOnlyList<LayoutPointInfo> points)
        => Math.Abs(SignedPolygonArea(points));

    private static double SignedPolygonArea(IReadOnlyList<LayoutPointInfo> points)
    {
        if (points.Count < 3)
            return 0;

        double sum = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            sum += current.X * next.Y - next.X * current.Y;
        }
        return sum / 2;
    }

    private static bool IsInside(
        LayoutPointInfo point,
        LayoutPointInfo edgeStart,
        LayoutPointInfo edgeEnd,
        bool counterClockwise)
    {
        var cross = (edgeEnd.X - edgeStart.X) * (point.Y - edgeStart.Y)
            - (edgeEnd.Y - edgeStart.Y) * (point.X - edgeStart.X);
        return counterClockwise ? cross >= -Epsilon : cross <= Epsilon;
    }

    private static LayoutPointInfo LineIntersection(
        LayoutPointInfo lineStart,
        LayoutPointInfo lineEnd,
        LayoutPointInfo edgeStart,
        LayoutPointInfo edgeEnd)
    {
        var lineDx = lineEnd.X - lineStart.X;
        var lineDy = lineEnd.Y - lineStart.Y;
        var edgeDx = edgeEnd.X - edgeStart.X;
        var edgeDy = edgeEnd.Y - edgeStart.Y;
        var denominator = lineDx * edgeDy - lineDy * edgeDx;
        if (Math.Abs(denominator) <= Epsilon)
            return new LayoutPointInfo { X = lineEnd.X, Y = lineEnd.Y };

        var t = ((edgeStart.X - lineStart.X) * edgeDy - (edgeStart.Y - lineStart.Y) * edgeDx)
            / denominator;
        return new LayoutPointInfo
        {
            X = lineStart.X + t * lineDx,
            Y = lineStart.Y + t * lineDy
        };
    }

    private static string CombinePrecision(string first, string second)
    {
        if (first == "unknown" || second == "unknown")
            return "unknown";
        if (first == "conservativeBounds" || second == "conservativeBounds")
            return "conservativeBounds";
        if (first == "exactPolygon" || second == "exactPolygon")
            return "exactPolygon";
        return "exactRect";
    }
}

internal static class LayoutDiagnosticsEngine
{
    private static readonly string[] s_nodeScopedRules =
    [
        LayoutDiagnosticRules.ElementClipped,
        LayoutDiagnosticRules.ElementOutsideWindow,
        LayoutDiagnosticRules.ContentOverflow,
        LayoutDiagnosticRules.TextNotFullyRendered,
        LayoutDiagnosticRules.InteractionOccluded,
        LayoutDiagnosticRules.AccessibilityVisibilityMismatch,
        LayoutDiagnosticRules.VisibleZeroArea
    ];

    private static readonly Dictionary<string, int> s_severityRanks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["info"] = 0,
        ["minor"] = 1,
        ["moderate"] = 2,
        ["serious"] = 3,
        ["critical"] = 4
    };

    public static LayoutInspectionResult Analyze(
        LayoutCaptureSnapshot capture,
        LayoutInspectionRequest request,
        string platform,
        bool stable,
        string? stabilityReason,
        IReadOnlyList<LayoutRuleSupportInfo> ruleSupport)
    {
        var result = new LayoutInspectionResult
        {
            Snapshot = new LayoutSnapshotInfo
            {
                Platform = platform,
                TreeRevision = capture.GeometryHash,
                DiagnosticsRevision = capture.DiagnosticsHash,
                Stable = stable,
                StabilityReason = stabilityReason,
                NodeCount = capture.Nodes.Count,
                Windows = capture.Windows
            },
            Coverage = new LayoutCoverageInfo
            {
                Rules = ruleSupport.Select(CloneSupport).ToList(),
                OpaqueSubtrees = capture.Nodes
                    .Where(node => node.IsCoverageOpaque || !node.GeometryAvailable)
                    .Select(ToReference)
                    .ToList(),
                Limitations = capture.Limitations.Distinct(StringComparer.Ordinal).ToList()
            }
        };

        result.Coverage.Overall = result.Coverage.Rules.Any(rule => rule.Support != "exact")
            || result.Coverage.OpaqueSubtrees.Count > 0
            || result.Coverage.Limitations.Count > 0
            ? "partial"
            : "complete";

        var requestedRules = request.Rules is { Count: > 0 }
            ? request.Rules.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : LayoutDiagnosticRules.All.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var supportByRule = ruleSupport.ToDictionary(
            support => support.RuleId,
            StringComparer.OrdinalIgnoreCase);
        var enabledRules = requestedRules
            .Where(rule => !supportByRule.TryGetValue(rule, out var support)
                || !support.Support.Equals("unsupported", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var passCount = 0;
        var notApplicableCount = requestedRules.Count(rule =>
            supportByRule.TryGetValue(rule, out var support)
            && support.Support.Equals("unsupported", StringComparison.OrdinalIgnoreCase));
        var unsupportedRequestedCount = notApplicableCount;
        var nodesById = capture.Nodes.ToDictionary(node => node.Element.Id, StringComparer.OrdinalIgnoreCase);

        if (!stable)
        {
            result.Coverage.Limitations.Add(
                stabilityReason
                ?? "The UI did not reach a stable geometry state before the inspection timeout.");
        }

        foreach (var node in capture.Nodes)
        {
            if (!node.GeometryAvailable)
            {
                notApplicableCount += s_nodeScopedRules.Count(enabledRules.Contains);
                continue;
            }

            var detectedRules = AnalyzeNode(result, request, enabledRules, node, nodesById);
            foreach (var rule in s_nodeScopedRules.Where(enabledRules.Contains))
            {
                if (!IsNodeRuleApplicable(rule, node, request))
                {
                    notApplicableCount++;
                    continue;
                }

                if (detectedRules.Contains(rule))
                    continue;

                passCount++;
                if (request.IncludePasses)
                    AddPassFinding(result, request, rule, node);
            }
        }

        var geometryNodes = capture.Nodes
            .Where(node => node.GeometryAvailable)
            .ToList();
        var overlapDetectedRules = AnalyzeOverlaps(
            result,
            request,
            enabledRules,
            geometryNodes);
        var overlapRules = new[]
        {
            LayoutDiagnosticRules.GeometricOverlap,
            LayoutDiagnosticRules.VisualOccluded
        };
        foreach (var rule in overlapRules.Where(enabledRules.Contains))
        {
            var analysisRan = IsExhaustive(request.Profile)
                || request.Rules?.Contains(rule, StringComparer.OrdinalIgnoreCase) == true;
            if (!analysisRan)
            {
                notApplicableCount++;
                continue;
            }
            if (geometryNodes.Count < 2)
            {
                notApplicableCount++;
                continue;
            }

            var detected = overlapDetectedRules.Contains(rule);
            if (!detected)
            {
                passCount++;
                if (request.IncludePasses)
                    AddPassFinding(result, request, rule, geometryNodes[0]);
            }
        }

        result.Findings = result.Findings
            .OrderBy(finding => OutcomeRank(finding.Outcome))
            .ThenByDescending(finding => SeverityRank(finding.Severity))
            .ThenByDescending(finding => ConfidenceRank(finding.Confidence))
            .ThenBy(finding => nodesById.TryGetValue(finding.Element.Id, out var node) ? node.TreeOrder : int.MaxValue)
            .ThenBy(finding => finding.RuleId, StringComparer.Ordinal)
            .ThenBy(finding => finding.Id, StringComparer.Ordinal)
            .ToList();

        result.Summary = Summarize(result.Findings, passCount, notApplicableCount);
        if (!stable)
            result.Summary.Incomplete++;
        result.Summary.Incomplete += capture.IncompleteReasons
            .Distinct(StringComparer.Ordinal)
            .Count();
        result.Summary.Incomplete += unsupportedRequestedCount;
        if (result.Coverage.OpaqueSubtrees.Count > 0)
            result.Summary.Incomplete++;
        return result;
    }

    public static LayoutRuleCatalog BuildCatalog(IReadOnlyList<LayoutRuleSupportInfo> support)
        => new() { Rules = support.Select(CloneSupport).ToList() };

    private static HashSet<string> AnalyzeNode(
        LayoutInspectionResult result,
        LayoutInspectionRequest request,
        HashSet<string> enabledRules,
        LayoutNodeSnapshot node,
        IReadOnlyDictionary<string, LayoutNodeSnapshot> nodesById)
    {
        var detectedRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fullArea = node.FullRegion.Area;
        var visibleArea = node.VisibleRegion.Area;
        var lostAreaRatio = fullArea > 0 ? Math.Clamp(1 - visibleArea / fullArea, 0, 1) : 0;
        var meaningfulClip = HasMeaningfulClip(node.FullRegion, node.VisibleRegion, node.WindowScale);

        if (node.IsRendered && meaningfulClip && lostAreaRatio > 0)
        {
            var windowClips = node.ClipChain.Where(clip => clip.Kind == "window-edge").ToList();
            var scrollClips = node.ClipChain.Where(clip => clip.Kind == "scroll-viewport").ToList();
            var otherClips = node.ClipChain
                .Where(clip => clip.Kind is not "window-edge" and not "scroll-viewport")
                .ToList();

            if (windowClips.Count > 0
                && !node.IsInsideScrollableViewport
                && enabledRules.Contains(LayoutDiagnosticRules.ElementOutsideWindow))
            {
                detectedRules.Add(LayoutDiagnosticRules.ElementOutsideWindow);
                var outsideWindowViolation = node.IsInteractive && lostAreaRatio >= 0.5;
                AddFinding(result, request, CreateClipFinding(
                    node,
                    LayoutDiagnosticRules.ElementOutsideWindow,
                    "window-edge",
                    outsideWindowViolation ? "serious" : node.IsInteractive ? "moderate" : "minor",
                    outsideWindowViolation ? "fix" : node.IsInteractive ? "review" : "informational",
                    outsideWindowViolation ? "violation" : "observation",
                    lostAreaRatio,
                    nodesById,
                    "Element extends beyond the current app window."));
            }

            if (enabledRules.Contains(LayoutDiagnosticRules.ElementClipped)
                && (otherClips.Count > 0 || IsStrict(request.Profile) && scrollClips.Count > 0))
            {
                detectedRules.Add(LayoutDiagnosticRules.ElementClipped);
                var actionable = otherClips.Count > 0 && node.IsInteractive;
                AddFinding(result, request, CreateClipFinding(
                    node,
                    LayoutDiagnosticRules.ElementClipped,
                    otherClips.FirstOrDefault()?.Kind ?? "scroll-viewport",
                    actionable ? "serious" : "minor",
                    actionable ? "fix" : "review",
                    actionable ? "violation" : "observation",
                    lostAreaRatio,
                    nodesById,
                    otherClips.Count > 0
                        ? "Element is partially or fully removed by an ancestor or explicit clip."
                        : "Element is partially outside a scroll viewport."));
            }
        }
        if (enabledRules.Contains(LayoutDiagnosticRules.VisibleZeroArea)
            && node.IsRendered
            && node.FullRegion.Precision != "unknown"
            && (node.FullRegion.Area <= 0 || node.VisibleRegion.Area <= 0))
        {
            detectedRules.Add(LayoutDiagnosticRules.VisibleZeroArea);
            var outsideScrollViewport = node.IsInsideScrollableViewport
                && node.FullRegion.Area > 0
                && node.VisibleRegion.Area <= 0;
            AddFinding(result, request, new LayoutFinding
            {
                RuleId = LayoutDiagnosticRules.VisibleZeroArea,
                Subtype = outsideScrollViewport
                    ? "outside-scroll-viewport"
                    : node.FullRegion.Area <= 0 ? "zero-layout-area" : "zero-visible-area",
                Outcome = outsideScrollViewport ? "observation" : node.IsInteractive ? "violation" : "observation",
                Severity = outsideScrollViewport || !node.IsInteractive ? "info" : "serious",
                Confidence = node.FullRegion.Precision.StartsWith("exact", StringComparison.Ordinal) ? "exact" : "medium",
                Actionability = outsideScrollViewport || !node.IsInteractive ? "informational" : "fix",
                Element = ToReference(node),
                Message = outsideScrollViewport
                    ? "Element is realized but currently outside an ancestor scroll viewport."
                    : node.IsInteractive
                    ? "Interactive element has no usable visible area."
                    : "Visible element has no usable visible area.",
                FixCategories = outsideScrollViewport ? [] : ["increase-host-space", "remove-zero-size-constraint"],
                Evidence = Evidence(node)
            });
        }

        if (node.IsRendered
            && enabledRules.Contains(LayoutDiagnosticRules.ContentOverflow)
            && node.ContentRegion is not null)
        {
            var overflow = LayoutRegionMath.GetOverflowInsets(node.ContentRegion, node.LayoutRegion, node.WindowScale);
            if (LayoutRegionMath.HasMeaningfulOverflow(overflow))
            {
                detectedRules.Add(LayoutDiagnosticRules.ContentOverflow);
                var scrollable = node.IsScrollable;
                var lost = node.VisibleRegion.Area + 0.000001 < node.FullRegion.Area || node.DescendantClipRegion is not null;
                var estimatedFromManagedLayout = node.ContentRegion.Precision is "conservativeBounds" or "unknown";
                AddFinding(result, request, new LayoutFinding
                {
                    RuleId = LayoutDiagnosticRules.ContentOverflow,
                    Subtype = scrollable ? "scrollable" : lost ? "lost-overflow" : "visible-overflow",
                    Outcome = scrollable || !lost || estimatedFromManagedLayout ? "observation" : "violation",
                    Severity = scrollable || estimatedFromManagedLayout
                        ? "info"
                        : lost ? "serious" : "minor",
                    Confidence = estimatedFromManagedLayout ? "low" : "high",
                    Actionability = scrollable || estimatedFromManagedLayout
                        ? "informational"
                        : lost ? "fix" : "review",
                    Element = ToReference(node),
                    Message = scrollable
                        ? "Content is larger than its viewport but is reachable by scrolling."
                        : lost
                            ? "Content extends beyond its host and is clipped or unreachable."
                            : estimatedFromManagedLayout
                                ? "MAUI desired-size metadata suggests content may extend outside its allocated layout region."
                                : "Content draws outside its allocated layout region.",
                    FixCategories = scrollable ? [] : ["increase-host-space", "enable-scroll", "change-layout-sizing"],
                    Evidence = new LayoutFindingEvidence
                    {
                            FullRegion = node.FullRegion,
                            VisibleRegion = node.VisibleRegion,
                            ContentRegion = node.ContentRegion,
                            OverflowInsetsPhysicalPixels = overflow,
                            ClipChain = node.ClipChain,
                            Limitations = estimatedFromManagedLayout
                                ? node.Limitations
                                    .Append("Content extent is estimated from MAUI DesiredSize rather than native paint bounds.")
                                    .Distinct(StringComparer.Ordinal)
                                    .ToList()
                                : node.Limitations.ToList()
                    }
                });
            }
        }

        if (node.IsRendered
            && enabledRules.Contains(LayoutDiagnosticRules.TextNotFullyRendered)
            && node.Text is
            {
                IsTruncated: null,
                AutoShrunk: null or false
            } unknownText)
        {
            detectedRules.Add(LayoutDiagnosticRules.TextNotFullyRendered);
            AddFinding(result, request, new LayoutFinding
            {
                RuleId = LayoutDiagnosticRules.TextNotFullyRendered,
                Subtype = "measurement-unavailable",
                Outcome = "incomplete",
                Severity = "info",
                Confidence = "low",
                Actionability = "informational",
                Element = ToReference(node),
                Message = "The platform did not provide enough text layout evidence to prove whether all text is rendered.",
                Evidence = new LayoutFindingEvidence
                {
                    FullRegion = node.FullRegion,
                    VisibleRegion = node.VisibleRegion,
                    Text = unknownText,
                    Limitations = node.Limitations.ToList()
                }
            });
        }
        else if (node.IsRendered
            && enabledRules.Contains(LayoutDiagnosticRules.TextNotFullyRendered)
            && node.Text is { } text
            && (text.IsTruncated == true || text.AutoShrunk == true))
        {
            detectedRules.Add(LayoutDiagnosticRules.TextNotFullyRendered);
            var autoShrunk = text.AutoShrunk == true && text.IsTruncated != true;
            var hardClip = text.Kind is
                "vertical-hard-clip" or "horizontal-hard-clip";
            AddFinding(result, request, new LayoutFinding
            {
                RuleId = LayoutDiagnosticRules.TextNotFullyRendered,
                Subtype = text.Kind ?? (autoShrunk ? "auto-shrunk" : "unknown-text-overflow"),
                Outcome = hardClip ? "violation" : "observation",
                Severity = autoShrunk
                    ? "info"
                    : hardClip
                        ? "serious"
                        : "moderate",
                Confidence = TextConfidence(text.MeasurementSource),
                Actionability = autoShrunk
                    ? "informational"
                    : hardClip
                        ? "fix"
                        : "review",
                Element = ToReference(node),
                Message = autoShrunk
                    ? "Text was reduced in size to fit its host."
                    : "Not all text content is rendered within the element.",
                FixCategories = autoShrunk
                    ? []
                    : ["increase-host-space", "change-line-break-mode", "increase-maximum-lines"],
                Evidence = new LayoutFindingEvidence
                {
                        FullRegion = node.FullRegion,
                        VisibleRegion = node.VisibleRegion,
                        Text = text,
                        Limitations = node.Limitations.ToList()
                }
            });
        }

        var interactionTarget = node.IsInteractive
            || request.Occlusion.Mode.Equals("all", StringComparison.OrdinalIgnoreCase);
        if (enabledRules.Contains(LayoutDiagnosticRules.InteractionOccluded)
            && !request.Occlusion.Mode.Equals("none", StringComparison.OrdinalIgnoreCase)
            && interactionTarget
            && node.InteractionOccluderId is { Length: > 0 } occluderId)
        {
            detectedRules.Add(LayoutDiagnosticRules.InteractionOccluded);
            nodesById.TryGetValue(occluderId, out var occluder);
            var blockedLower = node.InteractionBlockedLowerBound ?? 0;
            var expectedOverlay = occluder is not null && IsExpectedOverlay(occluder.Element);
            var actionable = node.IsInteractive && blockedLower >= 0.5 && !expectedOverlay;
            var finding = new LayoutFinding
            {
                RuleId = LayoutDiagnosticRules.InteractionOccluded,
                Subtype = "native-hit-test",
                Outcome = actionable ? "violation" : "observation",
                Severity = actionable
                    ? "serious"
                    : !node.IsInteractive ? "info" : expectedOverlay ? "minor" : "moderate",
                Confidence = "medium",
                Actionability = actionable ? "fix" : !node.IsInteractive ? "informational" : "review",
                Element = ToReference(node),
                Message = expectedOverlay
                    ? "An overlay-like element receives input above this target; review whether the overlay is intentional."
                    : actionable
                    ? "A different element receives input across most of this interactive element."
                    : "A different element receives input across part of this interactive element.",
                FixCategories = ["adjust-z-order", "disable-overlay-hit-testing", "move-overlay"],
                Evidence = new LayoutFindingEvidence
                {
                        FullRegion = node.FullRegion,
                        VisibleRegion = node.VisibleRegion,
                        Overlap = new LayoutOverlapEvidence
                        {
                            BlockedAreaLowerBound = node.InteractionBlockedLowerBound,
                            BlockedAreaUpperBound = node.InteractionBlockedUpperBound,
                            SampleCount = node.InteractionSampleCount
                        },
                        Limitations = node.Limitations.ToList()
                }
            };
            if (occluder is not null)
                finding.RelatedElements.Add(Related("occluder", occluder));
            AddFinding(result, request, finding);
        }

        if (enabledRules.Contains(LayoutDiagnosticRules.AccessibilityVisibilityMismatch)
            && node.AccessibilityVisible == true
            && node.IsInteractive
            && !node.IsInsideScrollableViewport
            && node.VisibleRegion.Area <= 0)
        {
            detectedRules.Add(LayoutDiagnosticRules.AccessibilityVisibilityMismatch);
            AddFinding(result, request, new LayoutFinding
            {
                RuleId = LayoutDiagnosticRules.AccessibilityVisibilityMismatch,
                Subtype = "accessible-but-not-visible",
                Outcome = "violation",
                Severity = "serious",
                Confidence = "high",
                Actionability = "fix",
                Element = ToReference(node),
                Message = "Element remains exposed as interactive accessibility content but has no visible region.",
                FixCategories = ["synchronize-accessibility-visibility"],
                Evidence = Evidence(node)
            });
        }

        return detectedRules;
    }

    private static HashSet<string> AnalyzeOverlaps(
        LayoutInspectionResult result,
        LayoutInspectionRequest request,
        HashSet<string> enabledRules,
        IReadOnlyList<LayoutNodeSnapshot> nodes)
    {
        var detectedRules = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var explicitOverlapRequest = request.Rules?.Any(rule =>
            rule is LayoutDiagnosticRules.GeometricOverlap or LayoutDiagnosticRules.VisualOccluded) == true;
        if (!IsExhaustive(request.Profile) && !explicitOverlapRequest)
            return detectedRules;

        foreach (var candidate in LayoutSpatialIndex.FindOverlaps(nodes))
        {
            var first = candidate.First;
            var second = candidate.Second;
            var intersection = candidate.Intersection;
            var smallerArea = Math.Max(
                0.000001,
                Math.Min(first.VisibleRegion.Area, second.VisibleRegion.Area));
            var overlapRatio = intersection.Area / smallerArea;
            if (overlapRatio < request.Occlusion.MinimumOverlapRatio)
                continue;

            var top = candidate.SameParent && first.ZIndex != second.ZIndex
                ? (first.ZIndex > second.ZIndex ? first : second)
                : (first.TreeOrder > second.TreeOrder ? first : second);
            var bottom = ReferenceEquals(top, first) ? second : first;
            var exactIntersection = intersection.Precision.StartsWith(
                "exact",
                StringComparison.Ordinal);
            var geometricConfidence = candidate.SameParent && exactIntersection
                ? "high"
                : "medium";

            if (enabledRules.Contains(LayoutDiagnosticRules.GeometricOverlap))
            {
                detectedRules.Add(LayoutDiagnosticRules.GeometricOverlap);
                AddFinding(result, request, new LayoutFinding
                {
                    RuleId = LayoutDiagnosticRules.GeometricOverlap,
                    Subtype = candidate.SameParent
                        ? "sibling-overlap"
                        : "cross-subtree-overlap",
                    Outcome = "observation",
                    Severity = "info",
                    Confidence = geometricConfidence,
                    Actionability = "informational",
                    Element = ToReference(bottom),
                    RelatedElements = [Related("overlapping", top)],
                    Message = candidate.SameParent
                        ? "Sibling elements occupy intersecting rendered regions."
                        : "Elements from different visual subtrees occupy intersecting rendered regions.",
                    Evidence = new LayoutFindingEvidence
                    {
                            FullRegion = bottom.FullRegion,
                            VisibleRegion = bottom.VisibleRegion,
                            Overlap = new LayoutOverlapEvidence
                            {
                                IntersectionRegion = intersection,
                                OverlapAreaRatio = overlapRatio
                            },
                            Limitations = candidate.SameParent
                                ? []
                                : ["Cross-subtree paint order is inferred from visual-tree order."]
                    }
                });
            }

            if (enabledRules.Contains(LayoutDiagnosticRules.VisualOccluded) && top.IsOpaque)
            {
                detectedRules.Add(LayoutDiagnosticRules.VisualOccluded);
                AddFinding(result, request, new LayoutFinding
                {
                    RuleId = LayoutDiagnosticRules.VisualOccluded,
                    Subtype = candidate.SameParent
                        ? "likely-opaque-sibling"
                        : "likely-opaque-cross-subtree",
                    Outcome = "observation",
                    Severity = overlapRatio >= 0.5 ? "moderate" : "minor",
                    Confidence = "medium",
                    Actionability = "review",
                    Element = ToReference(bottom),
                    RelatedElements = [Related("occluder", top)],
                    Message = "An opaque element painted above this element intersects its visible region.",
                    FixCategories = ["review-overlap", "adjust-z-order"],
                    Evidence = new LayoutFindingEvidence
                    {
                            FullRegion = bottom.FullRegion,
                            VisibleRegion = bottom.VisibleRegion,
                            Overlap = new LayoutOverlapEvidence
                            {
                                IntersectionRegion = intersection,
                                OverlapAreaRatio = overlapRatio
                            },
                            Limitations = candidate.SameParent
                                ? ["Visual occlusion is inferred from geometry, paint order, and opacity; transparent pixels are not inspected."]
                                :
                                [
                                    "Visual occlusion is inferred from geometry, paint order, and opacity; transparent pixels are not inspected.",
                                    "Cross-subtree paint order is inferred from visual-tree order."
                                ]
                    }
                });
            }
        }
        return detectedRules;
    }

    private static LayoutFinding CreateClipFinding(
        LayoutNodeSnapshot node,
        string ruleId,
        string subtype,
        string severity,
        string actionability,
        string outcome,
        double lostAreaRatio,
        IReadOnlyDictionary<string, LayoutNodeSnapshot> nodesById,
        string message)
    {
        var finding = new LayoutFinding
        {
            RuleId = ruleId,
            Subtype = subtype,
            Outcome = outcome,
            Severity = severity,
            Confidence = node.VisibleRegion.Precision.StartsWith("exact", StringComparison.Ordinal) ? "high" : "medium",
            Actionability = actionability,
            Element = ToReference(node),
            Message = $"{message} Visible area: {(1 - lostAreaRatio) * 100:F0}%.",
            FixCategories = ["increase-host-space", "adjust-layout-constraints", "enable-scroll"],
            Evidence = new LayoutFindingEvidence
            {
                FullRegion = node.FullRegion,
                VisibleRegion = node.VisibleRegion,
                ContentRegion = node.ContentRegion,
                LostAreaRatio = lostAreaRatio,
                ClipChain = node.ClipChain,
                Limitations = node.Limitations.ToList()
            }
        };

        foreach (var clipperId in node.ClipChain
            .Select(clip => clip.ClipperElementId)
            .Where(id => id is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (clipperId is not null && nodesById.TryGetValue(clipperId, out var clipper))
                finding.RelatedElements.Add(Related("clipper", clipper));
        }

        return finding;
    }

    private static LayoutFindingEvidence Evidence(LayoutNodeSnapshot node) => new()
    {
        FullRegion = node.FullRegion,
        VisibleRegion = node.VisibleRegion,
        ContentRegion = node.ContentRegion,
        ClipChain = node.ClipChain,
        Text = node.Text,
        Limitations = node.Limitations.ToList()
    };

    private static LayoutElementReference ToReference(LayoutNodeSnapshot node) => new()
    {
        Id = node.Element.Id,
        ParentId = node.Element.ParentId,
        Type = node.Element.Type,
        AutomationId = node.Element.AutomationId,
        Role = node.Element.Role,
        Interactive = node.IsInteractive,
        SourceFile = node.Element.SourceFile,
        SourceLine = node.Element.SourceLine,
        SourceColumn = node.Element.SourceColumn
    };

    private static LayoutRelatedElement Related(string relation, LayoutNodeSnapshot node) => new()
    {
        Relation = relation,
        Element = ToReference(node)
    };

    private static bool IsNodeRuleApplicable(
        string rule,
        LayoutNodeSnapshot node,
        LayoutInspectionRequest request)
        => rule switch
        {
            LayoutDiagnosticRules.ElementClipped
                or LayoutDiagnosticRules.ElementOutsideWindow
                or LayoutDiagnosticRules.VisibleZeroArea =>
                node.IsRendered
                && node.FullRegion.Precision != "unknown",
            LayoutDiagnosticRules.ContentOverflow =>
                node.IsRendered
                && node.ContentRegion is not null
                && node.LayoutRegion.Area > 0,
            LayoutDiagnosticRules.TextNotFullyRendered =>
                node.IsRendered && node.Text is not null,
            LayoutDiagnosticRules.InteractionOccluded =>
                !request.Occlusion.Mode.Equals("none", StringComparison.OrdinalIgnoreCase)
                && node.IsHitTestVisible
                && (node.IsInteractive
                    || request.Occlusion.Mode.Equals("all", StringComparison.OrdinalIgnoreCase)),
            LayoutDiagnosticRules.AccessibilityVisibilityMismatch =>
                node.AccessibilityVisible.HasValue,
            _ => false
        };

    private static void AddPassFinding(
        LayoutInspectionResult result,
        LayoutInspectionRequest request,
        string rule,
        LayoutNodeSnapshot node)
    {
        AddFinding(
            result,
            request,
            new LayoutFinding
            {
                RuleId = rule,
                Subtype = "pass",
                Outcome = "pass",
                Severity = "info",
                Confidence = node.FullRegion.Precision.StartsWith("exact", StringComparison.Ordinal)
                    ? "high"
                    : "medium",
                Actionability = "informational",
                Element = ToReference(node),
                Message = "No applicable layout issue was detected for this rule."
            },
            bypassSeverityFilter: true);
    }

    private static void AddFinding(
        LayoutInspectionResult result,
        LayoutInspectionRequest request,
        LayoutFinding finding,
        bool bypassSeverityFilter = false)
    {
        finding.SuppressionKey = Fingerprint(finding);
        finding.Id = UniqueFingerprint(
            result.Findings,
            finding,
            finding.SuppressionKey);
        var suppression = request.Suppressions.FirstOrDefault(candidate =>
            MatchesSuppression(candidate, finding));
        if (suppression is not null)
        {
            finding.Suppressed = true;
            finding.SuppressionReason = suppression.Reason;
        }

        if (!request.IncludeEvidence)
            finding.Evidence = null;

        if (!bypassSeverityFilter
            && finding.Outcome != "incomplete"
            && SeverityRank(finding.Severity) < SeverityRank(request.MinimumSeverity))
        {
            return;
        }

        result.Findings.Add(finding);
    }

    private static string UniqueFingerprint(
        IReadOnlyCollection<LayoutFinding> existing,
        LayoutFinding finding,
        string fingerprint)
    {
        if (!existing.Any(candidate =>
            candidate.Id.Equals(fingerprint, StringComparison.OrdinalIgnoreCase)))
        {
            return fingerprint;
        }

        var runtimeIdentity = string.Join(
            "|",
            fingerprint,
            finding.Element.Id,
            string.Join(
                ",",
                finding.RelatedElements
                    .OrderBy(related => related.Relation, StringComparer.Ordinal)
                    .ThenBy(related => related.Element.Id, StringComparer.Ordinal)
                    .Select(related => $"{related.Relation}:{related.Element.Id}")));
        var disambiguated = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(runtimeIdentity)))[..16]
            .ToLowerInvariant();
        var candidate = $"{fingerprint}-{disambiguated}";
        var occurrence = 2;
        while (existing.Any(item =>
            item.Id.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{fingerprint}-{disambiguated}-{occurrence++}";
        }

        return candidate;
    }

    private static bool MatchesSuppression(
        LayoutSuppression suppression,
        LayoutFinding finding)
    {
        if (suppression.RuleId is not null
            && !suppression.RuleId.Equals(finding.RuleId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (suppression.ElementId is not null
            && !suppression.ElementId.Equals(finding.Element.Id, StringComparison.OrdinalIgnoreCase))
            return false;
        if (suppression.AutomationId is not null
            && !suppression.AutomationId.Equals(finding.Element.AutomationId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (suppression.ElementType is not null
            && !suppression.ElementType.Equals(finding.Element.Type, StringComparison.OrdinalIgnoreCase))
            return false;
        if (suppression.Fingerprint is not null
            && !suppression.Fingerprint.Equals(
                string.IsNullOrWhiteSpace(finding.SuppressionKey)
                    ? finding.Id
                    : finding.SuppressionKey,
                StringComparison.OrdinalIgnoreCase)
            && !suppression.Fingerprint.Equals(
                finding.Id,
                StringComparison.OrdinalIgnoreCase))
            return false;
        if (suppression.SourceFile is not null
            && !SourcePathMatches(suppression.SourceFile, finding.Element.SourceFile))
            return false;
        if (suppression.SourceLineStart is not null)
        {
            if (finding.Element.SourceLine is not { } line)
                return false;
            var end = suppression.SourceLineEnd ?? suppression.SourceLineStart.Value;
            if (line < suppression.SourceLineStart.Value || line > end)
                return false;
        }

        if (suppression.RelatedElementId is not null
            && !finding.RelatedElements.Any(related =>
                suppression.RelatedElementId.Equals(
                    related.Element.Id,
                    StringComparison.OrdinalIgnoreCase)))
            return false;
        if (suppression.RelatedAutomationId is not null
            && !finding.RelatedElements.Any(related =>
                suppression.RelatedAutomationId.Equals(
                    related.Element.AutomationId,
                    StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private static bool SourcePathMatches(string requested, string? actual)
    {
        if (actual is null)
            return false;
        var normalizedRequested = requested.Replace('\\', '/').TrimStart('/');
        var normalizedActual = actual.Replace('\\', '/');
        return normalizedActual.Equals(normalizedRequested, StringComparison.OrdinalIgnoreCase)
            || normalizedActual.EndsWith(
                "/" + normalizedRequested,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string Fingerprint(LayoutFinding finding)
    {
        var related = string.Join(",", finding.RelatedElements
            .OrderBy(item => item.Relation, StringComparer.Ordinal)
            .ThenBy(item => FingerprintIdentity(item.Element), StringComparer.Ordinal)
            .Select(item => $"{item.Relation}:{FingerprintIdentity(item.Element)}"));
        var source = string.Join("|",
            finding.RuleId,
            finding.Subtype,
            FingerprintIdentity(finding.Element),
            related,
            FingerprintGeometryCause(finding.Evidence));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16]
            .ToLowerInvariant();
    }

    private static string FingerprintIdentity(LayoutElementReference element)
    {
        if (!string.IsNullOrWhiteSpace(element.SourceFile)
            && element.SourceLine is not null)
        {
            return string.Join(
                ":",
                "source",
                NormalizeSourceIdentity(element.SourceFile),
                element.SourceLine.Value.ToString(CultureInfo.InvariantCulture),
                element.SourceColumn?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                element.AutomationId,
                element.Type);
        }

        if (!string.IsNullOrWhiteSpace(element.AutomationId))
        {
            return string.Join(
                ":",
                "automation",
                element.AutomationId,
                element.Type,
                element.Role);
        }

        return string.Join(
            ":",
            "structural",
            element.Type,
            element.Role);
    }

    private static string NormalizeSourceIdentity(string path)
    {
        var segments = path
            .Replace('\\', '/')
            .Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries);
        return string.Join(
                "/",
                segments
                    .Skip(Math.Max(0, segments.Length - 3))
                    .Select(segment => segment.ToLowerInvariant()));
    }

    private static string FingerprintGeometryCause(LayoutFindingEvidence? evidence)
    {
        if (evidence is null)
            return string.Empty;

        var clipChain = string.Join(
            ",",
            evidence.ClipChain.Select(clip => string.Join(
                ":",
                clip.Kind,
                FingerprintRatio(clip.LostAreaRatio),
                FingerprintRegion(clip.Region))));

        var overflow = evidence.OverflowInsetsPhysicalPixels is { } insets
            ? string.Join(
                ",",
                FingerprintCoordinate(insets.Left),
                FingerprintCoordinate(insets.Top),
                FingerprintCoordinate(insets.Right),
                FingerprintCoordinate(insets.Bottom))
            : string.Empty;

        return string.Join(
            "|",
            FingerprintRegion(evidence.FullRegion),
            FingerprintRegion(evidence.VisibleRegion),
            FingerprintRegion(evidence.ContentRegion),
            FingerprintRegion(evidence.Overlap?.IntersectionRegion),
            overflow,
            clipChain);
    }

    private static string FingerprintRegion(LayoutRegionInfo? region)
    {
        if (region is null)
            return string.Empty;

        var bounds = region.Bounds;
        return string.Join(
            ",",
            FingerprintCoordinate(bounds.X),
            FingerprintCoordinate(bounds.Y),
            FingerprintCoordinate(bounds.Width),
            FingerprintCoordinate(bounds.Height));
    }

    private static string FingerprintCoordinate(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded == 0)
            rounded = 0;
        return rounded.ToString("0", CultureInfo.InvariantCulture);
    }

    private static string FingerprintRatio(double value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero)
            .ToString("0.00", CultureInfo.InvariantCulture);

    internal static LayoutInspectionSummary Summarize(
        IEnumerable<LayoutFinding> findings,
        int passCount = 0,
        int notApplicableCount = 0)
    {
        var summary = new LayoutInspectionSummary
        {
            Passes = passCount,
            NotApplicable = notApplicableCount
        };
        foreach (var finding in findings)
        {
            if (finding.Suppressed)
            {
                summary.Suppressed++;
                continue;
            }

            switch (finding.Outcome)
            {
                case "violation":
                    summary.Violations++;
                    break;
                case "observation":
                    summary.Observations++;
                    break;
                case "incomplete":
                    summary.Incomplete++;
                    break;
                case "pass":
                    break;
            }
        }
        return summary;
    }

    private static LayoutRuleSupportInfo CloneSupport(LayoutRuleSupportInfo support) => new()
    {
        RuleId = support.RuleId,
        Support = support.Support,
        Confidence = support.Confidence,
        Limitations = support.Limitations.ToList()
    };

    private static bool HasMeaningfulClip(
        LayoutRegionInfo full,
        LayoutRegionInfo visible,
        double scale)
    {
        scale = scale > 0 && double.IsFinite(scale) ? scale : 1;
        var fullRight = full.Bounds.X + full.Bounds.Width;
        var fullBottom = full.Bounds.Y + full.Bounds.Height;
        var visibleRight = visible.Bounds.X + visible.Bounds.Width;
        var visibleBottom = visible.Bounds.Y + visible.Bounds.Height;
        var edgeLoss = new[]
        {
            Math.Max(0, visible.Bounds.X - full.Bounds.X),
            Math.Max(0, visible.Bounds.Y - full.Bounds.Y),
            Math.Max(0, fullRight - visibleRight),
            Math.Max(0, fullBottom - visibleBottom)
        }.Max() * scale;

        if (edgeLoss > 0.000001)
            return edgeLoss >= 1;

        return Math.Max(0, full.Area - visible.Area) * scale * scale >= 1;
    }

    private static bool IsStrict(string profile)
        => profile.Equals("strict", StringComparison.OrdinalIgnoreCase)
            || profile.Equals("exhaustive", StringComparison.OrdinalIgnoreCase)
            || profile.Equals("ci", StringComparison.OrdinalIgnoreCase);

    private static bool IsExhaustive(string profile)
        => profile.Equals("exhaustive", StringComparison.OrdinalIgnoreCase);

    private static int SeverityRank(string severity)
        => s_severityRanks.TryGetValue(severity, out var rank) ? rank : 0;

    private static int OutcomeRank(string outcome) => outcome switch
    {
        "violation" => 0,
        "incomplete" => 1,
        "observation" => 2,
        "pass" => 3,
        _ => 4
    };

    private static int ConfidenceRank(string confidence) => confidence switch
    {
        "exact" => 3,
        "high" => 2,
        "medium" => 1,
        _ => 0
    };

    private static string TextConfidence(string source) => source switch
    {
        "android-layout" or "winui-is-text-trimmed" or "gtk-pango" => "exact",
        "uikit-textkit" or "appkit-textkit" or "wpf-textformatter" or "browser-layout" => "high",
        "winui-desiredsize-heuristic" => "medium",
        _ => "medium"
    };

    private static bool IsExpectedOverlay(ElementInfo element)
    {
        var identity = $"{element.Type} {element.AutomationId} {element.Role}";
        return identity.Contains("overlay", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("scrim", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("modal", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("popup", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("dialog", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("flyout", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("tooltip", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("adorner", StringComparison.OrdinalIgnoreCase);
    }
}
