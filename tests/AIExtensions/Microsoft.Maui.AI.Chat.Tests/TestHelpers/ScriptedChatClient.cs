// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.TestHelpers;

/// <summary>A model that answers each request with the next scripted response and records the requests.</summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>> _responses = new();

    internal List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    internal void Enqueue(Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> response)
        => _responses.Enqueue(response);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.Select(message => message.Clone()).ToList());
        return _responses.Dequeue()(cancellationToken);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
