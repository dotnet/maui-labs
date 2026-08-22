using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace AIExtensions.Sample.Garden.Components;

public static class GardenAdaptiveLayouts
{
    public const string HomeSurface = "Home";
    public const string HomeBodyRegion = "HomeBody";
    public const string CatalogSurface = "Catalog";
    public const string CatalogBodyRegion = "CatalogBody";
    public const string ProductSurface = "Product";
    public const string ProductBodyRegion = "ProductBody";
    public const string CartSurface = "Cart";
    public const string CartBodyRegion = "CartBody";
    public const string OrdersSurface = "Orders";
    public const string OrdersBodyRegion = "OrdersBody";

    public static ComponentLayoutDocument HomeStandard { get; } = Create(
        HomeSurface,
        HomeBodyRegion,
        ("welcome", GardenComponentCatalog.WelcomeHeroAlias, "catalog", "default"),
        ("quick-actions", GardenComponentCatalog.QuickActionsAlias, "catalog", "default"));

    public static ComponentLayoutDocument CatalogStandard { get; } = Create(
        CatalogSurface,
        CatalogBodyRegion,
        ("category-shelves", GardenComponentCatalog.CategoryShelvesAlias, "catalog", "default"));

    public static ComponentLayoutDocument CatalogEmptyStandard { get; } = Create(
        CatalogSurface,
        CatalogBodyRegion,
        ("catalog-empty", GardenComponentCatalog.CatalogEmptyStateAlias, "catalog", "default"));

    public static ComponentLayoutDocument ProductStandard { get; } = Create(
        ProductSurface,
        ProductBodyRegion,
        ("product-hero", GardenComponentCatalog.ProductHeroAlias, "product", "default"),
        ("product-core", GardenComponentCatalog.ProductCoreInfoAlias, "product", "default"),
        ("review-summary", GardenComponentCatalog.ReviewSummaryAlias, "reviews", "default"));

    public static ComponentLayoutDocument CartStandard { get; } = Create(
        CartSurface,
        CartBodyRegion,
        ("cart-items", GardenComponentCatalog.CartItemsAlias, "cart", "default"),
        ("cart-total", GardenComponentCatalog.CartTotalsBreakdownAlias, "cart", "default"));

    public static ComponentLayoutDocument CartEmptyStandard { get; } = Create(
        CartSurface,
        CartBodyRegion,
        ("cart-empty", GardenComponentCatalog.CartEmptyStateAlias, "cart", "default"));

    public static ComponentLayoutDocument OrdersStandard { get; } = Create(
        OrdersSurface,
        OrdersBodyRegion,
        ("orders-list", GardenComponentCatalog.OrdersListAlias, "orders", "default"));

    public static ComponentLayoutDocument OrdersEmptyStandard { get; } = Create(
        OrdersSurface,
        OrdersBodyRegion,
        ("orders-empty", GardenComponentCatalog.OrdersEmptyStateAlias, "orders", "default"));

    public static AdaptiveSurfaceDescriptor Surface(
        string surface,
        string region,
        string description,
        params AdaptiveRequiredComponentGroup[] requiredGroups)
        => new()
        {
            Surface = surface,
            Description = description,
            Regions =
            [
                new()
                {
                    Name = region,
                    Description = $"Adaptive body for the {surface} page. Fixed navigation and safety actions are outside this region.",
                },
            ],
            RequiredComponentGroups = requiredGroups,
        };

    public static AdaptiveRequiredComponentGroup Require(
        string name,
        params string[] componentAliases)
        => new()
        {
            Name = name,
            ComponentAliases = componentAliases,
        };

    private static ComponentLayoutDocument Create(
        string surface,
        string region,
        params (string Id, string Component, string DataPath, string Variant)[] components)
    {
        var rootId = $"{surface.ToLowerInvariant()}-root";
        var nodes = new List<ComponentLayoutNode>
        {
            new()
            {
                Id = rootId,
                Kind = ComponentLayoutNodeKind.Stack,
                Order = 0,
                Orientation = AdaptiveStackOrientation.Vertical,
                Reason = $"Present the standard {surface} experience.",
            },
        };
        for (var index = 0; index < components.Length; index++)
        {
            var component = components[index];
            nodes.Add(new()
            {
                Id = component.Id,
                Kind = ComponentLayoutNodeKind.Component,
                ParentId = rootId,
                Order = index,
                Component = component.Component,
                DataPath = component.DataPath,
                Variant = component.Variant,
                Reason = $"Standard {surface} content.",
            });
        }

        return new()
        {
            LayoutId = $"{surface.ToLowerInvariant()}-standard",
            Revision = 1,
            Surface = surface,
            Explanation = $"Standard {surface} layout.",
            Regions =
            [
                new()
                {
                    Region = region,
                    RootNodeId = rootId,
                },
            ],
            Nodes = nodes,
        };
    }
}
