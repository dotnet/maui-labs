using Android.Runtime;
using Kotlin.Coroutines.Intrinsics;

namespace AndroidX.Compose;

/// <summary>
/// Shared plumbing for calling Kotlin <c>suspend</c> functions from
/// C# <c>async</c>/<c>await</c>. Each call allocates one
/// <see cref="SuspendContinuation"/>, hands it to the Kotlin function,
/// and either:
/// <list type="bullet">
/// <item><description>completes synchronously when Kotlin returns
/// anything other than the <c>COROUTINE_SUSPENDED</c> sentinel, or</description></item>
/// <item><description>defers to the continuation when Kotlin returns
/// the sentinel (i.e. it will resume the continuation later from its
/// own dispatcher).</description></item>
/// </list>
/// In both cases the boxed <c>kotlin.Result&lt;T&gt;</c> is funneled
/// through <see cref="SuspendContinuation.ResumeWith"/> so failure
/// detection and unboxing live in one place.
/// </summary>
/// <remarks>
/// <para>
/// The returned <see cref="Task"/> may complete on the Kotlin resume
/// thread (usually the Compose main thread for UI-bound suspend
/// functions like <c>ScrollState.scrollTo</c>). Awaiters use
/// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> on
/// the underlying TCS, so they won't be inlined onto that thread —
/// they'll resume on whatever <see cref="SynchronizationContext"/> the
/// <c>await</c> captured.
/// </para>
/// <para>
/// Cancellation: an optional <see cref="CancellationToken"/> cancels
/// the returned <see cref="Task"/> (transitioning it to
/// <see cref="TaskStatus.Canceled"/>, so the <c>await</c> throws
/// <see cref="OperationCanceledException"/>) as soon as the
/// token fires. The Kotlin suspend body keeps running to its natural
/// completion — we don't yet plumb a <c>Job</c> into
/// <see cref="SuspendContinuation.Context"/>, so there is no
/// Kotlin-side cancel. The boxed result of the eventual resume is
/// disposed silently. True Kotlin-side cancel (calling
/// <c>job.cancel()</c> so animation/scroll bodies stop at the next
/// suspend point) is tracked as a follow-up.
/// </para>
/// </remarks>
internal static class SuspendBridge
{
    // Cached global ref to the COROUTINE_SUSPENDED singleton. Kotlin
    // identifies "I'll resume you later" by *reference equality* with
    // this exact object, so we cache one global ref and compare via
    // JNIEnv.IsSameObject (raw pointer compares aren't safe across
    // JNI ref types).
    static IntPtr s_suspendedHandle;

