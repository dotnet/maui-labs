using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// C# wrapper around the <c>androidx.compose.ui.layout.ContentScale</c>
/// singletons exposed by Kotlin's <c>ContentScale.Companion</c> object.
/// Compose's <c>ContentScale</c> is a real Kotlin interface (not a
/// <c>@JvmInline value class</c>) — same plumbing story as
/// <see cref="TextDecoration"/>, generated from the
/// <see cref="ComposeCompanionAttribute"/> below. The
/// <c>Xamarin.AndroidX.Compose.UI</c> binding currently doesn't surface
/// a managed <c>IContentScale</c> type, so we resolve the companion
/// getters via JNI directly.
///
/// Pass instances to facades that accept an optional <c>contentScale</c>
/// slot (e.g. <see cref="Image"/>) to control how source content is
/// scaled to fit its destination bounds. Left unset (null) on those
/// facades, Kotlin's default (<c>ContentScale.Fit</c>) applies.
/// </summary>
[ComposeCompanion("androidx/compose/ui/layout/ContentScale")]
public sealed partial class ContentScale : Java.Lang.Object
{
    ContentScale(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) { }

    /// <summary>
    /// <c>ContentScale.Fit</c> — scale the source uniformly (maintaining
    /// the aspect ratio) so both dimensions fit within the destination.
    /// Comparable to <c>ImageView.ScaleType.FIT_CENTER</c>.
    /// </summary>
    [ComposeCompanionGetter("getFit")]
    public static partial ContentScale Fit { get; }

    /// <summary>
    /// <c>ContentScale.Crop</c> — scale the source uniformly so it fully
    /// fills the destination, cropping any overflow. Comparable to
    /// <c>ImageView.ScaleType.CENTER_CROP</c>.
    /// </summary>
    [ComposeCompanionGetter("getCrop")]
    public static partial ContentScale Crop { get; }

    /// <summary>
    /// <c>ContentScale.FillHeight</c> — scale the source uniformly to
    /// match the destination's height; the width may overflow or be
    /// clipped depending on the parent.
    /// </summary>
    [ComposeCompanionGetter("getFillHeight")]
    public static partial ContentScale FillHeight { get; }

    /// <summary>
    /// <c>ContentScale.FillWidth</c> — scale the source uniformly to
    /// match the destination's width; the height may overflow or be
    /// clipped depending on the parent.
    /// </summary>
    [ComposeCompanionGetter("getFillWidth")]
    public static partial ContentScale FillWidth { get; }

    /// <summary>
    /// <c>ContentScale.FillBounds</c> — scale the source non-uniformly
    /// to exactly match the destination bounds. Distorts the aspect
    /// ratio. Comparable to <c>ImageView.ScaleType.FIT_XY</c>.
    /// </summary>
    [ComposeCompanionGetter("getFillBounds")]
    public static partial ContentScale FillBounds { get; }

    /// <summary>
    /// <c>ContentScale.Inside</c> — like <see cref="Fit"/> when the
    /// source is larger than the destination; otherwise the source is
    /// drawn at its intrinsic size (no upscaling). Comparable to
    /// <c>ImageView.ScaleType.CENTER_INSIDE</c>.
    /// </summary>
    [ComposeCompanionGetter("getInside")]
    public static partial ContentScale Inside { get; }

    /// <summary>
    /// <c>ContentScale.None</c> — draw the source at its intrinsic size
    /// (no scaling). Comparable to <c>ImageView.ScaleType.CENTER</c>.
    /// </summary>
    // ContentScale.getNone() returns the concrete subtype FixedScale, not the
    // ContentScale base — so override the JNI return descriptor to match.
    [ComposeCompanionGetter("getNone", ReturnDescriptor = "Landroidx/compose/ui/layout/FixedScale;")]
    public static partial ContentScale None { get; }
}
