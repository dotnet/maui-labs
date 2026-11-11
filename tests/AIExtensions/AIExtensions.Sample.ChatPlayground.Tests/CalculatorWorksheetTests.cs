using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

/// <summary>
/// The calculator worksheet: <see cref="CalculatorWorksheetHandler"/> folds every <c>calculate</c> call of a response
/// into one <see cref="CalculatorWorksheetBlock"/>, with a row per calculation that fills in when its result arrives.
/// </summary>
public sealed class CalculatorWorksheetTests
{
    [Fact]
    public async Task OneCalculation_IsAOneRowWorksheetThatFillsInWithItsResult()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(
            Calls(Calculate("call-1", 12, "multiply", 7)),
            Results(new FunctionResultContent("call-1", 84)),
            new ChatResponseUpdate(ChatRole.Assistant, "12 \u00D7 7 = 84.")));
        using var harness = new PlaygroundChatHarness(model);
        var progress = TrackWorksheets(harness);

        await harness.Session.SendMessageAsync("What is 12 \u00D7 7?");

        var worksheet = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<CalculatorWorksheetBlock>());
        var row = Assert.Single(worksheet.Rows);
        Assert.Equal("call-1", row.CallId);
        Assert.Equal("12 \u00D7 7", CalculatorDisplay.FormatExpression(row.Call.Arguments, CultureInfo.InvariantCulture));
        Assert.Equal("84", CalculatorDisplay.FormatResult(row.Result, CultureInfo.InvariantCulture));
        Assert.False(row.IsPending);
        Assert.False(row.IsError);
        Assert.Equal(["1 rows, 0 done", "1 rows, 1 done"], progress);
        Assert.Equal(BlockLifecycleState.Inactive, worksheet.LifecycleState);
        Assert.DoesNotContain(harness.LastTurn.ResponseBlocks, block => block is FunctionInvocationContentBlock);
        Assert.Equal("12 \u00D7 7 = 84.", Assert.IsType<TextContentBlock>(harness.LastTurn.ResponseBlocks[^1]).RawText);
    }

    [Fact]
    public async Task SeveralCalculations_AreRowsOfOneWorksheetThatTheCalculatorFillsIn()
    {
        // The model asks for two calculations at once and, after their results, for a third; function invocation runs
        // each with the playground's calculator, as in the app.
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(Calls(Calculate("call-1", 12, "multiply", 7), Calculate("call-2", 84, "add", 16))),
            ScriptedResponse.Stream(Calls(Calculate("call-3", 100, "divide", 0))),
            ScriptedResponse.Stream(new ChatResponseUpdate(ChatRole.Assistant, "Done.")));
        using var live = new FunctionInvokingChatClient(model);
        using var harness = new PlaygroundChatHarness(live);
        var progress = TrackWorksheets(harness);

        await harness.Session.SendMessageAsync("Work out 12 \u00D7 7, add 16, then divide by zero.");

        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        var worksheet = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<CalculatorWorksheetBlock>());
        Assert.Equal(["call-1", "call-2", "call-3"], worksheet.Rows.Select(row => row.CallId));
        Assert.Equal(["84", "100"],
            worksheet.Rows.Take(2).Select(row => CalculatorDisplay.FormatResult(row.Result, CultureInfo.InvariantCulture)));
        Assert.True(worksheet.Rows[2].IsError);
        Assert.All(worksheet.Rows, row => Assert.True(row.HasResult));
        Assert.Equal(["2 rows, 0 done", "2 rows, 1 done", "2 rows, 2 done", "3 rows, 2 done", "3 rows, 3 done"], progress);
        Assert.DoesNotContain(harness.LastTurn.ResponseBlocks, block => block is FunctionInvocationContentBlock);
        Assert.Equal("Done.", Assert.IsType<TextContentBlock>(harness.LastTurn.ResponseBlocks[^1]).RawText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Results_FillTheirOwnRowsInWhateverOrderTheyArrive(bool reversed)
    {
        FunctionResultContent first = new("call-1", 84), second = new("call-2", 100);
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(
            Calls(Calculate("call-1", 12, "multiply", 7), Calculate("call-2", 84, "add", 16)),
            Results(reversed ? second : first),
            Results(reversed ? first : second)));
        using var harness = new PlaygroundChatHarness(model);
        var filled = new List<string>();
        using var rows = harness.Session.OnBlockAdded((_, block) =>
        {
            if (block is CalculatorWorksheetBlock worksheet)
                OnRowFilled(worksheet, row => filled.Add(row.CallId));
        });

        await harness.Session.SendMessageAsync("12 \u00D7 7 and 84 + 16?");

        var worksheet = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<CalculatorWorksheetBlock>());
        Assert.Equal(["call-1", "call-2"], worksheet.Rows.Select(row => row.CallId));
        Assert.Equal(["84", "100"],
            worksheet.Rows.Select(row => CalculatorDisplay.FormatResult(row.Result, CultureInfo.InvariantCulture)));
        string[] order = reversed ? ["call-2", "call-1"] : ["call-1", "call-2"];
        Assert.Equal(order, filled);
    }

    [Fact]
    public async Task OtherResults_AreLeftToTheirOwnCards()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(
            Calls(Calculate("call-1", 2, "add", 2), new FunctionCallContent("call-2", "get_current_local_datetime")),
            Results(new FunctionResultContent("call-2", "10:00")),
            Results(new FunctionResultContent("call-9", "A result for a call nobody made")),
            Results(new FunctionResultContent("call-1", 4))));
        using var harness = new PlaygroundChatHarness(model);

        await harness.Session.SendMessageAsync("What time is it, and what is 2 + 2?");

        var worksheet = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<CalculatorWorksheetBlock>());
        var row = Assert.Single(worksheet.Rows);
        Assert.Equal("4", CalculatorDisplay.FormatResult(row.Result, CultureInfo.InvariantCulture));
        var time = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<FunctionInvocationContentBlock>());
        Assert.Equal("get_current_local_datetime", time.ToolName);
        Assert.Equal("10:00", time.Result?.Result);
        Assert.Equal(2, harness.LastTurn.ResponseBlocks.Count);
    }

    [Fact]
    public async Task StoppedTurn_LeavesTheRowsThatHadNoResultWithoutOne()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(
            Calls(Calculate("call-1", 12, "multiply", 7), Calculate("call-2", 84, "add", 16)),
            Results(new FunctionResultContent("call-1", 84))) with { WaitForCancellation = true });
        using var harness = new PlaygroundChatHarness(model);

        var send = harness.Session.SendMessageAsync("12 \u00D7 7 and 84 + 16?");
        await model.StreamCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Session.CancelAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await send.WaitAsync(TimeSpan.FromSeconds(5));

        // The worksheet shows "No result" for a row that is still pending once its turn stopped or failed.
        Assert.True(harness.LastTurn.IsStopped);
        Assert.False(harness.LastTurn.IsFailed);
        var worksheet = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<CalculatorWorksheetBlock>());
        Assert.Equal([false, true], worksheet.Rows.Select(row => row.IsPending));
        Assert.Equal(BlockLifecycleState.Inactive, worksheet.LifecycleState);
    }

    [Fact]
    public async Task FailedTurn_LeavesTheRowsThatHadNoResultWithoutOne()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(
            Calls(Calculate("call-1", 12, "multiply", 7))) with
        {
            Failure = new InvalidOperationException("The connection dropped."),
        });
        using var harness = new PlaygroundChatHarness(model);

        await harness.Session.SendMessageAsync("What is 12 \u00D7 7?");

        Assert.Equal(ConversationStatus.Error, harness.Session.Status);
        Assert.True(harness.LastTurn.IsFailed);
        var worksheet = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<CalculatorWorksheetBlock>());
        Assert.True(Assert.Single(worksheet.Rows).IsPending);
    }

    [Fact]
    public void Row_DescribesItsCalculationAndItsResult()
    {
        var row = new CalculationRow(Calculate("call-1", 1.5, "divide", 0));
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.True(row.IsPending);
        Assert.Null(row.Answer);
        Assert.Null(row.Message);

        row.Result = new FunctionResultContent("call-1", null) { Exception = new DivideByZeroException() };

        Assert.False(row.IsPending);
        Assert.True(row.IsError);
        Assert.False(row.HasAnswer);
        Assert.Equal(new DivideByZeroException().Message, row.Message);
        Assert.Contains(nameof(CalculationRow.Message), changed);
        Assert.Contains(nameof(CalculationRow.HasMessage), changed);
        Assert.Contains(nameof(CalculationRow.IsPending), changed);
    }

    [Theory]
    [InlineData("84", "= 84", null, false)]
    [InlineData("\"Not invoked: the turn ended first.\"", null, "Not invoked: the turn ended first.", false)]
    [InlineData("\"Error: Unknown operation.\"", null, "Error: Unknown operation.", true)]
    [InlineData("null", null, "No result", false)]
    public void Row_ShowsANumberAsItsAnswerAndAnythingElseAsAMessage(string json, string? answer, string? message, bool isError)
    {
        using var value = JsonDocument.Parse(json);
        var row = new CalculationRow(Calculate("call-1", 12, "multiply", 7))
        {
            Result = new FunctionResultContent("call-1", value.RootElement.Clone()),
        };

        Assert.Equal((answer, answer is not null), (row.Answer, row.HasAnswer));
        Assert.Equal((message, message is not null), (row.Message, row.HasMessage));
        Assert.Equal(isError, row.IsError);
    }

    private static FunctionCallContent Calculate(string callId, double left, string operation, double right) =>
        new(callId, CalculatorWorksheetHandler.ToolName,
            new Dictionary<string, object?> { ["left"] = left, ["operation"] = operation, ["right"] = right });

    private static ChatResponseUpdate Calls(params FunctionCallContent[] calls) =>
        new(ChatRole.Assistant, [.. calls]) { MessageId = "calls" };

    private static ChatResponseUpdate Results(params FunctionResultContent[] results) =>
        new(ChatRole.Tool, [.. results]);

    /// <summary>Records "rows, done" each time a worksheet of the session changes, as its card would show it.</summary>
    private static List<string> TrackWorksheets(PlaygroundChatHarness harness)
    {
        var progress = new List<string>();
        harness.Session.OnBlockAdded((_, block) =>
        {
            if (block is not CalculatorWorksheetBlock worksheet)
                return;

            void Record()
            {
                var snapshot = $"{worksheet.Rows.Count} rows, {worksheet.Rows.Count(row => row.HasResult)} done";
                if (progress.Count == 0 || progress[^1] != snapshot)
                    progress.Add(snapshot);
            }

            Record();
            worksheet.Rows.CollectionChanged += (_, _) => Record();
            OnRowFilled(worksheet, _ => Record());
        });
        return progress;
    }

    /// <summary>Calls <paramref name="filled"/> when a row of the worksheet, present or added later, gets its result.</summary>
    private static void OnRowFilled(CalculatorWorksheetBlock worksheet, Action<CalculationRow> filled)
    {
        void Follow(CalculationRow row) => row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CalculationRow.HasResult) && row.HasResult)
                filled(row);
        };

        foreach (var row in worksheet.Rows)
            Follow(row);
        worksheet.Rows.CollectionChanged += (_, e) =>
        {
            foreach (CalculationRow row in e.NewItems ?? Array.Empty<CalculationRow>())
                Follow(row);
        };
    }
}
