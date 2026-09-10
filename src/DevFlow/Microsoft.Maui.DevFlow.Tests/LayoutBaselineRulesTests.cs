using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Graphics;
using Driver = Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class LayoutBaselineRulesTests
{
    [Theory]
    [InlineData("agent")]
    [InlineData("exhaustive")]
    public void Analyze_SharedUnmeasuredShape_DoesNotDiscardIndependentFindings(string profile)
    {
        var first = Node("shared-shape", 0, 0);
        first.GeometryAvailable = false;
        first.Element.Type = "Rectangle";
        first.Element.ParentId = "first-border";
        var second = Node("shared-shape", 0, 0);
        second.GeometryAvailable = false;
        second.Element.Type = "Rectangle";
        second.Element.ParentId = "second-border";
        var target = Node("target", 100, 40);
        target.Sizing!.MinimumWidth = 120;
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.AddRange([first, second, target]);

        var result = LayoutDiagnosticsEngine.Analyze(capture, new LayoutInspectionRequest
        {
            Profile = profile,
            Rules = [LayoutDiagnosticRules.ConstraintViolation, LayoutDiagnosticRules.GeometricOverlap],
            MinimumSeverity = "info"
        }, "test", true, null, Support());

        Assert.Equal(1, result.Summary.Violations);
        Assert.All(result.Findings, finding => Assert.Equal("target", finding.Element.Id));
        Assert.Equal("partial", result.Coverage.Overall);
        Assert.True(result.Summary.Incomplete > 0);
        Assert.Contains(result.Coverage.Limitations, reason => reason.Contains("duplicate element identities"));
    }

    [Fact]
    public void Analyze_AmbiguousParentIdentity_DoesNotChooseAnOwnerForDescendants()
    {
        var first = Node("shared-parent", 20, 40);
        first.IsLayoutContainer = true;
        var second = Node("SHARED-PARENT", 200, 40);
        second.IsLayoutContainer = true;
        var child = Child(first, x: 30);
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.AddRange([first, second, child]);

        var result = LayoutDiagnosticsEngine.Analyze(capture, new LayoutInspectionRequest
        {
            Profile = "exhaustive",
            Rules = [LayoutDiagnosticRules.ChildOutsideParent, LayoutDiagnosticRules.GeometricOverlap],
            MinimumSeverity = "info",
            IncludePasses = true
        }, "test", true, null, Support());

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Summary.Passes);
        Assert.True(result.Summary.Incomplete > 0);
        Assert.Contains(result.Coverage.OpaqueSubtrees, element => element.Id == child.Element.Id);
    }

    [Fact]
    public void Catalog_AdditiveRules_KeepSchemaAndAdvanceRuleSet()
    {
        var catalog = LayoutDiagnosticsEngine.BuildCatalog(new VisualTreeWalker().GetLayoutRuleSupport());

        Assert.Equal("1.0", catalog.SchemaVersion);
        Assert.Equal("1.1", catalog.RuleSetVersion);
        foreach (var id in BaselineRules)
        {
            var rule = Assert.Single(catalog.Rules, rule => rule.RuleId == id);
            Assert.Equal("partial", rule.Support);
            Assert.NotEmpty(rule.Limitations);
        }
    }

    [Theory]
    [InlineData(120d, null, 100, "arranged-outside-limits")]
    [InlineData(null, 80d, 100, "arranged-outside-limits")]
    [InlineData(120d, 80d, 100, "conflicting-limits")]
    public void Analyze_WidthOutsideLimits_ReportsCapturedSizing(
        double? minimum, double? maximum, double arranged, string subtype)
    {
        var node = Node("target", arranged, 40);
        node.Sizing!.MinimumWidth = minimum;
        node.Sizing.MaximumWidth = maximum;

        var result = Analyze([node], [LayoutDiagnosticRules.ConstraintViolation]);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("violation", finding.Outcome);
        Assert.Equal("moderate", finding.Severity);
        Assert.Equal("high", finding.Confidence);
        Assert.Equal(subtype, finding.Subtype);
        Assert.Equal(arranged, finding.Evidence!.Sizing!.ArrangedWidth);
        Assert.Equal(minimum, finding.Evidence.Sizing.MinimumWidth);
        Assert.Equal(maximum, finding.Evidence.Sizing.MaximumWidth);
        Assert.Equal(0, result.Summary.Passes);
    }

    [Fact]
    public void Analyze_HeightOutsideLimits_ReportsHeight()
    {
        var node = Node("target", 100, 40);
        node.Sizing!.MinimumHeight = 60;

        var finding = Assert.Single(Analyze([node], [LayoutDiagnosticRules.ConstraintViolation]).Findings);

        Assert.Contains("height", finding.Message);
        Assert.Equal(60, finding.Evidence!.Sizing!.MinimumHeight);
    }

    [Theory]
    [InlineData(0.4, 1, false)]
    [InlineData(0.4, 2, false)]
    [InlineData(0.5, 2, true)]
    [InlineData(1, 1, true)]
    [InlineData(2, 0, true)]
    public void Analyze_ConstraintDifference_UsesPhysicalPixelTolerance(double difference, double scale, bool violation)
    {
        var node = Node("target", 100, 40);
        node.WindowScale = scale;
        node.Sizing!.MinimumWidth = 100 + difference;

        var result = Analyze([node], [LayoutDiagnosticRules.ConstraintViolation], includePasses: true);

        Assert.Equal(violation ? 1 : 0, result.Summary.Violations);
        Assert.Equal(violation ? 0 : 1, result.Summary.Passes);
    }

    [Fact]
    public void Analyze_ValidConstraints_PassesWithoutFinding()
    {
        var node = Node("target", 100, 40);
        node.Sizing!.MinimumWidth = 80;
        node.Sizing.MaximumWidth = 120;
        node.Sizing.MinimumHeight = 40;
        node.Sizing.MaximumHeight = 40;

        var result = Analyze([node], [LayoutDiagnosticRules.ConstraintViolation]);

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Summary.Passes);
    }

    [Theory]
    [InlineData(120, 40, "width")]
    [InlineData(100, 60, "height")]
    [InlineData(120, 60, "both-axes")]
    public void Analyze_DesiredSizeLargerThanFrame_IsObservationNotLostContent(
        double width, double height, string subtype)
    {
        var node = Node("target", 100, 40);
        node.Sizing!.DesiredWidth = width;
        node.Sizing.DesiredHeight = height;

        var finding = Assert.Single(Analyze([node], [LayoutDiagnosticRules.DesiredSizeConstrained]).Findings);

        Assert.Equal("observation", finding.Outcome);
        Assert.Equal("info", finding.Severity);
        Assert.Equal("informational", finding.Actionability);
        Assert.Equal(subtype, finding.Subtype);
        Assert.Contains("does not prove", finding.Message);
        Assert.Equal(width, finding.Evidence!.Sizing!.DesiredWidth);
        Assert.DoesNotContain("Text", JsonSerializer.Serialize(finding.Evidence));
    }

    [Fact]
    public void Analyze_FilteredDesiredSizeObservation_DoesNotBecomePass()
    {
        var node = Node("target", 100, 40);
        node.Sizing!.DesiredWidth = 200;

        var result = Analyze([node], [LayoutDiagnosticRules.DesiredSizeConstrained],
            includePasses: true, minimumSeverity: "minor");

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Summary.Passes);
        Assert.Equal(1, result.Summary.Filtered);
    }

    [Fact]
    public void Analyze_DefaultSeverity_AccountsForBothInformationalRules()
    {
        var parent = Node("parent", 100, 100);
        parent.IsLayoutContainer = true;
        var child = Child(parent, x: 90);
        child.Sizing!.DesiredWidth = 80;
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.AddRange([parent, child]);

        var result = LayoutDiagnosticsEngine.Analyze(capture, new LayoutInspectionRequest
        {
            Rules = [LayoutDiagnosticRules.DesiredSizeConstrained, LayoutDiagnosticRules.ChildOutsideParent]
        }, "test", true, null, Support());

        Assert.Empty(result.Findings);
        Assert.Equal(2, result.Summary.Filtered);
        Assert.Equal(1, result.Summary.Passes);
        Assert.Equal(1, result.Summary.NotApplicable);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Analyze_PartialDesiredAxes_DoNotClaimWholeRulePass(bool missingWidth, bool knownAxisOverflows)
    {
        var node = Node("target", 100, 40);
        node.Sizing!.DesiredWidth = missingWidth ? null : knownAxisOverflows ? 200 : 100;
        node.Sizing.DesiredHeight = missingWidth ? knownAxisOverflows ? 80 : 40 : null;

        var result = Analyze([node], [LayoutDiagnosticRules.DesiredSizeConstrained], includePasses: true);

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Summary.Passes);
        Assert.Equal(1, result.Summary.NotApplicable);
    }

    [Fact]
    public void Analyze_MixedConstraintFailures_RetainBothAxes()
    {
        var node = Node("target", 100, 40);
        node.Sizing!.MinimumWidth = 120;
        node.Sizing.MaximumWidth = 80;
        node.Sizing.MinimumHeight = 60;

        var finding = Assert.Single(Analyze([node], [LayoutDiagnosticRules.ConstraintViolation]).Findings);

        Assert.Equal("mixed-constraints", finding.Subtype);
        Assert.Contains("Width minimum", finding.Message);
        Assert.Contains("height 40 is below minimum 60", finding.Message);
        Assert.Equal(120, finding.Evidence!.Sizing!.MinimumWidth);
        Assert.Equal(60, finding.Evidence.Sizing.MinimumHeight);
    }

    [Fact]
    public void Analyze_ScrollableDesiredSize_IsNotApplicable()
    {
        var node = Node("scroll", 100, 40);
        node.IsScrollable = true;
        node.Sizing!.DesiredHeight = 400;

        var result = Analyze([node], [LayoutDiagnosticRules.DesiredSizeConstrained], includePasses: true);

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Summary.Passes);
        Assert.Equal(1, result.Summary.NotApplicable);
    }

    [Fact]
    public void Analyze_ChildOutsideParent_UsesParentLocalFrameAndLinksParent()
    {
        var parent = Node("parent", 100, 100);
        parent.IsLayoutContainer = true;
        parent.FullRegion = LayoutRegionMath.FromRect(500, 600, 100, 100);
        var child = Child(parent, x: 90);

        var finding = Assert.Single(Analyze([parent, child], [LayoutDiagnosticRules.ChildOutsideParent]).Findings);

        Assert.Equal("observation", finding.Outcome);
        Assert.Equal("info", finding.Severity);
        Assert.Equal("layout-parent", Assert.Single(finding.RelatedElements).Relation);
        Assert.Equal("parent", finding.RelatedElements[0].Element.Id);
        Assert.Equal(30, finding.Evidence!.LayoutOverflowInsetsPhysicalPixels!.Right);
        Assert.Null(finding.Evidence.OverflowInsetsPhysicalPixels);
        Assert.Equal(500, finding.Evidence.ParentRegion!.Bounds.X);
    }

    [Theory]
    [InlineData("scroll-parent")]
    [InlineData("transformed-child")]
    [InlineData("transformed-parent")]
    [InlineData("negative-margin")]
    [InlineData("other-window")]
    [InlineData("unknown-parent")]
    [InlineData("unrealized")]
    [InlineData("unknown-geometry")]
    public void Analyze_ExcludedChildOverflow_DoesNotClaimPass(string exclusion)
    {
        var parent = Node("parent", 100, 100);
        parent.IsLayoutContainer = true;
        var child = Child(parent, x: 90);
        switch (exclusion)
        {
            case "scroll-parent": parent.IsScrollable = true; break;
            case "transformed-child": child.HasVisualTransform = true; break;
            case "transformed-parent": parent.HasVisualTransform = true; break;
            case "negative-margin": child.HasNegativeMargin = true; break;
            case "other-window": child.WindowId = "window-1"; break;
            case "unknown-parent": child.Element.ParentId = "missing"; break;
            case "unrealized": child.IsRendered = false; break;
            case "unknown-geometry": child.GeometryAvailable = false; break;
        }

        var result = Analyze([parent, child], [LayoutDiagnosticRules.ChildOutsideParent], includePasses: true);

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Summary.Passes);
        Assert.Equal(2, result.Summary.NotApplicable);
    }

    [Fact]
    public void Analyze_ChildInsideParent_Passes()
    {
        var parent = Node("parent", 100, 100);
        parent.IsLayoutContainer = true;
        var result = Analyze([parent, Child(parent, x: 10)], [LayoutDiagnosticRules.ChildOutsideParent]);

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Summary.Passes);
        Assert.Equal(1, result.Summary.NotApplicable);
    }

    [Theory]
    [InlineData("native")]
    [InlineData("blazor")]
    public void Analyze_NoManagedSizingEvidence_IsNotApplicable(string framework)
    {
        var node = Node("unmeasured", 100, 40);
        node.Element.Framework = framework;
        node.Sizing = null;
        var result = Analyze([node], BaselineRules, includePasses: true);

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Summary.Passes);
        Assert.Equal(3, result.Summary.NotApplicable);
        Assert.Equal("partial", result.Coverage.Overall);
    }

    [Fact]
    public void Analyze_UnstableSizing_UsesGlobalIncompleteInsteadOfViolationOrPass()
    {
        var node = Node("target", 100, 40);
        node.Sizing!.MinimumWidth = 120;

        var result = Analyze([node], [LayoutDiagnosticRules.ConstraintViolation], includePasses: true, stable: false);

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Summary.Incomplete);
        Assert.Equal(0, result.Summary.Violations);
        Assert.Equal(0, result.Summary.Passes);
    }

    [Fact]
    public void Analyze_LargeUnstableTree_DoesNotCrowdOutExistingObservations()
    {
        var nodes = new List<LayoutNodeSnapshot>();
        var parent = Node("parent", 100, 100);
        parent.IsLayoutContainer = true;
        nodes.Add(parent);
        for (var index = 0; index < 600; index++)
        {
            var child = Child(parent, x: 90);
            child.Element.Id = $"child-{index}";
            child.Sizing!.MinimumWidth = 100;
            child.Sizing.DesiredWidth = 100;
            nodes.Add(child);
        }
        var legacy = Node("legacy-overflow", 100, 40);
        legacy.ContentRegion = LayoutRegionMath.FromRect(0, 0, 200, 40);
        nodes.Add(legacy);

        var result = Analyze(nodes, [.. BaselineRules, LayoutDiagnosticRules.ContentOverflow],
            includePasses: true, stable: false);

        Assert.Equal(LayoutDiagnosticRules.ContentOverflow, Assert.Single(result.Findings).RuleId);
        Assert.Equal(1, result.Summary.Incomplete);
        Assert.DoesNotContain(result.Findings, finding => BaselineRules.Contains(finding.RuleId));
    }

    [Fact]
    public void Analyze_AncestorTransform_SeparatesLayoutInsetsFromRenderedRegions()
    {
        var parent = Node("parent", 100, 100);
        parent.IsLayoutContainer = true;
        parent.FullRegion = LayoutRegionMath.FromRect(0, 0, 200, 200);
        parent.HasTransformedAncestor = true;
        var child = Child(parent, x: 90);
        child.FullRegion = LayoutRegionMath.FromRect(180, 0, 80, 80);
        child.HasTransformedAncestor = true;

        var finding = Assert.Single(Analyze([parent, child], [LayoutDiagnosticRules.ChildOutsideParent]).Findings);

        Assert.Equal(30, finding.Evidence!.LayoutOverflowInsetsPhysicalPixels!.Right);
        Assert.Null(finding.Evidence.OverflowInsetsPhysicalPixels);
        Assert.Equal(80, finding.Evidence.FullRegion!.Bounds.Width);
        Assert.Equal(40, finding.Evidence.Sizing!.ArrangedWidth);
        Assert.Contains("untransformed layout pixels", finding.Message);
        Assert.Contains(finding.Evidence.Limitations, limitation => limitation.Contains("visual transforms"));
    }

    [Fact]
    public void Snapshot_SizingChangeWithoutGeometryChange_InvalidatesStabilityAndDiagnostics()
    {
        var node = Node("target", 100, 40);
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(node);
        var geometry = capture.GeometryHash;
        var stability = capture.StabilityHash;
        var diagnostics = capture.DiagnosticsHash;

        node.Sizing!.MinimumWidth = 120;

        Assert.Equal(geometry, capture.GeometryHash);
        Assert.NotEqual(stability, capture.StabilityHash);
        Assert.NotEqual(diagnostics, capture.DiagnosticsHash);
    }

    [Fact]
    public void Analyze_NewRuleEvidence_RoundTripsToClientWithoutSchemaBump()
    {
        var node = Node("target", 100, 40);
        node.Sizing!.MinimumWidth = 120;
        var report = Analyze([node], ["LAYOUT.CONSTRAINT-VIOLATION"]);

        var clientReport = Driver.ProtocolJson.Deserialize<Driver.LayoutInspectionResult>(JsonSerializer.Serialize(report));

        Assert.Equal("1.0", clientReport!.SchemaVersion);
        Assert.Equal("1.1", clientReport.RuleSetVersion);
        var finding = Assert.Single(clientReport.Findings);
        Assert.Equal(Driver.LayoutDiagnosticRules.ConstraintViolation, finding.RuleId);
        Assert.Equal(120, finding.Evidence!.Sizing!.MinimumWidth);
        Assert.Null(finding.Evidence.Sizing.MaximumWidth);
    }

    [Fact]
    public void Analyze_SuppressedNewRule_DoesNotCountAsViolation()
    {
        var node = Node("target", 100, 40);
        node.Sizing!.MinimumWidth = 120;
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(node);
        var request = new LayoutInspectionRequest
        {
            Rules = [LayoutDiagnosticRules.ConstraintViolation],
            IncludeEvidence = false,
            Suppressions = [new LayoutSuppression { RuleId = LayoutDiagnosticRules.ConstraintViolation, AutomationId = "target" }]
        };

        var result = LayoutDiagnosticsEngine.Analyze(capture, request, "test", true, null, Support());

        var finding = Assert.Single(result.Findings);
        Assert.True(finding.Suppressed);
        Assert.Null(finding.Evidence);
        Assert.Equal(0, result.Summary.Violations);
        Assert.Equal(1, result.Summary.Suppressed);
    }

    [Fact]
    public void CaptureSizing_UnarrangedElement_HasNoEvidence()
        => Assert.Null(VisualTreeWalker.CaptureSizing(new Label()));

    [Fact]
    public void CaptureSizing_DesiredSizeMargins_AreRemovedWithoutRemeasuring()
    {
        var view = new MeasuredView { Margin = new Thickness(10), MinimumWidthRequest = 40 };
        ((IView)view).Measure(200, 100);
        view.Frame = new Rect(0, 0, 100, 40);
        var measurements = view.Measurements;

        var sizing = VisualTreeWalker.CaptureSizing(view);

        Assert.NotNull(sizing);
        Assert.Equal(100, sizing.ArrangedWidth);
        Assert.Equal(40, sizing.ArrangedHeight);
        Assert.Equal(100, sizing.DesiredWidth);
        Assert.Equal(40, sizing.DesiredHeight);
        Assert.Equal(measurements, view.Measurements);
        Assert.Equal(40, sizing.MinimumWidth);
        Assert.Null(sizing.MaximumWidth);
    }

    [Fact]
    public void CaptureLayoutSnapshot_NormalizedMarginsAndAncestorTransforms_AreSharedAcrossRules()
    {
        var view = new MeasuredView { AutomationId = "MarginProbe", Margin = new Thickness(10) };
        ((IView)view).Measure(200, 100);
        view.Frame = new Rect(10, 10, 100, 40);
        var parent = new Grid { Children = { view }, Scale = 2, Frame = new Rect(0, 0, 120, 60) };
        var page = new ContentPage { Content = parent, Frame = new Rect(0, 0, 200, 200) };
        var app = new Application();
        typeof(Application).GetMethod("AddWindow",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(app, [new Window(page)]);
        var measurements = view.Measurements;

        var capture = new VisualTreeWalker().CaptureLayoutSnapshot(app, new LayoutInspectionRequest());
        var node = Assert.Single(capture.Nodes, node => node.Element.AutomationId == "MarginProbe");

        Assert.Equal(100, node.Sizing!.DesiredWidth);
        Assert.Equal(100, node.ContentRegion!.Bounds.Width);
        Assert.Equal(40, node.ContentRegion.Bounds.Height);
        Assert.True(node.HasTransformedAncestor);
        Assert.Equal(measurements, view.Measurements);
    }

    private sealed class MeasuredView : View
    {
        public int Measurements { get; private set; }

        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            Measurements++;
            return new Size(120, 60);
        }
    }

    private static readonly string[] BaselineRules =
    [
        LayoutDiagnosticRules.ConstraintViolation,
        LayoutDiagnosticRules.DesiredSizeConstrained,
        LayoutDiagnosticRules.ChildOutsideParent
    ];

    private static IReadOnlyList<LayoutRuleSupportInfo> Support() => new VisualTreeWalker().GetLayoutRuleSupport();

    private static LayoutNodeSnapshot Node(string id, double width, double height) => new()
    {
        Element = new ElementInfo { Id = id, AutomationId = id, Type = "Grid", IsVisible = true },
        LayoutRegion = LayoutRegionMath.FromRect(0, 0, width, height),
        FullRegion = LayoutRegionMath.FromRect(0, 0, width, height),
        VisibleRegion = LayoutRegionMath.FromRect(0, 0, width, height),
        Sizing = new LayoutSizingEvidence { ArrangedWidth = width, ArrangedHeight = height, DesiredWidth = width, DesiredHeight = height }
    };

    private static LayoutNodeSnapshot Child(LayoutNodeSnapshot parent, double x)
    {
        var child = Node("child", 40, 40);
        child.Element.ParentId = parent.Element.Id;
        child.ParentRelativeBounds = new LayoutRectInfo { X = x, Width = 40, Height = 40 };
        return child;
    }

    private static LayoutInspectionResult Analyze(
        IEnumerable<LayoutNodeSnapshot> nodes,
        IEnumerable<string> rules,
        bool includePasses = false,
        string minimumSeverity = "info",
        bool stable = true)
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.AddRange(nodes);
        return LayoutDiagnosticsEngine.Analyze(capture,
            new LayoutInspectionRequest { Rules = rules.ToList(), IncludePasses = includePasses, MinimumSeverity = minimumSeverity },
            "test", stable, stable ? null : "Geometry is changing.", Support());
    }
}
