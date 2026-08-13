// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// The stateful conversation object the UI binds to. Groups <see cref="ContentBlock"/>s into
/// <see cref="ConversationTurn"/>s, tracks <see cref="Status"/>, and raises callbacks as turns and
/// blocks arrive.
/// </summary>
/// <remarks>
/// Drives the tool/approval loop: after streaming from the <see cref="UIAgent"/> it invokes any pending
/// backend tools and awaits <see cref="IInteractiveBlock"/>s (e.g. approvals), then feeds the results
/// back for another round.
/// <para>
/// This type is single-thread-affine and is not thread-safe. Callers must serialize access and
/// enter the owning application thread before calling it. Status checks are misuse guards, not
/// synchronization.
/// </para>
/// </remarks>
public class AgentContext(UIAgent agent) : IDisposable
{
    private readonly List<ConversationTurn> _turns = new();
    private readonly List<Action<ConversationTurn>> _turnAddedCallbacks = new();
    private readonly List<Action<ConversationStatus>> _statusChangedCallbacks = new();
    private readonly List<Action<ConversationTurn, ContentBlock>> _blockAddedCallbacks = new();
    private readonly List<Action<ConversationTurn>> _responseBlocksClearedCallbacks = new();
    private CancellationTokenSource? _streamingCts;
    private ChatMessage? _retryMessage;
    private UIAgent.HistoryCheckpoint _retryHistoryCheckpoint;
    private bool _restoreCompleted;
    private bool _disposed;

    public IReadOnlyList<ConversationTurn> Turns => _turns;

    public ConversationStatus Status { get; private set; }

    public Exception? Error { get; private set; }

