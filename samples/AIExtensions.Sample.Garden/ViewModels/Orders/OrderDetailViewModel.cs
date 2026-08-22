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
    private readonly GardenDataStore _store;

    public OrderDetailViewModel(GardenDataStore store)
    {
        _store = store;
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

    public GardenDataStore Store => _store;

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    public async Task LoadAsync(string orderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return;

        OrderId = orderId;
        try
        {
            var order = await _store.GetOrderAsync(orderId, cancellationToken);
            PlacedAt = order.PlacedAt.ToString("MMM d, yyyy  h:mm tt");
            Total = order.Total.ToString("C");
            ItemCount = order.Items.Count;
            Lines.Clear();
            foreach (var item in order.Items)
                Lines.Add(new OrderLineViewModel(item));
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ReorderAsync()
    {
        if (!string.IsNullOrWhiteSpace(OrderId))
        {
            try
            {
                await _store.ReorderAsync(OrderId);
                ErrorMessage = null;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
        }
    }
}
