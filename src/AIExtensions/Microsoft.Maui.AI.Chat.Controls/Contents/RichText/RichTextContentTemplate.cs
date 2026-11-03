using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Renders provider-supplied structured <see cref="RichContentBlock"/> content with
/// <see cref="RichTextView"/>.
/// </summary>
public class RichTextContentTemplate : ContentTemplate
{
    public override bool When(ContentContext context) =>
        context.Content is StructuredTextMessageContent<IReadOnlyList<RichTextNode>>;

    protected override DataTemplate CreateTemplate()
    {
        if (ViewType is not null)
            return base.CreateTemplate();

        return CreateMessageTemplate(() => new RichTextView());
    }
}
