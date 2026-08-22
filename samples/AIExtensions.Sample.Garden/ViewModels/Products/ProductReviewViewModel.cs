using AIExtensions.Sample.Garden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// View model for the product review modal.
/// Accepts a sku query parameter.
/// </summary>
[QueryProperty(nameof(Sku), "sku")]
public sealed partial class ProductReviewViewModel : ObservableObject
{
    private readonly GardenDataStore _store;

    public ProductReviewViewModel(GardenDataStore store)
    {
        _store = store;
    }

    public GardenDataStore Store => _store;

    [ObservableProperty]
    public partial string? Sku { get; set; }

    [ObservableProperty]
    public partial string ProductName { get; set; } = "";

    [ObservableProperty]
    public partial int Rating { get; set; } = 5;

    [ObservableProperty]
    public partial string? Comment { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    public async Task LoadAsync(string sku, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return;

        Sku = sku;
        try
        {
            var product = await _store.GetProductAsync(sku, cancellationToken);
            ProductName = product.Name;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (string.IsNullOrWhiteSpace(Sku))
            return;

        try
        {
            await _store.SubmitReviewAsync(Sku, Rating, Comment);
            ErrorMessage = null;
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
