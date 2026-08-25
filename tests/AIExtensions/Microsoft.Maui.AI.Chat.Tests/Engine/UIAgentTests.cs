// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

public class UIAgentTests
{
    [Fact]
    public async Task SendMessageAsync_NullMessage_ThrowsWithoutPoisoningHistory()
    {
        IReadOnlyList<ChatMessage>? sentMessages = null;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            sentMessages = messages.ToArray();
            return ResponseEmitters.EmitTextResponse("ok", cancellationToken);
        });
        var agent = new UIAgent(client);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => EnumerateAsync(agent.SendMessageAsync(null!)));

        await EnumerateAsync(agent.SendMessageAsync(new ChatMessage(ChatRole.User, "valid")));

        Assert.NotNull(sentMessages);
        Assert.Single(sentMessages);
        Assert.Equal("valid", sentMessages[0].Text);

        static async Task EnumerateAsync(IAsyncEnumerable<ContentBlock> blocks)
        {
            await foreach (var _ in blocks)
            {
            }
        }
    }

    [Fact]
    public async Task SendMessageAsync_TextResponse_YieldsUserThenAssistantBlocks()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
            ResponseEmitters.EmitTextResponse("Hi there!"));
        var agent = new UIAgent(client);

        var blocks = new List<ContentBlock>();
        await foreach (var block in agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Hello")))
        {
            blocks.Add(block);
        }

        Assert.True(blocks.Count >= 2);

        var userBlocks = blocks.Where(b => b.Role == ChatRole.User).ToList();
        Assert.NotEmpty(userBlocks);
        var userBlock = Assert.IsType<TextContentBlock>(userBlocks[0]);
        Assert.Equal("Hello", userBlock.RawText);

        var assistantBlocks = blocks.Where(b => b.Role == ChatRole.Assistant).ToList();
        Assert.NotEmpty(assistantBlocks);
        var assistantBlock = Assert.IsType<TextContentBlock>(assistantBlocks[0]);
        Assert.Equal("Hi there!", assistantBlock.RawText);
    }

    [Fact]
    public async Task SendMessageAsync_MultiTokenStreaming_SingleBlockWithAccumulatedText()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
            ResponseEmitters.EmitMultiTokenTextResponse(ct, "Hello", " ", "world", "!"));
        var agent = new UIAgent(client);

        var blocks = new List<ContentBlock>();
        await foreach (var block in agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Hi")))
        {
            blocks.Add(block);
        }

        var assistantBlocks = blocks.Where(b => b.Role == ChatRole.Assistant).ToList();
        Assert.Single(assistantBlocks);
        var rich = Assert.IsType<TextContentBlock>(assistantBlocks[0]);
        Assert.Equal("Hello world!", rich.RawText);
    }

    [Fact]
    public async Task SendMessageAsync_MultiTokenStreaming_OnChangedFiresPerToken()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
            ResponseEmitters.EmitMultiTokenTextResponse(ct, "A", "B", "C"));
        var agent = new UIAgent(client);

        var changeCount = 0;
        ContentBlock? firstBlock = null;

        await foreach (var block in agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Hi")))
        {
            if (block.Role == ChatRole.Assistant && firstBlock is null)
            {
                firstBlock = block;
                block.OnChanged(() => changeCount++);
            }
        }

        // Two appended tokens after the initial emission, plus finalization.
        Assert.Equal(3, changeCount);
    }

    [Fact]
    public async Task SendMessageAsync_AllBlocksInactiveAfterIteration()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
            ResponseEmitters.EmitTextResponse("Done"));
        var agent = new UIAgent(client);

        var blocks = new List<ContentBlock>();
        await foreach (var block in agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Go")))
        {
            blocks.Add(block);
        }

        Assert.All(blocks, b => Assert.Equal(BlockLifecycleState.Inactive, b.LifecycleState));
    }

    [Fact]
    public async Task SendMessageAsync_EmptyResponse_YieldsUserBlockOnly()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) => ResponseEmitters.EmitEmptyResponse());
        var agent = new UIAgent(client);

        var blocks = new List<ContentBlock>();
        await foreach (var block in agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Hello")))
        {
            blocks.Add(block);
        }

        Assert.All(blocks, b => Assert.Equal(ChatRole.User, b.Role));
    }

    [Fact]
    public async Task SendMessageAsync_PassesChatOptions_ToIChatClient()
    {
        ChatOptions? capturedOptions = null;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
        {
            capturedOptions = opts;
            return ResponseEmitters.EmitTextResponse("ok");
        });
        var expectedOptions = new ChatOptions { Temperature = 0.5f };
        var agent = new UIAgent(client, expectedOptions);

        await foreach (var _ in agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "test"))) { }

        Assert.Same(expectedOptions, capturedOptions);
    }

    [Fact]
    public async Task SendMessageAsync_FailedAttemptsDoNotGrowHistory()
    {
        var requests = new List<IReadOnlyList<ChatMessage>>();
        var callCount = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            requests.Add(messages.ToArray());
            callCount++;
            return callCount == 1
                ? ResponseEmitters.EmitErrorAfterTokens(
                    ["partial"],
                    new InvalidOperationException("failed"),
                    cancellationToken)
                : ResponseEmitters.EmitTextResponse(
                    "recovered",
                    cancellationToken);
        });
        var agent = new UIAgent(client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => EnumerateAsync(agent.SendMessageAsync(
                new ChatMessage(ChatRole.User, "failed request"))));
        await EnumerateAsync(agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "fresh request")));

        Assert.Equal(2, requests.Count);
        var freshRequest = Assert.Single(requests[1]);
        Assert.Equal("fresh request", freshRequest.Text);
    }

    [Fact]
    public async Task SendMessageAsync_SuccessfulTurnsGrowHistory()
    {
        var requests = new List<IReadOnlyList<ChatMessage>>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            requests.Add(messages.ToArray());
            return ResponseEmitters.EmitTextResponse(
                $"response {requests.Count}",
                cancellationToken);
        });
        var agent = new UIAgent(client);

        await EnumerateAsync(agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "first")));
        await EnumerateAsync(agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "second")));

        Assert.Collection(
            requests[1],
            message => Assert.Equal("first", message.Text),
            message => Assert.Equal("response 1", message.Text),
            message => Assert.Equal("second", message.Text));
    }

    [Fact]
    public async Task SendMessageAsync_CanceledAttemptsDoNotGrowHistory()
    {
        var requests = new List<IReadOnlyList<ChatMessage>>();
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            requests.Add(messages.ToArray());
            callCount++;
            return callCount == 1
                ? CancelableResponse(firstStarted, cancellationToken)
                : ResponseEmitters.EmitTextResponse("ok", cancellationToken);
        });
        var agent = new UIAgent(client);
        using var cancellation = new CancellationTokenSource();

        var first = EnumerateAsync(agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "canceled"),
            cancellation.Token));
        await firstStarted.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        await EnumerateAsync(agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "fresh")));

        var request = Assert.Single(requests[1]);
        Assert.Equal("fresh", request.Text);

        static async IAsyncEnumerable<ChatResponseUpdate> CancelableResponse(
            TaskCompletionSource started,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }
    }

    private static async Task EnumerateAsync(IAsyncEnumerable<ContentBlock> blocks)
    {
        await foreach (var _ in blocks)
        {
        }
    }
}
