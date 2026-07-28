using AIExtensions.Sample.Garden.Models;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// One line item inside an expanded order card.
/// </summary>
public sealed class OrderLineViewModel(ListItem item)
{
    public string Sku => item.Product.Sku;
    public string Emoji => item.Product.Emoji;
    public string ItemDescription => $"{item.Quantity}× {item.Product.Name}";
    public string SubtotalLabel => item.Subtotal.ToString("C");
    public string Line => $"{item.Quantity}× {item.Product.Name}  ·  {item.Subtotal:C}";
}
