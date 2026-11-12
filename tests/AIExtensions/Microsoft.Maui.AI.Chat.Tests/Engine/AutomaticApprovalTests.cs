// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// When one call of a response needs approval, <see cref="FunctionInvokingChatClient"/> turns every call of the
/// response into an approval request, and marks the requests of the calls whose tools need no approval with
/// <c>RequiresConfirmation</c> false. The engine shows those requests as the calls they carry and approves them
/// itself, so the user decides only the calls that need it, and the batch runs once they did.
/// </summary>
public class AutomaticApprovalTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task CallThatNeedsNoApproval_BatchedWithOneThatDoes_IsShownAsACallAndRunsWithTheDecision(
        bool approve,
        bool split)
    {
        var scenario = new Scenario { ApproveDelete = approve };
        scenario.Model.Enqueue(ct => EmitCalls(ct, split, Weather("w-1", "Paris"), Delete("d-1", "a.txt")));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = scenario.CreateContext();

        await context.SendMessageAsync("Weather in Paris, and delete a.txt");

        // Only the call that needs approval waited for the user, and no call ran before the decision.
        var waiting = Assert.Single(scenario.WhileWaiting);
        Assert.Equal(["DeleteFile"], waiting.AwaitingInput);
        Assert.Equal(["GetWeather"], waiting.CallsWithoutResult);
        Assert.Empty(waiting.Weather);

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["Paris"], scenario.Weather);
        Assert.Equal(approve ? ["a.txt"] : [], scenario.Deleted);
        var turn = Assert.Single(context.Turns);
        var weather = Assert.Single(turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>());
        Assert.Equal("GetWeather", weather.ToolName);
        Assert.Equal("Sunny in Paris", weather.Result?.Result?.ToString());
        Assert.False(weather.IsAbandoned);
        var approval = Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal("DeleteFile", approval.ToolName);
        Assert.Equal(approve ? "Deleted a.txt" : RejectedCall.Result("Not now"), approval.Result?.Result?.ToString());
        Assert.Equal("Done.", Assert.Single(turn.ResponseBlocks.OfType<TextContentBlock>()).RawText);
        Assert.Equal(turn.ResponseBlocks.Count, turn.ResponseBlocks.Select(block => block.Id).Distinct().Count());

        Assert.Equal(2, scenario.Model.Requests.Count);
        ToolHistory.AssertValid(scenario.Model.Requests[1]);
        Assert.Equal("Sunny in Paris", ToolHistory.FindResult(scenario.Model.Requests[1], "w-1"));
    }

    [Fact]
    public async Task CallThatNeedsNoApproval_WithACardOfItsOwn_ShowsTheCardWithItsResult()
    {
        var scenario = new Scenario { Handler = new WeatherCardHandler() };
        scenario.Model.Enqueue(ct => EmitCalls(ct, split: false, Weather("w-1", "Paris"), Delete("d-1", "a.txt")));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = scenario.CreateContext();

        await context.SendMessageAsync("Weather in Paris, and delete a.txt");

        var turn = Assert.Single(context.Turns);
        var card = Assert.Single(turn.ResponseBlocks.OfType<WeatherCardBlock>());
        Assert.Equal("Paris", card.City);
        Assert.Equal("Sunny in Paris", card.Forecast);
        Assert.Equal("DeleteFile", Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>()).ToolName);
        Assert.DoesNotContain(turn.ResponseBlocks, block => block is FunctionInvocationContentBlock);
        ToolHistory.AssertValid(scenario.Model.Requests[1]);
    }

    [Fact]
    public async Task CallsThatNeedNoApproval_ShowInOneBlockOfTheAppsOwnType()
    {
        var scenario = new Scenario { Handler = new WeatherSheetHandler() };
        scenario.Model.Enqueue(ct => EmitCalls(
            ct,
            split: false,
            Weather("w-1", "Paris"),
            Weather("w-2", "Oslo"),
            Delete("d-1", "a.txt")));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = scenario.CreateContext();

        await context.SendMessageAsync("Weather in Paris and Oslo, and delete a.txt");

        var turn = Assert.Single(context.Turns);
        var sheet = Assert.Single(turn.ResponseBlocks.OfType<WeatherSheetBlock>());
        Assert.Equal(["Paris: Sunny in Paris", "Oslo: Sunny in Oslo"], sheet.Rows.Select(row => $"{row.City}: {row.Forecast}"));
        Assert.Equal(["Paris", "Oslo"], scenario.Weather);
        Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        ToolHistory.AssertValid(scenario.Model.Requests[1]);
    }

    [Fact]
    public async Task StoppedWhileTheUserDecides_TheCallThatNeedsNoApprovalNeverRuns_AndTheNextTurnSendsAValidHistory()
    {
        var scenario = new Scenario { Decide = false };
        scenario.Model.Enqueue(ct => EmitCalls(ct, split: false, Weather("w-1", "Paris"), Delete("d-1", "a.txt")));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Next.", ct));
        using var context = scenario.CreateContext();

        var send = context.SendMessageAsync("Weather in Paris, and delete a.txt");
        await scenario.AwaitingInput.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await context.CancelAsync();
        await send.WaitAsync(TimeSpan.FromSeconds(10));

        var turn = Assert.Single(context.Turns);
        var weather = Assert.Single(turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>());
        Assert.True(weather.IsAbandoned);
        Assert.False(weather.HasResult);
        Assert.Empty(scenario.Weather);
        Assert.Empty(scenario.Deleted);

        await context.SendMessageAsync("Never mind");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        ToolHistory.AssertValid(scenario.Model.Requests[^1]);
        Assert.Empty(scenario.Weather);
    }

    [Fact]
    public async Task Restore_ShowsTheCallThatNeedsNoApprovalAsACallWithItsResult()
    {
        var thread = new JsonConversationThread();
        var scenario = new Scenario { Thread = thread };
        scenario.Model.Enqueue(ct => EmitCalls(ct, split: false, Weather("w-1", "Paris"), Delete("d-1", "a.txt")));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using (var live = scenario.CreateContext())
            await live.SendMessageAsync("Weather in Paris, and delete a.txt");

        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Again.", ct));
        using var restored = scenario.CreateContext();
        await restored.RestoreAsync();

        var turn = Assert.Single(restored.Turns);
        var weather = Assert.Single(turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>());
        Assert.Equal("Sunny in Paris", weather.Result?.Result?.ToString());
        Assert.False(weather.IsAbandoned);
        var approval = Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Equal("Deleted a.txt", approval.Result?.Result?.ToString());

        await restored.SendMessageAsync("Again");

        Assert.Equal(ConversationStatus.Idle, restored.Status);
        ToolHistory.AssertValid(scenario.Model.Requests[^1]);
        Assert.Equal(["Paris"], scenario.Weather);
        Assert.Equal(["a.txt"], scenario.Deleted);
    }

    [Fact]
    public async Task RequestsThatAllNeedNoDecision_RunWithoutWaitingForTheUser()
    {
        // A source other than the batching of FunctionInvokingChatClient can send requests that need no decision.
        var scenario = new Scenario();
        scenario.Model.Enqueue(ct => EmitContents(
            ct,
            new ToolApprovalRequestContent("approval-w-1", Weather("w-1", "Paris")) { RequiresConfirmation = false }));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = scenario.CreateContext();

        await context.SendMessageAsync("Weather in Paris");

        Assert.Empty(scenario.WhileWaiting);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["Paris"], scenario.Weather);
        var turn = Assert.Single(context.Turns);
        var weather = Assert.Single(turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>());
        Assert.Equal("Sunny in Paris", weather.Result?.Result?.ToString());
        Assert.DoesNotContain(turn.ResponseBlocks, block => block is ToolApprovalBlock);
        Assert.Equal("Done.", Assert.Single(turn.ResponseBlocks.OfType<TextContentBlock>()).RawText);
        ToolHistory.AssertValid(scenario.Model.Requests[1]);
    }

    [Fact]
    public async Task RequestThatNeedsConfirmation_IsStillAnApproval_EvenForAToolThatNeedsNone()
    {
        var scenario = new Scenario();
        scenario.Model.Enqueue(ct => EmitContents(
            ct,
            new ToolApprovalRequestContent("approval-w-1", Weather("w-1", "Paris"))));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = scenario.CreateContext();

        await context.SendMessageAsync("Weather in Paris");

        var waiting = Assert.Single(scenario.WhileWaiting);
        Assert.Equal(["GetWeather"], waiting.AwaitingInput);
        Assert.Equal(["Paris"], scenario.Weather);
        Assert.Equal(ApprovalStatus.Approved, Assert.Single(Assert.Single(context.Turns).ResponseBlocks.OfType<ToolApprovalBlock>()).Status);
    }

    [Fact]
    public async Task UIActionAndCallThatNeedNoApproval_BatchedWithOneThatDoes_EachRunTheirWay()
    {
        var scenario = new Scenario { RegisterShowMap = true };
        scenario.Model.Enqueue(ct => EmitCalls(
            ct,
            split: false,
            ShowMap("m-1", "Paris"),
            Weather("w-1", "Paris"),
            Delete("d-1", "a.txt")));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = scenario.CreateContext();

        await context.SendMessageAsync("Show Paris with its weather, and delete a.txt");

        var waiting = Assert.Single(scenario.WhileWaiting);
        Assert.Equal(["DeleteFile"], waiting.AwaitingInput);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["Paris"], scenario.Shown);
        Assert.Equal(["Paris"], scenario.Weather);
        Assert.Equal(["a.txt"], scenario.Deleted);
        var turn = Assert.Single(context.Turns);
        Assert.Equal("Map of Paris", Assert.Single(turn.ResponseBlocks.OfType<UIActionBlock>()).Result?.Result?.ToString());
        Assert.Equal("Sunny in Paris", Assert.Single(turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>()).Result?.Result?.ToString());
        Assert.Equal("Deleted a.txt", Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>()).Result?.Result?.ToString());
        ToolHistory.AssertValid(scenario.Model.Requests[1]);
        Assert.Equal("Map of Paris", ToolHistory.FindResult(scenario.Model.Requests[1], "m-1"));
        Assert.Equal("Sunny in Paris", ToolHistory.FindResult(scenario.Model.Requests[1], "w-1"));
    }

    private static FunctionCallContent Weather(string callId, string city)
        => new(callId, "GetWeather", new Dictionary<string, object?> { ["city"] = city });

    private static FunctionCallContent Delete(string callId, string fileName)
        => new(callId, "DeleteFile", new Dictionary<string, object?> { ["fileName"] = fileName });

    private static FunctionCallContent ShowMap(string callId, string city)
        => new(callId, "ShowMap", new Dictionary<string, object?> { ["city"] = city });

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitCalls(
        [EnumeratorCancellation] CancellationToken cancellationToken,
        bool split,
        params FunctionCallContent[] calls)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!split)
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = "assistant-calls",
                Contents = [.. calls],
                FinishReason = ChatFinishReason.ToolCalls,
            };
            yield break;
        }

        for (var i = 0; i < calls.Length; i++)
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = "assistant-calls",
                Contents = [calls[i]],
                FinishReason = i == calls.Length - 1 ? ChatFinishReason.ToolCalls : null,
            };
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitContents(
        [EnumeratorCancellation] CancellationToken cancellationToken,
        params AIContent[] contents)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-requests",
            Contents = [.. contents],
            FinishReason = ChatFinishReason.ToolCalls,
        };
        await Task.CompletedTask;
    }

    private sealed record Waiting(
        IReadOnlyList<string> AwaitingInput,
        IReadOnlyList<string> CallsWithoutResult,
        IReadOnlyList<string> Weather);

    private sealed class Scenario
    {
        internal ScriptedChatClient Model { get; } = new();

        internal List<string> Weather { get; } = [];

        internal List<string> Deleted { get; } = [];

        internal List<string> Shown { get; } = [];

        internal List<Waiting> WhileWaiting { get; } = [];

        internal TaskCompletionSource AwaitingInput { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool Decide { get; init; } = true;

        internal bool ApproveDelete { get; init; } = true;

        internal bool RegisterShowMap { get; init; }

        internal object? Handler { get; init; }

        internal IConversationThread? Thread { get; init; }

        internal AgentContext CreateContext()
        {
            AIFunction deleteFile = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
                (string fileName) =>
                {
                    Deleted.Add(fileName);
                    return $"Deleted {fileName}";
                },
                "DeleteFile",
                "Deletes a file."));
            AIFunction getWeather = AIFunctionFactory.Create(
                (string city) =>
                {
                    Weather.Add(city);
                    return $"Sunny in {city}";
                },
                "GetWeather",
                "Gets the weather.");
            AIFunction showMap = AIFunctionFactory.Create(
                (string city) =>
                {
                    Shown.Add(city);
                    return $"Map of {city}";
                },
                "ShowMap",
                "Shows a map.");

            var context = new AgentContext(new UIAgent(
                new FunctionInvokingChatClient(Model),
                options =>
                {
                    options.ChatOptions = new ChatOptions { Tools = [deleteFile, getWeather] };
                    options.Thread = Thread;
                    if (RegisterShowMap)
                        options.RegisterUIAction(showMap);
                    switch (Handler)
                    {
                        case WeatherCardHandler card:
                            options.AddBlockHandler(card);
                            break;
                        case WeatherSheetHandler sheet:
                            options.AddBlockHandler(sheet);
                            break;
                    }
                }));
            context.OnStatusChanged(status =>
            {
                if (status != ConversationStatus.AwaitingInput)
                    return;

                var blocks = context.Turns[^1].ResponseBlocks;
                WhileWaiting.Add(new Waiting(
                    [.. blocks.OfType<InteractiveFunctionBlock>().Where(block => block.IsAwaitingInput).Select(block => block.ToolName!)],
                    [.. blocks.OfType<FunctionInvocationContentBlock>().Where(block => !block.HasResult).Select(block => block.ToolName!)],
                    [.. Weather]));
                AwaitingInput.TrySetResult();
                if (!Decide)
                    return;

                foreach (var approval in blocks.OfType<ToolApprovalBlock>())
                {
                    if (approval.ToolName == "DeleteFile" && !ApproveDelete)
                        approval.Reject("Not now");
                    else
                        approval.Approve();
                }
            });
            return context;
        }
    }

    private sealed class WeatherCardBlock : ContentBlock
    {
        public string? City { get; set; }

        public string? Forecast { get; set; }
    }

    /// <summary>Shows a weather call as a card of the app's own type.</summary>
    private sealed class WeatherCardHandler : ContentBlockHandler<WeatherCardBlock>
    {
        public override BlockMappingResult<WeatherCardBlock> Handle(BlockMappingContext context, WeatherCardBlock state)
        {
            foreach (var content in context.UnhandledContents)
            {
                switch (content)
                {
                    case FunctionCallContent { Name: "GetWeather" } call when state.Id.Length == 0:
                        context.MarkHandled(call);
                        state.Id = call.CallId;
                        state.City = call.Arguments?["city"]?.ToString();
                        return BlockMappingResult<WeatherCardBlock>.Emit(state, state);
                    case FunctionResultContent result when state.Id.Length > 0 && result.CallId == state.Id:
                        context.MarkHandled(result);
                        state.Forecast = result.Result?.ToString();
                        return BlockMappingResult<WeatherCardBlock>.Complete();
                }
            }

            return BlockMappingResult<WeatherCardBlock>.Pass();
        }
    }

    private sealed class WeatherSheetBlock : ContentBlock
    {
        public List<WeatherRow> Rows { get; } = [];
    }

    private sealed record WeatherRow(string CallId, string? City, string? Forecast);

    /// <summary>Shows every weather call of a message as a row of one sheet, like the playground's worksheet.</summary>
    private sealed class WeatherSheetHandler : ContentBlockHandler<WeatherSheetBlock>
    {
        public override BlockMappingResult<WeatherSheetBlock> Handle(BlockMappingContext context, WeatherSheetBlock state)
        {
            var changed = false;
            foreach (var content in context.UnhandledContents)
            {
                switch (content)
                {
                    case FunctionCallContent { Name: "GetWeather" } call:
                        context.MarkHandled(call);
                        state.Rows.Add(new WeatherRow(call.CallId, call.Arguments?["city"]?.ToString(), Forecast: null));
                        changed = true;
                        break;
                    case FunctionResultContent result when state.Rows.FindIndex(row => row.CallId == result.CallId) is var index and >= 0:
                        context.MarkHandled(result);
                        state.Rows[index] = state.Rows[index] with { Forecast = result.Result?.ToString() };
                        changed = true;
                        break;
                }
            }

            if (!changed)
                return BlockMappingResult<WeatherSheetBlock>.Pass();

            if (state.Id.Length == 0)
            {
                state.Id = state.Rows[0].CallId;
                return BlockMappingResult<WeatherSheetBlock>.Emit(state, state);
            }

            return state.Rows.TrueForAll(static row => row.Forecast is not null)
                ? BlockMappingResult<WeatherSheetBlock>.Complete()
                : BlockMappingResult<WeatherSheetBlock>.Update(state);
        }
    }
}
