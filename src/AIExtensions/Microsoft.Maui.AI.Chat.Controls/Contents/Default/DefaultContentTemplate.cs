using Microsoft.Maui.AI.Chat.Controls.Themes;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>The lowest-priority fallback template; matches any block when no other template does.</summary>
public class DefaultContentTemplate : ContentTemplate
{
    public override bool When(ContentContext context) => true;

    protected override DataTemplate CreateTemplate()
    {
        if (ViewType is not null)
            return base.CreateTemplate();

        return new DataTemplate(() =>
        {
            var view = new DefaultMessageView();
            view.SetDynamicResource(ContentView.ControlTemplateProperty, ChatThemeKeys.DefaultTemplate);
            return PrepareDataTemplateView(view);
        });
    }

    internal override int GetPriority(ContentContext context) =>
        base.GetPriority(context) - 1000;
}
