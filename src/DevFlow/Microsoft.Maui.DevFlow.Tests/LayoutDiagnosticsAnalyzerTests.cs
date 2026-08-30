using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Pins the layout diagnostics rule contract.
///
/// These tests are the guard rail against the failure mode this subsystem exists to avoid:
/// confidently reporting something the runtime cannot actually prove. Each rule is checked for the
/// outcome AND the confidence it is allowed to claim, unavailable geometry must surface as
/// <c>incomplete</c> (never a pass), findings must be deterministically ordered, and no captured
/// field may carry element text or values.
/// </summary>
public class LayoutDiagnosticsAnalyzerTests
{
    private static readonly DateTime CapturedUtc = new(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc);

    // ── visible-zero-area ────────────────────────────────────────────────────────────────────

    [Fact]
    public void VisibleZeroArea_IsAHighConfidenceViolation()
    {
        var report = Analyze([
            Element("root", frame: Rect(0, 0, 100, 100)),
            Element("child", parentId: "root", frame: Rect(0, 0, 0, 20)),
        ]);

        var finding = Assert.Single(report.Findings, f => f.RuleId == LayoutDiagnosticRules.VisibleZeroArea);
        Assert.Equal(LayoutOutcomes.Violation, finding.Outcome);
        Assert.Equal(LayoutConfidence.High, finding.Confidence);
        Assert.Equal("child", finding.Element!.Id);
        Assert.Contains("width", finding.Message, StringComparison.OrdinalIgnoreCase);
        // The report must own up to the benign case that produces identical geometry.
        Assert.Contains(finding.Limitations, limitation => limitation.Contains("collapsed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VisibleZeroArea_IgnoresHiddenAndUnrealizedElements()
    {
        var report = Analyze([
            Element("hidden", frame: Rect(0, 0, 0, 0), isVisible: false),
            Element("unrealized", frame: Rect(0, 0, 0, 0), isRealized: false),
        ]);

        Assert.DoesNotContain(report.Findings, f =>
            f.RuleId == LayoutDiagnosticRules.VisibleZeroArea && f.Outcome == LayoutOutcomes.Violation);
    }

    [Fact]
    public void VisibleZeroArea_ReportsMissingGeometryAsIncompleteNotPass()
    {
        var report = Analyze([Element("noframe", frame: null)]);

        var finding = Assert.Single(report.Findings, f =>
            f.RuleId == LayoutDiagnosticRules.VisibleZeroArea && f.Outcome == LayoutOutcomes.Incomplete);
        Assert.Equal(1, finding.Evidence!.AffectedElements);
        Assert.Equal(0, report.Summary.Violations);
        Assert.Equal(0, report.Summary.Observations);
        Assert.All(report.Findings, f => Assert.Equal(LayoutOutcomes.Incomplete, f.Outcome));

        var coverage = Assert.Single(report.Coverage.Rules, r => r.RuleId == LayoutDiagnosticRules.VisibleZeroArea);
        Assert.Equal(LayoutRuleSupport.Unavailable, coverage.Support);
        Assert.Equal(1, coverage.Skipped);
    }

    [Fact]
    public void VisibleZeroArea_UsesWindowBoundsWhenManagedFrameIsUnset()
    {
        var element = Element("shell", frame: null);
        element.WindowBounds = Rect(0, 0, 1000, 700);

        var report = Analyze([element]);

        Assert.DoesNotContain(report.Findings, finding =>
            finding.RuleId == LayoutDiagnosticRules.VisibleZeroArea &&
            finding.Outcome == LayoutOutcomes.Violation);
        var coverage = Assert.Single(
            report.Coverage.Rules,
            rule => rule.RuleId == LayoutDiagnosticRules.VisibleZeroArea);
        Assert.Equal(1, coverage.Evaluated);
        Assert.Equal(0, coverage.Skipped);
    }

    [Fact]
    public void VisibleUnrealizedElement_MakesCoveragePartialInsteadOfFull()
    {
        var report = Analyze([
            Element("realized", frame: Rect(0, 0, 10, 10)),
            Element("unrealized", frame: Rect(0, 0, 10, 10), isRealized: false),
        ]);

        var coverage = Assert.Single(
            report.Coverage.Rules,
            rule => rule.RuleId == LayoutDiagnosticRules.VisibleZeroArea);
        Assert.Equal(LayoutRuleSupport.Partial, coverage.Support);
        Assert.Equal(1, coverage.Evaluated);
        Assert.Equal(1, coverage.Skipped);
        Assert.Contains(report.Findings, finding =>
            finding.RuleId == LayoutDiagnosticRules.VisibleZeroArea &&
            finding.Outcome == LayoutOutcomes.Incomplete);
    }

    // ── constraint violations ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(120d, 200d, null, "minimumWidth")]
    [InlineData(320d, null, 200d, "maximumWidth")]
    public void ConstraintViolation_IsAHighConfidenceViolation(
        double actualWidth, double? minimumWidth, double? maximumWidth, string expectedConstraint)
    {
        var element = Element("e", frame: Rect(0, 0, actualWidth, 40));
        element.MinimumWidth = minimumWidth;
        element.MaximumWidth = maximumWidth;

        var report = Analyze([element]);

        var finding = Assert.Single(report.Findings, f => f.RuleId == LayoutDiagnosticRules.ConstraintViolation);
        Assert.Equal(LayoutOutcomes.Violation, finding.Outcome);
        Assert.Equal(LayoutConfidence.High, finding.Confidence);
        Assert.Equal(expectedConstraint, finding.Evidence!.Constraint);
        Assert.Equal(actualWidth, finding.Evidence.ActualValue);
    }

    [Fact]
    public void ConstraintViolation_ToleratesSubPixelRounding()
    {
        var element = Element("e", frame: Rect(0, 0, 199.8, 40));
        element.MinimumWidth = 200;

        var report = Analyze([element]);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == LayoutDiagnosticRules.ConstraintViolation);
    }

    [Fact]
    public void ConstraintViolation_IsNotEvaluatedWithoutDeclaredConstraints()
    {
        var report = Analyze([Element("e", frame: Rect(0, 0, 100, 40))]);

        var coverage = Assert.Single(report.Coverage.Rules, r => r.RuleId == LayoutDiagnosticRules.ConstraintViolation);
        Assert.Equal(0, coverage.Evaluated);
        Assert.Equal(0, coverage.Skipped);
        Assert.Equal(LayoutRuleSupport.Unavailable, coverage.Support);
    }

    // ── outside-window ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void OutsideWindow_IsAMediumConfidenceObservationWhenWhollyOffSurface()
    {
        var element = Element("offscreen", frame: Rect(0, 0, 100, 40));
        element.WindowBounds = Rect(1500, 10, 100, 40);

        var report = Analyze([element], window: Rect(0, 0, 1000, 800));

        var finding = Assert.Single(report.Findings, f => f.RuleId == LayoutDiagnosticRules.OutsideWindow);
        Assert.Equal(LayoutOutcomes.Observation, finding.Outcome);
        Assert.Equal(LayoutConfidence.Medium, finding.Confidence);
        Assert.Contains(finding.Limitations, limitation =>
            limitation.Contains("off-canvas", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OutsideWindow_IgnoresPartiallyVisibleElements()
    {
        var element = Element("partial", frame: Rect(0, 0, 100, 40));
        element.WindowBounds = Rect(960, 10, 100, 40);

        var report = Analyze([element], window: Rect(0, 0, 1000, 800));

        Assert.DoesNotContain(report.Findings, f =>
            f.RuleId == LayoutDiagnosticRules.OutsideWindow && f.Outcome == LayoutOutcomes.Observation);
    }

    [Fact]
    public void OutsideWindow_IsIncompleteWhenWindowBoundsAreUnknown()
    {
        var element = Element("e", frame: Rect(0, 0, 100, 40));
        element.WindowBounds = Rect(0, 0, 100, 40);

        var report = Analyze([element], window: null);

        Assert.Single(report.Findings, f =>
            f.RuleId == LayoutDiagnosticRules.OutsideWindow && f.Outcome == LayoutOutcomes.Incomplete);
        Assert.Contains(report.Coverage.Limitations, limitation =>
            limitation.Contains("Window bounds were unavailable", StringComparison.Ordinal));
    }

    // ── desired-size-constrained ─────────────────────────────────────────────────────────────

    [Fact]
    public void DesiredSizeConstrained_IsAnObservationThatExplainsTheBenignCase()
    {
        var element = Element("e", frame: Rect(0, 0, 120, 20));
        element.DesiredSize = new LayoutSize { Width = 300, Height = 20 };

        var report = Analyze([element]);

        var finding = Assert.Single(report.Findings, f => f.RuleId == LayoutDiagnosticRules.DesiredSizeConstrained);
        Assert.Equal(LayoutOutcomes.Observation, finding.Outcome);
        Assert.NotEqual(LayoutOutcomes.Violation, finding.Outcome);
        Assert.Contains("normal", finding.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(180, finding.Evidence!.OverflowWidth);
        Assert.Null(finding.Evidence.OverflowHeight);
    }

    [Fact]
    public void DesiredSizeConstrained_IgnoresImmaterialOverflow()
    {
        var element = Element("e", frame: Rect(0, 0, 300, 20));
        element.DesiredSize = new LayoutSize { Width = 300.4, Height = 20 };

        var report = Analyze([element]);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == LayoutDiagnosticRules.DesiredSizeConstrained);
    }

    // ── child-outside-parent ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ChildOutsideParent_IsALowConfidenceObservationOnly()
    {
        var parent = Element("parent", frame: Rect(0, 0, 100, 100));
        parent.WindowBounds = Rect(0, 0, 100, 100);
        var child = Element("child", parentId: "parent", frame: Rect(0, 0, 200, 40));
        child.WindowBounds = Rect(0, 0, 200, 40);

        var report = Analyze([parent, child], window: Rect(0, 0, 1000, 800));

        var finding = Assert.Single(report.Findings, f => f.RuleId == LayoutDiagnosticRules.ChildOutsideParent);
        Assert.Equal(LayoutOutcomes.Observation, finding.Outcome);
        Assert.Equal(LayoutConfidence.Low, finding.Confidence);
        Assert.Equal("parent", finding.Parent!.Id);
        Assert.Contains(finding.Limitations, limitation =>
            limitation.Contains("Overflow is not clipping", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeVisibleRegion_ProducesClippingObservation()
    {
        var element = Element("clipped", frame: Rect(0, 0, 100, 100));
        element.FullRegion = Region(0, 0, 100, 100, "exact");
        element.VisibleRegion = Region(0, 0, 50, 100, "exact");

        var report = Analyze(
            [element],
            request: new LayoutInspectionRequest
            {
                Rules = [LayoutDiagnosticRules.ElementClipped],
            });

        var finding = Assert.Single(report.Findings);
        Assert.Equal(LayoutDiagnosticRules.ElementClipped, finding.RuleId);
        Assert.Equal(LayoutOutcomes.Observation, finding.Outcome);
        Assert.Equal(0.5, finding.Evidence!.LostAreaRatio);
    }

    [Fact]
    public void NativeTextLayout_ProducesExactTruncationObservation()
    {
        var element = Element("text", frame: Rect(0, 0, 100, 20));
        element.TextEvidence = new LayoutTextEvidence
        {
            IsTruncated = true,
            EllipsisCount = 1,
            MeasurementSource = "android-layout",
        };

        var report = Analyze(
            [element],
            request: new LayoutInspectionRequest
            {
                Rules = [LayoutDiagnosticRules.TextNotFullyRendered],
            });

        var finding = Assert.Single(report.Findings);
        Assert.Equal(LayoutConfidence.Exact, finding.Confidence);
        Assert.Equal(1, finding.Evidence!.Text!.EllipsisCount);
    }

    [Fact]
    public void SampledHitTesting_ReportsOnlyInteractionOcclusion()
    {
        var element = Element("button", frame: Rect(0, 0, 100, 40));
        element.Interactive = true;
        element.HitTestSampleCount = 5;
        element.BlockedHitTestSampleCount = 2;

        var report = Analyze(
            [element],
            request: new LayoutInspectionRequest
            {
                Rules =
                [
                    LayoutDiagnosticRules.InteractionOccluded,
                    LayoutDiagnosticRules.VisualOccluded,
                ],
            });

        Assert.Single(report.Findings, finding =>
            finding.RuleId == LayoutDiagnosticRules.InteractionOccluded);
        Assert.DoesNotContain(report.Findings, finding =>
            finding.RuleId == LayoutDiagnosticRules.VisualOccluded &&
            finding.Outcome != LayoutOutcomes.Incomplete);
    }

    [Fact]
    public void SiblingRectangleOverlap_DoesNotClaimVisualOcclusion()
    {
        var first = Element("first", frame: Rect(0, 0, 100, 100));
        first.WindowBounds = Rect(0, 0, 100, 100);
        var second = Element("second", frame: Rect(0, 0, 100, 100));
        second.WindowBounds = Rect(50, 0, 100, 100);

        var report = Analyze(
            [first, second],
            request: new LayoutInspectionRequest
            {
                Rules =
                [
                    LayoutDiagnosticRules.GeometricOverlap,
                    LayoutDiagnosticRules.VisualOccluded,
                ],
            });

        var overlap = Assert.Single(report.Findings, finding =>
            finding.RuleId == LayoutDiagnosticRules.GeometricOverlap);
        Assert.Equal(LayoutOutcomes.Observation, overlap.Outcome);
        Assert.DoesNotContain(report.Findings, finding =>
            finding.RuleId == LayoutDiagnosticRules.VisualOccluded &&
            finding.Outcome != LayoutOutcomes.Incomplete);
    }

    [Fact]
    public void Overlap_DoesNotCrossWindowCoordinateSpaces()
    {
        var first = Element("first", frame: Rect(0, 0, 100, 100));
        first.WindowBounds = Rect(0, 0, 100, 100);
        first.WindowId = "window-0";
        var second = Element("second", frame: Rect(0, 0, 100, 100));
        second.WindowBounds = Rect(0, 0, 100, 100);
        second.WindowId = "window-1";

        var report = Analyze(
            [first, second],
            request: new LayoutInspectionRequest
            {
                Rules = [LayoutDiagnosticRules.GeometricOverlap],
            });

        Assert.DoesNotContain(report.Findings, finding =>
            finding.RuleId == LayoutDiagnosticRules.GeometricOverlap &&
            finding.Outcome == LayoutOutcomes.Observation);
        Assert.Equal(["window-0", "window-1"], report.Snapshot.Windows.Select(window => window.Id));
    }

    [Fact]
    public void OverlapPassAccounting_MarksBothParticipantsAsHavingFindings()
    {
        var first = Element("first", frame: Rect(0, 0, 100, 100));
        first.WindowBounds = Rect(0, 0, 100, 100);
        var second = Element("second", frame: Rect(0, 0, 100, 100));
        second.WindowBounds = Rect(50, 0, 100, 100);

        var report = Analyze(
            [first, second],
            request: new LayoutInspectionRequest
            {
                Rules = [LayoutDiagnosticRules.GeometricOverlap],
                IncludePasses = true,
            });

        Assert.Equal(0, report.Summary.Passes);
    }

    [Fact]
    public void Report_BoundsFindingsAndReportsOmissions()
    {
        var snapshots = Enumerable.Range(0, LayoutDiagnosticsFormat.MaxFindings + 25)
            .Select(index => Element($"zero-{index}", frame: Rect(0, 0, 0, 10)))
            .ToList();

        var report = Analyze(
            snapshots,
            request: new LayoutInspectionRequest
            {
                Rules = [LayoutDiagnosticRules.VisibleZeroArea],
            });

        Assert.Equal(LayoutDiagnosticsFormat.MaxFindings, report.Findings.Count);
        Assert.Equal(25, report.Summary.OmittedFindings);
        Assert.Contains(report.Coverage.Limitations, limitation =>
            limitation.Contains("omitted 25", StringComparison.Ordinal));
    }

    // ── report-level guarantees ──────────────────────────────────────────────────────────────

    [Fact]
    public void Report_NeverClaimsClippingOcclusionTruncationOrAccessibility()
    {
        var parent = Element("parent", frame: Rect(0, 0, 100, 100));
        parent.WindowBounds = Rect(0, 0, 100, 100);
        var child = Element("child", parentId: "parent", frame: Rect(0, 0, 0, 0));
        child.WindowBounds = Rect(0, 0, 200, 40);
        child.DesiredSize = new LayoutSize { Width = 400, Height = 40 };

        var report = Analyze([parent, child], window: Rect(0, 0, 1000, 800));
        var json = JsonSerializer.Serialize(report);

        foreach (var forbidden in new[] { "clipped", "occluded", "truncated text", "accessibility mismatch" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Report_OrdersFindingsDeterministically()
    {
        LayoutElementSnapshot[] Snapshots()
        {
            var parent = Element("parent", frame: Rect(0, 0, 100, 100));
            parent.WindowBounds = Rect(0, 0, 100, 100);
            var overflowing = Element("overflow", parentId: "parent", frame: Rect(0, 0, 200, 40));
            overflowing.WindowBounds = Rect(0, 0, 200, 40);
            var zero = Element("zero", parentId: "parent", frame: Rect(0, 0, 0, 0));
            zero.WindowBounds = Rect(0, 0, 0, 0);
            var missing = Element("missing", parentId: "parent", frame: null);
            return [parent, overflowing, zero, missing];
        }

        var first = Analyze(Snapshots(), window: Rect(0, 0, 1000, 800));
        var second = Analyze(Snapshots(), window: Rect(0, 0, 1000, 800));

        Assert.Equal(
            first.Findings.Select(f => f.Id),
            second.Findings.Select(f => f.Id));
        // Violations first, then observations, then incomplete.
        var outcomes = first.Findings.Select(f => f.Outcome).ToList();
        Assert.Equal(outcomes.OrderBy(OutcomeRank).ToList(), outcomes);
    }

    [Fact]
    public void Report_AlwaysPublishesLimitationsAndTheNeverCapturedList()
    {
        var report = Analyze([Element("e", frame: Rect(0, 0, 10, 10))]);

        Assert.Contains(report.Coverage.Limitations, limitation =>
            limitation.Contains("managed MAUI layout state", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Element Text/Value content", report.Coverage.NeverCaptured);
        Assert.Equal(LayoutDiagnosticsFormat.SchemaVersion, report.SchemaVersion);
        Assert.Equal(LayoutDiagnosticsFormat.RuleSetVersion, report.RuleSetVersion);
        Assert.Equal(LayoutDiagnosticRules.Managed.Count, report.Coverage.Rules.Count);
    }

    [Fact]
    public void Report_MarksTruncatedScopesInItsLimitations()
    {
        var scope = new LayoutDiagnosticsScope { MaxElements = 1, ElementsExamined = 1, Truncated = true };
        var report = LayoutDiagnosticsAnalyzer.Analyze(
            [Element("e", frame: Rect(0, 0, 10, 10))], scope, "Windows", CapturedUtc);

        Assert.Contains(report.Coverage.Limitations, limitation =>
            limitation.Contains("element budget", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Snapshot_CarriesNoTextOrValueFields()
    {
        var snapshot = Element("e", frame: Rect(0, 0, 10, 10));
        var json = JsonSerializer.Serialize(snapshot);

        Assert.DoesNotContain("\"text\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"value\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"frame\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RichRequest_AddsSnapshotMetadataAndAppliesSuppression()
    {
        var target = Element("target", frame: Rect(0, 0, 0, 20));
        target.AutomationId = "Target";
        target.Interactive = true;
        target.SourceFile = @"Views\MainPage.xaml";
        target.SourceLine = 42;
        var request = new LayoutInspectionRequest
        {
            Rules = [LayoutDiagnosticRules.VisibleZeroArea],
            Suppressions =
            [
                new LayoutSuppression
                {
                    RuleId = LayoutDiagnosticRules.VisibleZeroArea,
                    AutomationId = "Target",
                    Reason = "Intentional collapsed host",
                }
            ],
        };

        var report = Analyze([target], request: request);

        var finding = Assert.Single(report.Findings);
        Assert.True(finding.Suppressed);
        Assert.Equal("Intentional collapsed host", finding.SuppressionReason);
        Assert.Equal(LayoutSeverity.Serious, finding.Severity);
        Assert.Equal(LayoutActionability.Fix, finding.Actionability);
        Assert.NotEmpty(finding.SuppressionKey);
        Assert.NotNull(finding.Evidence?.FullRegion);
        Assert.Equal(1, report.Summary.Suppressed);
        Assert.Equal(0, report.Summary.Violations);
        Assert.Equal(LayoutDiagnosticsFormat.SchemaVersion, report.SchemaVersion);
        Assert.NotEmpty(report.Snapshot.Id);
        Assert.NotEmpty(report.Snapshot.TreeRevision);
        Assert.NotEmpty(report.Snapshot.DiagnosticsRevision);
        Assert.Equal(1, report.Snapshot.NodeCount);
    }

    // ── suppression key stability ────────────────────────────────────────────────────────────
    //
    // A suppression fingerprint is written into `.mauidevflow` and has to keep matching after the
    // page is rebuilt and after the app restarts. Runtime element ids survive neither, so they must
    // never reach the key. Every case here drives the real analyzer end to end, so it exercises the
    // production key builder rather than a copy of it.

    [Fact]
    public void SuppressionKey_IsStableAcrossReconstructedRuntimeIds()
    {
        var first = OverflowKey(
            childId: "element-0007",
            parentId: "element-0003",
            parentAutomationId: "Host");
        var second = OverflowKey(
            childId: "pid-48213-node-91",
            parentId: "pid-48213-node-42",
            parentAutomationId: "Host");

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void SuppressionKey_IgnoresRelatedElementsThatHaveOnlyARuntimeId()
    {
        // A related element with no AutomationId, no source location, and no type is known only by
        // its runtime id. It contributes nothing rather than destabilising the key, so two walks
        // that assigned different ids agree.
        var first = OverflowKey(
            childId: "element-0007",
            parentId: "element-0003",
            parentAutomationId: null,
            parentType: "");
        var second = OverflowKey(
            childId: "pid-48213-node-91",
            parentId: "pid-48213-node-42",
            parentAutomationId: null,
            parentType: "");

        Assert.Equal(first, second);
    }

    [Fact]
    public void SuppressionKey_StillDistinguishesStableRelatedElements()
    {
        var host = OverflowKey(childId: "c", parentId: "p", parentAutomationId: "Host");
        var otherHost = OverflowKey(childId: "c", parentId: "p", parentAutomationId: "OtherHost");
        var typeOnly = OverflowKey(childId: "c", parentId: "p", parentAutomationId: null, parentType: "ScrollView");
        var anonymous = OverflowKey(childId: "c", parentId: "p", parentAutomationId: null, parentType: "");
        var sourced = OverflowKey(
            childId: "c",
            parentId: "p",
            parentAutomationId: null,
            parentType: "ScrollView",
            parentSourceFile: "Views/HostPage.xaml",
            parentSourceLine: 11);

        Assert.NotEqual(host, otherHost);
        Assert.NotEqual(typeOnly, anonymous);
        Assert.NotEqual(host, typeOnly);
        Assert.NotEqual(typeOnly, sourced);
    }

    [Fact]
    public void SuppressionKey_DistinguishesTheSubjectElementsOwnStableIdentity()
    {
        var baseline = OverflowKey(childId: "c", parentId: "p", parentAutomationId: "Host");
        var otherLine = OverflowKey(childId: "c", parentId: "p", parentAutomationId: "Host", childSourceLine: 43);
        var otherAutomationId = OverflowKey(
            childId: "c",
            parentId: "p",
            parentAutomationId: "Host",
            childAutomationId: "Other");
        var otherFile = OverflowKey(
            childId: "c",
            parentId: "p",
            parentAutomationId: "Host",
            childSourceFile: "Views/OtherPage.xaml");

        Assert.NotEqual(baseline, otherLine);
        Assert.NotEqual(baseline, otherAutomationId);
        Assert.NotEqual(baseline, otherFile);
    }

    [Fact]
    public void SuppressionKey_NormalizesSourcePathSeparators()
    {
        var windowsPath = OverflowKey(
            childId: "c",
            parentId: "p",
            parentAutomationId: "Host",
            childSourceFile: @"Views\MainPage.xaml");
        var posixPath = OverflowKey(
            childId: "c",
            parentId: "p",
            parentAutomationId: "Host",
            childSourceFile: "Views/MainPage.xaml");

        Assert.Equal(windowsPath, posixPath);
    }

    [Fact]
    public void SuppressionKey_NeverEmbedsARuntimeElementId()
    {
        var report = Analyze([
            Element("root-runtime-id", frame: Rect(0, 0, 100, 100)),
            Element("child-runtime-id", parentId: "root-runtime-id", frame: Rect(0, 0, 0, 20)),
        ]);

        Assert.NotEmpty(report.Findings);
        Assert.All(report.Findings, finding =>
        {
            Assert.NotEmpty(finding.SuppressionKey);
            Assert.DoesNotContain("runtime-id", finding.SuppressionKey, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Produces the suppression key of one <c>layout.child-outside-parent</c> finding. That rule
    /// attaches the parent as a related element, so it exercises both halves of the key.
    /// </summary>
    private static string OverflowKey(
        string childId,
        string parentId,
        string? parentAutomationId,
        string parentType = "Grid",
        string? parentSourceFile = null,
        int? parentSourceLine = null,
        string? childAutomationId = "Target",
        string? childSourceFile = "Views/MainPage.xaml",
        int? childSourceLine = 42)
    {
        var parent = Element(parentId, frame: Rect(0, 0, 100, 100));
        parent.Type = parentType;
        parent.AutomationId = parentAutomationId;
        parent.SourceFile = parentSourceFile;
        parent.SourceLine = parentSourceLine;
        parent.WindowBounds = Rect(0, 0, 100, 100);

        var child = Element(childId, parentId: parentId, frame: Rect(0, 0, 200, 20));
        child.AutomationId = childAutomationId;
        child.SourceFile = childSourceFile;
        child.SourceLine = childSourceLine;
        child.WindowBounds = Rect(0, 0, 200, 20);

        var report = Analyze(
            [parent, child],
            request: new LayoutInspectionRequest
            {
                Rules = [LayoutDiagnosticRules.ChildOutsideParent],
            });
        var finding = Assert.Single(
            report.Findings,
            candidate => candidate.Outcome == LayoutOutcomes.Observation);
        Assert.Equal(childId, finding.Element!.Id);
        return finding.SuppressionKey;
    }

    [Fact]
    public void SuppressionIgnoreMode_AnnotatesWithoutHidingTheFinding()
    {
        var target = Element("target", frame: Rect(0, 0, 0, 20));
        target.AutomationId = "Target";

        var report = Analyze(
            [target],
            request: new LayoutInspectionRequest
            {
                Rules = [LayoutDiagnosticRules.VisibleZeroArea],
                SuppressionMode = LayoutSuppressionModes.Ignore,
                Suppressions =
                [
                    new LayoutSuppression
                    {
                        RuleId = LayoutDiagnosticRules.VisibleZeroArea,
                        AutomationId = "Target",
                    },
                ],
            });

        var finding = Assert.Single(report.Findings);
        Assert.True(finding.WouldSuppress);
        Assert.False(finding.Suppressed);
        Assert.Equal(1, report.Summary.Violations);
        Assert.Equal(0, report.Summary.Suppressed);
        Assert.Equal(1, report.Summary.GeneratedFindings);
        Assert.Equal(1, report.Summary.ActiveFindings);
    }

    [Fact]
    public void SuppressionOffMode_DoesNotMatchPolicyEntries()
    {
        var target = Element("target", frame: Rect(0, 0, 0, 20));
        target.AutomationId = "Target";

        var report = Analyze(
            [target],
            request: new LayoutInspectionRequest
            {
                Rules = [LayoutDiagnosticRules.VisibleZeroArea],
                SuppressionMode = LayoutSuppressionModes.Off,
                Suppressions =
                [
                    new LayoutSuppression
                    {
                        RuleId = LayoutDiagnosticRules.VisibleZeroArea,
                        AutomationId = "Target",
                    },
                ],
            });

        var finding = Assert.Single(report.Findings);
        Assert.False(finding.WouldSuppress);
        Assert.False(finding.Suppressed);
    }

    [Fact]
    public void RichRequest_FiltersFindingsWithoutTurningThemIntoPasses()
    {
        var target = Element("target", frame: Rect(0, 0, 120, 20));
        target.DesiredSize = new LayoutSize { Width = 300, Height = 20 };
        var request = new LayoutInspectionRequest
        {
            Rules = [LayoutDiagnosticRules.DesiredSizeConstrained],
            MinimumSeverity = LayoutSeverity.Serious,
        };

        var report = Analyze([target], request: request);

        Assert.Empty(report.Findings);
        Assert.Equal(0, report.Summary.Observations);
        Assert.Equal(0, report.Summary.Passes);
    }

    [Fact]
    public void RichRequest_CanIncludeAggregatePassAccounting()
    {
        var request = new LayoutInspectionRequest
        {
            Rules = [LayoutDiagnosticRules.VisibleZeroArea],
            IncludePasses = true,
        };

        var report = Analyze(
            [Element("target", frame: Rect(0, 0, 20, 20))],
            request: request);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(LayoutOutcomes.Pass, finding.Outcome);
        Assert.Equal(LayoutSeverity.Info, finding.Severity);
        Assert.Equal(LayoutActionability.Informational, finding.Actionability);
        Assert.Equal(1, report.Summary.Passes);
        Assert.Contains("coverage", finding.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RichRequest_CountsPassingElementsInsteadOfFindings()
    {
        var failing = Element("failing", frame: Rect(0, 0, 50, 50));
        failing.MinimumWidth = 100;
        failing.MinimumHeight = 100;
        var passing = Element("passing", frame: Rect(0, 0, 150, 150));
        passing.MinimumWidth = 100;
        passing.MinimumHeight = 100;

        var report = Analyze(
            [failing, passing],
            request: new LayoutInspectionRequest
            {
                Rules = [LayoutDiagnosticRules.ConstraintViolation],
                IncludePasses = true,
            });

        Assert.Equal(2, report.Summary.Violations);
        Assert.Equal(1, report.Summary.Passes);
        var pass = Assert.Single(report.Findings, finding => finding.Outcome == LayoutOutcomes.Pass);
        Assert.Contains("1 evaluated element", pass.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RichRequest_NormalizesRuleIdsCaseInsensitively()
    {
        var report = Analyze(
            [Element("target", frame: Rect(0, 0, 0, 20))],
            request: new LayoutInspectionRequest
            {
                Rules = [LayoutDiagnosticRules.VisibleZeroArea.ToUpperInvariant()],
            });

        Assert.Equal(
            LayoutDiagnosticRules.VisibleZeroArea,
            Assert.Single(report.Findings).RuleId);
    }

    [Fact]
    public void RuleCatalog_ListsTheFullContractAndTruthfulManagedSupport()
    {
        var catalog = LayoutDiagnosticsAnalyzer.CreateRuleCatalog();

        Assert.Equal(LayoutDiagnosticRules.All.Count, catalog.Rules.Count);
        Assert.Equal(
            LayoutRuleSupport.Unavailable,
            Assert.Single(catalog.Rules, rule =>
                rule.RuleId == LayoutDiagnosticRules.ElementClipped).Support);
        Assert.Equal(
            LayoutRuleSupport.Partial,
            Assert.Single(catalog.Rules, rule =>
                rule.RuleId == LayoutDiagnosticRules.VisibleZeroArea).Support);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static int OutcomeRank(string outcome) => outcome switch
    {
        LayoutOutcomes.Violation => 0,
        LayoutOutcomes.Observation => 1,
        _ => 2,
    };

    private static LayoutDiagnosticsReport Analyze(
        IReadOnlyList<LayoutElementSnapshot> snapshots,
        LayoutRect? window = null,
        LayoutInspectionRequest? request = null)
    {
        var scope = new LayoutDiagnosticsScope
        {
            MaxElements = LayoutDiagnosticsFormat.DefaultMaxElements,
            ElementsExamined = snapshots.Count,
            WindowBounds = window,
        };
        return LayoutDiagnosticsAnalyzer.Analyze(snapshots, scope, "Windows", CapturedUtc, request);
    }

    private static LayoutElementSnapshot Element(
        string id,
        string? parentId = null,
        LayoutRect? frame = null,
        bool isVisible = true,
        bool isRealized = true)
        => new()
        {
            Id = id,
            ParentId = parentId,
            Type = "Label",
            IsVisible = isVisible,
            IsEnabled = true,
            IsRealized = isRealized,
            HasLayoutState = true,
            Frame = frame,
        };

    private static LayoutRect Rect(double x, double y, double width, double height)
        => new() { X = x, Y = y, Width = width, Height = height };

    private static LayoutRegionInfo Region(
        double x,
        double y,
        double width,
        double height,
        string precision)
        => new()
        {
            Bounds = Rect(x, y, width, height),
            Area = width * height,
            Precision = precision,
        };
}
