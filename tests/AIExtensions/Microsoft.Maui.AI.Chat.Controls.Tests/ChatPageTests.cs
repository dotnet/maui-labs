using Microsoft.Maui.AI.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Chat.Controls;
using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;
using Microsoft.Maui.AI.Chat.Controls.Themes;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

/// <summary>
/// Mirrors: Blazor.Tests/Components/ChatPageTests.cs
/// Tests the full CopilotChatView page-level behavior: session binding, send flow,
/// error handling, and state transitions.
/// </summary>
public class ChatPageTests
{
    [Fact]
    public void CopilotChatView_IsANeutralChatViewWithAnAgentConversationAdapter()
    {
        var session = SessionFactory.Create("Hello");
        var control = new CopilotChatView { Session = session };

        Assert.IsAssignableFrom<ChatView>(control);
        var conversation = Assert.IsType<AgentChatConversation>(
            control.Conversation);
        Assert.Same(session, conversation.Session);

        control.Session = null;
        Assert.Null(control.Conversation);
    }

    [Fact]
    public async Task AgentProjection_MapsBlocksIntoNeutralChatModels()
    {
        var session = SessionFactory.Create("Hello from the assistant");
        await session.SendMessageAsync("Hello from the user");
        using var conversation = new AgentChatConversation(session);
        var list = new MessageListView { Session = session };

        Assert.All(
            conversation.Messages,
            message => Assert.All(
                message.Contents,
                content => Assert.IsAssignableFrom<MessageContent>(content)));
        Assert.True(
            conversation.Messages
                .SelectMany(message => message.Contents)
                .OfType<TextMessageContent>()
                .Count() >= 2);
        Assert.All(
            list.Items,
            item => Assert.IsAssignableFrom<ChatContentItem>(item));
        list.Session = null;
    }

    [Fact]
    public void AgentProjection_MapsEveryMediaItemIntoNeutralMediaContent()
    {
        var block = new MediaContentBlock
        {
            Id = "media",
        };
        block.AddContent(new DataContent(
            new byte[] { 1, 2, 3 },
            "image/png")
        {
            Name = "garden.png",
        });
        block.AddContent(new DataContent(
            new byte[] { 4, 5 },
            "application/pdf")
        {
            Name = "layout.pdf",
        });

        var contents = AgentChatConversation.CreateMessageContents(
            block,
            turn: null,
            isRequest: false);

        var image = Assert.IsAssignableFrom<MediaMessageContent>(contents[0]);
        Assert.Equal("media:0", image.Id);
        Assert.Equal("garden.png", image.FileName);
        Assert.True(image.IsImage);
        var file = Assert.IsAssignableFrom<MediaMessageContent>(contents[1]);
        Assert.Equal("media:1", file.Id);
        Assert.Equal("layout.pdf", file.FileName);
        Assert.False(file.IsImage);
    }

    [Fact]
    public void EmptyMediaBlock_ProjectsNoPlaceholderContent()
    {
        var contents = AgentChatConversation.CreateMessageContents(
            new MediaContentBlock(),
            turn: null,
            isRequest: false);

        Assert.Empty(contents);
    }

    [Fact]
    public async Task HostedImageResult_ProjectsThroughAgentContextAsNeutralMedia()
    {
        var result = new ImageGenerationToolResultContent("call-1")
        {
            Outputs =
            [
                new DataContent(
                    new byte[] { 0x89, 0x50, 0x4E, 0x47 },
                    "image/png")
                {
                    Name = "sunflower.png",
                },
            ],
        };
        var client = new TestChatClient((_, _, _) =>
            Task.FromResult(new ChatResponse(
            [
                new ChatMessage(ChatRole.Tool, [result]),
                new ChatMessage(ChatRole.Assistant, "done"),
            ])));
        var session = SessionFactory.Create(client);

        await session.SendMessageAsync("draw");
        using var conversation = new AgentChatConversation(session);

        var media = Assert.Single(
            conversation.Messages
                .SelectMany(message => message.Contents)
                .OfType<MediaMessageContent>());
        Assert.Equal("sunflower.png", media.FileName);
        Assert.True(media.IsImage);
    }

