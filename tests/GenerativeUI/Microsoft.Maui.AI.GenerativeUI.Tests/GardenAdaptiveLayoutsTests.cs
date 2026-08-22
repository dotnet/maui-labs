using System.Text.Json;
using AIExtensions.Sample.Garden.Components;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.AI.GenerativeUI.Composition;
using Microsoft.Maui.AI.GenerativeUI.Registry;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

public sealed class GardenAdaptiveLayoutsTests
{
    [Fact]
    public void CheckedInStandardLayouts_ValidateAgainstCompleteCatalogAndDataManifest()
    {
        foreach (var scenario in StandardScenarios())
        {
            var result = Validate(scenario.Layout, scenario.Surface, scenario.Manifest);

            Assert.True(
                result.IsValid,
                $"{scenario.Surface.Surface}: {ComponentLayoutValidationErrorFormatter.Format(result)}");
        }
    }

    [Fact]
    public void AcceptanceIntentLayouts_UseWholeComponentsAndPreserveRequiredCapabilities()
    {
        foreach (var scenario in IntentScenarios())
        {
            var layout = CreateLayout(
                scenario.Surface.Surface,
                scenario.Surface.Regions[0].Name,
                scenario.Components);
            var result = Validate(layout, scenario.Surface, scenario.Manifest);

            Assert.True(
                result.IsValid,
                $"{scenario.Intent}: {ComponentLayoutValidationErrorFormatter.Format(result)}");
            Assert.All(
                layout.Nodes.Where(node => node.Kind == ComponentLayoutNodeKind.Component),
                node => Assert.Contains(
                    Catalog().Components,
                    registration => string.Equals(
                        registration.Descriptor.Alias,
                        node.Component,
                        StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void EmptyStateComponents_AreUnavailableWhenCanonicalCollectionsArePopulated()
    {
        var state = CompleteState();
        var registry = Catalog();
        var catalog = new AdaptiveComponentCatalogBuilder(registry).Build(
            state,
            [
                Data("catalog", GardenDataContracts.ProductList),
                Data("catalog", GardenDataContracts.EmptyProductList) with
                {
                    Available = false,
                    UnavailableReason = "Products are available.",
                },
                Data("cart", GardenDataContracts.Cart),
                Data("cart", GardenDataContracts.EmptyCart) with
                {
                    Available = false,
                    UnavailableReason = "The cart has items.",
                },
                Data("orders", GardenDataContracts.OrderList),
                Data("orders", GardenDataContracts.EmptyOrderList) with
                {
                    Available = false,
                    UnavailableReason = "Orders are available.",
                },
            ],
            [
                GardenAdaptiveLayouts.CatalogBodyRegion,
                GardenAdaptiveLayouts.CartBodyRegion,
                GardenAdaptiveLayouts.OrdersBodyRegion,
            ]);

        Assert.False(Entry(GardenComponentCatalog.CatalogEmptyStateAlias).Available);
        Assert.False(Entry(GardenComponentCatalog.CartEmptyStateAlias).Available);
        Assert.False(Entry(GardenComponentCatalog.OrdersEmptyStateAlias).Available);
        Assert.True(Entry(GardenComponentCatalog.CatalogGridAlias).Available);
        Assert.True(Entry(GardenComponentCatalog.CartItemsAlias).Available);
        Assert.True(Entry(GardenComponentCatalog.OrdersListAlias).Available);

        AdaptiveComponentCatalogEntry Entry(string alias)
            => Assert.Single(catalog, entry => entry.Alias == alias);
    }

    private static IEnumerable<LayoutScenario> StandardScenarios()
    {
        yield return new(
            GardenAdaptiveLayouts.HomeSurface,
            GardenAdaptiveLayouts.HomeStandard,
            Surface(GardenAdaptiveLayouts.HomeSurface, GardenAdaptiveLayouts.HomeBodyRegion),
            HomeManifest());
        yield return new(
            GardenAdaptiveLayouts.CatalogSurface,
            GardenAdaptiveLayouts.CatalogStandard,
            CatalogSurface(),
            CatalogManifest());
        yield return new(
            GardenAdaptiveLayouts.CatalogSurface,
            GardenAdaptiveLayouts.CatalogEmptyStandard,
            CatalogSurface(),
            EmptyCatalogManifest());
        yield return new(
            GardenAdaptiveLayouts.ProductSurface,
            GardenAdaptiveLayouts.ProductStandard,
            ProductSurface(),
            ProductManifest());
        yield return new(
            GardenAdaptiveLayouts.CartSurface,
            GardenAdaptiveLayouts.CartStandard,
            CartSurface(),
            CartManifest());
        yield return new(
            GardenAdaptiveLayouts.CartSurface,
            GardenAdaptiveLayouts.CartEmptyStandard,
            CartSurface(),
            EmptyCartManifest());
        yield return new(
            GardenAdaptiveLayouts.OrdersSurface,
            GardenAdaptiveLayouts.OrdersStandard,
            OrdersSurface(),
            OrdersManifest());
        yield return new(
            GardenAdaptiveLayouts.OrdersSurface,
            GardenAdaptiveLayouts.OrdersEmptyStandard,
            OrdersSurface(),
            EmptyOrdersManifest());
    }

    private static IEnumerable<IntentScenario> IntentScenarios()
    {
        yield return Intent(
            "Catalog grid",
            CatalogSurface(),
            CatalogManifest(),
            (GardenComponentCatalog.CatalogGridAlias, "catalog", "default"));
        yield return Intent(
            "Catalog list",
            CatalogSurface(),
            CatalogManifest(),
            (GardenComponentCatalog.CatalogListAlias, "catalog", "compact"));
        yield return Intent(
            "Catalog categories",
            CatalogSurface(),
            CatalogManifest(),
            (GardenComponentCatalog.CategoryShelvesAlias, "catalog", "default"));
        yield return Intent(
            "Catalog recommendations",
            CatalogSurface(),
            CatalogManifest(),
            (GardenComponentCatalog.RecommendationStripAlias, "recommendation", "emphasis"));
        yield return Intent(
            "Product size",
            ProductSurface(),
            ProductManifest(),
            (GardenComponentCatalog.ProductCoreInfoAlias, "product", "compact"),
            (GardenComponentCatalog.DimensionsPanelAlias, "product", "default"));
        yield return Intent(
            "Product colors",
            ProductSurface(),
            ProductManifest(),
            (GardenComponentCatalog.ProductCoreInfoAlias, "product", "compact"),
            (GardenComponentCatalog.ColorGalleryAlias, "product", "gallery"));
        yield return Intent(
            "Product growing guidance",
            ProductSurface(),
            ProductManifest(),
            (GardenComponentCatalog.ProductCoreInfoAlias, "product", "compact"),
            (GardenComponentCatalog.SeedGrowingTimelineAlias, "product", "default"));
        yield return Intent(
            "Product reviews",
            ProductSurface(),
            ProductManifest(),
            (GardenComponentCatalog.ProductCoreInfoAlias, "product", "compact"),
            (GardenComponentCatalog.ReviewSummaryAlias, "reviews", "emphasis"),
            (GardenComponentCatalog.ReviewListAlias, "reviews", "default"));
        yield return Intent(
            "Compact budget cart",
            CartSurface(),
            CartManifest(),
            (GardenComponentCatalog.CompactCartItemsAlias, "cart", "compact"),
            (GardenComponentCatalog.BudgetSummaryAlias, "cart", "emphasis"));
        yield return Intent(
            "Matching order summary",
            OrdersSurface(),
            OrdersManifest(),
            (GardenComponentCatalog.OrderSummaryAlias, "orders", "emphasis"),
            (GardenComponentCatalog.OrderStatsAlias, "orders", "default"));
        yield return Intent(
            "Balcony herb home",
            Surface(GardenAdaptiveLayouts.HomeSurface, GardenAdaptiveLayouts.HomeBodyRegion),
            HomeManifest(),
            (GardenComponentCatalog.RecommendationBundleAlias, "recommendation", "emphasis"),
            (GardenComponentCatalog.QuickActionsAlias, "catalog", "default"));
    }

    private static IntentScenario Intent(
        string intent,
        AdaptiveSurfaceDescriptor surface,
        IReadOnlyList<AdaptiveDataDescriptor> manifest,
        params (string Alias, string DataPath, string Variant)[] components)
        => new(intent, surface, manifest, components);

    private static ComponentLayoutValidationResult Validate(
        ComponentLayoutDocument layout,
        AdaptiveSurfaceDescriptor surface,
        IReadOnlyList<AdaptiveDataDescriptor> manifest)
    {
        var state = CompleteState();
        var catalog = new AdaptiveComponentCatalogBuilder(Catalog()).Build(
            state,
            manifest,
            surface.Regions.Select(region => region.Name).ToArray());
        var context = new AdaptiveSurfaceContext
        {
            SurfaceInstanceId = $"{surface.Surface.ToLowerInvariant()}:test",
            Surface = surface,
            DataManifest = manifest,
            ComponentCatalog = catalog,
            Viewport = new()
            {
                Width = 1024,
                Height = 768,
                Density = 2,
                Idiom = "Desktop",
                Orientation = "Landscape",
            },
            Intent = "Test intent.",
            StateSignature = "test:v1",
        };

        return new ComponentLayoutValidator().Validate(layout, context);
    }

    private static GenerativeUiRegistry Catalog()
        => new GenerativeUiRegistry()
            .AddGardenProductCatalog()
            .AddGardenAdaptiveCatalog();

    private static UiObject CompleteState()
    {
        var state = new UiObject();
        using var json = JsonDocument.Parse(
            """
            {
              "catalog": [{
                "sku": "seed-basil",
                "name": "Sweet Basil Seeds",
                "description": "Fragrant balcony herb.",
                "price": 3.49,
                "category": "seeds",
                "emoji": "plant",
                "quantity": 12
              }],
              "product": {
                "sku": "seed-basil",
                "name": "Sweet Basil Seeds",
                "description": "Fragrant balcony herb.",
                "price": 3.49,
                "category": "seeds",
                "emoji": "plant",
                "quantity": 12,
                "seedDetails": {
                  "plantingInstructions": "Sow indoors.",
                  "germinationWindow": "5-10 days",
                  "harvestWindow": "60-75 days"
                },
                "dimensions": {
                  "width": 2,
                  "height": 4,
                  "depth": 1,
                  "unit": "inches"
                },
                "colorOptions": {
                  "options": [{ "name": "Sage", "hex": "#7E9278" }]
                }
              },
              "cart": {
                "items": [{
                  "sku": "seed-basil",
                  "name": "Sweet Basil Seeds",
                  "unitPrice": 3.49,
                  "quantity": 1,
                  "subtotal": 3.49,
                  "emoji": "plant"
                }],
                "total": 3.49
              },
              "orders": [{
                "id": "order-1",
                "items": [{
                  "sku": "seed-basil",
                  "name": "Sweet Basil Seeds",
                  "unitPrice": 3.49,
                  "quantity": 1,
                  "subtotal": 3.49,
                  "emoji": "plant"
                }],
                "total": 3.49,
                "placedAt": "2026-04-01T00:00:00Z"
              }],
              "reviews": [{
                "id": "review-1",
                "sku": "seed-basil",
                "rating": 5,
                "comment": "Thriving.",
                "createdAt": "2026-04-01T00:00:00Z"
              }],
              "related": [{
                "sku": "soil",
                "name": "Potting Soil",
                "description": "Container mix.",
                "price": 12,
                "category": "soil",
                "emoji": "soil"
              }],
              "suggestions": [{
                "sku": "pot",
                "name": "Terracotta Pot",
                "description": "Balcony container.",
                "price": 10,
                "category": "tools",
                "emoji": "pot"
              }],
              "recommendation": {
                "title": "Balcony herb garden",
                "reason": "Compact and beginner friendly.",
                "products": [{
                  "sku": "seed-basil",
                  "name": "Sweet Basil Seeds",
                  "description": "Fragrant balcony herb.",
                  "price": 3.49,
                  "category": "seeds",
                  "emoji": "plant"
                }]
              }
            }
            """);
        foreach (var property in json.RootElement.EnumerateObject())
            UiObjectBuilder.Replace(state[property.Name], property.Value);
        return state;
    }

    private static AdaptiveSurfaceDescriptor Surface(string surface, string region)
        => GardenAdaptiveLayouts.Surface(surface, region, $"{surface} test surface.");

    private static AdaptiveSurfaceDescriptor CatalogSurface()
        => GardenAdaptiveLayouts.Surface(
            GardenAdaptiveLayouts.CatalogSurface,
            GardenAdaptiveLayouts.CatalogBodyRegion,
            "Catalog test surface.",
            GardenAdaptiveLayouts.Require(
                "catalog actions",
                GardenComponentCatalog.CatalogGridAlias,
                GardenComponentCatalog.CatalogListAlias,
                GardenComponentCatalog.CategoryShelvesAlias,
                GardenComponentCatalog.RecommendationStripAlias,
                GardenComponentCatalog.ComparisonTrayAlias,
                GardenComponentCatalog.CatalogEmptyStateAlias));

    private static AdaptiveSurfaceDescriptor ProductSurface()
        => GardenAdaptiveLayouts.Surface(
            GardenAdaptiveLayouts.ProductSurface,
            GardenAdaptiveLayouts.ProductBodyRegion,
            "Product test surface.",
            GardenAdaptiveLayouts.Require(
                "product information",
                GardenComponentCatalog.ProductCoreInfoAlias));

    private static AdaptiveSurfaceDescriptor CartSurface()
        => GardenAdaptiveLayouts.Surface(
            GardenAdaptiveLayouts.CartSurface,
            GardenAdaptiveLayouts.CartBodyRegion,
            "Cart test surface.",
            GardenAdaptiveLayouts.Require(
                "cart controls",
                GardenComponentCatalog.CartItemsAlias,
                GardenComponentCatalog.CompactCartItemsAlias,
                GardenComponentCatalog.CartEmptyStateAlias));

    private static AdaptiveSurfaceDescriptor OrdersSurface()
        => GardenAdaptiveLayouts.Surface(
            GardenAdaptiveLayouts.OrdersSurface,
            GardenAdaptiveLayouts.OrdersBodyRegion,
            "Orders test surface.",
            GardenAdaptiveLayouts.Require(
                "order actions",
                GardenComponentCatalog.OrdersListAlias,
                GardenComponentCatalog.OrderTimelineAlias,
                GardenComponentCatalog.OrderSummaryAlias,
                GardenComponentCatalog.OrderDetailAlias,
                GardenComponentCatalog.OrdersEmptyStateAlias));

    private static IReadOnlyList<AdaptiveDataDescriptor> HomeManifest()
        =>
        [
            Data("catalog", GardenDataContracts.ProductList),
            Data("cart", GardenDataContracts.Cart),
            Data("orders", GardenDataContracts.OrderList),
            Data("recommendation", GardenDataContracts.Recommendation),
        ];

    private static IReadOnlyList<AdaptiveDataDescriptor> CatalogManifest()
        =>
        [
            Data("catalog", GardenDataContracts.ProductList),
            Data("catalog", GardenDataContracts.EmptyProductList) with
            {
                Available = false,
                UnavailableReason = "Products are available.",
            },
            Data("recommendation", GardenDataContracts.Recommendation),
        ];

    private static IReadOnlyList<AdaptiveDataDescriptor> EmptyCatalogManifest()
        =>
        [
            Data("catalog", GardenDataContracts.ProductList) with
            {
                Available = false,
                UnavailableReason = "No products match.",
            },
            Data("catalog", GardenDataContracts.EmptyProductList),
            Data("recommendation", GardenDataContracts.Recommendation) with
            {
                Available = false,
                UnavailableReason = "No recommendation applies.",
            },
        ];

    private static IReadOnlyList<AdaptiveDataDescriptor> ProductManifest()
        =>
        [
            Data("product", GardenDataContracts.Product),
            Data("reviews", GardenDataContracts.ReviewList),
            Data("related", GardenDataContracts.ProductList),
        ];

    private static IReadOnlyList<AdaptiveDataDescriptor> CartManifest()
        =>
        [
            Data("cart", GardenDataContracts.Cart),
            Data("cart", GardenDataContracts.EmptyCart) with
            {
                Available = false,
                UnavailableReason = "The cart has items.",
            },
            Data("suggestions", GardenDataContracts.ProductList),
        ];

    private static IReadOnlyList<AdaptiveDataDescriptor> EmptyCartManifest()
        =>
        [
            Data("cart", GardenDataContracts.Cart) with
            {
                Available = false,
                UnavailableReason = "The cart is empty.",
            },
            Data("cart", GardenDataContracts.EmptyCart),
            Data("suggestions", GardenDataContracts.ProductList),
        ];

    private static IReadOnlyList<AdaptiveDataDescriptor> OrdersManifest()
        =>
        [
            Data("orders", GardenDataContracts.OrderList),
            Data("orders", GardenDataContracts.EmptyOrderList) with
            {
                Available = false,
                UnavailableReason = "Orders are available.",
            },
        ];

    private static IReadOnlyList<AdaptiveDataDescriptor> EmptyOrdersManifest()
        =>
        [
            Data("orders", GardenDataContracts.OrderList) with
            {
                Available = false,
                UnavailableReason = "Order history is empty.",
            },
            Data("orders", GardenDataContracts.EmptyOrderList),
        ];

    private static AdaptiveDataDescriptor Data(string path, string contract)
        => new()
        {
            Path = path,
            Contract = contract,
            Description = $"{contract} test data.",
        };

    private static ComponentLayoutDocument CreateLayout(
        string surface,
        string region,
        IReadOnlyList<(string Alias, string DataPath, string Variant)> components)
    {
        const string rootId = "root";
        var nodes = new List<ComponentLayoutNode>
        {
            new()
            {
                Id = rootId,
                Kind = ComponentLayoutNodeKind.Stack,
                Order = 0,
                Orientation = AdaptiveStackOrientation.Vertical,
                Reason = "Arrange registered whole components.",
            },
        };
        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            nodes.Add(new()
            {
                Id = $"component-{index}",
                Kind = ComponentLayoutNodeKind.Component,
                ParentId = rootId,
                Order = index,
                Component = component.Alias,
                DataPath = component.DataPath,
                Variant = component.Variant,
                Reason = "Answer the current presentation intent.",
            });
        }

        return new()
        {
            LayoutId = $"{surface.ToLowerInvariant()}-intent",
            Revision = 1,
            Surface = surface,
            Regions = [new() { Region = region, RootNodeId = rootId }],
            Nodes = nodes,
        };
    }

    private sealed record LayoutScenario(
        string Name,
        ComponentLayoutDocument Layout,
        AdaptiveSurfaceDescriptor Surface,
        IReadOnlyList<AdaptiveDataDescriptor> Manifest);

    private sealed record IntentScenario(
        string Intent,
        AdaptiveSurfaceDescriptor Surface,
        IReadOnlyList<AdaptiveDataDescriptor> Manifest,
        IReadOnlyList<(string Alias, string DataPath, string Variant)> Components);
}
