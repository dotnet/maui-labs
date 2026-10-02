using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait(TestFramework.Trait, TestFramework.Maui)]
public class LayoutBaselineTests(AppFixture app, ITestOutputHelper output) : IntegrationTestBase(app, output)
{
    [Fact]
    public async Task AnalyzeLayout_BaselineFixtures_ReportProblemsAndClearAfterCorrection()
    {
        var originalRoute = (await Client.GetStatusAsync())?.Route ?? "//native";
        try
        {
            await NavigateToPageAsync("//layoutdiagnostics", "BaselineFixtures");
            if ((await FindElementAsync("BaselineFixtureState")).Text != "Problem examples")
                Assert.True(await Client.TapAsync((await FindElementAsync("ToggleBaselineFixtures")).Id));

            var root = await FindElementAsync("BaselineFixtures");
            var toggle = await FindElementAsync("ToggleBaselineFixtures");
            var problems = await ScanAsync(root.Id);
            Assert.Equal("1.0", problems.SchemaVersion);
            Assert.Equal("1.1", problems.RuleSetVersion);
            Assert.True(problems.Snapshot.Stable, problems.Snapshot.StabilityReason);
            Assert.Contains(problems.Findings, finding => finding.RuleId == LayoutDiagnosticRules.ConstraintViolation
                && finding.Element.AutomationId == "ConflictingLimitsBox" && finding.Outcome == "violation");
            Assert.Contains(problems.Findings, finding => finding.RuleId == LayoutDiagnosticRules.DesiredSizeConstrained
                && finding.Element.AutomationId == "ConstrainedDesiredProbe" && finding.Outcome == "observation");
            var overflow = Assert.Single(problems.Findings, finding => finding.RuleId == LayoutDiagnosticRules.ChildOutsideParent
                && finding.Element.AutomationId == "BaselineOverflowChild");
            Assert.Equal("observation", overflow.Outcome);
            Assert.Contains(overflow.RelatedElements, related => related.Element.AutomationId == "BaselineOverflowParent");
            Assert.NotNull(overflow.Evidence?.ParentRegion);
            Assert.DoesNotContain(problems.Findings, finding => finding.Element.AutomationId is
                "BaselineValidMargin" or "BaselineScrollableContent" or "BaselineValidScroll");

            var defaultSeverity = await ScanAsync(root.Id, minimumSeverity: "minor");
            Assert.Equal(problems.Findings.Count(finding => finding.Severity == "info"), defaultSeverity.Summary.Filtered);
            Assert.DoesNotContain(defaultSeverity.Findings, finding => finding.RuleId is
                LayoutDiagnosticRules.DesiredSizeConstrained or LayoutDiagnosticRules.ChildOutsideParent);

            var allRules = await ScanAsync(root.Id, useDefaultRules: true);
            Assert.True(allRules.Snapshot.Stable, allRules.Snapshot.StabilityReason);
            Assert.DoesNotContain(allRules.Findings, finding => finding.Element.AutomationId == "BaselineValidMargin"
                && finding.RuleId is LayoutDiagnosticRules.ContentOverflow
                    or LayoutDiagnosticRules.ConstraintViolation
                    or LayoutDiagnosticRules.DesiredSizeConstrained
                    or LayoutDiagnosticRules.ChildOutsideParent);

            Assert.True(await Client.TapAsync(toggle.Id));
            var corrected = await ScanAsync(root.Id);

            Assert.True(corrected.Snapshot.Stable, corrected.Snapshot.StabilityReason);
            Assert.DoesNotContain(corrected.Findings, finding => finding.Element.AutomationId is
                "ConflictingLimitsBox" or "ConstrainedDesiredProbe" or "BaselineOverflowChild"
                or "BaselineValidMargin" or "BaselineScrollableContent" or "BaselineValidScroll");
            Assert.NotEqual(problems.Snapshot.DiagnosticsRevision, corrected.Snapshot.DiagnosticsRevision);

            var repeated = await ScanAsync(root.Id);
            Assert.Equal(corrected.Findings.Select(finding => finding.Id), repeated.Findings.Select(finding => finding.Id));
            Assert.Equal(corrected.Snapshot.DiagnosticsRevision, repeated.Snapshot.DiagnosticsRevision);
        }
        finally
        {
            if ((await Client.GetStatusAsync())?.Route == "//layoutdiagnostics"
                && (await TryFindElementAsync("BaselineFixtureState"))?.Text == "Valid layout")
            {
                await Client.TapAsync((await FindElementAsync("ToggleBaselineFixtures")).Id);
            }
            await Client.NavigateAsync(originalRoute);
            await Client.ControlMutationLeaseAsync("release");
        }
    }

    private async Task<LayoutInspectionResult> ScanAsync(
        string rootId, bool useDefaultRules = false, string minimumSeverity = "info")
    {
        var result = await Client.AnalyzeLayoutAsync(new LayoutInspectionRequest
        {
            Scope = new LayoutInspectionScope
            {
                RootElementId = rootId,
                IncludeNativeElements = false,
                IncludeBlazorElements = false
            },
            Rules = useDefaultRules ? null :
            [
                LayoutDiagnosticRules.ConstraintViolation,
                LayoutDiagnosticRules.DesiredSizeConstrained,
                LayoutDiagnosticRules.ChildOutsideParent
            ],
            MinimumSeverity = minimumSeverity,
            Occlusion = new LayoutOcclusionOptions { Mode = "none" },
            Stability = new LayoutStabilityOptions { TimeoutMs = 5000 }
        });
        Assert.NotNull(result);
        Assert.True(result.Snapshot.NodeCount > 0);
        if (!useDefaultRules)
            Assert.Equal(0, result.Summary.Incomplete);
        foreach (var finding in result.Findings)
            Output.WriteLine($"{finding.RuleId} / {finding.Element.AutomationId}: {finding.Outcome} - {finding.Message} Sizing: {System.Text.Json.JsonSerializer.Serialize(finding.Evidence?.Sizing)}");
        return result;
    }
}
