using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Builder-style C# wrapper around <c>androidx.compose.ui.text.TextStyle</c>.
/// Materializes a binding <see cref="AndroidX.Compose.UI.Text.TextStyle"/>
/// by starting from the Material 3 baseline (<c>TextStyle.Default</c>)
/// and overriding only the properties the caller set. Nine slots are
/// exposed (the most common set required by typography customization);
/// every other field — <c>BaselineShift</c>, <c>Shadow</c>,
/// <c>TextGeometricTransform</c>, etc. — is forwarded from the default.
/// </summary>
/// <remarks>
/// Used as the input to <c>MaterialTheme.BuildTypography(...)</c>.
/// Each <see cref="TextStyle"/> instance is independent — call
/// <see cref="Build"/> once per Typography slot.
/// </remarks>
public sealed class TextStyle
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

    /// <summary>Line height (sp). Leave <see langword="null"/> to inherit.</summary>
    public Sp? LineHeight { get; set; }

    /// <summary>Horizontal text alignment. Leave <see langword="null"/> to inherit.</summary>
    public TextAlign? TextAlign { get; set; }

    /// <summary>Text decoration (None / Underline / LineThrough). Leave <see langword="null"/> to inherit.</summary>
    public TextDecoration? TextDecoration { get; set; }

    static T? Cast<T>(Java.Lang.Object? wrapper) where T : Java.Lang.Object =>
        wrapper is null ? null : Java.Lang.Object.GetObject<T>(wrapper.Handle, JniHandleOwnership.DoNotTransfer);

    static int UnboxTextAlign(TextAlign? wrapper, int fallback)
    {
        if (wrapper is null) return fallback;
        var binding = Java.Lang.Object.GetObject<AndroidX.Compose.UI.Text.Style.TextAlign>(wrapper.Handle, JniHandleOwnership.DoNotTransfer);
        return binding!.Value;
    }

    /// <summary>
    /// Materialize the binding <see cref="AndroidX.Compose.UI.Text.TextStyle"/>.
    /// Properties left at <see langword="null"/> on this builder are
    /// copied verbatim from <c>TextStyle.Default</c>; properties the
    /// caller set replace the corresponding slot.
    /// </summary>
    internal AndroidX.Compose.UI.Text.TextStyle Build()
    {
        var d = TextStyleCompanion.Default;
        return d.Copy(
            color:                  Color is { } c   ? (long)c            : d.Color,
            fontSize:               FontSize is { } fs ? Sp.Pack(fs)      : d.FontSize,
            fontWeight:             FontWeight is null ? d.FontWeight     : Cast<AndroidX.Compose.UI.Text.Font.FontWeight>(FontWeight),
            fontStyle:              FontStyle is null  ? d.FontStyle      : Cast<AndroidX.Compose.UI.Text.Font.FontStyle>(FontStyle),
            fontSynthesis:          d.FontSynthesis,
            fontFamily:             FontFamily is null ? d.FontFamily     : Cast<AndroidX.Compose.UI.Text.Font.FontFamily>(FontFamily),
            fontFeatureSettings:    d.FontFeatureSettings,
            letterSpacing:          LetterSpacing is { } ls ? Sp.Pack(ls) : d.LetterSpacing,
            baselineShift:          d.BaselineShift,
            textGeometricTransform: d.TextGeometricTransform,
            localeList:             d.LocaleList,
            background:             d.Background,
            textDecoration:         TextDecoration is null ? d.TextDecoration : Cast<AndroidX.Compose.UI.Text.Style.TextDecoration>(TextDecoration),
            shadow:                 d.Shadow,
            drawStyle:              d.DrawStyle,
            textAlign:              UnboxTextAlign(TextAlign, d.GetTextAlign()),
            textDirection:          d.GetTextDirection(),
            lineHeight:             LineHeight is { } lh ? Sp.Pack(lh) : d.LineHeight,
            textIndent:             d.TextIndent,
            platformStyle:          d.PlatformStyle,
            lineHeightStyle:        d.LineHeightStyle,
            lineBreak:              d.GetLineBreak(),
            hyphens:                d.GetHyphens(),
            textMotion:             d.TextMotion);
    }
}
