using System.Collections;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

/// <summary>
/// <see cref="ConversationItems"/> shows a conversation as a row for each block and a row for each turn that ended,
/// and raises a change with its index for each row, so a list that virtualizes its rows can follow it.
/// </summary>
public class ConversationItemsTests
{
    [Fact]
    public async Task ASend_AddsARowForEachBlockAsItArrives_AndTheTurnsRowOnceItEnds()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => Emit(ct, new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("Thinking"), new TextContent("Hello")]) { MessageId = "m1" }));
        using var context = new AgentContext(new UIAgent(model));
        using var items = new ConversationItems(context);
        var log = Watch(items);

        await context.SendMessageAsync("Hi");

        var turn = Assert.Single(context.Turns);
        Assert.Equal(
            ["Add 0 TextContentBlock", "Add 1 ReasoningContentBlock", "Add 2 TextContentBlock", "Add 3 ConversationTurn"],
            log);
        Assert.Equal([turn.RequestBlocks[0], turn.ResponseBlocks[0], turn.ResponseBlocks[1], turn], items);
    }

    [Fact]
    public async Task TheRows_MatchTheConversationAtEveryChangeOfTheContext()
    {
        // Calls, their results, text and a second turn: after every event the context raises, the rows are the turns'
        // blocks in order, each turn followed by its own row once it ended.
        var model = new ScriptedChatClient();
        model.Enqueue(ct => Emit(ct, new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("c1", "get_weather", new Dictionary<string, object?> { ["city"] = "Paris" })]) { MessageId = "m1" }));
        model.Enqueue(ct => Emit(ct, new ChatResponseUpdate(ChatRole.Assistant, "It's sunny.") { MessageId = "m2" }));
        model.Enqueue(ct => Emit(ct, new ChatResponseUpdate(ChatRole.Assistant, "Bye.") { MessageId = "m3" }));
        var weather = AIFunctionFactory.Create((string city) => $"Sunny in {city}", "get_weather");
        using var context = new AgentContext(new UIAgent(
            new FunctionInvokingChatClient(model),
            options => options.ChatOptions = new ChatOptions { Tools = [weather] }));
        using var items = new ConversationItems(context);
        var mismatches = CheckAtEveryChange(context, items, out var checks);

        await context.SendMessageAsync("Weather in Paris?");
        await context.SendMessageAsync("Thanks");

        Assert.Empty(mismatches);
        Assert.True(checks() > 10);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(2, items.OfType<ConversationTurn>().Count());
        Assert.Single(items.OfType<FunctionInvocationContentBlock>());
        Assert.Equal(Expected(context), items);
    }

    [Fact]
    public async Task ARetry_RemovesTheFailedResponseAndTheTurnsRow_ThenAddsTheNewResponse()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => FailAfter(ct, new ChatResponseUpdate(ChatRole.Assistant, "Partial") { MessageId = "m1" }));
        model.Enqueue(ct => Emit(ct, new ChatResponseUpdate(ChatRole.Assistant, "Recovered") { MessageId = "m2" }));
        using var context = new AgentContext(new UIAgent(model));
        using var items = new ConversationItems(context);
        var log = Watch(items);
        var mismatches = CheckAtEveryChange(context, items, out _);

        await context.SendMessageAsync("Hi");

        Assert.Equal(ConversationStatus.Error, context.Status);
        Assert.Equal(["Add 0 TextContentBlock", "Add 1 TextContentBlock", "Add 2 ConversationTurn"], log);

        log.Clear();
        await context.RetryAsync();

        var turn = Assert.Single(context.Turns);
        Assert.Equal(
            ["Remove 1 TextContentBlock", "Remove 1 ConversationTurn", "Add 1 TextContentBlock", "Add 2 ConversationTurn"],
            log);
        Assert.Equal([turn.RequestBlocks[0], turn.ResponseBlocks[0], turn], items);
        Assert.Equal("Recovered", ((TextContentBlock)items[1]).RawText);
        Assert.Empty(mismatches);
    }

    [Fact]
    public async Task AStoppedTurn_GetsItsRowOnceItStopped()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => TextThenWait(ct, "Partial"));
        using var context = new AgentContext(new UIAgent(model));
        using var items = new ConversationItems(context);
        var log = Watch(items);
        var streamed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        items.CollectionChanged += (_, _) =>
        {
            if (items.Count == 2)
                streamed.TrySetResult();
        };

        var send = context.SendMessageAsync("Hi");
        await streamed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(["Add 0 TextContentBlock", "Add 1 TextContentBlock"], log);

        await context.CancelAsync();
        await send;

        var turn = Assert.Single(context.Turns);
        Assert.True(turn.IsStopped);
        Assert.Equal("Add 2 ConversationTurn", log[^1]);
        Assert.Same(turn, items[^1]);
    }

    [Fact]
    public async Task AListCreatedWhileATurnResponds_StartsWithItsBlocks_AndAddsItsRowOnceItEnds()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => TextThenWait(ct, "Partial"));
        using var context = new AgentContext(new UIAgent(model));
        var streamed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ((INotifyCollectionChanged)context.Turns).CollectionChanged += (_, e) =>
            ((INotifyCollectionChanged)((ConversationTurn)e.NewItems![0]!).ResponseBlocks).CollectionChanged +=
                (_, _) => streamed.TrySetResult();

        var send = context.SendMessageAsync("Hi");
        await streamed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var items = new ConversationItems(context);
        var log = Watch(items);
        var turn = Assert.Single(context.Turns);

        Assert.Equal([turn.RequestBlocks[0], turn.ResponseBlocks[0]], items);

        await context.CancelAsync();
        await send;

        Assert.Equal(["Add 2 ConversationTurn"], log);
        Assert.Equal(Expected(context), items);
    }

    [Fact]
    public async Task ARestoreOrAClear_RaisesOneReset()
    {
        using var context = new AgentContext(new UIAgent(new ScriptedChatClient()));
        using var items = new ConversationItems(context);
        var log = Watch(items);

        await context.RestoreAsync(
        [
            new ChatMessage(ChatRole.User, "first"),
            new ChatMessage(ChatRole.Assistant, "One"),
            new ChatMessage(ChatRole.User, "second"),
            new ChatMessage(ChatRole.Assistant, "Two"),
        ]);

        Assert.Equal(["Reset"], log);
        Assert.Equal(6, items.Count);
        Assert.Equal(Expected(context), items);

        await context.ClearAsync();

        Assert.Equal(["Reset", "Reset"], log);
        Assert.Empty(items);
    }

    [Fact]
    public async Task TheRows_StartWithTheContextsTurns()
    {
        using var context = new AgentContext(new UIAgent(new ScriptedChatClient()));
        await context.RestoreAsync([new ChatMessage(ChatRole.User, "first"), new ChatMessage(ChatRole.Assistant, "One")]);

        using var items = new ConversationItems(context);

        var turn = Assert.Single(context.Turns);
        Assert.Equal([turn.RequestBlocks[0], turn.ResponseBlocks[0], turn], items);
    }

    [Fact]
    public async Task AHandlerOfTheTurnsRow_CanSendTheNextMessage_AndLaterHandlersStillSeeTheRowsInOrder()
    {
        // The turn's row is added once the turn ended, so a handler of it can already send. The next turn's rows follow
        // once every handler saw the turn's row, so a handler after the one that sent, such as a CollectionView that
        // applies each change by its index, sees them in order.
        var model = new ScriptedChatClient();
        model.Enqueue(ct => Emit(ct, new ChatResponseUpdate(ChatRole.Assistant, "One") { MessageId = "m1" }));
        model.Enqueue(ct => Emit(ct, new ChatResponseUpdate(ChatRole.Assistant, "Two") { MessageId = "m2" }));
        using var context = new AgentContext(new UIAgent(model));
        using var items = new ConversationItems(context);
        Task? next = null;
        items.CollectionChanged += (_, e) =>
        {
            if (e.NewItems?[0] is ConversationTurn && next is null)
                next = context.SendMessageAsync("second");
        };
        var log = Watch(items);

        await context.SendMessageAsync("first");
        Assert.NotNull(next);
        await next;

        Assert.Equal(
            [
                "Add 0 TextContentBlock", "Add 1 TextContentBlock", "Add 2 ConversationTurn",
                "Add 3 TextContentBlock", "Add 4 TextContentBlock", "Add 5 ConversationTurn",
            ],
            log);
        Assert.Equal(Expected(context), items);
    }

    [Fact]
    public async Task Dispose_StopsFollowingTheContext()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => Emit(ct, new ChatResponseUpdate(ChatRole.Assistant, "One") { MessageId = "m1" }));
        using var context = new AgentContext(new UIAgent(model));
        var items = new ConversationItems(context);
        var log = Watch(items);

        items.Dispose();
        items.Dispose();
        await context.SendMessageAsync("Hi");

        Assert.Empty(log);
        Assert.Empty(items);
    }

    [Fact]
    public async Task TheRows_CannotBeChangedDirectly()
    {
        using var context = new AgentContext(new UIAgent(new ScriptedChatClient()));
        await context.RestoreAsync([new ChatMessage(ChatRole.User, "first"), new ChatMessage(ChatRole.Assistant, "One")]);
        using var items = new ConversationItems(context);
        IList list = items;
        var turn = Assert.Single(context.Turns);

        Assert.True(list.IsReadOnly);
        Assert.Equal(2, list.IndexOf(turn));
        Assert.True(list.Contains(turn.ResponseBlocks[0]));
        Assert.Equal(-1, list.IndexOf(null));
        Assert.Same(turn, list[2]);
        Assert.Throws<NotSupportedException>(() => list.Add(turn));
        Assert.Throws<NotSupportedException>(() => list.Insert(0, turn));
        Assert.Throws<NotSupportedException>(() => list.Remove(turn));
        Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => list[0] = turn);
        Assert.Throws<NotSupportedException>(() => list.Clear());
        var copy = new object[3];
        list.CopyTo(copy, 0);
        Assert.Equal(items, copy);
    }

    [Fact]
    public async Task TheRows_ShowInABindableLayout_WithTheSelectorsTemplatesAndTurnTemplate()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => FailAfter(ct, new ChatResponseUpdate(ChatRole.Assistant, "Partial") { MessageId = "m1" }));
        model.Enqueue(ct => Emit(ct, new ChatResponseUpdate(ChatRole.Assistant, "Recovered") { MessageId = "m2" }));
        using var context = new AgentContext(new UIAgent(model));
        using var items = new ConversationItems(context);
        var selector = new BlockTemplateSelector
        {
            Templates = { new BlockTemplate { When = BlockMatch.Is<TextContentBlock>(), Template = new DataTemplate(typeof(RawTextLabel)) } },
            TurnTemplate = new DataTemplate(() =>
            {
                var label = new Label();
                label.SetBinding(Label.TextProperty, new Binding(nameof(ConversationTurn.IsFailed), stringFormat: "failed: {0}"));
                return label;
            }),
        };
        var layout = new VerticalStackLayout();
        BindableLayout.SetItemTemplateSelector(layout, selector);
        BindableLayout.SetItemsSource(layout, items);

        await context.SendMessageAsync("Hi");

        Assert.Equal(["Hi", "Partial", "failed: True"], Texts(layout));

        await context.RetryAsync();

        Assert.Equal(["Hi", "Recovered", "failed: False"], Texts(layout));
    }

    private static List<string> Texts(Layout layout) => layout.Children.Select(child => ((Label)child).Text ?? "").ToList();

    /// <summary>The rows the conversation has now: each turn's blocks in order, and the turn once it no longer responds.</summary>
    private static List<object> Expected(AgentContext context)
    {
        var rows = new List<object>();
        foreach (var turn in context.Turns)
        {
            rows.AddRange(turn.RequestBlocks);
            rows.AddRange(turn.ResponseBlocks);
            if (!turn.IsResponding)
                rows.Add(turn);
        }

        return rows;
    }

    /// <summary>
    /// Records each change, and applies it to a copy that must match the rows after every change: a list that follows
    /// the changes, as a <see cref="CollectionView"/> does, shows the same rows.
    /// </summary>
    private static List<string> Watch(ConversationItems items)
    {
        var log = new List<string>();
        var copy = new List<object>(items);
        items.CollectionChanged += (_, e) =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems is [var added] && e.NewStartingIndex <= copy.Count:
                    copy.Insert(e.NewStartingIndex, added!);
                    log.Add($"Add {e.NewStartingIndex} {added!.GetType().Name}");
                    break;
                case NotifyCollectionChangedAction.Remove
                    when e.OldItems is [var removed] && e.OldStartingIndex < copy.Count && ReferenceEquals(copy[e.OldStartingIndex], removed):
                    copy.RemoveAt(e.OldStartingIndex);
                    log.Add($"Remove {e.OldStartingIndex} {removed!.GetType().Name}");
                    break;
                case NotifyCollectionChangedAction.Reset:
                    copy = new List<object>(items);
                    log.Add("Reset");
                    break;
                default:
                    log.Add($"A change the copy can't follow: {e.Action} at {e.NewStartingIndex}/{e.OldStartingIndex}");
                    break;
            }

            if (!copy.SequenceEqual(items))
                log.Add("The copy no longer matches the rows");
        };
        return log;
    }

    /// <summary>
    /// Compares the rows with the conversation after every event the context raises. The list subscribed first, so it
    /// has followed each change by then. A mismatch is recorded rather than thrown, because an exception thrown by a
    /// handler fails the turn.
    /// </summary>
    private static List<string> CheckAtEveryChange(AgentContext context, ConversationItems items, out Func<int> checks)
    {
        var mismatches = new List<string>();
        var count = 0;
        checks = () => count;

        void Check(string change)
        {
            count++;
            if (!Expected(context).SequenceEqual(items))
                mismatches.Add($"{change}: expected {Expected(context).Count} rows, the list has {items.Count}");
        }

        void Follow(ConversationTurn turn)
        {
            ((INotifyCollectionChanged)turn.RequestBlocks).CollectionChanged += (_, e) => Check($"RequestBlocks {e.Action}");
            ((INotifyCollectionChanged)turn.ResponseBlocks).CollectionChanged += (_, e) => Check($"ResponseBlocks {e.Action}");
            turn.PropertyChanged += (_, e) => Check($"turn.{e.PropertyName}");
        }

        foreach (var turn in context.Turns)
            Follow(turn);
        ((INotifyCollectionChanged)context.Turns).CollectionChanged += (_, e) =>
        {
            foreach (ConversationTurn turn in e.NewItems ?? Array.Empty<ConversationTurn>())
                Follow(turn);
            Check($"Turns {e.Action}");
        };
        context.PropertyChanged += (_, e) => Check(e.PropertyName ?? "");
        return mismatches;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Emit(
        [EnumeratorCancellation] CancellationToken cancellationToken,
        params ChatResponseUpdate[] updates)
    {
        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> FailAfter(
        [EnumeratorCancellation] CancellationToken cancellationToken,
        ChatResponseUpdate update)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return update;
        await Task.Yield();
        throw new InvalidOperationException("offline");
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> TextThenWait(
        [EnumeratorCancellation] CancellationToken cancellationToken,
        string text)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, text) { MessageId = "m1" };
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
