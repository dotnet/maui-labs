using Android.Runtime;
using Kotlin.Coroutines;
using Xamarin.KotlinX.Coroutines;

namespace AndroidX.Compose;

/// <summary>
/// JCW implementing Kotlin's <see cref="IContinuation"/> for the
/// long-running <c>Flow.collect</c> call that drives
/// <see cref="ComposeExtensions.SnapshotFlow{T}(Func{T})"/>. Unlike
/// <see cref="SuspendContinuation"/>, which models a one-shot suspend
/// resumed back into a <see cref="TaskCompletionSource{TResult}"/>,
/// this continuation:
/// <list type="bullet">
/// <item><description>Exposes a <see cref="Context"/> combining
/// <c>AndroidUiDispatcher.Main</c> with a Kotlin <see cref="IJob"/>
/// so that cancelling the job tears down the flow's
/// <c>snapshotFlow</c> internals (its apply observer registration,
/// its internal changes channel, etc.).</description></item>
/// <item><description>On <see cref="ResumeWith(Java.Lang.Object)"/>
/// — fired once when Kotlin's collect coroutine finishes for any
/// reason — completes the consumer's channel writer so the
/// <c>await foreach</c> exits cleanly (with the underlying exception
/// if the flow faulted with anything other than a normal
/// cancellation).</description></item>
/// </list>
/// </summary>
[Register("net/compose/SnapshotFlowContinuation")]
internal sealed class SnapshotFlowContinuation : Java.Lang.Object, IContinuation
{
    readonly IJob _job;
    readonly TaskCompletionSource<object?> _tcs;

    /// <summary>
    /// Task that completes when Kotlin's collect coroutine has fully
    /// resumed (i.e. <see cref="ResumeWith(Java.Lang.Object)"/> has
    /// fired). The result is irrelevant — callers <c>await</c> it just
    /// to know the Kotlin side has unwound (used by
    /// <see cref="SnapshotFlowEnumerator{T}.DisposeAsync"/>).
    /// </summary>
    public Task Completion => _tcs.Task;

    public SnapshotFlowContinuation(IJob job)
    {
        _job = job;
        _tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Coroutine context = <c>AndroidUiDispatcher.Main</c> combined
    /// with our <see cref="IJob"/>. The Main dispatcher provides the
    /// <c>MonotonicFrameClock</c> Compose needs internally; the Job
    /// is what lets us cancel from C#.
    /// </summary>
    public ICoroutineContext Context
    {
        get
        {
            var mainHandle = ComposeBridges.AndroidUiDispatcherMain();
            var main = Java.Lang.Object.GetObject<ICoroutineContext>(
                mainHandle, JniHandleOwnership.DoNotTransfer)!;
            return main.Plus(_job);
        }
    }

    /// <summary>
    /// Callback invoked once when Kotlin's collect coroutine
    /// resumes. Receives <c>null</c> for normal completion /
    /// cancellation, or the underlying <see cref="Exception"/>
    /// when the flow faulted with anything other than a
    /// <c>CancellationException</c>.
    /// </summary>
    /// <remarks>
    /// Set this exactly once before the Kotlin coroutine can ever
    /// reach a resume point (i.e. immediately after constructing the
    /// continuation, before passing it to <c>flow.Collect</c>). It's
    /// read once on the resume path and isn't expected to change.
    /// </remarks>
    internal Action<Exception?>? OnResumed { get; set; }

    /// <summary>
    /// Forces <see cref="Completion"/> to complete without going
    /// through Kotlin's <see cref="ResumeWith(Java.Lang.Object)"/>
    /// path. Use this only when the caller knows the suspend
    /// function completed synchronously (returned a non-suspended
    /// value before ever scheduling a resume), since Kotlin then
    /// never invokes the continuation and we'd otherwise wait
    /// forever for the resume that's never coming.
    /// </summary>
    internal void MarkSynchronouslyCompleted()
    {
        try { OnResumed?.Invoke(null); }
        catch (Exception ex) { _tcs.TrySetException(ex); return; }
        _tcs.TrySetResult(null);
    }

    public void ResumeWith(Java.Lang.Object p0)
    {
        Exception? error = null;
        try
        {
            if (p0 is not null && KotlinResult.IsFailure(p0.Handle))
            {
                var ex = KotlinResult.ExtractException(p0);
                // CancellationException is the normal "we were told
                // to stop" signal — surface as a clean stream close,
                // not as an exception on the awaiter.
                if (ex is not Java.Util.Concurrent.CancellationException)
                    error = ex;
            }
        }
        catch (Exception ex)
        {
            // KotlinResult helpers can throw on malformed boxes.
            // Surface the diagnostic to the awaiter rather than
            // swallowing it.
            error = ex;
        }

        try
        {
            OnResumed?.Invoke(error);
        }
        catch (Exception ex)
        {
            // The enumerator's completion handler should be
            // exception-free, but if it ever isn't we still need to
            // unblock awaiters of Completion.
            _tcs.TrySetException(ex);
            return;
        }

        _tcs.TrySetResult(null);
    }
}
