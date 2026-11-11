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
    public void UserMessage_ShowsTheAttachmentsAboveTheText() => UIThread.Run(async () =>
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

        var message = Assert.IsType<UserMessageView>(view.Realize(user));
        Assert.Collection(
            message.Attachments.Children,
            image => Assert.IsType<ChatImage>(image),
            chip => Assert.Contains("notes.pdf", Assert.IsType<Border>(chip).Descendants<Label>().Select(l => l.Text)));
        Assert.Equal("What is this?", message.TextLabel.Text);
        Assert.Equal(LayoutOptions.End, message.Bubble.HorizontalOptions);
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
        Assert.Equal("Hel", text.VisibleText());

        var collectionChanges = 0;
        ((INotifyCollectionChanged)view.MessageList.ItemsSource).CollectionChanged += (_, _) => collectionChanges++;
        stream.Text("lo");
        await UIThread.Until(() => text.VisibleText() == "Hello");

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
    public void FailedTurn_FollowedByANewMessage_IsMarkedNotSent() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Then((_, _, _) => PartialThenFail()).Reply("Sunny.");
        var view = ChatHost.Create(ChatHost.Session(client), v =>
            v.ResponseFooterTemplate = new DataTemplate(() => new Label { AutomationId = "Footer" }));

        await SendAsync(view, "Weather in Paris?");
        Assert.IsType<TurnErrorRow>(view.Rows()[^1]);

        // Sending is allowed after a failure, so the user can type a new message instead of retrying.
        await SendAsync(view, "Weather in Rome?");

        var turns = view.Session!.Turns;
        Assert.Collection(
            view.Rows(),
            row => Assert.Same(turns[0], Assert.IsType<UserMessageRow>(row).Turn),
            row => Assert.Equal("Partial", ((TextContentBlock)Assert.IsType<BlockRow>(row).Block).RawText),
            row => Assert.Same(turns[0], Assert.IsType<TurnNotSentRow>(row).Turn),
            row => Assert.Same(turns[1], Assert.IsType<UserMessageRow>(row).Turn),
            row => Assert.Equal("Sunny.", ((TextContentBlock)Assert.IsType<BlockRow>(row).Block).RawText),
            row => Assert.Same(turns[1], Assert.IsType<TurnFooterRow>(row).Turn));

        var marker = Assert.IsType<TurnNotSentView>(view.Realize(view.Rows()[2]));
        Assert.Equal("AIChatNotSent", marker.AutomationId);
        Assert.Empty(marker.Descendants<Button>());

        static async IAsyncEnumerable<ChatResponseUpdate> PartialThenFail()
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Partial");
            await Task.Yield();
            throw new HttpRequestException("connection reset");
        }
    });

    [Fact]
    public void TurnThatFailedBeforeTheViewShowedIt_IsMarkedNotSentOnceAnotherMessageIsSent() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Fail(new InvalidOperationException("model offline")).Reply("Hello");
        var session = ChatHost.Session(client);
        await session.SendMessageAsync("Hi");
        Assert.Equal(ConversationStatus.Error, session.Status);

        var view = ChatHost.Create(session);
        await SendAsync(view, "Anyone there?");

        Assert.Same(session.Turns[0], Assert.Single(view.Rows().OfType<TurnNotSentRow>()).Turn);
    });

    [Fact]
    public void RetriedTurn_IsNotMarkedNotSent() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Fail(new InvalidOperationException("model offline")).Reply("Recovered").Reply("Again");
        var view = ChatHost.Create(ChatHost.Session(client), v =>
            v.ResponseFooterTemplate = new DataTemplate(() => new Label { AutomationId = "Footer" }));

        await SendAsync(view, "Hi");
        await view.Session!.RetryAsync();
        await SendAsync(view, "And now?");

        Assert.DoesNotContain(view.Rows(), row => row is TurnNotSentRow or TurnErrorRow);
        Assert.Equal(2, view.Rows().OfType<TurnFooterRow>().Count());
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
    public void EncryptedReasoning_GetsNoRow() => UIThread.Run(async () =>
    {
        // Azure returns the reasoning summary, the answer, then the encrypted reasoning in a block of its own.
        var client = new ScriptedChatClient().Reply(
            new TextReasoningContent("Checking the forecast."),
            new TextContent("Sunny."),
            new TextReasoningContent(null) { ProtectedData = "opaque" });
        var view = ChatHost.Create(ChatHost.Session(client));

        await SendAsync(view, "Weather?");

        Assert.Equal(2, view.Session!.Turns[0].ResponseBlocks.OfType<ReasoningContentBlock>().Count());
        var reasoning = Assert.Single(view.Rows().OfType<BlockRow>(), row => row.Block is ReasoningContentBlock);
        Assert.Equal("Checking the forecast.", ((ReasoningContentBlock)reasoning.Block).Text);
    });

    [Fact]
    public void OnlyEncryptedReasoning_ShowsNothingForIt() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Weather?";
        var send = view.SendAsync();
        stream.Content(new TextReasoningContent(null) { ProtectedData = "opaque" });
        await UIThread.Until(() => view.Session!.Turns[0].ResponseBlocks.Count == 1);

        // Still working, with nothing to show yet.
        Assert.IsType<ThinkingRow>(view.Rows()[^1]);

        stream.Text("Sunny.");
        stream.Complete();
        await send;

        Assert.Collection(
            view.Rows(),
            row => Assert.IsType<UserMessageRow>(row),
            row => Assert.IsType<TextContentBlock>(Assert.IsType<BlockRow>(row).Block));
    });

    [Fact]
    public void AppTemplates_StillGetEncryptedReasoning() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply(new TextReasoningContent(null) { ProtectedData = "opaque" }, new TextContent("Sunny."));
        var template = new BlockTemplate { BlockType = typeof(ReasoningContentBlock), ViewType = typeof(CountingBlockView) };
        var view = ChatHost.Create(ChatHost.Session(client), v => v.ContentTemplates.Add(template));

        await SendAsync(view, "Weather?");

        Assert.Same(template, Assert.Single(view.Rows().OfType<BlockRow>(), row => row.Block is ReasoningContentBlock).Template);
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

        // The list settles after the answer's row was added.
        dispatcher.RunPending();

        foreach (var token in new[] { "b", "c", "d", "e" })
            stream.Text(token);
        await UIThread.Until(() => ((TextContentBlock)row.Block).RawText == "abcde");

        Assert.Equal(0, refreshes);
        Assert.Equal("a", custom.VisibleText());
        Assert.Equal(1, dispatcher.PendingCount);

        dispatcher.RunPending();
        Assert.Equal(1, refreshes);
        Assert.Equal("abcde", custom.VisibleText());

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
        var client = new ScriptedChatClient().Reply("One").Reply("Two");
        var view = ChatHost.Create(ChatHost.Session(client));
        var requests = new List<int>();
        view.MessageList.ScrollToRequested += (_, e) => requests.Add(e.Index);

        await SendAsync(view, "first");
        Assert.NotEmpty(requests);
        Assert.Equal(view.EndIndex(), requests[^1]);

        // Following scrolls to the end row: a point-tall item under the newest row, outside Rows.
        var items = (IList<ChatRow>)view.MessageList.ItemsSource;
        Assert.Equal(view.Rows().Count, view.EndIndex());
        Assert.IsType<EndRow>(items[view.EndIndex()]);
        Assert.Equal(1, Assert.IsType<ContentView>(view.Realize(items[view.EndIndex()])).HeightRequest);

        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -40, LastVisibleItemIndex = 0 });
        Assert.False(view.MessageList.IsPinned);
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = 40, LastVisibleItemIndex = view.EndIndex() });
        Assert.True(view.MessageList.IsPinned);

        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -40, LastVisibleItemIndex = 0 });
        await SendAsync(view, "second");
        Assert.True(view.MessageList.IsPinned);
        Assert.Equal(view.EndIndex(), requests[^1]);

        // A scroll that leaves the list at its end keeps following. One that takes the end row off screen unpins,
        // even with the newest row still on screen.
        var end = view.EndIndex();
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -2, LastVisibleItemIndex = end });
        Assert.True(view.MessageList.IsPinned);
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -2, LastVisibleItemIndex = end - 1 });
        Assert.False(view.MessageList.IsPinned);
    });

    [Fact]
    public void ScrollsRaisedAfterARowGetsShorter_DoNotUnpinTheList() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient()
            .Reply(new FunctionCallContent("c1", "delete_file", new Dictionary<string, object?> { ["path"] = "a.txt" }));
        var answer = client.ReplyLive();
        var view = ChatHost.Create(v =>
        {
            v.ChatClient = client;
            v.Tools = [new ApprovalRequiredAIFunction(AIFunctionFactory.Create((string path) => $"removed {path}", "delete_file"))];
        });
        var requests = new List<int>();
        view.MessageList.ScrollToRequested += (_, e) => requests.Add(e.Index);

        view.Text = "Delete a.txt";
        var send = view.SendAsync();
        await UIThread.Until(() => view.EffectiveSession!.Status == ConversationStatus.AwaitingInput);
        var row = Assert.Single(view.Rows().OfType<BlockRow>(), row => row.Block is ToolApprovalBlock);
        var approval = Assert.IsType<ToolApprovalView>(((BlockHost)view.Realize(row)).HostedView);

        // Approving collapses the card at once. A list at the end clamps its offset in the next layout pass and
        // reports that as scrolls back, in quick succession (as iOS does), with the list still at its end.
        approval.ApproveCommand.Execute(null);
        await UIThread.Until(() => !approval.Buttons.IsVisible);
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -49.3, LastVisibleItemIndex = view.EndIndex() });
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -0.3, LastVisibleItemIndex = view.EndIndex() });
        Assert.True(view.MessageList.IsPinned);

        // So the answer that follows still scrolls into view.
        answer.Text("Deleted.");
        await UIThread.Until(() => view.Rows()[^1] is BlockRow { Block: TextContentBlock });
        Assert.Equal(view.EndIndex(), requests[^1]);

        // A scroll back that takes the end off screen is the user reading back.
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -10, LastVisibleItemIndex = view.EndIndex() - 1 });
        Assert.False(view.MessageList.IsPinned);

        answer.Complete();
        await send;
    });

    [Fact]
    public void ASlowDragBackWhileAnAnswerStreams_UnpinsAndStaysUnpinned() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client));
        using var dispatcher = TestDispatcher.Install();
        var requests = new List<int>();
        view.MessageList.ScrollToRequested += (_, e) => requests.Add(e.Index);

        view.Text = "Hi";
        var send = view.SendAsync();
        stream.Text("An answer");
        await UIThread.Until(() => view.Session!.Turns.Count == 1 && view.Session.Turns[0].ResponseBlocks.Count == 1);
        dispatcher.RunPending();
        var block = (TextContentBlock)Assert.IsType<BlockRow>(view.Rows()[^1]).Block;
        var answer = view.Rows().Count - 1;
        var end = view.EndIndex();

        async Task StreamAsync(string text)
        {
            var expected = block.RawText + text;
            requests.Clear();
            stream.Text(text);
            await UIThread.Until(() => block.RawText == expected);
            dispatcher.RunPending();
        }

        // The answer is taller than the screen, so its row stays on screen throughout; only the end row under it
        // tells whether the list is still at its end.
        void Scroll(double delta, bool atEnd) =>
            view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = delta, LastVisibleItemIndex = atEnd ? end : answer });

        // The platform correcting its layout (Markdown settling as it streams, rows re-measured, rounding) leaves the
        // list at its end, in steps as quick as a drag's: it keeps following.
        Scroll(-12, atEnd: true);
        Scroll(-28, atEnd: true);
        Scroll(-0.5, atEnd: true);
        await StreamAsync(" that");
        Assert.True(view.MessageList.IsPinned);
        Assert.Equal(end, Assert.Single(requests));

        // A slow drag back is the user: within a few points the end row leaves the screen, with the answer on it.
        Scroll(-3, atEnd: true);
        Scroll(-3, atEnd: false);
        Assert.False(view.MessageList.IsPinned);

        // The answer keeps streaming without pulling the reader back down, even when the finger wavers.
        await StreamAsync(" keeps");
        Scroll(-3, atEnd: false);
        Scroll(2, atEnd: false);
        await StreamAsync(" growing");
        Assert.False(view.MessageList.IsPinned);
        Assert.Empty(requests);

        // Scrolling back down to the end follows the answer again.
        Scroll(7, atEnd: true);
        Assert.True(view.MessageList.IsPinned);
        await StreamAsync(".");
        Assert.Equal(end, Assert.Single(requests));

        stream.Complete();
        await UIThread.Until(() => dispatcher.PendingCount > 0 || !view.IsBusy);
        dispatcher.RunPending();
        await send;
    });

    [Fact]
    public void ClearingTheConversation_FollowsTheEndAgain() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("One").Reply("Two");
        var view = ChatHost.Create(ChatHost.Session(client));
        var requests = new List<int>();
        view.MessageList.ScrollToRequested += (_, e) => requests.Add(e.Index);

        await SendAsync(view, "first");
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -40, LastVisibleItemIndex = 0 });
        Assert.False(view.MessageList.IsPinned);

        // The app clears the conversation (not the composer): nothing is left to read back through.
        await view.Session!.ClearAsync();
        Assert.Empty(view.Rows());
        Assert.Empty((IList<ChatRow>)view.MessageList.ItemsSource);
        Assert.True(view.MessageList.IsPinned);

        requests.Clear();
        await view.Session.SendMessageAsync("second");
        Assert.Equal(view.EndIndex(), requests[^1]);
    });

    [Fact]
    public void ATurnSentByTheApp_FollowsTheEndAgain() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("One");
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client));
        var requests = new List<int>();
        view.MessageList.ScrollToRequested += (_, e) => requests.Add(e.Index);

        await SendAsync(view, "first");
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -40, LastVisibleItemIndex = 0 });
        Assert.False(view.MessageList.IsPinned);

        // Sent by the app (a suggestion-like button, a replay), not through the composer.
        requests.Clear();
        var send = view.Session!.SendMessageAsync("second");
        stream.Text("Two");
        await UIThread.Until(() => view.Rows()[^1] is BlockRow { Block: TextContentBlock { RawText: "Two" } });
        Assert.True(view.MessageList.IsPinned);
        Assert.Equal(view.EndIndex(), requests[^1]);

        // Once the reader scrolls back, the rest of that answer streams in without pulling them down again.
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -40, LastVisibleItemIndex = 0 });
        requests.Clear();
        stream.Text(" and more");
        await UIThread.Until(() => view.Rows()[^1] is BlockRow { Block: TextContentBlock { RawText: "Two and more" } });
        stream.Complete();
        await send;

        Assert.False(view.MessageList.IsPinned);
        Assert.Empty(requests);
    });

    [Fact]
    public void ContentThatGrowsOnItsOwn_IsFollowedWhilePinned() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("Look: ![logo](https://example.com/logo.png)");
        var view = ChatHost.Create(ChatHost.Session(client));
        var requests = new List<int>();
        view.MessageList.ScrollToRequested += (_, e) => requests.Add(e.Index);
        await SendAsync(view, "Show me the logo");

        var answer = Assert.Single(view.Rows().OfType<BlockRow>(), row => row.Block is TextContentBlock);
        var image = Assert.Single(view.Realize(answer).Descendants<LinkedImageView>());

        // The image loaded (or fell back to a chip) after its row was laid out, so it took a new size: keep following.
        requests.Clear();
        ((IView)image).Arrange(new Rect(0, 0, 320, 200));
        Assert.Equal(view.EndIndex(), Assert.Single(requests));

        // Unless the reader has scrolled back.
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -40, LastVisibleItemIndex = 0 });
        requests.Clear();
        ((IView)image).Arrange(new Rect(0, 0, 320, 120));
        Assert.Empty(requests);
    });

    [Fact]
    public void ScrollsWhileTheListSettlesAfterContentResized_DoNotUnpinIt() => UIThread.Run(async () =>
    {
        var (view, image, requests) = await ShowImageAnswerAsync();
        using var dispatcher = TestDispatcher.Install();
        var end = view.EndIndex();
        void Scroll(double delta, bool atEnd) =>
            view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = delta, LastVisibleItemIndex = atEnd ? end : end - 1 });

        // The image took its size after its row was laid out, and the list follows it to the new end. A list the
        // platform leaves at its end meanwhile is not scrolled again.
        requests.Clear();
        ((IView)image).Arrange(new Rect(0, 0, 320, 200));
        dispatcher.RunPending();
        Scroll(-0.5, atEnd: true);
        dispatcher.RunPending();
        Assert.Equal(end, Assert.Single(requests));

        // While it settles, the platform can move it back in small steps that take the end row off screen (iOS and
        // Mac Catalyst animate the change). That is not the reader: the list keeps following, and goes back to its end
        // once settled.
        ((IView)image).Arrange(new Rect(0, 0, 320, 240));
        dispatcher.RunPending();
        requests.Clear();
        Scroll(-1.5, atEnd: false);
        Scroll(-1, atEnd: false);
        Assert.True(view.MessageList.IsPinned);
        dispatcher.RunPending();
        Assert.Equal(end, Assert.Single(requests));

        // That scroll can move it again, once.
        requests.Clear();
        Scroll(-2, atEnd: false);
        Assert.True(view.MessageList.IsPinned);
        dispatcher.RunPending();
        Assert.Equal(end, Assert.Single(requests));

        // Once settled, a scroll back is the reader again.
        Scroll(-1.5, atEnd: false);
        Assert.False(view.MessageList.IsPinned);
    });

    [Fact]
    public void AFollowThatLandsShortOfTheEnd_IsCorrectedOnceSettled() => UIThread.Run(async () =>
    {
        var (view, image, requests) = await ShowImageAnswerAsync();
        using var dispatcher = TestDispatcher.Install();
        var end = view.EndIndex();

        // iOS reports the scroll at once, placed from the rows' estimated heights: short of the end.
        var landShort = true;
        view.MessageList.ScrollToRequested += (_, _) =>
        {
            if (landShort)
                view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = 308, LastVisibleItemIndex = end - 2 });
        };

        requests.Clear();
        ((IView)image).Arrange(new Rect(0, 0, 320, 200));
        dispatcher.RunPending();
        Assert.Equal(end, Assert.Single(requests));

        landShort = false;
        dispatcher.RunPending();
        Assert.Equal([end, end], requests);
        Assert.True(view.MessageList.IsPinned);
    });

    [Fact]
    public void ADragBackWhileTheListSettles_UnpinsOnceItGoesFarBack() => UIThread.Run(async () =>
    {
        var (view, image, requests) = await ShowImageAnswerAsync();
        using var dispatcher = TestDispatcher.Install();
        var end = view.EndIndex();

        ((IView)image).Arrange(new Rect(0, 0, 320, 200));
        dispatcher.RunPending();

        // The platform settling moves the list back a few points; the reader drags it much further.
        for (var step = 0; step < 2; step++)
            view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -30, LastVisibleItemIndex = end - 1 });
        Assert.True(view.MessageList.IsPinned);
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -30, LastVisibleItemIndex = end - 1 });
        Assert.False(view.MessageList.IsPinned);

        // And stays where they took it once the list settled.
        requests.Clear();
        dispatcher.RunPending();
        Assert.Empty(requests);
    });

    [Fact]
    public void AJumpToTheTopWhileFollowing_GoesBackToTheEnd() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("One").Reply("Two");
        var view = ChatHost.Create(ChatHost.Session(client));
        var requests = new List<int>();
        view.MessageList.ScrollToRequested += (_, e) => requests.Add(e.Index);
        await SendAsync(view, "first");
        await SendAsync(view, "second");
        ((IView)view.MessageList).Arrange(new Rect(0, 0, 400, 600));

        // iOS re-estimating rows it has not measured can drop a long conversation to its top in one step.
        requests.Clear();
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -1480.7, VerticalOffset = 0, LastVisibleItemIndex = 1 });
        Assert.True(view.MessageList.IsPinned);
        Assert.Equal(view.EndIndex(), Assert.Single(requests));

        // The reader takes many steps to get there, and the first unpins the list.
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -120, VerticalOffset = 1360, LastVisibleItemIndex = view.EndIndex() - 1 });
        Assert.False(view.MessageList.IsPinned);
        requests.Clear();
        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -1360, VerticalOffset = 0, LastVisibleItemIndex = 1 });
        Assert.Empty(requests);
    });

    [Fact]
    public void ScrollsWhileTheListSettlesAfterRowsChanged_DoNotUnpinIt() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client));
        using var dispatcher = TestDispatcher.Install();
        var requests = new List<int>();
        view.MessageList.ScrollToRequested += (_, e) => requests.Add(e.Index);

        view.Text = "Hi";
        var send = view.SendAsync();
        stream.Text("An answer");
        await UIThread.Until(() => view.Session!.Turns.Count == 1 && view.Session.Turns[0].ResponseBlocks.Count == 1);
        dispatcher.RunPending();
        var block = (TextContentBlock)Assert.IsType<BlockRow>(view.Rows()[^1]).Block;
        var end = view.EndIndex();
        void Scroll(double delta, bool atEnd) =>
            view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = delta, LastVisibleItemIndex = atEnd ? end : end - 1 });

        // The answer's row was added, and iOS animates that: the list moves back a few points, end row off screen.
        Scroll(-2.7, atEnd: false);
        Scroll(-3, atEnd: false);
        Assert.True(view.MessageList.IsPinned);
        requests.Clear();
        dispatcher.RunPending();
        Assert.Equal(end, Assert.Single(requests));
        dispatcher.RunPending();

        // Text streaming into the row adds no rows: a slow drag back is the reader at once.
        requests.Clear();
        stream.Text(" that grows");
        await UIThread.Until(() => block.RawText == "An answer that grows");
        dispatcher.RunPending();
        Assert.Equal(end, Assert.Single(requests));
        Scroll(-3, atEnd: false);
        Assert.False(view.MessageList.IsPinned);

        stream.Complete();
        await UIThread.Until(() => dispatcher.PendingCount > 0 || !view.IsBusy);
        dispatcher.RunPending();
        await send;
    });

    [Fact]
    public void ResizingTheList_KeepsTheNewestRowInViewWhilePinned() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("One");
        var view = ChatHost.Create(ChatHost.Session(client));
        var requests = new List<int>();
        view.MessageList.ScrollToRequested += (_, e) => requests.Add(e.Index);
        await SendAsync(view, "first");

        // The composer grew (staged attachments, a long draft), so the list got shorter.
        requests.Clear();
        ((IView)view.MessageList).Arrange(new Rect(0, 0, 400, 600));
        Assert.Equal(view.EndIndex(), Assert.Single(requests));

        view.MessageList.SendScrolled(new ItemsViewScrolledEventArgs { VerticalDelta = -40, LastVisibleItemIndex = 0 });
        requests.Clear();
        ((IView)view.MessageList).Arrange(new Rect(0, 0, 400, 500));
        Assert.Empty(requests);
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
        Assert.Equal(view.EndIndex(), requests[^1]);

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
        var image = Assert.IsType<ChatImage>(media.MediaView);
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

    private static async Task<(AIChatView View, LinkedImageView Image, List<int> Requests)> ShowImageAnswerAsync()
    {
        var client = new ScriptedChatClient().Reply("Look: ![logo](https://example.com/logo.png)");
        var view = ChatHost.Create(ChatHost.Session(client));
        var requests = new List<int>();
        view.MessageList.ScrollToRequested += (_, e) => requests.Add(e.Index);
        await SendAsync(view, "Show me the logo");

        var answer = Assert.Single(view.Rows().OfType<BlockRow>(), row => row.Block is TextContentBlock);
        return (view, Assert.Single(view.Realize(answer).Descendants<LinkedImageView>()), requests);
    }

    private static async Task SendAsync(AIChatView view, string text)
    {
        view.Text = text;
        await view.SendAsync();
    }
}
