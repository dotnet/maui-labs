namespace Microsoft.Maui.AI.Chat.Controls;

[ContentProperty(nameof(Templates))]
/// <summary>
/// Picks a <see cref="ContentTemplate"/> for each <see cref="ContentContext"/> by asking every
/// registered template's <c>When(...)</c> and choosing the highest-priority match.
/// </summary>
/// <remarks>
/// The registered templates act as an allow-list: a block is only rendered if some template matches it.
/// When nothing matches, the block renders as an empty (zero-size) view — omitting a template is how you
/// suppress a block kind (e.g. leave out <see cref="FunctionInvocationTemplate"/> to hide tool calls). To
/// render unexpected blocks with a catch-all instead, register a low-priority <see cref="DefaultContentTemplate"/>.
/// </remarks>
public class ContentTemplateSelector : DataTemplateSelector
{
    // Renders nothing: an unmatched block occupies no visible space. Templates are the
    // allow-list — to show a block kind, register a matching template; to hide it, omit one.
    private static readonly DataTemplate EmptyTemplate =
        new(() => new ContentView { IsVisible = false, HeightRequest = 0 });

    public IList<ContentTemplate> Templates { get; } = new List<ContentTemplate>();

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        if (item is not ContentContext context)
            return EmptyTemplate;

        ContentTemplate? selectedTemplate = null;
        var highestPriority = int.MinValue;

        foreach (var template in Templates)
        {
            if (!template.When(context))
                continue;

            var priority = template.GetPriority(context);
            if (selectedTemplate is null || priority > highestPriority)
            {
                selectedTemplate = template;
                highestPriority = priority;
            }
        }

        return selectedTemplate?.GetTemplate() ?? EmptyTemplate;
    }
}
