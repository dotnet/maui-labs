using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// Renders a <see cref="ProductResultsBlock"/> with <see cref="ProductResultsView"/>.
/// </summary>
/// <remarks>
/// The idiomatic first-party way to wire a custom block to its view: a tiny typed
/// <see cref="ContentTemplate"/> subclass (exactly like the built-in <c>MediaContentTemplate</c>),
/// rather than a declarative <c>GenericContentTemplate BlockType=… ViewType=…</c>. It encapsulates the
/// block match, reads as one element in XAML (<c>&lt;gchat:ProductResultsTemplate /&gt;</c>), and is what
/// a distributable custom-block library would ship alongside its block, handler, and view.
/// </remarks>
public sealed class ProductResultsTemplate : ContentTemplate
{
    public ProductResultsTemplate() => ViewType = typeof(ProductResultsView);

    public override bool When(ContentContext context) => context.Block is ProductResultsBlock;
}
