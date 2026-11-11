// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Buffers one turn's messages and raw updates until <see cref="UIAgent"/> commits them to its
/// history and conversation thread.
/// </summary>
internal sealed class PendingTurn
{
    private readonly List<ChatResponseUpdate> _roundUpdates = new();

    internal PendingTurn(int historyVersion, ConversationTurn? displayTurn)
    {
        HistoryVersion = historyVersion;
        DisplayTurn = displayTurn;
    }

    /// <summary>Gets the history version the turn started from; a clear or restore makes it stale.</summary>
    internal int HistoryVersion { get; }

    /// <summary>Gets the turn that shows this pending turn, if any; it receives the response metadata.</summary>
    internal ConversationTurn? DisplayTurn { get; }

    /// <summary>Gets the message that started the turn.</summary>
    internal ChatMessage? InitialMessage { get; private set; }

    /// <summary>Gets the turn's request and response messages in history order.</summary>
    internal List<ChatMessage> Messages { get; } = new();

    /// <summary>Gets the raw updates that follow <see cref="InitialMessage"/> in the thread.</summary>
    internal List<ChatResponseUpdate> ThreadUpdates { get; } = new();

    /// <summary>Gets the latest provider conversation identifier seen during the turn.</summary>
    internal string? ConversationId { get; private set; }

    /// <summary>Gets the blocks that show the turn's function calls, shared by all of its rounds.</summary>
    internal FunctionCallRegistry Calls { get; } = new();

    internal void AddRequest(ChatMessage message)
    {
        if (InitialMessage is null)
            InitialMessage = message;
        else
            ThreadUpdates.Add(UIAgent.CreateMessageUpdate(message, isContinuation: true));

        Messages.Add(message);
    }

    internal void AddResponse(ChatResponseUpdate rawUpdate, ChatResponseUpdate historyUpdate)
    {
        ThreadUpdates.Add(rawUpdate);
        _roundUpdates.Add(historyUpdate);
        if (!string.IsNullOrEmpty(rawUpdate.ConversationId))
            ConversationId = rawUpdate.ConversationId;
        DisplayTurn?.ObserveResponse(rawUpdate);
    }

    /// <summary>Moves the current round's response updates into <see cref="Messages"/>.</summary>
    internal void CompleteRound()
    {
        if (_roundUpdates.Count == 0)
            return;

        Messages.AddRange(_roundUpdates.ToChatResponse().Messages);
        _roundUpdates.Clear();
    }
}
