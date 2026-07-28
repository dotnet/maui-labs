using System.ComponentModel;
using AIExtensions.Sample.Garden.Models;
using Microsoft.Maui.AI.Attributes;

namespace AIExtensions.Sample.Garden.Services;

/// <summary>
/// Hard-coded product catalog for the garden shop.
/// Demonstrates: exporting tools from a static class.
/// </summary>
public static class ProductCatalog
{
    // Feature: [ExportAIFunction] on a static property — exposes a read-only
    // collection as a zero-parameter AI tool. The generator treats the getter
    // as a parameterless method and emits a schema with no inputs.
    [ExportAIFunction("list_all_products")]
    [Description("Returns every product in the garden shop catalog.")]
    public static IReadOnlyList<Product> All { get; } =
    [
        // Seeds — icon font glyphs (FluentFilled)
        new("seed-tomato",     "Heirloom Tomato Seeds",   "Seeds",      3.49m, FluentIcons.Food),
        new("seed-basil",      "Sweet Basil Seeds",       "Seeds",      2.49m, FluentIcons.LeafOne),
        new("seed-pepper",     "Bell Pepper Seeds",       "Seeds",      2.99m, FluentIcons.Temperature),
        new("seed-sunflower",  "Giant Sunflower Seeds",   "Seeds",      3.99m, FluentIcons.WeatherSunny),
        new("seed-lettuce",    "Mixed Lettuce Seeds",     "Seeds",      2.29m, FluentIcons.BowlSalad),

        // Soil & amendments
        new("soil-pottingmix", "All-Purpose Potting Mix", "Soil",      11.99m, FluentIcons.Earth),
        new("soil-compost",    "Organic Compost (10 lb)", "Soil",       8.49m, FluentIcons.LeafThree),
        new("soil-mulch",      "Cedar Mulch (2 cu ft)",   "Soil",      14.99m, FluentIcons.Box),

        // Fertilizer
        new("fert-tomato",     "Tomato Plant Food",       "Fertilizer", 9.99m, FluentIcons.Drop),
        new("fert-allpurpose", "All-Purpose Fertilizer",  "Fertilizer", 7.99m, FluentIcons.Beaker),

        // Tools & equipment
        new("tool-trowel",     "Hand Trowel",             "Tools",     12.49m, FluentIcons.Wrench),
        new("tool-pruner",     "Bypass Pruners",          "Tools",     18.99m, FluentIcons.Cut),
        new("tool-glove",      "Garden Gloves (pair)",    "Tools",      6.99m, FluentIcons.HandRight),
        new("tool-hose",       "50 ft Garden Hose",       "Equipment", 29.99m, FluentIcons.PaintBrush),
        new("tool-watering",   "Watering Can (1 gal)",    "Equipment", 14.99m, FluentIcons.Drop),
    ];

    // Feature: [ExportAIFunction] on a static method with an optional parameter.
    // The generator emits a schema where 'query' is not required, letting the
    // AI call it with or without a filter string.
    [ExportAIFunction("search_products")]
    [Description("Searches the garden shop catalog by name, category, or sku. Returns every product when no query is given.")]
    public static List<Product> SearchProducts(
        [Description("Optional text to filter by product name, sku, or category. Leave blank to list everything.")]
        string? query = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [.. All];

        var q = query.Trim();
        return [.. All.Where(p =>
            p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            p.Sku.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            p.Category.Contains(q, StringComparison.OrdinalIgnoreCase))];
    }

    // Feature: [ExportAIFunction] with a custom tool name that differs from
    // the method name. The AI sees "get_product" but the real method is FindByName.
    [ExportAIFunction("get_product")]
    [Description("Looks up a single product by sku or exact name.")]
    public static Product? FindByName(
        [Description("The product sku or exact name (e.g., 'seed-tomato' or 'Heirloom Tomato Seeds').")]
        string nameOrSku)
    {
        if (string.IsNullOrWhiteSpace(nameOrSku))
            return null;
        var q = nameOrSku.Trim();
        return All.FirstOrDefault(p => string.Equals(p.Sku, q, StringComparison.OrdinalIgnoreCase))
            ?? All.FirstOrDefault(p => string.Equals(p.Name, q, StringComparison.OrdinalIgnoreCase))
            ?? All.FirstOrDefault(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
    }
}
