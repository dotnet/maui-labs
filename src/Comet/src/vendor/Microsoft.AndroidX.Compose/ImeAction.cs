using Android.Runtime;
using BoundCompanion = AndroidX.Compose.UI.Text.Input.ImeAction.Companion;

namespace AndroidX.Compose;

/// <summary>
/// C# accessor for Compose's soft-keyboard action enumeration —
/// <c>androidx.compose.ui.text.input.ImeAction</c>. Surfaces every
/// constant on the Kotlin <c>ImeAction.Companion</c>
/// (<c>Done</c>, <c>Search</c>, <c>Send</c>, <c>Go</c>, <c>Next</c>,
/// <c>Previous</c>, etc.) as a named <c>int</c> property so call sites
/// can write
/// <c>field.KeyboardOptions = d.Copy(imeAction: ImeAction.Search, …)</c>
/// instead of hand-coded magic numbers.
///
/// In Kotlin source <c>ImeAction</c> is a <c>@JvmInline value
/// class</c> wrapping an <c>Int</c>. Every bound API that consumes one
/// (e.g. <c>KeyboardOptions.Copy(int keyboardType, int imeAction, …)</c>)
/// takes a raw <c>int</c> at the JNI boundary, so this type
/// intentionally exposes the raw int directly — no <c>box-impl</c>
/// indirection required.
///
/// Internals: <see cref="AndroidX.Compose.UI.Text.Input.ImeAction"/>
/// <em>and</em> its nested <see cref="BoundCompanion"/> are fully bound
/// (every <c>Search-eUduSuo</c>-style getter surfaces as an <c>int</c>
/// property on the Companion peer). The only thing missing is a
/// <c>static Companion Companion { get; }</c> accessor on the outer
/// class — Mono's binder skips it for Kotlin <c>object</c> companions
/// on <c>@JvmInline value class</c> outers
/// (<see href="https://github.com/dotnet/android-libraries/issues/1467"/>).
/// So we bootstrap the singleton handle exactly once via JNI
/// (<c>GetStaticObjectField</c>), wrap it as the bound Companion peer,
/// and every constant accessor below routes through that peer's
/// generated <c>InvokeNonvirtualInt32Method</c>. Same pattern as
/// <see cref="KeyboardType"/>.
/// </summary>
public static class ImeAction
{
    static BoundCompanion? s_companion;

    static BoundCompanion Companion()
    {
        if (s_companion is not null) return s_companion;
        IntPtr local = IntPtr.Zero;
        try
        {
            IntPtr cls = JNIEnv.FindClass("androidx/compose/ui/text/input/ImeAction");
            IntPtr fid = JNIEnv.GetStaticFieldID(cls, "Companion",
                "Landroidx/compose/ui/text/input/ImeAction$Companion;");
            local = JNIEnv.GetStaticObjectField(cls, fid);
            return s_companion = Java.Lang.Object.GetObject<BoundCompanion>(
                local, JniHandleOwnership.TransferLocalRef)!;
        }
        finally
        {
            // GetObject(.., TransferLocalRef) consumes `local` on success;
            // the explicit DeleteLocalRef only runs if the wrapper threw
            // before taking ownership.
            if (local != IntPtr.Zero && s_companion is null)
                JNIEnv.DeleteLocalRef(local);
        }
    }

    /// <summary>Unspecified IME action — equivalent to <see cref="Default"/>.</summary>
    public static int Unspecified { get; } = Companion().Unspecified_eUduSuo;

    /// <summary>Platform-default IME action (typically a newline / next-line).</summary>
    public static int Default { get; } = Companion().Default_eUduSuo;

    /// <summary>Suppresses the IME action button entirely.</summary>
    public static int None { get; } = Companion().None_eUduSuo;

    /// <summary>Confirm-and-go action — surfaces a "Go" key on the IME.</summary>
    public static int Go { get; } = Companion().Go_eUduSuo;

    /// <summary>Search action — surfaces a magnifier "Search" key on the IME.</summary>
    public static int Search { get; } = Companion().Search_eUduSuo;

    /// <summary>Send action — surfaces a paper-plane "Send" key on the IME.</summary>
    public static int Send { get; } = Companion().Send_eUduSuo;

    /// <summary>Move-to-previous-field action.</summary>
    public static int Previous { get; } = Companion().Previous_eUduSuo;

    /// <summary>Move-to-next-field action.</summary>
    public static int Next { get; } = Companion().Next_eUduSuo;

    /// <summary>Done / dismiss-keyboard action.</summary>
    public static int Done { get; } = Companion().Done_eUduSuo;
}
