using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Raw-JNI helpers for Kotlin's <c>kotlin.Result</c> type — the boxed
/// success/failure wrapper Kotlin coroutines pass through
/// <c>Continuation.resumeWith</c>.
/// </summary>
/// <remarks>
/// <para>
/// Both ends of the bridge live here:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="CreateFailure(Java.Lang.Throwable)"/>
/// — call <c>kotlin.ResultKt.createFailure(Throwable)</c> to wrap a C#
/// exception in a <c>Result.Failure</c> for
/// <c>Continuation.resumeWith</c> (used by
/// <see cref="LaunchedEffectBody"/> and any other C#→Kotlin suspend
/// resume site).</description></item>
/// <item><description><see cref="IsFailure(IntPtr)"/> +
/// <see cref="ExtractException(Java.Lang.Object)"/> — peek at a
/// resumed <c>Result</c> handle to detect a failure box and extract
/// the underlying <c>Throwable</c> for the awaiting C# task (used by
/// <see cref="SuspendBridge"/>).</description></item>
/// </list>
/// <para>
/// Why raw JNI: <c>kotlin.Result</c> is a <c>@JvmInline value class</c>
/// wrapping <c>Object</c>, so every accessor on it (and every helper
/// on <c>ResultKt</c>) emits a mangled JVM name (<c>Result-impl</c>,
/// <c>createFailure-impl</c>, <c>isFailure-impl</c>,
/// <c>exceptionOrNull-impl</c>) that the binder strips — same root
/// cause as
/// <see href="https://github.com/dotnet/java-interop/pull/1440">dotnet/java-interop#1440</see>.
/// The nested <c>Result$Failure</c> class is intentionally
/// <c>internal</c> in Kotlin source and isn't surfaced by the binder
/// regardless. When #1440 lands and the binder restores
/// <c>ResultKt.createFailure(Throwable)</c> / <c>throwOnFailure(Object)</c>
/// / etc., the bodies of these helpers can be replaced with calls to
/// the bound members and the cached field/method ids deleted.
/// </para>
/// <para>
/// Caching strategy: <c>JNIEnv.FindClass</c> /
/// <c>JNIEnv.GetStaticMethodID</c> / <c>JNIEnv.GetFieldID</c>
/// are pure functions of their string args, and Mono.Android's
/// <c>FindClass</c> returns a stable, globally registered class ref
/// — no <c>NewGlobalRef</c>/<c>DeleteLocalRef</c> dance. Multi-thread
/// racers all observe the same ids; losing the race just re-writes
/// identical values, so plain stores are fine.
/// </para>
/// </remarks>
internal static class KotlinResult
{
    // kotlin/ResultKt — host of the static `createFailure(Throwable): Object` helper.
    static IntPtr s_resultKtClass;
    static IntPtr s_resultKtCreateFailureMethod;

    // kotlin/Result$Failure — the boxed-failure type Kotlin uses to
    // distinguish failure resumes from success resumes. Its `exception`
    // field carries the underlying Throwable.
    static IntPtr s_resultFailureClass;
    static IntPtr s_resultFailureExceptionField;

    /// <summary>
    /// Construct a <c>kotlin.Result.Failure(throwable)</c> instance —
    /// the boxed-failure form a <see cref="Kotlin.Coroutines.IContinuation"/>
    /// expects when something goes wrong inside its suspend body.
    /// </summary>
    /// <param name="throwable">
    /// The Java throwable to wrap; non-null. C# exceptions can be
    /// converted via <see cref="JavaProxyThrowable"/>
    /// (or any helper that produces a <see cref="Java.Lang.Throwable"/>).
    /// </param>
    /// <returns>
    /// A managed peer wrapping the Kotlin failure object. The caller
    /// passes this directly to <c>continuation.ResumeWith(result)</c>.
    /// </returns>
    internal static unsafe Java.Lang.Object CreateFailure(Java.Lang.Throwable throwable)
    {
        ArgumentNullException.ThrowIfNull(throwable);

        EnsureResultKt();

        try
        {
            JValue* args = stackalloc JValue[1];
            args[0] = new JValue(throwable);
            var handle = JNIEnv.CallStaticObjectMethod(
                s_resultKtClass, s_resultKtCreateFailureMethod, args);
            // Transfer the local ref into a managed peer; the caller
            // hands the peer to Continuation.ResumeWith which keeps it
            // alive until the coroutine actually resumes.
            return Java.Lang.Object.GetObject<Java.Lang.Object>(
                handle, JniHandleOwnership.TransferLocalRef)!;
        }
        finally
        {
            GC.KeepAlive(throwable);
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="handle"/>
    /// refers to a <c>kotlin.Result.Failure</c> instance — i.e. the
    /// resumed value represents a failure rather than a success.
    /// </summary>
    /// <param name="handle">
    /// Raw JNI handle to test. <see cref="IntPtr.Zero"/> is treated as
    /// "not a failure" (a null resume value is a successful null).
    /// </param>
    internal static bool IsFailure(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;
        EnsureResultFailure();
        return JNIEnv.IsInstanceOf(handle, s_resultFailureClass);
    }

    /// <summary>
    /// Extract the underlying <see cref="Java.Lang.Throwable"/> from a
    /// <c>kotlin.Result.Failure</c> peer.
    /// </summary>
    /// <param name="failure">
    /// A peer wrapping a <c>Result.Failure</c> handle. Must have been
    /// vetted with <see cref="IsFailure(IntPtr)"/> first.
    /// </param>
    /// <returns>
    /// The wrapped throwable as a <see cref="Exception"/>
    /// (<see cref="Java.Lang.Throwable"/> derives from
    /// <see cref="Exception"/> on Mono.Android, so callers can
    /// <c>catch (Exception)</c> or pattern-match on the Java type).
    /// </returns>
    internal static Exception ExtractException(Java.Lang.Object failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        // s_resultFailureExceptionField is guaranteed populated:
        // IsFailure is the only sanctioned predicate, and it calls
        // EnsureResultFailure before this method ever runs.
        IntPtr exHandle = JNIEnv.GetObjectField(failure.Handle, s_resultFailureExceptionField);
        try
        {
            var th = Java.Lang.Object.GetObject<Java.Lang.Throwable>(
                exHandle, JniHandleOwnership.TransferLocalRef);
            if (th is not null)
                return th;
            return new InvalidOperationException(
                "Kotlin suspend call failed with a null Throwable in Result.Failure");
        }
        finally
        {
            GC.KeepAlive(failure);
        }
    }

    static void EnsureResultKt()
    {
        if (s_resultKtCreateFailureMethod != IntPtr.Zero) return;
        s_resultKtClass = JNIEnv.FindClass("kotlin/ResultKt");
        s_resultKtCreateFailureMethod = JNIEnv.GetStaticMethodID(
            s_resultKtClass,
            "createFailure",
            "(Ljava/lang/Throwable;)Ljava/lang/Object;");
    }

    static void EnsureResultFailure()
    {
        if (s_resultFailureClass != IntPtr.Zero) return;
        var cls = JNIEnv.FindClass("kotlin/Result$Failure");
        s_resultFailureExceptionField = JNIEnv.GetFieldID(cls, "exception", "Ljava/lang/Throwable;");
        s_resultFailureClass = cls;
    }
}
