using System.Collections.ObjectModel;
using AIExtensions.Sample.Garden.Shared;
using AIExtensions.Sample.Garden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// View model for the product detail page.
/// Accepts a sku query parameter and loads product info + reviews.
/// </summary>
[QueryProperty(nameof(Sku), "sku")]
public sealed partial class ProductDetailViewModel : ObservableObject
{
    private readonly GardenDataStore _store;

    public ProductDetailViewModel(GardenDataStore store)
    {
        _store = store;
    }

    [ObservableProperty]
    public partial string? Sku { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Emoji { get; set; } = "";

    [ObservableProperty]
    public partial string Category { get; set; } = "";

    [ObservableProperty]
    public partial string PriceLabel { get; set; } = "";

    [ObservableProperty]
    public partial string RatingLabel { get; set; } = "No reviews yet";

    [ObservableProperty]
    public partial bool HasReviews { get; set; }

    public ObservableCollection<ReviewViewModel> Reviews { get; } = [];

    public GardenDataStore Store => _store;

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
            Name = product.Name;
            Emoji = product.Emoji;
            Category = product.Category;
            PriceLabel = product.Price.ToString("C");
            await RefreshReviewsAsync(sku, cancellationToken);
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task RefreshReviewsAsync(string? sku = null, CancellationToken cancellationToken = default)
    {
        sku ??= Sku;
        if (string.IsNullOrWhiteSpace(sku))
            return;

        var reviews = await _store.GetProductReviewsAsync(sku, cancellationToken);
        Reviews.Clear();
        foreach (var r in reviews)
            Reviews.Add(new ReviewViewModel(r));

        HasReviews = reviews.Count > 0;
        double? avg = reviews.Count == 0 ? null : reviews.Average(r => r.Rating);
        RatingLabel = avg is not null
            ? $"{avg:F1} ★  ({reviews.Count} review{(reviews.Count != 1 ? "s" : "")})"
            : "No reviews yet";
    }

    [RelayCommand]
    private async Task AddToCartAsync()
    {
        if (!string.IsNullOrWhiteSpace(Sku))
        {
            try
            {
                await _store.AddToCartAsync(Sku);
                ErrorMessage = null;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
        }
    }

    [RelayCommand]
    private async Task WriteReviewAsync()
    {
        if (!string.IsNullOrWhiteSpace(Sku))
            await Shell.Current.GoToAsync($"review?sku={Sku}");
    }
}

public sealed class ReviewViewModel(Review review)
{
    public string Stars => new string('★', review.Rating) + new string('☆', 5 - review.Rating);
    public string Comment => review.Comment ?? "";
    public bool HasComment => !string.IsNullOrWhiteSpace(review.Comment);
    public string Date => review.CreatedAt.ToString("MMM d, yyyy");
}
