using AIExtensions.Sample.Garden.Models;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// View-model wrapper around a <see cref="ListItem"/> for display in the cart.
/// </summary>
public sealed class CartItemViewModel(ListItem item)
{
    public ListItem Item { get; } = item;

    public string Sku => Item.Product.Sku;
    public string Name => Item.Product.Name;
    public string Emoji => Item.Product.Emoji;
    public int Quantity => Item.Quantity;
    public string QuantityLine => $"× {Item.Quantity}  ·  {Item.Subtotal:C}";
}
