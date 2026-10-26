
namespace AndroidX.Compose;

/// <summary>
/// Base class for AndroidX.Compose view models. Derives from
/// <see cref="AndroidX.Lifecycle.ViewModel"/> so the host's
/// <see cref="AndroidX.Lifecycle.ViewModelStore"/> owns the
/// instance — the C# equivalent of subclassing
/// <c>androidx.lifecycle.ViewModel</c> in Kotlin.
/// </summary>
/// <remarks>
/// <para>
/// Allocate one instance per call site via
/// <see cref="ComposeExtensions.ViewModel{T}(Func{T}, int, string)"/>.
/// The factory routes through
/// <see cref="AndroidX.Lifecycle.ViewModelProvider"/> on the
/// nearest <see cref="AndroidX.Lifecycle.IViewModelStoreOwner"/>
/// (the host <see cref="AndroidX.Activity.ComponentActivity"/> at
/// the root, or the current
/// <see cref="AndroidX.Navigation.NavBackStackEntry"/> inside a
/// <see cref="NavHost"/>), so the view model survives recomposition
/// <em>and</em> configuration change — and is cleared exactly when
/// the owner clears (the activity is finished, or the destination
/// is popped off the back stack).
/// </para>
/// <para>
/// <strong>OnCleared template:</strong> the framework
/// <see cref="AndroidX.Lifecycle.ViewModel.OnCleared"/> is
/// <c>sealed override</c>'d here so cancellation and CTS disposal
/// always happen, even if a subclass forgets to call
/// <c>base.OnCleared()</c>. Subclasses override
/// <see cref="OnClearedCore"/> for their own cleanup; it runs
/// after the <see cref="Scope"/> token has been cancelled and
/// before the backing <see cref="CancellationTokenSource"/> is
/// disposed.
/// </para>
/// <para>
/// <strong>Threading:</strong> the view model is constructed on
/// whatever thread invoked the composition that first observed it
/// — typically the Android main thread. Subclasses should treat
/// instance fields as main-thread state and post mutations from
/// background work back through <see cref="LaunchAsync"/>, whose
/// continuations honor the captured
/// <see cref="SynchronizationContext"/> when awaiting.
/// </para>
/// <para>
/// <strong>Manual disposal is unsupported.</strong> The class
/// inherits <see cref="Java.Lang.Object.Dispose()"/> from its
/// Java peer base, but that disposes the JNI reference — it does
/// <em>not</em> remove the entry from the
/// <see cref="AndroidX.Lifecycle.ViewModelStore"/>. Treat the
/// store owner as the sole authority over view model lifetime
/// and never call <c>Dispose</c> directly.
/// </para>
/// </remarks>
public abstract class ViewModel : AndroidX.Lifecycle.ViewModel
{
    readonly CancellationTokenSource _cts = new();
    int _cleared;

    /// <summary>
    /// A <see cref="CancellationToken"/> that fires when the
    /// host clears this view model — i.e. the framework
    /// <see cref="AndroidX.Lifecycle.ViewModel.OnCleared"/>
    /// callback. The C# equivalent of Kotlin's
    /// <c>viewModelScope.coroutineContext[Job]</c>: pass it to
    /// any long-running task / HTTP call / channel read that
    /// should stop when the view model goes away.
    /// </summary>
    public CancellationToken Scope => _cts.Token;

    /// <summary>
    /// Returns <c>true</c> once the framework has cleared this
    /// view model. Useful for guarding callbacks that race the
    /// teardown.
    /// </summary>
    public bool IsCleared => Volatile.Read(ref _cleared) != 0;

    /// <summary>
    /// Launches <paramref name="body"/> tied to <see cref="Scope"/>.
    /// The C# equivalent of <c>viewModelScope.launch { … }</c> —
    /// any exception other than <see cref="OperationCanceledException"/>
    /// is logged via <see cref="Android.Util.Log.Error(string?, string?)"/>
    /// under the <c>AndroidX.Compose</c> tag rather than propagating as
    /// an unobserved task exception. The returned <see cref="Task"/>
    /// is the same task that surface-area callers can <c>await</c>
    /// (e.g. to chain a snackbar dispatch after a refresh
    /// completes).
    /// </summary>
    /// <param name="body">
    /// Async body. Receives <see cref="Scope"/> directly so
    /// downstream <c>await Task.Delay(…, ct)</c> /
    /// <c>repository.GetAsync(ct)</c> calls cancel cleanly.
    /// </param>
    /// <remarks>
    /// <para>
    /// The body is invoked synchronously on the calling thread up
    /// to the first <c>await</c> — that's the same behaviour
    /// Kotlin's <c>launch</c> exhibits when the dispatcher is the
    /// current one. To force a yield before the body starts, begin
    /// with <c>await Task.Yield();</c>.
    /// </para>
    /// <para>
    /// When the view model is already cleared at the time of the
    /// call, this returns <see cref="Task.CompletedTask"/> without
    /// invoking <paramref name="body"/>.
    /// </para>
    /// </remarks>
    public Task LaunchAsync(Func<CancellationToken, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (IsCleared)
            return Task.CompletedTask;

        // Capture the token snapshot BEFORE invoking the body. Once
        // the framework calls OnCleared we cancel and dispose the
        // CTS, so any task that resumes after that point can no
        // longer read `Scope` (it would throw ObjectDisposedException).
        // A captured token stays valid for IsCancellationRequested
        // reads even after the source is disposed.
        var token = Scope;
        return Wrap(body, token);

        static async Task Wrap(Func<CancellationToken, Task> b, CancellationToken token)
        {
            try
            {
                await b(token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Normal teardown — swallow.
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error("AndroidX.Compose",
                    $"ViewModel LaunchAsync body faulted: {ex}");
            }
        }
    }

    /// <summary>
    /// Override hook for view-model cleanup. Invoked exactly once,
    /// after <see cref="Scope"/> has been cancelled and before the
    /// backing <see cref="CancellationTokenSource"/> is disposed.
    /// Use it to release non-task resources (file handles, sockets,
    /// native peers, <see cref="IDisposable"/> subscriptions, etc.).
    /// </summary>
    /// <remarks>
    /// Throwing from this method logs the exception and continues
    /// teardown. The framework guarantees this method fires at most
    /// once per instance, so subclasses don't need their own
    /// idempotency guards.
    /// </remarks>
    protected virtual void OnClearedCore() { }

    /// <summary>
    /// Sealed override of
    /// <see cref="AndroidX.Lifecycle.ViewModel.OnCleared"/> — runs
    /// the framework cancellation, then dispatches to
    /// <see cref="OnClearedCore"/>, then disposes the backing
    /// <see cref="CancellationTokenSource"/>. Sealed so subclass
    /// teardown can't accidentally skip the cancellation step by
    /// forgetting <c>base.OnCleared()</c>.
    /// </summary>
    protected sealed override void OnCleared()
    {
        if (Interlocked.Exchange(ref _cleared, 1) != 0)
            return;

        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { /* already disposed */ }

        try { OnClearedCore(); }
        catch (Exception ex)
        {
            Android.Util.Log.Error("AndroidX.Compose",
                $"ViewModel ({GetType().Name}) OnClearedCore threw: {ex}");
        }

        _cts.Dispose();
        base.OnCleared();
    }
}
