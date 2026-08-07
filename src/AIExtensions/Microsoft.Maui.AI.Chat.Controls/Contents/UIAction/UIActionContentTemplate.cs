using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Matches a <see cref="UIActionBlock"/> and renders its automatic execution state.</summary>
public class UIActionContentTemplate : ContentTemplate
{
    public override bool When(ContentContext context) => context.Block is UIActionBlock;

    internal override DataTemplate GetTemplate()
    {
        if (ViewType is not null)
            return base.GetTemplate();

        return _cachedTemplate ??= new DataTemplate(
            () => PrepareDataTemplateView(new UIActionView()));
    }

    private DataTemplate? _cachedTemplate;
}
