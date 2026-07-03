using System.ComponentModel;

namespace GenerativeUI.Sample.Garden.Shared;

/// <summary>
/// The typed REST contract for the Garden sample. These records are the request/response DTOs the
/// minimal-API server exposes and the shapes the OpenAPI document describes. Descriptions are
/// authored deliberately and must survive OpenAPI reduction verbatim (never clipped), because they
/// are what tell an AI agent how to use the API correctly.
/// </summary>
[Description("A product in the garden shop catalog — seeds, tools, and supplies a gardener can browse, add to a cart, and order.")]
public record Product(
    [property: Description("Stable, URL-safe identifier used across the catalog, cart, and orders (for example \"basil-seeds\").")]
    string Sku,
    [property: Description("Display name shown in chat and on product cards (for example \"Basil Seeds\").")]
    string Name,
    [property: Description("Longer marketing description of the product. May span multiple sentences and must never be truncated when shown to the user.")]
    string Description,
    [property: Description("Unit price in US dollars.")]
    decimal Price,
    [property: Description("Catalog category such as \"seeds\", \"tools\", or \"soil\".")]
    string Category,
    [property: Description("Emoji used as a lightweight visual when no image is available (for example \"\ud83c\udf3f\").")]
    string Emoji,
    [property: Description("Absolute URL of the product image; rendered by the app's watermarking ProductImage control when present.")]
    string? ImageUrl = null,
    [property: Description("Current stock count; null when stock is not tracked for this product.")]
    int? Quantity = null);

[Description("Payload to create a new product in the catalog.")]
public record CreateProductRequest(
    [property: Description("Display name for the new product.")]
    string Name,
    [property: Description("Longer marketing description of the product.")]
    string Description,
    [property: Description("Unit price in US dollars.")]
    decimal Price,
    [property: Description("Category such as \"seeds\", \"tools\", or \"soil\".")]
    string Category,
    [property: Description("Optional emoji visual for the product.")]
    string? Emoji = null,
    [property: Description("Optional absolute image URL for the product.")]
    string? ImageUrl = null,
    [property: Description("Optional initial stock count.")]
    int? Quantity = null);

[Description("Payload to update an existing product. Only the supplied fields are changed (partial update).")]
public record UpdateProductRequest(
    [property: Description("New display name, or null to leave unchanged.")]
    string? Name = null,
    [property: Description("New description, or null to leave unchanged.")]
    string? Description = null,
    [property: Description("New unit price in US dollars, or null to leave unchanged.")]
    decimal? Price = null,
    [property: Description("New category, or null to leave unchanged.")]
    string? Category = null,
    [property: Description("New emoji visual, or null to leave unchanged.")]
    string? Emoji = null,
    [property: Description("New absolute image URL, or null to leave unchanged.")]
    string? ImageUrl = null,
    [property: Description("New stock count, or null to leave unchanged.")]
    int? Quantity = null);

[Description("The shopper's current cart: a list of line items plus a computed total.")]
public record Cart(
    [property: Description("Line items currently in the cart.")]
    IReadOnlyList<CartItem> Items,
    [property: Description("Sum of all line-item subtotals, in US dollars.")]
    decimal Total);

[Description("A single line in the cart: a product and the quantity ordered.")]
public record CartItem(
    [property: Description("SKU of the product on this line.")]
    string Sku,
    [property: Description("Display name of the product, denormalized for convenient rendering.")]
    string Name,
    [property: Description("Unit price at the time the item was added, in US dollars.")]
    decimal UnitPrice,
    [property: Description("Number of units ordered; always one or more.")]
    int Quantity,
    [property: Description("UnitPrice multiplied by Quantity, in US dollars.")]
    decimal Subtotal);

[Description("Payload to add a product to the cart, or increment its quantity if already present.")]
public record AddToCartRequest(
    [property: Description("SKU of the product to add.")]
    string Sku,
    [property: Description("Quantity to add; defaults to 1 when omitted.")]
    int Quantity = 1);

[Description("Payload to set the absolute quantity of an existing cart line.")]
public record UpdateCartItemRequest(
    [property: Description("New absolute quantity for the line; use 0 to remove it.")]
    int Quantity);

[Description("A placed order: the items purchased, a total, and when it was created.")]
public record Order(
    [property: Description("Unique order identifier.")]
    string Id,
    [property: Description("Items that were purchased in this order.")]
    IReadOnlyList<CartItem> Items,
    [property: Description("Order total in US dollars.")]
    decimal Total,
    [property: Description("UTC timestamp when the order was placed.")]
    DateTimeOffset PlacedAt);

[Description("A customer review of a product.")]
public record Review(
    [property: Description("Unique review identifier.")]
    string Id,
    [property: Description("SKU of the product being reviewed.")]
    string Sku,
    [property: Description("Star rating from 1 to 5.")]
    int Rating,
    [property: Description("Optional free-text review body.")]
    string? Comment,
    [property: Description("UTC timestamp when the review was submitted.")]
    DateTimeOffset CreatedAt);

[Description("Payload to submit a new product review.")]
public record CreateReviewRequest(
    [property: Description("SKU of the product being reviewed.")]
    string Sku,
    [property: Description("Star rating from 1 to 5.")]
    int Rating,
    [property: Description("Optional free-text review body.")]
    string? Comment = null);

[Description("A curated starter bundle recommending several products to a new gardener.")]
public record Recommendation(
    [property: Description("Human-readable title of the bundle.")]
    string Title,
    [property: Description("Why this bundle is recommended.")]
    string Reason,
    [property: Description("Products included in the bundle.")]
    IReadOnlyList<Product> Products);
