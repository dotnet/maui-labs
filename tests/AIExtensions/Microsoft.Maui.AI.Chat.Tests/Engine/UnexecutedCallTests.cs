// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// <see cref="FunctionInvokingChatClient"/> hands every call of a batch back uninvoked when one of them targets a
/// UI action. The engine answers each server call of the batch with an error result, whichever handler shows the
/// call, so the provider accepts the continuation, and that handler receives the result like any other.
/// </summary>
public class UnexecutedCallTests
{
    private const string NotExecuted = "Error: The tool call was not executed";

    [Theory]
    [InlineData(UIActionInvocationMode.Automatic)]
    [InlineData(UIActionInvocationMode.Manual)]
    public async Task CallsACustomHandlerClaims_AreAnswered_AndTheHandlerReceivesTheirResults(UIActionInvocationMode mode)
    {
        var calculated = new List<string>();
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(
            ct,
            Calculate("calc-1", "2+2"),
            Calculate("calc-2", "3*3"),
            SetTheme("theme-1")));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = CreateContext(model, calculated, mode, options => options.AddBlockHandler(new WorksheetHandler()));
        InvokeManualActionsWhenAwaitingInput(context);

        await context.SendMessageAsync("Calculate and go dark");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Empty(calculated);
        var followUp = model.Requests[^1];
        Assert.Equal(2, model.Requests.Count);
        ToolHistory.AssertValid(followUp);
        Assert.StartsWith(NotExecuted, ToolHistory.FindResult(followUp, "calc-1"));
        Assert.StartsWith(NotExecuted, ToolHistory.FindResult(followUp, "calc-2"));
        Assert.Equal("Theme set to dark", ToolHistory.FindResult(followUp, "theme-1"));

