using Microsoft.Maui.AI.Chat.Controls.Themes;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>The lowest-priority fallback template; matches any block when no other template does.</summary>
public class DefaultContentTemplate : ContentTemplate
{
    public override bool When(ContentContext context) =>
        context.Content is not TextMessageContent
            and not MediaMessageContent;

    protected override DataTemplate CreateTemplate()
    {
        if (ViewType is not null)
            return base.CreateTemplate();

        return CreateMessageTemplate(() =>
        {
            var view = new DefaultMessageView();
            view.SetDynamicResource(ContentView.ControlTemplateProperty, ChatThemeKeys.DefaultTemplate);
            return view;
        });
    }

    internal override int GetPriority(ContentContext context) =>
        base.GetPriority(context) - 1000;
}
