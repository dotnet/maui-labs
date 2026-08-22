using AIExtensions.Sample.Garden.Shared;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// One line item inside an expanded order card.
/// </summary>
public sealed class OrderLineViewModel(CartItem item)
{
    public string Sku => item.Sku;
    public string Emoji => item.Emoji;
    public string ItemDescription => $"{item.Quantity}× {item.Name}";
    public string SubtotalLabel => item.Subtotal.ToString("C");
    public string Line => $"{item.Quantity}× {item.Name}  ·  {item.Subtotal:C}";
}
