// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.AI.Chat.Tests.TestHelpers;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

public class ConversationThreadTests
{
    [Fact]
    public void CompleteTurn_CommitsPendingUpdatesExactlyOnce()
    {
        var thread = new InMemoryConversationThread("thread-1");

        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "Hello")
        {
            MessageId = "user-1",
        });
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-1",
            Contents = [new TextContent("Hi")],
        });

        Assert.Empty(thread.GetUpdates());
        Assert.Empty(thread.GetMessageHistory());

        thread.CompleteTurn();

        Assert.Equal(2, thread.GetUpdates().Count);
        Assert.Equal(2, thread.GetMessageHistory().Count);
        Assert.Equal(1, thread.CompleteTurnCallCount);
        Assert.Equal(1, thread.CommittedTurnCount);
    }

    [Fact]
    public void NewUserMessage_DiscardsIncompleteTurn()
    {
        var thread = new InMemoryConversationThread("thread-1");

        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "failed"));
        thread.AppendUpdate(new ChatResponseUpdate(
            ChatRole.Assistant,
            "partial"));

        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "retry"));
        thread.AppendUpdate(new ChatResponseUpdate(
            ChatRole.Assistant,
            "complete"));
        thread.CompleteTurn();

        var history = thread.GetMessageHistory();
        Assert.Equal(2, history.Count);
        Assert.Equal("retry", history[0].Text);
        Assert.Equal("complete", history[1].Text);
    }

    [Fact]
    public void GetMessageHistory_ReconstructsMixedMessageRoles()
    {
        var thread = new InMemoryConversationThread("thread-1");

        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "Run it")
        {
            MessageId = "user-1",
        });
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-call",
            Contents = [new FunctionCallContent("call-1", "Run", null)],
        });
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Tool,
            MessageId = "tool-1",
            Contents = [new FunctionResultContent("call-1", "done")],
        });
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-final",
            Contents = [new TextContent("Finished")],
        });
        thread.CompleteTurn();

        var history = thread.GetMessageHistory();
        Assert.Equal(
            [ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant],
            history.Select(message => message.Role));
    }

    [Fact]
    public void ConversationId_TracksPendingResponseAndResetsWithAttempt()
    {
        var thread = new InMemoryConversationThread("thread-1");

        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "first"));
        thread.AppendUpdate(new ChatResponseUpdate
        {
            ConversationId = "failed-conversation",
        });

        Assert.True(thread.IsStateful);
        Assert.Equal("failed-conversation", thread.ConversationId);

        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "retry"));

        Assert.False(thread.IsStateful);
        Assert.Null(thread.ConversationId);

        thread.AppendUpdate(new ChatResponseUpdate
        {
            ConversationId = "committed-conversation",
        });
        thread.CompleteTurn();

        Assert.True(thread.IsStateful);
        Assert.Equal("committed-conversation", thread.ConversationId);
    }

    [Fact]
    public void Clear_RemovesCommittedAndPendingState()
    {
        var thread = new InMemoryConversationThread("thread-1");

        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "committed"));
        thread.AppendUpdate(new ChatResponseUpdate
        {
            ConversationId = "conversation-1",
            Contents = [new TextContent("response")],
        });
        thread.CompleteTurn();
        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "pending"));

        thread.Clear();

        Assert.Empty(thread.GetUpdates());
        Assert.Empty(thread.GetMessageHistory());
        Assert.Equal(0, thread.PendingUpdateCount);
        Assert.False(thread.IsStateful);
        Assert.Null(thread.ConversationId);
        Assert.Equal(1, thread.ClearCallCount);
    }

    [Fact]
    public void ThreadId_IsStable()
    {
        var thread = new InMemoryConversationThread("thread-42");

        Assert.Equal("thread-42", thread.ThreadId);
    }
}
