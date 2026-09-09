// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Maui.AI.Chat.Tests.Pipeline;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

public class UIAgentThreadTests
{
    private sealed class CitationRaw
    {
        internal required string Source { get; init; }
        internal required string Quote { get; init; }
    }

    [Fact]
    public async Task SendMessageAsync_Success_CommitsRawTurnOnce()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var client = CreateClient(ResponseEmitters.EmitTextResponse("Hi"));
        var agent = CreateAgent(client, thread);

        await EnumerateAsync(agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Hello")));

        var updates = thread.GetUpdates();
        Assert.Equal(2, updates.Count);
        Assert.Equal(ChatRole.User, updates[0].Role);
        Assert.Equal(ChatRole.Assistant, updates[1].Role);
        Assert.Equal(1, thread.AppendUserMessageCount);
        Assert.Equal(1, thread.AppendUpdateCount);
        Assert.Equal(1, thread.CompleteTurnCallCount);
        Assert.Equal(1, thread.CommittedTurnCount);
    }

    [Fact]
    public async Task SendMessageAsync_Failure_LeavesPartialTurnUncommitted()
    {
        var thread = new InMemoryConversationThread("thread-1");
        IReadOnlyList<ChatMessage>? secondRequest = null;
        var callCount = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return ResponseEmitters.EmitErrorAfterTokens(
                    ["partial"],
                    new InvalidOperationException("boom"),
                    cancellationToken);
            }

