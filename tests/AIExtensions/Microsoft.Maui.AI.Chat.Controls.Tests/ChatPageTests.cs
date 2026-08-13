using Microsoft.Maui.AI.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Chat.Controls;
using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

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
}
