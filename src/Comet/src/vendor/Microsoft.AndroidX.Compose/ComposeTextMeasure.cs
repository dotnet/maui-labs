#nullable enable
namespace AndroidX.Compose;

/// <summary>
/// Measures text with Compose's OWN layout engine (<c>TextMeasurer</c> → <c>TextLayoutResult</c>),
/// the same engine that paints a <c>Text</c>. This is the React-Native model — measure and render
/// with one engine — so the reported first baseline equals the rendered one exactly (no
/// StaticLayout/Compose cross-engine seam). Used by the layout engine for baseline alignment.
/// </summary>
public static class ComposeTextMeasure
{
    // (probe removed — the full API is directly callable, no JNI bridge needed)
    static global::AndroidX.Compose.UI.Text.TextMeasurer? _measurer;
    static global::AndroidX.Compose.UI.Unit.IDensity? _density;

    static global::AndroidX.Compose.UI.Text.TextMeasurer Measurer(global::Android.Content.Context ctx)
    {
        if (_measurer is not null)
            return _measurer;
        var resolver = global::AndroidX.Compose.UI.Text.Font.FontFamilyResolver_androidKt.CreateFontFamilyResolver(ctx);
        _density = global::AndroidX.Compose.UI.Unit.AndroidDensity_androidKt.Density(ctx);
        _measurer = new global::AndroidX.Compose.UI.Text.TextMeasurer(
            resolver, _density, global::AndroidX.Compose.UI.Unit.LayoutDirection.Ltr, 64);
        return _measurer;
    }

    /// <summary>
    /// First-line baseline (Dp from the top) of <paramref name="text"/> as Compose lays it out with a
    /// style matching the rendered <c>Text</c> — so it equals the drawn baseline. Build the style to
    /// mirror the renderer: same font size / family / weight / line height, zero letter spacing.
    /// </summary>
    public static double FirstBaselineDp(
        global::Android.Content.Context ctx, float density, string? text,
        Sp fontSize, FontFamily? fontFamily, FontWeight? fontWeight, Sp lineHeight, double maxWidthDp)
    {
        var measurer = Measurer(ctx);
        var style = new TextStyle
        {
            FontSize = fontSize,
            FontFamily = fontFamily,
            FontWeight = fontWeight,
            LetterSpacing = Sp.Zero,
            LineHeight = lineHeight,
        }.Build();

        // Compose's Infinity sentinel (Int.MaxValue) marks an unbounded dimension without consuming
        // packing bits; a large *finite* maxHeight would overflow the Constraints bit budget.
        int widthPx = (maxWidthDp > 0 && !double.IsInfinity(maxWidthDp))
            ? (int)System.Math.Ceiling(maxWidthDp * density)
            : int.MaxValue;
        long constraints = global::AndroidX.Compose.UI.Unit.ConstraintsKt.Constraints(0, widthPx, 0, int.MaxValue);
        var resolver = global::AndroidX.Compose.UI.Text.Font.FontFamilyResolver_androidKt.CreateFontFamilyResolver(ctx);

        var result = measurer.Measure(
            text ?? string.Empty, style,
            overflow: 1 /* Clip */, softWrap: true, maxLines: int.MaxValue,
            constraints, global::AndroidX.Compose.UI.Unit.LayoutDirection.Ltr, _density!, resolver, false);

        return result.FirstBaseline / density;
    }
}