            secondRequest = messages.ToArray();
            return ResponseEmitters.EmitTextResponse(
                "recovered",
                cancellationToken);
        });
        var agent = CreateAgent(client, thread);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EnumerateAsync(agent.SendMessageAsync(
                new ChatMessage(ChatRole.User, "Hello"))));

        Assert.Empty(thread.GetUpdates());
        Assert.Empty(thread.GetMessageHistory());
        Assert.Equal(0, thread.PendingUpdateCount);
        Assert.Equal(0, thread.CompleteTurnCallCount);
        Assert.Equal(1, thread.AbortTurnCallCount);

        await EnumerateAsync(agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Fresh")));

        var freshMessage = Assert.Single(secondRequest!);
        Assert.Equal("Fresh", freshMessage.Text);
    }

    [Fact]
    public async Task SendMessageAsync_StatefulFailure_PreservesCommittedConversation()
    {
        var thread = new InMemoryConversationThread("thread-1");
        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "committed"));
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-1",
            ConversationId = "conversation-1",
            Contents = [new TextContent("committed response")],
        });
        thread.CompleteTurn();

        ChatOptions? secondOptions = null;
        IReadOnlyList<ChatMessage>? secondRequest = null;
        var callCount = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return ResponseEmitters.EmitErrorAfterTokens(
                    ["partial"],
                    new InvalidOperationException("boom"),
                    cancellationToken);
            }

            secondOptions = options;
            secondRequest = messages.ToArray();
            return ResponseEmitters.EmitTextResponse(
                "recovered",
                cancellationToken);
        });
        var agent = CreateAgent(client, thread);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EnumerateAsync(agent.SendMessageAsync(
                new ChatMessage(ChatRole.User, "failed"))));
        await EnumerateAsync(agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "fresh")));

        Assert.Equal("conversation-1", secondOptions?.ConversationId);
        Assert.Equal("fresh", Assert.Single(secondRequest!).Text);
        Assert.Equal(1, thread.AbortTurnCallCount);
        Assert.Equal(2, thread.CommittedTurnCount);
    }

    [Fact]
    public async Task SendMessageAsync_StatelessThread_UsesCommittedMessageHistory()
    {
        var thread = new InMemoryConversationThread("thread-1");
        CommitTextTurn(thread, "first", "first response");

        IReadOnlyList<ChatMessage>? capturedMessages = null;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            capturedMessages = messages.ToArray();
            return ResponseEmitters.EmitTextResponse(
                "second response",
                cancellationToken);
        });
        var agent = CreateAgent(client, thread);

        await EnumerateAsync(agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "second")));

        Assert.NotNull(capturedMessages);
        Assert.Equal(3, capturedMessages.Count);
        Assert.Equal(
            ["first", "first response", "second"],
            capturedMessages.Select(message => message.Text));
    }

    [Fact]
    public async Task SendMessageAsync_StatefulThread_ClonesOptionsAndSendsOnlyIncrement()
    {
        var thread = new InMemoryConversationThread("thread-1");
        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "first"));
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-1",
            ConversationId = "conversation-1",
            Contents = [new TextContent("first response")],
        });
        thread.CompleteTurn();

        var configuredOptions = new ChatOptions { Temperature = 0.25f };
        ChatOptions? capturedOptions = null;
        IReadOnlyList<ChatMessage>? capturedMessages = null;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            capturedMessages = messages.ToArray();
            capturedOptions = options;
            return ResponseEmitters.EmitTextResponse("second response", cancellationToken);
        });
        var agent = new UIAgent(client, options =>
        {
            options.Thread = thread;
            options.ChatOptions = configuredOptions;
        });

        await EnumerateAsync(agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "second")));

        Assert.NotNull(capturedOptions);
        Assert.NotSame(configuredOptions, capturedOptions);
        Assert.Equal("conversation-1", capturedOptions.ConversationId);
        Assert.Equal(0.25f, capturedOptions.Temperature);
        Assert.Null(configuredOptions.ConversationId);
        Assert.NotNull(capturedMessages);
        Assert.Single(capturedMessages);
        Assert.Equal("second", capturedMessages[0].Text);
    }

    [Fact]
    public async Task AgentContext_StatefulToolLoop_PropagatesLatestConversationIdAndCommitsOnce()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var configuredOptions = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(
                    (Func<string>)(() => "tool result"),
                    "GetValue"),
            ],
        };
        ChatOptions? continuationOptions = null;
        IReadOnlyList<ChatMessage>? continuationMessages = null;
        var callCount = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            callCount++;
            if (callCount == 1)
                return EmitToolCallWithConversationId("conversation-1", cancellationToken);

            continuationOptions = options;
            continuationMessages = messages.ToArray();
            return EmitTextWithConversationId(
                "done",
                "conversation-2",
                cancellationToken);
        });
        var context = new AgentContext(new UIAgent(client, options =>
        {
            options.Thread = thread;
            options.ChatOptions = configuredOptions;
        }));

        await context.SendMessageAsync("Run the tool");

        Assert.Equal(2, callCount);
        Assert.NotNull(continuationOptions);
        Assert.NotSame(configuredOptions, continuationOptions);
        Assert.Equal("conversation-1", continuationOptions.ConversationId);
        Assert.Null(configuredOptions.ConversationId);
        Assert.NotNull(continuationMessages);
        var continuationMessage = Assert.Single(continuationMessages);
        Assert.Equal(ChatRole.Tool, continuationMessage.Role);
        Assert.Equal("conversation-2", thread.ConversationId);
        Assert.Equal(1, thread.CompleteTurnCallCount);
        Assert.Equal(1, thread.CommittedTurnCount);

        var history = thread.GetMessageHistory();
        Assert.Equal(
            [ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant],
            history.Select(message => message.Role));
    }

    [Fact]
    public async Task RestoreAsync_ReplaysMultipleTurnsAndMixedBlocks()
    {
        var thread = new InMemoryConversationThread("thread-1");

        thread.AppendUserMessage(new ChatMessage(
            ChatRole.User,
            [
                new TextContent("first"),
                new DataContent(new byte[] { 1, 2, 3 }, "image/png"),
            ])
        {
            MessageId = "turn-1",
        });
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-1",
            Contents = [new TextContent("first response")],
        });
        thread.CompleteTurn();

        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "second")
        {
            MessageId = "turn-2",
        });
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-call",
            Contents = [new FunctionCallContent("call-1", "GetValue", null)],
        });
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Tool,
            MessageId = "tool-result",
            Contents = [new FunctionResultContent("call-1", "value")],
        });
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-2",
            Contents = [new TextContent("second response")],
        });
        thread.CompleteTurn();

        var agent = CreateAgent(
            CreateClient(ResponseEmitters.EmitEmptyResponse()),
            thread);

        var blocks = await agent.RestoreAsync();

        Assert.Equal(2, blocks.Count(block => block.StartsRestoredTurn));
        Assert.Equal(3, blocks.Count(block => block.IsRestoredRequest));
        Assert.Contains(blocks, block => block is MediaContentBlock);
        var functionBlock = Assert.Single(
            blocks.OfType<FunctionInvocationContentBlock>());
        Assert.True(functionBlock.HasResult);
        Assert.Equal("value", functionBlock.Result?.Result);
        Assert.Equal(
            ["turn-1", "turn-2"],
            blocks
                .Where(block => block.StartsRestoredTurn)
                .Select(block => block.RestoredTurnId));
    }

    [Fact]
    public async Task RestoreAsync_WithSameCustomHandler_ReprojectsRawUpdate()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var sourceAgent = CreateCitationAgent(
            CreateClient(EmitCitation()),
            thread);
        await EnumerateAsync(sourceAgent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Find a source")));

        var restoredAgent = CreateCitationAgent(
            CreateClient(ResponseEmitters.EmitEmptyResponse()),
            thread);

        var blocks = await restoredAgent.RestoreAsync();

        var citation = Assert.Single(blocks.OfType<CitationBlock>());
        Assert.Equal("Journal", citation.Source);
        Assert.Equal("Evidence", citation.Quote);
    }

    [Fact]
    public async Task RestoreAsync_WhenThreadDropsRawRepresentation_CustomProjectionIsUnavailable()
    {
        var thread = new InMemoryConversationThread(
            "thread-1",
            preserveRawRepresentation: false);
        var sourceAgent = CreateCitationAgent(
            CreateClient(EmitCitation()),
            thread);
        await EnumerateAsync(sourceAgent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Find a source")));

        var restoredAgent = CreateCitationAgent(
            CreateClient(ResponseEmitters.EmitEmptyResponse()),
            thread);

        var blocks = await restoredAgent.RestoreAsync();

        Assert.Empty(blocks.OfType<CitationBlock>());
    }

    [Fact]
    public async Task RestoreAsync_WithoutThread_ReturnsEmpty()
    {
        var agent = new UIAgent(
            CreateClient(ResponseEmitters.EmitEmptyResponse()));

        var blocks = await agent.RestoreAsync();

        Assert.Empty(blocks);
    }

    private static UIAgent CreateAgent(
        DelegatingStreamingChatClient client,
        InMemoryConversationThread thread)
        => new(client, options => options.Thread = thread);

    private static UIAgent CreateCitationAgent(
        DelegatingStreamingChatClient client,
        InMemoryConversationThread thread)
        => new(client, options =>
        {
            options.Thread = thread;
            options.AddBlockHandler(
                new DelegateBlockHandler<CitationBlock>((context, state) =>
                {
                    if (context.Update.RawRepresentation is not CitationRaw raw)
                        return BlockMappingResult<CitationBlock>.Pass();

                    context.MarkUpdateHandled();
                    state.Source = raw.Source;
                    state.Quote = raw.Quote;
                    state.Id = context.Update.MessageId
                        ?? Guid.NewGuid().ToString("N");
                    return BlockMappingResult<CitationBlock>.Emit(state, state);
                }));
        });

    private static DelegatingStreamingChatClient CreateClient(
        IAsyncEnumerable<ChatResponseUpdate> response)
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => response);
        return client;
    }

    private static void CommitTextTurn(
        InMemoryConversationThread thread,
        string request,
        string response)
    {
        thread.AppendUserMessage(new ChatMessage(ChatRole.User, request)
        {
            MessageId = Guid.NewGuid().ToString("N"),
        });
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = Guid.NewGuid().ToString("N"),
            Contents = [new TextContent(response)],
        });
        thread.CompleteTurn();
    }

    private static async Task EnumerateAsync(
        IAsyncEnumerable<ContentBlock> blocks)
    {
        await foreach (var _ in blocks)
        {
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitCitation(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "citation-1",
            RawRepresentation = new CitationRaw
            {
                Source = "Journal",
                Quote = "Evidence",
            },
        };
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate>
        EmitToolCallWithConversationId(
            string conversationId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-call",
            ConversationId = conversationId,
            Contents = [new FunctionCallContent("call-1", "GetValue", null)],
            FinishReason = ChatFinishReason.ToolCalls,
        };
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate>
        EmitTextWithConversationId(
            string text,
            string conversationId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-final",
            ConversationId = conversationId,
            Contents = [new TextContent(text)],
        };
        await Task.CompletedTask;
    }
}
