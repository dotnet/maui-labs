using AIExtensions.Sample.Garden.Messages;
using AIExtensions.Sample.Garden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.AI.Attributes;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// Top-level view model for <see cref="Pages.MainPage"/>.
/// Owns page navigation, initial server refresh, and the new-session action.
/// </summary>
public sealed partial class MainViewModel(GardenDataStore store) : ObservableObject
{
    private bool _initialized;

    public GardenDataStore Store => store;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;
        _initialized = true;

        StartNewSession();
        try
        {
            await store.RefreshAllAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _initialized = false;
            throw;
        }
        catch
        {
            // The fixed ServerUnavailableView exposes the error and retry action.
        }
    }

    [RelayCommand]
    private void StartNewSession()
    {
        WeakReferenceMessenger.Default.Send(new StartNewChatSessionMessage());
    }

    [RelayCommand]
    private async Task ShowCartAsync()
    {
        await Shell.Current.GoToAsync("cart");
    }

    // ─── Navigation AI tools ────────────────────────────────────────

    [ExportAIFunction("navigate_to_page",
        Description = "Navigate to a page in the app. Use 'catalog' to browse products, 'orders' to see past orders, or 'cart' to view the shopping cart. The persistent chat remains available beside non-home pages on wide windows.")]
    public async Task<string> NavigateToPageAsync(
        [System.ComponentModel.Description("The page to navigate to: 'catalog', 'orders', or 'cart'")] string page)
    {
        var route = page?.ToLowerInvariant() switch
        {
            "catalog" or "products" => "//main/products",
            "orders" => "//main/orders",
            "cart" => "cart",
            "chat" or "home" => "//main/chat",
            _ => throw new ArgumentException($"Unknown page '{page}'. Valid pages: 'catalog', 'orders', 'cart', 'chat'.")
        };

        var tcs = new TaskCompletionSource();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await Shell.Current.GoToAsync(route);
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        await tcs.Task;
        return $"Navigated to {page}. The {page} page is now showing.";
    }

    [ExportAIFunction("dismiss_page",
        Description = "Close the current page or modal and return toward the main shop view without resetting the chat.")]
    public async Task<string> DismissPageAsync()
    {
        var tcs = new TaskCompletionSource();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await Shell.Current.GoToAsync("..");
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        await tcs.Task;
        return "Returned to the main shop view.";
    }
}
