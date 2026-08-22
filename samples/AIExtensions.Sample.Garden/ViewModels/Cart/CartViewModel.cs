using System.Collections.ObjectModel;
using AIExtensions.Sample.Garden.Messages;
using AIExtensions.Sample.Garden.Shared;
using AIExtensions.Sample.Garden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// Owns all cart state: items, display mode, checkout, and the AI tools
/// that manipulate the cart display. Designed to be reusable — any page
/// can host a cart view bound to this VM.
/// </summary>
public sealed partial class CartViewModel : ObservableObject, IRecipient<CartChangedMessage>
{
    private readonly GardenDataStore _store;

    public CartViewModel(GardenDataStore store)
    {
        _store = store;

        WeakReferenceMessenger.Default.Register(this);
        RefreshFromStore();
    }

    void IRecipient<CartChangedMessage>.Receive(CartChangedMessage message) => RefreshFromStore();

    public ObservableCollection<CartItemViewModel> Items { get; } = [];

    public GardenDataStore Store => _store;

    [ObservableProperty]
    public partial string CartTotal { get; set; } = $"Total: {0:C}";

    [ObservableProperty]
    public partial bool HasItems { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNormalMode))]
    [NotifyPropertyChangedFor(nameof(IsCompactMode))]
    [NotifyPropertyChangedFor(nameof(CartModeLabel))]
    public partial CartMode CartMode
    {
        get;
        set;
    } = CartMode.Normal;

    public bool IsNormalMode => CartMode == CartMode.Normal;
    public bool IsCompactMode => CartMode == CartMode.Compact;
    public string CartModeLabel => CartMode switch
    {
        CartMode.Normal => "Compact",
        CartMode.Compact => "Normal",
        _ => "Toggle"
    };

    [RelayCommand]
    private void CycleCartMode()
    {
        CartMode = CartMode switch
        {
            CartMode.Normal => CartMode.Compact,
            CartMode.Compact => CartMode.Normal,
            _ => CartMode.Normal
        };
    }

    [RelayCommand]
    private async Task CheckoutAsync()
    {
        if (_store.CartItems.Count == 0)
            return;

        await RunAsync(_store.CheckoutAsync);
    }

    [RelayCommand]
    private async Task AddFromCatalogAsync(string? sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return;

        await RunAsync(ct => _store.AddToCartAsync(sku, cancellationToken: ct));
    }

    [RelayCommand]
    private async Task IncreaseAsync(string? sku)
    {
        var item = _store.CartItems.FirstOrDefault(i => i.Sku == sku);
        if (item is not null)
            await RunAsync(ct => _store.SetCartQuantityAsync(item.Sku, item.Quantity + 1, ct));
    }

    [RelayCommand]
    private async Task DecreaseAsync(string? sku)
    {
        var item = _store.CartItems.FirstOrDefault(i => i.Sku == sku);
        if (item is not null)
            await RunAsync(ct => _store.SetCartQuantityAsync(item.Sku, item.Quantity - 1, ct));
    }

    [RelayCommand]
    private async Task RemoveAsync(string? sku)
    {
        if (!string.IsNullOrWhiteSpace(sku))
            await RunAsync(ct => _store.RemoveFromCartAsync(sku, ct));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _store.RefreshCartAsync(cancellationToken);
            RefreshFromStore();
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void RefreshFromStore()
    {
        var source = _store.CartItems;
        SyncCollection(Items, source, v => v.Sku, i => i.Sku, i => new CartItemViewModel(i));
        CartTotal = _store.CartTotal.ToString("C");
        HasItems = source.Count > 0;
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        try
        {
            await action(CancellationToken.None);
            RefreshFromStore();
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task RunAsync<T>(Func<CancellationToken, Task<T>> action)
    {
        try
        {
            await action(CancellationToken.None);
            RefreshFromStore();
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private static void SyncCollection<TVM, TModel>(
        ObservableCollection<TVM> target,
        IReadOnlyList<TModel> source,
        Func<TVM, string> vmKey,
        Func<TModel, string> modelKey,
        Func<TModel, TVM> create)
    {
        for (int sourceIndex = 0; sourceIndex < source.Count; sourceIndex++)
        {
            var model = source[sourceIndex];
            var key = modelKey(model);
            var existingIndex = -1;

            for (int targetIndex = 0; targetIndex < target.Count; targetIndex++)
            {
                if (vmKey(target[targetIndex]) == key)
                {
                    existingIndex = targetIndex;
                    break;
                }
            }

            var viewModel = create(model);

            if (existingIndex < 0)
            {
                target.Insert(sourceIndex, viewModel);
                continue;
            }

            if (existingIndex != sourceIndex)
                target.Move(existingIndex, sourceIndex);

            target[sourceIndex] = viewModel;
        }

        while (target.Count > source.Count)
            target.RemoveAt(target.Count - 1);
    }
}
