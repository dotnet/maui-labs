using System.Text.Json;
using GenerativeUI.Sample.Garden.Components;
using GenerativeUI.Sample.Garden.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.AI.GenerativeUI.Canvas;
using Microsoft.Maui.AI.GenerativeUI.Composition;
using Microsoft.Maui.AI.GenerativeUI.Registry;
using CanvasState = Microsoft.Maui.AI.GenerativeUI.Canvas.CanvasState;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

public sealed class ComponentComposerTests
{
    [Fact]
    public async Task Compose_InvalidThenValid_RetriesOnceWithStructuredCorrection()
    {
        var generator = new ScriptedPlanGenerator(
            request => InvalidPlan(request),
            request => GoldenPlan(request));
        using var harness = CreateHarness(generator, GardenProductFixtures.WateringCan);

        var result = await harness.Composer.ComposeAsync(
            Request("Show me the watering can."),
            harness.State);

        Assert.Equal(CompositionPlanSource.Corrected, result.Source);
        Assert.Equal(1, result.CorrectionCount);
        Assert.True(result.Validation.IsValid);
        Assert.Equal(2, generator.Requests.Count);
        Assert.Null(generator.Requests[0].CorrectionErrors);
        Assert.Contains("unknown_component", generator.Requests[1].CorrectionErrors, StringComparison.Ordinal);
        Assert.Equal(generator.Requests[0].ExpectedPlanId, generator.Requests[1].ExpectedPlanId);
        Assert.Equal(generator.Requests[0].ExpectedRevision, generator.Requests[1].ExpectedRevision);
    }

    [Fact]
    public async Task Compose_TwoInvalidPlans_UsesValidatedDeterministicFallback()
    {
        var generator = new ScriptedPlanGenerator(
            request => InvalidPlan(request),
            request => InvalidPlan(request));
        using var harness = CreateHarness(generator, GardenProductFixtures.WateringCan);

        var result = await harness.Composer.ComposeAsync(
            Request("Show me the watering can."),
            harness.State);

        Assert.Equal(CompositionPlanSource.Fallback, result.Source);
        Assert.Equal(1, result.CorrectionCount);
        Assert.True(result.Validation.IsValid);
        Assert.Equal(2, generator.Requests.Count);
        Assert.Equal(
            [GardenComponentCatalog.ProductHeroAlias, GardenComponentCatalog.ProductCoreInfoAlias],
            result.Plan.Sections.Select(section => section.Component));
    }

