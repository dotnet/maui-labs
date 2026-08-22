using AIExtensions.Sample.Garden.Components;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace AIExtensions.Sample.Garden.Services;

public static class GardenAdaptiveLayouts
{
    public const string ProductSurface = "Product";
    public const string ProductBodyRegion = "ProductBody";

    public static ComponentLayoutDocument ProductStandard { get; } = new()
    {
        LayoutId = "product-standard",
        Revision = 1,
        Surface = ProductSurface,
        Explanation = "Standard product overview.",
        Regions =
        [
            new()
            {
                Region = ProductBodyRegion,
                RootNodeId = "product-root",
            },
        ],
        Nodes =
        [
            new()
            {
                Id = "product-root",
                Kind = ComponentLayoutNodeKind.Stack,
                Order = 0,
                Orientation = AdaptiveStackOrientation.Vertical,
                Reason = "Present the standard product overview.",
            },
            new()
            {
                Id = "product-hero",
                Kind = ComponentLayoutNodeKind.Component,
                ParentId = "product-root",
                Order = 0,
                Component = GardenComponentCatalog.ProductHeroAlias,
                DataPath = "product",
                Variant = "default",
                Reason = "Identify the selected product.",
            },
            new()
            {
                Id = "product-core",
                Kind = ComponentLayoutNodeKind.Component,
                ParentId = "product-root",
                Order = 1,
                Component = GardenComponentCatalog.ProductCoreInfoAlias,
                DataPath = "product",
                Variant = "default",
                Reason = "Show the essential product details.",
            },
        ],
    };
}
