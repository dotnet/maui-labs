using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class LayoutDiagnosticsMcpTests
{
    [Fact]
    public void SerializeResult_DefaultAgentMode_OmitsHeavyEvidence()
    {
        var result = CreateResult();

        var json = LayoutDiagnosticsTool.SerializeResult(
            result,
            includeEvidence: false,
            profile: "agent",
            maxFindings: 100);

        Assert.Contains("\"returnedFindings\":1", json);
        Assert.Contains("layout.element-clipped", json);
        Assert.Contains("\"rules\"", json);
        Assert.Contains("\"opaqueSubtrees\"", json);
        Assert.DoesNotContain("fullRegion", json);
        Assert.DoesNotContain("clipChain", json);
    }

    [Fact]
    public void SerializeResult_ExplicitEvidence_PreservesGeometry()
    {
        var json = LayoutDiagnosticsTool.SerializeResult(
            CreateResult(),
            includeEvidence: true,
            profile: "agent",
            maxFindings: 100);

        Assert.Contains("fullRegion", json);
        Assert.Contains("clipChain", json);
    }

    private static LayoutInspectionResult CreateResult() => new()
    {
        Snapshot = new LayoutSnapshotInfo { Id = "snapshot", Stable = true },
        Coverage = new LayoutCoverageInfo
        {
            Overall = "partial",
            Rules =
            [
                new LayoutRuleSupportInfo
                {
                    RuleId = LayoutDiagnosticRules.ElementClipped,
                    Support = "partial",
                    Confidence = "high"
                }
            ],
            OpaqueSubtrees =
            [
                new LayoutElementReference
                {
                    Id = "surface",
                    Type = "SurfaceView"
                }
            ]
        },
        Findings =
        [
            new LayoutFinding
            {
                Id = "finding",
                RuleId = LayoutDiagnosticRules.ElementClipped,
                Outcome = "violation",
                Severity = "serious",
                Confidence = "high",
                Actionability = "fix",
                Element = new LayoutElementReference
                {
                    Id = "button",
                    Type = "Button",
                    Interactive = true
                },
                Message = "Button is clipped.",
                Evidence = new LayoutFindingEvidence
                {
                    FullRegion = new LayoutRegionInfo
                    {
                        Bounds = new LayoutRectInfo { Width = 100, Height = 40 },
                        Area = 4000,
                        Precision = "exactRect"
                    },
                    ClipChain =
                    [
                        new LayoutClipContribution
                        {
                            Kind = "ancestor-layout-clip",
                            AreaBefore = 4000,
                            AreaAfter = 2000
                        }
                    ]
                }
            }
        ]
    };
}
