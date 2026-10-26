namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// Guarantees that a dialog's <see cref="DialogNativeRegistrationScope"/> is disposed exactly
/// once when the dialog's presentation completes - whether that's <see cref="Complete"/>
/// running a result callback after an AppKit sheet is dismissed, or <see cref="Fail"/>
/// reporting a synchronous setup failure before any sheet was ever shown - and that no
/// exception can ever escape back across the native AppKit boundary that invoked completion:
/// whether the caller's result callback succeeds, throws (falling back to
/// <c>onUnhandledException</c>), that fallback itself throws, or (defensively, since it should
/// never happen) AppKit invokes the sheet completion handler more than once. This matters
/// because <c>NSAlert.BeginSheetForResponse</c>'s completion handler runs later, from native
/// code: an unhandled managed exception there would cross the native boundary instead of
/// surfacing as a normal .NET exception, and would leave the corresponding MAUI
/// AlertArguments/PromptArguments/ActionSheetArguments task never completed - so callers of
/// <see cref="Complete"/> and <see cref="Fail"/> must always supply a fallback that sets a safe
/// default result. Contains no AppKit or MAUI dependencies so it can be exercised by plain unit
/// tests.
/// </summary>
internal sealed class DialogCompletionScope
{
    readonly IDisposable _registrationScope;
    int _completed;

    public DialogCompletionScope(IDisposable registrationScope)
    {
        ArgumentNullException.ThrowIfNull(registrationScope);
        _registrationScope = registrationScope;
    }

    /// <summary>True once <see cref="Complete"/> or <see cref="Fail"/> has run. Exposed for testing.</summary>
    public bool IsCompleted => _completed != 0;

    /// <summary>
    /// Runs <paramref name="onCompleted"/> and then disposes the tracked registration scope,
    /// exactly once, guaranteeing the scope is disposed on every path. If
    /// <paramref name="onCompleted"/> throws, <paramref name="onUnhandledException"/> runs
    /// instead - typically to log the failure and set a safe fallback result - and if that
    /// fallback itself throws, the failure is swallowed (after the registration scope is still
    /// disposed) rather than ever escaping this method. Any call after the first (whether to
    /// <see cref="Complete"/> or <see cref="Fail"/>) is ignored, so a duplicate or re-entrant
    /// completion callback can never double-dispose the registration scope or run the result
    /// callback (or its fallback) more than once.
    /// </summary>
    public void Complete(Action onCompleted, Action<Exception> onUnhandledException)
    {
        ArgumentNullException.ThrowIfNull(onCompleted);
        ArgumentNullException.ThrowIfNull(onUnhandledException);

        Finish(onCompleted, onUnhandledException);
    }

    /// <summary>
    /// Reports a failure that happened before any result could even be attempted - e.g.
    /// dialog/button/input registration or window resolution failing synchronously, before an
    /// AppKit sheet was ever shown - by unconditionally running <paramref name="onUnhandledException"/>
    /// (instead of only running it when a supplied result callback happens to throw) and then
    /// disposing the tracked registration scope, exactly once. Like <see cref="Complete"/>, if
    /// <paramref name="onUnhandledException"/> itself throws, the failure is swallowed after the
    /// registration scope is still disposed, so this method never lets an exception escape.
    /// Callers that also need <paramref name="exception"/> to propagate to their own caller
    /// (e.g. to preserve prior synchronous-failure behavior) should still rethrow it themselves
    /// after calling this method; doing so is safe here because such a rethrow only crosses
    /// managed synchronous callers - it never crosses the native AppKit completion boundary,
    /// since presentation never reached that point.
    /// </summary>
    public void Fail(Exception exception, Action<Exception> onUnhandledException)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(onUnhandledException);

        // The "already fell back" no-op second argument mirrors Complete's structure (an
        // onCompleted that always needs the fallback, whose own failure is then swallowed)
        // without re-throwing/re-catching the original exception.
        Finish(() => onUnhandledException(exception), _ => { });
    }

    void Finish(Action onCompleted, Action<Exception> onUnhandledException)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return;

        try
        {
            try
            {
                onCompleted();
            }
            catch (Exception ex)
            {
                onUnhandledException(ex);
            }
        }
        catch
        {
            // The fallback itself failed. There is nothing more we can safely do here, but
            // this method must never let an exception escape into the native completion
            // callback that invoked it - the registration scope below is still disposed.
        }
        finally
        {
            _registrationScope.Dispose();
        }
    }
}
