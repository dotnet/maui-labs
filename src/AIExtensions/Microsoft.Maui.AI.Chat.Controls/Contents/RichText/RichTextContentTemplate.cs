using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Renders provider-supplied structured <see cref="RichContentBlock"/> content with
/// <see cref="RichTextView"/>.
/// </summary>
public class RichTextContentTemplate : ContentTemplate
{
    public override bool When(ContentContext context) =>
        context.Block is RichContentBlock and not TextContentBlock;

    protected override DataTemplate CreateTemplate()
    {
        if (ViewType is not null)
            return base.CreateTemplate();

        return new DataTemplate(() =>
        {
            var view = new RichTextView();
            view.SetDynamicResource(
                ContentView.ControlTemplateProperty,
                Themes.ChatThemeKeys.ChatMessageTemplate);
            return PrepareDataTemplateView(view);
        });
    }
}
