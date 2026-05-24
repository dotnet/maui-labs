using System.ComponentModel;
using AIExtensions.Sample.Garden.Models;
using Microsoft.Maui.AI.Attributes;

namespace AIExtensions.Sample.Garden.Services;

/// <summary>
/// In-memory review store. Registered as a singleton in DI.
/// </summary>
public sealed class ReviewStore
{
    private readonly List<ProductReview> _reviews = [];

    [ExportAIFunction("list_reviews")]
    [Description("Lists all product reviews, newest first.")]
    public IReadOnlyList<ProductReview> Reviews => [.. _reviews.OrderByDescending(r => r.CreatedAt)];

    [ExportAIFunction("get_product_reviews")]
    [Description("Gets reviews for a specific product by sku.")]
    public IReadOnlyList<ProductReview> GetProductReviews(
        [Description("The product sku to get reviews for.")] string sku)
        => [.. _reviews.Where(r => string.Equals(r.ProductSku, sku, StringComparison.OrdinalIgnoreCase))
                       .OrderByDescending(r => r.CreatedAt)];

    [ExportAIFunction("submit_review")]
    [Description("Submit a product review with a rating (1-5) and optional comment.")]
    public ProductReview Submit(
        [Description("The product sku to review.")] string sku,
        [Description("Rating from 1 (worst) to 5 (best).")] int rating,
        [Description("Optional review comment.")] string? comment = null)
    {
        if (rating < 1 || rating > 5)
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 1 and 5.");

        var review = new ProductReview(sku, rating, comment, DateTime.UtcNow);
        _reviews.Add(review);
        return review;
    }

    public double? AverageRating(string sku)
    {
        var productReviews = _reviews.Where(r => string.Equals(r.ProductSku, sku, StringComparison.OrdinalIgnoreCase)).ToList();
        return productReviews.Count == 0 ? null : productReviews.Average(r => r.Rating);
    }
}
