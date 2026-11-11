// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// The stateful conversation object the UI binds to. Groups <see cref="ContentBlock"/>s into
/// <see cref="ConversationTurn"/>s, tracks <see cref="Status"/>, and raises the standard .NET change events
/// as turns and blocks arrive.
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
/// <see cref="ConversationTurn.IsStopped"/>. A failed turn commits nothing, is marked
/// <see cref="ConversationTurn.IsFailed"/> with the exception in <see cref="ConversationTurn.Error"/>, and can
/// be retried with <see cref="RetryAsync"/>.
/// </para>
/// <para>
/// Event handlers run inline. An exception thrown by one before the turn is committed fails the turn; one
/// thrown while a committed or stopped turn is reported reaches the caller of the send or retry, and the
/// turn stays committed. The status change that reports the end of a turn (<see cref="ConversationStatus.Idle"/>
/// or <see cref="ConversationStatus.Error"/>) is raised once the turn is no longer active, so its
/// handler can already send the next message, retry, or clear. A handler that runs after one that sent
/// reads the status of the next send; the turn that ended keeps <see cref="ConversationTurn.IsFailed"/>,
/// <see cref="ConversationTurn.Error"/> and <see cref="ConversationTurn.IsStopped"/>, which say how it ended.
/// </para>
/// <para>
/// The context, its turns and their blocks are bindable: the context raises <see cref="PropertyChanged"/>
/// for <see cref="Status"/> and <see cref="Error"/>, <see cref="Turns"/> and each turn's
/// <see cref="ConversationTurn.RequestBlocks"/> and <see cref="ConversationTurn.ResponseBlocks"/> raise
/// <see cref="System.Collections.Specialized.INotifyCollectionChanged.CollectionChanged"/>, and turns and
/// blocks raise <see cref="INotifyPropertyChanged.PropertyChanged"/>. Every event is raised synchronously,
/// on the thread that makes the change, once the state it reports is in place; the engine has no
/// dispatcher. A UI therefore drives the context from its UI thread: it sends, retries, cancels and clears
/// there, and the response then streams on that thread through its synchronization context.
/// </para>
/// <para>
/// Handlers of these events may call back into the context. A handler of a turn's
/// addition or of a retry's removal of response blocks cannot start another send or retry, because the
/// turn's attempt already started; a handler of a reset of <see cref="Turns"/> cannot send either, so every
/// handler of a collection change sees the change before the next one. Nothing can send while a restore
/// runs, including a handler of a typed-state change the restore replays. Send the next message from a
/// status change instead.
/// </para>
/// <para>
/// This type is single-thread-affine and is not thread-safe. Callers must serialize access and
/// enter the owning application thread before calling it. Status checks are misuse guards, not
/// synchronization.
/// </para>
/// </remarks>
public sealed class AgentContext : IDisposable, INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs s_statusChanged = new(nameof(Status));
    private static readonly PropertyChangedEventArgs s_errorChanged = new(nameof(Error));

    private readonly UIAgent _agent;
    private readonly ObservableList<ConversationTurn> _turns = new();
    private Attempt? _attempt;
    private TaskCompletionSource? _restore;
    private ChatMessage? _retryMessage;
    private bool _restoreCompleted;
    private bool _resettingTurns;
    private ConversationStatus _raisedStatus;
    private Exception? _raisedError;
    private bool _disposed;

    public AgentContext(UIAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agent = agent;
        Turns = new ReadOnlyObservableCollection<ConversationTurn>(_turns);
    }

    /// <summary>Occurs after <see cref="Status"/> or <see cref="Error"/> changed.</summary>
    /// <remarks>
    /// A handler reads the current values. One that runs earlier may already have started the next send, so
    /// a later handler reads the status that send reports; the turn that ended keeps
    /// <see cref="ConversationTurn.IsFailed"/>, <see cref="ConversationTurn.Error"/> and
    /// <see cref="ConversationTurn.IsStopped"/>. A value that changed and changed back before it was raised is
    /// not raised.
    /// </remarks>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the conversation's turns, in order.</summary>
    /// <remarks>
    /// Raises <see cref="System.Collections.Specialized.INotifyCollectionChanged.CollectionChanged"/> when a
    /// send adds a turn, before its response streams, and a
    /// <see cref="System.Collections.Specialized.NotifyCollectionChangedAction.Reset"/> once a restore loaded
    /// every turn and when <see cref="ClearAsync"/> removed them. Cast it to
    /// <see cref="System.Collections.Specialized.INotifyCollectionChanged"/> to subscribe from code; XAML
    /// bindings find the event themselves.
    /// </remarks>
    public ReadOnlyObservableCollection<ConversationTurn> Turns { get; }

    /// <summary>Gets the conversation's status.</summary>
    public ConversationStatus Status { get; private set; }

    /// <summary>Gets the exception that failed the last turn, while <see cref="Status"/> is <see cref="ConversationStatus.Error"/>.</summary>
    /// <remarks>
    /// The next send, retry or clear resets it. The failed turn keeps the exception in
    /// <see cref="ConversationTurn.Error"/>.
    /// </remarks>
    public Exception? Error { get; private set; }

    /// <summary>
    /// Stops and discards the active response, if any, and then clears all conversation turns,
    /// local and persistent history, typed state, and error state. The underlying agent and tools
    /// remain configured.
    /// </summary>
    /// <returns>A task that completes when the conversation has been cleared.</returns>
    /// <remarks>
    /// <para>
    /// The active response is discarded rather than committed, and it is awaited before anything
    /// is cleared, so it cannot change the cleared conversation. A restore in progress is awaited too,
    /// so it cannot reload what the clear removes. Do not block on the returned task from an engine
    /// event handler or UI action; the response cannot unwind while its thread is blocked.
    /// </para>
    /// <para>
    /// The typed state is reset last, in one change, so a handler of that change finds the conversation
    /// cleared and can already send its first message.
    /// </para>
    /// </remarks>
    public async Task ClearAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (true)
        {
            if (_attempt is { } attempt)
            {
                attempt.Abandon();
                await attempt.Unwound;
            }
            else if (_restore is { } restore)
            {
                await restore.Task;
            }
            else
            {
                break;
            }

            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        _agent.ClearHistory();
        _retryMessage = null;
        _restoreCompleted = false;
        Error = null;
        Status = ConversationStatus.Idle;
        ResetTurns(Array.Empty<ConversationTurn>());

        // Resetting the state also drops a pending prediction.
        _agent.ResetState();
        RaiseStatusChanged();
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

        if (_resettingTurns)
        {
            throw new InvalidOperationException(
                "A message cannot be sent while the reset of Turns is reported. Send it from a status change.");
        }

        if (_restore is not null)
        {
            throw new InvalidOperationException(
                "A message cannot be sent while a restore is in progress. Send it once RestoreAsync returned.");
        }

        _agent.ThrowIfToolsCannotBeInvoked();
        var requestMessage = EnsureMessageId(message);
        var turn = new ConversationTurn { Id = requestMessage.MessageId! };

        // The attempt starts before the turn is reported, so a handler of the report cannot start another.
        var attempt = BeginAttempt(cancellationToken);
        try
        {
            _turns.Add(turn);
        }
        catch
        {
            EndAttempt(attempt);
            attempt.Complete();
            throw;
        }

        await RunAttemptAsync(
            attempt,
            requestMessage,
            turn,
            includeInitialRequestBlocks: true);
    }

    /// <summary>
    /// Restores committed conversation turns from the configured <see cref="IConversationThread"/>.
    /// </summary>
    /// <remarks>
    /// Restore requires an idle, empty context and may be called only once before
    /// <see cref="ClearAsync"/>. It reconstructs display/history blocks only: restored approvals,
    /// tools, and other interactive blocks are not resumed or invoked. A call that its turn left without a
    /// result is <see cref="FunctionInvocationContentBlock.IsAbandoned"/>, and the rebuilt history leaves it
    /// out, as the live turn's history did. Once all turns are loaded,
    /// <see cref="Turns"/> raises one reset, with every turn's blocks already in place; the turns' block
    /// collections and the status raise nothing. The typed state raises the changes the restore replays.
    /// Until the turns are loaded, a send fails and <see cref="ClearAsync"/> waits for the restore.
    /// </remarks>
    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfCannotRestore(cancellationToken);
        await RunRestoreAsync(_agent.RestoreTurnsAsync, cancellationToken);
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
    /// Once all turns are loaded, <see cref="Turns"/> raises one reset, with every turn's blocks already
    /// in place, so a view that already shows this context renders them. The turns' block collections and
    /// the status raise nothing. The typed state raises the changes the restore replays. Until the turns
    /// are loaded, a send fails and <see cref="ClearAsync"/> waits for the restore.
    /// </para>
    /// </remarks>
    public async Task RestoreAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ThrowIfCannotRestore(cancellationToken);
        await RunRestoreAsync(token => _agent.RestoreTurnsAsync(messages, token), cancellationToken);
    }

    /// <summary>
    /// Runs the agent's restore as the context's restore in progress, then loads the turns it rebuilt.
    /// </summary>
    private async Task RunRestoreAsync(
        Func<CancellationToken, Task<IReadOnlyList<RestoredTurn>>> restoreTurnsAsync,
        CancellationToken cancellationToken)
    {
        // The restore is in progress before the agent runs it, so a handler of a state change it replays
        // cannot send into, or restore over, the conversation it is rebuilding.
        var restore = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _restore = restore;
        try
        {
            var turns = await restoreTurnsAsync(cancellationToken);

            // The turns' announcements may already continue the conversation.
            EndRestore(restore);
            AddRestoredTurns(turns);
        }
        finally
        {
            EndRestore(restore);

            // Last: a clear that waits for the restore resumes once it has been reported.
            restore.TrySetResult();
        }
    }

    private void EndRestore(TaskCompletionSource restore)
    {
        if (ReferenceEquals(_restore, restore))
            _restore = null;
    }

    private void ThrowIfCannotRestore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_attempt is not null || Status != ConversationStatus.Idle)
            throw new InvalidOperationException("RestoreAsync requires an idle context.");

        if (_restoreCompleted || _restore is not null || _turns.Count != 0)
        {
            throw new InvalidOperationException(
                "RestoreAsync requires an empty context and may not be called twice. Call ClearAsync first.");
        }
    }

    private void AddRestoredTurns(IReadOnlyList<RestoredTurn> restoredTurns)
    {
        var turns = new List<ConversationTurn>(restoredTurns.Count);
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
            turns.Add(turn);
        }

        _restoreCompleted = true;
        if (turns.Count == 0)
            return;

        // Raise the reset only once all turns are loaded, so a view that rebuilds from Turns shows the whole
        // conversation. Restored blocks are not announced individually.
        ResetTurns(turns);
    }

    /// <summary>Replaces the turns and raises the reset of <see cref="Turns"/>.</summary>
    private void ResetTurns(IEnumerable<ConversationTurn> turns)
    {
        var wasResetting = _resettingTurns;
        _resettingTurns = true;
        try
        {
            _turns.Reset(turns);
        }
        finally
        {
            _resettingTurns = wasResetting;
        }
    }

    /// <summary>
    /// Retries the last failed message in its existing turn. The whole turn is sent again.
    /// </summary>
    public async Task RetryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_attempt is not null)
            throw new InvalidOperationException("A message is already being processed.");

        if (Status != ConversationStatus.Error || _retryMessage is not { } message)
        {
            throw new InvalidOperationException(
                $"RetryAsync requires Status == Error, but Status is {Status}.");
        }

        _agent.ThrowIfToolsCannotBeInvoked();
        var turn = _turns[^1];

        // The attempt starts before the response blocks are removed, so a handler of the removal cannot
        // start another.
        var attempt = BeginAttempt(cancellationToken);
        try
        {
            turn.ClearResponseBlocks();
        }
        catch
        {
            EndAttempt(attempt);
            attempt.Complete();
            throw;
        }

        await RunAttemptAsync(
            attempt,
            message,
            turn,
            includeInitialRequestBlocks: false);
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
    /// Do not block on the returned task from an engine event handler or UI action; the response cannot
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
        PropertyChanged = null;
    }

    private Attempt BeginAttempt(CancellationToken callerToken)
    {
        var attempt = new Attempt(callerToken);
        _attempt = attempt;
        return attempt;
    }

    private async Task RunAttemptAsync(
        Attempt attempt,
        ChatMessage message,
        ConversationTurn turn,
        bool includeInitialRequestBlocks)
    {
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
            EndAttempt(attempt);

            // Last: CancelAsync and ClearAsync resume once every state change above is done.
            attempt.Complete();
        }

        if (outcome != AttemptOutcome.Succeeded)
            attempt.CallerToken.ThrowIfCancellationRequested();
    }

    private async Task<AttemptOutcome> StreamIntoTurnAsync(
        Attempt attempt,
        ChatMessage message,
        ConversationTurn turn,
        bool includeInitialRequestBlocks)
    {
        var cancellationToken = attempt.Token;
        var pendingTurn = _agent.BeginTurn(turn);
        var interactiveBlocks = new List<IInteractiveBlock>();
        try
        {
            var retried = turn.SetErrorSilently(null);
            Status = ConversationStatus.Streaming;
            Error = null;
            turn.RaiseFailureChanged(retried);
            RaiseStatusChanged();

            IReadOnlyList<ChatMessage> currentMessages = [message];
            var isInitialRequest = true;

            while (currentMessages.Count > 0)
            {
                var humanInputBlocks = new List<IInteractiveBlock>();
                var uiActionBlocks = new List<UIActionBlock>();

                // The round stops between updates, so every update it recorded is shown in full, and a stop
                // before the request is sent still shows the request.
                await foreach (var block in _agent.SendRoundAsync(
                    pendingTurn,
                    currentMessages,
                    cancellationToken).WithCancellation(cancellationToken))
                {
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
                    }

                    if (currentMessages.Any(request =>
                        block.Role == request.Role))
                    {
                        if (!isInitialRequest || includeInitialRequestBlocks)
                            turn.AddRequestBlock(block);
                    }
                    else
                    {
                        turn.AddResponseBlock(block);
                    }
                }

                isInitialRequest = false;
                currentMessages = [];

                if (humanInputBlocks.Count == 0 && uiActionBlocks.Count == 0)
                    break;

                _agent.ThrowIfNoRoundLeft(pendingTurn);

                var resultTasks = new List<Task<AIContent[]>>();

                foreach (var uiAction in uiActionBlocks)
                {
                    _ = uiAction.InvokeAsync(cancellationToken);
                    resultTasks.Add(GetResultAsync(uiAction, cancellationToken));
                }

                foreach (var interactive in humanInputBlocks)
                {
                    resultTasks.Add(interactive is ToolApprovalBlock { UIAction: { } action } approval
                        ? RunApprovedUIActionAsync(approval, action, interactiveBlocks, cancellationToken)
                        : GetResultAsync(interactive, cancellationToken));
                }

                if (humanInputBlocks.Count > 0)
                {
                    Status = ConversationStatus.AwaitingInput;
                    RaiseStatusChanged();
                }

                var results = (await Task.WhenAll(resultTasks))
                    .SelectMany(static contents => contents)
                    .ToArray();
                cancellationToken.ThrowIfCancellationRequested();

                if (results.Length > 0)
                {
                    var messages = new List<ChatMessage>(2);
                    var toolResults = results
                        .OfType<FunctionResultContent>()
                        .Cast<AIContent>()
                        .ToList();

                    // Server tools run inside FunctionInvokingChatClient. A call of the round that is
                    // still unanswered here was handed back uninvoked (for example because it shared a
                    // batch with a UI action), and the provider rejects a continuation that leaves it
                    // without a result. The calls come from the round's messages, not from the blocks
                    // that show them, so a call that a handler of the app's own folds into its block is
                    // answered too. The block shows the result once the continuation is sent, through
                    // the handler that claimed the call, like any result.
                    var answered = toolResults
                        .Cast<FunctionResultContent>()
                        .Select(static result => result.CallId)
                        .ToHashSet(StringComparer.Ordinal);
                    foreach (var call in pendingTurn.OpenCalls)
                    {
                        if (!answered.Contains(call.CallId))
                            toolResults.Add(_agent.CreateUnansweredCallResult(call));
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
                RaiseStatusChanged();
            }

            cancellationToken.ThrowIfCancellationRequested();
            _agent.CommitTurn(pendingTurn, stopped: false);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            var abandoned = AbandonUnfinished(turn, interactiveBlocks);
            try
            {
                return EndCanceledAttempt(attempt, pendingTurn, turn);
            }
            finally
            {
                ReportAbandoned(abandoned);
            }
        }
        catch (Exception ex)
        {
            var abandoned = AbandonUnfinished(turn, interactiveBlocks);
            try
            {
                Fail(attempt, turn, message, ex);
            }
            finally
            {
                ReportAbandoned(abandoned);
            }

            return AttemptOutcome.Failed;
        }

        // The turn is committed, so an exception from an app handler while it is reported reaches the
        // caller instead of failing a turn that can no longer be retried.
        var unanswered = AbandonUnfinished(turn, interactiveBlocks: null);
        _retryMessage = null;
        Status = ConversationStatus.Idle;
        try
        {
            ReportAttemptEnded(attempt);
        }
        finally
        {
            ReportAbandoned(unanswered);
        }

        return AttemptOutcome.Succeeded;
    }

    private static async Task<AIContent[]> GetResultAsync(
        IInteractiveBlock interactive,
        CancellationToken cancellationToken)
        => [await interactive.GetResultAsync(cancellationToken)];

    /// <summary>
    /// Waits for the decision on a UI action that needs approval and runs the action once it is approved.
    /// The approval is the action's UI step, so a manual action runs on approval too.
    /// </summary>
    /// <returns>The approval response, followed by the action's result when it was approved.</returns>
    /// <remarks>
    /// <see cref="FunctionInvokingChatClient"/> cannot invoke a UI action, so the client answers the
    /// approved call with the action's result. A rejection is answered by the approval response alone.
    /// </remarks>
    private static async Task<AIContent[]> RunApprovedUIActionAsync(
        ToolApprovalBlock approval,
        UIActionBlock action,
        List<IInteractiveBlock> interactiveBlocks,
        CancellationToken cancellationToken)
    {
        var response = await approval.GetResultAsync(cancellationToken);
        if (response is not ToolApprovalResponseContent { Approved: true })
            return [response];

        interactiveBlocks.Add(action);
        _ = action.InvokeAsync(cancellationToken);
        var result = await action.GetResultAsync(cancellationToken);
        return [response, result];
    }

    private AttemptOutcome EndCanceledAttempt(
        Attempt attempt,
        PendingTurn pendingTurn,
        ConversationTurn turn)
    {
        var stopped = false;
        if (!attempt.Discard)
        {
            try
            {
                _agent.CommitTurn(pendingTurn, stopped: true);
            }
            catch (Exception ex)
            {
                Fail(attempt, turn, pendingTurn.InitialMessage, ex);
                return AttemptOutcome.Failed;
            }

            stopped = turn.SetIsStoppedSilently(true);
        }

        _retryMessage = null;
        Error = null;
        Status = ConversationStatus.Idle;
        ReportAttemptEnded(attempt, stopped ? turn : null);
        return AttemptOutcome.Stopped;
    }

    private void Fail(Attempt attempt, ConversationTurn turn, ChatMessage? message, Exception exception)
    {
        var failure = turn.SetErrorSilently(exception);
        _retryMessage = message;
        Error = exception;
        Status = ConversationStatus.Error;
        EndAttempt(attempt);
        try
        {
            _agent.RejectPendingPredictiveState();
        }
        finally
        {
            _agent.LogProcessingError(exception);
            try
            {
                turn.RaiseFailureChanged(failure);
            }
            finally
            {
                RaiseStatusChanged();
            }
        }
    }

    /// <summary>
    /// Reports that an attempt ended once its outcome is final: rejects a prediction nobody accepted, raises
    /// the change of a stopped turn and then the status change. They run app code, which may already send,
    /// retry or clear; code that throws cannot change the outcome.
    /// </summary>
    private void ReportAttemptEnded(Attempt attempt, ConversationTurn? stoppedTurn = null)
    {
        EndAttempt(attempt);
        try
        {
            _agent.RejectPendingPredictiveState();
        }
        finally
        {
            try
            {
                stoppedTurn?.RaiseIsStoppedChanged();
            }
            finally
            {
                RaiseStatusChanged();
            }
        }
    }

    /// <summary>Stops treating an attempt as active once nothing it does can change the conversation.</summary>
    private void EndAttempt(Attempt attempt)
    {
        if (ReferenceEquals(_attempt, attempt))
            _attempt = null;
    }

    /// <summary>
    /// Ends what the turn left unfinished once nothing can send it: the approvals and UI actions of a turn that
    /// stopped or failed stop accepting input, and the turn's calls without a result are abandoned, because
    /// they can no longer get one. Nothing is reported yet.
    /// </summary>
    /// <param name="turn">The turn that ended.</param>
    /// <param name="interactiveBlocks">
    /// The turn's approvals and UI actions, or <see langword="null"/> once all of them were answered.
    /// </param>
    /// <returns>The blocks whose changes <see cref="ReportAbandoned"/> raises, each once.</returns>
    private static List<ContentBlock> AbandonUnfinished(ConversationTurn turn, List<IInteractiveBlock>? interactiveBlocks)
    {
        var abandoned = new List<ContentBlock>();
        foreach (var interactive in interactiveBlocks ?? [])
        {
            if (interactive is InteractiveFunctionBlock block && block.Abandon())
                abandoned.Add(block);
        }

        foreach (var block in turn.RequestBlocks.Concat(turn.ResponseBlocks))
        {
            var call = block as FunctionInvocationContentBlock ?? (block as InteractiveFunctionBlock)?.InnerBlock;
            if (call is null || !call.Abandon())
                continue;

            abandoned.Add(call);

            // A card shown by an approval or UI action refreshes from the wrapper's notification.
            if (!ReferenceEquals(call, block) && !abandoned.Exists(other => ReferenceEquals(other, block)))
                abandoned.Add(block);
        }

        return abandoned;
    }

    /// <summary>
    /// Raises the property changes of the blocks the turn's end changed. Runs once the turn's outcome is
    /// final, so a handler that throws cannot leave the turn running.
    /// </summary>
    private static void ReportAbandoned(List<ContentBlock> abandoned)
    {
        foreach (var block in abandoned)
        {
            switch (block)
            {
                case InteractiveFunctionBlock interactive:
                    interactive.ReportAwaitingInputChanged();
                    break;
                case FunctionInvocationContentBlock call:
                    call.ReportAbandonedChanged();
                    break;
            }
        }

        // A block of the app's own type also reports that any of its properties may have changed.
        foreach (var block in abandoned)
            block.InvokeNotifyChanged();
    }

    private static ChatMessage EnsureMessageId(ChatMessage message)
    {
        if (!string.IsNullOrEmpty(message.MessageId))
            return message;

        var clone = message.Clone();
        clone.MessageId = Guid.NewGuid().ToString("N");
        return clone;
    }

    /// <summary>Raises the changes of <see cref="Error"/> and <see cref="Status"/> that were not raised yet.</summary>
    private void RaiseStatusChanged()
    {
        // PropertyChanged reports the current values, once each changes. A handler may already have started
        // the next send, which raised its own change; the value is recorded before the handlers run, so a
        // nested change is compared with what this one raised.
        if (!ReferenceEquals(Error, _raisedError))
        {
            _raisedError = Error;
            PropertyChanged?.Invoke(this, s_errorChanged);
        }

        if (Status != _raisedStatus)
        {
            _raisedStatus = Status;
            PropertyChanged?.Invoke(this, s_statusChanged);
        }
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
            CallerToken = callerToken;
            _cancellation = callerToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(callerToken)
                : new CancellationTokenSource();
            Token = _cancellation.Token;
        }

        /// <summary>Gets the token of the send or retry that started the attempt.</summary>
        internal CancellationToken CallerToken { get; }

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
}
