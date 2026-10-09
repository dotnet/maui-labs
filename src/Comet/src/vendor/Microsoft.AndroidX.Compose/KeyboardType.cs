using Android.Runtime;
using BoundCompanion = AndroidX.Compose.UI.Text.Input.KeyboardType.Companion;

namespace AndroidX.Compose;

/// <summary>
/// C# accessor for Compose's IME-type enumeration —
/// <c>androidx.compose.ui.text.input.KeyboardType</c>. Surfaces every
/// constant on the Kotlin <c>KeyboardType.Companion</c>
/// (<c>Text</c>, <c>Number</c>, <c>Phone</c>, <c>Uri</c>, <c>Email</c>,
/// <c>Password</c>, etc.) as a named <c>int</c> property so call sites
/// can write
/// <c>field.KeyboardOptions = d.Copy(..., KeyboardType.Number, ...)</c>
/// instead of hand-coded magic numbers.
///
/// In Kotlin source <c>KeyboardType</c> is a
/// <c>@JvmInline value class</c> wrapping an <c>Int</c>. Every bound
/// API that consumes one (e.g. <c>KeyboardOptions.Copy(int
/// keyboardType, …)</c>) takes a raw <c>int</c> at the JNI boundary,
/// so this type intentionally exposes the raw int directly — no
/// <c>box-impl</c> indirection required.
///
/// Internals: <see cref="AndroidX.Compose.UI.Text.Input.KeyboardType"/>
/// <em>and</em> its nested <see cref="BoundCompanion"/> are fully bound
/// (every <c>Number_PjHm6EE</c>-style getter surfaces as an <c>int</c>
/// property on the Companion peer). The only thing missing is a
/// <c>static Companion Companion { get; }</c> accessor on the outer
/// class — Mono's binder skips it for Kotlin <c>object</c> companions
/// (<see href="https://github.com/dotnet/android-libraries/issues/1467"/>).
/// So we bootstrap the singleton handle exactly once via JNI
/// (<c>GetStaticObjectField</c>), wrap it as the bound Companion peer,
/// and every constant accessor below routes through that peer's
/// generated <c>InvokeNonvirtualInt32Method</c>.
/// </summary>
public static class KeyboardType
{
    static BoundCompanion? s_companion;

    static BoundCompanion Companion()
    {
        if (s_companion is not null) return s_companion;
        IntPtr local = IntPtr.Zero;
        try
        {
            IntPtr cls = JNIEnv.FindClass("androidx/compose/ui/text/input/KeyboardType");
            IntPtr fid = JNIEnv.GetStaticFieldID(cls, "Companion",
                "Landroidx/compose/ui/text/input/KeyboardType$Companion;");
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

    /// <summary>The unspecified-keyboard-type sentinel (Kotlin's default).</summary>
    public static int Unspecified { get; } = Companion().Unspecified_PjHm6EE;

    /// <summary>Plain text keyboard — the default IME on most fields.</summary>
    public static int Text { get; } = Companion().Text_PjHm6EE;

    /// <summary>ASCII-only keyboard.</summary>
    public static int Ascii { get; } = Companion().Ascii_PjHm6EE;

    /// <summary>Numeric-only keyboard (digits, minus, decimal).</summary>
    public static int Number { get; } = Companion().Number_PjHm6EE;

    /// <summary>Telephone-number keyboard (digits + <c>+ * #</c>).</summary>
    public static int Phone { get; } = Companion().Phone_PjHm6EE;

    /// <summary>URI-entry keyboard (<c>/ . :</c>).</summary>
    public static int Uri { get; } = Companion().Uri_PjHm6EE;

    /// <summary>Email-address keyboard (<c>@ .</c>).</summary>
    public static int Email { get; } = Companion().Email_PjHm6EE;

    /// <summary>Password keyboard — masks input, suppresses suggestions.</summary>
    public static int Password { get; } = Companion().Password_PjHm6EE;

    /// <summary>Numeric password keyboard — digit-only password (e.g. PINs).</summary>
    public static int NumberPassword { get; } = Companion().NumberPassword_PjHm6EE;

    /// <summary>Decimal-number keyboard (digits + decimal separator).</summary>
    public static int Decimal { get; } = Companion().Decimal_PjHm6EE;
}
