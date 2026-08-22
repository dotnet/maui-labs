using System.Collections.ObjectModel;
using AIExtensions.Sample.Garden.Messages;
using AIExtensions.Sample.Garden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// Owns order history state, reorder, and clear actions.
/// </summary>
public sealed partial class OrdersViewModel : ObservableObject, IRecipient<ChatTurnCompletedMessage>
{
    private readonly GardenDataStore _store;

    public OrdersViewModel(GardenDataStore store)
    {
        _store = store;

        WeakReferenceMessenger.Default.Register(this);
        RefreshFromStore();
    }

    public ObservableCollection<OrderViewModel> Orders { get; } = [];

    public GardenDataStore Store => _store;

    void IRecipient<ChatTurnCompletedMessage>.Receive(ChatTurnCompletedMessage message)
        => RefreshFromStore();

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [RelayCommand]
    private async Task ReorderAsync(string? orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return;
        await RunAsync(ct => _store.ReorderAsync(orderId, ct));
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        await RunAsync(_store.ClearOrdersAsync);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _store.RefreshOrdersAsync(cancellationToken);
            RefreshFromStore();
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public void RefreshFromStore()
    {
        var source = _store.Orders;
        var sourceKeys = new HashSet<string>(source.Select(o => o.Id));
        for (int i = Orders.Count - 1; i >= 0; i--)
        {
            if (!sourceKeys.Contains(Orders[i].OrderId))
                Orders.RemoveAt(i);
        }
        var existing = new HashSet<string>(Orders.Select(v => v.OrderId));
        foreach (var order in source)
        {
            if (!existing.Contains(order.Id))
                Orders.Add(new OrderViewModel(order));
        }
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
}
