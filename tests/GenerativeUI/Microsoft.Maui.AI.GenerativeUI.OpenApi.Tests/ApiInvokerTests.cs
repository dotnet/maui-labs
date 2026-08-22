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
    public async Task InvokeAsync_read_returns_success_envelope()
    {
        var (invoker, handler) = InvokerWith(_ => Json(HttpStatusCode.OK, """{"items":[],"total":0}"""));

        var result = await invoker.InvokeAsync(Spec, "getCart", access: ApiAccess.Read);

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("https://api.garden.example/cart", handler.LastRequest!.RequestUri!.AbsoluteUri);
        Assert.Equal("""{"status":200,"data":{"items":[],"total":0}}""", result.ToResponseJson());
    }

    [Fact]
    public async Task InvokeAsync_write_with_no_content_returns_status_only_envelope()
    {
        var (invoker, _) = InvokerWith(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var result = await invoker.InvokeAsync(Spec, "clearCart", access: ApiAccess.Write);

        Assert.True(result.IsSuccess);
        Assert.Equal(204, result.StatusCode);
        Assert.Null(result.Body);
        Assert.Equal("""{"status":204}""", result.ToResponseJson());
    }

    [Fact]
    public async Task InvokeAsync_non_success_parses_problem_details()
    {
        var problem = """{"title":"Not Found","status":404,"detail":"No product 'nope'."}""";
        var (invoker, _) = InvokerWith(_ => Json(HttpStatusCode.NotFound, problem));

        var result = await invoker.InvokeAsync(Spec, "getProduct", new JsonObject { ["sku"] = "nope" }, ApiAccess.Read);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("Not Found", result.Error!.Title);
        Assert.Equal("No product 'nope'.", result.Error!.Detail);
        Assert.Equal("""{"status":404,"error":{"title":"Not Found","detail":"No product 'nope'."}}""", result.ToResponseJson());
    }

    [Fact]
    public async Task InvokeAsync_caps_a_large_response_body()
    {
        var big = new string('x', 5000);
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, $$"""{"note":"{{big}}"}"""));
        using var httpClient = new HttpClient(handler);
        var invoker = new ApiInvoker(
            new GenerativeOpenApiOptions { BaseAddress = new Uri("https://api.garden.example"), MaxResponseBytes = 64 },
            httpClient);

        var result = await invoker.InvokeAsync(Spec, "getCart", access: ApiAccess.Read);

        Assert.True(result.IsSuccess);
        Assert.True(result.Truncated);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(result.Body!) <= 64);
    }

    [Fact]
    public async Task InvokeAsync_read_on_a_write_operation_is_rejected()
    {
        var (invoker, handler) = InvokerWith(_ => Json(HttpStatusCode.OK, "{}"));

        var result = await invoker.InvokeAsync(Spec, "deleteProduct", new JsonObject { ["sku"] = "x" }, ApiAccess.Read);

        Assert.False(result.IsSuccess);
        Assert.Equal("wrong_tool", result.Error!.Title);
        Assert.Null(handler.LastRequest); // never dispatched
    }

    [Fact]
    public async Task InvokeAsync_write_on_a_read_operation_is_rejected()
    {
        var (invoker, handler) = InvokerWith(_ => Json(HttpStatusCode.OK, "{}"));

        var result = await invoker.InvokeAsync(Spec, "getProduct", new JsonObject { ["sku"] = "x" }, ApiAccess.Write);

        Assert.False(result.IsSuccess);
        Assert.Equal("wrong_tool", result.Error!.Title);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task InvokeAsync_unknown_operation_returns_error()
    {
        var (invoker, _) = InvokerWith(_ => Json(HttpStatusCode.OK, "{}"));

        var result = await invoker.InvokeAsync(Spec, "nope", access: ApiAccess.Read);

        Assert.False(result.IsSuccess);
        Assert.Equal("no_such_operation", result.Error!.Title);
    }

    [Fact]
    public async Task InvokeAsync_missing_required_arg_returns_error()
    {
        var (invoker, handler) = InvokerWith(_ => Json(HttpStatusCode.OK, "{}"));

        var result = await invoker.InvokeAsync(Spec, "getProduct", new JsonObject(), ApiAccess.Read);

        Assert.False(result.IsSuccess);
        Assert.Equal("bad_request", result.Error!.Title);
        Assert.Null(handler.LastRequest);
    }

    private static (ApiInvoker Invoker, StubHandler Handler) InvokerWith(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var httpClient = new HttpClient(handler);
        var invoker = new ApiInvoker(new GenerativeOpenApiOptions { BaseAddress = new Uri("https://api.garden.example") }, httpClient);
        return (invoker, handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(responder(request));
        }
    }
}
