using AIExtensions.Sample.Garden.Models;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// View model for one row in the past-orders list.
/// </summary>
public sealed class OrderViewModel(Order order)
{
    public string OrderId => order.Id;
    public string PlacedAt => order.PlacedAt.ToString("MMM d, h:mm tt");
    public string Total => order.Total.ToString("C");
    public int ItemCount => order.Items.Count;
    public string ItemCountLabel => $"{order.Items.Count} item{(order.Items.Count != 1 ? "s" : "")}";
    public IReadOnlyList<OrderLineViewModel> Lines { get; } =
        [.. order.Items.Select(i => new OrderLineViewModel(i))];
}
