using AIExtensions.Sample.Garden.Messages;
using AIExtensions.Sample.Garden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.AI.Attributes;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// Top-level view model for <see cref="Pages.MainPage"/>.
/// Owns page navigation, the new-session action, and the chat template mode toggle.
/// </summary>
public sealed partial class MainViewModel(CurrentCart currentCart) : ObservableObject
{
    private bool _initialized;

    public void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;

        StartNewSession();
    }

    /// <summary>
    /// Whether the chat renders with the rich (fancy) template set. The toggle broadcasts a
    /// <see cref="ChatTemplateModeChangedMessage"/> so the chat view swaps its templates — the header
    /// stays decoupled from the chat view model (messaging, not a shared reference).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemplateToggleIcon))]
    public partial bool IsFancyChat { get; set; } = true;

    /// <summary>Fluent glyph for the toggle button — a sparkle when fancy, plain text lines when plain.</summary>
    public string TemplateToggleIcon => IsFancyChat ? FluentIcons.Sparkle : FluentIcons.TextAlignLeft;

    [RelayCommand]
    private void ToggleChatTemplateMode()
    {
        IsFancyChat = !IsFancyChat;
        WeakReferenceMessenger.Default.Send(new ChatTemplateModeChangedMessage(IsFancyChat));
    }

    [RelayCommand]
    private void StartNewSession()
    {
        currentCart.Clear();
        WeakReferenceMessenger.Default.Send(new StartNewChatSessionMessage());
    }

    [RelayCommand]
    private async Task ShowCartAsync()
    {
        await Shell.Current.GoToAsync("cart");
    }

    // ─── Navigation AI tools ────────────────────────────────────────

    [ExportAIFunction("navigate_to_page",
        Description = "Navigate to a page in the app. Use 'catalog' to browse products, 'orders' to see past orders, 'cart' to view the shopping cart. Pages open as modal overlays.")]
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
        Description = "Close the current modal page (catalog or orders) and return to the main shop view.")]
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
