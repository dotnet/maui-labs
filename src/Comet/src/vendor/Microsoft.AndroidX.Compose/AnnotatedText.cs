using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <see cref="Text"/> rendering an <see cref="AnnotatedString"/> —
/// rich text with per-span styling and clickable link/mention
/// annotations. The companion type to <see cref="Text"/> for
/// plain-string content; use it whenever the body has inline markup
/// (bold runs, links, mentions, code spans, …) the caller pre-built
/// via <see cref="AnnotatedStringBuilder"/>.
/// </summary>
/// <remarks>
/// Hand-written instead of source-generated: the source-generated
/// <see cref="Text"/> facade emits a single <c>Render</c> from one
/// bridge declaration, so it can't be extended with a second
/// <c>Text(AnnotatedString)</c> constructor that routes to a different
/// JNI bridge (the AnnotatedString overload's mangled
/// <c>Text-IbK3jfQ</c> signature carries an extra <c>Map</c> slot for
/// inline content). Modelled as a sibling facade rather than demoting
/// <see cref="Text"/> to hand-written — matches the precedent set by
/// <see cref="Icon"/> exposing both vector-asset and resource-id paths.
/// </remarks>
public sealed class AnnotatedText : ComposableNode
{
    readonly AnnotatedString _text;

    /// <summary>Construct a rich-text node from an <see cref="AnnotatedString"/>.</summary>
    public AnnotatedText(AnnotatedString text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
    }

    /// <summary>Foreground text color (overrides any default from the enclosing theme).</summary>
    public Color? Color { get; set; }

    /// <summary>Font size (sp).</summary>
    public Sp? FontSize { get; set; }

    /// <summary>Italic / Normal.</summary>
    public FontStyle? FontStyle { get; set; }

    /// <summary>Font weight.</summary>
    public FontWeight? FontWeight { get; set; }

    /// <summary>Font family.</summary>
    public FontFamily? FontFamily { get; set; }

    /// <summary>Letter spacing (sp).</summary>
    public Sp? LetterSpacing { get; set; }

    /// <summary>Text decoration.</summary>
    public TextDecoration? Decoration { get; set; }

    /// <summary>Horizontal text alignment.</summary>
    public TextAlign? Align { get; set; }

    /// <summary>Line height (sp).</summary>
    public Sp? LineHeight { get; set; }

    /// <summary>Overflow strategy when text exceeds its bounds.</summary>
    public TextOverflow? Overflow { get; set; }

    /// <summary>Whether soft line wrapping is enabled.</summary>
    public bool? SoftWrap { get; set; }

    /// <summary>Maximum visible line count.</summary>
    public int? MaxLines { get; set; }

    /// <summary>Minimum visible line count.</summary>
    public int? MinLines { get; set; }

    /// <inheritdoc/>
    public override void Render(IComposer composer)
    {
        ComposeBridges.TextAnnotated(
            text:          _text,
            modifier:      BuildModifier(),
            color:         Color,
            fontSize:      FontSize,
            fontStyle:     FontStyle,
            fontWeight:    FontWeight,
            fontFamily:    FontFamily,
            letterSpacing: LetterSpacing,
            decoration:    Decoration,
            align:         Align,
            lineHeight:    LineHeight,
            overflow:      Overflow,
            softWrap:      SoftWrap,
            maxLines:      MaxLines,
            minLines:      MinLines,
            composer:      composer);
    }
}
