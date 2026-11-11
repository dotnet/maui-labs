using System.Collections.Specialized;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class MessageListTests
{
    [Fact]
    public void NoSession_HasNoRows()
    {
        var view = ChatHost.Create();

        Assert.Empty(view.Rows());
        Assert.True(view.IsEmpty);
    }

    [Fact]
    public void Turn_ProjectsTheUserBubbleThenEachResponseBlock() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("Hel", "lo");
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Hi there";
        await view.SendAsync();

        Assert.Collection(
            view.Rows(),
            row => Assert.Equal("Hi there", Assert.IsAssignableFrom<RichContentBlock>(Assert.Single(Assert.IsType<UserMessageRow>(row).Blocks)).RawText),
            row => Assert.Equal("Hello", Assert.IsType<TextContentBlock>(Assert.IsType<BlockRow>(row).Block).RawText));
        Assert.False(view.IsEmpty);
    });

    [Fact]
    public void UserBubble_ShowsTextAndAttachmentsTogether() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("Nice picture");
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "What is this?";
        Assert.True(view.AddAttachment(Blocks.Png("cat.png")));
        Assert.True(view.AddAttachment(Blocks.Pdf("notes.pdf")));
        await view.SendAsync();

        var user = Assert.IsType<UserMessageRow>(view.Rows()[0]);
        Assert.Contains(user.Blocks, block => block is RichContentBlock { RawText: "What is this?" });
        Assert.Contains(user.Blocks, block => block is MediaContentBlock);

        var bubble = Assert.IsType<UserMessageView>(view.Realize(user));
        Assert.Collection(
            bubble.Stack.Children,
            image => Assert.IsType<Image>(image),
            chip => Assert.Contains("notes.pdf", Assert.IsType<Border>(chip).Descendants<Label>().Select(l => l.Text)),
            text => Assert.Equal("What is this?", Assert.IsType<Label>(text).Text));
        Assert.Equal(LayoutOptions.End, ((Border)bubble.Content).HorizontalOptions);
    });

    [Fact]
    public void Streaming_UpdatesTheSameRowInPlace() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Hi";
        var send = view.SendAsync();
        stream.Text("Hel");
        await UIThread.Until(() => view.Rows().Count == 2 && view.Rows()[1] is BlockRow);

        var row = (BlockRow)view.Rows()[1];
        var host = Assert.IsType<BlockHost>(view.Realize(row));
        var text = Assert.IsType<TextBlockView>(host.HostedView);
        Assert.Equal("Hel", text.Label.Text);

        var collectionChanges = 0;
        ((INotifyCollectionChanged)view.MessageList.ItemsSource).CollectionChanged += (_, _) => collectionChanges++;
        stream.Text("lo");
        await UIThread.Until(() => text.Label.Text == "Hello");

        Assert.Same(row, view.Rows()[1]);
        Assert.Equal(0, collectionChanges);

        stream.Complete();
        await send;
    });

    [Fact]
    public void Thinking_ShowsUntilVisibleContentArrives() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Hi";
        var send = view.SendAsync();
        await UIThread.Until(() => view.IsBusy);

        Assert.IsType<ThinkingRow>(view.Rows()[^1]);
        Assert.IsType<ThinkingView>(view.Realize(view.Rows()[^1]));

        stream.Text("Hello");
        await UIThread.Until(() => view.Rows()[^1] is BlockRow);
        Assert.DoesNotContain(view.Rows(), row => row is ThinkingRow);

        stream.Complete();
        await send;
        Assert.False(view.IsBusy);
    });

    [Fact]
    public void Thinking_StaysAfterAToolCallWhileTheResponseContinues() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Weather?";
        var send = view.SendAsync();
        stream.Content(new FunctionCallContent("c1", "get_weather"));
        await UIThread.Until(() => view.Rows().Any(row => row is BlockRow { Block: FunctionInvocationContentBlock }));

        Assert.IsType<ThinkingRow>(view.Rows()[^1]);

        stream.Complete();
        await send;
    });

    [Fact]
    public void FailedTurn_ShowsAnErrorRowWhoseRetryResends() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Fail(new InvalidOperationException("model offline")).Reply("Recovered");
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Hi";
        await view.SendAsync();

        Assert.Equal(ConversationStatus.Error, view.Session!.Status);
        var errorRow = Assert.IsType<TurnErrorRow>(view.Rows()[^1]);
        Assert.IsType<UserMessageRow>(view.Rows()[0]);
        Assert.False(view.HasError);

        var error = Assert.IsType<TurnErrorView>(view.Realize(errorRow));
        Assert.Equal("AIChatTurnError", error.AutomationId);
        Assert.Equal("AIChatRetryButton", error.RetryButton.AutomationId);
        error.RetryButton.Command.Execute(null);
        await UIThread.Until(() => view.Session.Status == ConversationStatus.Idle);

        Assert.DoesNotContain(view.Rows(), row => row is TurnErrorRow);
        Assert.Equal("Recovered", ((TextContentBlock)((BlockRow)view.Rows()[^1]).Block).RawText);
        Assert.Equal(2, client.Requests.Count);
    });

    [Fact]
    public void Retry_RemovesThePartialResponseBeforeTheNewOneStreams() => UIThread.Run(async () =>
    {
        var retry = Waiters.Signal();
        var client = new ScriptedChatClient()
            .Then((_, _, _) => PartialThenFail())
            .WaitThenReply(retry.Task, "Complete answer");
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Hi";
        await view.SendAsync();
        Assert.Contains(view.Rows(), row => row is BlockRow { Block: TextContentBlock { RawText: "Partial" } });
        Assert.IsType<TurnErrorRow>(view.Rows()[^1]);

        var retrying = view.Session!.RetryAsync();
        await UIThread.Until(() => view.IsBusy);

        Assert.DoesNotContain(view.Rows(), row => row is BlockRow or TurnErrorRow);
        Assert.IsType<ThinkingRow>(view.Rows()[^1]);

        retry.SetResult();
        await retrying;
        Assert.Equal("Complete answer", ((TextContentBlock)((BlockRow)view.Rows()[^1]).Block).RawText);

        static async IAsyncEnumerable<ChatResponseUpdate> PartialThenFail()
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Partial");
            await Task.Yield();
            throw new HttpRequestException("connection reset");
        }
    });

    [Fact]
    public void ClearAsync_RemovesEveryRow() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("One").Reply("Two");
        var view = ChatHost.Create(ChatHost.Session(client));
        await SendAsync(view, "first");
        await SendAsync(view, "second");
        Assert.Equal(4, view.Rows().Count);

        await view.ClearAsync();

        Assert.Empty(view.Rows());
        Assert.True(view.IsEmpty);
        Assert.Empty(view.Session!.Turns);
    });

    [Fact]
    public void AssistantMedia_RendersImagesInlineAndOtherFilesAsChips() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply(Blocks.Png("chart.png"), Blocks.Pdf("report.pdf"));
        var view = ChatHost.Create(ChatHost.Session(client));

        await SendAsync(view, "Make a chart");

        var mediaRows = view.Rows().OfType<BlockRow>().Where(row => row.Block is MediaContentBlock).ToList();
        Assert.NotEmpty(mediaRows);
        var items = mediaRows.Select(row => ((MediaBlockView)((BlockHost)view.Realize(row)).HostedView!).MediaView).ToList();
        Assert.Equal(2, items.Count);
        Assert.Contains(items, item => item is Image);
        Assert.Contains(items, item => item is Border chip && chip.Descendants<Label>().Any(label => label.Text == "report.pdf"));
    });

    [Fact]
    public void ToolCalls_RenderAsCardsWithTheirResult() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient()
            .Reply(new FunctionCallContent("c1", "get_weather", new Dictionary<string, object?> { ["city"] = "Paris" }))
            .Reply("It is sunny");
        var view = ChatHost.Create(v =>
        {
            v.ChatClient = client;
            v.Tools = [AIFunctionFactory.Create((string city) => $"Sunny in {city}", "get_weather")];
        });

        await SendAsync(view, "Weather in Paris?");

        var toolRow = Assert.Single(view.Rows().OfType<BlockRow>(), row => row.Block is FunctionInvocationContentBlock);
        var card = Assert.IsType<ToolCallView>(((BlockHost)view.Realize(toolRow)).HostedView);
        Assert.Equal("get_weather", card.TitleLabel.Text);
        Assert.Equal("Completed", card.StatusLabel.Text);
        Assert.Equal("It is sunny", ((TextContentBlock)((BlockRow)view.Rows()[^1]).Block).RawText);
    });

    [Fact]
    public void GeneratedToolBlocks_UseTheirTypedTemplate() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient()
            .Reply(new FunctionCallContent("c1", "get_weather", new Dictionary<string, object?> { ["city"] = "Oslo" }))
            .Reply("Cold");
        var function = AIFunctionFactory.Create((string city) => new { temperature = -3.5 }, "get_weather");
        var session = new AgentContext(new UIAgent(client.AsBuilder().UseFunctionInvocation().Build(), options =>
        {
            options.ChatOptions = new ChatOptions { Tools = [function] };
            options.AddGeneratedToolBlocks();
        }));
        var template = new BlockTemplate { BlockType = typeof(WeatherBlock), ViewType = typeof(CountingBlockView) };
        var view = ChatHost.Create(session, v => v.ContentTemplates.Add(template));

        await SendAsync(view, "Weather in Oslo?");

        var row = Assert.Single(view.Rows().OfType<BlockRow>(), row => row.Block is WeatherBlock);
        Assert.Same(template, row.Template);
        var custom = Assert.IsType<CountingBlockView>(((BlockHost)view.Realize(row)).HostedView);
        var weather = Assert.IsType<WeatherBlock>(custom.BindingContext);
        Assert.Same(weather, custom.Block);
        Assert.Same(session, custom.Session);
        Assert.Equal("Oslo", weather.City);
        Assert.Equal(-3.5, weather.Temperature);
    });

    [Fact]
    public void ResponseFooter_FollowsEachCompletedTurn() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client), v =>
            v.ResponseFooterTemplate = new DataTemplate(() => new Label { AutomationId = "Footer" }));

        view.Text = "Hi";
        var send = view.SendAsync();
        stream.Text("Hello");
        await UIThread.Until(() => view.Rows()[^1] is BlockRow);
        Assert.DoesNotContain(view.Rows(), row => row is TurnFooterRow);

        stream.Complete();
        await send;

        var footer = Assert.IsType<TurnFooterRow>(view.Rows()[^1]);
        var host = Assert.IsType<TurnFooterHost>(view.Realize(footer));
        var label = Assert.IsType<Label>(host.Content);
        Assert.Same(view.Session!.Turns[0], label.BindingContext);
    });

    [Fact]
    public void AppTemplates_CanClaimRequestBlocks() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("Hello");
        var template = new BlockTemplate { Role = "user", BlockType = typeof(RichContentBlock), ViewType = typeof(CountingBlockView) };
        var view = ChatHost.Create(ChatHost.Session(client), v => v.ContentTemplates.Add(template));

        await SendAsync(view, "Hi");

        Assert.DoesNotContain(view.Rows(), row => row is UserMessageRow);
        Assert.Same(template, ((BlockRow)view.Rows()[0]).Template);
    });

    [Fact]
    public void TemplateWithoutAView_HidesMatchingBlocks() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply(new TextReasoningContent("pondering"), new TextContent("Answer"));
        var view = ChatHost.Create(ChatHost.Session(client), v => v.ContentTemplates.Add(new BlockTemplate { BlockType = typeof(ReasoningContentBlock) }));

        await SendAsync(view, "Why?");

        Assert.Contains(view.Session!.Turns[0].ResponseBlocks, block => block is ReasoningContentBlock);
        Assert.DoesNotContain(view.Rows(), row => row is BlockRow { Block: ReasoningContentBlock });
        Assert.Contains(view.Rows(), row => row is BlockRow { Block: TextContentBlock });
    });

    [Fact]
    public void ChangingTemplates_ReselectsExistingRows() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("Hello");
        var view = ChatHost.Create(ChatHost.Session(client));
        await SendAsync(view, "Hi");
        var before = (BlockRow)view.Rows()[1];

        var template = new BlockTemplate { BlockType = typeof(TextContentBlock), ViewType = typeof(CountingBlockView) };
        view.ContentTemplates.Add(template);

        var after = (BlockRow)view.Rows()[1];
        Assert.NotSame(before, after);
        Assert.Same(template, after.Template);

        template.ViewType = null;
        Assert.DoesNotContain(view.Rows(), row => row is BlockRow);

        view.ContentTemplates.Clear();
        Assert.Contains(view.Rows(), row => row is BlockRow { Template.CanRender: true });
    });

    [Fact]
    public void StreamingChanges_AreCoalescedIntoOneRefreshPerTick() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client));
        using var dispatcher = TestDispatcher.Install();

        view.Text = "Hi";
        var send = view.SendAsync();
        stream.Text("a");
        await UIThread.Until(() => view.Session!.Turns.Count == 1 && view.Session.Turns[0].ResponseBlocks.Count == 1);
        Assert.DoesNotContain(view.Rows(), row => row is BlockRow);
        Assert.Equal(1, dispatcher.PendingCount);

        dispatcher.RunPending();
        var row = Assert.IsType<BlockRow>(view.Rows()[^1]);
        var host = Assert.IsType<BlockHost>(view.Realize(row));
        var custom = Assert.IsType<TextBlockView>(host.HostedView);
        var refreshes = 0;
        row.Changed += (_, _) => refreshes++;

        foreach (var token in new[] { "b", "c", "d", "e" })
            stream.Text(token);
        await UIThread.Until(() => ((TextContentBlock)row.Block).RawText == "abcde");

        Assert.Equal(0, refreshes);
        Assert.Equal("a", custom.Label.Text);
        Assert.Equal(1, dispatcher.PendingCount);

        dispatcher.RunPending();
        Assert.Equal(1, refreshes);
        Assert.Equal("abcde", custom.Label.Text);

        stream.Complete();
        await UIThread.Until(() => dispatcher.PendingCount > 0 || !view.IsBusy);
        dispatcher.RunPending();
        await send;
    });

    [Fact]
    public void BlockViews_RefreshOncePerBatch() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client), v =>
            v.ContentTemplates.Add(new BlockTemplate { BlockType = typeof(TextContentBlock), ViewType = typeof(CountingBlockView) }));
        using var dispatcher = TestDispatcher.Install();

        view.Text = "Hi";
        var send = view.SendAsync();
        stream.Text("a");
        await UIThread.Until(() => dispatcher.PendingCount > 0 && view.Session!.Turns[0].ResponseBlocks.Count == 1);
        dispatcher.RunPending();

        var row = (BlockRow)view.Rows()[^1];
        var custom = Assert.IsType<CountingBlockView>(((BlockHost)view.Realize(row)).HostedView);
        var initial = custom.ChangeCount;

        for (var i = 0; i < 10; i++)
            stream.Text("x");
        await UIThread.Until(() => ((TextContentBlock)row.Block).RawText.Length == 11);
        dispatcher.RunPending();

        Assert.Equal(initial + 1, custom.ChangeCount);

        stream.Complete();
        await UIThread.Until(() => dispatcher.PendingCount > 0 || !view.IsBusy);
        dispatcher.RunPending();
        await send;
    });

    [Fact]
    public void Unloading_ReleasesEverySessionAndBlockSubscription() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("One").Reply("Two");
        var session = ChatHost.Session(client);
        var view = ChatHost.Create(session);
        await SendAsync(view, "first");
        Assert.True(view.MessageList.SubscriptionCount > 0);

        ChatHost.Unload(view);

        Assert.False(view.IsLoaded);
        Assert.Equal(0, view.MessageList.SubscriptionCount);

        var rows = view.Rows().ToArray();
        await session.SendMessageAsync("second");
        Assert.Equal(rows, view.Rows());

        ChatHost.Load(view);
        Assert.Equal(4, view.Rows().Count);
        Assert.True(view.MessageList.SubscriptionCount > 0);
    });

    [Fact]
    public void ReloadingTheView_ShowsWhatChangedWhileItWasUnloaded() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client), v => v.ContentTemplates.Add(
            new BlockTemplate { BlockType = typeof(TextContentBlock), Role = "assistant", ViewType = typeof(CountingBlockView) }));

        view.Text = "Weather?";
        var send = view.SendAsync();
        stream.Content(new FunctionCallContent("c1", "get_weather"));
        stream.Text("Checking");
        await UIThread.Until(() => view.Rows().Any(row => row is BlockRow { Block: TextContentBlock }));

        var textRow = Assert.Single(view.Rows().OfType<BlockRow>(), row => row.Block is TextContentBlock);
        var text = Assert.IsType<CountingBlockView>(((BlockHost)view.Realize(textRow)).HostedView);
        var toolRow = Assert.Single(view.Rows().OfType<BlockRow>(), row => row.Block is FunctionInvocationContentBlock);
        var card = Assert.IsType<ToolCallView>(((BlockHost)view.Realize(toolRow)).HostedView);
        Assert.Equal("Running\u2026", card.StatusLabel.Text);
        var renders = text.ChangeCount;

        // The response streams on and ends while the view is away (another tab, a pushed page).
        ChatHost.Unload(view);
        stream.Text(" the forecast");
        stream.Complete();
        await send;

        ChatHost.Load(view);

        Assert.Equal("Checking the forecast", ((TextContentBlock)textRow.Block).RawText);
        Assert.Same(textRow, view.Rows().OfType<BlockRow>().Single(row => row.Block is TextContentBlock));
        Assert.True(text.ChangeCount > renders);
        Assert.Equal("Not completed", card.StatusLabel.Text);
    });

    [Fact]
    public void ChangingTemplatesWhileUnloaded_DoesNotObserveTheSession() => UIThread.Run(async () =>
    {
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient().Reply("Hello")));
        await SendAsync(view, "Hi");
        ChatHost.Unload(view);

        view.ResponseFooterTemplate = new DataTemplate(() => new Label());
        view.ContentTemplates = [new BlockTemplate { BlockType = typeof(TextContentBlock), Role = "assistant", ViewType = typeof(CountingBlockView) }];

        Assert.Equal(0, view.MessageList.SubscriptionCount);

        ChatHost.Load(view);
        Assert.True(view.MessageList.SubscriptionCount > 0);
        Assert.IsType<CountingBlockView>(((BlockHost)view.Realize(view.Rows()[1])).HostedView);
        Assert.IsType<TurnFooterRow>(view.Rows()[^1]);
    });

    [Fact]
    public void ChangesRaisedOnAnotherThread_AreAppliedOnTheUIThread() => UIThread.Run(async () =>
    {
        using var dispatcher = UIThreadDispatcher.Install();
        var session = ChatHost.Session(new ScriptedChatClient().Reply("Hel", "lo"));
        var view = ChatHost.Create(session);
        view.MessageList.FlushDelay = TimeSpan.Zero;
        var threads = new HashSet<int>();
        ((INotifyCollectionChanged)view.MessageList.ItemsSource).CollectionChanged += (_, _) => threads.Add(Environment.CurrentManagedThreadId);
        view.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AIChatView.IsBusy) or nameof(AIChatView.IsEmpty))
                threads.Add(Environment.CurrentManagedThreadId);
        };

        // An app that calls the engine from a worker thread gets the engine's notifications on that thread.
        await Task.Run(() => session.SendMessageAsync("Hi"));
        await UIThread.Until(() => view.Rows().Count == 2 && !view.IsBusy && !view.IsEmpty);

        Assert.Equal("Hello", ((TextContentBlock)((BlockRow)view.Rows()[1]).Block).RawText);
        Assert.Equal([dispatcher.ThreadId], threads);
    });

    [Fact]
    public void NewRows_ScrollToTheEndWhilePinned() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("One").Reply("Two").Reply("Three");
        var view = ChatHost.Create(ChatHost.Session(client));
        var requests = new List<int>();
        view.MessageList.ScrollToRequested += (_, e) => requests.Add(e.Index);

        await SendAsync(view, "first");
        Assert.NotEmpty(requests);
        Assert.Equal(view.Rows().Count - 1, requests[^1]);

        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -40, LastVisibleItemIndex = 0 });
        Assert.False(view.MessageList.IsPinned);
        requests.Clear();
        await view.Session!.SendMessageAsync("second");
        Assert.Empty(requests);

        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = 40, LastVisibleItemIndex = view.Rows().Count - 1 });
        Assert.True(view.MessageList.IsPinned);

        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -40, LastVisibleItemIndex = 0 });
        await SendAsync(view, "third");
        Assert.True(view.MessageList.IsPinned);
        Assert.Equal(view.Rows().Count - 1, requests[^1]);

        // A small correction while the newest row is visible keeps following; leaving that row unpins.
        var last = view.Rows().Count - 1;
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -2, LastVisibleItemIndex = last });
        Assert.True(view.MessageList.IsPinned);
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -8, LastVisibleItemIndex = last - 1 });
        Assert.False(view.MessageList.IsPinned);
    });

    [Fact]
    public void SwappingSessions_ProjectsTheNewSession() => UIThread.Run(async () =>
    {
        var first = ChatHost.Session(new ScriptedChatClient().Reply("From first"));
        var second = ChatHost.Session(new ScriptedChatClient().Reply("From second"));
        await second.SendMessageAsync("hello");
        var view = ChatHost.Create(first);
        await SendAsync(view, "hi");

        view.Session = second;

        Assert.Equal("From second", ((TextContentBlock)((BlockRow)view.Rows()[1]).Block).RawText);
        await first.SendMessageAsync("ignored");
        Assert.Equal(2, view.Rows().Count);
    });

    [Fact]
    public void ScrollsRaisedWhileRowsChange_DoNotUnpinTheList() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client));
        var requests = new List<int>();
        view.MessageList.ScrollToRequested += (_, e) => requests.Add(e.Index);

        // A list scrolled to the end clamps its offset when a row is removed, and the platform reports that
        // synchronously as a scroll back through the history.
        ((INotifyCollectionChanged)view.MessageList.ItemsSource).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
                view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -24, LastVisibleItemIndex = 0 });
        };

        view.Text = "Hi";
        var send = view.SendAsync();
        await UIThread.Until(() => view.Rows()[^1] is ThinkingRow);

        stream.Text("Hello");
        await UIThread.Until(() => view.Rows()[^1] is BlockRow);

        Assert.True(view.MessageList.IsPinned);
        Assert.Equal(view.Rows().Count - 1, requests[^1]);

        stream.Complete();
        await send;
    });

    [Fact]
    public void StoppingATurn_KeepsItsOutputAndMarksItsCallsStopped() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Search for it";
        var send = view.SendAsync();
        stream.Text("Looking");
        stream.Content(new FunctionCallContent("c1", "search", new Dictionary<string, object?> { ["q"] = "maui" }));
        await UIThread.Until(() => view.Rows().OfType<BlockRow>().Any(row => row.Block is FunctionInvocationContentBlock));

        var toolRow = view.Rows().OfType<BlockRow>().Single(row => row.Block is FunctionInvocationContentBlock);
        var card = Assert.IsType<ToolCallView>(((BlockHost)view.Realize(toolRow)).HostedView);
        Assert.Equal("Running\u2026", card.StatusLabel.Text);

        await view.StopAsync();
        await send;

        // The stop changed no block; the list refreshes the turn's rows when the status changes.
        Assert.True(view.Session!.Turns[0].IsStopped);
        Assert.Equal("Stopped", card.StatusLabel.Text);
        Assert.IsType<TurnStoppedRow>(view.Rows()[^1]);
        Assert.Contains(view.Rows(), row => row is BlockRow { Block: TextContentBlock { RawText: "Looking" } });
    });

    [Fact]
    public void FailedTurn_KeepsItsPartialOutputUntilRetried() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        client.Reply("Second try");
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Hi";
        var send = view.SendAsync();
        stream.Text("Partial");
        await UIThread.Until(() => view.Rows().OfType<BlockRow>().Any());
        stream.Complete(new InvalidOperationException("boom"));
        await send;

        Assert.Equal(ConversationStatus.Error, view.Session!.Status);
        Assert.Collection(
            view.Rows(),
            row => Assert.IsType<UserMessageRow>(row),
            row => Assert.Equal("Partial", ((TextContentBlock)Assert.IsType<BlockRow>(row).Block).RawText),
            row => Assert.IsType<TurnErrorRow>(row));
        Assert.False(view.HasError);

        var error = Assert.IsType<TurnErrorView>(view.Realize(view.Rows()[^1]));
        await error.RetryAsync();

        Assert.Collection(
            view.Rows(),
            row => Assert.IsType<UserMessageRow>(row),
            row => Assert.Equal("Second try", ((TextContentBlock)Assert.IsType<BlockRow>(row).Block).RawText));
    });

    [Fact]
    public void RestoredConversation_RendersWithoutSendingAndContinues() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("Cloudy tomorrow.");
        var session = ChatHost.Session(client);
        await session.RestoreAsync(
        [
            new ChatMessage(ChatRole.User, "Weather in Paris?"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "get_weather", new Dictionary<string, object?> { ["city"] = "Paris" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "Sunny")]),
            new ChatMessage(ChatRole.Assistant, "It is sunny in Paris."),
        ]);

        var view = ChatHost.Create(session);

        Assert.Empty(client.Requests);
        Assert.False(view.IsEmpty);
        Assert.False(view.ShowSuggestions);
        Assert.Collection(
            view.Rows(),
            row => Assert.Equal("Weather in Paris?", ((TextContentBlock)Assert.Single(Assert.IsType<UserMessageRow>(row).Blocks)).RawText),
            row =>
            {
                var card = Assert.IsType<ToolCallView>(((BlockHost)view.Realize(row)).HostedView);
                Assert.Equal("get_weather", card.TitleLabel.Text);
                Assert.Equal("Completed", card.StatusLabel.Text);
            },
            row => Assert.Equal("It is sunny in Paris.", ((TextContentBlock)Assert.IsType<BlockRow>(row).Block).RawText));

        await SendAsync(view, "And tomorrow?");

        Assert.Equal(5, view.Rows().Count);
        Assert.Equal("Cloudy tomorrow.", ((TextContentBlock)((BlockRow)view.Rows()[^1]).Block).RawText);
        Assert.Contains(client.Requests[0], message => message.Text == "It is sunny in Paris.");
    });

    [Fact]
    public void RestoringIntoAShownSession_RendersTheConversation() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var session = ChatHost.Session(client);
        var view = ChatHost.Create(session);
        Assert.True(view.IsEmpty);

        await session.RestoreAsync(
        [
            new ChatMessage(ChatRole.User, "Weather in Paris?"),
            new ChatMessage(ChatRole.Assistant, "It is sunny in Paris."),
        ]);

        Assert.Empty(client.Requests);
        Assert.False(view.IsEmpty);
        Assert.Collection(
            view.Rows(),
            row => Assert.Equal("Weather in Paris?", ((TextContentBlock)Assert.Single(Assert.IsType<UserMessageRow>(row).Blocks)).RawText),
            row => Assert.Equal("It is sunny in Paris.", ((TextContentBlock)Assert.IsType<BlockRow>(row).Block).RawText));
    });

    [Fact]
    public void ApprovedCall_ShowsItsResultInTheApprovalCard() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient()
            .Reply(new FunctionCallContent("c1", "delete_file", new Dictionary<string, object?> { ["path"] = "a.txt" }))
            .Reply("Deleted.");
        var view = ChatHost.Create(v =>
        {
            v.ChatClient = client;
            v.Tools = [new ApprovalRequiredAIFunction(AIFunctionFactory.Create((string path) => $"removed {path}", "delete_file"))];
        });

        view.Text = "Delete a.txt";
        var send = view.SendAsync();
        await UIThread.Until(() => view.EffectiveSession!.Status == ConversationStatus.AwaitingInput);

        var row = Assert.Single(view.Rows().OfType<BlockRow>(), row => row.Block is ToolApprovalBlock);
        var approval = Assert.IsType<ToolApprovalView>(((BlockHost)view.Realize(row)).HostedView);
        Assert.True(approval.Buttons.IsVisible);

        approval.ApproveCommand.Execute(null);
        await send;

        // One card per call: the result lands in the approval, not in a second tool card.
        Assert.DoesNotContain(view.Rows(), r => r is BlockRow { Block: FunctionInvocationContentBlock });
        Assert.False(approval.Buttons.IsVisible);
        Assert.Equal("Approved", approval.StatusLabel.Text);
        Assert.Equal("removed a.txt", approval.ResultLabel.Text);
        Assert.Equal("Deleted.", ((TextContentBlock)((BlockRow)view.Rows()[^1]).Block).RawText);
    });

    [Fact]
    public void PartialImages_UpdateTheirRowInPlace() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Draw a cat";
        var send = view.SendAsync();
        stream.Content(new ImageGenerationToolResultContent("img-1") { Outputs = [Blocks.Png("partial.png")] });
        await UIThread.Until(() => view.Rows().OfType<BlockRow>().Any());

        var row = Assert.Single(view.Rows().OfType<BlockRow>());
        var media = Assert.IsType<MediaBlockView>(((BlockHost)view.Realize(row)).HostedView);
        var image = Assert.IsType<Image>(media.MediaView);
        var partial = image.Source;

        var final = Blocks.Png("final.png");
        stream.Content(new ImageGenerationToolResultContent("img-1") { Outputs = [final] });
        await UIThread.Until(() => ReferenceEquals(((MediaContentBlock)row.Block).Content, final) && !ReferenceEquals(image.Source, partial));
        stream.Complete();
        await send;

        Assert.Same(row, Assert.Single(view.Rows().OfType<BlockRow>()));
        Assert.Same(image, media.MediaView);
    });

    [Fact]
    public void ResponseFooter_ShowsTheModelAndUsageOfACompletedTurn() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client), v => v.ResponseFooterTemplate = new DataTemplate(() =>
        {
            var label = new Label();
            label.SetBinding(Label.TextProperty, new MultiBinding
            {
                StringFormat = "{0} \u00B7 {1} tokens",
                Bindings = { new Binding(nameof(ConversationTurn.ModelId)), new Binding("Usage.TotalTokenCount") },
            });
            return label;
        }));

        view.Text = "Hi";
        var send = view.SendAsync();
        stream.TryWrite(new ChatResponseUpdate(ChatRole.Assistant, "Hello") { ModelId = "model-a" });
        await UIThread.Until(() => view.Rows().OfType<BlockRow>().Any());
        Assert.DoesNotContain(view.Rows(), row => row is TurnFooterRow);

        stream.TryWrite(new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { TotalTokenCount = 42 })]) { ModelId = "model-b" });
        stream.Complete();
        await send;

        var footer = Assert.IsType<TurnFooterRow>(view.Rows()[^1]);
        var label = Assert.Single(view.Realize(footer).Descendants<Label>());
        Assert.Equal("model-b \u00B7 42 tokens", label.Text);
    });

    [Fact]
    public void ReloadingTheView_KeepsItsRowsUnlessATemplateChanged() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("One");
        var template = new BlockTemplate { BlockType = typeof(TextContentBlock), Role = "assistant", ViewType = typeof(CountingBlockView) };
        var view = ChatHost.Create(ChatHost.Session(client), v => v.ContentTemplates.Add(template));
        await SendAsync(view, "first");
        var rows = view.Rows().ToArray();
        Assert.IsType<UserMessageRow>(rows[0]);

        ChatHost.Unload(view);
        ChatHost.Load(view);
        Assert.Equal(rows, view.Rows(), ReferenceEqualityComparer.Instance);

        // A template edited while the view was unloaded is not observed, but its rows still rebuild.
        ChatHost.Unload(view);
        template.ViewType = typeof(ServiceBackedView);
        template.ViewType = typeof(CountingBlockView);
        ChatHost.Load(view);

        Assert.Same(rows[0], view.Rows()[0]);
        var row = Assert.IsType<BlockRow>(view.Rows()[1]);
        Assert.NotSame(rows[1], row);
        Assert.Equal(template.Version, row.TemplateVersion);
    });

    private static async Task SendAsync(AIChatView view, string text)
    {
        view.Text = text;
        await view.SendAsync();
    }
}