    /// <summary>
    /// Call a Kotlin <c>suspend</c> function and project its boxed
    /// result through <paramref name="unbox"/> into a typed
    /// <see cref="Task{TResult}"/>.
    /// </summary>
    /// <param name="call">
    /// Invokes the Kotlin <c>suspend</c> function with the supplied
    /// <see cref="SuspendContinuation"/> as the trailing argument and
    /// returns either the boxed result (synchronous completion) or
    /// the <c>COROUTINE_SUSPENDED</c> sentinel.
    /// </param>
    /// <param name="unbox">
    /// Converts the success-value box (e.g. <c>java.lang.Float</c>) to
    /// the target C# type. May be passed <c>null</c> for suspend
    /// functions that return <c>Unit</c>. The supplied box is owned
    /// by <see cref="Invoke{T}"/> and disposed after this delegate
    /// returns; do not capture or return the box itself.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the returned task. See the type-level remarks for the
    /// (current) semantics: only the C# awaiter sees the cancel; the
    /// Kotlin suspend body keeps running.
    /// </param>
    public static Task<T> Invoke<T>(
        Func<SuspendContinuation, IntPtr> call,
        Func<Java.Lang.Object?, T> unbox,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(unbox);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<T>(cancellationToken);

        var cont = new SuspendContinuation(cancellationToken);

        // The ctor's Register call invokes the cancellation callback
        // synchronously if the token fires between the pre-check above
        // and registration. Re-check before invoking Kotlin so we don't
        // start a (visible) scroll/animation we'd then have to wait on.
        if (cancellationToken.IsCancellationRequested)
        {
            cont.Dispose();
            return Task.FromCanceled<T>(cancellationToken);
        }

        IntPtr syncHandle;
        try
        {
            syncHandle = call(cont);
        }
        catch (Exception ex)
        {
            // Exceptions thrown before the suspend function suspends
            // (e.g. JNI lookup failures, Kotlin throwing inline before
            // creating a Result.Failure) shouldn't surface as
            // synchronous throws from an async-style API.
            cont.Dispose();
            return Task.FromException<T>(ex);
        }

        // We work in raw JNI handles here rather than wrapping syncHandle
        // in a Java.Lang.Object up-front. The COROUTINE_SUSPENDED
        // sentinel is a Kotlin singleton, and Mono's peer cache resolves
        // every local ref to it back to a single cached Java.Lang.Object
        // whose Handle is a *global* ref. If we wrap with TransferLocalRef
        // and later dispose, the dispose path calls DeleteLocalRef on a
        // global, which CheckJNI aborts. Raw-handle work-flow side-steps
        // the issue entirely.
        if (syncHandle != IntPtr.Zero)
        {
            if (IsCoroutineSuspended(syncHandle))
            {
                // Kotlin will resume cont later. Free the local ref to
                // the sentinel; the cached global lives in
                // s_suspendedHandle for future comparisons. Leave the
                // continuation alive — ResumeWith disposes it when
                // Kotlin actually resumes.
                JNIEnv.DeleteLocalRef(syncHandle);
            }
            else
            {
                // Synchronous completion — funnel through the same
                // promote-to-global + TCS path as the Kotlin-async
                // callback (SuspendContinuation.ResumeWith), then
                // dispose ourselves since Kotlin will never call
                // ResumeWith on this path.
                cont.CompleteWithLocalHandle(syncHandle);
                cont.Dispose();
            }
        }
        else
        {
            // Suspend returned a Java null. Treat as a successful null
            // result (rare in practice; most suspends either suspend or
            // return a boxed Unit / boxed primitive).
            cont.CompleteWithLocalHandle(IntPtr.Zero);
            cont.Dispose();
        }

        return AwaitAndUnbox(cont, unbox);
    }

    /// <summary>
    /// Non-generic overload for suspend functions that return Kotlin
    /// <c>Unit</c> (e.g. <c>ScrollState.animateScrollTo</c>).
    /// </summary>
    public static Task Invoke(
        Func<SuspendContinuation, IntPtr> call,
        CancellationToken cancellationToken = default) =>
        Invoke<object?>(call, static _ => null, cancellationToken);

    // Awaits the TCS, runs the failure-vs-success split, and disposes
    // the boxed result. Lives in its own async method so a cancelled
    // TCS surfaces as a Canceled (not Faulted) Task — the async state
    // machine routes an uncaught OperationCanceledException straight
    // into AsyncTaskMethodBuilder.SetCanceled.
    static async Task<T> AwaitAndUnbox<T>(SuspendContinuation cont, Func<Java.Lang.Object?, T> unbox)
    {
        Java.Lang.Object? boxed;
        try
        {
            boxed = await cont.Tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            // Belt-and-suspenders: the state machine already roots cont
            // across the await, but this also covers the synchronous-
            // completion path where Dispose already ran (the
            // pin is gone, so only the state-machine field keeps cont
            // alive while we touch its TCS).
            GC.KeepAlive(cont);
        }

        try
        {
            if (boxed is not null && KotlinResult.IsFailure(boxed.Handle))
                throw KotlinResult.ExtractException(boxed);
            return unbox(boxed);
        }
        finally
        {
            boxed?.Dispose();
        }
    }

    internal static bool IsCoroutineSuspended(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;
        EnsureSuspendedHandle();
        return JNIEnv.IsSameObject(handle, s_suspendedHandle);
    }

    internal static void EnsureSuspendedHandle()
    {
        if (s_suspendedHandle != IntPtr.Zero) return;
        var inst = IntrinsicsKt.COROUTINE_SUSPENDED;
        var gref = JNIEnv.NewGlobalRef(inst.Handle);
        // CAS so concurrent first-callers don't leak a global ref.
        if (Interlocked.CompareExchange(ref s_suspendedHandle, gref, IntPtr.Zero) != IntPtr.Zero)
            JNIEnv.DeleteGlobalRef(gref);
        GC.KeepAlive(inst);
    }
}
