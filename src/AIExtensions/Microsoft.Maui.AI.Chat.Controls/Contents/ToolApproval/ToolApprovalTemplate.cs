using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls.Themes;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Matches <see cref="ToolApprovalBlock"/> items in the chat.
/// Optionally filters by tool name.
/// Set <see cref="ContentTemplate.ViewType"/> to a custom inner content view;
/// leave null for the default arguments display.
/// </summary>
public class ToolApprovalTemplate : ContentTemplate
{
    /// <summary>Filter to a specific tool name, or null to match all approval requests.</summary>
    public string? ToolName { get; set; }

    public override bool When(ContentContext context)
    {
        if (context.Block is not ToolApprovalBlock fab)
            return false;

        if (ToolName is not null && !string.Equals(fab.ToolName, ToolName, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    protected override DataTemplate CreateTemplate()
    {
        return CreateMessageTemplate(() =>
        {
            var wrapper = new ToolApprovalView();
            wrapper.InnerContentType = ViewType;
            // Explicit template lookup — implicit styles may not resolve inside CollectionView
            wrapper.SetDynamicResource(ContentView.ControlTemplateProperty, ChatThemeKeys.ToolApprovalTemplate);
            return wrapper;
        });
    }

    internal override int GetPriority(ContentContext context) =>
        base.GetPriority(context) + (ToolName is null ? -100 : 100);
}
