using System.Text.Json;
using AIExtensions.Sample.Garden.Components;
using AIExtensions.Sample.Garden.Shared;
using Microsoft.Maui;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.AI.GenerativeUI.Composition;
using Microsoft.Maui.AI.GenerativeUI.Registry;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

public sealed class GardenComponentTests
{
    [Fact]
    public void ProductHero_RendersWateringCanWithAccessibleHeading()
    {
        var component = Bind(new ProductHero(), GardenProductFixtures.WateringCan);

        var name = Find<Label>(component, "ProductHeroName");
        Assert.Equal(GardenProductFixtures.WateringCan.Name, name.Text);
        Assert.Equal(SemanticHeadingLevel.Level1, SemanticProperties.GetHeadingLevel(name));
        Assert.Equal("Product hero", SemanticProperties.GetDescription(component));
    }

    [Fact]
    public void ProductCoreInfo_RendersPriceDescriptionAndStock()
    {
        var component = Bind(new ProductCoreInfo(), GardenProductFixtures.WateringCan);

        Assert.Equal("$18.50", Find<Label>(component, "ProductCoreInfoPrice").Text);
        Assert.Equal(
            GardenProductFixtures.WateringCan.Description,
            Find<Label>(component, "ProductCoreInfoDescription").Text);
        Assert.Equal("15 in stock", Find<Label>(component, "ProductCoreInfoStock").Text);

        component.ApplyVariant("compact");
        Assert.Equal(3, Find<Label>(component, "ProductCoreInfoDescription").MaxLines);
    }

    [Fact]
    public void DimensionsPanel_RendersAllMeasurements()
    {
        var component = Bind(new DimensionsPanel(), GardenProductFixtures.WateringCan);

        Assert.Equal("20.5", Find<Label>(component, "DimensionWidth").Text);
        Assert.Equal("14", Find<Label>(component, "DimensionHeight").Text);
        Assert.Equal("8.5", Find<Label>(component, "DimensionDepth").Text);
        Assert.Equal("inches", Find<Label>(component, "DimensionsUnit").Text);
        Assert.Equal("Product dimensions", SemanticProperties.GetDescription(component));
    }

    [Fact]
    public void ColorGallery_RendersNamedOptionsAndRicherVariantInPlace()
    {
        var component = Bind(new ColorGallery(), GardenProductFixtures.WateringCan);

        Assert.NotNull(Find<VerticalStackLayout>(component, "ColorOption-Galvanized-Steel"));
        Assert.NotNull(Find<VerticalStackLayout>(component, "ColorOption-Sage-Green"));
        Assert.NotNull(Find<VerticalStackLayout>(component, "ColorOption-Warm-Copper"));

        var content = component.Content;
        component.ApplyVariant("gallery");

        Assert.Same(content, component.Content);
        Assert.Equal("gallery", component.Variant);
        Assert.NotNull(Find<VerticalStackLayout>(component, "ColorOption-Sage-Green"));
    }

    [Fact]
    public void SameProductSnapshot_PreservesBindingNodesAndRefreshesColorGallery()
    {
        var state = CreateState(GardenProductFixtures.WateringCan);
        var productNode = state["product"];
        var dimensionsNode = productNode["dimensions"];
        var colorsNode = productNode["colorOptions"]["options"];
        var gallery = new ColorGallery { BindingContext = productNode };
        var updated = GardenProductFixtures.WateringCan with
        {
            Dimensions = new Dimensions(21m, 15m, 9m, "inches"),
            ColorOptions = new ColorOptions([new ProductColor("Ocean Blue", "#245A77")]),
        };

        UiObjectBuilder.Replace(
            productNode,
            JsonSerializer.SerializeToElement(updated, GardenJsonContext.Default.Product));

        Assert.Same(dimensionsNode, productNode["dimensions"]);
        Assert.Same(colorsNode, productNode["colorOptions"]["options"]);
        Assert.NotNull(Find<VerticalStackLayout>(gallery, "ColorOption-Ocean-Blue"));
        Assert.DoesNotContain(
            Descendants(gallery).OfType<VerticalStackLayout>(),
            element => element.AutomationId == "ColorOption-Sage-Green");
    }

