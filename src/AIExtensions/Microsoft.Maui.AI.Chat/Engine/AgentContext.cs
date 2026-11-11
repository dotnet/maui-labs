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
/// Drives the client side of the tool loop: after streaming a round from the <see cref="UIAgent"/> it
/// invokes automatic UI actions and awaits <see cref="IInteractiveBlock"/>s (approvals, manual UI
/// actions), then feeds the results back for another round. Server tools are invoked by
/// <see cref="FunctionInvokingChatClient"/>, which the chat client must include whenever
/// <see cref="ChatOptions.Tools"/> contains an invocable <see cref="AIFunction"/>.
/// <para>
/// Each turn is a transaction. A completed turn is committed to the agent's history and thread. A
/// stopped turn (<see cref="CancelAsync()"/> or the caller's cancellation token) keeps its request
/// and partial output, is committed without calls that never received a result, and is marked
/// <see cref="ConversationTurn.IsStopped"/>. A failed turn commits nothing and can be retried with
/// <see cref="RetryAsync"/>.
/// </para>
/// <para>
/// This type is single-thread-affine and is not thread-safe. Callers must serialize access and
/// enter the owning application thread before calling it. Status checks are misuse guards, not
/// synchronization.
/// </para>
/// </remarks>
public sealed class AgentContext : IDisposable
{
    private readonly UIAgent _agent;
    private readonly List<ConversationTurn> _turns = new();
    private readonly List<Action<ConversationTurn>> _turnAddedCallbacks = new();
    private readonly List<Action<ConversationStatus>> _statusChangedCallbacks = new();
    private readonly List<Action<ConversationTurn, ContentBlock>> _blockAddedCallbacks = new();
    private readonly List<Action<ConversationTurn>> _responseBlocksClearedCallbacks = new();
    private Attempt? _attempt;
    private ChatMessage? _retryMessage;
    private bool _restoreCompleted;
    private bool _disposed;

    public AgentContext(UIAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agent = agent;
    }

    public IReadOnlyList<ConversationTurn> Turns => _turns;

    public ConversationStatus Status { get; private set; }

    public Exception? Error { get; private set; }

    /// <summary>
    /// Stops and discards the active response, if any, and then clears all conversation turns,
    /// local and persistent history, typed state, and error state. The underlying agent and tools
    /// remain configured.
    /// </summary>
    /// <returns>A task that completes when the conversation has been cleared.</returns>
    /// <remarks>
    /// The active response is discarded rather than committed, and it is awaited before anything
    /// is cleared, so it cannot change the cleared conversation. Do not block on the returned task
    /// from an engine callback or UI action; the response cannot unwind while its thread is blocked.
    /// </remarks>
    public async Task ClearAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (_attempt is { } attempt)
        {
            attempt.Abandon();
            await attempt.Unwound;
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        _agent.RejectPendingPredictiveState();
        _agent.ResetState();
        _turns.Clear();
        _agent.ClearHistory();
        _retryMessage = null;
        _restoreCompleted = false;
        Error = null;
        Status = ConversationStatus.Idle;
        NotifyStatusChanged();
    }

