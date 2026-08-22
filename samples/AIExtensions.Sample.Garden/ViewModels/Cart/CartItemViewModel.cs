using AIExtensions.Sample.Garden.Shared;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// View-model wrapper around a server <see cref="CartItem"/> for display in the cart.
/// </summary>
public sealed class CartItemViewModel(CartItem item)
{
    public CartItem Item { get; } = item;

    public string Sku => Item.Sku;
    public string Name => Item.Name;
    public string Emoji => Item.Emoji;
    public int Quantity => Item.Quantity;
    public string QuantityLine => $"× {Item.Quantity}  ·  {Item.Subtotal:C}";
}
