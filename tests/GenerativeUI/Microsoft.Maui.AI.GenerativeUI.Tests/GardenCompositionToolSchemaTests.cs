using System.Text.Json.Nodes;
using GenerativeUI.Sample.Garden.Tools;
using AIExtensions.Sample.Garden.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

public sealed partial class GardenCompositionToolSchemaTests
{
    [AIToolSource(typeof(GardenCompositionTools))]
    private partial class GardenCompositionToolContext : AIToolContext;

    [Fact]
    public void ComposeProductDetailSchema_ContainsTypedOptionalProductFacets()
    {
        var tool = Assert.IsAssignableFrom<AIFunctionDeclaration>(
            GardenCompositionToolContext.Default.Tools.Single(tool =>
                tool.Name == "compose_product_detail"));
        var schema = JsonNode.Parse(tool.JsonSchema.GetRawText())!;
        var properties = schema["properties"]!.AsObject();
        var required = schema["required"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .ToList();

        Assert.NotNull(properties["intent"]);
        Assert.NotNull(properties["product"]);
        Assert.Contains("intent", required);
        Assert.DoesNotContain("product", required);

        var productSchema = properties["product"]!.ToJsonString();
        Assert.Contains("seedDetails", productSchema, StringComparison.Ordinal);
        Assert.Contains("dimensions", productSchema, StringComparison.Ordinal);
        Assert.Contains("colorOptions", productSchema, StringComparison.Ordinal);
    }

    [Fact]
    public void GardenProductServices_RegisterCompositionToolSource()
    {
        var services = new ServiceCollection();

        services.AddGardenProductComponents();

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(GardenCompositionTools));
    }
}
