// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.TestHelpers;

internal sealed class InMemoryConversationThread : IConversationThread
{
    private readonly List<ChatResponseUpdate> _committedUpdates = new();
    private readonly bool _preserveRawRepresentation;
    private readonly bool _preserveAdditionalProperties;
    private List<ChatResponseUpdate>? _pendingUpdates;
    private string? _committedConversationId;
    private string? _pendingConversationId;

    internal InMemoryConversationThread(
        string threadId,
        bool preserveRawRepresentation = true,
        bool preserveAdditionalProperties = true)
    {
        ThreadId = threadId;
        _preserveRawRepresentation = preserveRawRepresentation;
        _preserveAdditionalProperties = preserveAdditionalProperties;
    }

    public string ThreadId { get; }

    public bool IsStateful => ConversationId is not null;

    public string? ConversationId => _pendingUpdates is null
        ? _committedConversationId
        : _pendingConversationId;

    internal int AppendUserMessageCount { get; private set; }

    internal int AppendUpdateCount { get; private set; }

    internal int CompleteTurnCallCount { get; private set; }

    internal int CommittedTurnCount { get; private set; }

    internal int ClearCallCount { get; private set; }

    internal int PendingUpdateCount => _pendingUpdates?.Count ?? 0;

    public void AppendUserMessage(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        AppendUserMessageCount++;
        _pendingConversationId = _committedConversationId;
        _pendingUpdates =
        [
            new ChatResponseUpdate
            {
                Role = message.Role,
                AuthorName = message.AuthorName,
                CreatedAt = message.CreatedAt,
                MessageId = message.MessageId ?? Guid.NewGuid().ToString("N"),
                Contents = [.. message.Contents],
                RawRepresentation = _preserveRawRepresentation
                    ? message.RawRepresentation
                    : null,
                AdditionalProperties = !_preserveAdditionalProperties
                    || message.AdditionalProperties is null
                    ? null
                    : new AdditionalPropertiesDictionary(message.AdditionalProperties),
            },
        ];
    }

    public void AppendUpdate(ChatResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (_pendingUpdates is null)
        {
            throw new InvalidOperationException(
                "AppendUserMessage must start a pending turn before updates are appended.");
        }

        AppendUpdateCount++;
        var storedUpdate = update.Clone();
        if (!_preserveRawRepresentation)
            storedUpdate.RawRepresentation = null;
        if (!_preserveAdditionalProperties)
            storedUpdate.AdditionalProperties = null;

        _pendingUpdates.Add(storedUpdate);
        if (update.ConversationId is not null)
            _pendingConversationId = update.ConversationId;
    }

    public void CompleteTurn()
    {
        CompleteTurnCallCount++;

        if (_pendingUpdates is null)
            return;

        _committedUpdates.AddRange(_pendingUpdates);
        _committedConversationId = _pendingConversationId;
        _pendingUpdates = null;
        _pendingConversationId = null;
        CommittedTurnCount++;
    }

    public IReadOnlyList<ChatResponseUpdate> GetUpdates()
        => _committedUpdates.Select(update => update.Clone()).ToArray();

    public IReadOnlyList<ChatMessage> GetMessageHistory()
    {
        var messages = new List<ChatMessage>();
        messages.AddMessages(_committedUpdates);
        return messages;
    }

    public void Clear()
    {
        ClearCallCount++;
        _committedUpdates.Clear();
        _pendingUpdates = null;
        _committedConversationId = null;
        _pendingConversationId = null;
    }
}
