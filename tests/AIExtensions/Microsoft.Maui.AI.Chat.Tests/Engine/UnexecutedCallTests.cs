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

        // Each call's block shows its own result.
        var calls = Assert.Single(context.Turns).ResponseBlocks.OfType<FunctionInvocationContentBlock>().ToList();
        Assert.Equal(2, calls.Count);
        Assert.Equal("4", calls[0].Result?.Result?.ToString());
        Assert.StartsWith(NotExecuted, calls[1].Result?.Result?.ToString());
        Assert.False(calls[1].IsAbandoned);
    }

    [Fact]
    public async Task CallReusingTheIdOfACallAHandlerShowsWithoutItsResult_RunsAsANewCall()
    {
        // A handler of the app's own shows the searches but not their results. The provider numbers its calls per
        // response, so the UI action that the model calls after the search reuses the search's id.
        var themes = new List<string>();
        var search = AIFunctionFactory.Create((string query) => $"Sunny in {query}", "Search", "Searches the web.");
        var setTheme = AIFunctionFactory.Create(
            (string theme) =>
            {
                themes.Add(theme);
                return $"Theme set to {theme}";
            },
            "SetTheme",
            "Sets the theme.");
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(
            ct,
            new FunctionCallContent("0", "Search", new Dictionary<string, object?> { ["query"] = "Paris" })));
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, SetTheme("0")));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Hello.", ct));
        using var context = new AgentContext(new UIAgent(new FunctionInvokingChatClient(model), options =>
        {
            options.ChatOptions = new ChatOptions { Tools = [search] };
            options.RegisterUIAction(setTheme, UIActionInvocationMode.Automatic);
            options.AddBlockHandler(new WorksheetHandler("Search", claimsResults: false));
        }));

        await context.SendMessageAsync("Search Paris, then go dark");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["dark"], themes);
        Assert.Equal(3, model.Requests.Count);
        ToolHistory.AssertValidWithReusedIds(model.Requests[^1]);
        var turn = Assert.Single(context.Turns);
        Assert.Single(Assert.Single(turn.ResponseBlocks.OfType<WorksheetBlock>()).Rows);
        Assert.True(Assert.Single(turn.ResponseBlocks.OfType<UIActionBlock>()).IsComplete);

        await context.SendMessageAsync("Hi");

        ToolHistory.AssertValidWithReusedIds(model.Requests[^1]);
    }

    [Fact]
    public async Task CallIdReusedWhileTheWorksheetStillStreams_EveryRowGetsItsResult()
    {
        // FunctionInvokingChatClient runs the first calculation, and the next response reuses its id for a calculation
        // that comes back uninvoked. The worksheet, which shows a row per call, claims both.
        var calculated = new List<string>();
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, Calculate("0", "2+2")));
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, Calculate("0", "3*3"), SetTheme("1")));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = CreateContext(
            model,
            calculated,
            UIActionInvocationMode.Automatic,
            options => options.AddBlockHandler(new WorksheetHandler(rowPerCall: true)));

        await context.SendMessageAsync("Calculate twice and go dark");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["2+2"], calculated);
        ToolHistory.AssertValidWithReusedIds(model.Requests[^1]);
        var rows = Assert.Single(Assert.Single(context.Turns).ResponseBlocks.OfType<WorksheetBlock>()).Rows;
        Assert.Collection(
            rows,
            row => Assert.Equal("4", row.Result?.Result?.ToString()),
            row => Assert.StartsWith(NotExecuted, row.Result?.Result?.ToString()));
    }

    [Fact]
    public async Task StoppedBeforeTheBatchIsAnswered_RestoreShowsTheRowOfTheClaimedCallAgain()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var calculated = new List<string>();
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, Calculate("calc-1", "2+2"), SetTheme("theme-1")));
        Action<UIAgentOptions> configure = options =>
        {
            options.AddBlockHandler(new WorksheetHandler());
            options.Thread = thread;
        };
        using var live = CreateContext(model, calculated, UIActionInvocationMode.Manual, configure);
        var awaitingInput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        live.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentContext.Status) && live.Status == ConversationStatus.AwaitingInput)
                awaitingInput.TrySetResult();
        };

        var send = live.SendMessageAsync("Calculate and go dark");
        await awaitingInput.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await live.CancelAsync();
        await send.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(Assert.Single(Assert.Single(live.Turns[^1].ResponseBlocks.OfType<WorksheetBlock>()).Rows).Result);

        // The handler shows the restored call again, so the app can show its row the way the stopped turn ended it.
        using var restored = CreateContext(new ScriptedChatClient(), calculated, UIActionInvocationMode.Manual, configure);
        await restored.RestoreAsync();

        var turn = Assert.Single(restored.Turns);
        Assert.True(turn.IsStopped);
        var row = Assert.Single(Assert.Single(turn.ResponseBlocks.OfType<WorksheetBlock>()).Rows);
        Assert.Equal("calc-1", row.CallId);
        Assert.Null(row.Result);
        Assert.True(Assert.Single(turn.ResponseBlocks.OfType<UIActionBlock>()).IsAbandoned);
        Assert.Empty(calculated);
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

        /// <summary>Finds the first row of the call id that still waits for its result.</summary>
        public WorksheetRow? FindWaiting(string callId) => Rows.Find(row => row.CallId == callId && row.Result is null);
    }

    private sealed class WorksheetRow(FunctionCallContent call)
    {
        public FunctionCallContent Call { get; } = call;

        public string CallId => Call.CallId;

        public FunctionResultContent? Result { get; set; }
    }

    /// <summary>
    /// Claims the calls of a tool in an update into a worksheet, like an app that folds calls into rows: a row per call
    /// id, or with <paramref name="rowPerCall"/> a row per call even when a provider reuses its id. With
    /// <paramref name="claimsResults"/> false it shows the calls but leaves their results to others.
    /// </summary>
    private sealed class WorksheetHandler(
        string toolName = "Calculate",
        bool claimsResults = true,
        bool rowPerCall = false) : ContentBlockHandler<WorksheetBlock>
    {
        public override BlockMappingResult<WorksheetBlock> Handle(BlockMappingContext context, WorksheetBlock state)
        {
            var changed = false;
            foreach (var content in context.UnhandledContents)
            {
                switch (content)
                {
                    case FunctionCallContent call when call.Name == toolName:
                        context.MarkHandled(call);
                        if (rowPerCall
                            ? !state.Rows.Exists(row => ReferenceEquals(row.Call, call))
                            : state.Find(call.CallId) is null)
                        {
                            state.Rows.Add(new WorksheetRow(call));
                        }

                        changed = true;
                        break;
                    case FunctionResultContent result
                        when claimsResults
                            && (rowPerCall ? state.FindWaiting(result.CallId) : state.Find(result.CallId)) is { } row:
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
