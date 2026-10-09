using Android.Runtime;
using BoundBrush = AndroidX.Compose.UI.Graphics.Brush;
using BoundSolidColor = AndroidX.Compose.UI.Graphics.SolidColor;

namespace AndroidX.Compose;

/// <summary>
/// Static factory entrypoint for Kotlin's
/// <c>androidx.compose.ui.graphics.Brush</c> — the value passed to fill
/// / stroke slots that want richer paint than a single
/// <see cref="Color"/>. The instances returned by these factories are
/// bound <see cref="AndroidX.Compose.UI.Graphics.Brush"/> peers and can
/// be passed straight to <c>Modifier.Background(brush, …)</c> /
/// <c>Modifier.Border(width, brush, …)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors Kotlin's <c>Brush.linearGradient(…)</c>,
/// <c>Brush.horizontalGradient(…)</c>, etc. — Kotlin exposes these via
/// <c>Brush.Companion</c>, but the binder doesn't surface a public
/// accessor for the singleton (and the gradient factories' inline
/// <c>Color</c> / <c>TileMode</c> params come through under mangled
/// names like <c>LinearGradient_mHitzGk</c>), so this class wraps the
/// Companion call into a friendly C# entrypoint.
/// </para>
/// <para>
/// Brush instances are immutable — keep one in a field (or
/// <c>composer.Remember(() =&gt; Brush.HorizontalGradient(…))</c>)
/// when the same brush renders every frame; otherwise let the
/// composition build a fresh instance each pass.
/// </para>
/// </remarks>
public static class Brush
{
    /// <summary>
    /// <c>SolidColor(color)</c> — a brush that paints a single flat
    /// color. Mostly useful for APIs that demand a brush rather than a
    /// <see cref="Color"/>; for the typical "fill with a color" case
    /// prefer the <c>Modifier.Background(Color)</c> overload, which
    /// avoids the extra JNI allocation.
    /// </summary>
    public static BoundBrush SolidColor(Color color) =>
        Java.Lang.Object.GetObject<BoundSolidColor>(
            ComposeBridges.BrushSolidColor(color),
            JniHandleOwnership.TransferLocalRef)!;

    /// <summary>
    /// <c>Brush.linearGradient(colors, start, end, tileMode)</c> —
    /// gradient interpolating along the line from
    /// <paramref name="start"/> to <paramref name="end"/>. Colors are
    /// spaced evenly along the line; pass
    /// <see cref="Offset.Zero"/> and <see cref="Offset.Infinite"/> for
    /// a fill-the-region diagonal gradient (matches Kotlin's
    /// <c>Brush.linearGradient(colors)</c> default).
    /// </summary>
    public static BoundBrush LinearGradient(
        Color[] colors,
        Offset start,
        Offset end,
        TileMode tileMode = TileMode.Clamp) =>
        ComposeBridges.BrushCompanion().LinearGradient_mHitzGk(
            ComposeBridges.ToColorList(colors), start.Packed, end.Packed, (int)tileMode);

    /// <summary>
    /// Convenience overload — matches Kotlin's
    /// <c>Brush.linearGradient(colors)</c> default of
    /// <c>start = Offset.Zero, end = Offset.Infinite,
    /// tileMode = Clamp</c>. <see cref="Offset.Infinite"/> resolves to
    /// the bottom-right corner of the drawing region at paint time, so
    /// this produces a top-left-to-bottom-right diagonal gradient.
    /// </summary>
    public static BoundBrush LinearGradient(params Color[] colors) =>
        LinearGradient(colors, Offset.Zero, Offset.Infinite, TileMode.Clamp);

    /// <summary>
    /// <c>Brush.horizontalGradient(colors, startX, endX, tileMode)</c>
    /// — gradient along the X axis. The Y component spans the full
    /// height of whatever box this brush is painted onto.
    /// </summary>
    public static BoundBrush HorizontalGradient(
        Color[] colors,
        float startX = 0f,
        float endX = float.PositiveInfinity,
        TileMode tileMode = TileMode.Clamp) =>
        ComposeBridges.BrushCompanion().HorizontalGradient_8A_3gB4(
            ComposeBridges.ToColorList(colors), startX, endX, (int)tileMode);

    /// <summary>
    /// <c>Brush.horizontalGradient(colors)</c> — Kotlin default
    /// (full-width sweep, <see cref="TileMode.Clamp"/>).
    /// </summary>
    public static BoundBrush HorizontalGradient(params Color[] colors) =>
        HorizontalGradient(colors, 0f, float.PositiveInfinity, TileMode.Clamp);

    /// <summary>
    /// <c>Brush.verticalGradient(colors, startY, endY, tileMode)</c>
    /// — gradient along the Y axis. The X component spans the full
    /// width of whatever box this brush is painted onto.
    /// </summary>
    public static BoundBrush VerticalGradient(
        Color[] colors,
        float startY = 0f,
        float endY = float.PositiveInfinity,
        TileMode tileMode = TileMode.Clamp) =>
        ComposeBridges.BrushCompanion().VerticalGradient_8A_3gB4(
            ComposeBridges.ToColorList(colors), startY, endY, (int)tileMode);

    /// <summary>
    /// <c>Brush.verticalGradient(colors)</c> — Kotlin default
    /// (full-height sweep, <see cref="TileMode.Clamp"/>).
    /// </summary>
    public static BoundBrush VerticalGradient(params Color[] colors) =>
        VerticalGradient(colors, 0f, float.PositiveInfinity, TileMode.Clamp);

    /// <summary>
    /// <c>Brush.radialGradient(colors, center, radius, tileMode)</c>
    /// — gradient radiating outward from <paramref name="center"/> to
    /// <paramref name="radius"/>. Pass <see cref="Offset.Unspecified"/>
    /// for <paramref name="center"/> to centre the gradient on the
    /// painted box.
    /// </summary>
    public static BoundBrush RadialGradient(
        Color[] colors,
        Offset center,
        float radius = float.PositiveInfinity,
        TileMode tileMode = TileMode.Clamp) =>
        ComposeBridges.BrushCompanion().RadialGradient_P_Vx_Ks(
            ComposeBridges.ToColorList(colors), center.Packed, radius, (int)tileMode);

    /// <summary>
    /// <c>Brush.radialGradient(colors)</c> — auto-centred,
    /// auto-sized, <see cref="TileMode.Clamp"/>.
    /// </summary>
    public static BoundBrush RadialGradient(params Color[] colors) =>
        RadialGradient(colors, Offset.Unspecified, float.PositiveInfinity, TileMode.Clamp);

    /// <summary>
    /// <c>Brush.sweepGradient(colors, center)</c> — angular gradient
    /// that wraps around <paramref name="center"/> through 360°. Pass
    /// <see cref="Offset.Unspecified"/> for <paramref name="center"/>
    /// to centre on the painted box.
    /// </summary>
    public static BoundBrush SweepGradient(Color[] colors, Offset center) =>
        ComposeBridges.BrushCompanion().SweepGradient_Uv8p0NA(
            ComposeBridges.ToColorList(colors), center.Packed);

    /// <summary>
    /// <c>Brush.sweepGradient(colors)</c> — auto-centred angular
    /// gradient sweeping through 360°.
    /// </summary>
    public static BoundBrush SweepGradient(params Color[] colors) =>
        SweepGradient(colors, Offset.Unspecified);
}
