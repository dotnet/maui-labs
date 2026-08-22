using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

internal static class AdaptiveCompositionTestCatalog
{
    public const string Surface = "Product";
    public const string Region = "Main";

    public static AdaptiveSurfaceContext Context(
        IReadOnlyList<AdaptiveComponentCatalogEntry>? catalog = null)
        => new()
        {
            SurfaceInstanceId = "product:watering-can",
            Surface = new AdaptiveSurfaceDescriptor
            {
                Surface = Surface,
                Description = "Product detail content.",
                Regions =
                [
                    new()
                    {
                        Name = Region,
                        Description = "Primary product content.",
                    },
                ],
            },
            DataManifest =
            [
                new()
                {
                    Path = "product",
                    Contract = "Product",
                    Description = "Selected product.",
                },
            ],
            ComponentCatalog = catalog ??
            [
                new()
                {
                    Alias = "ProductHero",
                    Description = "Product identity.",
                    DataContract = "Product",
                    RequiredBindings = ["name"],
                    OptionalBindings = [],
                    Variants = ["default", "compact"],
                    AllowedRegions = [Region],
                    CompatibleDataPaths = ["product"],
                    Available = true,
                },
            ],
            Viewport = new()
            {
                Width = 800,
                Height = 1200,
                Density = 2,
                Idiom = "Desktop",
                Orientation = "Portrait",
            },
            Intent = "Show the product.",
            StateSignature = "product:sku-watering-can:v1",
        };

    public static ComponentLayoutDocument StandardLayout(
        string layoutId = "product-layout",
        int revision = 1,
        string rootId = "root",
        string componentId = "hero")
        => new()
        {
            LayoutId = layoutId,
            Revision = revision,
            Surface = Surface,
            Regions =
            [
                new()
                {
                    Region = Region,
                    RootNodeId = rootId,
                },
            ],
            Nodes =
            [
                new()
                {
                    Id = rootId,
                    Kind = ComponentLayoutNodeKind.Stack,
                    Order = 0,
                    Orientation = AdaptiveStackOrientation.Vertical,
                    Reason = "Arrange the product content.",
                },
                new()
                {
                    Id = componentId,
                    Kind = ComponentLayoutNodeKind.Component,
                    ParentId = rootId,
                    Order = 0,
                    Component = "ProductHero",
                    DataPath = "product",
                    Variant = "default",
                    Reason = "Identify the product.",
                },
            ],
        };
}
