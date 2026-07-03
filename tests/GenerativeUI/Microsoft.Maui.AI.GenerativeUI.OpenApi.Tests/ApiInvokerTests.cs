using System.Net;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.AI.GenerativeUI.OpenApi.Tests;

/// <summary>
/// Verifies that <see cref="ApiInvoker"/> assembles requests from a <see cref="ReducedSpec"/> by
/// operationId: path/query routing, body serialization, method gating, and the SSRF allowlist.
/// </summary>
public sealed class ApiInvokerTests
{
    private static readonly ReducedSpec Spec = OpenApiReducer.Reduce(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "garden.openapi.json")));

    private static ApiInvoker Invoker(HttpClient? httpClient = null) =>
        new(new GenerativeOpenApiOptions { BaseAddress = new Uri("https://api.garden.example") }, httpClient);

    [Fact]
    public void Builds_get_with_path_param()
    {
        using var request = Invoker().BuildRequest(Spec, "getProduct", new JsonObject { ["sku"] = "basil-seeds" });

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.garden.example/products/basil-seeds", request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public void Escapes_path_param_values()
    {
        using var request = Invoker().BuildRequest(Spec, "getProduct", new JsonObject { ["sku"] = "a b" });

        Assert.Equal("https://api.garden.example/products/a%20b", request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public void Builds_query_string_from_flat_args()
    {
        using var request = Invoker().BuildRequest(Spec, "listProducts",
            new JsonObject { ["category"] = "seeds", ["search"] = "basil" });

        Assert.Equal("https://api.garden.example/products?category=seeds&search=basil", request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Builds_post_with_body()
    {
        using var request = Invoker().BuildRequest(Spec, "createProduct",
            new JsonObject { ["body"] = new JsonObject { ["name"] = "Pears", ["price"] = 3.49 } });

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.NotNull(request.Content);
        var json = await request.Content!.ReadAsStringAsync();
        Assert.Equal("""{"name":"Pears","price":3.49}""", json);
    }

    [Fact]
    public void Routes_path_and_body_without_collision()
    {
        using var request = Invoker().BuildRequest(Spec, "updateCartItem",
            new JsonObject { ["sku"] = "tomato-seeds", ["body"] = new JsonObject { ["quantity"] = 5 } });

        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://api.garden.example/cart/items/tomato-seeds", request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public void Method_comes_from_the_operation_not_the_caller()
    {
        using var request = Invoker().BuildRequest(Spec, "deleteProduct", new JsonObject { ["sku"] = "x" });
        Assert.Equal(HttpMethod.Delete, request.Method);
    }

    [Fact]
    public void Missing_required_path_arg_throws()
        => Assert.Throws<InvalidOperationException>(() => Invoker().BuildRequest(Spec, "getProduct", new JsonObject()));

    [Fact]
    public void Unknown_operation_throws()
        => Assert.Throws<InvalidOperationException>(() => Invoker().BuildRequest(Spec, "nope"));

    [Fact]
    public void Foreign_host_is_rejected()
    {
        var invoker = new ApiInvoker(new GenerativeOpenApiOptions
        {
            BaseAddress = new Uri("https://api.garden.example"),
            AllowedHosts = ["other.example"],
        });

        Assert.Throws<InvalidOperationException>(() => invoker.BuildRequest(Spec, "getCart"));
    }

    [Fact]
    public async Task SendAsync_dispatches_through_the_http_client()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        var invoker = new ApiInvoker(new GenerativeOpenApiOptions { BaseAddress = new Uri("https://api.garden.example") }, httpClient);

        using var response = await invoker.SendAsync(Spec, "getCart");

        Assert.Equal("https://api.garden.example/cart", handler.LastRequestUri!.AbsoluteUri);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
