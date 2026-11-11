// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// Approvals and UI actions accept a decision or an invocation only while a turn can send it.
/// </summary>
public class PendingInteractionTests
{
    [Fact]
    public async Task FailedTurn_PendingApprovalAndManualActionNoLongerRespond()
    {
        var shown = new List<string>();
        var model = new ScriptedChatClient();
        model.Enqueue(ApprovalAndActionThenErrorAsync);
        using var context = new AgentContext(new UIAgent(model, options =>
            options.RegisterUIAction(CreateShowMap(shown), UIActionInvocationMode.Manual)));

        await context.SendMessageAsync("Delete the file and show Paris");

        Assert.Equal(ConversationStatus.Error, context.Status);
        var blocks = Assert.Single(context.Turns).ResponseBlocks;
        var approval = Assert.Single(blocks.OfType<ToolApprovalBlock>());
        var action = Assert.Single(blocks.OfType<UIActionBlock>());
        Assert.False(approval.IsAwaitingInput);
        Assert.False(action.IsAwaitingInput);

        approval.Approve();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action.InvokeAsync());

        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        Assert.Empty(shown);
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task RestoredPendingApprovalAndManualAction_NoLongerRespond()
    {
        var shown = new List<string>();
        using var context = new AgentContext(new UIAgent(new ScriptedChatClient(), options =>
            options.RegisterUIAction(CreateShowMap(shown), UIActionInvocationMode.Manual)));

        await context.RestoreAsync(
        [
            new ChatMessage(ChatRole.User, "Delete the file and show Paris"),
            new ChatMessage(ChatRole.Assistant, [DeleteFileApproval(), ShowMapCall()]),
        ]);

        var blocks = Assert.Single(context.Turns).ResponseBlocks;
        var approval = Assert.Single(blocks.OfType<ToolApprovalBlock>());
        var action = Assert.Single(blocks.OfType<UIActionBlock>());
        Assert.False(approval.IsAwaitingInput);
        Assert.False(action.IsAwaitingInput);

        approval.Approve();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action.InvokeAsync());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => approval.GetResultAsync());

        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        Assert.Empty(shown);
    }

    [Fact]
    public async Task RestoredDecidedApproval_KeepsItsDecision()
    {
        using var context = new AgentContext(new UIAgent(new ScriptedChatClient()));
        var request = DeleteFileApproval();

        await context.RestoreAsync(
        [
            new ChatMessage(ChatRole.User, "Delete the file"),
            new ChatMessage(ChatRole.Assistant, [request]),
            new ChatMessage(ChatRole.User, [request.CreateResponse(approved: true)]),
            new ChatMessage(ChatRole.Assistant, [request.ToolCall]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("delete-1", "Deleted")]),
        ]);

        var approval = Assert.Single(Assert.Single(context.Turns).ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.IsType<ToolApprovalResponseContent>(await approval.GetResultAsync());
    }

    [Fact]
    public async Task StoppingTheTurn_EndsTheWaitForInputAndReportsIt()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ApprovalAndActionAsync);
        using var context = new AgentContext(new UIAgent(model, options =>
            options.RegisterUIAction(CreateShowMap([]), UIActionInvocationMode.Manual)));
        var awaitingInput = WhenAwaitingInput(context);

        var send = context.SendMessageAsync("Delete the file and show Paris");
        await awaitingInput.WaitAsync(TimeSpan.FromSeconds(10));
        var blocks = context.Turns[^1].ResponseBlocks;
        var approval = Assert.Single(blocks.OfType<ToolApprovalBlock>());
        var action = Assert.Single(blocks.OfType<UIActionBlock>());
        Assert.True(approval.IsAwaitingInput);
        Assert.True(action.IsAwaitingInput);
        var approvalChanges = RecordAwaitingInput(approval);
        var actionChanges = RecordAwaitingInput(action);

        await context.CancelAsync();
        await send.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(approval.IsAwaitingInput);
        Assert.False(action.IsAwaitingInput);
        Assert.Equal([false], approvalChanges);
        Assert.Equal([false], actionChanges);
    }

    [Fact]
    public void DecidedApproval_NoLongerAwaitsInput()
    {
        var approved = CreateApproval();
        Assert.True(approved.IsAwaitingInput);
        approved.Approve();
        Assert.False(approved.IsAwaitingInput);

        var rejected = CreateApproval();
        rejected.Reject();
        Assert.False(rejected.IsAwaitingInput);

        // A decision that can no longer be sent leaves nothing to answer either.
        var abandoned = CreateApproval();
        Assert.True(abandoned.Abandon());
        Assert.False(abandoned.IsAwaitingInput);
        Assert.False(abandoned.Abandon());
        abandoned.Approve();
        Assert.Equal(ApprovalStatus.Pending, abandoned.Status);
        Assert.False(approved.Abandon());
    }

    [Fact]
    public async Task ManualAction_AwaitsInputUntilItRuns()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;
        var showMap = AIFunctionFactory.Create(
            async (string city, CancellationToken cancellationToken) =>
            {
                if (++invocations == 1)
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                else
                    await release.Task;
                return $"Map of {city}";
            },
            "ShowMap",
            "Shows a map.");
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, ShowMapCall()));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = new AgentContext(new UIAgent(model, options =>
            options.RegisterUIAction(showMap, UIActionInvocationMode.Manual)));
        var awaitingInput = WhenAwaitingInput(context);

        var send = context.SendMessageAsync("Show Paris");
        await awaitingInput.WaitAsync(TimeSpan.FromSeconds(10));
        var action = Assert.Single(context.Turns[^1].ResponseBlocks.OfType<UIActionBlock>());
        var changes = RecordAwaitingInput(action);
        Assert.True(action.IsAwaitingInput);

        // Running, then canceled by the app: it waits for input again.
        using var cancellation = new CancellationTokenSource();
        var canceled = action.InvokeAsync(cancellation.Token);
        Assert.False(action.IsAwaitingInput);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.True(action.IsAwaitingInput);

        // Running, then complete.
        var invocation = action.InvokeAsync();
        Assert.False(action.IsAwaitingInput);
        release.SetResult();
        await invocation.WaitAsync(TimeSpan.FromSeconds(10));
        await send.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(action.IsAwaitingInput);
        Assert.True(action.IsComplete);
        Assert.Equal([false, true, false], changes);
    }

    [Fact]
    public async Task AutomaticAction_NeverAwaitsInput()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, ShowMapCall()));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = new AgentContext(new UIAgent(model, options =>
            options.RegisterUIAction(CreateShowMap([]), UIActionInvocationMode.Automatic)));
        var awaitingInput = new List<bool>();
        context.OnBlockAdded((_, block) =>
        {
            if (block is UIActionBlock action)
                awaitingInput.Add(action.IsAwaitingInput);
        });

        await context.SendMessageAsync("Show Paris");

        Assert.Equal([false], awaitingInput);
        Assert.False(Assert.Single(context.Turns[0].ResponseBlocks.OfType<UIActionBlock>()).IsAwaitingInput);
    }

    [Fact]
    public void AppInteractiveBlock_DoesNotAwaitInputUnlessItSaysSo()
    {
        Assert.False(new AppInteractiveBlock(new FunctionInvocationContentBlock()).IsAwaitingInput);
    }

    [Fact]
    public async Task ManualActionInvocationCanceledByTheApp_CanBeInvokedAgain()
    {
        var invocations = 0;
        var showMap = AIFunctionFactory.Create(
            async (string city, CancellationToken cancellationToken) =>
            {
                if (++invocations == 1)
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                return $"Map of {city}";
            },
            "ShowMap",
            "Shows a map.");
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, ShowMapCall()));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = new AgentContext(new UIAgent(model, options =>
            options.RegisterUIAction(showMap, UIActionInvocationMode.Manual)));
        var awaitingInput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                awaitingInput.TrySetResult();
        });

        var send = context.SendMessageAsync("Show Paris");
        await awaitingInput.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var action = Assert.Single(context.Turns[^1].ResponseBlocks.OfType<UIActionBlock>());
        using var cancellation = new CancellationTokenSource();
        var canceled = action.InvokeAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);

        Assert.Equal(ConversationStatus.AwaitingInput, context.Status);
        Assert.False(action.IsComplete);

        var result = Assert.IsType<FunctionResultContent>(
            await action.InvokeAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        await send.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("Map of Paris", result.Result?.ToString());
        Assert.Equal(2, invocations);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal("Map of Paris", ToolHistory.FindResult(model.Requests[1], "map-1"));
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ApprovalAndActionAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "response-1",
            Contents = [DeleteFileApproval(), ShowMapCall()],
        };
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

    private static ToolApprovalBlock CreateApproval()
    {
        var call = new FunctionCallContent("delete-1", "DeleteFile");
        return new ToolApprovalBlock(new FunctionInvocationContentBlock { Call = call }, new ToolApprovalRequestContent("approval-1", call));
    }

    /// <summary>Records the value of <see cref="InteractiveFunctionBlock.IsAwaitingInput"/> each time it is raised.</summary>
    private static List<bool> RecordAwaitingInput(InteractiveFunctionBlock block)
    {
        var values = new List<bool>();
        block.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InteractiveFunctionBlock.IsAwaitingInput))
                values.Add(block.IsAwaitingInput);
        };
        return values;
    }

    private sealed class AppInteractiveBlock(FunctionInvocationContentBlock innerBlock) : InteractiveFunctionBlock(innerBlock);

    private static async IAsyncEnumerable<ChatResponseUpdate> ApprovalAndActionThenErrorAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "response-1",
            Contents = [DeleteFileApproval(), ShowMapCall()],
        };
        await Task.Yield();
        throw new InvalidOperationException("The connection was lost.");
    }

    private static ToolApprovalRequestContent DeleteFileApproval()
        => new("approval-1", new FunctionCallContent("delete-1", "DeleteFile"));

    private static FunctionCallContent ShowMapCall()
        => new("map-1", "ShowMap", new Dictionary<string, object?> { ["city"] = "Paris" });

    private static AIFunction CreateShowMap(List<string> shown)
        => AIFunctionFactory.Create(
            (string city) =>
            {
                shown.Add(city);
                return $"Map of {city}";
            },
            "ShowMap",
            "Shows a map.");
}
