using Microsoft.Maui.DevFlow.Agent.Core;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Tests;

public class LayoutDiagnosticsEngineTests
{
    [Fact]
    public void Intersect_PolygonsWithOverlappingBoundsButNoSharedArea_ReturnsEmpty()
    {
        var first = LayoutRegionMath.FromPoints(
        [
            new LayoutPointInfo { X = 0, Y = 0 },
            new LayoutPointInfo { X = 10, Y = 0 },
            new LayoutPointInfo { X = 0, Y = 10 }
        ]);
        var second = LayoutRegionMath.FromPoints(
        [
            new LayoutPointInfo { X = 10, Y = 10 },
            new LayoutPointInfo { X = 10, Y = 2 },
            new LayoutPointInfo { X = 2, Y = 10 }
        ]);

        var intersection = LayoutRegionMath.Intersect(first, second);

        Assert.Equal(0, intersection.Area, 6);
    }

    [Fact]
    public void RegionFromInfo_PreservesKnownZeroAreaBounds()
    {
        var region = VisualTreeWalker.RegionFromInfo(new ElementInfo
        {
            Id = "zero",
            Type = "Button",
            WindowBounds = new BoundsInfo
            {
                X = 10,
                Y = 20,
                Width = 0,
                Height = 40
            }
        });

        Assert.Equal("exactRect", region.Precision);
        Assert.Equal(0, region.Area);
        Assert.Equal(10, region.Bounds.X);
        Assert.Equal(40, region.Bounds.Height);
    }

