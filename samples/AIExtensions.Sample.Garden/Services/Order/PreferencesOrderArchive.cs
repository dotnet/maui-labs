using System.Text.Json;
using AIExtensions.Sample.Garden.Models;

namespace AIExtensions.Sample.Garden.Services;

/// <summary>
/// Preferences-backed order archive. Orders survive app restarts.
/// Demonstrates that any IOrderArchive implementation automatically
/// inherits AI tool capability — no attribute changes needed.
/// </summary>
public sealed class PreferencesOrderArchive : IOrderArchive
{
    private const string StorageKey = "garden_orders";

    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private List<Order>? _cache;

    public IReadOnlyList<Order> Orders => LoadOrders();

    public Order? FindOrder(string orderId) =>
        LoadOrders().FirstOrDefault(o => string.Equals(o.Id, orderId, StringComparison.OrdinalIgnoreCase));

    public Order Checkout(CurrentCart cart)
    {
        var items = cart.Items;
        if (items.Count == 0)
            throw new InvalidOperationException("The cart is empty — nothing to check out.");

        var order = new Order(
            Id: NextOrderId(),
            PlacedAt: DateTime.Now,
            Items: [.. items]);

        var orders = LoadOrders();
        orders.Insert(0, order);
        Save(orders);

        cart.Clear();
        return order;
    }

    public void Reorder(string orderId, CurrentCart cart)
    {
        var order = FindOrder(orderId)
            ?? throw new InvalidOperationException($"No past order with id '{orderId}'. Call list_past_orders to see available ids.");
        foreach (var item in order.Items)
            cart.AddItem(item.Product.Sku, item.Quantity);
    }

    public void Clear()
    {
        _cache = [];
        Preferences.Default.Remove(StorageKey);
    }

    private List<Order> LoadOrders()
    {
        if (_cache is not null)
            return _cache;

        var json = Preferences.Default.Get<string?>(StorageKey, null);
        if (string.IsNullOrEmpty(json))
        {
            _cache = [];
            return _cache;
        }

        try
        {
            _cache = JsonSerializer.Deserialize<List<Order>>(json, s_json) ?? [];
        }
        catch
        {
            _cache = [];
        }
        return _cache;
    }

    private void Save(List<Order> orders)
    {
        _cache = orders;
        var json = JsonSerializer.Serialize(orders, s_json);
        Preferences.Default.Set(StorageKey, json);
    }

    private static string NextOrderId()
    {
        const string counterKey = "garden_order_counter";
        var next = Preferences.Default.Get(counterKey, 0) + 1;
        Preferences.Default.Set(counterKey, next);
        return $"ORD-{next:D5}";
    }
}
