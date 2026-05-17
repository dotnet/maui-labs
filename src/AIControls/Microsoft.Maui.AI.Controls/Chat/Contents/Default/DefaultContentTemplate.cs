using Microsoft.Maui.AI.Controls.Themes;

namespace Microsoft.Maui.AI.Controls.Chat;

public class DefaultContentTemplate : ContentTemplate
{
    public override bool When(ContentContext context) => true;

    internal override DataTemplate GetTemplate()
    {
        if (ViewType is not null)
            return base.GetTemplate();

        return _cachedTemplate ??= new DataTemplate(() =>
        {
            var view = new DefaultMessageView();
            view.SetDynamicResource(ContentView.ControlTemplateProperty, ChatThemeKeys.DefaultTemplate);
            return PrepareDataTemplateView(view);
        });
    }

    internal override int GetPriority(ContentContext context) =>
        base.GetPriority(context) - 1000;

    private DataTemplate? _cachedTemplate;
}
