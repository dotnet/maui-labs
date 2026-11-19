using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// C# wrapper around <c>androidx.compose.ui.text.style.TextAlign</c>.
/// In Kotlin source, <c>TextAlign</c> is a <c>@JvmInline value class</c>
/// wrapping an <c>Int</c>, but every <c>@Composable</c> function in
/// Material 3 declares it as <em>nullable</em> (<c>textAlign: TextAlign?
/// = null</c>) — so at the JNI boundary it travels as a boxed
/// <c>androidx/compose/ui/text/style/TextAlign;</c> reference, not a
/// packed <c>int</c>. The bridge generator's reference-type path
/// passes the handle through to the JNI <c>L</c> slot.
///
/// To produce that boxed reference the constants here call the
/// inline-class's mangled <c>Companion.getCenter-e0LSkKk()I</c>
/// (and friends) for the packed int, then route it through the
/// synthesized static <c>TextAlign.box-impl(I)LTextAlign;</c> to wrap.
/// <c>ComposeCompanionGenerator</c> emits both halves of that dance
/// from the <c>InlineClass = true</c> on the
/// <see cref="ComposeCompanionAttribute"/> below.
///
/// Will swap to bound <c>AndroidX.Compose.UI.Text.Style.TextAlign</c>
/// once <see href="https://github.com/dotnet/android-libraries/pull/1440"/>
/// ships and we adopt the next <c>Xamarin.AndroidX.Compose.UI.Text.Android</c>
/// release.
/// </summary>
[ComposeCompanion("androidx/compose/ui/text/style/TextAlign", InlineClass = true)]
public sealed partial class TextAlign : Java.Lang.Object
{
    TextAlign(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) { }

    /// <summary>Align text to the left edge of the container.</summary>
    [ComposeCompanionGetter("getLeft-e0LSkKk")]
    public static partial TextAlign Left { get; }

    /// <summary>Align text to the right edge of the container.</summary>
    [ComposeCompanionGetter("getRight-e0LSkKk")]
    public static partial TextAlign Right { get; }

    /// <summary>Center text within the container.</summary>
    [ComposeCompanionGetter("getCenter-e0LSkKk")]
    public static partial TextAlign Center { get; }

    /// <summary>Stretch lines to fill the container width.</summary>
    [ComposeCompanionGetter("getJustify-e0LSkKk")]
    public static partial TextAlign Justify { get; }

    /// <summary>Align text to the layout-direction start edge.</summary>
    [ComposeCompanionGetter("getStart-e0LSkKk")]
    public static partial TextAlign Start { get; }

    /// <summary>Align text to the layout-direction end edge.</summary>
    [ComposeCompanionGetter("getEnd-e0LSkKk")]
    public static partial TextAlign End { get; }

    /// <summary>The unspecified-alignment sentinel value (Kotlin's default for nullable slots).</summary>
    [ComposeCompanionGetter("getUnspecified-e0LSkKk")]
    public static partial TextAlign Unspecified { get; }
}
