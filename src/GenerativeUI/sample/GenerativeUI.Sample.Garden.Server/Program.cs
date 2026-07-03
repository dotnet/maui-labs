using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using GenerativeUI.Sample.Garden.Server;
using GenerativeUI.Sample.Garden.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<GardenStore>();

// Use the shared source-generated JSON context (typed + AOT-friendly), keeping the default
// reflection resolver in the chain for framework types (ProblemDetails, etc.).
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, GardenJsonContext.Default));

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();

// ── Products ──────────────────────────────────────────────────────────────────────────────────
var products = app.MapGroup("/products").WithTags("Products");

products.MapGet("/", (
        GardenStore store,
        [Description("Optional category filter, such as \"seeds\" or \"tools\".")] string? category,
        [Description("Optional free-text filter matched against product name and description.")] string? search) =>
        TypedResults.Ok(store.ListProducts(category, search)))
    .WithName("listProducts")
    .WithSummary("List products.")
    .WithDescription("Returns the full catalog, optionally filtered by category or a free-text search over name and description.");

products.MapGet("/{sku}", Results<Ok<Product>, NotFound> (
        GardenStore store,
        [Description("Stable product identifier (SKU).")] string sku) =>
        store.GetProduct(sku) is { } p ? TypedResults.Ok(p) : TypedResults.NotFound())
    .WithName("getProduct")
    .WithSummary("Get a product by SKU.")
    .WithDescription("Returns the full product record for a single SKU, or 404 if no such product exists.");

products.MapPost("/", (GardenStore store, CreateProductRequest request) =>
    {
        var created = store.CreateProduct(request);
        return TypedResults.Created($"/products/{created.Sku}", created);
    })
    .WithName("createProduct")
    .WithSummary("Create a product.")
    .WithDescription("Adds a new product to the catalog and returns it with its generated SKU.");

products.MapPut("/{sku}", Results<Ok<Product>, NotFound> (
        GardenStore store,
        [Description("SKU of the product to update.")] string sku,
        UpdateProductRequest request) =>
        store.UpdateProduct(sku, request) is { } p ? TypedResults.Ok(p) : TypedResults.NotFound())
    .WithName("updateProduct")
    .WithSummary("Update a product.")
    .WithDescription("Applies a partial update to an existing product; only supplied fields change. Returns the updated product or 404.");

products.MapDelete("/{sku}", Results<NoContent, NotFound> (
        GardenStore store,
        [Description("SKU of the product to delete.")] string sku) =>
        store.DeleteProduct(sku) ? TypedResults.NoContent() : TypedResults.NotFound())
    .WithName("deleteProduct")
    .WithSummary("Delete a product.")
    .WithDescription("Permanently removes a product from the catalog (and from the cart if present).");

products.MapGet("/{sku}/reviews", (
        GardenStore store,
        [Description("SKU whose reviews to return.")] string sku) =>
        TypedResults.Ok(store.GetProductReviews(sku)))
    .WithName("getProductReviews")
    .WithSummary("List a product's reviews.")
    .WithDescription("Returns all reviews for a single product, newest first.");

// ── Cart ──────────────────────────────────────────────────────────────────────────────────────
var cart = app.MapGroup("/cart").WithTags("Cart");

cart.MapGet("/", (GardenStore store) => TypedResults.Ok(store.GetCart()))
    .WithName("getCart")
    .WithSummary("Get the current cart.")
    .WithDescription("Returns the shopper's current cart with its line items and computed total.");

cart.MapPost("/items", (GardenStore store, AddToCartRequest request) => TypedResults.Ok(store.AddToCart(request)))
    .WithName("addCartItem")
    .WithSummary("Add an item to the cart.")
    .WithDescription("Adds a product to the cart, or increments its quantity if already present. Returns the updated cart.");

