using Microsoft.AspNetCore.Components.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Matches <see cref="RichContentBlock"/> blocks. This is a higher-fidelity
/// alternative to <see cref="TextContentTemplate"/> that renders the
/// structured <see cref="RichTextNode"/> AST (headings, code blocks, lists, etc.)
/// instead of plain raw text.
/// </summary>
public class RichTextContentTemplate : ContentTemplate
{
    public RichTextContentTemplate()
    {
        ViewType = typeof(RichTextContentView);
    }

    public override bool When(ContentContext context)
        => context.Block is RichContentBlock rcb && rcb.Content.Count > 0;

    internal override int GetPriority(ContentContext context)
        => base.GetPriority(context) + 200; // Higher than TextContentTemplate
}
