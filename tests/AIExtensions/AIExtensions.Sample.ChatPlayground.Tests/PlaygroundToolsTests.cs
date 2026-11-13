using System.Text.Json;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Tests;

public sealed class PlaygroundToolsTests
{
    [Fact]
    public async Task ConnectionStatus_InvokesProviderAndReturnsJsonObject()
    {
        var calls = 0;
        var tools = new PlaygroundTools(() =>
        {
            calls++;
            return new ConnectionStatusResult("Internet", ["WiFi"]);
        });
        var function = Assert.IsAssignableFrom<AIFunction>(tools.ConnectionStatus);

        Assert.Equal("get_connection_status", function.Name);
        var schema = function.ReturnJsonSchema;
        Assert.NotNull(schema);
        Assert.Equal("object", schema.Value.GetProperty("type").GetString());
        var properties = schema.Value.GetProperty("properties");
        Assert.True(properties.TryGetProperty("networkAccess", out _));
        Assert.True(properties.TryGetProperty("connectionProfiles", out _));

        var result = await function.InvokeAsync(new AIFunctionArguments());
        var json = JsonSerializer.SerializeToElement(result, AIJsonUtilities.DefaultOptions);

        Assert.Equal(1, calls);
        Assert.Equal(JsonValueKind.Object, json.ValueKind);
        Assert.Equal("Internet", json.GetProperty("networkAccess").GetString());
        Assert.Equal("WiFi", Assert.Single(json.GetProperty("connectionProfiles").EnumerateArray()).GetString());
    }

    // A model that is free to write any text sends what the prompt shows, such as "×", and the call fails. Apple
    // Intelligence ends its whole response when a tool fails, so the schema lists the operations it may choose from.
    [Fact]
    public void Calculator_SchemaListsTheFourOperations()
    {
        var tools = new PlaygroundTools(() => new ConnectionStatusResult("Internet", []));
        var function = Assert.IsAssignableFrom<AIFunction>(tools.Calculator);

        var operation = function.JsonSchema.GetProperty("properties").GetProperty("operation");

        Assert.Equal("string", operation.GetProperty("type").GetString());
        Assert.Equal(
            ["add", "subtract", "multiply", "divide"],
            operation.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }
}
