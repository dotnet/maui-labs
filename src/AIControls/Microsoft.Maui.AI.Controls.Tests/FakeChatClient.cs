// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Controls.Tests;

/// <summary>
/// Configurable fake <see cref="IChatClient"/> for unit testing.
/// Produces a controlled sequence of <see cref="ChatResponseUpdate"/> items.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly List<ChatResponseUpdate> _updates = [];
    private Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>? _streamHandler;

    public void Dispose() { }

    public ChatClientMetadata Metadata => new("fake");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, "ok")));

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_streamHandler is not null)
            return _streamHandler(chatMessages, options, cancellationToken);

        return YieldUpdates(cancellationToken);
    }

    private async IAsyncEnumerable<ChatResponseUpdate> YieldUpdates(
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var update in _updates)
        {
            if (ct.IsCancellationRequested) yield break;
            yield return update;
            await Task.Yield();
        }
    }

    /// <summary>Queue updates to be yielded during streaming.</summary>
    public FakeChatClient WithUpdates(params ChatResponseUpdate[] updates)
    {
        _updates.AddRange(updates);
        return this;
    }

    /// <summary>Queue text updates that stream word-by-word.</summary>
    public FakeChatClient WithTextResponse(string text)
    {
        var words = text.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            var word = i > 0 ? " " + words[i] : words[i];
            _updates.Add(new ChatResponseUpdate
            {
                Role = i == 0 ? ChatRole.Assistant : null,
                Contents = [new TextContent(word)]
            });
        }
        return this;
    }

    /// <summary>Queue a reasoning + text response.</summary>
    public FakeChatClient WithReasoningAndText(string reasoning, string text)
    {
        _updates.Add(new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextReasoningContent(reasoning)]
        });
        _updates.Add(new ChatResponseUpdate
        {
            Contents = [new TextContent(text)]
        });
        return this;
    }

    /// <summary>Use a custom handler for full control over streaming.</summary>
    public FakeChatClient WithStreamHandler(
        Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> handler)
    {
        _streamHandler = handler;
        return this;
    }

    /// <summary>Create an update with a FunctionCallContent.</summary>
    public static ChatResponseUpdate FunctionCallUpdate(string callId, string name, IDictionary<string, object?>? args = null)
        => new()
        {
            Contents = [new FunctionCallContent(callId, name, args)]
        };

    /// <summary>Create an update with a FunctionResultContent.</summary>
    public static ChatResponseUpdate FunctionResultUpdate(string callId, object? result)
        => new()
        {
            Contents = [new FunctionResultContent(callId, result)]
        };

    /// <summary>Create an update with DataContent (for state snapshots/deltas).</summary>
    public static ChatResponseUpdate DataContentUpdate(byte[] data, string mediaType)
        => new()
        {
            Contents = [new DataContent(data, mediaType)]
        };
}
