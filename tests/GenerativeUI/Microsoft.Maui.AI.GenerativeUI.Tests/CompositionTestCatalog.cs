using System.Text.Json;
using AIExtensions.Sample.Garden.Shared;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.AI.GenerativeUI.Composition;
using Microsoft.Maui.AI.GenerativeUI.Registry;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

internal static class CompositionTestCatalog
{
    public const string Scaffold = "ProductDetail";
    public const string DataPath = "product";

    public static GenerativeUiRegistry CreateRegistry()
    {
        var registry = new GenerativeUiRegistry();
        registry
            .AddComponent<HeroComponent>(new ComponentDescriptor
            {
                Alias = "ProductHero",
                Description = "Product image and name.",
                DataContract = nameof(Product),
                RequiredBindings = ["name"],
                OptionalBindings = ["imageUrl", "emoji"],
                AllowedSlots = [CompositionSlot.Hero],
                Variants = ["default", "compact"],
            })
            .AddComponent<CoreComponent>(new ComponentDescriptor
            {
                Alias = "ProductCoreInfo",
                Description = "Core product description and price.",
                DataContract = nameof(Product),
                RequiredBindings = ["name", "description", "price"],
                AllowedSlots = [CompositionSlot.Primary, CompositionSlot.Supporting],
                Variants = ["default", "compact"],
            })
            .AddComponent<DimensionsComponent>(new ComponentDescriptor
            {
                Alias = "DimensionsPanel",
                Description = "Physical size details.",
                DataContract = nameof(Product),
                RequiredBindings =
                [
                    "dimensions.width",
                    "dimensions.height",
                    "dimensions.depth",
                    "dimensions.unit",
                ],
                AllowedSlots = [CompositionSlot.Primary, CompositionSlot.Supporting],
                Variants = ["default"],
            })
            .AddComponent<ColorsComponent>(new ComponentDescriptor
            {
                Alias = "ColorGallery",
                Description = "Available product colors.",
                DataContract = nameof(Product),
                RequiredBindings = ["colorOptions.options"],
                AllowedSlots = [CompositionSlot.Primary, CompositionSlot.Supporting],
                Variants = ["swatches", "gallery"],
            })
            .AddComponent<SeedComponent>(new ComponentDescriptor
            {
                Alias = "SeedGrowingTimeline",
                Description = "Planting, germination, and harvest timeline.",
                DataContract = nameof(Product),
                RequiredBindings =
                [
                    "seedDetails.plantingInstructions",
                    "seedDetails.germinationWindow",
                    "seedDetails.harvestWindow",
                ],
                AllowedSlots = [CompositionSlot.Primary, CompositionSlot.Supporting],
                Variants = ["default"],
            })
            .AddScaffold<TestScaffold>(
                Scaffold,
                "Product detail scaffold.",
                [
                    new(CompositionSlot.Hero, AllowsMultiple: false),
                    new(CompositionSlot.Primary, AllowsMultiple: false),
                    new(CompositionSlot.Supporting, AllowsMultiple: true),
                    new(CompositionSlot.Actions, AllowsMultiple: true),
                ]);

        return registry;
    }

    public static UiObject CreateState(Product product)
    {
        var state = new UiObject();
        var element = JsonSerializer.SerializeToElement(product, GardenJsonContext.Default.Product);
        UiObjectBuilder.Populate(state[DataPath], element);
        return state;
    }

    public static CompositionPlan ValidWateringCanPlan() => new()
    {
        PlanId = "watering-can-detail",
        Revision = 1,
        Scaffold = Scaffold,
        Title = "Watering Can",
        Sections =
        [
            new()
            {
                Id = "hero",
                Slot = CompositionSlot.Hero,
                Component = "ProductHero",
                DataPath = DataPath,
                Variant = "default",
                Priority = 100,
                Reason = "Identify the product.",
            },
            new()
            {
                Id = "core",
                Slot = CompositionSlot.Primary,
                Component = "ProductCoreInfo",
                DataPath = DataPath,
                Variant = "default",
                Priority = 90,
                Reason = "Show core buying information.",
            },
            new()
            {
                Id = "dimensions",
                Slot = CompositionSlot.Supporting,
                Component = "DimensionsPanel",
                DataPath = DataPath,
                Variant = "default",
                Priority = 50,
                Reason = "The product has physical dimensions.",
            },
            new()
            {
                Id = "colors",
                Slot = CompositionSlot.Supporting,
                Component = "ColorGallery",
                DataPath = DataPath,
                Variant = "swatches",
                Priority = 40,
                Reason = "The product has color choices.",
            },
        ],
    };

    internal sealed class HeroComponent;
    internal sealed class CoreComponent;
    internal sealed class DimensionsComponent;
    internal sealed class ColorsComponent;
    internal sealed class SeedComponent;
    internal sealed class TestScaffold;
}
