// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// A call's block tells a card all it needs: the approval that wraps the call, and whether the call will never
/// get a result because its turn ended without one.
/// </summary>
public class CallOutcomeTests
{
    [Fact]
    public async Task Approval_LinksEachWrappedCallToItsApproval()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitUpdates(
            [
                new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    MessageId = "response-1",
                    Contents =
                    [
                        // A typed card, created by the handler for the tool's name.
                        new ToolApprovalRequestContent("approval-1", DeleteFileCall()),

                        // A call no handler claims, shown by the built-in block.
                        new ToolApprovalRequestContent("approval-2", new FunctionCallContent("search-1", "Search")),

                        // A call a handler shows as another kind of block, so a fallback block holds it.
                        new ToolApprovalRequestContent("approval-4", new FunctionCallContent("notify-1", "Notify")),

                        // A call without an approval.
                        new FunctionCallContent("weather-1", "GetWeather") { InformationalOnly = true },
                    ],
                },
            ],
            ct));
        using var context = new AgentContext(new UIAgent(model, options =>
        {
            options.AddBlockHandler(new DeleteFileCardHandler());
            options.AddBlockHandler(new NoticeHandler());
        }));
        var awaitingInput = WhenAwaitingInput(context);

        var send = context.SendMessageAsync("Tidy up");
        await awaitingInput.WaitAsync(TimeSpan.FromSeconds(10));

        var blocks = context.Turns[^1].ResponseBlocks;
        var approvals = blocks.OfType<ToolApprovalBlock>().ToDictionary(approval => approval.ToolName!);
        Assert.Equal(["DeleteFile", "Notify", "Search"], approvals.Keys.Order());
        Assert.IsType<DeleteFileCard>(approvals["DeleteFile"].InnerBlock);
        Assert.Equal(typeof(FunctionInvocationContentBlock), approvals["Search"].InnerBlock.GetType());
        Assert.Equal(typeof(FunctionInvocationContentBlock), approvals["Notify"].InnerBlock.GetType());
        Assert.Equal("notify-1", approvals["Notify"].InnerBlock.Call?.CallId);
        Assert.DoesNotContain(blocks, block => block is Notice);
        Assert.All(approvals.Values, approval => Assert.Same(approval, approval.InnerBlock.Approval));
        Assert.Null(Assert.Single(blocks.OfType<FunctionInvocationContentBlock>()).Approval);

        await context.CancelAsync();
        await send.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task UIActionThatNeedsApproval_ItsCallLinksToTheApprovalAndGetsTheActionsResult()
    {
        var shown = new List<string>();
        var showMap = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            (string city) =>
            {
                shown.Add(city);
                return $"Map of {city}";
            },
            "ShowMap",
            "Shows a map."));
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, ShowMapCall()));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = new AgentContext(new UIAgent(
            new FunctionInvokingChatClient(model),
            options => options.RegisterUIAction(showMap, UIActionInvocationMode.Automatic)));
        ToolApprovalBlock? approval = null;
        var linkedWhenAsked = false;
        context.OnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;

            approval = context.Turns[^1].ResponseBlocks.OfType<ToolApprovalBlock>().Single();
            linkedWhenAsked = ReferenceEquals(approval, approval.InnerBlock.Approval);
            approval.Approve();
        });

        await context.SendMessageAsync("Show Paris");

        Assert.NotNull(approval);
        Assert.True(linkedWhenAsked);
        Assert.Same(approval.UIAction?.InnerBlock, approval.InnerBlock);
        Assert.Equal(["Paris"], shown);
        Assert.Equal("Map of Paris", approval.InnerBlock.Result?.Result?.ToString());
        Assert.False(approval.InnerBlock.IsAbandoned);
        Assert.False(approval.IsAbandoned);
    }

    [Fact]
    public async Task StoppedWhileTheCallRuns_TheCallIsAbandoned()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calculate = AIFunctionFactory.Create(
            async (string expression, CancellationToken cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return "4";
            },
            "Calculate",
            "Calculates an expression.");
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitToolCallResponse("calc-1", "Calculate", Expression("2+2"), ct));
        using var context = new AgentContext(new UIAgent(
            new FunctionInvokingChatClient(model),
            options => options.ChatOptions = new ChatOptions { Tools = [calculate] }));

        var send = context.SendMessageAsync("What is 2+2?");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var turn = context.Turns[^1];
        var call = Assert.Single(turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>());
        Assert.False(call.IsAbandoned);
        var abandonedChanges = RecordAbandoned(call);

        await context.CancelAsync();
        await send.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(turn.IsStopped);
        Assert.False(call.HasResult);
        Assert.True(call.IsAbandoned);
        Assert.Equal([true], abandonedChanges);
    }

    [Fact]
    public async Task StoppedWhileAnApprovedCallRuns_TheApprovalShowsTheCallAbandoned()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calculate = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            async (string expression, CancellationToken cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return "4";
            },
            "Calculate",
            "Calculates an expression."));
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitToolCallResponse("calc-1", "Calculate", Expression("2+2"), ct));
        using var context = new AgentContext(new UIAgent(
            new FunctionInvokingChatClient(model),
            options => options.ChatOptions = new ChatOptions { Tools = [calculate] }));
        context.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                context.Turns[^1].ResponseBlocks.OfType<ToolApprovalBlock>().Single().Approve();
        });

        var send = context.SendMessageAsync("What is 2+2?");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var approval = Assert.Single(context.Turns[^1].ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.False(approval.IsAwaitingInput);
        var changes = RecordStates(approval);

        await context.CancelAsync();
        await send.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.True(approval.InnerBlock.IsAbandoned);
        Assert.Equal(["IsAbandoned: awaiting input False, abandoned True"], changes);
    }

    [Fact]
    public async Task FailedWhileTheCallRuns_TheCallIsAbandonedOnceTheFailureIsFinal()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(CalculationThenErrorAsync);
        using var context = new AgentContext(new UIAgent(model));
        var log = new List<string>();
        FunctionInvocationContentBlock? call = null;
        context.OnBlockAdded((turn, block) =>
        {
            if (block is not FunctionInvocationContentBlock added)
                return;

            call = added;
            added.PropertyChanged += (_, e) => log.Add(
                $"call.{e.PropertyName}:{added.IsAbandoned}:{turn.IsFailed}:{context.Status}");
        });
        context.OnTurnAdded(turn => turn.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversationTurn.IsFailed))
                log.Add($"turn.IsFailed:{call?.IsAbandoned}");
        });
        context.PropertyChanged += (_, e) =>
        {
            if (context.Status == ConversationStatus.Error)
                log.Add($"context.{e.PropertyName}:{call?.IsAbandoned}");
        };

        await context.SendMessageAsync("What is 2+2?");

        Assert.Equal(ConversationStatus.Error, context.Status);
        Assert.NotNull(call);
        Assert.True(call.IsAbandoned);

        // The call is abandoned before anything reports the failure, and reports it last.
        Assert.Equal(
            [
                "turn.IsFailed:True",
                "context.Error:True",
                "context.Status:True",
                "call.IsAbandoned:True:True:Error",
            ],
            log.Where(entry => !entry.StartsWith("call.LifecycleState", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RetryAfterAFailure_ShowsANewCallThatGetsItsResult()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(CalculationThenErrorAsync);
        model.Enqueue(ct => ResponseEmitters.EmitToolCallWithResultResponse("calc-1", "Calculate", Expression("2+2"), "4", ct));
        using var context = new AgentContext(new UIAgent(model));
        await context.SendMessageAsync("What is 2+2?");
        var turn = Assert.Single(context.Turns);
        var failedCall = Assert.Single(turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>());
        Assert.True(failedCall.IsAbandoned);

        await context.RetryAsync();

        Assert.Equal(ConversationStatus.Idle, context.Status);
        var call = Assert.Single(turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>());
        Assert.NotSame(failedCall, call);
        Assert.True(call.HasResult);
        Assert.False(call.IsAbandoned);
        Assert.True(failedCall.IsAbandoned);
    }

    [Fact]
    public async Task CompletedTurn_CallsLeftWithoutAResultAreAbandoned()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitUpdates(
            [
                new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    MessageId = "assistant-1",
                    Contents =
                    [
                        new FunctionCallContent("search-1", "Search") { InformationalOnly = true },
                        new FunctionCallContent("search-2", "Search") { InformationalOnly = true },
                    ],
                },
                new ChatResponseUpdate
                {
                    Role = ChatRole.Tool,
                    MessageId = "tool-1",
                    Contents = [new FunctionResultContent("search-1", "Found it.")],
                },
                new ChatResponseUpdate(ChatRole.Assistant, "Done.") { MessageId = "assistant-2" },
            ],
            ct));
        using var context = new AgentContext(new UIAgent(model));
        var abandonedChanges = new Dictionary<string, List<bool>>();
        context.OnBlockAdded((_, block) =>
        {
            if (block is FunctionInvocationContentBlock call)
                abandonedChanges[call.Id] = RecordAbandoned(call);
        });

        await context.SendMessageAsync("Search");

        var turn = Assert.Single(context.Turns);
        Assert.False(turn.IsStopped);
        Assert.False(turn.IsFailed);
        var calls = turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>().ToDictionary(call => call.Id);
        Assert.True(calls["search-1"].HasResult);
        Assert.False(calls["search-1"].IsAbandoned);
        Assert.Empty(abandonedChanges["search-1"]);
        Assert.False(calls["search-2"].HasResult);
        Assert.True(calls["search-2"].IsAbandoned);
        Assert.Equal([true], abandonedChanges["search-2"]);
    }

    [Fact]
    public async Task StoppedWhileWaitingForInput_TheApprovalAndUIActionCallsAreAbandoned()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitUpdates(
            [
                new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    MessageId = "response-1",
                    Contents = [new ToolApprovalRequestContent("approval-1", DeleteFileCall()), ShowMapCall()],
                },
            ],
            ct));
        using var context = new AgentContext(new UIAgent(model, options =>
            options.RegisterUIAction(CreateShowMap(), UIActionInvocationMode.Manual)));
        var awaitingInput = WhenAwaitingInput(context);

        var send = context.SendMessageAsync("Delete the file and show Paris");
        await awaitingInput.WaitAsync(TimeSpan.FromSeconds(10));
        var blocks = context.Turns[^1].ResponseBlocks;
        var approval = Assert.Single(blocks.OfType<ToolApprovalBlock>());
        var action = Assert.Single(blocks.OfType<UIActionBlock>());
        var approvalChanges = RecordStates(approval);
        var actionChanges = RecordStates(action);

        await context.CancelAsync();
        await send.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        Assert.True(approval.IsAbandoned);
        Assert.True(approval.InnerBlock.IsAbandoned);
        Assert.True(action.IsAbandoned);
        Assert.True(action.InnerBlock.IsAbandoned);

        // Each card raises each change once, when both changes are in place.
        string[] expected =
        [
            "IsAwaitingInput: awaiting input False, abandoned True",
            "IsAbandoned: awaiting input False, abandoned True",
        ];
        Assert.Equal(expected, approvalChanges);
        Assert.Equal(expected, actionChanges);
    }

    [Fact]
    public async Task RejectedCall_KeepsTheRejectionAsItsResultAndIsNotAbandoned()
    {
        var deleted = new List<string>();
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitToolCallResponse("delete-1", "DeleteFile", FileName("a.txt"), ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("I left it.", ct));
        using var context = CreateDeleteFileContext(model, deleted);
        context.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                context.Turns[^1].ResponseBlocks.OfType<ToolApprovalBlock>().Single().Reject("Not now");
        });

        await context.SendMessageAsync("Delete a.txt");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Empty(deleted);
        var approval = Assert.Single(Assert.Single(context.Turns).ResponseBlocks.OfType<ToolApprovalBlock>());
        var call = approval.InnerBlock;
        Assert.Same(approval, call.Approval);
        Assert.Equal(ApprovalStatus.Rejected, call.Approval?.Status);
        Assert.True(call.HasResult);
        Assert.Equal(RejectedCall.Result("Not now"), call.Result?.Result?.ToString());
        Assert.False(call.IsAbandoned);
        Assert.False(approval.IsAbandoned);
    }

    [Fact]
    public async Task RejectedAndStoppedBeforeTheRejectionWasSent_TheCallIsAbandoned()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitApprovalRequest("delete-1", "DeleteFile", FileName("a.txt"), ct));
        using var context = new AgentContext(new UIAgent(model));
        Task? stop = null;
        context.OnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;

            context.Turns[^1].ResponseBlocks.OfType<ToolApprovalBlock>().Single().Reject("Not now");
            stop = context.CancelAsync();
        });

        await context.SendMessageAsync("Delete a.txt");
        await stop!.WaitAsync(TimeSpan.FromSeconds(10));

        var turn = Assert.Single(context.Turns);
        Assert.True(turn.IsStopped);
        var approval = Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Rejected, approval.Status);
        Assert.False(approval.HasResult);
        Assert.True(approval.InnerBlock.IsAbandoned);
        Assert.True(approval.IsAbandoned);
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task RestoredCallsWithoutARecordedResult_AreAbandoned()
    {
        using var context = new AgentContext(new UIAgent(new ScriptedChatClient()));
        var approved = new ToolApprovalRequestContent("approval-2", new FunctionCallContent("delete-2", "DeleteFile"));

        await context.RestoreAsync(
        [
            new ChatMessage(ChatRole.User, "Search, then delete the files"),
            new ChatMessage(
                ChatRole.Assistant,
                [
                    new FunctionCallContent("search-1", "Search"),
                    new FunctionCallContent("search-2", "Search"),
                    new ToolApprovalRequestContent("approval-1", new FunctionCallContent("delete-1", "DeleteFile")),
                    approved,
                ]),
            new ChatMessage(ChatRole.User, [approved.CreateResponse(approved: true)]),
            new ChatMessage(
                ChatRole.Tool,
                [
                    new FunctionResultContent("search-1", "Found it."),
                    new FunctionResultContent("delete-2", "Deleted"),
                ]),
        ]);

        var blocks = Assert.Single(context.Turns).ResponseBlocks;
        var calls = blocks.OfType<FunctionInvocationContentBlock>().ToDictionary(call => call.Id);
        Assert.False(calls["search-1"].IsAbandoned);
        Assert.True(calls["search-2"].IsAbandoned);
        var approvals = blocks.OfType<ToolApprovalBlock>().ToDictionary(approval => approval.Call!.CallId);
        Assert.All(approvals.Values, approval => Assert.Same(approval, approval.InnerBlock.Approval));
        Assert.Equal(ApprovalStatus.Pending, approvals["delete-1"].Status);
        Assert.True(approvals["delete-1"].IsAbandoned);
        Assert.Equal(ApprovalStatus.Approved, approvals["delete-2"].Status);
        Assert.True(approvals["delete-2"].HasResult);
        Assert.False(approvals["delete-2"].IsAbandoned);
    }

    [Fact]
    public async Task RestoredFromAThread_CallsWithoutAResultAreAbandoned()
    {
        var thread = new InMemoryConversationThread("thread-1");
        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "Search") { MessageId = "user-1" });
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-1",
            Contents =
            [
                new FunctionCallContent("search-1", "Search") { InformationalOnly = true },
                new FunctionCallContent("search-2", "Search") { InformationalOnly = true },
            ],
        });
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Tool,
            MessageId = "tool-1",
            Contents = [new FunctionResultContent("search-1", "Found it.")],
        });
        thread.CompleteTurn();
        using var context = new AgentContext(new UIAgent(new ScriptedChatClient(), options => options.Thread = thread));

        await context.RestoreAsync();

        var calls = Assert.Single(context.Turns).ResponseBlocks
            .OfType<FunctionInvocationContentBlock>()
            .ToDictionary(call => call.Id);
        Assert.False(calls["search-1"].IsAbandoned);
        Assert.True(calls["search-2"].IsAbandoned);
    }

    [Fact]
    public async Task NextMessageSentFromTheIdleNotification_TheEndedTurnStillReportsItsCalls()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitUpdates(
            [
                new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new FunctionCallContent("search-1", "Search") { InformationalOnly = true }])
                {
                    MessageId = "assistant-1",
                },
            ],
            ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Next.", ct));
        using var context = new AgentContext(new UIAgent(model));
        FunctionInvocationContentBlock? call = null;
        List<bool>? abandonedChanges = null;
        context.OnBlockAdded((_, block) =>
        {
            if (block is FunctionInvocationContentBlock added)
            {
                call = added;
                abandonedChanges = RecordAbandoned(added);
            }
        });
        Task? next = null;
        var sent = false;
        context.OnStatusChanged(status =>
        {
            // The next turn may end before SendMessageAsync returns, so remember the send first.
            if (status == ConversationStatus.Idle && !sent)
            {
                sent = true;
                next = context.SendMessageAsync("Next");
            }
        });

        await context.SendMessageAsync("Search");
        await next!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, context.Turns.Count);
        Assert.True(call?.IsAbandoned);
        Assert.Equal([true], abandonedChanges);
    }

    private static AgentContext CreateDeleteFileContext(ScriptedChatClient model, List<string> deleted)
    {
        var deleteFile = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            (string fileName) =>
            {
                deleted.Add(fileName);
                return $"Deleted {fileName}";
            },
            "DeleteFile",
            "Deletes a file."));
        return new AgentContext(new UIAgent(
            new FunctionInvokingChatClient(model),
            options => options.ChatOptions = new ChatOptions { Tools = [deleteFile] }));
    }

    private static List<bool> RecordAbandoned(FunctionInvocationContentBlock call)
    {
        var changes = new List<bool>();
        call.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FunctionInvocationContentBlock.IsAbandoned))
                changes.Add(call.IsAbandoned);
        };
        return changes;
    }

    /// <summary>Records each change the block raises with the waiting and abandoned state it reads.</summary>
    private static List<string> RecordStates(InteractiveFunctionBlock block)
    {
        var changes = new List<string>();
        block.PropertyChanged += (_, e) => changes.Add(
            $"{e.PropertyName}: awaiting input {block.IsAwaitingInput}, abandoned {block.IsAbandoned}");
        return changes;
    }

    private static Task WhenAwaitingInput(AgentContext context)
    {
        var awaitingInput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                awaitingInput.TrySetResult();
        });
        return awaitingInput.Task;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CalculationThenErrorAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "response-1",
            Contents = [new FunctionCallContent("calc-1", "Calculate", Expression("2+2"))],
        };
        await Task.Yield();
        throw new InvalidOperationException("The connection was lost.");
    }

    private static Dictionary<string, object?> Expression(string expression) => new() { ["expression"] = expression };

    private static Dictionary<string, object?> FileName(string fileName) => new() { ["fileName"] = fileName };

    private static FunctionCallContent DeleteFileCall() => new("delete-1", "DeleteFile", FileName("a.txt"));

    private static FunctionCallContent ShowMapCall()
        => new("map-1", "ShowMap", new Dictionary<string, object?> { ["city"] = "Paris" });

    private static AIFunction CreateShowMap()
        => AIFunctionFactory.Create((string city) => $"Map of {city}", "ShowMap", "Shows a map.");

    private sealed class DeleteFileCard : FunctionInvocationContentBlock
    {
    }

    private sealed class Notice : ContentBlock
    {
    }

    private sealed class NoticeHandler : ContentBlockHandler<Notice>
    {
        public override BlockMappingResult<Notice> Handle(BlockMappingContext context, Notice state)
        {
            foreach (var content in context.UnhandledContents)
            {
                if (content is FunctionCallContent { Name: "Notify" } call)
                {
                    context.MarkHandled(call);
                    state.Id = call.CallId;
                    return BlockMappingResult<Notice>.Emit(state, state);
                }
            }

            return BlockMappingResult<Notice>.Pass();
        }
    }

    private sealed class DeleteFileCardHandler : ContentBlockHandler<DeleteFileCard>
    {
        public override BlockMappingResult<DeleteFileCard> Handle(BlockMappingContext context, DeleteFileCard state)
        {
            foreach (var content in context.UnhandledContents)
            {
                if (state.Call is null && content is FunctionCallContent { Name: "DeleteFile" } call)
                {
                    context.MarkHandled(call);
                    state.Call = call;
                    return BlockMappingResult<DeleteFileCard>.Emit(state, state);
                }

                if (state.Call is { } shown && content is FunctionResultContent result && result.CallId == shown.CallId)
                {
                    context.MarkHandled(result);
                    state.Result = result;
                    return BlockMappingResult<DeleteFileCard>.Complete();
                }
            }

            return BlockMappingResult<DeleteFileCard>.Pass();
        }
    }
}
