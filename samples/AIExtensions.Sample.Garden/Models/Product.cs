namespace AIExtensions.Sample.Garden.Models;

/// <summary>
/// A product in the garden shop catalog (seeds, soil, tools, fertilizer, etc.).
/// </summary>
/// <param name="Sku">Stable id used by tools.</param>
/// <param name="Name">Display name shown in chat and on cards.</param>
/// <param name="Category">Top-level grouping for the workspace panel.</param>
/// <param name="Price">Unit price in USD.</param>
/// <param name="Emoji">Emoji shown next to the product everywhere.</param>
public record Product(
    string Sku,
    string Name,
    string Category,
    decimal Price,
    string Emoji);
