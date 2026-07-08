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
    /// Handler axis: whether the chat uses the custom Garden block handlers (custom blocks) or only the
    /// built-in defaults (raw function-call blocks). Toggling sends a <see cref="StartNewChatSessionMessage"/>
    /// with the new mode so the chat view recreates its session with that handler set.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HandlerToggleIcon))]
    public partial bool UseCustomHandlers { get; set; } = true;

    /// <summary>Fluent glyph for the handler toggle — a toolbox when custom, a box when raw/defaults.</summary>
    public string HandlerToggleIcon => UseCustomHandlers ? FluentIcons.Toolbox : FluentIcons.Box;

    [RelayCommand]
    private void ToggleChatHandlerMode()
    {
        UseCustomHandlers = !UseCustomHandlers;
        // Switching handlers starts a fresh conversation with the new handler set (the chat view
        // recreates its session because the mode differs from the current one).
        WeakReferenceMessenger.Default.Send(new StartNewChatSessionMessage(UseCustomHandlers));
    }

    /// <summary>
    /// Rendering axis: whether blocks render through the raw block-preview inspector instead of the
    /// designed views. Broadcasts <see cref="ChatBlockPreviewModeChangedMessage"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewToggleIcon))]
    public partial bool IsPreview { get; set; }

    /// <summary>Fluent glyph for the preview toggle — a beaker when inspecting, a leaf for designed views.</summary>
    public string PreviewToggleIcon => IsPreview ? FluentIcons.Beaker : FluentIcons.LeafOne;

    [RelayCommand]
    private void ToggleChatPreviewMode()
    {
        IsPreview = !IsPreview;
        WeakReferenceMessenger.Default.Send(new ChatBlockPreviewModeChangedMessage(IsPreview));
    }

    [RelayCommand]
    private void StartNewSession()
    {
        currentCart.Clear();
        // Same handler mode → the chat view just clears the current conversation.
        WeakReferenceMessenger.Default.Send(new StartNewChatSessionMessage(UseCustomHandlers));
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
