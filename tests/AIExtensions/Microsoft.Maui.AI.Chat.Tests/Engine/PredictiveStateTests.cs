// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

public class PredictiveStateTests
{
    [Theory]
    [InlineData(true, "Complete draft")]
    [InlineData(false, "Original")]
    public async Task Confirmation_AcceptsOrRejectsPredictiveStateAndContinues(
        bool accepted,
        string expectedDocument)
    {
        var callCount = 0;
        List<ChatMessage>? continuationMessages = null;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            callCount++;
            if (callCount == 1)
                return EmitPredictionWithConfirmation(cancellationToken);

            continuationMessages = messages.ToList();
            return ResponseEmitters.EmitTextResponse("Reviewed.", cancellationToken);
        });

        UIAgent<DocumentState> agent = null!;

        string ConfirmChanges(bool accepted)
        {
            if (accepted)
                agent.State.AcceptPredictiveState();
            else
                agent.State.RejectPredictiveState();

            return accepted ? "Accepted." : "Rejected.";
        }

        agent = CreateAgent(client, ConfirmChanges);
        using (agent)
        using (var context = new AgentContext(agent))
        {
            var documents = new List<string>();
            agent.State.PropertyChanged += (_, e) => documents.Add($"{e.PropertyName}: {agent.State.Value.Document}");
            using var statusSubscription = context.OnStatusChanged(status =>
            {
                if (status == ConversationStatus.AwaitingInput)
                {
                    var action = context.Turns[^1].ResponseBlocks
                        .OfType<UIActionBlock>()
                        .Single();
                    action.Call!.Arguments ??= new Dictionary<string, object?>();
                    action.Call.Arguments["accepted"] = accepted;
                    _ = action.InvokeAsync();
                }
            });

            await context.SendMessageAsync("Edit the document");

            Assert.Equal(
                [
                    "Value: Draft",
                    "HasPendingPredictiveState: Draft",
                    "Value: Complete draft",
                    .. accepted
                        ? (string[])["HasPendingPredictiveState: Complete draft"]
                        : ["Value: Original", "HasPendingPredictiveState: Original"],
                ],
                documents);
            Assert.Equal(expectedDocument, agent.State.Value.Document);
            Assert.False(agent.State.HasPendingPredictiveState);
            Assert.Equal(2, callCount);
            Assert.NotNull(continuationMessages);
            var result = Assert.IsType<FunctionResultContent>(
                Assert.Single(continuationMessages.Last().Contents));
            Assert.Equal(accepted ? "Accepted." : "Rejected.", result.Result?.ToString());
        }
    }

    [Fact]
    public async Task CallerCancellation_RollsBackPredictiveStateAndCancelsTask()
    {
        var stateObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((_, _, cancellationToken) =>
            EmitPredictionUntilCanceled(stateObserved, cancellationToken));
        using var agent = CreateAgent(client);
        using var context = new AgentContext(agent);
        using var cancellationSource = new CancellationTokenSource();

        var sendTask = context.SendMessageAsync(
            "Edit the document",
            cancellationSource.Token);
        await stateObserved.Task;
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sendTask);
        Assert.Equal("Original", agent.State.Value.Document);
        Assert.False(agent.State.HasPendingPredictiveState);
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    private static UIAgent<DocumentState> CreateAgent(
        IChatClient client,
        Func<bool, string>? confirm = null)
    {
        return new UIAgent<DocumentState>(client, options =>
        {
            options.StateMapper = context =>
            {
                if (context.Update.RawRepresentation is not DocumentState state)
                    return;

                context.SetPredictiveState(state);
            };
            if (confirm is not null)
            {
                options.RegisterUIAction(
                    AIFunctionFactory.Create(
                        confirm,
                        "confirm_changes",
                        "Confirm the document changes."),
                    UIActionInvocationMode.Manual);
            }
        }, new DocumentState { Document = "Original" });
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitPredictionWithConfirmation(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return CreateStateUpdate("Draft");
        yield return CreateStateUpdate("Complete draft");
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "confirmation-message",
            Contents = [new FunctionCallContent("confirmation-call", "confirm_changes")],
            FinishReason = ChatFinishReason.ToolCalls,
        };
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitPredictionUntilCanceled(
        TaskCompletionSource stateObserved,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return CreateStateUpdate("Draft");
        stateObserved.SetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private static ChatResponseUpdate CreateStateUpdate(string document) => new()
    {
        Role = ChatRole.Assistant,
        RawRepresentation = new DocumentState { Document = document },
    };

    private sealed class DocumentState
    {
        public string Document { get; set; } = string.Empty;
    }
}