        // The worksheet's handler received the results the request carries.
        var worksheet = Assert.Single(Assert.Single(context.Turns).ResponseBlocks.OfType<WorksheetBlock>());
        Assert.Equal(["calc-1", "calc-2"], worksheet.Rows.Select(row => row.CallId));
        Assert.All(worksheet.Rows, row => Assert.Same(FindResultContent(followUp, row.CallId), row.Result));
    }

    [Fact]
    public async Task CallATypedBlockShows_IsAnsweredThroughItsHandler()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, Calculate("calc-1", "2+2"), SetTheme("theme-1")));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = CreateContext(
            model,
            calculated: [],
            UIActionInvocationMode.Automatic,
            options => options.AddBlockHandler(new CalculationCardHandler()));

        await context.SendMessageAsync("Calculate and go dark");

        ToolHistory.AssertValid(model.Requests[^1]);
        var card = Assert.Single(Assert.Single(context.Turns).ResponseBlocks.OfType<CalculationCard>());
        Assert.StartsWith(NotExecuted, card.Answer);
        Assert.True(card.HasResult);
        Assert.False(card.IsAbandoned);
    }

    [Fact]
    public async Task InformationalCall_GetsNoResult()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(
            ct,
            new FunctionCallContent("search-1", "WebSearch") { InformationalOnly = true },
            SetTheme("theme-1")));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = CreateContext(model, calculated: [], UIActionInvocationMode.Automatic);

        await context.SendMessageAsync("Search and go dark");

        var followUp = model.Requests[^1];
        Assert.DoesNotContain(
            followUp.SelectMany(message => message.Contents).OfType<FunctionResultContent>(),
            result => result.CallId == "search-1");
        Assert.Equal("Theme set to dark", ToolHistory.FindResult(followUp, "theme-1"));
    }

    [Fact]
    public async Task CallAnsweredEarlierInTheRound_GetsNoSecondResult_EvenWhenALaterCallReusesItsId()
    {
        // FunctionInvokingChatClient runs the first calculation itself, so its result is part of the round. The
        // next response numbers its calls from "0" again, and its batch comes back uninvoked.
        var calculated = new List<string>();
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, Calculate("0", "2+2")));
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, Calculate("0", "3*3"), SetTheme("1")));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = CreateContext(model, calculated, UIActionInvocationMode.Automatic);

        await context.SendMessageAsync("Calculate twice and go dark");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["2+2"], calculated);
        var followUp = model.Requests[^1];
        ToolHistory.AssertValidWithReusedIds(followUp);
        var results = followUp.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToList();
        Assert.Equal(3, results.Count);
        Assert.Equal(("0", "4"), (results[0].CallId, results[0].Result?.ToString()));
        var continuation = results.Skip(1).ToDictionary(result => result.CallId);
        Assert.StartsWith(NotExecuted, continuation["0"].Result?.ToString());
        Assert.Equal("Theme set to dark", continuation["1"].Result?.ToString());
    }

    private static AgentContext CreateContext(
        ScriptedChatClient model,
        List<string> calculated,
        UIActionInvocationMode mode,
        Action<UIAgentOptions>? configure = null)
    {
        var calculate = AIFunctionFactory.Create(
            (string expression) =>
            {
                calculated.Add(expression);
                return expression == "2+2" ? "4" : "9";
            },
            "Calculate",
            "Calculates an expression.");
        var setTheme = AIFunctionFactory.Create((string theme) => $"Theme set to {theme}", "SetTheme", "Sets the theme.");
        return new AgentContext(new UIAgent(new FunctionInvokingChatClient(model), options =>
        {
            options.ChatOptions = new ChatOptions { Tools = [calculate] };
            options.RegisterUIAction(setTheme, mode);
            configure?.Invoke(options);
        }));
    }

    private static void InvokeManualActionsWhenAwaitingInput(AgentContext context)
    {
        context.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AgentContext.Status) || context.Status != ConversationStatus.AwaitingInput)
                return;

            foreach (var action in context.Turns[^1].ResponseBlocks.OfType<UIActionBlock>())
            {
                if (action.IsAwaitingInput)
                    _ = action.InvokeAsync();
            }
        };
    }

    private static FunctionCallContent Calculate(string callId, string expression)
        => new(callId, "Calculate", new Dictionary<string, object?> { ["expression"] = expression });

    private static FunctionCallContent SetTheme(string callId)
        => new(callId, "SetTheme", new Dictionary<string, object?> { ["theme"] = "dark" });

    private static FunctionResultContent FindResultContent(IReadOnlyList<ChatMessage> messages, string callId)
        => Assert.Single(messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>(), result => result.CallId == callId);

    /// <summary>Every calculation of a response as one block, with a row per call that fills in with its result.</summary>
    private sealed class WorksheetBlock : ContentBlock
    {
        public List<WorksheetRow> Rows { get; } = [];

        public WorksheetRow? Find(string callId) => Rows.Find(row => row.CallId == callId);
    }

    private sealed class WorksheetRow(string callId)
    {
        public string CallId { get; } = callId;

        public FunctionResultContent? Result { get; set; }
    }

    /// <summary>Claims the calculations of an update into a worksheet, like an app that folds calls into rows.</summary>
    private sealed class WorksheetHandler : ContentBlockHandler<WorksheetBlock>
    {
        public override BlockMappingResult<WorksheetBlock> Handle(BlockMappingContext context, WorksheetBlock state)
        {
            var changed = false;
            foreach (var content in context.UnhandledContents)
            {
                switch (content)
                {
                    case FunctionCallContent { Name: "Calculate" } call:
                        context.MarkHandled(call);
                        if (state.Find(call.CallId) is null)
                            state.Rows.Add(new WorksheetRow(call.CallId));
                        changed = true;
                        break;
                    case FunctionResultContent result when state.Find(result.CallId) is { } row:
                        context.MarkHandled(result);
                        row.Result = result;
                        changed = true;
                        break;
                }
            }

            if (!changed)
                return BlockMappingResult<WorksheetBlock>.Pass();

            if (!string.IsNullOrEmpty(state.Id))
                return BlockMappingResult<WorksheetBlock>.Update(state);

            state.Id = context.Update.MessageId ?? Guid.NewGuid().ToString("N");
            return BlockMappingResult<WorksheetBlock>.Emit(state, state);
        }
    }

    /// <summary>A typed tool card, like a <c>[ToolBlock]</c>, whose handler reads the answer out of the result.</summary>
    private sealed class CalculationCard : FunctionInvocationContentBlock
    {
        public string? Answer { get; set; }
    }

    private sealed class CalculationCardHandler : ContentBlockHandler<CalculationCard>
    {
        public override BlockMappingResult<CalculationCard> Handle(BlockMappingContext context, CalculationCard state)
        {
            foreach (var content in context.UnhandledContents)
            {
                if (state.Call is null && content is FunctionCallContent { Name: "Calculate" } call)
                {
                    context.MarkHandled(call);
                    state.Call = call;
                    return BlockMappingResult<CalculationCard>.Emit(state, state);
                }

                if (state.Call is { } shown && content is FunctionResultContent result && result.CallId == shown.CallId)
                {
                    context.MarkHandled(result);
                    state.Result = result;
                    state.Answer = result.Result?.ToString();
                    return BlockMappingResult<CalculationCard>.Complete();
                }
            }

            return BlockMappingResult<CalculationCard>.Pass();
        }
    }
}
