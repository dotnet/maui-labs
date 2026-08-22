using AIExtensions.Sample.Garden.Shared;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

public sealed class ComponentCandidateResolverTests
{
    [Fact]
    public void Resolve_WateringCan_IncludesDimensionsAndColorsButNotSeedTimeline()
    {
        var registry = CompositionTestCatalog.CreateRegistry();
        var resolver = new ComponentCandidateResolver(registry);

        var candidates = resolver.Resolve(
            CompositionTestCatalog.CreateState(GardenProductFixtures.WateringCan),
            nameof(Product),
            CompositionTestCatalog.DataPath);

        Assert.Equal(
            ["ColorGallery", "DimensionsPanel", "ProductCoreInfo", "ProductHero"],
            candidates.Select(candidate => candidate.Descriptor.Alias));
    }

    [Fact]
    public void Resolve_Seed_IncludesTimelineButNotDimensionsOrColors()
    {
        var registry = CompositionTestCatalog.CreateRegistry();
        var resolver = new ComponentCandidateResolver(registry);

        var candidates = resolver.Resolve(
            CompositionTestCatalog.CreateState(GardenProductFixtures.BasilSeeds),
            nameof(Product),
            CompositionTestCatalog.DataPath);

        Assert.Equal(
            ["ProductCoreInfo", "ProductHero", "SeedGrowingTimeline"],
            candidates.Select(candidate => candidate.Descriptor.Alias));
    }

    [Fact]
    public void Resolve_MissingDataPath_ReturnsNoCandidates()
    {
        var registry = CompositionTestCatalog.CreateRegistry();
        var resolver = new ComponentCandidateResolver(registry);

        var candidates = resolver.Resolve(
            CompositionTestCatalog.CreateState(GardenProductFixtures.WateringCan),
            nameof(Product),
            "missing");

        Assert.Empty(candidates);
    }
}