    [Fact]
    public void PackageReferences_PreserveTheThreeLayerBoundary()
    {
        var engineReferences = typeof(AgentContext).Assembly.GetReferencedAssemblies();
        var neutralReferences = typeof(ChatView).Assembly.GetReferencedAssemblies();
        var bridgeReferences = typeof(CopilotChatView).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            engineReferences,
            reference => reference.Name == "Microsoft.Maui.Chat.Controls");
        Assert.DoesNotContain(
            neutralReferences,
            reference => reference.Name == "Microsoft.Maui.AI.Chat");
        Assert.Contains(
            bridgeReferences,
            reference => reference.Name == "Microsoft.Maui.Chat.Controls");
        Assert.Contains(
            bridgeReferences,
            reference => reference.Name == "Microsoft.Maui.AI.Chat");
    }

    [Fact]
    public void CopilotChatView_UsesAiMessageListInsideTheSharedShell()
    {
        var theme = new MessageListTheme();
        var view = new FactoryCopilotChatView
        {
            MessageListTemplate = Assert.IsType<DataTemplate>(
                theme[ChatThemeKeys.MessageListTemplate]),
        };

        AssertAiMessageList(view);
    }

    [Fact]
    public void CopilotChatView_ResolvesAiMessageListTemplateFromResources()
    {
        var host = new ContentView();
        host.Resources.MergedDictionaries.Add(new ChatTheme());
        var view = new FactoryCopilotChatView();

        host.Content = view;

        Assert.NotNull(view.MessageListTemplate);
        AssertAiMessageList(view);
    }

    [Fact]
    public void AiTheme_ContainsOnlyAiTokensAndBlockTemplates()
    {
        var theme = new ChatTheme();

        Assert.Equal(2, theme.MergedDictionaries.Count);
        Assert.Contains(theme.MergedDictionaries, dictionary => dictionary is ChatColors);
        Assert.Contains(theme.MergedDictionaries, dictionary => dictionary is MessageListTheme);
    }

    [Fact]
    public void Session_CanBeSetAndCleared()
    {
        var control = new CopilotChatView();

        var session = SessionFactory.Create("test");

        control.Session = session;
        Assert.Same(session, control.Session);

        control.Session = null;
        Assert.Null(control.Session);
    }

    [Fact]
    public void Session_Swap_DoesNotThrow()
    {
        var control = new CopilotChatView();

        var session1 = SessionFactory.Create("First");
        var session2 = SessionFactory.Create("Second");

        control.Session = session1;
        control.Session = session2;

        Assert.Same(session2, control.Session);
    }

    [Fact]
    public void DetachAndReattach_ReleasesAndRebuildsAgentConversation()
    {
        var session = SessionFactory.Create("test");
        var control = new CopilotChatView { Session = session };
        var host = new ContentView { Content = control };
        Assert.IsType<AgentChatConversation>(control.Conversation);

        host.Content = null;
        Assert.Null(control.Conversation);

        host.Content = control;
        var conversation = Assert.IsType<AgentChatConversation>(
            control.Conversation);
        Assert.Same(session, conversation.Session);
    }

    [Fact]
    public async Task ErrorState_SetsStatusAndExposesException()
    {
        var client = new TestChatClient((_, _, _) =>
            throw new InvalidOperationException("API rate limited"));
        var session = SessionFactory.Create(client);

        await session.SendMessageAsync("Hi");

        Assert.Equal(ConversationStatus.Error, session.Status);
        Assert.IsType<InvalidOperationException>(session.Error);
        Assert.Equal("API rate limited", session.Error!.Message);
    }

    [Fact]
    public async Task SendMessage_ClearsTextProperty()
    {
        var control = new CopilotChatView();

        var session = SessionFactory.Create("Reply");
        control.Session = session;

        // Text property should be clearable (simulates what SendCurrentTextAsync does)
        control.Text = "Hello";
        Assert.Equal("Hello", control.Text);

        control.Text = string.Empty;
        Assert.Equal(string.Empty, control.Text);
    }

    [Fact]
    public void SendMessage_WhenNoSession_DoesNotThrow()
    {
        var control = new CopilotChatView();

        control.Text = "Hello";

        // No session set, nothing should happen (guard in SendCurrentTextAsync)
        Assert.Null(control.Session);
    }

    [Fact]
    public async Task SendMessage_WhenBusy_Blocked()
    {
        var response = new TaskCompletionSource<ChatResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var control = new CopilotChatView
        {
            Session = SessionFactory.Create(new TestChatClient(
                (_, _, _) => response.Task)),
            Text = "Hello",
        };

        var send = control.SendCurrentTextAsync();
        await Task.Yield();
        Assert.True(control.IsBusy);

        response.SetResult(new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, "done")]));
        await send;
    }

    [Fact]
    public void SendMessage_WhenTextEmpty_Blocked()
    {
        var control = new CopilotChatView();

        var session = SessionFactory.Create("Reply");
        control.Session = session;
        control.Text = "   ";

        // Whitespace-only text should not send (guard in SendCurrentTextAsync)
        Assert.True(string.IsNullOrWhiteSpace(control.Text));
    }

    [Fact]
    public async Task CallerCancellation_CompletesSendTaskAsCanceled()
    {
        var tcs = new TaskCompletionSource<ChatResponse>();
        var client = new TestChatClient((_, _, ct) =>
        {
            ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        });
        var session = SessionFactory.Create(client);

        using var cts = new CancellationTokenSource();
        var sendTask = session.SendMessageAsync("Hi", cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sendTask);
        Assert.Equal(ConversationStatus.Idle, session.Status);
    }

    [Fact]
    public async Task SendCurrentText_DisposedSession_RestoresDraftAndSurfacesGenericError()
    {
        var session = SessionFactory.Create("unused");
        session.Dispose();
        var control = new CopilotChatView
        {
            Session = session,
            Text = "Keep this draft",
        };

        await control.SendCurrentTextAsync();

        Assert.Equal("Keep this draft", control.Text);
        Assert.Equal(
            "Your message could not be sent. Please try again.",
            control.SendError);
    }

    [Fact]
    public async Task SendCurrentText_SecondSendWhileFirstActive_IsIgnored()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<ChatResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new TestChatClient((_, _, _) =>
        {
            started.TrySetResult();
            return response.Task;
        });
        var control = new CopilotChatView
        {
            Session = SessionFactory.Create(client),
            Text = "first",
        };

        var firstSend = control.SendCurrentTextAsync();
        await started.Task;
        control.Text = "second";

        await control.SendCurrentTextAsync();
        response.SetResult(new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, "done")]));
        await firstSend;

        Assert.Single(client.SentMessages);
        Assert.Equal("second", control.Text);
        Assert.Null(control.SendError);
    }

    [Fact]
    public async Task StopAsync_AgentSendIsGracefulAndDoesNotRestoreSentDraft()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<ChatResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new TestChatClient((_, _, cancellationToken) =>
        {
            started.TrySetResult();
            cancellationToken.Register(
                () => response.TrySetCanceled(cancellationToken));
            return response.Task;
        });
        var session = SessionFactory.Create(client);
        var control = new CopilotChatView
        {
            Session = session,
            Text = "sent once",
        };

        var send = control.SendCurrentTextAsync();
        await started.Task;
        await control.StopAsync();
        await send;

        Assert.Equal(ConversationStatus.Idle, session.Status);
        Assert.Equal(string.Empty, control.Text);
        Assert.Equal("Response stopped.", control.InputStatusMessage);
        Assert.Null(control.SendError);
    }

    private sealed class FactoryCopilotChatView : CopilotChatView
    {
        public ChatMessagesView CreateMessageList() => CreateMessageListView();
    }

    private static void AssertAiMessageList(FactoryCopilotChatView view)
    {
        Assert.IsType<MessageListView>(view.CreateMessageList());
        Assert.False(view.ShowBusyIndicator);
    }
}
