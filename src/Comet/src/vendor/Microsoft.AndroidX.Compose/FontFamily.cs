using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// C# wrapper around <c>androidx.compose.ui.text.font.FontFamily</c>.
/// Compose's <c>FontFamily</c> is a real Kotlin class, but Compose
/// 1.11.2.1's <c>ui-text-android</c> package is shipped as a
/// Java-library-only stub with zero exported types — so the .NET
/// binding doesn't expose it yet. We subclass <see cref="Java.Lang.Object"/>
/// directly and resolve the Kotlin <c>Companion</c> instances via JNI
/// (boilerplate emitted by <c>ComposeCompanionGenerator</c>); the bridge
/// generator's reference-type code path
/// (<c>x is null ? IntPtr.Zero : x.Handle</c>) passes the handle through
/// to the JNI <c>L</c> slot.
///
/// Note the per-getter <c>ReturnDescriptor</c> overrides — the Kotlin
/// companion getters return concrete subtypes (<c>SystemFontFamily</c> /
/// <c>GenericFontFamily</c>), not the base interface, so the JNI
/// signatures we look up have to match.
///
/// Will swap to bound <c>AndroidX.Compose.UI.Text.Font.FontFamily</c>
/// once <see href="https://github.com/dotnet/android-libraries/pull/1440"/>
/// ships and we adopt the next <c>Xamarin.AndroidX.Compose.UI.Text.Android</c>
/// release.
/// </summary>
[ComposeCompanion("androidx/compose/ui/text/font/FontFamily")]
public sealed partial class FontFamily : Java.Lang.Object
{
    FontFamily(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) { }

    /// <summary>
    /// <c>FontFamily.Default</c> — the platform's system font family.
    /// </summary>
    [ComposeCompanionGetter("getDefault", ReturnDescriptor = "Landroidx/compose/ui/text/font/SystemFontFamily;")]
    public static partial FontFamily Default { get; }

    /// <summary>Generic sans-serif family (<c>FontFamily.SansSerif</c>).</summary>
    [ComposeCompanionGetter("getSansSerif", ReturnDescriptor = "Landroidx/compose/ui/text/font/GenericFontFamily;")]
    public static partial FontFamily SansSerif { get; }

    /// <summary>Generic serif family (<c>FontFamily.Serif</c>).</summary>
    [ComposeCompanionGetter("getSerif", ReturnDescriptor = "Landroidx/compose/ui/text/font/GenericFontFamily;")]
    public static partial FontFamily Serif { get; }

    /// <summary>Generic monospace family (<c>FontFamily.Monospace</c>).</summary>
    [ComposeCompanionGetter("getMonospace", ReturnDescriptor = "Landroidx/compose/ui/text/font/GenericFontFamily;")]
    public static partial FontFamily Monospace { get; }

    /// <summary>Generic cursive family (<c>FontFamily.Cursive</c>).</summary>
    [ComposeCompanionGetter("getCursive", ReturnDescriptor = "Landroidx/compose/ui/text/font/GenericFontFamily;")]
    public static partial FontFamily Cursive { get; }
}
