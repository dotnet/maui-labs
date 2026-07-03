using System.Text.Json.Serialization;

namespace GenerativeUI.Sample.Garden.Shared;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for the Garden DTOs. The server wires this
/// into its JSON options, and the Generative UI library's generic invoker uses the same context so
/// (de)serialization stays typed and AOT-friendly without the library ever referencing these models.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Product))]
[JsonSerializable(typeof(IReadOnlyList<Product>))]
[JsonSerializable(typeof(CreateProductRequest))]
[JsonSerializable(typeof(UpdateProductRequest))]
[JsonSerializable(typeof(Cart))]
[JsonSerializable(typeof(CartItem))]
[JsonSerializable(typeof(AddToCartRequest))]
[JsonSerializable(typeof(UpdateCartItemRequest))]
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(IReadOnlyList<Order>))]
[JsonSerializable(typeof(Review))]
[JsonSerializable(typeof(IReadOnlyList<Review>))]
[JsonSerializable(typeof(CreateReviewRequest))]
[JsonSerializable(typeof(Recommendation))]
public partial class GardenJsonContext : JsonSerializerContext;
