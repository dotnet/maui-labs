// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// A UI action that is still running when its turn stops, and returns afterwards, cannot send its result,
/// so its block must not show one either.
/// </summary>
public class LateUIActionResultTests
{
    private const int ResultAfterTurnEndedEventId = 108;

    public enum ActionKind
    {
        Manual,
        Automatic,
        Approved,
    }

    [Theory]
    [InlineData(ActionKind.Manual)]
    [InlineData(ActionKind.Automatic)]
    [InlineData(ActionKind.Approved)]
    public async Task ActionReturningAfterStop_ShowsNoResultAndSendsNothing(ActionKind kind)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AIFunction pickPhoto = AIFunctionFactory.Create(
            async () =>
            {
                started.TrySetResult();

                // Like a picker that keeps its sheet open, the action does not observe cancellation.
                await release.Task;
                return "photo.jpg";
            },
            "PickPhoto",
            "Lets the user pick a photo.");
        if (kind == ActionKind.Approved)
            pickPhoto = new ApprovalRequiredAIFunction(pickPhoto);

        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(
            ct,
            new FunctionCallContent("photo-1", "PickPhoto")));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Hello.", ct));
        var logs = new CapturingLoggerFactory();
        using var context = new AgentContext(new UIAgent(
            new FunctionInvokingChatClient(model),
            options => options.RegisterUIAction(
                pickPhoto,
                kind == ActionKind.Automatic ? UIActionInvocationMode.Automatic : UIActionInvocationMode.Manual),
            logs));
        Task<AIContent>? invocation = null;
        context.OnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;

            var blocks = context.Turns[^1].ResponseBlocks;
            if (kind == ActionKind.Approved)
                Assert.Single(blocks.OfType<ToolApprovalBlock>()).Approve();
            else
                invocation = Assert.Single(blocks.OfType<UIActionBlock>()).InvokeAsync();
        });

        var send = context.SendMessageAsync("Pick a photo");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await context.CancelAsync();
        await send.WaitAsync(TimeSpan.FromSeconds(10));

        var turn = Assert.Single(context.Turns);
        Assert.True(turn.IsStopped);
        InteractiveFunctionBlock card = kind == ActionKind.Approved
            ? Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>())
            : Assert.Single(turn.ResponseBlocks.OfType<UIActionBlock>());
        var action = card as UIActionBlock ?? ((ToolApprovalBlock)card).UIAction!;
        Assert.True(card.IsAbandoned);
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        action.PropertyChanged += (_, _) => settled.TrySetResult();
        logs.Logged += entry =>
        {
            if (entry.EventId.Id == ResultAfterTurnEndedEventId)
                settled.TrySetResult();
        };

        release.SetResult();
        await settled.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(card.Result);
        Assert.False(card.HasResult);
        Assert.True(card.IsAbandoned);
        Assert.False(card.IsAwaitingInput);
        Assert.False(action.IsComplete);
        if (kind == ActionKind.Approved)
            Assert.Equal(ApprovalStatus.Approved, ((ToolApprovalBlock)card).Status);
        if (invocation is not null)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation);

        await context.SendMessageAsync("Hi");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(
            ["user:Pick a photo", "user:Hi"],
            model.Requests[^1].Select(message => $"{message.Role}:{message.Text}"));
        Assert.Null(card.Result);
    }
}
