using System.Collections.ObjectModel;
using AIExtensions.Sample.Garden.Models;
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
    private readonly CurrentCart _currentCart;
    private readonly ReviewStore _reviewStore;

    public ProductDetailViewModel(CurrentCart currentCart, ReviewStore reviewStore)
    {
        _currentCart = currentCart;
        _reviewStore = reviewStore;
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

    partial void OnSkuChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var product = ProductCatalog.FindByName(value);
        if (product is null)
            return;

        Name = product.Name;
        Emoji = product.Emoji;
        Category = product.Category;
        PriceLabel = product.Price.ToString("C");
        RefreshReviews(value);
    }

    public void RefreshReviews(string? sku = null)
    {
        sku ??= Sku;
        if (string.IsNullOrWhiteSpace(sku))
            return;

        var reviews = _reviewStore.GetProductReviews(sku);
        Reviews.Clear();
        foreach (var r in reviews)
            Reviews.Add(new ReviewViewModel(r));

        HasReviews = reviews.Count > 0;
        var avg = _reviewStore.AverageRating(sku);
        RatingLabel = avg is not null
            ? $"{avg:F1} ★  ({reviews.Count} review{(reviews.Count != 1 ? "s" : "")})"
            : "No reviews yet";
    }

    [RelayCommand]
    private void AddToCart()
    {
        if (!string.IsNullOrWhiteSpace(Sku))
            _currentCart.AddItem(Sku);
    }

    [RelayCommand]
    private async Task WriteReviewAsync()
    {
        if (!string.IsNullOrWhiteSpace(Sku))
            await Shell.Current.GoToAsync($"review?sku={Sku}");
    }
}

public sealed class ReviewViewModel(ProductReview review)
{
    public string Stars => new string('★', review.Rating) + new string('☆', 5 - review.Rating);
    public string Comment => review.Comment ?? "";
    public bool HasComment => !string.IsNullOrWhiteSpace(review.Comment);
    public string Date => review.CreatedAt.ToString("MMM d, yyyy");
}
