using Microsoft.Maui.AI.Chat.Controls.Themes;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

public class FunctionCallTemplate : ContentTemplate
{
    public string? ToolName { get; set; }

    public override bool When(ContentContext context) =>
        context.Content is FunctionCallContent call &&
        (ToolName is null || string.Equals(call.Name, ToolName, StringComparison.OrdinalIgnoreCase));

    internal override DataTemplate GetTemplate()
    {
        if (ViewType is not null)
            return base.GetTemplate();

        return _cachedTemplate ??= new DataTemplate(() =>
        {
            var view = new FunctionCallMessageView();
            view.SetDynamicResource(ContentView.ControlTemplateProperty, ChatThemeKeys.FunctionCallTemplate);
            return PrepareDataTemplateView(view);
        });
    }

    internal override int GetPriority(ContentContext context) =>
        base.GetPriority(context) + (ToolName is null ? -100 : 100);

    private DataTemplate? _cachedTemplate;
}
