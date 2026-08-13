using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Matches a <see cref="UIActionBlock"/> and renders its automatic execution state.</summary>
public class UIActionContentTemplate : ContentTemplate
{
    public override bool When(ContentContext context) => context.Block is UIActionBlock;

    protected override DataTemplate CreateTemplate()
    {
        if (ViewType is not null)
            return base.CreateTemplate();

        return new DataTemplate(
            () => PrepareDataTemplateView(new UIActionView()));
    }
}
