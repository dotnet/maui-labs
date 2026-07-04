using System.Text.Json.Nodes;
using GenerativeUI.Sample.Garden.Server;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Microsoft.Maui.AI.GenerativeUI.OpenApi.Tests;

/// <summary>
/// End-to-end round trip against the sample Garden server hosted in-memory via
/// <see cref="WebApplicationFactory{TEntryPoint}"/>: fetch the live OpenAPI document, reduce it, then
/// drive <see cref="OpenApiExplorerTools"/> over the factory's HttpClient. Validates the whole chain —
/// fetch → reduce → tool → invoke → live server → normalized response. Each test uses a fresh factory
/// so the in-memory store is isolated.
/// </summary>
public sealed class RoundTripTests
{
    private static async Task<(WebApplicationFactory<Program> Factory, OpenApiExplorerTools Tools)> CreateAsync()
    {
        var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var liveDocument = await client.GetStringAsync("/openapi/v1.json");
        var spec = OpenApiReducer.Reduce(liveDocument);
        var invoker = new ApiInvoker(new GenerativeOpenApiOptions { BaseAddress = client.BaseAddress! }, client);
        return (factory, new OpenApiExplorerTools(spec, invoker));
    }

    [Fact]
    public async Task List_endpoints_filters_by_query()
    {
        var (factory, tools) = await CreateAsync();
        using (factory)
        {
            var array = JsonNode.Parse(tools.ListEndpoints(query: "/cart"))!.AsArray();
            var ids = array.Select(n => n!["operationId"]!.GetValue<string>()).OrderBy(x => x, StringComparer.Ordinal).ToArray();

            Assert.Equal(new[] { "addCartItem", "clearCart", "getCart", "removeCartItem", "updateCartItem" }, ids);
        }
    }

    [Fact]
    public async Task Read_api_lists_the_seeded_products()
    {
        var (factory, tools) = await CreateAsync();
        using (factory)
        {
            var envelope = JsonNode.Parse(await tools.ReadApiAsync("listProducts"))!;
            Assert.Equal(200, envelope["status"]!.GetValue<int>());

            var skus = envelope["data"]!.AsArray().Select(p => p!["sku"]!.GetValue<string>()).ToArray();
            Assert.Equal(new[] { "basil-seeds", "potting-soil", "terracotta-pot", "tomato-seeds", "watering-can" }, skus);
        }
    }

    [Fact]
    public async Task Write_then_read_reflects_the_cart_mutation()
    {
        var (factory, tools) = await CreateAsync();
        using (factory)
        {
            var added = JsonNode.Parse(await tools.WriteApiAsync("addCartItem",
                new JsonObject { ["body"] = new JsonObject { ["sku"] = "basil-seeds", ["quantity"] = 2 } }))!;

            Assert.Equal(200, added["status"]!.GetValue<int>());
            var line = Assert.Single(added["data"]!["items"]!.AsArray());
            Assert.Equal("basil-seeds", line!["sku"]!.GetValue<string>());
            Assert.Equal(2, line!["quantity"]!.GetValue<int>());

            var cart = JsonNode.Parse(await tools.ReadApiAsync("getCart"))!;
            Assert.Equal(6.98m, cart["data"]!["total"]!.GetValue<decimal>());
        }
    }

    [Fact]
    public async Task Write_api_creates_a_product_and_returns_201()
    {
        var (factory, tools) = await CreateAsync();
        using (factory)
        {
            var created = JsonNode.Parse(await tools.WriteApiAsync("createProduct",
                new JsonObject
                {
                    ["body"] = new JsonObject
                    {
                        ["name"] = "Pears",
                        ["description"] = "Sweet pears.",
                        ["price"] = 3.49,
                        ["category"] = "seeds",
                    },
                }))!;

            Assert.Equal(201, created["status"]!.GetValue<int>());
            Assert.Equal("pears", created["data"]!["sku"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Read_api_refuses_a_write_operation()
    {
        var (factory, tools) = await CreateAsync();
        using (factory)
        {
            var result = JsonNode.Parse(await tools.ReadApiAsync("deleteProduct", new JsonObject { ["sku"] = "basil-seeds" }))!;

            Assert.Equal("wrong_tool", result["error"]!["title"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Read_api_surfaces_a_server_404_as_a_structured_error()
    {
        var (factory, tools) = await CreateAsync();
        using (factory)
        {
            var result = JsonNode.Parse(await tools.ReadApiAsync("getProduct", new JsonObject { ["sku"] = "does-not-exist" }))!;

            Assert.Equal(404, result["status"]!.GetValue<int>());
            Assert.NotNull(result["error"]);
        }
    }

    [Fact]
    public async Task Describe_endpoint_inlines_the_response_schema_one_level()
    {
        var (factory, tools) = await CreateAsync();
        using (factory)
        {
            var detail = JsonNode.Parse(tools.DescribeEndpoint("getProduct"))!;

            Assert.Null(detail["requestSchema"]); // GET has no body
            var props = detail["responseSchema"]!["properties"]!.AsArray()
                .Select(p => p!["name"]!.GetValue<string>()).ToArray();
            Assert.Equal(new[] { "sku", "name", "description", "price", "category", "emoji", "imageUrl", "quantity" }, props);
        }
    }
}
