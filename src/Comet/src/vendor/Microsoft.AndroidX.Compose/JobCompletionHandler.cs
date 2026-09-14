using Android.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// JCW for <c>Function1&lt;Throwable?, Unit&gt;</c> registered via
/// <see cref="Xamarin.KotlinX.Coroutines.IJob.InvokeOnCompletion(bool, bool, IFunction1)"/>.
/// Compose invokes this handler when the launched coroutine's job
/// transitions into the cancelling state, so we can propagate
/// cancellation into the C# <see cref="CancellationTokenSource"/>
/// that the user's <c>Func&lt;CancellationToken, Task&gt;</c> body
/// observes.
/// </summary>
[Register("net/compose/JobCompletionHandler")]
internal sealed class JobCompletionHandler : Java.Lang.Object, IFunction1
{
    readonly CancellationTokenSource _cts;

    public JobCompletionHandler(CancellationTokenSource cts)
    {
        _cts = cts;
    }

    public Java.Lang.Object Invoke(Java.Lang.Object? p0)
    {
        // p0 is the Throwable cause (or null for normal completion).
        // Either way we cancel the CTS — the user's Task body sees
        // ct.IsCancellationRequested and exits cooperatively.
        try
        {
            _cts.Cancel();
        }
        catch
        {
            // Cancel is best-effort. A racing Dispose on the CTS or
            // an exception raised by a registered callback should
            // never propagate out into the JVM (Kotlin would crash
            // the process with an UnhandledException). Swallow.
        }
        return Kotlin.Unit.Instance!;
    }
}
