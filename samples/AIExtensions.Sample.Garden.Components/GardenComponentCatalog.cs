using AIExtensions.Sample.Garden.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.GenerativeUI.Composition;
using Microsoft.Maui.AI.GenerativeUI.Registry;

namespace AIExtensions.Sample.Garden.Components;

public static class GardenComponentCatalog
{
    public const string ProductHeroAlias = "ProductHero";
    public const string ProductCoreInfoAlias = "ProductCoreInfo";
    public const string DimensionsPanelAlias = "DimensionsPanel";
    public const string ColorGalleryAlias = "ColorGallery";
    public const string SeedGrowingTimelineAlias = "SeedGrowingTimeline";
    public const string WelcomeHeroAlias = "WelcomeHero";
    public const string RecommendationBundleAlias = "RecommendationBundle";
    public const string QuickActionsAlias = "QuickActions";
    public const string CartSummaryAlias = "CartSummary";
    public const string RecentOrdersSummaryAlias = "RecentOrdersSummary";
    public const string SeasonalGardenTipAlias = "SeasonalGardenTip";
    public const string CatalogGridAlias = "CatalogGrid";
    public const string CatalogListAlias = "CatalogList";
    public const string CategoryShelvesAlias = "CategoryShelves";
    public const string RecommendationStripAlias = "RecommendationStrip";
    public const string ComparisonTrayAlias = "ComparisonTray";
    public const string CatalogEmptyStateAlias = "CatalogEmptyState";
    public const string ReviewSummaryAlias = "ReviewSummary";
    public const string ReviewListAlias = "ReviewList";
    public const string RelatedProductsAlias = "RelatedProducts";
    public const string StockAvailabilityAlias = "StockAvailability";
    public const string CartItemsAlias = "CartItems";
    public const string CompactCartItemsAlias = "CompactCartItems";
    public const string CartTotalsBreakdownAlias = "CartTotalsBreakdown";
    public const string BudgetSummaryAlias = "BudgetSummary";
    public const string SuggestedAddOnsAlias = "SuggestedAddOns";
    public const string CartEmptyStateAlias = "CartEmptyState";
    public const string OrdersListAlias = "OrdersList";
    public const string OrderTimelineAlias = "OrderTimeline";
    public const string OrderSummaryAlias = "OrderSummary";
    public const string OrderStatsAlias = "OrderStats";
    public const string OrderDetailAlias = "OrderDetail";
    public const string OrdersEmptyStateAlias = "OrdersEmptyState";

