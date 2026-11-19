using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Cached accessor for <c>androidx.compose.ui.text.TextStyle.Companion</c> —
/// the singleton that exposes <c>Default</c> (and via that
/// <c>Default.toSpanStyle()</c>). The C# binding doesn't expose a static
/// accessor for the Companion field, so we read it through JNI once and
/// cache the wrapper for both <see cref="TextStyle"/> and
/// <see cref="SpanStyle"/> to share.
/// </summary>
static class TextStyleCompanion
{
    static AndroidX.Compose.UI.Text.TextStyle.Companion? s_companion;
    static AndroidX.Compose.UI.Text.TextStyle? s_default;
    static AndroidX.Compose.UI.Text.SpanStyle? s_defaultSpan;

    public static AndroidX.Compose.UI.Text.TextStyle.Companion Get()
    {
        if (s_companion is not null) return s_companion;
        IntPtr local = IntPtr.Zero;
        try
        {
            IntPtr cls = JNIEnv.FindClass("androidx/compose/ui/text/TextStyle");
            IntPtr fid = JNIEnv.GetStaticFieldID(cls, "Companion",
                "Landroidx/compose/ui/text/TextStyle$Companion;");
            local = JNIEnv.GetStaticObjectField(cls, fid);
            return s_companion = Java.Lang.Object.GetObject<AndroidX.Compose.UI.Text.TextStyle.Companion>(
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

    public static AndroidX.Compose.UI.Text.TextStyle Default =>
        s_default ??= Get().Default;

    public static AndroidX.Compose.UI.Text.SpanStyle DefaultSpan =>
        s_defaultSpan ??= Default.ToSpanStyle();
}
