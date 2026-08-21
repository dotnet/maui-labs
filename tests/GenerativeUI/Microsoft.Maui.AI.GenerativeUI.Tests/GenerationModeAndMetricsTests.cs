using GenerativeUI.Sample.Garden.Components;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

public sealed class GenerationModeAndMetricsTests
{
    [Fact]
    public void ComponentComposer_IsDefaultAndExposesOnlyReadAndComposeTools()
    {
        Assert.Equal(
            GardenGenerationMode.ComponentComposer,
            GardenGenerationModes.Options[0].Mode);
        Assert.True(GardenGenerationModes.IncludesTool(
            GardenGenerationMode.ComponentComposer,
            "read_api"));
        Assert.True(GardenGenerationModes.IncludesTool(
            GardenGenerationMode.ComponentComposer,
            "compose_product_detail"));
        Assert.False(GardenGenerationModes.IncludesTool(
            GardenGenerationMode.ComponentComposer,
            "write_api"));
        Assert.False(GardenGenerationModes.IncludesTool(
            GardenGenerationMode.ComponentComposer,
            "render_ui"));
        Assert.False(GardenGenerationModes.IncludesTool(
            GardenGenerationMode.ComponentComposer,
            "apply_patch"));
    }

    [Fact]
    public void BaselineFullGeneration_PreservesPrimitiveAndWriteToolsWithoutComposer()
    {
        Assert.True(GardenGenerationModes.IncludesTool(
            GardenGenerationMode.BaselineFullGeneration,
            "write_api"));
        Assert.True(GardenGenerationModes.IncludesTool(
            GardenGenerationMode.BaselineFullGeneration,
            "render_ui"));
        Assert.True(GardenGenerationModes.IncludesTool(
            GardenGenerationMode.BaselineFullGeneration,
            "apply_patch"));
        Assert.False(GardenGenerationModes.IncludesTool(
            GardenGenerationMode.BaselineFullGeneration,
            "compose_product_detail"));
    }

    [Fact]
    public void Metrics_ReportsProviderUsageAndVisualStability()
    {
        var collector = new GenerationMetricsCollector();
        collector.BeginTurn("Component Composer");
        collector.RecordComposition(
            new ComponentCompositionResult(
                new CompositionPlan
                {
                    PlanId = "plan",
                    Revision = 1,
                    Scaffold = "ProductDetail",
                    Title = "Watering Can",
                },
                CompositionPlanSource.Corrected,
                CorrectionCount: 1,
                new CompositionValidationResult([]),
                TimeSpan.FromMilliseconds(80),
                InputTokens: 120,
                OutputTokens: 30),
            new CompositionRenderDiff(
                ScaffoldReused: true,
                Added: [],
                Reused: ["hero", "core"],
                Moved: ["dimensions"],
                Reconfigured: [],
                Removed: []));
        collector.CompleteMain(
            TimeSpan.FromMilliseconds(450),
            inputTokens: 900,
            outputTokens: 100);

        var snapshot = collector.Snapshot;
        Assert.Equal(900, snapshot.MainInputTokens);
        Assert.Equal(120, snapshot.ComposerInputTokens);
        Assert.Equal(CompositionPlanSource.Corrected, snapshot.PlanSource);
        Assert.True(snapshot.PlanValid);
        Assert.True(snapshot.RenderDiff!.ScaffoldReused);
        Assert.Contains("corrections 1", collector.Summary, StringComparison.Ordinal);
        Assert.Contains("scaffold reused", collector.Summary, StringComparison.Ordinal);
        Assert.Contains("tokens 900/100", collector.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Metrics_DoesNotEstimateMissingTokenUsage()
    {
        var collector = new GenerationMetricsCollector();
        collector.BeginTurn("Baseline Full Generation");
        collector.CompleteMain(TimeSpan.FromMilliseconds(10), null, null);

        Assert.Contains("tokens n/a", collector.Summary, StringComparison.Ordinal);
    }
}
