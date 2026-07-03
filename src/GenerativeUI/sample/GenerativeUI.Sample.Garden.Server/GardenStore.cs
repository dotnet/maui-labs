using System.Collections.Concurrent;
using GenerativeUI.Sample.Garden.Shared;

namespace GenerativeUI.Sample.Garden.Server;

/// <summary>
/// Thread-safe in-memory backing store for the Garden sample. Seeded at construction. This is the
/// authoritative state the REST endpoints read and mutate — there is no database.
/// </summary>
public sealed class GardenStore
{
    private readonly ConcurrentDictionary<string, Product> _products = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _cart = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Order> _orders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Review> _reviews = new(StringComparer.OrdinalIgnoreCase);

    public GardenStore()
    {
        foreach (var p in Seed())
            _products[p.Sku] = p;
    }

    private static IEnumerable<Product> Seed() =>
    [
        new("basil-seeds", "Basil Seeds", "Sweet Genovese basil — fast-growing, fragrant, and perfect for pesto. Sow indoors and transplant after the last frost.", 3.49m, "seeds", "\ud83c\udf3f", null, 120),
        new("tomato-seeds", "Tomato Seeds", "Heirloom beefsteak tomatoes with rich, old-fashioned flavor. Indeterminate vines crop all season with support.", 4.25m, "seeds", "\ud83c\udf45", null, 80),
        new("terracotta-pot", "Terracotta Pot", "Classic 8-inch terracotta pot with a drainage hole. Breathable clay keeps roots healthy and prevents overwatering.", 9.99m, "tools", "\ud83e\udea3", null, 40),
        new("watering-can", "Watering Can", "2-gallon galvanized-steel watering can with a removable brass rose for a gentle, even shower.", 18.50m, "tools", "\ud83d\udea3", null, 15),
        new("potting-soil", "Potting Soil", "Organic all-purpose potting mix with coco coir and perlite for excellent drainage and aeration.", 12.00m, "soil", "\ud83e\udeb4", null, 60),
    ];

    // Products
    public IReadOnlyList<Product> ListProducts(string? category, string? search)
    {
        IEnumerable<Product> query = _products.Values;
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p =>
                p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        return query.OrderBy(p => p.Name).ToList();
    }

    public Product? GetProduct(string sku) => _products.GetValueOrDefault(sku);

    public Product CreateProduct(CreateProductRequest r)
    {
        var sku = Slug(r.Name);
        var product = new Product(sku, r.Name, r.Description, r.Price, r.Category, r.Emoji ?? "\ud83c\udf31", r.ImageUrl, r.Quantity);
        _products[sku] = product;
        return product;
    }

    public Product? UpdateProduct(string sku, UpdateProductRequest r)
    {
        if (!_products.TryGetValue(sku, out var p))
            return null;
        var updated = p with
        {
            Name = r.Name ?? p.Name,
            Description = r.Description ?? p.Description,
            Price = r.Price ?? p.Price,
            Category = r.Category ?? p.Category,
            Emoji = r.Emoji ?? p.Emoji,
            ImageUrl = r.ImageUrl ?? p.ImageUrl,
            Quantity = r.Quantity ?? p.Quantity,
        };
        _products[sku] = updated;
        return updated;
    }

    public bool DeleteProduct(string sku)
    {
        _cart.TryRemove(sku, out _);
        return _products.TryRemove(sku, out _);
    }

    // Cart
    public Cart GetCart()
    {
        var items = new List<CartItem>();
        foreach (var (sku, qty) in _cart)
        {
            if (!_products.TryGetValue(sku, out var p))
                continue;
            items.Add(new CartItem(p.Sku, p.Name, p.Price, qty, p.Price * qty));
        }
        items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return new Cart(items, items.Sum(i => i.Subtotal));
    }

    public Cart AddToCart(AddToCartRequest r)
    {
        var qty = r.Quantity <= 0 ? 1 : r.Quantity;
        _cart.AddOrUpdate(r.Sku, qty, (_, existing) => existing + qty);
        return GetCart();
    }

    public Cart SetCartQuantity(string sku, int quantity)
    {
        if (quantity <= 0)
            _cart.TryRemove(sku, out _);
        else
            _cart[sku] = quantity;
        return GetCart();
    }

    public Cart RemoveFromCart(string sku)
    {
        _cart.TryRemove(sku, out _);
        return GetCart();
    }

    public void ClearCart() => _cart.Clear();

    // Orders
    public Order Checkout()
    {
        var cart = GetCart();
        var order = new Order(Guid.NewGuid().ToString("n")[..8], cart.Items, cart.Total, DateTimeOffset.UtcNow);
        _orders[order.Id] = order;
        _cart.Clear();
        return order;
    }

    public IReadOnlyList<Order> ListOrders() => _orders.Values.OrderByDescending(o => o.PlacedAt).ToList();

    public Order? GetOrder(string id) => _orders.GetValueOrDefault(id);

    public Cart? Reorder(string id)
    {
        if (!_orders.TryGetValue(id, out var order))
            return null;
        foreach (var item in order.Items)
            _cart.AddOrUpdate(item.Sku, item.Quantity, (_, existing) => existing + item.Quantity);
        return GetCart();
    }

    public void ClearOrders() => _orders.Clear();

    // Reviews
    public IReadOnlyList<Review> ListReviews() => _reviews.Values.OrderByDescending(r => r.CreatedAt).ToList();

    public IReadOnlyList<Review> GetProductReviews(string sku) =>
        _reviews.Values.Where(r => string.Equals(r.Sku, sku, StringComparison.OrdinalIgnoreCase))
                       .OrderByDescending(r => r.CreatedAt).ToList();

    public Review CreateReview(CreateReviewRequest r)
    {
        var review = new Review(Guid.NewGuid().ToString("n")[..8], r.Sku, Math.Clamp(r.Rating, 1, 5), r.Comment, DateTimeOffset.UtcNow);
        _reviews[review.Id] = review;
        return review;
    }

    // Recommendations
    public Recommendation GetRecommendations()
    {
        var picks = _products.Values.OrderBy(p => p.Price).Take(3).ToList();
        return new Recommendation(
            "Starter Garden Bundle",
            "A budget-friendly trio to get a first-time gardener growing: easy seeds plus the essentials.",
            picks);
    }

    private static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        return new string(chars).Trim('-').Replace("--", "-");
    }
}
