using System.Diagnostics;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class LayoutDiagnosticsTrustTests
{
    [Fact]
    public void Analyze_NestedClips_AttributesEveryClipper()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(Node("outer", null, 0, 0, 50, 100, 0));
        capture.Nodes.Add(Node("inner", "outer", 0, 0, 25, 100, 1));
        var target = Node("target", "inner", 0, 0, 100, 100, 2);
        target.VisibleRegion = LayoutRegionMath.FromRect(0, 0, 25, 100);
        target.ClipChain =
        [
            new LayoutClipContribution
            {
                ClipperElementId = "outer",
                Kind = "ancestor-layout-clip",
                AreaBefore = 10000,
                AreaAfter = 5000,
                LostAreaRatio = 0.5
            },
            new LayoutClipContribution
            {
                ClipperElementId = "inner",
                Kind = "ancestor-layout-clip",
                AreaBefore = 5000,
                AreaAfter = 2500,
                LostAreaRatio = 0.5
            }
        ];
        capture.Nodes.Add(target);

        var result = Analyze(capture);

        var finding = Assert.Single(result.Findings,
            finding => finding.RuleId == LayoutDiagnosticRules.ElementClipped);
        Assert.Equal(0.75, finding.Evidence!.LostAreaRatio!.Value, 3);
        Assert.Equal(2, finding.RelatedElements.Count);
    }

    [Fact]
    public void Analyze_IntentionalLayoutCorpus_HasNoActionableViolations()
    {
        var capture = new LayoutCaptureSnapshot();
        var scroll = Node("scroll", null, 0, 0, 100, 100, 0);
        scroll.IsScrollable = true;
        scroll.ContentRegion = LayoutRegionMath.FromRect(0, 0, 100, 300);
        capture.Nodes.Add(scroll);

        var button = Node("button", "host", 0, 0, 100, 40, 1);
        button.Element.Type = "Button";
        button.Element.Traits = ["interactive"];
        button.IsInteractive = true;
        button.InteractionOccluderId = "overlay";
        button.InteractionBlockedLowerBound = 0.8;
        button.InteractionBlockedUpperBound = 1;
        capture.Nodes.Add(button);
        var overlay = Node("overlay", "host", 0, 0, 100, 40, 2);
        overlay.Element.AutomationId = "ModalScrimOverlay";
        capture.Nodes.Add(overlay);

        var result = Analyze(capture, minimumSeverity: "info");

        Assert.DoesNotContain(result.Findings, finding =>
            !finding.Suppressed && finding.Outcome == "violation");
        Assert.Contains(result.Findings, finding =>
            finding.RuleId == LayoutDiagnosticRules.ContentOverflow
            && finding.Subtype == "scrollable");
        Assert.Contains(result.Findings, finding =>
            finding.RuleId == LayoutDiagnosticRules.InteractionOccluded
            && finding.Outcome == "observation");
    }

    [Fact]
    public void Analyze_RepeatedSnapshot_HasDeterministicOrderingAndFingerprints()
    {
        var capture = new LayoutCaptureSnapshot();
        for (var index = 0; index < 3; index++)
        {
            var node = Node($"node-{index}", null, index * 20, 0, 100, 40, index);
            node.Element.Type = "Button";
            node.Element.AutomationId = $"Button{index}";
            node.Element.Traits = ["interactive"];
            node.IsInteractive = true;
            node.VisibleRegion = LayoutRegionMath.FromRect(index * 20, 0, 50, 40);
            node.ClipChain =
            [
                new LayoutClipContribution
                {
                    ClipperElementId = "host",
                    Kind = "ancestor-layout-clip",
                    AreaBefore = 4000,
                    AreaAfter = 2000,
                    LostAreaRatio = 0.5
                }
            ];
            capture.Nodes.Add(node);
        }

        var first = Analyze(capture);
        var second = Analyze(capture);

        Assert.Equal(
            first.Findings.Select(finding => finding.Id),
            second.Findings.Select(finding => finding.Id));
        Assert.Equal(
            first.Findings.Select(finding => finding.Element.Id),
            second.Findings.Select(finding => finding.Element.Id));
    }

    [Fact]
    public void DriverContract_UnknownRuleAndOutcome_ArePreserved()
    {
        const string json = """
            {
              "schemaVersion":"1.0",
              "ruleSetVersion":"2.0",
              "snapshot":{"id":"s","capturedAt":"now","platform":"test","treeRevision":"r","stable":true,"nodeCount":0,"windows":[]},
              "coverage":{"overall":"partial","rules":[],"opaqueSubtrees":[],"limitations":[]},
              "summary":{"violations":0,"observations":0,"incomplete":0,"passes":0,"notApplicable":0,"suppressed":0},
              "findings":[{
                "id":"f",
                "ruleId":"layout.future-rule",
                "outcome":"future-outcome",
                "severity":"minor",
                "confidence":"low",
                "actionability":"review",
                "element":{"id":"e","type":"Future","interactive":false},
                "relatedElements":[],
                "message":"future",
                "fixCategories":[],
                "suppressed":false
              }]
            }
            """;

        var result = Microsoft.Maui.DevFlow.Driver.DriverJson.Deserialize<
            Microsoft.Maui.DevFlow.Driver.LayoutInspectionResult>(json);

        var finding = Assert.Single(result!.Findings);
        Assert.Equal("layout.future-rule", finding.RuleId);
        Assert.Equal("future-outcome", finding.Outcome);
    }

    [Theory]
    [InlineData(500, 250)]
    [InlineData(2000, 1000)]
    public void Analyze_LargeRealizedTree_MeetsBudget(int nodeCount, int budgetMs)
    {
        var capture = new LayoutCaptureSnapshot();
        for (var index = 0; index < nodeCount; index++)
        {
            capture.Nodes.Add(Node(
                $"node-{index}",
                null,
                (index % 50) * 20,
                (index / 50) * 20,
                10,
                10,
                index));
        }

        var stopwatch = Stopwatch.StartNew();
        _ = Analyze(capture);
        stopwatch.Stop();

        Assert.True(
            stopwatch.ElapsedMilliseconds < budgetMs,
            $"Expected {nodeCount} nodes under {budgetMs} ms, actual {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void CompactMcpPayload_StaysBelowAgentBudget()
    {
        var result = new Microsoft.Maui.DevFlow.Driver.LayoutInspectionResult();
        for (var index = 0; index < 500; index++)
        {
            result.Findings.Add(new Microsoft.Maui.DevFlow.Driver.LayoutFinding
            {
                Id = $"finding-{index}",
                RuleId = Microsoft.Maui.DevFlow.Driver.LayoutDiagnosticRules.ElementClipped,
                Outcome = "violation",
                Severity = "serious",
                Confidence = "high",
                Actionability = "fix",
                Element = new Microsoft.Maui.DevFlow.Driver.LayoutElementReference
                {
                    Id = $"element-{index}",
                    Type = "Button",
                    Interactive = true
                },
                Message = new string('x', 256)
            });
        }

        var json = LayoutDiagnosticsTool.SerializeResult(
            result,
            includeEvidence: false,
            profile: "agent",
            maxFindings: 100);

        Assert.True(
            System.Text.Encoding.UTF8.GetByteCount(json) < 512 * 1024,
            "Compact MCP payload exceeded 512 KB.");
    }

    private static LayoutInspectionResult Analyze(
        LayoutCaptureSnapshot capture,
        string minimumSeverity = "minor")
        => LayoutDiagnosticsEngine.Analyze(
            capture,
            new LayoutInspectionRequest
            {
                Profile = "agent",
                MinimumSeverity = minimumSeverity,
                Stability = new LayoutStabilityOptions { Mode = "immediate" }
            },
            "test",
            stable: true,
            stabilityReason: null,
            LayoutDiagnosticRules.All.Select(rule => new LayoutRuleSupportInfo
            {
                RuleId = rule,
                Support = rule == LayoutDiagnosticRules.AccessibilityVisibilityMismatch
                    ? "unsupported"
                    : "partial",
                Confidence = "medium"
            }).ToList());

    private static LayoutNodeSnapshot Node(
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
            TreeOrder = treeOrder,
            IsHitTestVisible = true
        };
    }
}
