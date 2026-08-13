using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk.Tests;

public class StatefulSessionTests
{
    private static FakeCopilotSession IdleSession(string id = "session-1") => new(id)
    {
        OnSend = (s, _) =>
        {
            s.EmitAll(SdkEvents.Delta("ok", "m1"), SdkEvents.Idle());
            return Task.CompletedTask;
        },
    };

    [Fact]
    public async Task Null_conversation_id_creates_a_new_session()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(IdleSession("new-session"));

        await using var client = TestChatClient.Create(backend);
        var response = await client.GetResponseAsync(TestExtensions.UserMessage("hi"));

        var call = Assert.Single(backend.Calls);
        Assert.Equal(RecordedSessionCallKind.Create, call.Kind);
        Assert.Equal("new-session", response.ConversationId);
    }

    [Fact]
    public async Task Set_conversation_id_resumes_the_session_without_continuing_pending_work()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(IdleSession("existing"));

        await using var client = TestChatClient.Create(backend);
        var response = await client.GetResponseAsync(
            TestExtensions.UserMessage("continue please"),
            new ChatOptions { ConversationId = "existing" });

        var call = Assert.Single(backend.Calls);
        Assert.Equal(RecordedSessionCallKind.Resume, call.Kind);
        Assert.False(call.ContinuePendingWork);
        Assert.Equal("existing", call.SessionId);
        Assert.Equal("existing", response.ConversationId);

        // Only the latest user message is sent on a follow-up (the runtime holds the durable history).
        var session = backend.Sessions[0];
        Assert.Equal("continue please", session.SentMessages[0].Prompt);
    }

    [Fact]
    public async Task Historical_tool_results_do_not_misclassify_a_new_user_turn()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(IdleSession("existing"));
        await using var client = TestChatClient.Create(backend);
        List<ChatMessage> history =
        [
            new ChatMessage(ChatRole.User, "Check weather"),
            new ChatMessage(
                ChatRole.Assistant,
                [new FunctionCallContent("call-1", "get_weather")]),
            new ChatMessage(
                ChatRole.Tool,
                [new FunctionResultContent("call-1", "Sunny")]),
            new ChatMessage(ChatRole.User, "What should I wear?"),
        ];

        var response = await client.GetResponseAsync(
            history,
            new ChatOptions { ConversationId = "existing" });

        Assert.Equal("ok", response.Text);
        var call = Assert.Single(backend.Calls);
        Assert.Equal(RecordedSessionCallKind.Resume, call.Kind);
    }

    [Fact]
    public async Task Initial_multi_message_history_is_preserved_as_a_transcript()
    {
        var backend = new FakeCopilotBackend();
        var session = backend.AddSession(IdleSession());

        await using var client = TestChatClient.Create(backend);
        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.System, "Be concise."),
            new ChatMessage(ChatRole.User, "What is 2+2?"),
            new ChatMessage(ChatRole.Assistant, "4"),
            new ChatMessage(ChatRole.User, "And 3+3?"),
        ];

        await client.GetResponseAsync(messages);

        var prompt = session.SentMessages[0].Prompt!;
        Assert.Contains("Conversation so far", prompt);
        Assert.Contains("User: What is 2+2?", prompt);
        Assert.Contains("Assistant: 4", prompt);
        Assert.Contains("Current message:", prompt);
        Assert.Contains("And 3+3?", prompt);

        // System messages are routed to the system instructions, not the transcript.
        Assert.Equal("Be concise.", backend.Calls[0].Parameters.SystemInstructions);
        Assert.DoesNotContain("Be concise.", prompt);
    }

    [Fact]
    public async Task Single_user_message_is_sent_verbatim_without_transcript_framing()
    {
        var backend = new FakeCopilotBackend();
        var session = backend.AddSession(IdleSession());

        await using var client = TestChatClient.Create(backend);
        await client.GetResponseAsync(TestExtensions.UserMessage("Just this."));

        Assert.Equal("Just this.", session.SentMessages[0].Prompt);
    }
}
