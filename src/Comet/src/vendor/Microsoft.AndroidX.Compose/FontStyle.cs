using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// C# wrapper around <c>androidx.compose.ui.text.font.FontStyle</c>.
/// In Kotlin source, <c>FontStyle</c> is a <c>@JvmInline value class</c>
/// wrapping an <c>Int</c>, but every <c>@Composable</c> function in
/// Material 3 declares it as <em>nullable</em> (<c>fontStyle: FontStyle?
/// = null</c>) — so at the JNI boundary it travels as a boxed
/// <c>androidx/compose/ui/text/font/FontStyle;</c> reference, not a
/// packed <c>int</c>. The bridge generator's reference-type path
/// passes the handle through to the JNI <c>L</c> slot.
///
/// Same trick as <see cref="TextAlign"/>: call the mangled
/// <c>Companion.getNormal-_-LCdwA()I</c> for the packed int, then
/// route through static <c>FontStyle.box-impl(I)LFontStyle;</c>. Both
/// halves are emitted by <c>ComposeCompanionGenerator</c> from the
/// <c>InlineClass = true</c> on the attribute below.
///
/// Will swap to bound <c>AndroidX.Compose.UI.Text.Font.FontStyle</c>
/// once <see href="https://github.com/dotnet/android-libraries/pull/1440"/>
/// ships.
/// </summary>
[ComposeCompanion("androidx/compose/ui/text/font/FontStyle", InlineClass = true)]
public sealed partial class FontStyle : Java.Lang.Object
{
    FontStyle(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) { }

    /// <summary>Upright glyphs (the default).</summary>
    [ComposeCompanionGetter("getNormal-_-LCdwA")]
    public static partial FontStyle Normal { get; }

    /// <summary>Italic / slanted glyphs.</summary>
    [ComposeCompanionGetter("getItalic-_-LCdwA")]
    public static partial FontStyle Italic { get; }
}
