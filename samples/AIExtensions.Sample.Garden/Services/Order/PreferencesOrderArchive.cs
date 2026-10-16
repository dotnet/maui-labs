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
    private const string InitializedKey = "garden_orders_initialized";

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
        Preferences.Default.Set(InitializedKey, true);
        Save(_cache);
    }

    private List<Order> LoadOrders()
    {
        if (_cache is not null)
            return _cache;

        var json = Preferences.Default.Get<string?>(StorageKey, null);
        if (!Preferences.Default.Get(InitializedKey, false))
        {
            var existing = Deserialize(json);
            _cache = CreateSeedOrders()
                .Concat(existing)
                .GroupBy(order => order.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(order => order.PlacedAt)
                .ToList();
            Preferences.Default.Set(InitializedKey, true);
            Save(_cache);
            return _cache;
        }

        _cache = Deserialize(json);
        return _cache;

        static List<Order> Deserialize(string? json)
        {
            if (string.IsNullOrEmpty(json))
                return [];

            try
            {
                return JsonSerializer.Deserialize<List<Order>>(json, s_json) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
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

    private static List<Order> CreateSeedOrders()
    {
        var now = DateTime.Now;
        return
        [
            new Order(
                "DEMO-00003",
                now.AddDays(-4),
                [
                    new ListItem(GetProduct("seed-basil"), 5),
                    new ListItem(GetProduct("seed-tomato"), 3),
                    new ListItem(GetProduct("soil-pottingmix"), 1),
                ]),
            new Order(
                "DEMO-00002",
                now.AddDays(-13),
                [
                    new ListItem(GetProduct("tool-trowel"), 1),
                    new ListItem(GetProduct("tool-glove"), 2),
                    new ListItem(GetProduct("soil-compost"), 1),
                ]),
            new Order(
                "DEMO-00001",
                now.AddDays(-28),
                [
                    new ListItem(GetProduct("seed-sunflower"), 4),
                    new ListItem(GetProduct("fert-allpurpose"), 1),
                    new ListItem(GetProduct("tool-watering"), 1),
                ]),
        ];

        static Product GetProduct(string sku)
            => ProductCatalog.FindByName(sku)
                ?? throw new InvalidOperationException(
                    $"Seed order product '{sku}' is missing from the catalog.");
    }
}
