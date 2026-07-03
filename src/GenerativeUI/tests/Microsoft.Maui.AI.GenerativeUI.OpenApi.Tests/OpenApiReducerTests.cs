namespace Microsoft.Maui.AI.GenerativeUI.OpenApi.Tests;

/// <summary>
/// Exercises <see cref="OpenApiReducer"/> against the checked-in Garden OpenAPI snapshot (kept current
/// by the server-side snapshot test). Verifies endpoint/model projection, verbatim description
/// preservation, and type/nullability mapping.
/// </summary>
public sealed class OpenApiReducerTests
{
    private static readonly ReducedSpec Spec = OpenApiReducer.Reduce(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "garden.openapi.json")));

    private static ReducedEndpoint Endpoint(string operationId) =>
        Spec.Endpoints.Single(e => e.OperationId == operationId);

    private static ReducedProperty Property(string model, string name) =>
        Spec.Models[model].Properties.Single(p => p.Name == name);

    [Fact]
    public void Reduces_all_operations() => Assert.Equal(19, Spec.Endpoints.Count);

    [Fact]
    public void Reduces_all_models() => Assert.Equal(11, Spec.Models.Count);

    [Fact]
    public void GetProduct_projects_method_path_param_and_response_model()
    {
        var endpoint = Endpoint("getProduct");

        Assert.Equal("GET", endpoint.Method);
        Assert.Equal("/products/{sku}", endpoint.Path);
        Assert.Equal("Product", endpoint.ResponseModel);
        Assert.Null(endpoint.RequestModel);
        Assert.False(string.IsNullOrEmpty(endpoint.Summary));
        Assert.Contains("Products", endpoint.Tags!);

        var parameter = Assert.Single(endpoint.Parameters!);
        Assert.Equal("sku", parameter.Name);
        Assert.Equal("path", parameter.In);
        Assert.True(parameter.Required);
        Assert.False(string.IsNullOrEmpty(parameter.Description));
    }

    [Fact]
    public void ListProducts_projects_array_response_and_optional_query_params()
    {
        var endpoint = Endpoint("listProducts");

        Assert.Equal("Product[]", endpoint.ResponseModel);
        Assert.Equal(2, endpoint.Parameters!.Count);
        Assert.All(endpoint.Parameters!, p => Assert.Equal("query", p.In));
        Assert.All(endpoint.Parameters!, p => Assert.False(p.Required));
    }

    [Fact]
    public void CreateProduct_projects_request_and_response_models()
    {
        var endpoint = Endpoint("createProduct");

        Assert.Equal("POST", endpoint.Method);
        Assert.Equal("CreateProductRequest", endpoint.RequestModel);
        Assert.Equal("Product", endpoint.ResponseModel);
    }

    [Fact]
    public void DeleteProduct_has_no_response_model()
    {
        var endpoint = Endpoint("deleteProduct");

        Assert.Equal("DELETE", endpoint.Method);
        Assert.Null(endpoint.ResponseModel);
    }

    [Fact]
    public void Preserves_model_description_verbatim()
    {
        Assert.Equal(
            "A product in the garden shop catalog — seeds, tools, and supplies a gardener can browse, add to a cart, and order.",
            Spec.Models["Product"].Description);
    }

    [Fact]
    public void Preserves_long_property_description_without_clipping()
    {
        Assert.Equal(
            "Longer marketing description of the product. May span multiple sentences and must never be truncated when shown to the user.",
            Property("Product", "description").Description);
    }

    [Fact]
    public void Maps_number_type_and_format()
    {
        var price = Property("Product", "price");

        Assert.Equal("number", price.Type);
        Assert.Equal("double", price.Format);
        Assert.True(price.Required);
        Assert.False(price.Nullable);
    }

    [Fact]
    public void Maps_nullable_optional_scalars()
    {
        var quantity = Property("Product", "quantity");
        Assert.Equal("integer", quantity.Type);
        Assert.True(quantity.Nullable);
        Assert.False(quantity.Required);

        var imageUrl = Property("Product", "imageUrl");
        Assert.Equal("string", imageUrl.Type);
        Assert.True(imageUrl.Nullable);
        Assert.False(imageUrl.Required);
    }

    [Fact]
    public void Maps_array_of_model_properties()
    {
        Assert.Equal("CartItem[]", Property("Cart", "items").Type);
        Assert.Equal("Product[]", Property("Recommendation", "products").Type);
    }

    [Fact]
    public void Authored_operation_ids_survive()
    {
        Assert.Contains(Spec.Endpoints, e => e.OperationId == "getRecommendations");
        Assert.Contains(Spec.Endpoints, e => e.OperationId == "updateCartItem");
    }
}
