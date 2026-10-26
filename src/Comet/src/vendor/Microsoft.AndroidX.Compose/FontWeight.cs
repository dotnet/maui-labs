using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// C# wrapper around <c>androidx.compose.ui.text.font.FontWeight</c>.
/// Compose's <c>FontWeight</c> is a real Kotlin class (not a value
/// class), but Compose 1.11.1's <c>ui-text-android</c> package is
/// shipped as a Java-library-only stub with zero exported types — so
/// the .NET binding doesn't expose it yet. We subclass
/// <see cref="Java.Lang.Object"/> directly and resolve the Kotlin
/// <c>Companion</c> instances via JNI (the boilerplate is emitted by
/// <c>ComposeCompanionGenerator</c> from the
/// <see cref="ComposeCompanionAttribute"/> below); the bridge
/// generator's reference-type code path
/// (<c>x is null ? IntPtr.Zero : x.Handle</c>) then passes the handle
/// through to the JNI <c>L</c> slot.
///
/// Will swap to bound <c>AndroidX.Compose.UI.Text.Font.FontWeight</c>
/// once <see href="https://github.com/dotnet/android-libraries/issues/1439"/>
/// ships and we adopt <c>Xamarin.AndroidX.Compose.UI.Text.Android</c>
/// 1.11.2.1+.
/// </summary>
[ComposeCompanion("androidx/compose/ui/text/font/FontWeight")]
public sealed partial class FontWeight : Java.Lang.Object
{
    FontWeight(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) { }

    /// <summary><c>FontWeight.Thin</c> (W100).</summary>
    [ComposeCompanionGetter("getThin")]
    public static partial FontWeight Thin { get; }

    /// <summary><c>FontWeight.ExtraLight</c> (W200).</summary>
    [ComposeCompanionGetter("getExtraLight")]
    public static partial FontWeight ExtraLight { get; }

    /// <summary><c>FontWeight.Light</c> (W300).</summary>
    [ComposeCompanionGetter("getLight")]
    public static partial FontWeight Light { get; }

    /// <summary><c>FontWeight.Normal</c> (W400). The default.</summary>
    [ComposeCompanionGetter("getNormal")]
    public static partial FontWeight Normal { get; }

    /// <summary><c>FontWeight.Medium</c> (W500).</summary>
    [ComposeCompanionGetter("getMedium")]
    public static partial FontWeight Medium { get; }

    /// <summary><c>FontWeight.SemiBold</c> (W600).</summary>
    [ComposeCompanionGetter("getSemiBold")]
    public static partial FontWeight SemiBold { get; }

    /// <summary><c>FontWeight.Bold</c> (W700).</summary>
    [ComposeCompanionGetter("getBold")]
    public static partial FontWeight Bold { get; }

    /// <summary><c>FontWeight.ExtraBold</c> (W800).</summary>
    [ComposeCompanionGetter("getExtraBold")]
    public static partial FontWeight ExtraBold { get; }

    /// <summary><c>FontWeight.Black</c> (W900).</summary>
    [ComposeCompanionGetter("getBlack")]
    public static partial FontWeight Black { get; }
}