    [Fact]
    public void ColorGallery_DetachUnsubscribesFromStateCollection()
    {
        var state = CreateState(GardenProductFixtures.WateringCan);
        var colors = state["product"]["colorOptions"]["options"].Children;
        var gallery = new ColorGallery { BindingContext = state["product"] };

        gallery.Detach();
        colors.Add(UiObjectBuilder.Build(JsonSerializer.SerializeToElement(
            new ProductColor("Ocean Blue", "#245A77"),
            GardenJsonContext.Default.ProductColor)));

        Assert.DoesNotContain(
            Descendants(gallery).OfType<VerticalStackLayout>(),
            element => element.AutomationId?.StartsWith("ColorOption-", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void SeedGrowingTimeline_RendersPlantingGerminationAndHarvest()
    {
        var component = Bind(new SeedGrowingTimeline(), GardenProductFixtures.BasilSeeds);

        Assert.Contains(
            "1/4 inch",
            Find<Label>(component, "SeedPlantingStepValue").Text,
            StringComparison.Ordinal);
        Assert.Equal("5–10 days", Find<Label>(component, "SeedGerminationStepValue").Text);
        Assert.Equal("60–75 days", Find<Label>(component, "SeedHarvestStepValue").Text);
        Assert.Equal("Seed planting and growing timeline", SemanticProperties.GetDescription(component));
    }

    [Fact]
    public void CatalogGrid_RendersProjectedProductsInsideScrollableSurface()
    {
        var element = JsonSerializer.SerializeToElement(
            new List<Product> { GardenProductFixtures.BasilSeeds, GardenProductFixtures.WateringCan },
            GardenJsonContext.Default.ListProduct);
        var component = new CatalogGrid(new StubGardenComponentActions())
        {
            BindingContext = UiObjectBuilder.Build(element),
        };

        Assert.Contains(
            Descendants(component).OfType<Label>(),
            label => label.Text == GardenProductFixtures.BasilSeeds.Name);
        Assert.Contains(
            Descendants(component).OfType<Label>(),
            label => label.Text == GardenProductFixtures.WateringCan.Name);
    }

    [Fact]
    public void Catalog_ResolvesOnlyComponentsWhoseFacetBindingsExist()
    {
        var registry = new GenerativeUiRegistry().AddGardenProductCatalog();
        var resolver = new ComponentCandidateResolver(registry);

        var wateringCan = resolver.Resolve(
            CreateState(GardenProductFixtures.WateringCan),
            nameof(Product),
            "product");
        var seed = resolver.Resolve(
            CreateState(GardenProductFixtures.BasilSeeds),
            nameof(Product),
            "product");

        Assert.Equal(
            ["ColorGallery", "DimensionsPanel", "ProductCoreInfo", "ProductHero"],
            wateringCan.Select(candidate => candidate.Descriptor.Alias));
        Assert.Equal(
            ["ProductCoreInfo", "ProductHero", "SeedGrowingTimeline"],
            seed.Select(candidate => candidate.Descriptor.Alias));
    }

    private static T Bind<T>(T component, Product product)
        where T : ProductComponentView
    {
        var element = JsonSerializer.SerializeToElement(product, GardenJsonContext.Default.Product);
        component.BindingContext = UiObjectBuilder.Build(element);
        return component;
    }

    private static UiObject CreateState(Product product)
    {
        var state = new UiObject();
        var element = JsonSerializer.SerializeToElement(product, GardenJsonContext.Default.Product);
        UiObjectBuilder.Populate(state["product"], element);
        return state;
    }

    private static T Find<T>(Element root, string automationId)
        where T : Element
        => Descendants(root)
            .OfType<T>()
            .Single(element => string.Equals(
                (element as VisualElement)?.AutomationId,
                automationId,
                StringComparison.Ordinal));

    private static IEnumerable<Element> Descendants(Element root)
    {
        yield return root;
        if (root is not IVisualTreeElement visual)
            yield break;

        foreach (var child in visual.GetVisualChildren().OfType<Element>())
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private sealed class StubGardenComponentActions : IGardenComponentActions
    {
        public Task NavigateAsync(string destination) => Task.CompletedTask;

        public Task OpenProductAsync(string sku) => Task.CompletedTask;

        public Task AddToCartAsync(string sku) => Task.CompletedTask;

        public Task SetCartQuantityAsync(string sku, int quantity) => Task.CompletedTask;

        public Task RemoveFromCartAsync(string sku) => Task.CompletedTask;

        public Task OpenOrderAsync(string orderId) => Task.CompletedTask;

        public Task ReorderAsync(string orderId) => Task.CompletedTask;
    }
}
