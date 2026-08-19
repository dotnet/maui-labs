using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Matches an <see cref="ErrorContentBlock"/> and renders it as an error bubble.</summary>
/// <remarks>
/// <see cref="MessageListView"/> projects an <see cref="ErrorContentBlock"/> when a turn fails, so failures
/// render inline as messages without adding diagnostic details to the persisted conversation.
/// </remarks>
public class ErrorContentTemplate : ContentTemplate
{
    public override bool When(ContentContext context) => context.Block is ErrorContentBlock;

    protected override DataTemplate CreateTemplate()
    {
        if (ViewType is not null)
            return base.CreateTemplate();

        return CreateMessageTemplate(() => new ErrorMessageView());
    }
}
