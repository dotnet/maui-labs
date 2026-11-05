using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Builder-style C# wrapper around
/// <c>androidx.compose.ui.text.SpanStyle</c>. The Kotlin type has no
/// public no-arg constructor — only <c>Copy(...)</c> overloads — so this
/// builder materializes a binding <see cref="AndroidX.Compose.UI.Text.SpanStyle"/>
/// by starting from <c>TextStyle.Default.toSpanStyle()</c> and overriding
/// only the slots the caller set. Any field left at <see langword="null"/>
/// is forwarded verbatim from the default — same trick used by
/// <see cref="TextStyle"/> for the larger paragraph-style surface.
/// </summary>
/// <remarks>
/// Pass instances to <see cref="AnnotatedStringBuilder.PushStyle"/> or
/// <see cref="AnnotatedStringBuilder.AddStyle"/> to apply per-run
/// styling inside an <see cref="AnnotatedString"/>. The builder
/// allocates one binding <c>SpanStyle</c> instance per call to
/// <see cref="Build"/>; cache the materialized result if the same
/// style is reused across many spans.
/// </remarks>
public sealed class SpanStyle
{
    /// <summary>Foreground text color. Leave <see langword="null"/> to inherit.</summary>
    public Color? Color { get; set; }

    /// <summary>Font size (sp). Leave <see langword="null"/> to inherit.</summary>
    public Sp? FontSize { get; set; }

    /// <summary>Font weight. Leave <see langword="null"/> to inherit.</summary>
    public FontWeight? FontWeight { get; set; }

    /// <summary>Font style (Normal / Italic). Leave <see langword="null"/> to inherit.</summary>
    public FontStyle? FontStyle { get; set; }

    /// <summary>Font family. Leave <see langword="null"/> to inherit.</summary>
    public FontFamily? FontFamily { get; set; }

    /// <summary>Letter spacing (sp). Leave <see langword="null"/> to inherit.</summary>
    public Sp? LetterSpacing { get; set; }

    /// <summary>Background color for this span. Leave <see langword="null"/> to inherit.</summary>
    public Color? Background { get; set; }

    /// <summary>Text decoration (Underline, LineThrough, etc.). Leave <see langword="null"/> to inherit.</summary>
    public TextDecoration? Decoration { get; set; }

    static T? Cast<T>(Java.Lang.Object? wrapper) where T : Java.Lang.Object =>
        wrapper is null ? null : Java.Lang.Object.GetObject<T>(wrapper.Handle, JniHandleOwnership.DoNotTransfer);

    /// <summary>
    /// Materialize the binding <see cref="AndroidX.Compose.UI.Text.SpanStyle"/>.
    /// Properties left at <see langword="null"/> on this builder are
    /// copied verbatim from <c>TextStyle.Default.toSpanStyle()</c>;
    /// properties the caller set replace the corresponding slot.
    /// </summary>
    internal AndroidX.Compose.UI.Text.SpanStyle Build()
    {
        var d = TextStyleCompanion.DefaultSpan;
        return d.Copy(
            color:                  Color is { } c ? (long)c : d.Color,
            fontSize:               FontSize is { } fs ? Sp.Pack(fs) : d.FontSize,
            fontWeight:             FontWeight is null ? d.FontWeight : Cast<AndroidX.Compose.UI.Text.Font.FontWeight>(FontWeight),
            fontStyle:              FontStyle is null  ? d.FontStyle  : Cast<AndroidX.Compose.UI.Text.Font.FontStyle>(FontStyle),
            fontSynthesis:          d.FontSynthesis,
            fontFamily:             FontFamily is null ? d.FontFamily : Cast<AndroidX.Compose.UI.Text.Font.FontFamily>(FontFamily),
            fontFeatureSettings:    d.FontFeatureSettings,
            letterSpacing:          LetterSpacing is { } ls ? Sp.Pack(ls) : d.LetterSpacing,
            baselineShift:          d.BaselineShift,
            textGeometricTransform: d.TextGeometricTransform,
            localeList:             d.LocaleList,
            background:             Background is { } bg ? (long)bg : d.Background,
            textDecoration:         Decoration is null ? d.TextDecoration : Cast<AndroidX.Compose.UI.Text.Style.TextDecoration>(Decoration),
            shadow:                 d.Shadow,
            platformStyle:          d.PlatformStyle,
            drawStyle:              d.DrawStyle);
    }
}
