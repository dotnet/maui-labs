using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Matches an <see cref="ErrorContentBlock"/> and renders it as an error bubble.</summary>
/// <remarks>
/// <see cref="AgentContext"/> adds an <see cref="ErrorContentBlock"/> when a turn fails, so failures
/// render inline as messages (in addition to <see cref="ConversationStatus.Error"/>).
/// </remarks>
public class ErrorContentTemplate : ContentTemplate
{
    public override bool When(ContentContext context) => context.Block is ErrorContentBlock;

    internal override DataTemplate GetTemplate()
    {
        if (ViewType is not null)
            return base.GetTemplate();

        return _cachedTemplate ??= new DataTemplate(() => PrepareDataTemplateView(new ErrorMessageView()));
    }

    private DataTemplate? _cachedTemplate;
}
