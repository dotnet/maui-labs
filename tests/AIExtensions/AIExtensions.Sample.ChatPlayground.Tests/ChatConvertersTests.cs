using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

/// <summary>The converters that the chat's block templates use to show tool calls and approvals.</summary>
public sealed class ChatConvertersTests
{
    [Fact]
    public void ToolArguments_AreOnePerLineOrTheOneTheParameterNames()
    {
        using var json = JsonDocument.Parse("""{"city":"Paris","days":[1,2]}""");
        var call = new FunctionCallContent("call-1", "lookup", new Dictionary<string, object?>
        {
            ["city"] = json.RootElement.GetProperty("city").Clone(),
            ["days"] = json.RootElement.GetProperty("days").Clone(),
            ["exact"] = true,
        });
        var converter = new ToolArgumentsConverter();

        Assert.Equal("city: Paris\ndays: [1,2]\nexact: True", Convert(converter, call));
        Assert.Equal("Paris", Convert(converter, call, "city"));
        Assert.Null(Convert(converter, call, "missing"));
        Assert.Null(Convert(converter, new FunctionCallContent("call-2", "now")));
        Assert.Null(Convert(converter, null));
    }

    [Fact]
    public void ToolResult_IsWhatTheToolReturnedOrWhyItFailed()
    {
        var converter = new ToolResultConverter();

        Assert.Equal("10:00", Convert(converter, new FunctionResultContent("call-1", JsonSerializer.SerializeToElement("10:00"))));
        Assert.Equal("{\"ok\":true}", Convert(converter, new FunctionResultContent("call-1", JsonSerializer.SerializeToElement(new { ok = true }))));
        Assert.Equal("Tool failed",
            Convert(converter, new FunctionResultContent("call-1", "Error") { Exception = new InvalidOperationException("Tool failed") }));
        Assert.Null(Convert(converter, null));
    }

    [Fact]
    public void ApprovalStatus_SaysHowTheApprovalEnded()
    {
        var converter = new ApprovalStatusConverter();

        Assert.Equal(
            ["Approved", "Rejected", "Not answered"],
            new[] { ApprovalStatus.Approved, ApprovalStatus.Rejected, ApprovalStatus.Pending }.Select(status => Convert(converter, status)));
    }

    [Fact]
    public void TurnError_SaysWhyTheTurnFailedOnOneLine()
    {
        var converter = new TurnErrorConverter();

        Assert.Equal(
            "The response failed: HTTP 400 (invalid_request_error) Unsupported parameter: 'temperature'.",
            Convert(converter, new InvalidOperationException("HTTP 400 (invalid_request_error)\r\n\r\n Unsupported parameter: 'temperature'.")));
        Assert.Null(Convert(converter, null));
    }

    [Fact]
    public void HasValue_IsFalseForNothingToShow()
    {
        var converter = new HasValueConverter();

        Assert.Equal(
            [false, false, true, false, true, true],
            new object?[] { null, "  ", "text", Array.Empty<int>(), new[] { 1 }, 0 }.Select(value => (bool)Convert(converter, value)!));
    }

    [Fact]
    public void BoldText_ShowsEachMarkedPhraseInBoldAndTheRestAsItIs()
    {
        Assert.Equal(
            [("Planning calculations", FontAttributes.Bold), ("\n\nI'll work out each step.", FontAttributes.None)],
            BoldSpans("**Planning calculations**\n\nI'll work out each step."));
        Assert.Equal(
            [("Check ", FontAttributes.None), ("this", FontAttributes.Bold), (" and ", FontAttributes.None), ("that", FontAttributes.Bold)],
            BoldSpans("Check **this** and **that**"));

        // Markers that do not wrap a phrase on one line stay as they are.
        foreach (var text in new[] { "2 ** 3 ** 4", "**unclosed", "**across\nlines**", "****" })
            Assert.Equal([(text, FontAttributes.None)], BoldSpans(text));
        Assert.Equal([("**a\nb** ", FontAttributes.None), ("c", FontAttributes.Bold)], BoldSpans("**a\nb** **c**"));
        Assert.Empty(BoldSpans(string.Empty));
        Assert.Null(Convert(new BoldTextConverter(), null));
    }

    private static (string Text, FontAttributes Attributes)[] BoldSpans(string text) =>
        Assert.IsType<FormattedString>(Convert(new BoldTextConverter(), text))
            .Spans.Select(static span => (span.Text, span.FontAttributes)).ToArray();

    private static object? Convert(IValueConverter converter, object? value, object? parameter = null) =>
        converter.Convert(value, typeof(string), parameter, CultureInfo.InvariantCulture);
}
