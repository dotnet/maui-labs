using Microsoft.AspNetCore.Components.AI;
using Microsoft.Maui.AI.Chat.Controls.Themes;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

public class FunctionResultTemplate : ContentTemplate
{
    public string? ToolName { get; set; }

    public override bool When(ContentContext context)
    {
        if (context.Block is not FunctionInvocationContentBlock ficb)
            return false;

        // Only match when result IS available
        if (ficb.Result is null)
            return false;

        if (ToolName is not null && !string.Equals(ficb.Call?.Name, ToolName, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    internal override DataTemplate GetTemplate()
    {
        if (ViewType is not null)
            return base.GetTemplate();

        return _cachedTemplate ??= new DataTemplate(() =>
        {
            var view = new FunctionResultMessageView();
            view.SetDynamicResource(ContentView.ControlTemplateProperty, ChatThemeKeys.FunctionResultTemplate);
            return PrepareDataTemplateView(view);
        });
    }

    internal override int GetPriority(ContentContext context) =>
        base.GetPriority(context) + (ToolName is null ? -100 : 100);

    private DataTemplate? _cachedTemplate;
}