cart.MapPut("/items/{sku}", (
        GardenStore store,
        [Description("SKU of the cart line to update.")] string sku,
        UpdateCartItemRequest request) =>
        TypedResults.Ok(store.SetCartQuantity(sku, request.Quantity)))
    .WithName("updateCartItem")
    .WithSummary("Set a cart line's quantity.")
    .WithDescription("Sets the absolute quantity of an existing cart line; a quantity of 0 removes it. Returns the updated cart.");

cart.MapDelete("/items/{sku}", (
        GardenStore store,
        [Description("SKU of the cart line to remove.")] string sku) =>
        TypedResults.Ok(store.RemoveFromCart(sku)))
    .WithName("removeCartItem")
    .WithSummary("Remove a cart line.")
    .WithDescription("Removes a single line from the cart and returns the updated cart.");

cart.MapDelete("/", (GardenStore store) =>
    {
        store.ClearCart();
        return TypedResults.NoContent();
    })
    .WithName("clearCart")
    .WithSummary("Clear the cart.")
    .WithDescription("Empties the cart entirely.");

// ── Orders ────────────────────────────────────────────────────────────────────────────────────
var orders = app.MapGroup("/orders").WithTags("Orders");

orders.MapPost("/", (GardenStore store) => TypedResults.Ok(store.Checkout()))
    .WithName("checkout")
    .WithSummary("Check out the current cart.")
    .WithDescription("Places an order from the current cart, clears the cart, and returns the created order.");

orders.MapGet("/", (GardenStore store) => TypedResults.Ok(store.ListOrders()))
    .WithName("listOrders")
    .WithSummary("List past orders.")
    .WithDescription("Returns the order archive, newest first.");

orders.MapGet("/{id}", Results<Ok<Order>, NotFound> (
        GardenStore store,
        [Description("Order identifier.")] string id) =>
        store.GetOrder(id) is { } o ? TypedResults.Ok(o) : TypedResults.NotFound())
    .WithName("getOrder")
    .WithSummary("Get an order by id.")
    .WithDescription("Returns a single placed order, or 404 if the id is unknown.");

orders.MapPost("/{id}/reorder", Results<Ok<Cart>, NotFound> (
        GardenStore store,
        [Description("Id of the order to copy into the cart.")] string id) =>
        store.Reorder(id) is { } c ? TypedResults.Ok(c) : TypedResults.NotFound())
    .WithName("reorder")
    .WithSummary("Reorder a past order.")
    .WithDescription("Copies the items of a past order back into the cart and returns the updated cart.");

orders.MapDelete("/", (GardenStore store) =>
    {
        store.ClearOrders();
        return TypedResults.NoContent();
    })
    .WithName("clearOrders")
    .WithSummary("Clear order history.")
    .WithDescription("Removes all past orders from the archive.");

// ── Reviews ───────────────────────────────────────────────────────────────────────────────────
var reviews = app.MapGroup("/reviews").WithTags("Reviews");

reviews.MapGet("/", (GardenStore store) => TypedResults.Ok(store.ListReviews()))
    .WithName("listReviews")
    .WithSummary("List all reviews.")
    .WithDescription("Returns every product review, newest first.");

reviews.MapPost("/", (GardenStore store, CreateReviewRequest request) =>
    {
        var created = store.CreateReview(request);
        return TypedResults.Created($"/products/{created.Sku}/reviews", created);
    })
    .WithName("createReview")
    .WithSummary("Submit a review.")
    .WithDescription("Adds a 1-to-5 star review (with an optional comment) for a product and returns it.");

// ── Recommendations ───────────────────────────────────────────────────────────────────────────
app.MapGet("/recommendations", (GardenStore store) => TypedResults.Ok(store.GetRecommendations()))
    .WithTags("Recommendations")
    .WithName("getRecommendations")
    .WithSummary("Get a recommended bundle.")
    .WithDescription("Returns a curated starter bundle of products for a new gardener.");

app.Run();

/// <summary>
/// Exposed so the WebApplicationFactory-based tests can boot this server in-memory to snapshot the
/// generated OpenAPI document.
/// </summary>
public partial class Program;
