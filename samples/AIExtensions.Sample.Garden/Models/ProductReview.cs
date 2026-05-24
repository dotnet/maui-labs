namespace AIExtensions.Sample.Garden.Models;

/// <summary>
/// A user review on a product.
/// </summary>
public record ProductReview(
    string ProductSku,
    int Rating,
    string? Comment,
    DateTime CreatedAt);
