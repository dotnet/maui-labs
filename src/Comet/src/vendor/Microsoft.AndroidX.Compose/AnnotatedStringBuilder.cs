namespace AndroidX.Compose;

/// <summary>
/// Mutable builder for <see cref="AnnotatedString"/>. Mirrors Kotlin's
/// <c>androidx.compose.ui.text.AnnotatedString.Builder</c> +
/// <c>buildAnnotatedString { ... }</c> DSL. Pushed styles and links
/// form a nested stack — every <see cref="PushStyle"/>/<see cref="PushLink"/>
/// pairs with a <see cref="Pop()"/>.
/// </summary>
/// <example>
/// <code>
/// var b = new AnnotatedStringBuilder();
/// b.Append("Hello ");
/// b.PushStyle(new SpanStyle { FontWeight = FontWeight.Bold });
/// b.Append("world");
/// b.Pop();
/// AnnotatedString result = b.ToAnnotatedString();
/// </code>
/// </example>
public sealed class AnnotatedStringBuilder
{
    readonly AndroidX.Compose.UI.Text.AnnotatedString.Builder _builder = new();

    /// <summary>Current length of the builder's text buffer.</summary>
    public int Length => _builder.Length;

    /// <summary>Append a plain run of text at the current cursor.</summary>
    public AnnotatedStringBuilder Append(string text)
    {
        _builder.Append(text);
        return this;
    }

    /// <summary>
    /// Push a <see cref="SpanStyle"/> onto the style stack — every
    /// character appended until the matching <see cref="Pop()"/> takes
    /// these style overrides on top of any outer styles.
    /// </summary>
    /// <returns>The stack index of the pushed style, mirroring the
    /// Kotlin builder. Callers can pass this to <see cref="Pop(int)"/>
    /// to pop down to a specific level.</returns>
    public int PushStyle(SpanStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return _builder.PushStyle(style.Build());
    }

    /// <summary>
    /// Push a <see cref="LinkAnnotation"/> onto the link stack — every
    /// character appended until the matching <see cref="Pop()"/> becomes
    /// part of the link's click target.
    /// </summary>
    /// <returns>The stack index of the pushed annotation.</returns>
    public int PushLink(LinkAnnotation link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return _builder.PushLink(link.Binding);
    }

    /// <summary>Pop the most recently pushed style or link.</summary>
    public void Pop() => _builder.Pop();

    /// <summary>
    /// Pop the style/link stack down to (but not including) the given
    /// index — i.e. discard everything pushed AFTER that index.
    /// </summary>
    public void Pop(int index) => _builder.Pop(index);

    /// <summary>
    /// Attach a non-rendering string annotation to a previously
    /// appended range. Used by clients to look up metadata at tapped
    /// offsets without affecting the displayed text.
    /// </summary>
    public void AddStringAnnotation(string tag, string annotation, int start, int end) =>
        _builder.AddStringAnnotation(tag, annotation, start, end);

    /// <summary>Apply a <see cref="SpanStyle"/> to a specific range.</summary>
    public void AddStyle(SpanStyle style, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(style);
        _builder.AddStyle(style.Build(), start, end);
    }

    /// <summary>Attach a <see cref="LinkAnnotation"/> to a specific range.</summary>
    public void AddLink(LinkAnnotation link, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(link);
        switch (link.Binding)
        {
            case AndroidX.Compose.UI.Text.LinkAnnotation.Url url:
                _builder.AddLink(url, start, end);
                break;
            case AndroidX.Compose.UI.Text.LinkAnnotation.Clickable c:
                _builder.AddLink(c, start, end);
                break;
            default:
                throw new NotSupportedException(
                    $"Unknown LinkAnnotation subtype '{link.Binding?.GetType().FullName ?? "null"}'. " +
                    "Add an explicit AddLink overload when Compose introduces a new variant.");
        }
    }

    /// <summary>Materialize the immutable <see cref="AnnotatedString"/>.</summary>
    public AnnotatedString ToAnnotatedString() =>
        new AnnotatedString(_builder.ToAnnotatedString());
}
