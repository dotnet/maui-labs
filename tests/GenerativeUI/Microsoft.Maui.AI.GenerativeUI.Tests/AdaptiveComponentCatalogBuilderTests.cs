using AIExtensions.Sample.Garden.Shared;
using Microsoft.Maui.AI.GenerativeUI.Composition;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using System.Text.Json;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

public sealed class AdaptiveComponentCatalogBuilderTests
{
    [Fact]
    public void Build_IncludesAvailableAndUnavailableRegisteredComponents()
    {
        var registry = CompositionTestCatalog.CreateRegistry();
        var state = CompositionTestCatalog.CreateState(GardenProductFixtures.WateringCan);

        var catalog = new AdaptiveComponentCatalogBuilder(registry).Build(
            state,
            [
                new()
                {
                    Path = "product",
                    Contract = nameof(Product),
                    Description = "Selected product.",
                },
            ],
            [AdaptiveCompositionTestCatalog.Region]);

        Assert.Equal(registry.Components.Count, catalog.Count);
        Assert.True(Assert.Single(catalog, item => item.Alias == "ProductHero").Available);
        var seeds = Assert.Single(catalog, item => item.Alias == "SeedGrowingTimeline");
        Assert.False(seeds.Available);
        Assert.False(string.IsNullOrWhiteSpace(seeds.UnavailableReason));
    }

    [Fact]
    public void Build_NullRequiredBinding_MarksComponentUnavailable()
    {
        var registry = CompositionTestCatalog.CreateRegistry();
        var state = new UiObject();
        UiObjectBuilder.Replace(
            state["product"],
            JsonDocument.Parse("""{"name":null}""").RootElement);

        var catalog = new AdaptiveComponentCatalogBuilder(registry).Build(
            state,
            [
                new()
                {
                    Path = "product",
                    Contract = nameof(Product),
                    Description = "Selected product.",
                },
            ],
            [AdaptiveCompositionTestCatalog.Region]);

        Assert.False(Assert.Single(catalog, item => item.Alias == "ProductHero").Available);
    }
}
