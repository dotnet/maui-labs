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

    /// <summary>Gets the number of request rounds the turn has started.</summary>
    internal int Rounds { get; private set; }

    /// <summary>
    /// Gets the calls of the last completed round that the round did not answer, such as a batch that
    /// <see cref="FunctionInvokingChatClient"/> handed back uninvoked because it included a UI action.
    /// </summary>
    internal IReadOnlyList<FunctionCallContent> OpenCalls { get; private set; } = [];

    /// <summary>Counts a request round that starts.</summary>
    internal void BeginRound() => Rounds++;

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

    /// <summary>
    /// Moves the current round's response updates into <see cref="Messages"/> and records the round's
    /// <see cref="OpenCalls"/>.
    /// </summary>
    internal void CompleteRound()
    {
        if (_roundUpdates.Count == 0)
        {
            OpenCalls = [];
            return;
        }

        var messages = _roundUpdates.ToChatResponse().Messages;
        OpenCalls = UnansweredCalls.FindOpen(messages);
        Messages.AddRange(messages);
        _roundUpdates.Clear();
    }
}
