using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class AIChatViewTests
{
    [Fact]
    public void NewView_HasUsableDefaults()
    {
        var view = new AIChatView();

        Assert.Null(view.Session);
        Assert.Null(view.ChatClient);
        Assert.Empty(view.ContentTemplates);
        Assert.Empty(view.Suggestions);
        Assert.Equal(string.Empty, view.Text);
        Assert.Equal("Message", view.Placeholder);
        Assert.Equal("How can I help you today?", view.WelcomeTitle);
        Assert.True(view.IsInputEnabled);
        Assert.True(view.IsInputVisible);
        Assert.True(view.IsEmpty);
        Assert.False(view.IsBusy);
        Assert.False(view.CanSend);
        Assert.False(view.CanStop);
        Assert.False(view.HasError);
        Assert.False(view.AllowAttachments);
        Assert.False(view.AllowAudioCapture);
        Assert.False(view.AllowLiveSpeech);
        Assert.False(view.CanCaptureAudio);
        Assert.False(view.CanUseLiveSpeech);
        Assert.Equal(10 * 1024 * 1024, view.MaximumAttachmentBytes);
        Assert.Equal(10, view.MaximumAttachmentCount);
        Assert.Empty(view.Attachments);
        Assert.True(view.LiveSpeechAutoSubmit);
        Assert.True(view.ShowInterimSpeechText);
        Assert.False(view.ShowInterimAudioTranscript);
        Assert.NotNull(view.ControlTemplate);
    }

    [Fact]
    public void ContentTemplates_IsTheContentProperty()
    {
        var attribute = typeof(AIChatView).GetCustomAttributes(typeof(ContentPropertyAttribute), inherit: true)
            .Cast<ContentPropertyAttribute>()
            .Single();

        Assert.Equal(nameof(AIChatView.ContentTemplates), attribute.Name);
    }

    [Fact]
    public void Limits_IgnoreNonPositiveValues()
    {
        var view = new AIChatView { MaximumAttachmentBytes = 2048, MaximumAttachmentCount = 3 };

        view.MaximumAttachmentBytes = 0;
        view.MaximumAttachmentCount = -1;

        Assert.Equal(2048, view.MaximumAttachmentBytes);
        Assert.Equal(3, view.MaximumAttachmentCount);
    }

    [Fact]
    public void DefaultTemplate_ContainsTheComposerAndMessageList()
    {
        var view = ChatHost.Create();

        Assert.Contains(view.MessageList, view.Descendants<CollectionView>());
        Assert.Equal("AIChatInput", Assert.Single(view.Descendants<Editor>()).AutomationId);
        foreach (var id in new[] { "AIChatSendButton", "AIChatStopButton", "AIChatAttachButton", "AIChatAudioButton", "AIChatLiveSpeechButton" })
            view.ByAutomationId<Button>(id);
    }

    [Fact]
    public void Input_IsTwoWayBoundToText()
    {
        var view = ChatHost.Create();
        var editor = view.ByAutomationId<Editor>("AIChatInput");

        view.Text = "from code";
        Assert.Equal("from code", editor.Text);

        editor.Text = "typed";
        Assert.Equal("typed", view.Text);

        view.Placeholder = "Ask anything";
        Assert.Equal("Ask anything", editor.Placeholder);

        view.IsInputEnabled = false;
        Assert.False(editor.IsEnabled);
    }

    [Fact]
    public void InputSlots_HostAppContent()
    {
        var leading = new Button { Text = "L" };
        var trailing = new Button { Text = "T" };
        var view = ChatHost.Create(v =>
        {
            v.InputLeadingContent = leading;
            v.InputTrailingContent = trailing;
        });

        Assert.Contains(leading, view.Descendants<Button>());
        Assert.Contains(trailing, view.Descendants<Button>());
    }

    [Fact]
    public void InputHeaderContent_IsShownAboveTheComposerOnlyWhileSet()
    {
        var model = new object();
        var banner = new Label { Text = "Preparing the model" };
        var view = ChatHost.Create(v => v.BindingContext = model);
        var header = view.ByAutomationId<ContentView>("AIChatInputHeader");
        var inputArea = Assert.IsAssignableFrom<Layout>(header.Parent);
        var composer = inputArea.Children.OfType<Border>().Single(border => border.Descendants<Editor>().Any());

        Assert.False(header.IsVisible);

        view.InputHeaderContent = banner;

        Assert.True(header.IsVisible);
        Assert.Same(banner, header.Content);
        Assert.Same(model, banner.BindingContext);
        Assert.True(inputArea.IndexOf(header) < inputArea.IndexOf(composer));

        banner.IsVisible = false;
        Assert.False(header.IsVisible);
        banner.IsVisible = true;
        Assert.True(header.IsVisible);

        view.IsInputVisible = false;
        Assert.False(inputArea.IsVisible);

        view.InputHeaderContent = null;
        Assert.False(header.IsVisible);
    }

    [Fact]
    public void HeaderTemplate_IsShownAboveTheMessages()
    {
        var view = ChatHost.Create(v => v.HeaderTemplate = new DataTemplate(() => new Label { Text = "Assistant" }));

        Assert.Contains(view.Descendants<Label>(), label => label.Text == "Assistant");

        view.HeaderTemplate = null;
        Assert.DoesNotContain(view.Descendants<Label>(), label => label.Text == "Assistant");
    }

    [Fact]
    public void HeaderTemplate_InheritsTheBindingContextUnlessItSetsItsOwn()
    {
        var model = new object();
        var own = new object();
        var view = ChatHost.Create(v =>
        {
            v.BindingContext = model;
            v.HeaderTemplate = new DataTemplate(() => new Label { Text = "Inherited" });
        });

        Assert.Same(model, view.Descendants<Label>().Single(label => label.Text == "Inherited").BindingContext);

        view.HeaderTemplate = new DataTemplate(() => new Label { Text = "Explicit", BindingContext = own });
        Assert.Same(own, view.Descendants<Label>().Single(label => label.Text == "Explicit").BindingContext);
    }

    [Fact]
    public void EmptyView_ShowsTheWelcomeUntilTheFirstTurn() => UIThread.Run(async () =>
    {
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient().Reply("Hello")), v => v.WelcomeMessage = "Ask me anything.");

        Assert.Contains(view.Descendants<Label>(), label => label.Text == "How can I help you today?");
        Assert.Contains(view.Descendants<Label>(), label => label.Text == "Ask me anything.");
        var emptyView = view.Descendants<ContentView>().Single(content => content.Descendants<Label>().Any(label => label.Text == "Ask me anything."));
        Assert.True(emptyView.IsVisible);

        view.Text = "Hi";
        await view.SendAsync();

        Assert.False(view.IsEmpty);
        Assert.False(emptyView.IsVisible);
    });

    [Fact]
    public void EmptyViewTemplate_ReplacesTheWelcome()
    {
        var view = ChatHost.Create(v => v.EmptyViewTemplate = new DataTemplate(() => new Label { Text = "Custom empty" }));

        Assert.Contains(view.Descendants<Label>(), label => label.Text == "Custom empty");
        Assert.DoesNotContain(view.Descendants<Label>(), label => label.Text == "How can I help you today?");

        view.EmptyViewTemplate = null;
        Assert.Contains(view.Descendants<Label>(), label => label.Text == "How can I help you today?");
    }

    [Fact]
    public void MessagesHeaderTemplate_ScrollsWithTheMessagesAndInheritsTheBindingContext()
    {
        var model = new object();
        var view = ChatHost.Create(v =>
        {
            v.BindingContext = model;
            v.MessagesHeaderTemplate = new DataTemplate(() => new Label { Text = "AI can make mistakes." });
        });

        var header = Assert.IsType<Label>(view.MessageList.Header);
        Assert.Equal("AI can make mistakes.", header.Text);
        Assert.Same(model, header.BindingContext);

        var other = new object();
        view.BindingContext = other;
        Assert.Same(other, header.BindingContext);
    }

    [Fact]
    public void InputSlots_InheritTheBindingContext()
    {
        var model = new object();
        var leading = new Button { Text = "L" };
        var view = ChatHost.Create(v =>
        {
            v.InputLeadingContent = leading;
            v.BindingContext = model;
        });

        Assert.Same(model, leading.BindingContext);
    }

    [Fact]
    public void Suggestions_AreShownWhileEmptyAndSendWhenTapped() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("Sure");
        var view = ChatHost.Create(ChatHost.Session(client), v =>
        {
            v.Suggestions.Add("Plan a trip");
            v.Suggestions.Add("  ");
            v.Suggestions.Add("Write a poem");
        });

        var chips = view.Descendants<Button>().Where(button => button.AutomationId == "AIChatSuggestion").ToList();
        Assert.Equal(["Plan a trip", "Write a poem"], chips.Select(chip => chip.Text));
        Assert.True(view.ShowSuggestions);

        chips[1].Command.Execute(chips[1].CommandParameter);
        await UIThread.Until(() => !view.IsBusy && view.Session!.Turns.Count == 1);

        Assert.Equal("Write a poem", client.LastUserText);
        Assert.False(view.ShowSuggestions);
        Assert.Equal(string.Empty, view.Text);
    });

    [Fact]
    public void Suggestions_FollowCollectionChanges()
    {
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()));
        Assert.False(view.ShowSuggestions);

        view.Suggestions.Add("One");
        Assert.True(view.ShowSuggestions);
        Assert.Single(view.Descendants<Button>(), button => button.AutomationId == "AIChatSuggestion");

        view.Suggestions = new System.Collections.ObjectModel.ObservableCollection<string> { "A", "B" };
        Assert.Equal(2, view.Descendants<Button>().Count(button => button.AutomationId == "AIChatSuggestion"));

        view.Suggestions.Remove("A");
        Assert.Equal("B", Assert.Single(view.Descendants<Button>(), button => button.AutomationId == "AIChatSuggestion").Text);
    }

    [Fact]
    public void ChatClient_CreatesAndOwnsASession() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("Hello");
        var view = ChatHost.Create(v => v.ChatClient = client);

        var owned = view.OwnedChatSession;
        Assert.NotNull(owned);
        Assert.Same(owned!.Context, view.EffectiveSession);
        Assert.Null(view.Session);

        view.Text = "Hi";
        await view.SendAsync();

        Assert.Equal("Hi", client.LastUserText);
        Assert.Equal(2, view.Rows().Count);
    });

    [Fact]
    public void ChatClient_WithoutFunctionInvocation_IsWrappedSoToolsRun() => UIThread.Run(async () =>
    {
        var calls = 0;
        var client = new ScriptedChatClient()
            .Reply(new FunctionCallContent("c1", "lookup"))
            .Reply("Done");
        var view = ChatHost.Create(v =>
        {
            v.ChatClient = client;
            v.Tools = [AIFunctionFactory.Create(() => { calls++; return "found"; }, "lookup")];
        });

        Assert.NotNull(view.OwnedChatSession!.Client.GetService<FunctionInvokingChatClient>());
        Assert.Same(client, view.OwnedChatSession.Source);

        view.Text = "Find it";
        await view.SendAsync();

        Assert.Equal(1, calls);
        Assert.Equal(2, client.Requests.Count);
    });

    [Fact]
    public void ChatClient_WithFunctionInvocation_IsUsedAsIs()
    {
        var client = new ScriptedChatClient().AsBuilder().UseFunctionInvocation().Build();
        var view = ChatHost.Create(v => v.ChatClient = client);

        Assert.Same(client, view.OwnedChatSession!.Client);
    }

    [Fact]
    public void InstructionsAndTools_ReachEveryRequest() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("One").Reply("Two");
        var tool = AIFunctionFactory.Create(() => "x", "tool_a");
        var view = ChatHost.Create(v =>
        {
            v.ChatClient = client;
            v.Instructions = "Be brief.";
            v.Tools = [tool];
        });

        view.Text = "first";
        await view.SendAsync();
        Assert.Equal("Be brief.", client.RequestOptions[0]?.Instructions);
        Assert.Contains(client.RequestOptions[0]!.Tools!, t => t.Name == "tool_a");

        view.Instructions = "Be verbose.";
        view.Tools = null;
        view.Text = "second";
        await view.SendAsync();
        Assert.Equal("Be verbose.", client.RequestOptions[1]?.Instructions);
        Assert.True(client.RequestOptions[1]?.Tools is null or { Count: 0 });
    });

    [Fact]
    public void ReplacingTheChatClient_StartsANewConversation() => UIThread.Run(async () =>
    {
        var first = new ScriptedChatClient().Reply("One");
        var view = ChatHost.Create(v => v.ChatClient = first);
        view.Text = "hi";
        await view.SendAsync();
        var oldSession = view.EffectiveSession!;
        view.Text = "draft";

        var second = new ScriptedChatClient();
        view.ChatClient = second;

        Assert.NotSame(oldSession, view.EffectiveSession);
        Assert.Empty(view.Rows());
        Assert.True(view.IsEmpty);
        Assert.Equal(string.Empty, view.Text);
        Assert.False(first.IsDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => oldSession.SendMessageAsync("x"));
    });

    [Fact]
    public void DisconnectingTheHandler_StopsAndDisposesTheOwnedSession() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(v => v.ChatClient = client);
        view.Handler = new StubViewHandler();
        var owned = view.EffectiveSession!;

        view.Text = "Hi";
        var send = view.SendAsync();
        await stream.WriteAsync(new ChatResponseUpdate(ChatRole.Assistant, "Hel"));
        await UIThread.Until(() => view.Rows().Any(row => row is BlockRow));

        // MAUI disconnects the handlers of a page that is popped.
        view.Handler = null;

        // The response stops instead of streaming on (and keeping the page alive).
        await send.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(view.OwnedChatSession);
        Assert.Null(view.EffectiveSession);
        Assert.False(view.IsBusy);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => owned.SendMessageAsync("x"));
        Assert.False(client.IsDisposed);
    });

    [Fact]
    public void DisconnectingTheHandler_LeavesAnAppSessionAlone() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("One").Reply("Two");
        var session = ChatHost.Session(client);
        var view = ChatHost.Create(session);
        view.Handler = new StubViewHandler();

        view.Handler = null;

        Assert.Same(session, view.EffectiveSession);
        await session.SendMessageAsync("Still usable?");
        Assert.Equal(ConversationStatus.Idle, session.Status);
    });

    [Fact]
    public void UnloadingAndReloading_KeepsTheOwnedSession() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("One");
        var view = ChatHost.Create(v => v.ChatClient = client);
        view.Handler = new StubViewHandler();
        view.Text = "Hi";
        await view.SendAsync();
        var owned = view.EffectiveSession!;

        // Switching tabs unloads the view but keeps its handler (replacing the window's page would disconnect it).
        var page = (ContentPage)view.Parent;
        page.Content = null;
        Assert.False(view.IsLoaded);
        page.Content = view;
        Assert.True(view.IsLoaded);

        Assert.NotNull(view.Handler);
        Assert.Same(owned, view.EffectiveSession);
        Assert.Single(owned.Turns);
        Assert.Contains(view.Rows(), row => row is BlockRow);
    });

    [Fact]
    public void ExplicitSession_WinsOverTheChatClient() => UIThread.Run(async () =>
    {
        var sessionClient = new ScriptedChatClient().Reply("From session");
        var session = ChatHost.Session(sessionClient);
        var convenience = new ScriptedChatClient();
        var view = ChatHost.Create(v =>
        {
            v.ChatClient = convenience;
            v.Session = session;
        });

        Assert.Same(session, view.EffectiveSession);
        Assert.Null(view.OwnedChatSession);

        view.Text = "Hi";
        await view.SendAsync();
        Assert.Single(sessionClient.Requests);
        Assert.Empty(convenience.Requests);

        view.Session = null;
        Assert.NotNull(view.OwnedChatSession);
        Assert.Same(view.OwnedChatSession!.Context, view.EffectiveSession);
    });

    [Fact]
    public void WithoutSessionOrClient_ResolvesTheChatClientFromServicesWhenLoaded() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("From DI");
        var services = new ServiceCollection().AddSingleton<IChatClient>(client).BuildServiceProvider();
        var view = new AIChatView { ServiceProviderOverride = services };
        Assert.Null(view.EffectiveSession);

        ChatHost.Load(view);

        Assert.NotNull(view.OwnedChatSession);
        Assert.Same(client, view.OwnedChatSession!.Source);
        view.Text = "Hi";
        await view.SendAsync();
        Assert.Equal("Hi", client.LastUserText);
    });

    [Fact]
    public void BusyState_FollowsTheSession() => UIThread.Run(async () =>
    {
        var gate = Waiters.Signal();
        var client = new ScriptedChatClient().WaitThenReply(gate.Task, "Done");
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Hi";
        var send = view.SendAsync();
        await UIThread.Until(() => view.IsBusy);

        Assert.True(view.CanStop);
        Assert.True(view.StopCommand.CanExecute(null));
        view.Text = "next";
        Assert.False(view.CanSend);
        Assert.False(view.SendCommand.CanExecute(null));
        Assert.False(view.ByAutomationId<Button>("AIChatSendButton").IsVisible);
        Assert.True(view.ByAutomationId<Button>("AIChatStopButton").IsVisible);

        gate.SetResult();
        await send;

        Assert.False(view.IsBusy);
        Assert.False(view.CanStop);
        Assert.True(view.CanSend);
        Assert.True(view.ByAutomationId<Button>("AIChatSendButton").IsVisible);
        Assert.False(view.ByAutomationId<Button>("AIChatStopButton").IsVisible);
    });

    [Fact]
    public void Unloaded_ReleasesTheView() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("Hello").Reply("Later");
        var session = ChatHost.Session(client);
        var reference = await CreateLoadSendAndUnloadAsync(session);

        for (var i = 0; i < 3 && reference.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Yield();
        }

        Assert.False(reference.IsAlive);
        await session.SendMessageAsync("still usable");
        Assert.Equal(2, session.Turns.Count);
    });

    [Fact]
    public void SwappingSessions_ReleasesTheOldSession() => UIThread.Run(async () =>
    {
        var view = ChatHost.Create();
        var reference = await AssignSessionAndSendAsync(view);

        view.Session = ChatHost.Session(new ScriptedChatClient());
        for (var i = 0; i < 3 && reference.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Yield();
        }

        Assert.False(reference.IsAlive);
    });

    [Fact]
    public void ReloadedView_ResumesTrackingTheSession() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("One").Reply("Two");
        var session = ChatHost.Session(client);
        var view = ChatHost.Create(session);

        ChatHost.Unload(view);
        await session.SendMessageAsync("while unloaded");
        Assert.True(view.IsEmpty);

        ChatHost.Load(view);
        Assert.False(view.IsEmpty);
        Assert.Equal(2, view.Rows().Count);

        view.Text = "after";
        await view.SendAsync();
        Assert.Equal(4, view.Rows().Count);
    });

    [Fact]
    public void FocusInput_WithoutATemplate_ReturnsFalse()
    {
        Assert.False(new AIChatView().FocusInput());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> AssignSessionAndSendAsync(AIChatView view)
    {
        var session = ChatHost.Session(new ScriptedChatClient().Reply("Hello"));
        view.Session = session;
        view.Text = "Hi";
        await view.SendAsync();
        return new WeakReference(session);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> CreateLoadSendAndUnloadAsync(AgentContext session)
    {
        var view = ChatHost.Create(session, v =>
        {
            v.ContentTemplates.Add(new BlockTemplate { When = BlockMatch.Is<TextContentBlock>(), Template = new ViewTemplate(typeof(CountingBlockView)) });
            v.Suggestions.Add("Hello");
        });
        view.Text = "Hi";
        await view.SendAsync();
        foreach (var row in view.Rows())
            view.Realize(row);

        ChatHost.Unload(view);
        if (view.Parent is ContentPage page)
            page.Content = null;

        return new WeakReference(view);
    }

    /// <summary>A handler with no platform view, so the view can be connected and disconnected in tests.</summary>
    private sealed class StubViewHandler() : Microsoft.Maui.Handlers.ViewHandler<IView, object>(new PropertyMapper<IView, StubViewHandler>())
    {
        protected override object CreatePlatformView() => new();
    }
}
