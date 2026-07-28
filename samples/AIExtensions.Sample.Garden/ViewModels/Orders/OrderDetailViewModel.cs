using System.Collections.ObjectModel;
using AIExtensions.Sample.Garden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// View model for the order detail page.
/// Accepts an orderId query parameter.
/// </summary>
[QueryProperty(nameof(OrderId), "orderId")]
public sealed partial class OrderDetailViewModel : ObservableObject
{
    private readonly IOrderArchive _archive;
    private readonly CurrentCart _currentCart;

    public OrderDetailViewModel(IOrderArchive archive, CurrentCart currentCart)
    {
        _archive = archive;
        _currentCart = currentCart;
    }

    [ObservableProperty]
    public partial string? OrderId { get; set; }

    [ObservableProperty]
    public partial string PlacedAt { get; set; } = "";

    [ObservableProperty]
    public partial string Total { get; set; } = "";

    [ObservableProperty]
    public partial int ItemCount { get; set; }

    public ObservableCollection<OrderLineViewModel> Lines { get; } = [];

    partial void OnOrderIdChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var order = _archive.FindOrder(value);
        if (order is null)
            return;

        PlacedAt = order.PlacedAt.ToString("MMM d, yyyy  h:mm tt");
        Total = order.Total.ToString("C");
        ItemCount = order.Items.Count;

        Lines.Clear();
        foreach (var item in order.Items)
            Lines.Add(new OrderLineViewModel(item));
    }

    [RelayCommand]
    private void Reorder()
    {
        if (!string.IsNullOrWhiteSpace(OrderId))
            _archive.Reorder(OrderId, _currentCart);
    }
}
