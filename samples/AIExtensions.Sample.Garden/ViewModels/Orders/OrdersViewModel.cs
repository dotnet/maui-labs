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
    private readonly IOrderArchive _archive;
    private readonly CurrentCart _currentCart;

    public OrdersViewModel(IOrderArchive archive, CurrentCart currentCart)
    {
        _archive = archive;
        _currentCart = currentCart;

        WeakReferenceMessenger.Default.Register(this);
        Refresh();
    }

    public ObservableCollection<OrderViewModel> Orders { get; } = [];

    void IRecipient<ChatTurnCompletedMessage>.Receive(ChatTurnCompletedMessage message)
        => Refresh();

    [RelayCommand]
    private void Reorder(string? orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return;
        _archive.Reorder(orderId, _currentCart);
    }

    [RelayCommand]
    private void Clear()
    {
        _archive.Clear();
        Refresh();
    }

    public void Refresh()
    {
        var source = _archive.Orders;
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
}
