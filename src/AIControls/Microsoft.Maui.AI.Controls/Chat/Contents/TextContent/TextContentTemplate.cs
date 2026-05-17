using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Controls.Themes;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Controls.Chat;

public class TextContentTemplate : ContentTemplate
{
    public ContentRole? Role { get; set; }

    public override bool When(ContentContext context) =>
        context.Content is TextContent &&
        (Role is null || context.Role == Role.Value);

    internal override DataTemplate GetTemplate()
    {
        if (ViewType is not null)
            return base.GetTemplate();

        return _cachedTemplate ??= new DataTemplate(() =>
        {
            var view = new ChatMessageView();
            view.SetDynamicResource(ContentView.ControlTemplateProperty, ChatThemeKeys.ChatMessageTemplate);
            return PrepareDataTemplateView(view);
        });
    }

    internal override int GetPriority(ContentContext context) =>
        base.GetPriority(context) + (Role is null ? 0 : 100);

    private DataTemplate? _cachedTemplate;
}
