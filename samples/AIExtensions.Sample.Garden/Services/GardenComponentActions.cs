using AIExtensions.Sample.Garden.Components;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace AIExtensions.Sample.Garden.Services;

public sealed class GardenComponentActions(
    GardenDataStore store,
    AdaptiveSurfaceCoordinator coordinator) : IGardenComponentActions
{
    public async Task NavigateAsync(string destination)
    {
        var route = destination.ToLowerInvariant() switch
        {
            "catalog" => "//main/products",
            "orders" => "//main/orders",
            "cart" => "cart",
            "home" or "chat" => "//main/chat",
            _ => throw new ArgumentException($"Unknown Garden destination '{destination}'.", nameof(destination)),
        };
        await RunNavigationAsync(route);
    }

    public async Task OpenProductAsync(string sku)
        => await RunNavigationAsync($"product?sku={Uri.EscapeDataString(sku)}");

    public async Task AddToCartAsync(string sku)
        => await RunMutationAsync(() => store.AddToCartAsync(sku));

    public async Task SetCartQuantityAsync(string sku, int quantity)
        => await RunMutationAsync(() => store.SetCartQuantityAsync(sku, Math.Max(0, quantity)));

    public async Task RemoveFromCartAsync(string sku)
        => await RunMutationAsync(() => store.RemoveFromCartAsync(sku));

    public async Task OpenOrderAsync(string orderId)
        => await RunNavigationAsync($"order?orderId={Uri.EscapeDataString(orderId)}");

    public async Task ReorderAsync(string orderId)
        => await RunMutationAsync(() => store.ReorderAsync(orderId));

    private static async Task ShowFailureAsync(Exception exception)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is not null)
            await page.DisplayAlertAsync("Garden action failed", exception.Message, "OK");
    }

    private async Task RunMutationAsync(Func<Task> action)
    {
        try
        {
            await action();
            await coordinator.RefreshAsync();
        }
        catch (Exception ex) when (
            ex is HttpRequestException or GardenApiException or TaskCanceledException)
        {
            await ShowFailureAsync(ex);
        }
    }

    private static async Task RunNavigationAsync(string route)
    {
        try
        {
            await Shell.Current.GoToAsync(route);
        }
        catch (InvalidOperationException ex)
        {
            await ShowFailureAsync(ex);
        }
    }
}
