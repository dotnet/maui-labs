using System.ComponentModel;
using AIExtensions.Sample.Garden.Shared;
using Microsoft.Maui.AI.Attributes;

namespace AIExtensions.Sample.Garden.Services;

public sealed class GardenChatTools(GardenDataStore store)
{
    [ExportAIFunction("list_products")]
    [Description("Lists Garden catalog products, optionally filtered by category or search text.")]
    public async Task<IReadOnlyList<Product>> ListProductsAsync(
        [Description("Optional category filter.")] string? category = null,
        [Description("Optional text matched against product names and descriptions.")] string? search = null)
    {
        await store.RefreshCatalogAsync();
        IEnumerable<Product> products = store.Products;
        if (!string.IsNullOrWhiteSpace(category))
            products = products.Where(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(search))
            products = products.Where(p =>
                p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        return products.ToArray();
    }

    [ExportAIFunction("get_product")]
    [Description("Gets one Garden product by its stable SKU.")]
    public Task<Product> GetProductAsync(
        [Description("The product SKU.")] string sku) =>
        store.GetProductAsync(sku);

    [ExportAIFunction("get_cart")]
    [Description("Gets the current server-backed shopping cart.")]
    public async Task<Cart> GetCartAsync()
    {
        await store.RefreshCartAsync();
        return new Cart(store.CartItems.ToArray(), store.CartTotal);
    }

    [ExportAIFunction("add_to_cart", ApprovalRequired = true)]
    [Description("Adds a catalog product to the cart. This changes server state and requires approval.")]
    public async Task<Cart> AddToCartAsync(
        [Description("Product SKU to add.")] string sku,
        [Description("Quantity to add.")] int quantity = 1)
    {
        await store.AddToCartAsync(sku, quantity);
        return new Cart(store.CartItems.ToArray(), store.CartTotal);
    }

    [ExportAIFunction("set_cart_quantity", ApprovalRequired = true)]
    [Description("Sets an absolute cart quantity. Zero removes the line. This changes server state and requires approval.")]
    public async Task<Cart> SetCartQuantityAsync(
        [Description("Product SKU already in the cart.")] string sku,
        [Description("New absolute quantity.")] int quantity)
    {
        await store.SetCartQuantityAsync(sku, quantity);
        return new Cart(store.CartItems.ToArray(), store.CartTotal);
    }

    [ExportAIFunction("remove_from_cart", ApprovalRequired = true)]
    [Description("Removes a line from the cart. This changes server state and requires approval.")]
    public async Task<Cart> RemoveFromCartAsync(
        [Description("Product SKU to remove.")] string sku)
    {
        await store.RemoveFromCartAsync(sku);
        return new Cart(store.CartItems.ToArray(), store.CartTotal);
    }

    [ExportAIFunction("clear_cart", ApprovalRequired = true)]
    [Description("Clears the cart. This changes server state and requires approval.")]
    public async Task<string> ClearCartAsync()
    {
        await store.ClearCartAsync();
        return "Cart cleared.";
    }

    [ExportAIFunction("list_orders")]
    [Description("Lists server-backed orders, newest first.")]
    public async Task<IReadOnlyList<Order>> ListOrdersAsync()
    {
        await store.RefreshOrdersAsync();
        return store.Orders.ToArray();
    }

    [ExportAIFunction("get_order")]
    [Description("Gets a server-backed order by id.")]
    public Task<Order> GetOrderAsync(
        [Description("Order id.")] string orderId) =>
        store.GetOrderAsync(orderId);

    [ExportAIFunction("checkout", ApprovalRequired = true)]
    [Description("Checks out the current cart. This changes server state and requires approval.")]
    public Task<Order> CheckoutAsync() => store.CheckoutAsync();

    [ExportAIFunction("reorder", ApprovalRequired = true)]
    [Description("Copies an order into the cart. This changes server state and requires approval.")]
    public async Task<Cart> ReorderAsync(
        [Description("Order id to copy.")] string orderId)
    {
        await store.ReorderAsync(orderId);
        return new Cart(store.CartItems.ToArray(), store.CartTotal);
    }

    [ExportAIFunction("clear_orders", ApprovalRequired = true)]
    [Description("Clears all order history. This changes server state and requires approval.")]
    public async Task<string> ClearOrdersAsync()
    {
        await store.ClearOrdersAsync();
        return "Order history cleared.";
    }

    [ExportAIFunction("list_reviews")]
    [Description("Lists all Garden product reviews.")]
    public async Task<IReadOnlyList<Review>> ListReviewsAsync()
    {
        await store.RefreshReviewsAsync();
        return store.Reviews.ToArray();
    }

    [ExportAIFunction("get_product_reviews")]
    [Description("Lists reviews for one product SKU.")]
    public Task<IReadOnlyList<Review>> GetProductReviewsAsync(
        [Description("Product SKU.")] string sku) =>
        store.GetProductReviewsAsync(sku);

    [ExportAIFunction("submit_review", ApprovalRequired = true)]
    [Description("Submits a product review. This changes server state and requires approval.")]
    public Task<Review> SubmitReviewAsync(
        [Description("Product SKU.")] string sku,
        [Description("Rating from 1 to 5.")] int rating,
        [Description("Optional review comment.")] string? comment = null) =>
        store.SubmitReviewAsync(sku, rating, comment);

    [ExportAIFunction("get_recommendations")]
    [Description("Gets the current curated Garden starter recommendation.")]
    public Task<Recommendation> GetRecommendationsAsync() =>
        store.GetRecommendationsAsync();
}
