using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Tests;

public sealed class CalculatorDisplayTests
{
    [Theory]
    [InlineData("add", "+")]
    [InlineData("subtract", "\u2212")]
    [InlineData("multiply", "\u00D7")]
    [InlineData(" Divide ", "\u00F7")]
    [InlineData("*", "\u00D7")]
    [InlineData("modulo", "modulo")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void FormatOperator_UsesArithmeticSymbols(string? operation, string expected) =>
        Assert.Equal(expected, CalculatorDisplay.FormatOperator(operation));

    [Fact]
    public void FormatExpression_ReadsModelArgumentsAsJson()
    {
        var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            """{"left":1234.5,"operation":"multiply","right":"7"}""");

        Assert.Equal("1,234.5 \u00D7 7", CalculatorDisplay.FormatExpression(arguments, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FormatExpression_ReturnsNullUntilEveryArgumentIsKnown()
    {
        Assert.Null(CalculatorDisplay.FormatExpression(null));
        Assert.Null(CalculatorDisplay.FormatExpression(new Dictionary<string, object?> { ["left"] = 1, ["operation"] = "add" }));
    }

    [Theory]
    [InlineData(84d, "84")]
    [InlineData(0.30000000000000004d, "0.3")]
    [InlineData(1d / 3d, "0.3333333333")]
    [InlineData(-2500d, "-2,500")]
    public void FormatResult_HidesBinaryRoundingNoise(double value, string expected)
    {
        Assert.Equal(expected, CalculatorDisplay.FormatResult(new FunctionResultContent("call", value), CultureInfo.InvariantCulture));
        Assert.Equal(expected, CalculatorDisplay.FormatResult(
            new FunctionResultContent("call", JsonSerializer.SerializeToElement(value)), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FormatResult_UsesTheCulture() =>
        Assert.Equal("1.234,5", CalculatorDisplay.FormatResult(
            new FunctionResultContent("call", 1234.5), CultureInfo.GetCultureInfo("de-DE")));

    [Fact]
    public void FailedResults_ShowTheirErrorInsteadOfAResult()
    {
        var thrown = new FunctionResultContent("call", null) { Exception = new InvalidOperationException("Division by zero.") };
        var reported = new FunctionResultContent("call", JsonSerializer.SerializeToElement("Error: Unknown operation."));

        Assert.Null(CalculatorDisplay.FormatResult(thrown));
        Assert.Equal("Division by zero.", CalculatorDisplay.FormatError(thrown));
        Assert.Null(CalculatorDisplay.FormatResult(reported));
        Assert.Equal("Error: Unknown operation.", CalculatorDisplay.FormatError(reported));
        Assert.Null(CalculatorDisplay.FormatError(new FunctionResultContent("call", 4)));
        Assert.Null(CalculatorDisplay.FormatError(null));
        Assert.Null(CalculatorDisplay.FormatResult(null));
    }

    [Fact]
    public async Task CalculatorTool_ResultIsShownAsArithmetic()
    {
        var tools = new PlaygroundTools(() => new ConnectionStatusResult("Internet", []));
        var arguments = new AIFunctionArguments { ["left"] = 12, ["operation"] = "multiply", ["right"] = 7 };

        var value = await Assert.IsAssignableFrom<AIFunction>(tools.Calculator).InvokeAsync(arguments);

        Assert.Equal("12 \u00D7 7", CalculatorDisplay.FormatExpression(arguments, CultureInfo.InvariantCulture));
        Assert.Equal("84", CalculatorDisplay.FormatResult(new FunctionResultContent("call", value), CultureInfo.InvariantCulture));
    }
}

public sealed class TurnFooterConverterTests
{
    [Theory]
    [InlineData("gpt-4.1", 1024L, 128L, null, "gpt-4.1 \u00B7 1,024 in \u00B7 128 out")]
    [InlineData("gpt-4.1", null, null, null, "gpt-4.1")]
    [InlineData(null, 10L, 5L, null, "10 in \u00B7 5 out")]
    [InlineData(null, null, 5L, null, "5 out")]
    [InlineData("model", null, null, 42L, "model \u00B7 42 tokens")]
    [InlineData("model", 1L, null, 42L, "model \u00B7 1 in")]
    [InlineData(" ", null, null, null, "")]
    [InlineData(null, null, null, null, "")]
    public void Format_LeavesOutWhatTheProviderDidNotReport(
        string? modelId, long? input, long? output, long? total, string expected)
    {
        var usage = input is null && output is null && total is null
            ? null
            : new UsageDetails { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = total };

        Assert.Equal(expected, TurnFooterConverter.Format(modelId, usage, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Convert_DescribesTheTurnFromItsModelItsUsageAndWhetherItStopped()
    {
        var converter = new TurnFooterConverter();
        var usage = new UsageDetails { InputTokenCount = 3, OutputTokenCount = 2 };

        Assert.Equal("actual-model \u00B7 3 in \u00B7 2 out",
            converter.Convert(["actual-model", usage, false], typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("Stopped \u00B7 actual-model \u00B7 3 in \u00B7 2 out",
            converter.Convert(["actual-model", usage, true], typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("Stopped", converter.Convert([null, null, true], typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(string.Empty, converter.Convert([null, null, false], typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(string.Empty, converter.Convert([], typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack("text", [typeof(string)], null, CultureInfo.InvariantCulture));
    }
}
