using Microsoft.Maui.AI.Controls.Themes;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Controls.Chat;

/// <summary>
/// Matches <see cref="ToolApprovalRequestContent"/> items in the chat.
/// Optionally filters by tool name.
/// Set <see cref="ContentTemplate.ViewType"/> to a custom inner content view;
/// leave null for the default arguments display.
/// </summary>
public class ToolApprovalTemplate : ContentTemplate
{
    /// <summary>Filter to a specific tool name, or null to match all approval requests.</summary>
    public string? ToolName { get; set; }

    public override bool When(ContentContext context) =>
        context.Content is ToolApprovalRequestContent &&
        (ToolName is null || string.Equals(context.ToolName, ToolName, StringComparison.OrdinalIgnoreCase));

    internal override DataTemplate GetTemplate()
    {
        var innerType = ViewType;
        return _cachedTemplate ??= new DataTemplate(() =>
        {
            var wrapper = new ToolApprovalView();
            wrapper.InnerContentType = innerType;
            // Explicit template lookup — implicit styles may not resolve inside CollectionView
            wrapper.SetDynamicResource(ContentView.ControlTemplateProperty, ChatThemeKeys.ToolApprovalTemplate);
            return PrepareDataTemplateView(wrapper);
        });
    }

    internal override int GetPriority(ContentContext context) =>
        base.GetPriority(context) + (ToolName is null ? -100 : 100);

    private DataTemplate? _cachedTemplate;
}