    /// <summary>
    /// Clears all conversation turns, local and persistent history, and error state.
    /// The underlying agent and tools remain configured.
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelAndDisposeStreaming();
        agent.RejectPendingPredictiveState();
        _turns.Clear();
        agent.ClearHistory();
        _retryMessage = null;
        _retryHistoryCheckpoint = default;
        _restoreCompleted = false;
        Error = null;
        Status = ConversationStatus.Idle;
        NotifyStatusChanged();
    }

    public Task SendMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        return SendMessageAsync(new ChatMessage(ChatRole.User, text), cancellationToken);
    }

    public async Task SendMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Status == ConversationStatus.Streaming)
            throw new InvalidOperationException("A message is already being processed.");

        var requestMessage = EnsureMessageId(message);
        var turn = new ConversationTurn { Id = requestMessage.MessageId! };
        _turns.Add(turn);
        NotifyTurnAdded(turn);

        var historyCheckpoint = agent.CaptureHistoryCheckpoint();
        await RunAttemptAsync(
            requestMessage,
            turn,
            historyCheckpoint,
            includeInitialRequestBlocks: true,
            cancellationToken);
    }

    /// <summary>
    /// Restores committed conversation turns from the configured <see cref="IConversationThread"/>.
    /// </summary>
    /// <remarks>
    /// Restore requires an idle, empty context and may be called only once before <see cref="Clear"/>.
    /// It reconstructs display/history blocks only: restored approvals, tools, and other interactive
    /// blocks are not resumed or invoked.
    /// </remarks>
    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Status != ConversationStatus.Idle)
            throw new InvalidOperationException("RestoreAsync requires an idle context.");

        if (_restoreCompleted || _turns.Count != 0)
        {
            throw new InvalidOperationException(
                "RestoreAsync requires an empty context and may not be called twice. Call Clear first.");
        }

        var blocks = await agent.RestoreAsync(cancellationToken);
        ConversationTurn? currentTurn = null;

        foreach (var block in blocks)
        {
            if (block.StartsRestoredTurn || currentTurn is null)
            {
                currentTurn = new ConversationTurn
                {
                    Id = block.RestoredTurnId
                        ?? block.Id
                        ?? Guid.NewGuid().ToString("N"),
                };
                _turns.Add(currentTurn);
            }

            if (block.IsRestoredRequest)
                currentTurn.AddRequestBlock(block);
            else
                currentTurn.AddResponseBlock(block);
        }

        _restoreCompleted = true;
    }

    /// <summary>
    /// Retries the last failed message in its existing turn.
    /// </summary>
    public async Task RetryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Status != ConversationStatus.Error || _retryMessage is null)
        {
            throw new InvalidOperationException(
                $"RetryAsync requires Status == Error, but Status is {Status}.");
        }

        var turn = _turns[^1];
        turn.ClearResponseBlocks();
        NotifyResponseBlocksCleared(turn);
        agent.RestoreHistory(_retryHistoryCheckpoint);

        await RunAttemptAsync(
            _retryMessage,
            turn,
            _retryHistoryCheckpoint,
            includeInitialRequestBlocks: false,
            cancellationToken);
    }

    private async Task RunAttemptAsync(
        ChatMessage message,
        ConversationTurn turn,
        UIAgent.HistoryCheckpoint historyCheckpoint,
        bool includeInitialRequestBlocks,
        CancellationToken callerToken)
    {
        var (streamingCts, streamingToken) = ReplaceStreamingCancellationSource();
        CancellationTokenSource? linkedCts = null;

        try
        {
            if (callerToken.CanBeCanceled)
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    streamingToken,
                    callerToken);
                streamingToken = linkedCts.Token;
            }

            var outcome = await StreamIntoTurnAsync(
                message,
                turn,
                historyCheckpoint,
                includeInitialRequestBlocks,
                streamingToken);

            if (outcome != AttemptOutcome.Succeeded && callerToken.IsCancellationRequested)
            {
                CleanupCanceledTurn(turn, historyCheckpoint, message);
                callerToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            linkedCts?.Dispose();
            if (ReferenceEquals(_streamingCts, streamingCts))
                _streamingCts = null;
            streamingCts.Dispose();
        }
    }

    private async Task<AttemptOutcome> StreamIntoTurnAsync(
        ChatMessage message,
        ConversationTurn turn,
        UIAgent.HistoryCheckpoint historyCheckpoint,
        bool includeInitialRequestBlocks,
        CancellationToken cancellationToken)
    {
        Status = ConversationStatus.Streaming;
        Error = null;
        NotifyStatusChanged();

        try
        {
            IReadOnlyList<ChatMessage> currentMessages = [message];
            var startsThreadTurn = true;
            var isInitialRequest = true;

            while (currentMessages.Count > 0)
            {
                var humanInputBlocks = new List<IInteractiveBlock>();
                var uiActionBlocks = new List<UIActionBlock>();
                var uninvokedToolBlocks = new List<FunctionInvocationContentBlock>();

                await foreach (var block in agent.SendMessagesAsync(
                    currentMessages,
                    startsThreadTurn,
                    completesThreadTurn: false,
                    cancellationToken).WithCancellation(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (block is UIActionBlock uiAction)
                    {
                        uiActionBlocks.Add(uiAction);
                    }
                    else if (block is IInteractiveBlock interactive)
                    {
                        humanInputBlocks.Add(interactive);
                    }
                    else if (block is FunctionInvocationContentBlock ficb
                             && ficb.Call is { InformationalOnly: false }
                             && ficb.Result is null)
                    {
                        uninvokedToolBlocks.Add(ficb);
                    }

                    if (currentMessages.Any(request =>
                        block.Role == request.Role))
                    {
                        if (!isInitialRequest || includeInitialRequestBlocks)
                        {
                            turn.AddRequestBlock(block);
                            NotifyBlockAdded(turn, block);
                        }
                    }
                    else
                    {
                        turn.AddResponseBlock(block);
                        NotifyBlockAdded(turn, block);
                    }
                }

                startsThreadTurn = false;
                isInitialRequest = false;
                currentMessages = [];

                uninvokedToolBlocks.RemoveAll(b => b.Result is not null);

                if (humanInputBlocks.Count == 0
                    && uiActionBlocks.Count == 0
                    && uninvokedToolBlocks.Count == 0)
                    break;

                var resultTasks = new List<Task<AIContent>>();

                foreach (var uiAction in uiActionBlocks)
                    resultTasks.Add(uiAction.InvokeAsync(cancellationToken));

                foreach (var interactive in humanInputBlocks)
                    resultTasks.Add(interactive.GetResultAsync(cancellationToken));

                foreach (var toolBlock in uninvokedToolBlocks)
                    resultTasks.Add(InvokeBackendToolAsync(toolBlock, cancellationToken));

                if (humanInputBlocks.Count > 0)
                {
                    Status = ConversationStatus.AwaitingInput;
                    NotifyStatusChanged();
                }

                var results = await Task.WhenAll(resultTasks);
                cancellationToken.ThrowIfCancellationRequested();

                if (results.Length > 0)
                {
                    var messages = new List<ChatMessage>(2);
                    var toolResults = results
                        .OfType<FunctionResultContent>()
                        .Cast<AIContent>()
                        .ToArray();
                    if (toolResults.Length > 0)
                    {
                        messages.Add(new ChatMessage(
                            ChatRole.Tool,
                            toolResults)
                        {
                            MessageId = Guid.NewGuid().ToString("N"),
                        });
                    }

                    var userResults = results
                        .Where(static result =>
                            result is not FunctionResultContent)
                        .ToArray();
                    if (userResults.Length > 0)
                    {
                        messages.Add(new ChatMessage(
                            ChatRole.User,
                            userResults)
                        {
                            MessageId = Guid.NewGuid().ToString("N"),
                        });
                    }

                    currentMessages = messages;
                }

                Status = ConversationStatus.Streaming;
                NotifyStatusChanged();
            }

            cancellationToken.ThrowIfCancellationRequested();
            agent.CompleteThreadTurn();
            agent.RejectPendingPredictiveState();

            _retryMessage = null;
            _retryHistoryCheckpoint = default;
            Status = ConversationStatus.Idle;
            NotifyStatusChanged();
            return AttemptOutcome.Succeeded;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            agent.RejectPendingPredictiveState();
            CleanupCanceledTurn(turn, historyCheckpoint, message);
            return AttemptOutcome.Canceled;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            agent.RejectPendingPredictiveState();
            agent.RollbackHistory(historyCheckpoint, message);
            _retryMessage = message;
            _retryHistoryCheckpoint = historyCheckpoint;
            Error = ex;
            Status = ConversationStatus.Error;
            NotifyStatusChanged();
            return AttemptOutcome.Failed;
        }
    }

    private async Task<AIContent> InvokeBackendToolAsync(
        FunctionInvocationContentBlock block,
        CancellationToken cancellationToken)
    {
        var result = await agent.InvokeToolAsync(block.Call!, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        block.Result = result;
        agent.ApplyFunctionResult(block, result);
        block.InvokeNotifyChanged();
        return result;
    }

    public Task CancelAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Status is ConversationStatus.Idle or ConversationStatus.Error)
            return Task.CompletedTask;

        _streamingCts?.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelAndDisposeStreaming();
        agent.RejectPendingPredictiveState();
        _turnAddedCallbacks.Clear();
        _statusChangedCallbacks.Clear();
        _blockAddedCallbacks.Clear();
        _responseBlocksClearedCallbacks.Clear();
    }

    private (CancellationTokenSource Source, CancellationToken Token)
        ReplaceStreamingCancellationSource()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _streamingCts?.Cancel();
        _streamingCts?.Dispose();

        var next = new CancellationTokenSource();
        var token = next.Token;
        _streamingCts = next;
        return (next, token);
    }

    private void CancelAndDisposeStreaming()
    {
        _streamingCts?.Cancel();
        _streamingCts?.Dispose();
        _streamingCts = null;
    }

    private void CleanupCanceledTurn(
        ConversationTurn turn,
        UIAgent.HistoryCheckpoint historyCheckpoint,
        ChatMessage requestMessage)
    {
        turn.ClearResponseBlocks();
        NotifyResponseBlocksCleared(turn);
        agent.RollbackHistory(historyCheckpoint, requestMessage);
        _retryMessage = null;
        _retryHistoryCheckpoint = default;
        Error = null;

        if (Status != ConversationStatus.Idle)
        {
            Status = ConversationStatus.Idle;
            NotifyStatusChanged();
        }
    }

    private static ChatMessage EnsureMessageId(ChatMessage message)
    {
        if (!string.IsNullOrEmpty(message.MessageId))
            return message;

        var clone = message.Clone();
        clone.MessageId = Guid.NewGuid().ToString("N");
        return clone;
    }

    public IDisposable RegisterOnTurnAdded(Action<ConversationTurn> callback)
    {
        _turnAddedCallbacks.Add(callback);
        return new CallbackRegistration<Action<ConversationTurn>>(_turnAddedCallbacks, callback);
    }

    public IDisposable RegisterOnStatusChanged(Action<ConversationStatus> callback)
    {
        _statusChangedCallbacks.Add(callback);
        return new CallbackRegistration<Action<ConversationStatus>>(_statusChangedCallbacks, callback);
    }

    public IDisposable RegisterOnBlockAdded(Action<ConversationTurn, ContentBlock> callback)
    {
        _blockAddedCallbacks.Add(callback);
        return new CallbackRegistration<Action<ConversationTurn, ContentBlock>>(_blockAddedCallbacks, callback);
    }

    /// <summary>
    /// Registers a callback invoked when retry or cancellation removes every response block in a turn.
    /// </summary>
    public IDisposable RegisterOnResponseBlocksCleared(Action<ConversationTurn> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _responseBlocksClearedCallbacks.Add(callback);
        return new CallbackRegistration<Action<ConversationTurn>>(
            _responseBlocksClearedCallbacks,
            callback);
    }

    private void NotifyStatusChanged()
    {
        var snapshot = _statusChangedCallbacks.ToArray();
        foreach (var cb in snapshot)
            cb(Status);
    }

    private void NotifyTurnAdded(ConversationTurn turn)
    {
        var snapshot = _turnAddedCallbacks.ToArray();
        foreach (var cb in snapshot)
            cb(turn);
    }

    private void NotifyBlockAdded(ConversationTurn turn, ContentBlock block)
    {
        var snapshot = _blockAddedCallbacks.ToArray();
        foreach (var cb in snapshot)
            cb(turn, block);
    }

    private void NotifyResponseBlocksCleared(ConversationTurn turn)
    {
        var snapshot = _responseBlocksClearedCallbacks.ToArray();
        foreach (var callback in snapshot)
            callback(turn);
    }

    private enum AttemptOutcome
    {
        Succeeded,
        Failed,
        Canceled,
    }

    private sealed class CallbackRegistration<T> : IDisposable
    {
        private List<T>? _list;
        private T? _callback;

        internal CallbackRegistration(List<T> list, T callback)
        {
            _list = list;
            _callback = callback;
        }

        public void Dispose()
        {
            if (_list is not null && _callback is not null)
            {
                _list.Remove(_callback);
                _list = null;
                _callback = default;
            }
        }
    }
}
