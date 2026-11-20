#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CometBaristaNotes.Services;

internal enum EditorLeaveOutcome
{
    Left,
    KeptEditing,
    Cancelled,
}

internal readonly record struct EditorLeaveRequest(
    long Generation,
    Task<EditorLeaveOutcome> Completion)
{
    public bool RequiresConfirmation => Generation != 0;
}

internal sealed class EditorLeaveGuard
{
    readonly object _sync = new();
    PendingLeave? _pending;
    long _nextGeneration;

    public EditorLeaveRequest Begin(
        bool requiresConfirmation,
        Action confirmedLeave,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmedLeave);
        if (cancellationToken.IsCancellationRequested)
        {
            return new EditorLeaveRequest(
                0,
                Task.FromResult(EditorLeaveOutcome.Cancelled));
        }
        if (!requiresConfirmation)
        {
            confirmedLeave();
            return new EditorLeaveRequest(
                0,
                Task.FromResult(EditorLeaveOutcome.Left));
        }

        var completion = new TaskCompletionSource<EditorLeaveOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingLeave(
            Interlocked.Increment(ref _nextGeneration),
            confirmedLeave,
            completion);
        SetPending(pending);
        if (cancellationToken.CanBeCanceled)
        {
            pending.CancellationRegistration = cancellationToken.Register(
                () => Cancel(pending.Generation, EditorLeaveOutcome.Cancelled));
            if (completion.Task.IsCompleted)
                pending.CancellationRegistration.Dispose();
        }
        return new EditorLeaveRequest(pending.Generation, completion.Task);
    }

    public bool Confirm(long generation, Action beforeConfirmedLeave)
    {
        ArgumentNullException.ThrowIfNull(beforeConfirmedLeave);
        var pending = TakePending(generation);
        if (pending is null)
            return false;

        pending.CancellationRegistration.Dispose();
        try
        {
            beforeConfirmedLeave();
            pending.ConfirmedLeave();
            pending.Completion.TrySetResult(EditorLeaveOutcome.Left);
            return true;
        }
        catch (Exception ex)
        {
            pending.Completion.TrySetException(ex);
            throw;
        }
    }

    public bool KeepEditing(long generation) =>
        Cancel(generation, EditorLeaveOutcome.KeptEditing);

    public void Cancel()
    {
        PendingLeave? pending;
        lock (_sync)
        {
            pending = _pending;
            _pending = null;
        }
        if (pending is null)
            return;
        pending.CancellationRegistration.Dispose();
        pending.Completion.TrySetResult(EditorLeaveOutcome.KeptEditing);
    }

    void SetPending(PendingLeave pending)
    {
        PendingLeave? previous;
        lock (_sync)
        {
            previous = _pending;
            _pending = pending;
        }
        previous?.CancellationRegistration.Dispose();
        previous?.Completion.TrySetResult(EditorLeaveOutcome.KeptEditing);
    }

    PendingLeave? TakePending(long generation)
    {
        lock (_sync)
        {
            if (_pending?.Generation != generation)
                return null;
            var pending = _pending;
            _pending = null;
            return pending;
        }
    }

    bool Cancel(long generation, EditorLeaveOutcome outcome)
    {
        var pending = TakePending(generation);
        if (pending is null)
            return false;
        if (outcome != EditorLeaveOutcome.Cancelled)
            pending.CancellationRegistration.Dispose();
        pending.Completion.TrySetResult(outcome);
        return true;
    }

    sealed class PendingLeave(
        long generation,
        Action confirmedLeave,
        TaskCompletionSource<EditorLeaveOutcome> completion)
    {
        public long Generation { get; } = generation;
        public Action ConfirmedLeave { get; } = confirmedLeave;
        public TaskCompletionSource<EditorLeaveOutcome> Completion { get; } = completion;
        public CancellationTokenRegistration CancellationRegistration { get; set; }
    }
}
