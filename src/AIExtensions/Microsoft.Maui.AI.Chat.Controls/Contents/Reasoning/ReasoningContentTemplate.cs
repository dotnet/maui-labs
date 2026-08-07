using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Matches a <see cref="ReasoningContentBlock"/> and renders a collapsible reasoning view.</summary>
public class ReasoningContentTemplate : ContentTemplate
{
    public override bool When(ContentContext context) => context.Block is ReasoningContentBlock;

    internal override DataTemplate GetTemplate()
    {
        if (ViewType is not null)
            return base.GetTemplate();

        return _cachedTemplate ??= new DataTemplate(
            () => PrepareDataTemplateView(new ReasoningView()));
    }

    private DataTemplate? _cachedTemplate;
}
