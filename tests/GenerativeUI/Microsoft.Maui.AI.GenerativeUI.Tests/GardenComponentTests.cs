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

    [Fact]
    public void FallbackPlan_IsDeterministicHeroAndCore()
    {
        var factory = new ProductDetailFallbackPlanFactory();
        var context = new CompositionFallbackContext(
            GardenComponentCatalog.ProductDetailScaffoldAlias,
            "product",
            "Watering Can",
            "watering-can-detail",
            2);

        var first = factory.CreateFallback(context);
        var second = factory.CreateFallback(context);

        Assert.Equal("watering-can-detail", first.PlanId);
        Assert.Equal(2, first.Revision);
        Assert.Equal(
            first.Sections.Select(section => (section.Id, section.Slot, section.Component, section.DataPath, section.Variant)),
            second.Sections.Select(section => (section.Id, section.Slot, section.Component, section.DataPath, section.Variant)));
        Assert.Collection(
            first.Sections,
            section =>
            {
                Assert.Equal("product-hero", section.Id);
                Assert.Equal(CompositionSlot.Hero, section.Slot);
                Assert.Equal(GardenComponentCatalog.ProductHeroAlias, section.Component);
            },
            section =>
            {
                Assert.Equal("product-core", section.Id);
                Assert.Equal(CompositionSlot.Primary, section.Slot);
                Assert.Equal(GardenComponentCatalog.ProductCoreInfoAlias, section.Component);
            });
    }

    [Fact]
    public void Scaffold_ReconcilesOnlyAffectedSlotsAndPreservesViews()
    {
        var scaffold = new ProductDetailScaffold();
        var hero = new ProductHero();
        var core = new ProductCoreInfo();
        var colors = new ColorGallery();

        scaffold.ApplySlots(new Dictionary<CompositionSlot, IReadOnlyList<View>>
        {
            [CompositionSlot.Hero] = [hero],
            [CompositionSlot.Primary] = [core],
            [CompositionSlot.Supporting] = [colors],
            [CompositionSlot.Actions] = [],
        });

        scaffold.ApplySlots(new Dictionary<CompositionSlot, IReadOnlyList<View>>
        {
            [CompositionSlot.Hero] = [hero],
            [CompositionSlot.Primary] = [colors],
            [CompositionSlot.Supporting] = [core],
            [CompositionSlot.Actions] = [],
        });

        Assert.Same(hero, Assert.Single(scaffold.GetSlotChildren(CompositionSlot.Hero)));
        Assert.Same(colors, Assert.Single(scaffold.GetSlotChildren(CompositionSlot.Primary)));
        Assert.Same(core, Assert.Single(scaffold.GetSlotChildren(CompositionSlot.Supporting)));
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
}
