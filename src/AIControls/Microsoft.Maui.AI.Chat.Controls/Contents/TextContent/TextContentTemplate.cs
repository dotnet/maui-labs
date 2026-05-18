using Microsoft.AspNetCore.Components.AI;
using Microsoft.Maui.AI.Chat.Controls.Themes;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

public class TextContentTemplate : ContentTemplate
{
    /// <summary>
    /// Optional role filter. Use "User" or "Assistant" to restrict matching.
    /// </summary>
    public string? Role { get; set; }

    public override bool When(ContentContext context)
    {
        if (context.Block is not RichContentBlock)
            return false;

        if (Role is not null)
        {
            var expectedRole = Role.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant;
            if (context.Role != expectedRole)
                return false;
        }

        return true;
    }

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
