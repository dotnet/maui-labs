// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// <see cref="AgentState{T}"/> raises <see cref="INotifyPropertyChanged.PropertyChanged"/> for its value and its
/// pending prediction once they changed, before its <c>OnChanged</c> callbacks, so a view can bind to the typed
/// state.
/// </summary>
public class AgentStatePropertyChangedTests
{
    [Fact]
    public async Task ServerSnapshot_RaisesValueOnceTheStateIsReplaced()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ReplyAsync(ct, Snapshot("Pasta")));
        using var agent = CreateAgent(model);
        using var context = new AgentContext(agent);
        var log = Watch(agent.State);

        await context.SendMessageAsync("Recipe?");

        Assert.Equal(["Value: Pasta, pending False", "OnChanged: Pasta, pending False"], log);
    }

    [Fact]
    public void ServerSnapshot_ReplacingAPrediction_RaisesValueAndThePendingPrediction()
    {
        var state = new AgentState<NoteState>(new NoteState { Title = "Original" });
        state.SetPredictiveValue(new NoteState { Title = "Draft" });
        var log = Watch(state);

        state.Value = new NoteState { Title = "Final" };

        Assert.Equal(
            [
                "Value: Final, pending False",
                "HasPendingPredictiveState: Final, pending False",
                "OnChanged: Final, pending False",
            ],
            log);
    }

    [Fact]
    public void Value_AssignedTheSameInstanceAgain_RaisesValueSoBindingsRefresh()
    {
        var state = new AgentState<NoteState>(new NoteState { Title = "Soup" });
        var log = Watch(state);

        state.Value.Title = "Stew";
        state.Value = state.Value;

        Assert.Equal(["Value: Stew, pending False", "OnChanged: Stew, pending False"], log);
    }

    [Fact]
    public async Task PredictiveUpdates_RaiseTheValueAndThePendingPrediction_AndTheRollBackWhenTheTurnEnds()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ReplyAsync(ct, Prediction("Draft 1"), Prediction("Draft 2")));
        using var agent = CreateAgent(model);
        using var context = new AgentContext(agent);
        var log = Watch(agent.State);
        context.RegisterOnStatusChanged(status =>
        {
            if (status == ConversationStatus.Idle)
                log.Add("Idle");
        });

        await context.SendMessageAsync("Rename it");

        Assert.Equal(
            [
                "Value: Draft 1, pending True",
                "HasPendingPredictiveState: Draft 1, pending True",
                "OnChanged: Draft 1, pending True",
                "Value: Draft 2, pending True",
                "OnChanged: Draft 2, pending True",
                "Value: Original, pending False",
                "HasPendingPredictiveState: Original, pending False",
                "OnChanged: Original, pending False",
                "Idle",
            ],
            log);
    }

    [Fact]
    public void Accept_RaisesOnlyThePendingPrediction()
    {
        var state = new AgentState<NoteState>(new NoteState { Title = "Original" });
        state.SetPredictiveValue(new NoteState { Title = "Draft" });
        var log = Watch(state);

        state.AcceptPredictiveState();
        state.AcceptPredictiveState();

        Assert.Equal(
            ["HasPendingPredictiveState: Draft, pending False", "OnChanged: Draft, pending False"],
            log);
    }

    [Fact]
    public void Reject_RaisesTheRestoredValueAndThePendingPrediction()
    {
        var state = new AgentState<NoteState>(new NoteState { Title = "Original" });
        state.SetPredictiveValue(new NoteState { Title = "Draft" });
        var log = Watch(state);

        state.RejectPredictiveState();
        state.RejectPredictiveState();

        Assert.Equal(
            [
                "Value: Original, pending False",
                "HasPendingPredictiveState: Original, pending False",
                "OnChanged: Original, pending False",
            ],
            log);
    }

    [Fact]
    public async Task PredictionAcceptedByAPropertyChangedHandler_IsKeptWhenTheTurnEnds()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ReplyAsync(ct, Prediction("Draft")));
        using var agent = CreateAgent(model);
        using var context = new AgentContext(agent);
        agent.State.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentState<NoteState>.HasPendingPredictiveState)
                && agent.State.HasPendingPredictiveState)
            {
                agent.State.AcceptPredictiveState();
            }
        };
        var pending = new List<bool>();
        agent.State.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentState<NoteState>.HasPendingPredictiveState))
                pending.Add(agent.State.HasPendingPredictiveState);
        };

        await context.SendMessageAsync("Rename it");

        // The accept ran inside the first handler, so the later one only hears that nothing is pending.
        Assert.Equal("Draft", agent.State.Value.Title);
        Assert.False(agent.State.HasPendingPredictiveState);
        Assert.Equal([false, false], pending);
    }

    [Fact]
    public async Task Restore_RaisesTheRestoredState()
    {
        var thread = new InMemoryConversationThread("thread-state");
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ReplyAsync(ct, Snapshot("Pasta")));
        using (var liveAgent = CreateAgent(model, thread))
        using (var live = new AgentContext(liveAgent))
            await live.SendMessageAsync("Recipe?");

        using var agent = CreateAgent(new ScriptedChatClient(), thread);
        using var context = new AgentContext(agent);
        var log = Watch(agent.State);

        await context.RestoreAsync();

        Assert.Equal(["Value: Pasta, pending False", "OnChanged: Pasta, pending False"], log);
    }

    [Fact]
    public async Task FailedRestore_RaisesTheStateItPutsBack()
    {
        var thread = new InMemoryConversationThread("thread-failure");
        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "Recipe?"));
        thread.AppendUpdate(Snapshot("Restored"));
        thread.AppendUpdate(new ChatResponseUpdate(ChatRole.Assistant, "throw") { MessageId = "m1" });
        thread.CompleteTurn();
        using var agent = CreateAgent(new ScriptedChatClient(), thread);
        agent.State.Value = new NoteState { Title = "Previous" };
        using var context = new AgentContext(agent);
        var log = Watch(agent.State);

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.RestoreAsync());

        Assert.Equal(
            [
                "Value: Original, pending False",
                "OnChanged: Original, pending False",
                "Value: Restored, pending False",
                "OnChanged: Restored, pending False",
                "Value: Previous, pending False",
                "OnChanged: Previous, pending False",
            ],
            log);
    }

    [Fact]
    public async Task Clear_RaisesTheInitialValue()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ReplyAsync(ct, Snapshot("Pasta")));
        using var agent = CreateAgent(model);
        using var context = new AgentContext(agent);
        await context.SendMessageAsync("Recipe?");
        var log = Watch(agent.State);

        await context.ClearAsync();

        Assert.Equal(["Value: Original, pending False", "OnChanged: Original, pending False"], log);
    }

    [Fact]
    public async Task StateHandler_CanSendTheFirstMessageOfAClearedConversation()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ReplyAsync(ct, Snapshot("Pasta")));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Hello again", ct));
        using var agent = CreateAgent(model);
        using var context = new AgentContext(agent);
        await context.SendMessageAsync("Recipe?");
        Task? next = null;
        agent.State.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentState<NoteState>.Value) && agent.State.Value.Title == "Original")
                next ??= context.SendMessageAsync("Start over");
        };

        await context.ClearAsync();
        Assert.NotNull(next);
        await next;

        // The new turn starts the cleared conversation: the clear neither removes it nor sends the old history.
        var turn = Assert.Single(context.Turns);
        Assert.Equal("Hello again", Assert.IsType<TextContentBlock>(Assert.Single(turn.ResponseBlocks)).RawText);
        Assert.Equal(["user:Start over"], Describe(model.Requests[^1]));
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    [Fact]
    public async Task StateHandler_CannotSendWhileARestoreReplaysTheState()
    {
        var thread = new InMemoryConversationThread("thread-state");
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ReplyAsync(ct, Snapshot("Pasta")));
        using (var liveAgent = CreateAgent(model, thread))
        using (var live = new AgentContext(liveAgent))
            await live.SendMessageAsync("Recipe?");

        var next = new ScriptedChatClient();
        next.Enqueue(ct => ResponseEmitters.EmitTextResponse("Two", ct));
        using var agent = CreateAgent(next, thread);
        using var context = new AgentContext(agent);
        Task? nested = null;
        agent.State.PropertyChanged += (_, _) => nested ??= context.SendMessageAsync("too early");

        await context.RestoreAsync();

        // The restore is not finished while it replays the state, so the conversation cannot continue yet.
        Assert.NotNull(nested);
        await Assert.ThrowsAsync<InvalidOperationException>(() => nested);
        Assert.Empty(next.Requests);
        Assert.Single(context.Turns);
        Assert.Equal("Pasta", agent.State.Value.Title);

        await context.SendMessageAsync("second");

        Assert.Equal(["user:Recipe?", "assistant:Done", "user:second"], Describe(next.Requests[^1]));
        Assert.Equal(2, context.Turns.Count);
    }

    [Fact]
    public async Task StateHandler_CanClearWhileARestoreReplaysTheState()
    {
        var thread = new InMemoryConversationThread("thread-state");
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ReplyAsync(ct, Snapshot("Pasta")));
        using (var liveAgent = CreateAgent(model, thread))
        using (var live = new AgentContext(liveAgent))
            await live.SendMessageAsync("Recipe?");

        var next = new ScriptedChatClient();
        next.Enqueue(ct => ResponseEmitters.EmitTextResponse("Fresh", ct));
        using var agent = CreateAgent(next, thread);
        using var context = new AgentContext(agent);
        Task? clear = null;
        agent.State.PropertyChanged += (_, _) => clear ??= context.ClearAsync();

        await context.RestoreAsync();
        Assert.NotNull(clear);
        await clear;

        // The clear waited for the restore, so it removed the restored conversation instead of being undone by it.
        Assert.Empty(context.Turns);
        Assert.Equal("Original", agent.State.Value.Title);
        Assert.Empty(thread.GetUpdates());

        await context.SendMessageAsync("Hi");

        Assert.Equal(["user:Hi"], Describe(next.Requests[^1]));
    }

    private static List<string> Describe(IReadOnlyList<ChatMessage> request)
        => request.Select(message => $"{message.Role}:{message.Text}").ToList();

    private static UIAgent<NoteState> CreateAgent(IChatClient client, IConversationThread? thread = null)
        => new(
            client,
            options =>
            {
                options.Thread = thread;
                options.StateMapper = context =>
                {
                    if (context.Update.RawRepresentation is not StateReport report)
                    {
                        if (context.Update.Text == "throw")
                            throw new InvalidOperationException("The mapper failed.");

                        return;
                    }

                    var value = new NoteState { Title = report.Title };
                    if (report.Predictive)
                        context.SetPredictiveState(value);
                    else
                        context.SetState(value);
                };
            },
            new NoteState { Title = "Original" });

    /// <summary>Records the state's property changes and callbacks, reading the state each one reports.</summary>
    private static List<string> Watch(AgentState<NoteState> state)
    {
        var log = new List<string>();
        state.PropertyChanged += (_, e) =>
            log.Add($"{e.PropertyName}: {state.Value.Title}, pending {state.HasPendingPredictiveState}");
        state.OnChanged(() => log.Add($"OnChanged: {state.Value.Title}, pending {state.HasPendingPredictiveState}"));
        return log;
    }

    private static ChatResponseUpdate Snapshot(string title)
        => new() { Role = ChatRole.Assistant, MessageId = "m1", RawRepresentation = new StateReport(title, Predictive: false) };

    private static ChatResponseUpdate Prediction(string title)
        => new() { Role = ChatRole.Assistant, MessageId = "m1", RawRepresentation = new StateReport(title, Predictive: true) };

    private static async IAsyncEnumerable<ChatResponseUpdate> ReplyAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken,
        params ChatResponseUpdate[] states)
    {
        foreach (var state in states)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return state;
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, "Done") { MessageId = "m1" };
        await Task.CompletedTask;
    }

    private sealed record StateReport(string Title, bool Predictive);

    private sealed class NoteState
    {
        public string Title { get; set; } = string.Empty;
    }
}
