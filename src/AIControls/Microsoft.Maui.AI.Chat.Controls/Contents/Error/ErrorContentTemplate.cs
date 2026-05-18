using Microsoft.Maui.AI.Chat.Controls.Themes;

namespace Microsoft.Maui.AI.Chat.Controls;

public class ErrorContentTemplate : ContentTemplate
{
    public override bool When(ContentContext context)
    {
        // Core engine surfaces errors via AgentContext.Status == Error,
        // not as content blocks. This template remains for future use.
        return false;
    }

    internal override DataTemplate GetTemplate()
    {
        if (ViewType is not null)
            return base.GetTemplate();

        return _cachedTemplate ??= new DataTemplate(() =>
        {
            var view = new ErrorMessageView();
            view.SetDynamicResource(ContentView.ControlTemplateProperty, ChatThemeKeys.ErrorTemplate);
            return PrepareDataTemplateView(view);
        });
    }

    private DataTemplate? _cachedTemplate;
}
