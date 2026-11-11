// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// The thread keeps the calls a turn left without a result, so a restore shows them as the turn ended them
/// (<see cref="FunctionInvocationContentBlock.IsAbandoned"/>). The history sent to the model still leaves them and
/// a stopped turn's approvals out, live and restored, so the next request is the same and stays valid.
/// </summary>
public class UnansweredCallRestoreTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task StoppedWhileACallRuns_RestoreShowsTheCallAbandoned_AndTheNextRequestLeavesItOut()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitToolCallResponse("calc-1", "Calculate", Expression("2+2"), ct));
        using var live = CreateContext(model, thread, SlowCalculate(started));

        var send = live.SendMessageAsync("What is 2+2?");
        await started.Task.WaitAsync(s_timeout);
        await live.CancelAsync();
        await send.WaitAsync(s_timeout);
        Assert.True(Assert.Single(live.Turns[^1].ResponseBlocks.OfType<FunctionInvocationContentBlock>()).IsAbandoned);

        var restoredModel = new ScriptedChatClient();
        using var restored = CreateContext(restoredModel, thread, SlowCalculate(started));
        await restored.RestoreAsync();

        var turn = Assert.Single(restored.Turns);
        Assert.True(turn.IsStopped);
        var call = Assert.Single(turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>());
        Assert.Equal("calc-1", call.Call?.CallId);
        Assert.False(call.HasResult);
        Assert.True(call.IsAbandoned);

        await AssertNextRequestsAsync(live, model, restored, restoredModel, ["user: What is 2+2?", "user: Hi"]);
    }

    [Fact]
    public async Task StoppedWhileACallAfterReasoningRuns_RestoreShowsBoth_AndTheNextRequestLeavesOutTheCallAndTheCutOffReasoning()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitUpdates(
            [
                new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    MessageId = "assistant-1",
                    Contents =
                    [
                        new TextReasoningContent("Let me calculate."),
                        new FunctionCallContent("calc-1", "Calculate", Expression("2+2")),
                    ],
                },
            ],
            ct));
        using var live = CreateContext(model, thread, SlowCalculate(started));

        var send = live.SendMessageAsync("What is 2+2?");
        await started.Task.WaitAsync(s_timeout);
        await live.CancelAsync();
        await send.WaitAsync(s_timeout);

        var restoredModel = new ScriptedChatClient();
        using var restored = CreateContext(restoredModel, thread, SlowCalculate(started));
        await restored.RestoreAsync();

        var blocks = Assert.Single(restored.Turns).ResponseBlocks;
        Assert.Equal("Let me calculate.", Assert.Single(blocks.OfType<ReasoningContentBlock>()).Text);
        Assert.True(Assert.Single(blocks.OfType<FunctionInvocationContentBlock>()).IsAbandoned);

        // Without the call the reasoning ends the stopped turn, so the history leaves it out too, live and restored.
        await AssertNextRequestsAsync(live, model, restored, restoredModel, ["user: What is 2+2?", "user: Hi"]);
    }

    [Fact]
    public async Task StoppedWhileAnApprovalWaits_RestoreShowsTheApprovalAbandoned_AndTheNextRequestLeavesItOut()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var deleted = new List<string>();
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitToolCallResponse("delete-1", "DeleteFile", FileName("a.txt"), ct));
        using var live = CreateContext(model, thread, DeleteFile(deleted));
        var awaitingInput = WhenAwaitingInput(live);

        var send = live.SendMessageAsync("Delete a.txt");
        await awaitingInput.WaitAsync(s_timeout);
        await live.CancelAsync();
        await send.WaitAsync(s_timeout);

        var restoredModel = new ScriptedChatClient();
        using var restored = CreateContext(restoredModel, thread, DeleteFile(deleted));
        await restored.RestoreAsync();

        var turn = Assert.Single(restored.Turns);
        Assert.True(turn.IsStopped);
        var approval = Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal("delete-1", approval.Call?.CallId);
        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        Assert.False(approval.IsAwaitingInput);
        Assert.True(approval.IsAbandoned);
        Assert.True(approval.InnerBlock.IsAbandoned);
        Assert.Same(approval, approval.InnerBlock.Approval);

        // FunctionInvokingChatClient would reject an approval request that has no response.
        await AssertNextRequestsAsync(live, model, restored, restoredModel, ["user: Delete a.txt", "user: Hi"]);
        Assert.Empty(deleted);
    }

    [Fact]
    public async Task StoppedWhileAnApprovedCallRuns_RestoreShowsTheApprovalApprovedAndTheCallAbandoned_AndNothingRunsAgain()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;
        var calculate = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            async (string expression, CancellationToken cancellationToken) =>
            {
                invocations++;
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return "4";
            },
            "Calculate",
            "Calculates an expression."));
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitToolCallResponse("calc-1", "Calculate", Expression("2+2"), ct));
        using var live = CreateContext(model, thread, calculate);
        live.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentContext.Status) && live.Status == ConversationStatus.AwaitingInput)
                live.Turns[^1].ResponseBlocks.OfType<ToolApprovalBlock>().Single().Approve();
        };

        var send = live.SendMessageAsync("What is 2+2?");
        await started.Task.WaitAsync(s_timeout);
        await live.CancelAsync();
        await send.WaitAsync(s_timeout);
        var liveApproval = Assert.Single(live.Turns[^1].ResponseBlocks.OfType<ToolApprovalBlock>());

        var restoredModel = new ScriptedChatClient();
        using var restored = CreateContext(restoredModel, thread, calculate);
        await restored.RestoreAsync();

        var turn = Assert.Single(restored.Turns);
        Assert.True(turn.IsStopped);
        var approval = Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Equal(liveApproval.Status, approval.Status);
        Assert.False(approval.InnerBlock.HasResult);
        Assert.True(approval.InnerBlock.IsAbandoned);
        Assert.Equal(liveApproval.IsAbandoned, approval.IsAbandoned);

        // FunctionInvokingChatClient would run an approved call that has no result before sending the request.
        await AssertNextRequestsAsync(live, model, restored, restoredModel, ["user: What is 2+2?", "user: Hi"]);
        Assert.Equal(1, invocations);
    }

    [Fact]
    public async Task CompletedTurnWithACallLeftWithoutAResult_RestoreShowsTheCallAbandoned_AndTheNextRequestLeavesItOut()
    {
        var thread = new InMemoryConversationThread("thread-1");
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
        using var live = new AgentContext(new UIAgent(model, options => options.Thread = thread));
        await live.SendMessageAsync("Search");

        var restoredModel = new ScriptedChatClient();
        using var restored = new AgentContext(new UIAgent(restoredModel, options => options.Thread = thread));
        await restored.RestoreAsync();

        var turn = Assert.Single(restored.Turns);
        Assert.False(turn.IsStopped);
        var calls = turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>().ToDictionary(call => call.Id);
        Assert.Equal(["search-1", "search-2"], calls.Keys.Order());
        Assert.False(calls["search-1"].IsAbandoned);
        Assert.False(calls["search-2"].HasResult);
        Assert.True(calls["search-2"].IsAbandoned);

        await AssertNextRequestsAsync(
            live,
            model,
            restored,
            restoredModel,
            ["user: Search", "assistant: call search-1", "tool: result search-1", "assistant: Done.", "user: Hi"]);
    }

    [Fact]
    public async Task StoppedTurnWithAnAnsweredCall_RestoreKeepsTheCallWithItsResult()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitUpdates(
            [
                new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    MessageId = "assistant-1",
                    Contents = [new FunctionCallContent("search-1", "Search") { InformationalOnly = true }],
                },
                new ChatResponseUpdate
                {
                    Role = ChatRole.Tool,
                    MessageId = "tool-1",
                    Contents = [new FunctionResultContent("search-1", "Found it.")],
                },
            ],
            ct));
        model.Enqueue(ct => ResponseEmitters.EmitToolCallResponse("calc-1", "Calculate", Expression("2+2"), ct));
        using var live = CreateContext(model, thread, SlowCalculate(started));
        await live.SendMessageAsync("Search");

        var send = live.SendMessageAsync("What is 2+2?");
        await started.Task.WaitAsync(s_timeout);
        await live.CancelAsync();
        await send.WaitAsync(s_timeout);

        var restoredModel = new ScriptedChatClient();
        using var restored = CreateContext(restoredModel, thread, SlowCalculate(started));
        await restored.RestoreAsync();

        Assert.Collection(
            restored.Turns,
            turn =>
            {
                var call = Assert.Single(turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>());
                Assert.True(call.HasResult);
                Assert.False(call.IsAbandoned);
            },
            turn =>
            {
                Assert.True(turn.IsStopped);
                Assert.True(Assert.Single(turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>()).IsAbandoned);
            });
        await AssertNextRequestsAsync(
            live,
            model,
            restored,
            restoredModel,
            ["user: Search", "assistant: call search-1", "tool: result search-1", "user: What is 2+2?", "user: Hi"]);
    }

    /// <summary>
    /// Sends the next message from the live session and from the restored one, and asserts that both send the same
    /// valid history.
    /// </summary>
    private static async Task AssertNextRequestsAsync(
        AgentContext live,
        ScriptedChatClient liveModel,
        AgentContext restored,
        ScriptedChatClient restoredModel,
        string[] expected)
    {
        liveModel.Enqueue(ct => ResponseEmitters.EmitTextResponse("Hello.", ct));
        restoredModel.Enqueue(ct => ResponseEmitters.EmitTextResponse("Hello.", ct));

        await live.SendMessageAsync("Hi");
        await restored.SendMessageAsync("Hi");

        Assert.Equal(ConversationStatus.Idle, live.Status);
        Assert.Equal(ConversationStatus.Idle, restored.Status);
        Assert.Equal(expected, Describe(liveModel.Requests[^1]));
        Assert.Equal(expected, Describe(Assert.Single(restoredModel.Requests)));
        ToolHistory.AssertValid(restoredModel.Requests[^1]);
    }

    private static string[] Describe(IReadOnlyList<ChatMessage> request)
        => request
            .Select(message => $"{message.Role}: {string.Join(", ", message.Contents.Select(Describe))}")
            .ToArray();

    private static string Describe(AIContent content) => content switch
    {
        TextContent text => text.Text,
        FunctionCallContent call => $"call {call.CallId}",
        FunctionResultContent result => $"result {result.CallId}",
        ToolApprovalRequestContent request => $"approval request {request.RequestId}",
        ToolApprovalResponseContent response => $"approval response {response.RequestId}",
        _ => content.GetType().Name,
    };

    private static AgentContext CreateContext(ScriptedChatClient model, IConversationThread thread, AIFunction tool)
        => new(new UIAgent(new FunctionInvokingChatClient(model), options =>
        {
            options.ChatOptions = new ChatOptions { Tools = [tool] };
            options.Thread = thread;
        }));

    private static AIFunction SlowCalculate(TaskCompletionSource started)
        => AIFunctionFactory.Create(
            async (string expression, CancellationToken cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return "4";
            },
            "Calculate",
            "Calculates an expression.");

    private static AIFunction DeleteFile(List<string> deleted)
        => new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            (string fileName) =>
            {
                deleted.Add(fileName);
                return $"Deleted {fileName}";
            },
            "DeleteFile",
            "Deletes a file."));

    private static Task WhenAwaitingInput(AgentContext context)
    {
        var awaitingInput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentContext.Status) && context.Status == ConversationStatus.AwaitingInput)
                awaitingInput.TrySetResult();
        };
        return awaitingInput.Task;
    }

    private static Dictionary<string, object?> Expression(string expression) => new() { ["expression"] = expression };

    private static Dictionary<string, object?> FileName(string fileName) => new() { ["fileName"] = fileName };
}
