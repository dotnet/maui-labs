using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Matches a <see cref="ThinkingContentBlock"/> and renders the transient "Thinking…" bubble.</summary>
/// <remarks>Set <c>ViewType</c> to supply a custom loading view.</remarks>
public class ThinkingContentTemplate : ContentTemplate
{
    public override bool When(ContentContext context) => context.Block is ThinkingContentBlock;

    protected override DataTemplate CreateTemplate()
    {
        if (ViewType is not null)
            return base.CreateTemplate();

        return CreateMessageTemplate(() => new ThinkingView());
    }
}
