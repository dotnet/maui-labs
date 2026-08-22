using System.Collections.ObjectModel;
using System.Net;
using AIExtensions.Sample.Garden.Messages;
using AIExtensions.Sample.Garden.Shared;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace AIExtensions.Sample.Garden.Services;

public sealed partial class GardenDataStore(GardenApiClient api) : ObservableObject
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<CartItem> CartItems { get; } = [];
    public ObservableCollection<Order> Orders { get; } = [];
    public ObservableCollection<Review> Reviews { get; } = [];

    [ObservableProperty]
    public partial decimal CartTotal { get; private set; }

    [ObservableProperty]
    public partial Recommendation? Recommendation { get; private set; }

    [ObservableProperty]
    public partial bool IsServerUnavailable { get; private set; }

    [ObservableProperty]
    public partial string? ServerError { get; private set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; private set; }

    public async Task RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        IsRefreshing = true;
        try
        {
            await RefreshCatalogCoreAsync(cancellationToken);
            await RefreshCartCoreAsync(cancellationToken);
            await RefreshOrdersCoreAsync(cancellationToken);
            await RefreshReviewsCoreAsync(cancellationToken);
            Recommendation = await ExecuteAsync(api.GetRecommendationsAsync, cancellationToken);
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
    }

    public async Task RefreshCatalogAsync(CancellationToken cancellationToken = default) =>
        Replace(Products, await ExecuteAsync(
            ct => api.ListProductsAsync(cancellationToken: ct),
            cancellationToken));

    public async Task<Product> GetProductAsync(string sku, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(ct => api.GetProductAsync(sku, ct), cancellationToken);

    public async Task RefreshCartAsync(CancellationToken cancellationToken = default) =>
        ApplyCart(await ExecuteAsync(api.GetCartAsync, cancellationToken));

    public async Task AddToCartAsync(string sku, int quantity = 1, CancellationToken cancellationToken = default)
    {
        ApplyCart(await ExecuteAsync(ct => api.AddToCartAsync(sku, quantity, ct), cancellationToken));
        NotifyCartChanged();
    }

    public async Task SetCartQuantityAsync(string sku, int quantity, CancellationToken cancellationToken = default)
    {
        ApplyCart(await ExecuteAsync(ct => api.UpdateCartItemAsync(sku, quantity, ct), cancellationToken));
        NotifyCartChanged();
    }

    public async Task RemoveFromCartAsync(string sku, CancellationToken cancellationToken = default)
    {
        ApplyCart(await ExecuteAsync(ct => api.RemoveFromCartAsync(sku, ct), cancellationToken));
        NotifyCartChanged();
    }

    public async Task ClearCartAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(api.ClearCartAsync, cancellationToken);
        ApplyCart(new Cart([], 0));
        NotifyCartChanged();
    }

    public async Task RefreshOrdersAsync(CancellationToken cancellationToken = default) =>
        Replace(Orders, await ExecuteAsync(api.ListOrdersAsync, cancellationToken));

    public async Task<Order> CheckoutAsync(CancellationToken cancellationToken = default)
    {
        var order = await ExecuteAsync(api.CheckoutAsync, cancellationToken);
        await RefreshOrdersAsync(cancellationToken);
        ApplyCart(new Cart([], 0));
        NotifyCartChanged();
        NotifyOrdersChanged();
        return order;
    }

    public async Task<Order> GetOrderAsync(string orderId, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(ct => api.GetOrderAsync(orderId, ct), cancellationToken);

    public async Task ReorderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        ApplyCart(await ExecuteAsync(ct => api.ReorderAsync(orderId, ct), cancellationToken));
        NotifyCartChanged();
    }

    public async Task ClearOrdersAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(api.ClearOrdersAsync, cancellationToken);
        Orders.Clear();
        NotifyOrdersChanged();
    }

    public async Task RefreshReviewsAsync(CancellationToken cancellationToken = default) =>
        Replace(Reviews, await ExecuteAsync(api.ListReviewsAsync, cancellationToken));

    public async Task<IReadOnlyList<Review>> GetProductReviewsAsync(
        string sku,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync(ct => api.GetProductReviewsAsync(sku, ct), cancellationToken);

    public async Task<Review> SubmitReviewAsync(
        string sku,
        int rating,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        var review = await ExecuteAsync(ct => api.SubmitReviewAsync(sku, rating, comment, ct), cancellationToken);
        Reviews.Insert(0, review);
        return review;
    }

    public async Task<Recommendation> GetRecommendationsAsync(CancellationToken cancellationToken = default)
    {
        Recommendation = await ExecuteAsync(api.GetRecommendationsAsync, cancellationToken);
        return Recommendation;
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        try
        {
            await RefreshAllAsync();
        }
        catch
        {
            // ExecuteAsync has already populated the fixed server-unavailable UI.
        }
    }

    private async Task RefreshCatalogCoreAsync(CancellationToken cancellationToken) =>
        Replace(Products, await ExecuteAsync(
            ct => api.ListProductsAsync(cancellationToken: ct),
            cancellationToken));

    private async Task RefreshCartCoreAsync(CancellationToken cancellationToken) =>
        ApplyCart(await ExecuteAsync(api.GetCartAsync, cancellationToken));

    private async Task RefreshOrdersCoreAsync(CancellationToken cancellationToken) =>
        Replace(Orders, await ExecuteAsync(api.ListOrdersAsync, cancellationToken));

    private async Task RefreshReviewsCoreAsync(CancellationToken cancellationToken) =>
        Replace(Reviews, await ExecuteAsync(api.ListReviewsAsync, cancellationToken));

    private async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await operation(cancellationToken);
            IsServerUnavailable = false;
            ServerError = null;
            return result;
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            IsServerUnavailable = true;
            ServerError = ex.Message;
            throw;
        }
    }

    private async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation(cancellationToken);
            IsServerUnavailable = false;
            ServerError = null;
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            IsServerUnavailable = true;
            ServerError = ex.Message;
            throw;
        }
    }

    private static bool IsConnectionFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException ||
        exception is GardenApiException { StatusCode: >= HttpStatusCode.InternalServerError };

    private void ApplyCart(Cart cart)
    {
        Replace(CartItems, cart.Items);
        CartTotal = cart.Total;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    private static void NotifyCartChanged() =>
        WeakReferenceMessenger.Default.Send(new CartChangedMessage());

    private static void NotifyOrdersChanged() =>
        WeakReferenceMessenger.Default.Send(new OrdersChangedMessage());
}