    [Fact]
    public async Task Compose_Cancellation_PropagatesWithoutFallback()
    {
        var generator = new CancelledPlanGenerator();
        using var harness = CreateHarness(generator, GardenProductFixtures.WateringCan);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.Composer.ComposeAsync(
                Request("Show me the watering can."),
                harness.State,
                cancellation.Token));
    }

    [Fact]
    public async Task GoldenWateringCanFollowUps_PreserveScaffoldAndOnlyMoveAffectedSections()
    {
        var generator = new GoldenPlanGenerator();
        using var harness = CreateHarness(generator, GardenProductFixtures.WateringCan);

        var initial = await harness.Composer.ComposeAsync(
            Request("Show me the watering can."),
            harness.State);
        var initialDiff = harness.Renderer.Render(initial.Plan, harness.State);
        var scaffold = harness.Canvas.CurrentView;
        var hero = harness.Session.GetSectionView("product-hero");
        var core = harness.Session.GetSectionView("product-core");
        var dimensions = harness.Session.GetSectionView("product-dimensions");
        var colors = harness.Session.GetSectionView("product-colors");

        Assert.False(initialDiff.ScaffoldReused);
        Assert.Equal(
            [
                GardenComponentCatalog.ProductHeroAlias,
                GardenComponentCatalog.ProductCoreInfoAlias,
                GardenComponentCatalog.DimensionsPanelAlias,
                GardenComponentCatalog.ColorGalleryAlias,
            ],
            initial.Plan.Sections.Select(section => section.Component));

        var size = await harness.Composer.ComposeAsync(
            Request("How big is the watering can?"),
            harness.State);
        var sizeDiff = harness.Renderer.Render(size.Plan, harness.State);

        Assert.Same(scaffold, harness.Canvas.CurrentView);
        Assert.True(sizeDiff.ScaffoldReused);
        Assert.Empty(sizeDiff.Added);
        Assert.Empty(sizeDiff.Removed);
        Assert.Equal(
            ["product-core", "product-dimensions"],
            sizeDiff.Moved.Order(StringComparer.Ordinal));
        Assert.Same(hero, harness.Session.GetSectionView("product-hero"));
        Assert.Same(core, harness.Session.GetSectionView("product-core"));
        Assert.Same(dimensions, harness.Session.GetSectionView("product-dimensions"));
        Assert.Same(colors, harness.Session.GetSectionView("product-colors"));
        Assert.Equal(
            "product-dimensions",
            Assert.Single(
                harness.Session.CurrentPlan!.Sections,
                section => section.Slot == CompositionSlot.Primary).Id);

        var color = await harness.Composer.ComposeAsync(
            Request("What colors?"),
            harness.State);
        var colorDiff = harness.Renderer.Render(color.Plan, harness.State);

        Assert.Same(scaffold, harness.Canvas.CurrentView);
        Assert.Equal(
            ["product-colors", "product-dimensions"],
            colorDiff.Moved.Order(StringComparer.Ordinal));
        Assert.Equal(["product-colors"], colorDiff.Reconfigured);
        Assert.Same(hero, harness.Session.GetSectionView("product-hero"));
        Assert.Same(core, harness.Session.GetSectionView("product-core"));
        Assert.Same(dimensions, harness.Session.GetSectionView("product-dimensions"));
        Assert.Same(colors, harness.Session.GetSectionView("product-colors"));
        var colorSection = Assert.Single(color.Plan.Sections, section =>
            section.Component == GardenComponentCatalog.ColorGalleryAlias);
        Assert.Equal(CompositionSlot.Primary, colorSection.Slot);
        Assert.Equal("gallery", colorSection.Variant);
    }

    [Fact]
    public async Task GoldenSeedPlan_UsesTimelineWithoutDimensionsOrColors()
    {
        var generator = new GoldenPlanGenerator();
        using var harness = CreateHarness(generator, GardenProductFixtures.BasilSeeds);

        var result = await harness.Composer.ComposeAsync(
            Request("Show me how to grow these basil seeds."),
            harness.State);
        harness.Renderer.Render(result.Plan, harness.State);

        Assert.Equal(
            [
                GardenComponentCatalog.ProductHeroAlias,
                GardenComponentCatalog.ProductCoreInfoAlias,
                GardenComponentCatalog.SeedGrowingTimelineAlias,
            ],
            result.Plan.Sections.Select(section => section.Component));
        Assert.DoesNotContain(
            result.Plan.Sections,
            section => section.Component is
                GardenComponentCatalog.DimensionsPanelAlias or GardenComponentCatalog.ColorGalleryAlias);
        Assert.NotNull(harness.Session.GetSectionView("seed-timeline"));
    }

    private static ComponentCompositionRequest Request(string intent)
        => new(
            intent,
            GardenComponentCatalog.ProductDetailScaffoldAlias,
            nameof(Product),
            "product",
            intent.Contains("basil", StringComparison.OrdinalIgnoreCase) ? "Basil Seeds" : "Watering Can");

    private static TestHarness CreateHarness(IComponentPlanGenerator generator, Product product)
    {
        var registry = new GenerativeUiRegistry().AddGardenProductCatalog();
        var services = new ServiceCollection()
            .AddGardenProductComponents()
            .BuildServiceProvider();
        var canvas = new CanvasState();
        var session = new CompositionSessionState();
        var state = new UiObject();
        UiObjectBuilder.Replace(
            state["product"],
            JsonSerializer.SerializeToElement(product, GardenJsonContext.Default.Product));
        var composer = new ComponentComposer(
            generator,
            new ComponentCandidateResolver(registry),
            new CompositionPlanValidator(registry),
            session,
            services.GetServices<ICompositionFallbackPlanFactory>());
        var renderer = new CompositionPlanRenderer(registry, services, canvas, session);
        return new(services, state, canvas, session, composer, renderer);
    }

    private static CompositionPlan InvalidPlan(CompositionPlanGenerationRequest request)
        => new()
        {
            PlanId = request.ExpectedPlanId,
            Revision = request.ExpectedRevision,
            Scaffold = request.Scaffold,
            Title = request.Title,
            Sections =
            [
                new()
                {
                    Id = "invented",
                    Slot = CompositionSlot.Primary,
                    Component = "InventedPanel",
                    DataPath = request.DataPath,
                    Priority = 100,
                    Reason = "Invalid model output.",
                },
            ],
        };

    private static CompositionPlan GoldenPlan(CompositionPlanGenerationRequest request)
        => GoldenPlanGenerator.CreatePlan(request);

    private sealed record TestHarness(
        ServiceProvider Services,
        UiObject State,
        CanvasState Canvas,
        CompositionSessionState Session,
        ComponentComposer Composer,
        CompositionPlanRenderer Renderer) : IDisposable
    {
        public void Dispose() => Services.Dispose();
    }

    private sealed class ScriptedPlanGenerator(
        params Func<CompositionPlanGenerationRequest, CompositionPlan?>[] responses)
        : IComponentPlanGenerator
    {
        private readonly Queue<Func<CompositionPlanGenerationRequest, CompositionPlan?>> _responses = new(responses);

        public List<CompositionPlanGenerationRequest> Requests { get; } = [];

        public Task<CompositionPlan?> GenerateAsync(
            CompositionPlanGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(_responses.Dequeue()(request));
        }
    }

    private sealed class CancelledPlanGenerator : IComponentPlanGenerator
    {
        public Task<CompositionPlan?> GenerateAsync(
            CompositionPlanGenerationRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromCanceled<CompositionPlan?>(cancellationToken);
    }

    private sealed class GoldenPlanGenerator : IComponentPlanGenerator
    {
        public Task<CompositionPlan?> GenerateAsync(
            CompositionPlanGenerationRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult<CompositionPlan?>(CreatePlan(request));

        public static CompositionPlan CreatePlan(CompositionPlanGenerationRequest request)
        {
            var available = request.Candidates
                .Select(candidate => candidate.Descriptor.Alias)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seed = available.Contains(GardenComponentCatalog.SeedGrowingTimelineAlias);
            var asksSize = request.Intent.Contains("big", StringComparison.OrdinalIgnoreCase) ||
                           request.Intent.Contains("dimension", StringComparison.OrdinalIgnoreCase);
            var asksColors = request.Intent.Contains("color", StringComparison.OrdinalIgnoreCase);

            var sections = new List<CompositionSection>
            {
                Section(request, "product-hero", CompositionSlot.Hero, GardenComponentCatalog.ProductHeroAlias, "default", 100),
            };

            if (seed)
            {
                sections.Add(Section(
                    request,
                    "product-core",
                    CompositionSlot.Primary,
                    GardenComponentCatalog.ProductCoreInfoAlias,
                    "default",
                    90));
                sections.Add(Section(
                    request,
                    "seed-timeline",
                    CompositionSlot.Supporting,
                    GardenComponentCatalog.SeedGrowingTimelineAlias,
                    "default",
                    80));
            }
            else if (asksSize)
            {
                sections.Add(Section(
                    request,
                    "product-dimensions",
                    CompositionSlot.Primary,
                    GardenComponentCatalog.DimensionsPanelAlias,
                    "default",
                    100));
                sections.Add(Section(
                    request,
                    "product-core",
                    CompositionSlot.Supporting,
                    GardenComponentCatalog.ProductCoreInfoAlias,
                    "compact",
                    70));
                sections.Add(Section(
                    request,
                    "product-colors",
                    CompositionSlot.Supporting,
                    GardenComponentCatalog.ColorGalleryAlias,
                    "swatches",
                    50));
            }
            else if (asksColors)
            {
                sections.Add(Section(
                    request,
                    "product-colors",
                    CompositionSlot.Primary,
                    GardenComponentCatalog.ColorGalleryAlias,
                    "gallery",
                    100));
                sections.Add(Section(
                    request,
                    "product-core",
                    CompositionSlot.Supporting,
                    GardenComponentCatalog.ProductCoreInfoAlias,
                    "compact",
                    70));
                sections.Add(Section(
                    request,
                    "product-dimensions",
                    CompositionSlot.Supporting,
                    GardenComponentCatalog.DimensionsPanelAlias,
                    "default",
                    50));
            }
            else
            {
                sections.Add(Section(
                    request,
                    "product-core",
                    CompositionSlot.Primary,
                    GardenComponentCatalog.ProductCoreInfoAlias,
                    "default",
                    90));
                sections.Add(Section(
                    request,
                    "product-dimensions",
                    CompositionSlot.Supporting,
                    GardenComponentCatalog.DimensionsPanelAlias,
                    "default",
                    50));
                sections.Add(Section(
                    request,
                    "product-colors",
                    CompositionSlot.Supporting,
                    GardenComponentCatalog.ColorGalleryAlias,
                    "swatches",
                    40));
            }

            return new CompositionPlan
            {
                PlanId = request.ExpectedPlanId,
                Revision = request.ExpectedRevision,
                Scaffold = request.Scaffold,
                Title = request.Title,
                Sections = sections,
            };
        }

        private static CompositionSection Section(
            CompositionPlanGenerationRequest request,
            string id,
            CompositionSlot slot,
            string component,
            string variant,
            int priority)
            => new()
            {
                Id = request.CurrentPlan?.Sections.FirstOrDefault(section =>
                    string.Equals(section.Component, component, StringComparison.OrdinalIgnoreCase))?.Id ?? id,
                Slot = slot,
                Component = component,
                DataPath = request.DataPath,
                Variant = variant,
                Priority = priority,
                Reason = $"Selected for intent: {request.Intent}",
            };
    }
}
