// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// <see cref="AgentContext"/>, its turns and their block collections raise the standard property and collection
/// change events, once the state they report is in place, so a XAML list can bind to them directly.
/// </summary>
public class BindableConversationTests
{
    [Fact]
    public async Task SendingAMessage_RaisesTheTurnItsBlocksAndTheStatusInOrder()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultiTokenTextResponse(ct, "Hel", "lo"));
        using var context = new AgentContext(new UIAgent(model));
        var log = Watch(context);

        await context.SendMessageAsync("Hi");

        Assert.Equal(
            [
                "Turns Add 0 (1 turns)",
                "Status=Streaming",
                "RequestBlocks Add 0 TextContentBlock",
                "ResponseBlocks Add 0 TextContentBlock",
                "Status=Idle",
            ],
            log);
    }

    [Fact]
    public async Task SecondMessage_AddsItsTurnAfterTheFirst()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("One", ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Two", ct));
        using var context = new AgentContext(new UIAgent(model));
        var added = new List<string>();
        Observe(context.Turns).CollectionChanged += (_, e) =>
            added.Add($"{e.Action} {e.NewStartingIndex} {ReferenceEquals(e.NewItems![0], context.Turns[e.NewStartingIndex])}");

        await context.SendMessageAsync("first");
        await context.SendMessageAsync("second");

        Assert.Equal(["Add 0 True", "Add 1 True"], added);
    }

    [Fact]
    public async Task TurnMetadata_IsRaisedWhileTheResponseStreams()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitUpdates(
            [
                new ChatResponseUpdate(ChatRole.Assistant, "Hello") { MessageId = "m1", ModelId = "model-a" },
                new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { TotalTokenCount = 40 })])
                {
                    MessageId = "m1",
                    ModelId = "model-b",
                },
                new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { TotalTokenCount = 2 })])
                {
                    MessageId = "m1",
                },
            ],
            ct));
        using var context = new AgentContext(new UIAgent(model));
        var log = Watch(context);

        await context.SendMessageAsync("Hi");

        Assert.Equal(
            [
                "Turns Add 0 (1 turns)",
                "Status=Streaming",
                "RequestBlocks Add 0 TextContentBlock",
                "turn.ModelId=model-a (Streaming)",
                "ResponseBlocks Add 0 TextContentBlock",
                "turn.ModelId=model-b (Streaming)",
                "turn.Usage=40 (Streaming)",
                "turn.Usage=42 (Streaming)",
                "Status=Idle",
            ],
            log);
    }

    [Fact]
    public async Task StoppedTurn_RaisesIsStoppedOnceTheConversationIsIdle()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => TextThenWaitAsync("Partial", ct));
        using var context = new AgentContext(new UIAgent(model));
        var streamed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.OnBlockAdded((_, block) =>
        {
            if (block.Role == ChatRole.Assistant)
                streamed.TrySetResult();
        });
        var log = Watch(context);

        var send = context.SendMessageAsync("Hi");
        await streamed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        log.Clear();
        await context.CancelAsync();
        await send;

        Assert.Equal(["turn.IsStopped=True (Idle)", "Status=Idle"], log);
    }

    [Fact]
    public async Task FailedTurn_RaisesIsFailedAndError_AndRetryClearsTheTurnBeforeItStreamsAgain()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitErrorAfterTokens(["Partial"], new InvalidOperationException("offline"), ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Recovered", ct));
        using var context = new AgentContext(new UIAgent(model));
        var log = Watch(context);

        await context.SendMessageAsync("Hi");

        Assert.Equal(
            [
                "Turns Add 0 (1 turns)",
                "Status=Streaming",
                "RequestBlocks Add 0 TextContentBlock",
                "ResponseBlocks Add 0 TextContentBlock",
                "turn.IsFailed=True (Error)",
                "Error=offline",
                "Status=Error",
            ],
            log);

        log.Clear();
        await context.RetryAsync();

        Assert.Equal(
            [
                "ResponseBlocks Reset",
                "turn.IsFailed=False (Streaming)",
                "Error=none",
                "Status=Streaming",
                "ResponseBlocks Add 0 TextContentBlock",
                "Status=Idle",
            ],
            log);
    }

    [Fact]
    public async Task Retry_ClearsTheMetadataOfTheFailedAttempt()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => FailAfterAsync(
            new ChatResponseUpdate(ChatRole.Assistant, "Partial") { MessageId = "m1", ModelId = "model-a" },
            ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Recovered", ct));
        using var context = new AgentContext(new UIAgent(model));
        await context.SendMessageAsync("Hi");
        var turn = Assert.Single(context.Turns);
        Assert.Equal("model-a", turn.ModelId);
        var log = new List<string>();
        Observe(turn.ResponseBlocks).CollectionChanged += (_, e) => log.Add($"{e.Action} ({turn.ResponseBlocks.Count} blocks, {turn.ModelId ?? "no model"})");
        turn.PropertyChanged += (_, e) => log.Add($"{e.PropertyName} ({turn.ResponseBlocks.Count} blocks, {turn.ModelId ?? "no model"})");

        await context.RetryAsync();

        Assert.Equal(
            [
                "Reset (0 blocks, no model)",
                "ModelId (0 blocks, no model)",
                "IsFailed (0 blocks, no model)",
                "Add (1 blocks, no model)",
            ],
            log);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_RaisesOneResetOnceEveryTurnIsLoaded(bool fromThread)
    {
        var thread = fromThread ? new InMemoryConversationThread("thread-1") : null;
        if (thread is not null)
        {
            var model = new ScriptedChatClient();
            model.Enqueue(ct => ResponseEmitters.EmitTextResponse("One", ct));
            model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Two", ct));
            using var live = new AgentContext(new UIAgent(model, options => options.Thread = thread));
            await live.SendMessageAsync("first");
            await live.SendMessageAsync("second");
        }

        using var context = new AgentContext(new UIAgent(
            new ScriptedChatClient(),
            options => options.Thread = thread));
        var log = new List<string>();
        Observe(context.Turns).CollectionChanged += (_, e) => log.Add(
            $"{e.Action} ({context.Turns.Count} turns, "
            + $"{context.Turns.Sum(turn => turn.RequestBlocks.Count + turn.ResponseBlocks.Count)} blocks)");

        if (thread is not null)
        {
            await context.RestoreAsync();
        }
        else
        {
            await context.RestoreAsync(
            [
                new ChatMessage(ChatRole.User, "first"),
                new ChatMessage(ChatRole.Assistant, "One"),
                new ChatMessage(ChatRole.User, "second"),
                new ChatMessage(ChatRole.Assistant, "Two"),
            ]);
        }

        Assert.Equal(["Reset (2 turns, 4 blocks)"], log);
    }

    [Fact]
    public async Task Clear_RaisesResetOnceTheConversationIsCleared()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitErrorAfterTokens([], new InvalidOperationException("offline"), ct));
        using var context = new AgentContext(new UIAgent(model));
        await context.SendMessageAsync("Hi");
        Assert.Equal(ConversationStatus.Error, context.Status);
        var log = Watch(context);

        await context.ClearAsync();

        Assert.Equal(
            [
                "Turns Reset (0 turns, Idle, no error)",
                "Error=none",
                "Status=Idle",
            ],
            log);
    }

    [Fact]
    public async Task StatusHandler_CanSendTheNextMessage_AndALaterHandlerReadsTheNextStatus()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("reply 1", ct));
        model.Enqueue(ct => ReplyAfterAsync(release.Task, "reply 2", ct));
        using var context = new AgentContext(new UIAgent(model));
        Task? next = null;
        context.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentContext.Status) && context.Status == ConversationStatus.Idle && next is null)
                next = context.SendMessageAsync("second");
        };
        var observed = new List<ConversationStatus>();
        context.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentContext.Status))
                observed.Add(context.Status);
        };

        await context.SendMessageAsync("first");

        // The second handler reads Status after the first one already started the next turn, so it reads the
        // next turn's status; the turn that ended says how it ended.
        Assert.NotNull(next);
        Assert.Equal([ConversationStatus.Streaming, ConversationStatus.Streaming, ConversationStatus.Streaming], observed);
        Assert.False(context.Turns[0].IsFailed);
        Assert.False(context.Turns[0].IsStopped);

        release.SetResult();
        await next;

        Assert.Equal(2, context.Turns.Count);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(ConversationStatus.Idle, observed[^1]);
    }

    [Fact]
    public async Task IsFailedHandler_CanRetryTheTurn()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitErrorAfterTokens([], new InvalidOperationException("offline"), ct));
        model.Enqueue(ct => ReplyAfterAsync(release.Task, "Recovered", ct));
        using var context = new AgentContext(new UIAgent(model));
        Task? retry = null;
        context.OnTurnAdded(turn => turn.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversationTurn.IsFailed) && turn.IsFailed && retry is null)
                retry = context.RetryAsync();
        });
        var reported = new List<ConversationStatus>();
        context.OnStatusChanged(reported.Add);

        await context.SendMessageAsync("Hi");

        // The retry started before the context raised the failure, so Status was streaming again when it was
        // raised and a change that changed back is not raised: Error never reaches a binding.
        Assert.NotNull(retry);
        Assert.Equal([ConversationStatus.Streaming], reported);
        Assert.Null(context.Error);

        release.SetResult();
        await retry;

        var turn = Assert.Single(context.Turns);
        Assert.False(turn.IsFailed);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal([ConversationStatus.Streaming, ConversationStatus.Idle], reported);
        Assert.Equal("Recovered", Assert.IsType<TextContentBlock>(Assert.Single(turn.ResponseBlocks)).RawText);
        Assert.Equal(2, model.Requests.Count);
    }

    [Fact]
    public async Task TurnsHandler_CannotStartAnotherSendWhileItsTurnIsAdded()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("One", ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Two", ct));
        using var context = new AgentContext(new UIAgent(model));
        Task? nested = null;
        Observe(context.Turns).CollectionChanged += (_, _) => nested ??= context.SendMessageAsync("nested");

        await context.SendMessageAsync("first");

        Assert.NotNull(nested);
        await Assert.ThrowsAsync<InvalidOperationException>(() => nested);
        Assert.Single(context.Turns);
        Assert.Single(model.Requests);
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    [Fact]
    public async Task TurnsHandler_CannotSendWhileARestoreIsReported()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Two", ct));
        using var context = new AgentContext(new UIAgent(model));
        Task? nested = null;
        Observe(context.Turns).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                nested ??= context.SendMessageAsync("too early");
        };

        await context.RestoreAsync([new ChatMessage(ChatRole.User, "first"), new ChatMessage(ChatRole.Assistant, "One")]);

        // Handlers after the sending one would hear of the new turn before the reset that loaded the others.
        Assert.NotNull(nested);
        await Assert.ThrowsAsync<InvalidOperationException>(() => nested);
        Assert.Single(context.Turns);
        Assert.Empty(model.Requests);

        await context.SendMessageAsync("second");

        Assert.Equal(2, context.Turns.Count);
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task IsStoppedHandler_CanSendTheNextMessage()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => TextThenWaitAsync("Partial", ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Next", ct));
        using var context = new AgentContext(new UIAgent(model));
        var streamed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? next = null;
        context.OnTurnAdded(turn =>
        {
            Observe(turn.ResponseBlocks).CollectionChanged += (_, _) => streamed.TrySetResult();
            turn.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ConversationTurn.IsStopped) && next is null)
                    next = context.SendMessageAsync("next");
            };
        });
        var reported = new List<ConversationStatus>();
        context.OnStatusChanged(reported.Add);

        var send = context.SendMessageAsync("Hi");
        await streamed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await context.CancelAsync();
        await send;
        Assert.NotNull(next);
        await next;

        Assert.Equal(2, context.Turns.Count);
        Assert.True(context.Turns[0].IsStopped);
        Assert.Equal("Next", Assert.IsType<TextContentBlock>(Assert.Single(context.Turns[1].ResponseBlocks)).RawText);
        // Each status is raised once it differs from the last one raised, so the next send's start, which kept
        // Streaming, and the stop's Idle, which the next send had already reported, are not raised again.
        Assert.Equal([ConversationStatus.Streaming, ConversationStatus.Idle], reported);
        Assert.Equal(["user:Hi", "assistant:Partial", "user:next"], Describe(model.Requests[^1]));
    }

    [Fact]
    public async Task BlocksHandler_CanStopTheTurn()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => TextThenWaitAsync("Partial", ct));
        using var context = new AgentContext(new UIAgent(model));
        Task? stop = null;
        context.OnTurnAdded(turn => Observe(turn.ResponseBlocks).CollectionChanged += (_, _) =>
            stop ??= context.CancelAsync());

        await context.SendMessageAsync("Hi");
        Assert.NotNull(stop);
        await stop;

        var turn = Assert.Single(context.Turns);
        Assert.True(turn.IsStopped);
        Assert.Equal("Partial", Assert.IsType<TextContentBlock>(Assert.Single(turn.ResponseBlocks)).RawText);
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    [Fact]
    public async Task ResponseBlocksHandler_CannotRetryAgainWhileARetryClearsTheTurn()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitErrorAfterTokens(["Partial"], new InvalidOperationException("offline"), ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Recovered", ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Again", ct));
        using var context = new AgentContext(new UIAgent(model));
        await context.SendMessageAsync("Hi");
        var turn = Assert.Single(context.Turns);
        Task? nested = null;
        Observe(turn.ResponseBlocks).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                nested ??= context.RetryAsync();
        };

        await context.RetryAsync();

        Assert.NotNull(nested);
        await Assert.ThrowsAsync<InvalidOperationException>(() => nested);
        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal("Recovered", Assert.IsType<TextContentBlock>(Assert.Single(turn.ResponseBlocks)).RawText);
    }

    [Fact]
    public async Task BlocksHandler_CanClearTheConversationWhileItStreams()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => TextThenWaitAsync("Partial", ct));
        using var context = new AgentContext(new UIAgent(model));
        Task? clear = null;
        context.OnTurnAdded(turn => Observe(turn.ResponseBlocks).CollectionChanged += (_, _) =>
            clear ??= context.ClearAsync());
        var turns = new List<string>();
        Observe(context.Turns).CollectionChanged += (_, e) => turns.Add($"{e.Action} ({context.Turns.Count} turns, {context.Status})");

        await context.SendMessageAsync("Hi");
        Assert.NotNull(clear);
        await clear;

        Assert.Empty(context.Turns);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["Add (1 turns, Idle)", "Reset (0 turns, Idle)"], turns);
    }

    private static INotifyCollectionChanged Observe<T>(ReadOnlyObservableCollection<T> collection) => collection;

    private static List<string> Describe(IReadOnlyList<ChatMessage> request)
        => request.Select(message => $"{message.Role}:{message.Text}").ToList();

    /// <summary>
    /// Records the context's events, and those of every turn it adds, reading the state each event reports.
    /// </summary>
    private static List<string> Watch(AgentContext context)
    {
        var log = new List<string>();
        context.PropertyChanged += (_, e) => log.Add(e.PropertyName switch
        {
            nameof(AgentContext.Status) => $"Status={context.Status}",
            nameof(AgentContext.Error) => $"Error={context.Error?.Message ?? "none"}",
            _ => $"{e.PropertyName}",
        });
        Observe(context.Turns).CollectionChanged += (_, e) =>
        {
            log.Add(e.Action == NotifyCollectionChangedAction.Reset
                ? $"Turns Reset ({context.Turns.Count} turns, {context.Status}, {context.Error?.Message ?? "no error"})"
                : $"Turns {e.Action} {e.NewStartingIndex} ({context.Turns.Count} turns)");
            foreach (ConversationTurn turn in e.NewItems ?? Array.Empty<ConversationTurn>())
                WatchTurn(context, turn, log);
        };
        foreach (var turn in context.Turns)
            WatchTurn(context, turn, log);

        return log;
    }

    private static void WatchTurn(AgentContext context, ConversationTurn turn, List<string> log)
    {
        turn.PropertyChanged += (_, e) => log.Add(e.PropertyName switch
        {
            nameof(ConversationTurn.IsFailed) => $"turn.IsFailed={turn.IsFailed} ({context.Status})",
            nameof(ConversationTurn.IsStopped) => $"turn.IsStopped={turn.IsStopped} ({context.Status})",
            nameof(ConversationTurn.ModelId) => $"turn.ModelId={turn.ModelId} ({context.Status})",
            nameof(ConversationTurn.Usage) => $"turn.Usage={turn.Usage?.TotalTokenCount} ({context.Status})",
            _ => $"turn.{e.PropertyName}",
        });
        Observe(turn.RequestBlocks).CollectionChanged += (_, e) => log.Add($"RequestBlocks {Describe(e, turn.RequestBlocks)}");
        Observe(turn.ResponseBlocks).CollectionChanged += (_, e) => log.Add($"ResponseBlocks {Describe(e, turn.ResponseBlocks)}");
    }

    private static string Describe(NotifyCollectionChangedEventArgs e, ReadOnlyObservableCollection<ContentBlock> blocks)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
            return "Reset";

        var block = Assert.IsAssignableFrom<ContentBlock>(Assert.Single(e.NewItems!));
        Assert.Same(block, blocks[e.NewStartingIndex]);
        return $"{e.Action} {e.NewStartingIndex} {block.GetType().Name}";
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> TextThenWaitAsync(
        string text,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, text) { MessageId = "m1" };
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ReplyAfterAsync(
        Task gate,
        string text,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, text) { MessageId = Guid.NewGuid().ToString("N") };
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> FailAfterAsync(
        ChatResponseUpdate update,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return update;
        await Task.Yield();
        throw new InvalidOperationException("offline");
    }
}