    [Fact]
    public void Analyze_AncestorClipOnInteractiveElement_ReportsViolationWithClipper()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Windows.Add(new LayoutWindowInfo
        {
            Id = "window-0",
            Bounds = new LayoutRectInfo { Width = 200, Height = 200 }
        });
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "button",
                ParentId = "grid",
                Type = "Button",
                AutomationId = "Submit",
                IsVisible = true,
                IsEnabled = true,
                Traits = ["interactive"]
            },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 50, 40),
            ClipChain =
            [
                new LayoutClipContribution
                {
                    ClipperElementId = "grid",
                    Kind = "ancestor-layout-clip",
                    Precision = "exactRect",
                    AreaBefore = 4000,
                    AreaAfter = 2000,
                    LostAreaRatio = 0.5
                }
            ],
            IsInteractive = true,
            TreeOrder = 1
        });
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo { Id = "grid", Type = "Grid", IsVisible = true },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 50, 40),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 50, 40),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 50, 40),
            TreeOrder = 0
        });

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            Request(),
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        var finding = Assert.Single(result.Findings,
            finding => finding.RuleId == LayoutDiagnosticRules.ElementClipped);
        Assert.Equal("violation", finding.Outcome);
        Assert.Equal("serious", finding.Severity);
        Assert.Contains(finding.RelatedElements,
            related => related.Relation == "clipper" && related.Element.Id == "grid");
        Assert.Equal(0.5, finding.Evidence!.LostAreaRatio!.Value, 3);
    }

    [Fact]
    public void Analyze_DuplicateAutomationIds_ProduceDistinctFindingIds()
    {
        var capture = new LayoutCaptureSnapshot();
        foreach (var id in new[] { "first-runtime-id", "second-runtime-id" })
        {
            capture.Nodes.Add(new LayoutNodeSnapshot
            {
                Element = new ElementInfo
                {
                    Id = id,
                    Type = "Image",
                    AutomationId = "Duplicate",
                    IsVisible = true
                },
                LayoutRegion = LayoutRegionMath.Empty(),
                FullRegion = LayoutRegionMath.Empty(),
                VisibleRegion = LayoutRegionMath.Empty()
            });
        }

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            Request(minimumSeverity: "info"),
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        var findings = result.Findings
            .Where(finding =>
                finding.RuleId == LayoutDiagnosticRules.VisibleZeroArea)
            .ToList();
        Assert.Equal(2, findings.Count);
        Assert.Equal(2, findings.Select(finding => finding.Id).Distinct().Count());
        Assert.Single(findings.Select(finding => finding.SuppressionKey).Distinct());
    }

    [Fact]
    public void Analyze_SubpixelGeometryJitter_PreservesFindingId()
    {
        static LayoutInspectionResult Analyze(double x)
        {
            var capture = new LayoutCaptureSnapshot();
            capture.Nodes.Add(new LayoutNodeSnapshot
            {
                Element = new ElementInfo
                {
                    Id = "runtime-id",
                    Type = "Button",
                    IsVisible = true,
                    Traits = ["interactive"]
                },
                LayoutRegion = LayoutRegionMath.FromRect(x, 0, 100, 40),
                FullRegion = LayoutRegionMath.FromRect(x, 0, 100, 40),
                VisibleRegion = LayoutRegionMath.FromRect(x, 0, 50, 40),
                ClipChain =
                [
                    new LayoutClipContribution
                    {
                        ClipperElementId = "host",
                        Kind = "ancestor-layout-clip",
                        AreaBefore = 4000,
                        AreaAfter = 2000,
                        LostAreaRatio = 0.5
                    }
                ],
                IsInteractive = true
            });
            return LayoutDiagnosticsEngine.Analyze(
                capture,
                Request(),
                "test",
                stable: true,
                stabilityReason: null,
                Support());
        }

        var before = Assert.Single(
            Analyze(10).Findings,
            finding => finding.RuleId == LayoutDiagnosticRules.ElementClipped);
        var after = Assert.Single(
            Analyze(10.25).Findings,
            finding => finding.RuleId == LayoutDiagnosticRules.ElementClipped);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Id, after.Id);
    }

    [Fact]
    public void Analyze_RuntimeIdChanges_PreserveStructuralSuppressionKey()
    {
        static LayoutFinding Analyze(string runtimeId)
        {
            var capture = new LayoutCaptureSnapshot();
            capture.Nodes.Add(new LayoutNodeSnapshot
            {
                Element = new ElementInfo
                {
                    Id = runtimeId,
                    Type = "Image",
                    IsVisible = true
                },
                LayoutRegion = LayoutRegionMath.Empty(),
                FullRegion = LayoutRegionMath.Empty(),
                VisibleRegion = LayoutRegionMath.Empty()
            });
            var result = LayoutDiagnosticsEngine.Analyze(
                capture,
                Request(minimumSeverity: "info"),
                "test",
                stable: true,
                stabilityReason: null,
                Support());
            return Assert.Single(
                result.Findings,
                finding => finding.RuleId
                    == LayoutDiagnosticRules.VisibleZeroArea);
        }

        Assert.Equal(
            Analyze("runtime-one").SuppressionKey,
            Analyze("runtime-two").SuppressionKey);
    }

    [Fact]
    public void Analyze_EvidenceProjection_DoesNotChangeSuppressionKey()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "host",
                Type = "Grid",
                IsVisible = true
            },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            ContentRegion = LayoutRegionMath.FromRect(0, 0, 200, 40)
        });
        var withEvidenceRequest = Request(minimumSeverity: "info");
        withEvidenceRequest.IncludeEvidence = true;
        var withoutEvidenceRequest = Request(minimumSeverity: "info");
        withoutEvidenceRequest.IncludeEvidence = false;

        var withEvidence = Assert.Single(
            LayoutDiagnosticsEngine.Analyze(
                capture,
                withEvidenceRequest,
                "test",
                stable: true,
                stabilityReason: null,
                Support()).Findings,
            finding => finding.RuleId == LayoutDiagnosticRules.ContentOverflow);
        var withoutEvidence = Assert.Single(
            LayoutDiagnosticsEngine.Analyze(
                capture,
                withoutEvidenceRequest,
                "test",
                stable: true,
                stabilityReason: null,
                Support()).Findings,
            finding => finding.RuleId == LayoutDiagnosticRules.ContentOverflow);

        Assert.NotNull(withEvidence.Evidence);
        Assert.Null(withoutEvidence.Evidence);
        Assert.Equal(
            withEvidence.SuppressionKey,
            withoutEvidence.SuppressionKey);
    }

    [Fact]
    public void Analyze_UnknownTextMeasurement_IsIncompleteInsteadOfPass()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "label",
                Type = "Label",
                IsVisible = true
            },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 100, 20),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 20),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 100, 20),
            Text = new LayoutTextEvidence
            {
                MeasurementSource = "maui-label"
            }
        });
        var request = Request(minimumSeverity: "info");
        request.Rules = [LayoutDiagnosticRules.TextNotFullyRendered];

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            request,
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        var finding = Assert.Single(result.Findings);
        Assert.Equal("incomplete", finding.Outcome);
        Assert.Equal("measurement-unavailable", finding.Subtype);
        Assert.Equal(1, result.Summary.Incomplete);
        Assert.Equal(0, result.Summary.Passes);
    }

    [Fact]
    public void Analyze_HardClippedText_IsViolation()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "label",
                Type = "Label",
                IsVisible = true
            },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 100, 20),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 20),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 100, 20),
            Text = new LayoutTextEvidence
            {
                Kind = "horizontal-hard-clip",
                IsTruncated = true,
                MeasurementSource = "android-layout"
            }
        });
        var request = Request(minimumSeverity: "info");
        request.Rules = [LayoutDiagnosticRules.TextNotFullyRendered];

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            request,
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        var finding = Assert.Single(result.Findings);
        Assert.Equal("violation", finding.Outcome);
        Assert.Equal("serious", finding.Severity);
        Assert.Equal("fix", finding.Actionability);
    }

    [Fact]
    public void Analyze_HiddenTextNode_IsNotRenderedOrPassed()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "hidden-label",
                Type = "Label",
                IsVisible = true
            },
            IsRendered = false,
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 100, 20),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 20),
            VisibleRegion = LayoutRegionMath.Empty(),
            Text = new LayoutTextEvidence
            {
                IsTruncated = true,
                Kind = "horizontal-hard-clip"
            }
        });
        var request = Request(minimumSeverity: "info");
        request.Rules = [LayoutDiagnosticRules.TextNotFullyRendered];
        request.IncludePasses = true;

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            request,
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Summary.Passes);
        Assert.Equal(1, result.Summary.NotApplicable);
    }

    [Fact]
    public void Analyze_ScrollContentOverflow_IsObservation()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo { Id = "scroll", Type = "ScrollView", IsVisible = true },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 100, 100),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 100),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 100, 100),
            ContentRegion = LayoutRegionMath.FromRect(0, 0, 100, 300),
            IsScrollable = true
        });

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            Request(minimumSeverity: "info"),
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        var finding = Assert.Single(result.Findings,
            finding => finding.RuleId == LayoutDiagnosticRules.ContentOverflow);
        Assert.Equal("scrollable", finding.Subtype);
        Assert.Equal("observation", finding.Outcome);
        Assert.Equal("informational", finding.Actionability);
    }

    [Fact]
    public void Analyze_MatchingSuppression_PreservesFindingAsSuppressed()
    {
        var request = Request(minimumSeverity: "info");
        request.Suppressions.Add(new LayoutSuppression
        {
            RuleId = LayoutDiagnosticRules.VisibleZeroArea,
            AutomationId = "Decorative",
            Reason = "Expected hidden decoration"
        });
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "decorative",
                Type = "Image",
                AutomationId = "Decorative",
                IsVisible = true
            },
            LayoutRegion = LayoutRegionMath.Empty(),
            FullRegion = LayoutRegionMath.Empty(),
            VisibleRegion = LayoutRegionMath.Empty()
        });

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            request,
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        var finding = Assert.Single(result.Findings);
        Assert.True(finding.Suppressed);
        Assert.Equal("Expected hidden decoration", finding.SuppressionReason);
        Assert.Equal(1, result.Summary.Suppressed);
        Assert.Equal(0, result.Summary.Violations);
    }

    [Fact]
    public void Analyze_UnstableSnapshot_ReportsCoverageWithoutInjectingRuleFinding()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo { Id = "root", Type = "Grid", IsVisible = true },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 100, 100),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 100),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 100, 100)
        });

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            Request(),
            "test",
            stable: false,
            stabilityReason: "animation active",
            Support());

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Summary.Incomplete);
        Assert.False(result.Snapshot.Stable);
        Assert.Contains("animation active", result.Coverage.Limitations);
    }

    [Fact]
    public void Analyze_IncompleteCapture_ReportsIncompleteSummary()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.MarkIncomplete("Blazor layout capture timed out.");

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            Request(),
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        Assert.Equal(1, result.Summary.Incomplete);
        Assert.Contains(
            "Blazor layout capture timed out.",
            result.Coverage.Limitations);
    }

    [Fact]
    public void Analyze_UnavailableGeometry_IsReportedAsOpaqueCoverage()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "native-surface",
                Type = "HwndHost",
                IsVisible = true
            },
            GeometryAvailable = false,
            IsCoverageOpaque = true
        });

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            Request(minimumSeverity: "info"),
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        Assert.Empty(result.Findings);
        Assert.Equal(
            "native-surface",
            Assert.Single(result.Coverage.OpaqueSubtrees).Id);
        Assert.True(result.Summary.NotApplicable > 0);
        Assert.Equal(1, result.Summary.Incomplete);
        Assert.Equal("partial", result.Coverage.Overall);
    }

    [Fact]
    public void Analyze_UnsupportedRequestedRule_IsIncomplete()
    {
        var request = Request(minimumSeverity: "info");
        request.Rules = [LayoutDiagnosticRules.InteractionOccluded];
        var support = Support().Select(rule =>
        {
            if (rule.RuleId == LayoutDiagnosticRules.InteractionOccluded)
                rule.Support = "unsupported";
            return rule;
        }).ToList();

        var result = LayoutDiagnosticsEngine.Analyze(
            new LayoutCaptureSnapshot(),
            request,
            "test",
            stable: true,
            stabilityReason: null,
            support);

        Assert.Equal(1, result.Summary.NotApplicable);
        Assert.Equal(1, result.Summary.Incomplete);
    }

    [Fact]
    public void Analyze_MissingRequestedRoot_IsIncomplete()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "existing",
                Type = "Grid",
                IsVisible = true
            },
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 100),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 100, 100)
        });
        new VisualTreeWalker().ApplyLayoutScope(
            capture,
            new LayoutInspectionScope
            {
                RootElementId = "missing"
            });

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            Request(),
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        Assert.Equal(0, result.Snapshot.NodeCount);
        Assert.Equal(1, result.Summary.Incomplete);
        Assert.Contains(
            result.Coverage.Limitations,
            limitation => limitation.Contains(
                "was not found",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_SubPhysicalPixelClip_DoesNotReportFinding()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo { Id = "label", Type = "Label", IsVisible = true },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 100, 100),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 100),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 99.6, 100),
            WindowScale = 2,
            ClipChain =
            [
                new LayoutClipContribution
                {
                    ClipperElementId = "host",
                    Kind = "ancestor-layout-clip",
                    AreaBefore = 10000,
                    AreaAfter = 9960,
                    LostAreaRatio = 0.004
                }
            ]
        });

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            Request(minimumSeverity: "info"),
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        Assert.DoesNotContain(result.Findings,
            finding => finding.RuleId == LayoutDiagnosticRules.ElementClipped);
    }

    [Fact]
    public void Analyze_InteractiveElementOutsideScrollViewport_IsNotViolation()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "offscreen-button",
                Type = "Button",
                IsVisible = true,
                Traits = ["interactive"]
            },
            LayoutRegion = LayoutRegionMath.FromRect(0, 500, 100, 40),
            FullRegion = LayoutRegionMath.FromRect(0, 500, 100, 40),
            VisibleRegion = LayoutRegionMath.Empty(),
            IsInteractive = true,
            IsInsideScrollableViewport = true,
            AccessibilityVisible = true
        });

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            Request(minimumSeverity: "info"),
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        Assert.DoesNotContain(result.Findings, finding => finding.Outcome == "violation");
        Assert.Contains(result.Findings, finding =>
            finding.RuleId == LayoutDiagnosticRules.VisibleZeroArea
            && finding.Subtype == "outside-scroll-viewport");
        Assert.DoesNotContain(result.Findings,
            finding => finding.RuleId == LayoutDiagnosticRules.AccessibilityVisibilityMismatch);
    }

    [Fact]
    public void RequestContract_AgentAndDriverSerializeIdentically()
    {
        var agentRequest = new LayoutInspectionRequest
        {
            Profile = "strict",
            Rules = [LayoutDiagnosticRules.ElementClipped],
            Scope = new LayoutInspectionScope { RootElementId = "root", Window = 1 },
            Stability = new LayoutStabilityOptions { Mode = "immediate", TimeoutMs = 500 }
        };
        var driverRequest = new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
        {
            Profile = "strict",
            Rules = [Microsoft.Maui.DevFlow.Driver.LayoutDiagnosticRules.ElementClipped],
            Scope = new Microsoft.Maui.DevFlow.Driver.LayoutInspectionScope
            {
                RootElementId = "root",
                Window = 1
            },
            Stability = new Microsoft.Maui.DevFlow.Driver.LayoutStabilityOptions
            {
                Mode = "immediate",
                TimeoutMs = 500
            }
        };

        Assert.Equal(
            JsonSerializer.Serialize(agentRequest),
            JsonSerializer.Serialize(driverRequest));
    }

    [Fact]
    public void Analyze_ExpectedOverlayOccluder_IsReviewObservation()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "button",
                Type = "Button",
                IsVisible = true,
                Traits = ["interactive"]
            },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            IsInteractive = true,
            InteractionOccluderId = "overlay",
            InteractionBlockedLowerBound = 0.8,
            InteractionBlockedUpperBound = 1,
            InteractionSampleCount = 81
        });
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "overlay",
                Type = "Grid",
                AutomationId = "ModalOverlay",
                IsVisible = true
            },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 100, 40)
        });

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            Request(minimumSeverity: "info"),
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        var finding = Assert.Single(result.Findings,
            finding => finding.RuleId == LayoutDiagnosticRules.InteractionOccluded);
        Assert.Equal("observation", finding.Outcome);
        Assert.Equal("review", finding.Actionability);
    }

    [Fact]
    public void Analyze_OcclusionModeNone_SkipsInteractionFinding()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "button",
                Type = "Button",
                IsVisible = true,
                Traits = ["interactive"]
            },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            IsInteractive = true,
            IsHitTestVisible = true,
            InteractionOccluderId = "overlay",
            InteractionBlockedLowerBound = 1,
            InteractionBlockedUpperBound = 1
        });

        var request = Request(minimumSeverity: "info");
        request.Rules = [LayoutDiagnosticRules.InteractionOccluded];
        request.Occlusion.Mode = "none";
        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            request,
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        Assert.DoesNotContain(result.Findings,
            finding => finding.RuleId == LayoutDiagnosticRules.InteractionOccluded);
        Assert.Equal(1, result.Summary.NotApplicable);
    }

    [Fact]
    public void Analyze_IncludePasses_EmitsPassFindingAndCountsPass()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo { Id = "label", Type = "Label", IsVisible = true },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 100, 20),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 20),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 100, 20)
        });
        var request = Request(minimumSeverity: "critical");
        request.Rules = [LayoutDiagnosticRules.ElementClipped];
        request.IncludePasses = true;

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            request,
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        var finding = Assert.Single(result.Findings);
        Assert.Equal("pass", finding.Outcome);
        Assert.Equal(1, result.Summary.Passes);
        Assert.Equal(0, result.Summary.NotApplicable);
    }

    [Fact]
    public void Analyze_TextRuleWithoutTextEvidence_IsNotApplicable()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo { Id = "grid", Type = "Grid", IsVisible = true },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 100, 100),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 100),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 100, 100)
        });
        var request = Request(minimumSeverity: "info");
        request.Rules = [LayoutDiagnosticRules.TextNotFullyRendered];

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            request,
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Summary.Passes);
        Assert.Equal(1, result.Summary.NotApplicable);
    }

    [Theory]
    [InlineData("none", null, null)]
    [InlineData("length", 11, null)]
    [InlineData("raw", 11, "hello world")]
    public void ApplyTextPrivacy_RespectsRequestedMode(
        string mode,
        int? expectedLength,
        string? expectedText)
    {
        var evidence = new LayoutTextEvidence();

        VisualTreeWalker.ApplyTextPrivacy(evidence, "hello world", mode);

        Assert.Equal(expectedLength, evidence.TextLength);
        Assert.Equal(expectedText, evidence.Text);
    }

    [Fact]
    public void Analyze_InteractiveElementMostlyOutsideWindow_IsViolation()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "button",
                Type = "Button",
                IsVisible = true,
                Traits = ["interactive"]
            },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 40, 40),
            IsInteractive = true,
            ClipChain =
            [
                new LayoutClipContribution
                {
                    Kind = "window-edge",
                    AreaBefore = 4000,
                    AreaAfter = 1600,
                    LostAreaRatio = 0.6
                }
            ]
        });
        var request = Request(minimumSeverity: "info");
        request.Rules = [LayoutDiagnosticRules.ElementOutsideWindow];

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            request,
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        var finding = Assert.Single(result.Findings);
        Assert.Equal("violation", finding.Outcome);
        Assert.Equal("serious", finding.Severity);
    }

    [Fact]
    public void Analyze_SuppressionMatchesTypeRelatedElementAndSourceRange()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "button",
                ParentId = "host",
                Type = "Button",
                AutomationId = "Submit",
                IsVisible = true,
                Traits = ["interactive"],
                SourceFile = @"C:\repo\Views\Page.xaml",
                SourceLine = 15,
                SourceColumn = 9
            },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 40),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 50, 40),
            IsInteractive = true,
            ClipChain =
            [
                new LayoutClipContribution
                {
                    ClipperElementId = "host",
                    Kind = "ancestor-layout-clip",
                    AreaBefore = 4000,
                    AreaAfter = 2000,
                    LostAreaRatio = 0.5
                }
            ]
        });
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "host",
                Type = "Grid",
                AutomationId = "ClipHost",
                IsVisible = true
            },
            LayoutRegion = LayoutRegionMath.FromRect(0, 0, 50, 40),
            FullRegion = LayoutRegionMath.FromRect(0, 0, 50, 40),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 50, 40)
        });
        var request = Request(minimumSeverity: "info");
        request.Suppressions.Add(new LayoutSuppression
        {
            RuleId = LayoutDiagnosticRules.ElementClipped,
            ElementType = "Button",
            RelatedAutomationId = "ClipHost",
            SourceFile = "Views/Page.xaml",
            SourceLineStart = 10,
            SourceLineEnd = 20,
            Reason = "Known layout"
        });

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            request,
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        var finding = Assert.Single(result.Findings,
            finding => finding.RuleId == LayoutDiagnosticRules.ElementClipped);
        Assert.True(finding.Suppressed);
        Assert.Equal("Known layout", finding.SuppressionReason);
        Assert.Equal(@"C:\repo\Views\Page.xaml", finding.Element.SourceFile);
    }

    [Fact]
    public void SpatialIndex_FindsCrossSubtreeOverlapAndExcludesAncestors()
    {
        var parentA = Snapshot("parent-a", null, 0, 0, 100, 100, treeOrder: 0);
        var childA = Snapshot("child-a", "parent-a", 20, 20, 50, 50, treeOrder: 1);
        var parentB = Snapshot("parent-b", null, 200, 0, 100, 100, treeOrder: 2);
        var childB = Snapshot("child-b", "parent-b", 40, 40, 50, 50, treeOrder: 3);

        var overlaps = LayoutSpatialIndex.FindOverlaps(
            [parentA, childA, parentB, childB]);

        Assert.Contains(overlaps, candidate =>
            !candidate.SameParent
            && candidate.First.Element.Id == "child-a"
            && candidate.Second.Element.Id == "child-b");
        Assert.DoesNotContain(overlaps, candidate =>
            candidate.First.Element.Id == "parent-a"
            && candidate.Second.Element.Id == "child-a");
    }

    [Fact]
    public void Analyze_ExhaustiveProfileReportsCrossSubtreeOverlap()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(Snapshot("parent-a", null, 0, 0, 10, 10, treeOrder: 0));
        capture.Nodes.Add(Snapshot("child-a", "parent-a", 20, 20, 50, 50, treeOrder: 1));
        capture.Nodes.Add(Snapshot("parent-b", null, 200, 0, 10, 10, treeOrder: 2));
        capture.Nodes.Add(Snapshot("child-b", "parent-b", 40, 40, 50, 50, treeOrder: 3));
        var request = Request(minimumSeverity: "info");
        request.Profile = "exhaustive";
        request.Rules = [LayoutDiagnosticRules.GeometricOverlap];

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            request,
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        Assert.Contains(result.Findings, finding =>
            finding.RuleId == LayoutDiagnosticRules.GeometricOverlap
            && finding.Subtype == "cross-subtree-overlap");
    }

    [Fact]
    public void Analyze_FilteredOverlapDetection_IsNotCountedAsPass()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(Snapshot("first", null, 0, 0, 50, 50, treeOrder: 0));
        capture.Nodes.Add(Snapshot("second", null, 25, 25, 50, 50, treeOrder: 1));
        var request = Request(minimumSeverity: "minor");
        request.Profile = "exhaustive";
        request.Rules = [LayoutDiagnosticRules.GeometricOverlap];
        request.IncludePasses = true;

        var result = LayoutDiagnosticsEngine.Analyze(
            capture,
            request,
            "test",
            stable: true,
            stabilityReason: null,
            Support());

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Summary.Passes);
    }

    private static LayoutInspectionRequest Request(string minimumSeverity = "minor") => new()
    {
        Profile = "agent",
        MinimumSeverity = minimumSeverity,
        Stability = new LayoutStabilityOptions { Mode = "immediate" }
    };

    private static LayoutNodeSnapshot Snapshot(
        string id,
        string? parentId,
        double x,
        double y,
        double width,
        double height,
        int treeOrder)
    {
        var region = LayoutRegionMath.FromRect(x, y, width, height);
        return new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = id,
                ParentId = parentId,
                Type = "Grid",
                IsVisible = true
            },
            LayoutRegion = region,
            FullRegion = region,
            VisibleRegion = region,
            TreeOrder = treeOrder
        };
    }

    private static IReadOnlyList<LayoutRuleSupportInfo> Support()
        => LayoutDiagnosticRules.All.Select(rule => new LayoutRuleSupportInfo
        {
            RuleId = rule,
            Support = "partial",
            Confidence = "medium"
        }).ToList();
}
