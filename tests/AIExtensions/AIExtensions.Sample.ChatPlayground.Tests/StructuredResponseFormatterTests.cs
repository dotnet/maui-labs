using System.Text.Json.Nodes;

namespace AIExtensions.Sample.ChatPlayground.Tests;

public sealed class StructuredResponseFormatterTests
{
    [Theory]
    [InlineData("""{"status":"ok","extra":{"values":[1,true,null]}}""")]
    [InlineData("""[{"kind":"custom"},42]""")]
    [InlineData("42")]
    [InlineData("null")]
    public void TryFormat_IndentsAnyCompleteJsonValue(string json)
    {
        var formatted = StructuredResponseFormatter.TryFormat(json);

        Assert.NotNull(formatted);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(formatted)));
        if (json[0] is '{' or '[')
            Assert.Contains('\n', formatted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("""{"summary":"Do""")]
    [InlineData("""{"summary":""")]
    [InlineData("not json")]
    public void TryFormat_ReturnsNullWhileTheJsonIsIncomplete(string? text) =>
        Assert.Null(StructuredResponseFormatter.TryFormat(text));

    [Fact]
    public void TryFormat_KeepsTextReadable()
    {
        var formatted = StructuredResponseFormatter.TryFormat("""{"summary":"Café <ok> & \"quoted\""}""");

        Assert.Contains("Café <ok> & \\\"quoted\\\"", formatted);
    }

    [Theory]
    [InlineData("{", true)]
    [InlineData("  \n{\"summary\"", true)]
    [InlineData("""{"summary":"Done"}""", true)]
    [InlineData("[1]", false)]
    [InlineData("42", false)]
    [InlineData("The answer is {42}", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void LooksLikeJsonObject_RecognizesAnObjectFromItsFirstCharacter(string? text, bool expected) =>
        Assert.Equal(expected, StructuredResponseFormatter.LooksLikeJsonObject(text));

    [Fact]
    public void TryReadResponse_ReadsTheSchemaAndDropsBlankKeyPoints()
    {
        const string json = """
            {"summary":"Done","keyPoints":["one","  ",""],"category":"Technical","sentiment":"positive"}
            """;

        Assert.True(StructuredResponseFormatter.TryReadResponse(json, out var response));
        Assert.Equal("Done", response.Summary);
        Assert.Equal(["one"], response.KeyPoints);
        Assert.Equal("Technical", response.Category);
        Assert.Equal(PlaygroundSentiment.Positive, response.Sentiment);
    }

    [Theory]
    [InlineData("""{"answer":4}""")]
    [InlineData("""{"summary":"  "}""")]
    [InlineData("""{"summary":"Done","sentiment":"elated"}""")]
    [InlineData("""{"summary":"Do""")]
    [InlineData("""[{"summary":"Done"}]""")]
    [InlineData("Done")]
    public void TryReadResponse_RejectsOtherJsonSoItIsShownAsJson(string text)
    {
        Assert.False(StructuredResponseFormatter.TryReadResponse(text, out var response));
        Assert.Null(response);
    }
}
