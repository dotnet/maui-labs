using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class LayoutDiagnosticsDeltaTests
{
    [Fact]
    public void Build_GeometryOnlyJitter_DoesNotEmitDelta()
    {
        var previous = Result(Finding("same", "observation", x: 10));
        var current = Result(Finding("same", "observation", x: 10.25));

        Assert.Null(LayoutDiagnosticsDeltaBuilder.Build(previous, current));
    }

    [Fact]
    public void Build_AddUpdateAndRemove_ProducesFingerprintDelta()
    {
        var previous = Result(
            Finding("removed", "observation"),
            Finding("updated", "observation"));
        var current = Result(
            Finding("updated", "violation"),
            Finding("added", "observation"));

        var delta = Assert.IsType<LayoutDiagnosticsDelta>(
            LayoutDiagnosticsDeltaBuilder.Build(previous, current));

        Assert.Equal("added", Assert.Single(delta.Added).Id);
        Assert.Equal("updated", Assert.Single(delta.Updated).Id);
        Assert.Equal("removed", Assert.Single(delta.Removed));
    }

    [Fact]
    public void Build_DuplicateFingerprints_FallsBackToFullRefresh()
    {
        var previous = Result(
            Finding("duplicate", "observation"),
            Finding("duplicate", "observation"));
        var current = Result(
            Finding("duplicate", "observation"),
            Finding("duplicate", "violation"));

        Assert.Null(LayoutDiagnosticsDeltaBuilder.Build(previous, current));
    }

    private static LayoutInspectionResult Result(params LayoutFinding[] findings)
        => new()
        {
            Snapshot = new LayoutSnapshotInfo { Id = Guid.NewGuid().ToString("N") },
            Findings = findings.ToList(),
            Summary = new LayoutInspectionSummary
            {
                Violations = findings.Count(finding => finding.Outcome == "violation"),
                Observations = findings.Count(finding => finding.Outcome == "observation")
            }
        };

    private static LayoutFinding Finding(
        string id,
        string outcome,
        double x = 0)
        => new()
        {
            Id = id,
            RuleId = LayoutDiagnosticRules.ElementClipped,
            Outcome = outcome,
            Severity = outcome == "violation" ? "serious" : "minor",
            Confidence = "high",
            Actionability = outcome == "violation" ? "fix" : "review",
            Element = new LayoutElementReference
            {
                Id = "element-" + id,
                Type = "Button",
                Interactive = true
            },
            Message = "Finding " + id,
            Evidence = new LayoutFindingEvidence
            {
                FullRegion = new LayoutRegionInfo
                {
                    Bounds = new LayoutRectInfo
                    {
                        X = x,
                        Width = 100,
                        Height = 40
                    },
                    Area = 4000,
                    Precision = "exactRect"
                }
            }
        };
}