    public static IServiceCollection AddGardenProductComponents(this IServiceCollection services)
    {
        services.AddTransient<ProductHero>();
        services.AddTransient<ProductCoreInfo>();
        services.AddTransient<DimensionsPanel>();
        services.AddTransient<ColorGallery>();
        services.AddTransient<SeedGrowingTimeline>();
        services.AddTransient<WelcomeHero>();
        services.AddTransient<RecommendationBundle>();
        services.AddTransient<QuickActions>();
        services.AddTransient<CartSummary>();
        services.AddTransient<RecentOrdersSummary>();
        services.AddTransient<SeasonalGardenTip>();
        services.AddTransient<CatalogGrid>();
        services.AddTransient<CatalogList>();
        services.AddTransient<CategoryShelves>();
        services.AddTransient<RecommendationStrip>();
        services.AddTransient<ComparisonTray>();
        services.AddTransient<CatalogEmptyState>();
        services.AddTransient<ReviewSummary>();
        services.AddTransient<ReviewList>();
        services.AddTransient<RelatedProducts>();
        services.AddTransient<StockAvailability>();
        services.AddTransient<CartItems>();
        services.AddTransient<CompactCartItems>();
        services.AddTransient<CartTotalsBreakdown>();
        services.AddTransient<BudgetSummary>();
        services.AddTransient<SuggestedAddOns>();
        services.AddTransient<CartEmptyState>();
        services.AddTransient<OrdersList>();
        services.AddTransient<OrderTimeline>();
        services.AddTransient<OrderSummary>();
        services.AddTransient<OrderStats>();
        services.AddTransient<OrderDetail>();
        services.AddTransient<OrdersEmptyState>();
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
                AllowedRegions = ["ProductBody"],
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
                AllowedRegions = ["ProductBody"],
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
                AllowedRegions = ["ProductBody"],
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
                AllowedRegions = ["ProductBody"],
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
                AllowedRegions = ["ProductBody"],
                Variants = ["default"],
            });

        return registry;
    }

    public static GenerativeUiRegistry AddGardenAdaptiveCatalog(this GenerativeUiRegistry registry)
    {
        registry
            .AddGardenComponent<WelcomeHero>(
                WelcomeHeroAlias,
                "Welcoming orientation for the Garden home surface. Use for first visits or broad gardening goals.",
                GardenDataContracts.ProductList,
                ["HomeBody"])
            .AddGardenComponent<RecommendationBundle>(
                RecommendationBundleAlias,
                "Curated products plus a rationale. Emphasize for a concrete goal such as a balcony herb garden.",
                GardenDataContracts.Recommendation,
                ["HomeBody"])
            .AddGardenComponent<QuickActions>(
                QuickActionsAlias,
                "App-authored navigation shortcuts to catalog, cart, and orders.",
                GardenDataContracts.ProductList,
                ["HomeBody"])
            .AddGardenComponent<CartSummary>(
                CartSummaryAlias,
                "Compact current-cart total. Use on Home when an active cart is relevant.",
                GardenDataContracts.Cart,
                ["HomeBody"])
            .AddGardenComponent<RecentOrdersSummary>(
                RecentOrdersSummaryAlias,
                "Compact recent order history. Prefer after checkout or when the user asks about prior activity.",
                GardenDataContracts.OrderList,
                ["HomeBody"])
            .AddGardenComponent<SeasonalGardenTip>(
                SeasonalGardenTipAlias,
                "Short evergreen gardening guidance. Use only as supporting Home content.",
                GardenDataContracts.ProductList,
                ["HomeBody"])
            .AddGardenComponent<CatalogGrid>(
                CatalogGridAlias,
                "Visual two-column product browser with app-authored Details and Add actions.",
                GardenDataContracts.ProductList,
                ["CatalogBody"])
            .AddGardenComponent<CatalogList>(
                CatalogListAlias,
                "Dense product list for constrained space, budgets, or specific searches.",
                GardenDataContracts.ProductList,
                ["CatalogBody"])
            .AddGardenComponent<CategoryShelves>(
                CategoryShelvesAlias,
                "Relaxed browse-oriented product collection. Prefer for general catalog discovery.",
                GardenDataContracts.ProductList,
                ["CatalogBody"])
            .AddGardenComponent<RecommendationStrip>(
                RecommendationStripAlias,
                "Goal-specific recommended products with direct Details and Add actions.",
                GardenDataContracts.Recommendation,
                ["CatalogBody"])
            .AddGardenComponent<ComparisonTray>(
                ComparisonTrayAlias,
                "Compact set of products to compare for fit, price, or a durable user goal.",
                GardenDataContracts.ProductList,
                ["CatalogBody"])
            .AddGardenComponent<CatalogEmptyState>(
                CatalogEmptyStateAlias,
                "Catalog no-results message. Use only when the projected catalog list is empty.",
                GardenDataContracts.EmptyProductList,
                ["CatalogBody"])
            .AddGardenComponent<ReviewSummary>(
                ReviewSummaryAlias,
                "Compact star-rating snapshot. Promote when the user asks whether a product is well reviewed.",
                GardenDataContracts.ReviewList,
                ["ProductBody"])
            .AddGardenComponent<ReviewList>(
                ReviewListAlias,
                "Detailed customer review list. Use for explicit review questions.",
                GardenDataContracts.ReviewList,
                ["ProductBody"])
            .AddGardenComponent<RelatedProducts>(
                RelatedProductsAlias,
                "Related catalog products with app-authored Details and Add actions.",
                GardenDataContracts.ProductList,
                ["ProductBody"])
            .AddGardenComponent<StockAvailability>(
                StockAvailabilityAlias,
                "Current stock availability for the selected product.",
                GardenDataContracts.Product,
                ["ProductBody"])
            .AddGardenComponent<CartItems>(
                CartItemsAlias,
                "Full cart item cards with app-authored quantity and remove controls.",
                GardenDataContracts.Cart,
                ["CartBody"])
            .AddGardenComponent<CompactCartItems>(
                CompactCartItemsAlias,
                "Dense cart item summary for budget questions or small viewports.",
                GardenDataContracts.Cart,
                ["CartBody"])
            .AddGardenComponent<CartTotalsBreakdown>(
                CartTotalsBreakdownAlias,
                "Prominent cart total and checkout cost context.",
                GardenDataContracts.Cart,
                ["CartBody"])
            .AddGardenComponent<BudgetSummary>(
                BudgetSummaryAlias,
                "Budget-focused cart total. Promote when the user asks whether the cart fits a budget.",
                GardenDataContracts.Cart,
                ["CartBody"])
            .AddGardenComponent<SuggestedAddOns>(
                SuggestedAddOnsAlias,
                "Non-mutating product suggestions with explicit app-authored Add actions.",
                GardenDataContracts.ProductList,
                ["CartBody"])
            .AddGardenComponent<CartEmptyState>(
                CartEmptyStateAlias,
                "Empty-cart message. Use only when there are no cart items.",
                GardenDataContracts.EmptyCart,
                ["CartBody"])
            .AddGardenComponent<OrdersList>(
                OrdersListAlias,
                "Full order history with app-authored Open and Reorder actions.",
                GardenDataContracts.OrderList,
                ["OrdersBody"])
            .AddGardenComponent<OrderTimeline>(
                OrderTimelineAlias,
                "Chronological order presentation with app-authored actions.",
                GardenDataContracts.OrderList,
                ["OrdersBody"])
            .AddGardenComponent<OrderSummary>(
                OrderSummaryAlias,
                "Compact order matches for a product or gardening-goal query.",
                GardenDataContracts.OrderList,
                ["OrdersBody"])
            .AddGardenComponent<OrderStats>(
                OrderStatsAlias,
                "Spending-oriented order totals. Promote for summary or budget-history questions.",
                GardenDataContracts.OrderList,
                ["OrdersBody"])
            .AddGardenComponent<OrderDetail>(
                OrderDetailAlias,
                "Expanded order cards with app-authored Open and Reorder actions.",
                GardenDataContracts.OrderList,
                ["OrdersBody"])
            .AddGardenComponent<OrdersEmptyState>(
                OrdersEmptyStateAlias,
                "No-orders message. Use only when order history is empty.",
                GardenDataContracts.EmptyOrderList,
                ["OrdersBody"]);
        return registry;
    }

    private static GenerativeUiRegistry AddGardenComponent<T>(
        this GenerativeUiRegistry registry,
        string alias,
        string description,
        string contract,
        IReadOnlyList<string> regions)
        where T : notnull
        => registry.AddComponent<T>(new()
        {
            Alias = alias,
            Description = description,
            DataContract = contract,
            AllowedRegions = regions,
            Variants = ["default", "compact", "emphasis"],
        });
}
