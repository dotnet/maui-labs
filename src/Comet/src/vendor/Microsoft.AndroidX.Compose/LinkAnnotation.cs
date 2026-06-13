using BindingLink = AndroidX.Compose.UI.Text.LinkAnnotation;
using BindingTextLinkStyles = AndroidX.Compose.UI.Text.TextLinkStyles;

namespace AndroidX.Compose;

/// <summary>
/// C# wrapper around <c>androidx.compose.ui.text.LinkAnnotation</c> —
/// attaches a tappable target to a range inside an
/// <see cref="AnnotatedString"/>. The two concrete subtypes are
/// produced via the static factories <see cref="Url"/> and
/// <see cref="Clickable"/>; PushLink/AddLink on
/// <see cref="AnnotatedStringBuilder"/> accept either.
/// </summary>
/// <remarks>
/// Url annotations are handled by Compose itself — tapping opens the
/// URL via the platform's <c>UriHandler</c>. Clickable annotations
/// invoke the C# <see cref="Action{T}"/> the caller supplied
/// (e.g. to show a profile dialog for an <c>@mention</c>).
/// </remarks>
public sealed class LinkAnnotation
{
    internal BindingLink Binding { get; }

    LinkAnnotation(BindingLink binding) => Binding = binding;

    /// <summary>
    /// Create a URL link. Compose will launch the platform URI handler
    /// when the rendered text is tapped — no extra wiring required.
    /// </summary>
    /// <param name="url">Absolute URL to open.</param>
    /// <param name="style">Optional default <see cref="SpanStyle"/> to
    /// apply to the linked range; pass <see langword="null"/> to
    /// inherit from the enclosing text styling.</param>
    public static LinkAnnotation Url(string url, SpanStyle? style = null)
    {
        var styles = style is null ? null : new BindingTextLinkStyles(style.Build(), null, null, null);
        return new LinkAnnotation(new BindingLink.Url(url, styles, null));
    }

    /// <summary>
    /// Create a clickable annotation whose tap fires a C# callback.
    /// Useful for in-app navigation — mentions, hashtags, etc. — where
    /// the linked text isn't a URL.
    /// </summary>
    /// <param name="tag">Identifier passed back to <paramref name="onClick"/>
    /// when invoked; appears as the link's tag in Compose's annotation
    /// model.</param>
    /// <param name="onClick">Callback fired on tap. Receives <paramref name="tag"/>.</param>
    /// <param name="style">Optional default <see cref="SpanStyle"/> for
    /// the linked range.</param>
    public static LinkAnnotation Clickable(string tag, Action<string> onClick, SpanStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        var styles   = style is null ? null : new BindingTextLinkStyles(style.Build(), null, null, null);
        var listener = new LinkClickListener(tag, onClick);
        return new LinkAnnotation(new BindingLink.Clickable(tag, styles, listener));
    }
}
