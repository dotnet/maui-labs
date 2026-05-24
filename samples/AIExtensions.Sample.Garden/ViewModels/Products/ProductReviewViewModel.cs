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
    private readonly ReviewStore _reviewStore;

    public ProductReviewViewModel(ReviewStore reviewStore)
    {
        _reviewStore = reviewStore;
    }

    [ObservableProperty]
    public partial string? Sku { get; set; }

    [ObservableProperty]
    public partial string ProductName { get; set; } = "";

    [ObservableProperty]
    public partial int Rating { get; set; } = 5;

    [ObservableProperty]
    public partial string? Comment { get; set; }

    partial void OnSkuChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var product = ProductCatalog.FindByName(value);
        if (product is not null)
            ProductName = product.Name;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (string.IsNullOrWhiteSpace(Sku))
            return;

        _reviewStore.Submit(Sku, Rating, Comment);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
