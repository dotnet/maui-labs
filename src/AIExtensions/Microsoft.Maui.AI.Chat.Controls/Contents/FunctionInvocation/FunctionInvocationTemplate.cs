using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls.Themes;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Matches a <see cref="Microsoft.Maui.AI.Chat.FunctionInvocationContentBlock"/> regardless of whether its
/// result has arrived yet, rendering a single view that starts as the pending call and updates to show the result.
/// </summary>
/// <remarks>
/// Set <see cref="ContentTemplate.ViewType"/> to a custom view (e.g. a weather card) to render a specific
/// tool, and set <see cref="ToolName"/> to scope the template to one tool. A tool-scoped template outranks the
/// generic one via <see cref="GetPriority"/>, so the default invocation view stays as a catch-all fallback.
/// </remarks>
public class FunctionInvocationTemplate : ContentTemplate
{
    public string? ToolName { get; set; }

    public override bool When(ContentContext context)
    {
        if (context.Block is not FunctionInvocationContentBlock ficb)
            return false;

        if (ToolName is not null && !string.Equals(ficb.Call?.Name, ToolName, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    protected override DataTemplate CreateTemplate()
    {
        if (ViewType is not null)
            return base.CreateTemplate();

        return new DataTemplate(() =>
        {
            var view = new FunctionInvocationView();
            view.SetDynamicResource(ContentView.ControlTemplateProperty, ChatThemeKeys.FunctionInvocationTemplate);
            return PrepareDataTemplateView(view);
        });
    }

    internal override int GetPriority(ContentContext context) =>
        base.GetPriority(context) + (ToolName is null ? -100 : 100);
}
