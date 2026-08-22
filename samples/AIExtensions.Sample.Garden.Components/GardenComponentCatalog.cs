using AIExtensions.Sample.Garden.Shared;
using GenerativeUI.Sample.Garden.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.GenerativeUI.Composition;
using Microsoft.Maui.AI.GenerativeUI.Registry;

namespace AIExtensions.Sample.Garden.Components;

public static class GardenComponentCatalog
{
    public const string ProductDetailScaffoldAlias = "ProductDetail";
    public const string ProductHeroAlias = "ProductHero";
    public const string ProductCoreInfoAlias = "ProductCoreInfo";
    public const string DimensionsPanelAlias = "DimensionsPanel";
    public const string ColorGalleryAlias = "ColorGallery";
    public const string SeedGrowingTimelineAlias = "SeedGrowingTimeline";

    public static IServiceCollection AddGardenProductComponents(this IServiceCollection services)
    {
        services.AddTransient<ProductHero>();
        services.AddTransient<ProductCoreInfo>();
        services.AddTransient<DimensionsPanel>();
        services.AddTransient<ColorGallery>();
        services.AddTransient<SeedGrowingTimeline>();
        services.AddTransient<ProductDetailScaffold>();
        services.AddSingleton<GardenCompositionTools>();
        services.AddSingleton<ICompositionFallbackPlanFactory, ProductDetailFallbackPlanFactory>();
        services.AddSingleton<GenerationMetricsCollector>();
        return services;
    }

    public static GenerativeUiRegistry AddGardenProductCatalog(this GenerativeUiRegistry registry)
    {
        registry
            .AddComponent<ProductHero>(new ComponentDescriptor
            {
                Alias = ProductHeroAlias,
                Description =
                    "Image-led product identity with name and fallback emoji. Use once at the top of a product detail. " +
                    "Do not use for a list row or when no product is active.",
                DataContract = nameof(Product),
                RequiredBindings = ["name"],
                OptionalBindings = ["imageUrl", "emoji"],
                AllowedSlots = [CompositionSlot.Hero],
                Variants = ["default", "compact"],
            })
            .AddComponent<ProductCoreInfo>(new ComponentDescriptor
            {
                Alias = ProductCoreInfoAlias,
                Description =
                    "Core product description, price, category, and stock. Use for every product detail. " +
                    "Keep it primary for generic browsing and supporting when a specific facet directly answers the user.",
                DataContract = nameof(Product),
                RequiredBindings = ["name", "description", "price"],
                OptionalBindings = ["category", "quantity"],
                AllowedSlots = [CompositionSlot.Primary, CompositionSlot.Supporting],
                Variants = ["default", "compact"],
            })
            .AddComponent<DimensionsPanel>(new ComponentDescriptor
            {
                Alias = DimensionsPanelAlias,
                Description =
                    "Physical width, height, and depth. Use only when dimensions exist. Promote to Primary when the user asks " +
                    "about size, fit, width, height, depth, or how big the product is.",
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
            .AddComponent<ColorGallery>(new ComponentDescriptor
            {
                Alias = ColorGalleryAlias,
                Description =
                    "Named product color choices shown as native swatches. Use only when color options exist. Use the richer " +
                    "'gallery' variant and promote to Primary when the user asks which colors are available.",
                DataContract = nameof(Product),
                RequiredBindings = ["colorOptions.options"],
                AllowedSlots = [CompositionSlot.Primary, CompositionSlot.Supporting],
                Variants = ["swatches", "gallery"],
            })
            .AddComponent<SeedGrowingTimeline>(new ComponentDescriptor
            {
                Alias = SeedGrowingTimelineAlias,
                Description =
                    "Planting, germination, and harvest sequence for seeds. Use only for a product with seed growing details. " +
                    "Do not use for tools, containers, soil, or products without seed details.",
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
            .AddScaffold<ProductDetailScaffold>(
                ProductDetailScaffoldAlias,
                "Native product detail scaffold with persistent Hero, Primary, Supporting, and Actions slots.",
                [
                    new(CompositionSlot.Hero, AllowsMultiple: false),
                    new(CompositionSlot.Primary, AllowsMultiple: false),
                    new(CompositionSlot.Supporting, AllowsMultiple: true),
                    new(CompositionSlot.Actions, AllowsMultiple: true),
                ]);

        return registry;
    }
}

public sealed class ProductDetailFallbackPlanFactory : ICompositionFallbackPlanFactory
{
    public string Scaffold => GardenComponentCatalog.ProductDetailScaffoldAlias;

    public CompositionPlan CreateFallback(CompositionFallbackContext context)
    {
        if (!string.Equals(context.Scaffold, Scaffold, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unsupported scaffold '{context.Scaffold}'.", nameof(context));

        var heroId = ExistingId(context.CurrentPlan, GardenComponentCatalog.ProductHeroAlias) ?? "product-hero";
        var coreId = ExistingId(context.CurrentPlan, GardenComponentCatalog.ProductCoreInfoAlias) ?? "product-core";

        return new CompositionPlan
        {
            PlanId = context.PlanId,
            Revision = context.Revision,
            Scaffold = Scaffold,
            Title = context.Title,
            Sections =
            [
                new()
                {
                    Id = heroId,
                    Slot = CompositionSlot.Hero,
                    Component = GardenComponentCatalog.ProductHeroAlias,
                    DataPath = context.DataPath,
                    Variant = "default",
                    Priority = 100,
                    Reason = "Deterministic product identity fallback.",
                },
                new()
                {
                    Id = coreId,
                    Slot = CompositionSlot.Primary,
                    Component = GardenComponentCatalog.ProductCoreInfoAlias,
                    DataPath = context.DataPath,
                    Variant = "default",
                    Priority = 90,
                    Reason = "Deterministic core product information fallback.",
                },
            ],
        };
    }

    private static string? ExistingId(CompositionPlan? plan, string component)
        => plan?.Sections.FirstOrDefault(section =>
            string.Equals(section.Component, component, StringComparison.OrdinalIgnoreCase))?.Id;
}
