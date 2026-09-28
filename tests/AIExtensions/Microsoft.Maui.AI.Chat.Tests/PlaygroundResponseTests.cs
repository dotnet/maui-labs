using System.Text.Json;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class PlaygroundResponseTests
{
    [Fact]
    public void JsonSchema_DescribesFieldsAndConstrainsSentiment()
    {
        var format = Assert.IsType<ChatResponseFormatJson>(
            ChatResponseFormat.ForJsonSchema<PlaygroundResponse>(PlaygroundJsonContext.Default.Options));
        var schema = Assert.IsType<JsonElement>(format.Schema);
        var properties = schema.GetProperty("properties");

        Assert.Equal(new[] { "summary", "keyPoints", "sentiment" },
            properties.EnumerateObject().Select(property => property.Name));
        foreach (var name in new[] { "summary", "keyPoints", "sentiment" })
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty(name).GetProperty("description").GetString()));

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
            Sentiment = PlaygroundSentiment.Positive,
        };

        var json = JsonSerializer.Serialize(response, PlaygroundJsonContext.Default.PlaygroundResponse);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("category", out _));
        Assert.Equal("Positive", document.RootElement.GetProperty("sentiment").GetString());
    }
}
