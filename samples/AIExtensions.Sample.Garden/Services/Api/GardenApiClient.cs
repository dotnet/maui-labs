using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using AIExtensions.Sample.Garden.Shared;

namespace AIExtensions.Sample.Garden.Services;

public sealed class GardenApiClient(HttpClient httpClient)
{
    public Task<IReadOnlyList<Product>> ListProductsAsync(
        string? category = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(category))
            query.Add($"category={Uri.EscapeDataString(category)}");
        if (!string.IsNullOrWhiteSpace(search))
            query.Add($"search={Uri.EscapeDataString(search)}");

        var path = query.Count == 0 ? "products/" : $"products/?{string.Join('&', query)}";
        return GetListAsync(path, GardenJsonContext.Default.ListProduct, cancellationToken);
    }

    public Task<Product> GetProductAsync(string sku, CancellationToken cancellationToken = default) =>
        GetAsync($"products/{Uri.EscapeDataString(sku)}", GardenJsonContext.Default.Product, cancellationToken);

    public Task<Cart> GetCartAsync(CancellationToken cancellationToken = default) =>
        GetAsync("cart/", GardenJsonContext.Default.Cart, cancellationToken);

    public Task<Cart> AddToCartAsync(string sku, int quantity = 1, CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Post,
            "cart/items",
            new AddToCartRequest(sku, quantity),
            GardenJsonContext.Default.AddToCartRequest,
            GardenJsonContext.Default.Cart,
            cancellationToken);

    public Task<Cart> UpdateCartItemAsync(string sku, int quantity, CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Put,
            $"cart/items/{Uri.EscapeDataString(sku)}",
            new UpdateCartItemRequest(quantity),
            GardenJsonContext.Default.UpdateCartItemRequest,
            GardenJsonContext.Default.Cart,
            cancellationToken);

    public Task<Cart> RemoveFromCartAsync(string sku, CancellationToken cancellationToken = default) =>
        SendAsync($"cart/items/{Uri.EscapeDataString(sku)}", HttpMethod.Delete, GardenJsonContext.Default.Cart, cancellationToken);

    public Task ClearCartAsync(CancellationToken cancellationToken = default) =>
        SendNoContentAsync("cart/", HttpMethod.Delete, cancellationToken);

    public Task<IReadOnlyList<Order>> ListOrdersAsync(CancellationToken cancellationToken = default) =>
        GetListAsync("orders/", GardenJsonContext.Default.ListOrder, cancellationToken);

    public Task<Order> CheckoutAsync(CancellationToken cancellationToken = default) =>
        SendAsync("orders/", HttpMethod.Post, GardenJsonContext.Default.Order, cancellationToken);

    public Task<Order> GetOrderAsync(string orderId, CancellationToken cancellationToken = default) =>
        GetAsync($"orders/{Uri.EscapeDataString(orderId)}", GardenJsonContext.Default.Order, cancellationToken);

    public Task<Cart> ReorderAsync(string orderId, CancellationToken cancellationToken = default) =>
        SendAsync($"orders/{Uri.EscapeDataString(orderId)}/reorder", HttpMethod.Post, GardenJsonContext.Default.Cart, cancellationToken);

    public Task ClearOrdersAsync(CancellationToken cancellationToken = default) =>
        SendNoContentAsync("orders/", HttpMethod.Delete, cancellationToken);

    public Task<IReadOnlyList<Review>> ListReviewsAsync(CancellationToken cancellationToken = default) =>
        GetListAsync("reviews/", GardenJsonContext.Default.ListReview, cancellationToken);

    public Task<IReadOnlyList<Review>> GetProductReviewsAsync(string sku, CancellationToken cancellationToken = default) =>
        GetListAsync($"products/{Uri.EscapeDataString(sku)}/reviews", GardenJsonContext.Default.ListReview, cancellationToken);

    public Task<Review> SubmitReviewAsync(
        string sku,
        int rating,
        string? comment = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Post,
            "reviews/",
            new CreateReviewRequest(sku, rating, comment),
            GardenJsonContext.Default.CreateReviewRequest,
            GardenJsonContext.Default.Review,
            cancellationToken);

    public Task<Recommendation> GetRecommendationsAsync(CancellationToken cancellationToken = default) =>
        GetAsync("recommendations", GardenJsonContext.Default.Recommendation, cancellationToken);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(
        string path,
        JsonTypeInfo<List<T>> typeInfo,
        CancellationToken cancellationToken) =>
        await GetAsync(path, typeInfo, cancellationToken);

    private async Task<T> GetAsync<T>(
        string path,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync(request, typeInfo, cancellationToken);
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest value,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(value, requestTypeInfo),
        };
        return await SendAsync(request, responseTypeInfo, cancellationToken);
    }

    private async Task<T> SendAsync<T>(
        string path,
        HttpMethod method,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        return await SendAsync(request, typeInfo, cancellationToken);
    }

    private async Task SendNoContentAsync(
        string path,
        HttpMethod method,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<T> SendAsync<T>(
        HttpRequestMessage request,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken)
            ?? throw new GardenApiException(response.StatusCode, "The Garden server returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new GardenApiException(
            response.StatusCode,
            string.IsNullOrWhiteSpace(detail)
                ? $"The Garden server returned {(int)response.StatusCode} ({response.ReasonPhrase})."
                : detail);
    }
}

public sealed class GardenApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
