using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Matches a <see cref="ReasoningContentBlock"/> and renders a collapsible reasoning view.</summary>
public class ReasoningContentTemplate : ContentTemplate
{
    public override bool When(ContentContext context) => context.Block is ReasoningContentBlock;

    protected override DataTemplate CreateTemplate()
    {
        if (ViewType is not null)
            return base.CreateTemplate();

        return CreateMessageTemplate(() => new ReasoningView());
    }
}
