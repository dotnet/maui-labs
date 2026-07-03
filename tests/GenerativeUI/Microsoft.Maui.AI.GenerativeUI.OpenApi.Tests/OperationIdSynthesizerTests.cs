namespace Microsoft.Maui.AI.GenerativeUI.OpenApi.Tests;

public sealed class OperationIdSynthesizerTests
{
    [Theory]
    [InlineData("GET", "/products", "get_products")]
    [InlineData("GET", "/products/{sku}", "get_products_by_sku")]
    [InlineData("POST", "/cart/items", "post_cart_items")]
    [InlineData("PUT", "/cart/items/{sku}", "put_cart_items_by_sku")]
    [InlineData("POST", "/orders/{id}/reorder", "post_orders_by_id_reorder")]
    [InlineData("DELETE", "/cart", "delete_cart")]
    public void Synthesize_folds_path_params(string method, string path, string expected)
        => Assert.Equal(expected, OperationIdSynthesizer.Synthesize(method, path));

    [Fact]
    public void Resolve_prefers_authored_id()
        => Assert.Equal("getProduct", OperationIdSynthesizer.Resolve("getProduct", "GET", "/products/{sku}"));

    [Fact]
    public void Resolve_synthesizes_when_missing()
        => Assert.Equal("get_products_by_sku", OperationIdSynthesizer.Resolve(null, "GET", "/products/{sku}"));

    [Fact]
    public void Resolve_synthesizes_when_blank()
        => Assert.Equal("get_products", OperationIdSynthesizer.Resolve("   ", "GET", "/products"));
}
