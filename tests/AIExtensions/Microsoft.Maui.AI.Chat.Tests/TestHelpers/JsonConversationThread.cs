// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.TestHelpers;

/// <summary>
/// A thread that saves each update as JSON with <see cref="AIJsonUtilities.DefaultOptions"/> and reads it back, like
/// a thread that persists to a file or database without special handling: additional property values come back as
/// <see cref="JsonElement"/>s.
/// </summary>
internal sealed class JsonConversationThread : IConversationThread
{
    private readonly List<string> _committed = [];
    private List<string>? _pending;

    public string ThreadId => "json-thread";

    public bool IsStateful => false;

    public string? ConversationId => null;

    public void AppendUserMessage(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _pending =
        [
            Write(new ChatResponseUpdate
            {
                Role = message.Role,
                AuthorName = message.AuthorName,
                CreatedAt = message.CreatedAt,
                MessageId = message.MessageId,
                Contents = [.. message.Contents],
                AdditionalProperties = message.AdditionalProperties,
            }),
        ];
    }

    public void AppendUpdate(ChatResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        (_pending ?? throw new InvalidOperationException("AppendUserMessage must start a turn.")).Add(Write(update));
    }

    public void CompleteTurn()
    {
        if (_pending is null)
            return;

        _committed.AddRange(_pending);
        _pending = null;
    }

    public IReadOnlyList<ChatResponseUpdate> GetUpdates() => [.. _committed.Select(Read)];

    public void Clear()
    {
        _committed.Clear();
        _pending = null;
    }

    private static string Write(ChatResponseUpdate update) => JsonSerializer.Serialize(update, AIJsonUtilities.DefaultOptions);

    private static ChatResponseUpdate Read(string json)
        => JsonSerializer.Deserialize<ChatResponseUpdate>(json, AIJsonUtilities.DefaultOptions)
            ?? throw new InvalidOperationException("A saved update is empty.");
}
