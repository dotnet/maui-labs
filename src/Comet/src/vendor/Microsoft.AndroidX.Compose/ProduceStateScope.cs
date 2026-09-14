using Android.Runtime;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Slot value used by <see cref="ComposeExtensions.ProduceState{T}(T, Func{MutableState{T}, CancellationToken, Task}, int, string)"/>:
/// holds the <see cref="MutableState{T}"/> the caller writes to,
/// plus the producer task / cancellation lifecycle. Implements
/// <see cref="IRememberObserver"/> so it starts the producer when
/// Compose adds the value to the composition
/// (<see cref="OnRemembered"/>) and cancels it when the value is
/// removed (<see cref="OnForgotten"/> / <see cref="OnAbandoned"/>).
///
/// <c>ProduceStateScope</c> must be the <em>direct</em> slot value
/// (not wrapped in <see cref="RememberHolder"/>) — Compose only
/// inspects the exact object handed to
/// <see cref="IComposer.UpdateRememberedValue"/> for the
/// <see cref="IRememberObserver"/> interface.
/// </summary>
[Register("net/compose/ProduceStateScope")]
internal sealed class ProduceStateScope<T> : Java.Lang.Object, IRememberObserver
{
    public readonly MutableState<T> State;
    public object?[]? Keys;

    readonly Func<MutableState<T>, CancellationToken, Task> _producer;
    CancellationTokenSource? _cts;
    bool _started;
    bool _disposed;

    public ProduceStateScope(
        T initial,
        Func<MutableState<T>, CancellationToken, Task> producer,
        object?[]? keys)
    {
        State = new MutableState<T>(initial);
        _producer = producer;
        Keys = keys is null ? null : (object?[])keys.Clone();
    }

    // Peer-rehydration ctor — the .NET-for-Android runtime invokes
    // this when an existing JNI handle for our [Register]'d class
    // crosses back into managed code without a live peer. We never
    // create new scope instances this way (Compose hands back the
    // original peer from its slot table), but the runtime requires
    // the constructor to exist.
    internal ProduceStateScope(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
        // Make non-nullable fields satisfied; this peer will never be
        // used by managed callers — only the slot-table identity is.
        State = null!;
        _producer = null!;
    }

    /// <summary>
    /// Replace the active producer task — used when caller keys
    /// change between compositions: cancel the running task and
    /// kick off a fresh one with the new keys recorded.
    /// </summary>
    public void Restart(object?[]? newKeys)
    {
        Stop();
        Keys = newKeys is null ? null : (object?[])newKeys.Clone();
        _disposed = false;
        _started = false;
        Start();
    }

    public void OnRemembered() => Start();

    public void OnForgotten() => Stop();

    public void OnAbandoned() => Stop();

    void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var task = _producer(State, token);
        // Surface producer faults to logcat instead of leaving them
        // as unobserved task exceptions. Match ComposeExtensions'
        // logging tag so all our diagnostics share a single filter.
        _ = task.ContinueWith(static t =>
        {
            if (t.Exception is { } ex)
            {
                Android.Util.Log.Error(
                    "AndroidX.Compose",
                    "ProduceState producer faulted: " + ex);
            }
        }, TaskScheduler.Default);
    }

    void Stop()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts?.Cancel(); }
        catch { /* token source may already be disposed */ }
        _cts?.Dispose();
        _cts = null;
    }
}