    public Task SendMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        return SendMessageAsync(new ChatMessage(ChatRole.User, text), cancellationToken);
    }

    /// <summary>Sends a message and streams the response into a new turn.</summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">
    /// Stops the turn like <see cref="CancelAsync()"/>; the returned task is then canceled.
    /// </param>
    /// <returns>A task that completes when the turn is committed, stopped, or failed.</returns>
    public async Task SendMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_attempt is not null
            || Status is ConversationStatus.Streaming or ConversationStatus.AwaitingInput)
        {
            throw new InvalidOperationException("A message is already being processed.");
        }

        _agent.ThrowIfToolsCannotBeInvoked();
        var requestMessage = EnsureMessageId(message);
        var turn = new ConversationTurn { Id = requestMessage.MessageId! };
        _turns.Add(turn);
        NotifyTurnAdded(turn);

        await RunAttemptAsync(
            requestMessage,
            turn,
            includeInitialRequestBlocks: true,
            cancellationToken);
    }

    /// <summary>
    /// Restores committed conversation turns from the configured <see cref="IConversationThread"/>.
    /// </summary>
    /// <remarks>
    /// Restore requires an idle, empty context and may be called only once before
    /// <see cref="ClearAsync"/>. It reconstructs display/history blocks only: restored approvals,
    /// tools, and other interactive blocks are not resumed or invoked. Once all turns are loaded, each
    /// is announced through <see cref="RegisterOnTurnAdded"/> with its blocks already in place; no
    /// block-added or status-changed notifications are raised.
    /// </remarks>
    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfCannotRestore(cancellationToken);
        AddRestoredTurns(await _agent.RestoreTurnsAsync(cancellationToken));
    }

    /// <summary>
    /// Loads an existing conversation, such as a recorded or imported one, so it is shown as turns and
    /// the next <see cref="SendMessageAsync(ChatMessage, CancellationToken)"/> continues it.
    /// </summary>
    /// <param name="messages">The conversation's messages in order.</param>
    /// <param name="cancellationToken">Cancels the restore.</param>
    /// <remarks>
    /// <para>
    /// Each user message starts a turn, except one that only answers approvals or tool calls. The
    /// messages are rendered by the registered handlers but nothing is invoked: tool calls and UI
    /// actions are shown with their recorded results, and approvals and UI actions are not resumed.
    /// System messages are sent with the history but not shown.
    /// </para>
    /// <para>
    /// The messages become the history sent with the next request, except tool calls without a
    /// recorded result and their approvals, which a provider would reject or execute.
    /// </para>
    /// <para>
    /// Restore requires an idle, empty context and may be called only once before
    /// <see cref="ClearAsync"/>. It cannot be used with a configured <see cref="IConversationThread"/>,
    /// which is the source of truth for its conversation; restore from the thread instead.
    /// </para>
    /// <para>
    /// Once all turns are loaded, each is announced through <see cref="RegisterOnTurnAdded"/> with its
    /// blocks already in place, so a view that already shows this context renders them. No block-added or
    /// status-changed notifications are raised.
    /// </para>
    /// </remarks>
    public async Task RestoreAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ThrowIfCannotRestore(cancellationToken);
        AddRestoredTurns(await _agent.RestoreTurnsAsync(messages, cancellationToken));
    }

    private void ThrowIfCannotRestore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_attempt is not null || Status != ConversationStatus.Idle)
            throw new InvalidOperationException("RestoreAsync requires an idle context.");

        if (_restoreCompleted || _turns.Count != 0)
        {
            throw new InvalidOperationException(
                "RestoreAsync requires an empty context and may not be called twice. Call ClearAsync first.");
        }
    }

    private void AddRestoredTurns(IReadOnlyList<RestoredTurn> restoredTurns)
    {
        var firstRestored = _turns.Count;
        foreach (var restoredTurn in restoredTurns)
        {
            var turn = new ConversationTurn
            {
                Id = restoredTurn.Id,
                IsStopped = restoredTurn.IsStopped,
                ModelId = restoredTurn.ModelId,
                Usage = restoredTurn.Usage,
            };
            foreach (var block in restoredTurn.RequestBlocks)
                turn.AddRequestBlock(block);
            foreach (var block in restoredTurn.ResponseBlocks)
                turn.AddResponseBlock(block);
            _turns.Add(turn);
        }

        _restoreCompleted = true;

        // Announce turns only once all of them are loaded, so a view that rebuilds from Turns on the first
        // notification shows the whole conversation. Restored blocks are not announced individually.
        for (var i = firstRestored; i < _turns.Count; i++)
            NotifyTurnAdded(_turns[i]);
    }

    /// <summary>
    /// Retries the last failed message in its existing turn. The whole turn is sent again.
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

        _agent.ThrowIfToolsCannotBeInvoked();
        var turn = _turns[^1];
        turn.ClearResponseBlocks();
        NotifyResponseBlocksCleared(turn);

        await RunAttemptAsync(
            _retryMessage,
            turn,
            includeInitialRequestBlocks: false,
            cancellationToken);
    }

    public Task CancelAsync() => CancelAsync(CancellationToken.None);

    /// <summary>
    /// Stops the active response and waits until it has stopped. The turn keeps its request and
    /// partial output and is marked <see cref="ConversationTurn.IsStopped"/>.
    /// </summary>
    /// <param name="cancellationToken">
    /// Stops waiting for the response to stop. The stop request itself is not withdrawn.
    /// </param>
    /// <returns>A task that completes when the response has stopped, or immediately when idle.</returns>
    /// <remarks>
    /// Do not block on the returned task from an engine callback or UI action; the response cannot
    /// unwind while its thread is blocked.
    /// </remarks>
    public async Task CancelAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_attempt is not { } attempt)
            return;

        attempt.Stop();
        await attempt.Unwound.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _attempt?.Abandon();
        _agent.RejectPendingPredictiveState();
        _turnAddedCallbacks.Clear();
        _statusChangedCallbacks.Clear();
        _blockAddedCallbacks.Clear();
        _responseBlocksClearedCallbacks.Clear();
    }

    private async Task RunAttemptAsync(
        ChatMessage message,
        ConversationTurn turn,
        bool includeInitialRequestBlocks,
        CancellationToken callerToken)
    {
        var attempt = new Attempt(callerToken);
        _attempt = attempt;
        AttemptOutcome outcome;
        try
        {
            outcome = await StreamIntoTurnAsync(
                attempt,
                message,
                turn,
                includeInitialRequestBlocks);
        }
        finally
        {
            if (ReferenceEquals(_attempt, attempt))
                _attempt = null;

            // Last: CancelAsync and ClearAsync resume once every state change above is done.
            attempt.Complete();
        }

        if (outcome != AttemptOutcome.Succeeded)
            callerToken.ThrowIfCancellationRequested();
    }

    private async Task<AttemptOutcome> StreamIntoTurnAsync(
        Attempt attempt,
        ChatMessage message,
        ConversationTurn turn,
        bool includeInitialRequestBlocks)
    {
        var cancellationToken = attempt.Token;
        Status = ConversationStatus.Streaming;
        Error = null;
        NotifyStatusChanged();

        var pendingTurn = _agent.BeginTurn(turn);
        var interactiveBlocks = new List<IInteractiveBlock>();
        try
        {
            IReadOnlyList<ChatMessage> currentMessages = [message];
            var isInitialRequest = true;

            while (currentMessages.Count > 0)
            {
                var humanInputBlocks = new List<IInteractiveBlock>();
                var uiActionBlocks = new List<UIActionBlock>();
                var toolCallBlocks = new List<FunctionInvocationContentBlock>();

                await foreach (var block in _agent.SendRoundAsync(
                    pendingTurn,
                    currentMessages,
                    cancellationToken).WithCancellation(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    switch (block)
                    {
                        case UIActionBlock { IsComplete: true }:
                            break;
                        case UIActionBlock { Mode: UIActionInvocationMode.Automatic } uiAction:
                            uiActionBlocks.Add(uiAction);
                            interactiveBlocks.Add(uiAction);
                            break;
                        case IInteractiveBlock interactive:
                            humanInputBlocks.Add(interactive);
                            interactiveBlocks.Add(interactive);
                            break;
                        case FunctionInvocationContentBlock toolCall:
                            toolCallBlocks.Add(toolCall);
                            break;
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

                isInitialRequest = false;
                currentMessages = [];

                if (humanInputBlocks.Count == 0 && uiActionBlocks.Count == 0)
                    break;

                var resultTasks = new List<Task<AIContent>>();

                foreach (var uiAction in uiActionBlocks)
                {
                    _ = uiAction.InvokeAsync(cancellationToken);
                    resultTasks.Add(uiAction.GetResultAsync(cancellationToken));
                }

                foreach (var interactive in humanInputBlocks)
                    resultTasks.Add(interactive.GetResultAsync(cancellationToken));

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
                        .ToList();

                    // Server tools run inside FunctionInvokingChatClient. A call that is still
                    // unanswered here was handed back uninvoked (for example because it shared a
                    // batch with a UI action), and the provider rejects a continuation that leaves
                    // it without a result.
                    foreach (var toolCall in toolCallBlocks)
                    {
                        if (toolCall is { Result: null, Call: { InformationalOnly: false } call })
                        {
                            var error = _agent.CreateUnansweredCallResult(call);
                            toolCall.Result = error;
                            toolCall.InvokeNotifyChanged();
                            toolResults.Add(error);
                        }
                    }

                    if (toolResults.Count > 0)
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
            _agent.CommitTurn(pendingTurn, stopped: false);
            _agent.RejectPendingPredictiveState();

            _retryMessage = null;
            Status = ConversationStatus.Idle;
            NotifyStatusChanged();
            return AttemptOutcome.Succeeded;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            return EndCanceledAttempt(attempt, pendingTurn, turn, interactiveBlocks);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Fail(message, ex);
            return AttemptOutcome.Failed;
        }
    }

    private AttemptOutcome EndCanceledAttempt(
        Attempt attempt,
        PendingTurn pendingTurn,
        ConversationTurn turn,
        List<IInteractiveBlock> interactiveBlocks)
    {
        _agent.RejectPendingPredictiveState();
        AbandonPendingInteractions(interactiveBlocks);

        if (!attempt.Discard)
        {
            try
            {
                _agent.CommitTurn(pendingTurn, stopped: true);
            }
            catch (Exception ex)
            {
                Fail(pendingTurn.InitialMessage, ex);
                return AttemptOutcome.Failed;
            }

            turn.IsStopped = true;
        }

        _retryMessage = null;
        Error = null;
        Status = ConversationStatus.Idle;
        NotifyStatusChanged();
        return AttemptOutcome.Stopped;
    }

    private void Fail(ChatMessage? message, Exception exception)
    {
        _agent.RejectPendingPredictiveState();
        _retryMessage = message;
        Error = exception;
        Status = ConversationStatus.Error;
        _agent.LogProcessingError(exception);
        NotifyStatusChanged();
    }

    private static void AbandonPendingInteractions(List<IInteractiveBlock> interactiveBlocks)
    {
        foreach (var interactive in interactiveBlocks)
        {
            switch (interactive)
            {
                case ToolApprovalBlock approval:
                    approval.Abandon();
                    break;
                case UIActionBlock uiAction:
                    uiAction.Abandon();
                    break;
            }
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

    /// <summary>
    /// Registers a callback invoked when a turn is added to <see cref="Turns"/>: when a message is sent,
    /// before its response streams, and for each turn loaded by <c>RestoreAsync</c>, with its blocks
    /// already in place.
    /// </summary>
    /// <param name="callback">The callback, which receives the added turn.</param>
    /// <returns>A registration that removes the callback when disposed.</returns>
    public IDisposable RegisterOnTurnAdded(Action<ConversationTurn> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _turnAddedCallbacks.Add(callback);
        return new CallbackRegistration<Action<ConversationTurn>>(_turnAddedCallbacks, callback);
    }

    public IDisposable RegisterOnStatusChanged(Action<ConversationStatus> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _statusChangedCallbacks.Add(callback);
        return new CallbackRegistration<Action<ConversationStatus>>(_statusChangedCallbacks, callback);
    }

    public IDisposable RegisterOnBlockAdded(Action<ConversationTurn, ContentBlock> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _blockAddedCallbacks.Add(callback);
        return new CallbackRegistration<Action<ConversationTurn, ContentBlock>>(_blockAddedCallbacks, callback);
    }

    /// <summary>
    /// Registers a callback invoked when <see cref="RetryAsync"/> removes every response block in a turn.
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
        Stopped,
    }

    /// <summary>One run of a turn: its cancellation and the signal that it has fully unwound.</summary>
    private sealed class Attempt
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly TaskCompletionSource _unwound =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Attempt(CancellationToken callerToken)
        {
            _cancellation = callerToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(callerToken)
                : new CancellationTokenSource();
            Token = _cancellation.Token;
        }

        internal CancellationToken Token { get; }

        /// <summary>Gets whether cancellation discards the turn instead of stopping it.</summary>
        internal bool Discard { get; private set; }

        internal Task Unwound => _unwound.Task;

        internal void Stop() => _cancellation.Cancel();

        internal void Abandon()
        {
            Discard = true;
            _cancellation.Cancel();
        }

        internal void Complete()
        {
            _cancellation.Dispose();
            _unwound.TrySetResult();
        }
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
