using AIExtensions.Sample.Garden.Models;

namespace AIExtensions.Sample.Garden.ViewModels;

public sealed record OrderInsightValue(string Label, decimal Value);

public sealed record OrderInsightsSnapshot(
    IReadOnlyList<OrderInsightValue> SpendingByCategory,
    IReadOnlyList<OrderInsightValue> PopularProducts)
{
    public static OrderInsightsSnapshot Empty { get; } = new([], []);

    public static OrderInsightsSnapshot Create(IReadOnlyList<Order> orders)
    {
        var items = orders.SelectMany(order => order.Items).ToArray();
        if (items.Length == 0)
            return Empty;

        var spending = items
            .GroupBy(item => item.Product.Category)
            .Select(group => new OrderInsightValue(
                group.Key,
                group.Sum(item => item.Subtotal)))
            .OrderByDescending(item => item.Value)
            .Take(4)
            .ToArray();

        var popular = items
            .GroupBy(item => item.Product.Name)
            .Select(group => new OrderInsightValue(
                group.Key,
                group.Sum(item => item.Quantity)))
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();

        return new OrderInsightsSnapshot(spending, popular);
    }
}
