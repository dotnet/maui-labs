using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// C# wrapper around <c>androidx.compose.ui.text.AnnotatedString</c> —
/// Compose's rich-text type. Holds a plain string body plus a list of
/// <see cref="SpanStyle"/> ranges and link annotations. Build instances
/// via <see cref="AnnotatedStringBuilder"/>; pass them to
/// <see cref="AnnotatedText"/> to render.
/// </summary>
/// <remarks>
/// This is a thin <see cref="Java.Lang.Object"/> shell over the bound
/// <see cref="AndroidX.Compose.UI.Text.AnnotatedString"/>. We don't
/// expose the binding type directly so the public surface stays in the
/// <c>AndroidX.Compose</c> namespace alongside <see cref="SpanStyle"/> and
/// <see cref="LinkAnnotation"/>, and so future generator-emitted
/// bridges can accept it via the standard reference-type handle path.
/// </remarks>
public sealed class AnnotatedString : Java.Lang.Object
{
    AnnotatedString(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) { }

    internal AnnotatedString(AndroidX.Compose.UI.Text.AnnotatedString binding)
        : base(binding.Handle, JniHandleOwnership.DoNotTransfer)
    {
        GC.KeepAlive(binding);
    }

    /// <summary>
    /// Construct an <see cref="AnnotatedString"/> with no styling — a
    /// plain string. Equivalent to <c>AnnotatedString(text)</c> in
    /// Kotlin.
    /// </summary>
    public AnnotatedString(string text)
        : this(new AndroidX.Compose.UI.Text.AnnotatedString(
            text, new List<AndroidX.Compose.UI.Text.AnnotatedString.Range>()))
    {
    }

    /// <summary>The raw character content of this string, ignoring spans.</summary>
    public string Text => Binding().Text;

    /// <summary>Number of characters in the string.</summary>
    public int Length => Binding().Length;

    AndroidX.Compose.UI.Text.AnnotatedString Binding() =>
        Java.Lang.Object.GetObject<AndroidX.Compose.UI.Text.AnnotatedString>(Handle, JniHandleOwnership.DoNotTransfer)!;
}
