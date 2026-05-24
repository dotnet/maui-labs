namespace AIExtensions.Sample.Garden.Models;

/// <summary>
/// A line item in a cart or order.
/// </summary>
public record ListItem(
    Product Product,
    int Quantity)
{
    public decimal Subtotal => Product.Price * Quantity;
}
