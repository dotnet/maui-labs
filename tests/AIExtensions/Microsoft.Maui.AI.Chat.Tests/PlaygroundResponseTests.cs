using System.Text.Json;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class PlaygroundResponseTests
{
    [Fact]
    public void JsonSchema_LeavesFieldsOptionalAndConstrainsOnlySentiment()
    {
        var format = Assert.IsType<ChatResponseFormatJson>(
            ChatResponseFormat.ForJsonSchema<PlaygroundResponse>(PlaygroundJsonContext.Default.Options));
        var schema = Assert.IsType<JsonElement>(format.Schema);
        var properties = schema.GetProperty("properties");

        Assert.Equal(new[] { "summary", "keyPoints", "category", "sentiment" },
            properties.EnumerateObject().Select(property => property.Name));
        Assert.False(schema.TryGetProperty("required", out _));
        foreach (var name in new[] { "summary", "keyPoints", "category", "sentiment" })
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty(name).GetProperty("description").GetString()));

        Assert.Equal("string", properties.GetProperty("category").GetProperty("type").GetString());
        Assert.False(properties.GetProperty("category").TryGetProperty("enum", out _));
        Assert.Equal(
            new[] { "Neutral", "Positive", "Negative", "Mixed" },
            properties.GetProperty("sentiment").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void JsonResponse_UsesStringEnumValues()
    {
        var response = new PlaygroundResponse
        {
            Summary = "A short story.",
            KeyPoints = ["A beginning", "An ending"],
            Category = "Creative",
            Sentiment = PlaygroundSentiment.Positive,
        };

        var json = JsonSerializer.Serialize(response, PlaygroundJsonContext.Default.PlaygroundResponse);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Creative", document.RootElement.GetProperty("category").GetString());
        Assert.Equal("Positive", document.RootElement.GetProperty("sentiment").GetString());
    }

    [Fact]
    public void JsonResponse_MissingFields_UsesDefaults()
    {
        const string json = """{"summary":"Done"}""";

        var response = JsonSerializer.Deserialize(json, PlaygroundJsonContext.Default.PlaygroundResponse);

        Assert.NotNull(response);
        Assert.Equal("Done", response.Summary);
        Assert.Empty(response.KeyPoints);
        Assert.Equal(string.Empty, response.Category);
        Assert.Equal(PlaygroundSentiment.Neutral, response.Sentiment);
    }
}
