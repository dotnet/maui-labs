using AIExtensions.Sample.Garden.Models;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// Represents a single product in the catalog grid.
/// </summary>
public sealed class CatalogItemViewModel(Product product)
{
    public string Sku { get; } = product.Sku;
    public string Name { get; } = product.Name;
    public string Emoji { get; } = product.Emoji;
    public decimal Price { get; } = product.Price;
    public string PriceLabel { get; } = $"{product.Price:C}";
    public string Category { get; } = product.Category;
}
