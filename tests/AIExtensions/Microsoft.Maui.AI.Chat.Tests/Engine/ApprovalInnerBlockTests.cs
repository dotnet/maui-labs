// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// An approval wraps the block that the registered handlers create for its call, as they would without the
/// approval, also a block of the app's own type. That block receives the call's result through its handler, and
/// the approval holds the call and its outcome itself.
/// </summary>
public class ApprovalInnerBlockTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApprovalOfACallShownByABlockOfTheAppsOwnType_WrapsThatBlock_AndBothGetTheResult(bool approve)
    {
        var scenario = new Scenario { Approve = approve };
        scenario.Model.Enqueue(ct => EmitCalls(ct, Weather("w-1", "Paris")));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = scenario.CreateContext();

        await context.SendMessageAsync("Weather in Paris");

        var waiting = Assert.Single(scenario.WhileWaiting);
        Assert.Equal("Paris", waiting.City);
        Assert.Null(waiting.Forecast);
        Assert.False(waiting.HasResult);

        Assert.Equal(ConversationStatus.Idle, context.Status);
        var turn = Assert.Single(context.Turns);
        var approval = Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        var card = Assert.IsType<WeatherCard>(approval.InnerBlock);
        Assert.DoesNotContain(turn.ResponseBlocks, block => block is WeatherCard);
        Assert.Equal("w-1", approval.Call?.CallId);
        Assert.Equal("GetWeather", approval.ToolName);
        Assert.Equal(approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected, approval.Status);
        var expected = approve ? "Sunny in Paris" : RejectedCall.Result("Not now");
        Assert.Equal(expected, card.Forecast);
        Assert.Equal(expected, approval.Result?.Result?.ToString());
        Assert.False(approval.IsAbandoned);
        Assert.Equal(approve ? ["Paris"] : [], scenario.Weather);
        ToolHistory.AssertValid(scenario.Model.Requests[1]);
    }

    [Fact]
    public async Task ApprovalOfACallShownByABlockOfTheAppsOwnType_HoldsTheResult_WhenTheBlockDoesNotShowIt()
    {
        var scenario = new Scenario { CardShowsResult = false };
        scenario.Model.Enqueue(ct => EmitCalls(ct, Weather("w-1", "Paris")));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = scenario.CreateContext();

        await context.SendMessageAsync("Weather in Paris");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        var approval = Assert.Single(Assert.Single(context.Turns).ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Null(Assert.IsType<WeatherCard>(approval.InnerBlock).Forecast);
        Assert.Equal("Sunny in Paris", approval.Result?.Result?.ToString());
        Assert.True(approval.HasResult);
        ToolHistory.AssertValid(scenario.Model.Requests[1]);
    }

    [Fact]
    public async Task StoppedWhileTheUserDecides_TheApprovalOfABlockOfTheAppsOwnTypeIsAbandoned()
    {
        var scenario = new Scenario { Decide = false };
        scenario.Model.Enqueue(ct => EmitCalls(ct, Weather("w-1", "Paris")));
        using var context = scenario.CreateContext();

        var send = context.SendMessageAsync("Weather in Paris");
        await scenario.AwaitingInput.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var approval = Assert.Single(context.Turns[^1].ResponseBlocks.OfType<ToolApprovalBlock>());
        var changes = new List<string?>();
        approval.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        await context.CancelAsync();
        await send.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        Assert.False(approval.IsAwaitingInput);
        Assert.True(approval.IsAbandoned);
        Assert.False(approval.HasResult);
        Assert.Contains(nameof(ToolApprovalBlock.IsAbandoned), changes);
        Assert.Contains(nameof(ToolApprovalBlock.IsAwaitingInput), changes);
        Assert.Empty(scenario.Weather);
    }

    [Fact]
    public async Task Restore_WrapsTheBlockOfTheAppsOwnTypeAgain_WithItsResult()
    {
        var thread = new JsonConversationThread();
        var scenario = new Scenario { Thread = thread };
        scenario.Model.Enqueue(ct => EmitCalls(ct, Weather("w-1", "Paris")));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using (var live = scenario.CreateContext())
            await live.SendMessageAsync("Weather in Paris");

        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Again.", ct));
        using var restored = scenario.CreateContext();
        await restored.RestoreAsync();

        var turn = Assert.Single(restored.Turns);
        var approval = Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.False(approval.IsAwaitingInput);
        Assert.Equal("Sunny in Paris", Assert.IsType<WeatherCard>(approval.InnerBlock).Forecast);
        Assert.Equal("Sunny in Paris", approval.Result?.Result?.ToString());
        Assert.False(approval.IsAbandoned);

        await restored.SendMessageAsync("Again");

        Assert.Equal(ConversationStatus.Idle, restored.Status);
        ToolHistory.AssertValid(scenario.Model.Requests[^1]);
        Assert.Equal(["Paris"], scenario.Weather);
    }

    [Fact]
    public async Task CallsThatEachNeedApproval_EachWrapTheirOwnBlockOfTheAppsOwnType()
    {
        var scenario = new Scenario();
        scenario.Model.Enqueue(ct => EmitCalls(ct, Weather("w-1", "Paris"), Weather("w-2", "Oslo")));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = scenario.CreateContext();

        await context.SendMessageAsync("Weather in Paris and Oslo");

        var approvals = Assert.Single(context.Turns).ResponseBlocks.OfType<ToolApprovalBlock>().ToList();
        Assert.Equal(["w-1", "w-2"], approvals.Select(approval => approval.Call?.CallId));
        Assert.Equal(
            ["Paris: Sunny in Paris", "Oslo: Sunny in Oslo"],
            approvals.Select(approval => approval.InnerBlock).Cast<WeatherCard>().Select(card => $"{card.City}: {card.Forecast}"));
        Assert.NotSame(approvals[0].InnerBlock, approvals[1].InnerBlock);
        ToolHistory.AssertValid(scenario.Model.Requests[1]);
    }

    private static FunctionCallContent Weather(string callId, string city)
        => new(callId, "GetWeather", new Dictionary<string, object?> { ["city"] = city });

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitCalls(
        [EnumeratorCancellation] CancellationToken cancellationToken,
        params FunctionCallContent[] calls)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-calls",
            Contents = [.. calls],
            FinishReason = ChatFinishReason.ToolCalls,
        };
        await Task.CompletedTask;
    }

    private sealed record Waiting(string? City, string? Forecast, bool HasResult);

    private sealed class Scenario
    {
        internal ScriptedChatClient Model { get; } = new();

        internal List<string> Weather { get; } = [];

        internal List<Waiting> WhileWaiting { get; } = [];

        internal TaskCompletionSource AwaitingInput { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool Decide { get; init; } = true;

        internal bool Approve { get; init; } = true;

        internal bool CardShowsResult { get; init; } = true;

        internal IConversationThread? Thread { get; init; }

        internal AgentContext CreateContext()
        {
            AIFunction getWeather = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
                (string city) =>
                {
                    Weather.Add(city);
                    return $"Sunny in {city}";
                },
                "GetWeather",
                "Gets the weather."));

            var context = new AgentContext(new UIAgent(
                new FunctionInvokingChatClient(Model),
                options =>
                {
                    options.ChatOptions = new ChatOptions { Tools = [getWeather] };
                    options.Thread = Thread;
                    options.AddBlockHandler(new WeatherCardHandler(CardShowsResult));
                }));
            context.OnStatusChanged(status =>
            {
                if (status != ConversationStatus.AwaitingInput)
                    return;

                var approvals = context.Turns[^1].ResponseBlocks.OfType<ToolApprovalBlock>().ToList();
                var first = approvals[0];
                var card = (WeatherCard)first.InnerBlock;
                WhileWaiting.Add(new Waiting(card.City, card.Forecast, first.HasResult));
                AwaitingInput.TrySetResult();
                if (!Decide)
                    return;

                foreach (var approval in approvals)
                {
                    if (Approve)
                        approval.Approve();
                    else
                        approval.Reject("Not now");
                }
            });
            return context;
        }
    }

    /// <summary>A card of the app's own type, which is not a function block.</summary>
    private sealed class WeatherCard : ContentBlock
    {
        public string? City { get; set; }

        public string? Forecast { get; set; }
    }

    private sealed class WeatherCardHandler(bool showsResult) : ContentBlockHandler<WeatherCard>
    {
        public override BlockMappingResult<WeatherCard> Handle(BlockMappingContext context, WeatherCard state)
        {
            foreach (var content in context.UnhandledContents)
            {
                switch (content)
                {
                    case FunctionCallContent { Name: "GetWeather" } call when state.Id.Length == 0:
                        context.MarkHandled(call);
                        state.Id = call.CallId;
                        state.City = call.Arguments?["city"]?.ToString();
                        return BlockMappingResult<WeatherCard>.Emit(state, state);
                    case FunctionResultContent result when showsResult && state.Id.Length > 0 && result.CallId == state.Id:
                        context.MarkHandled(result);
                        state.Forecast = result.Result?.ToString();
                        return BlockMappingResult<WeatherCard>.Complete();
                }
            }

            return BlockMappingResult<WeatherCard>.Pass();
        }
    }
}
