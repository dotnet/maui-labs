namespace Microsoft.Maui.AI.Chat.Controls;

[ContentProperty(nameof(Templates))]
/// <summary>
/// Picks a <see cref="ContentTemplate"/> for each <see cref="ContentContext"/> by asking every
/// registered template's <c>When(...)</c> and choosing the highest-priority match.
/// </summary>
/// <remarks>
/// Consumer templates are evaluated before the built-in fallback tier, so any explicit match wins regardless
/// of its numeric priority. Within each tier, the highest priority wins and declaration order breaks ties.
/// When nothing matches, the block renders as an empty (zero-size) view.
/// </remarks>
public class ContentTemplateSelector : DataTemplateSelector
{
    // Renders nothing: an unmatched block occupies no visible space. Templates are the
    // allow-list — to show a block kind, register a matching template; to hide it, omit one.
    private static readonly DataTemplate EmptyTemplate =
        new(() => new ContentView { IsVisible = false, HeightRequest = 0 });

    public IList<ContentTemplate> Templates { get; } = new List<ContentTemplate>();

    internal IList<ContentTemplate> FallbackTemplates { get; } = new List<ContentTemplate>();

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        if (item is not ContentContext context)
            return EmptyTemplate;

        return SelectBestTemplate(Templates, context)?.GetTemplate()
            ?? SelectBestTemplate(FallbackTemplates, context)?.GetTemplate()
            ?? EmptyTemplate;
    }

    private static ContentTemplate? SelectBestTemplate(
        IEnumerable<ContentTemplate> templates,
        ContentContext context)
    {
        ContentTemplate? selectedTemplate = null;
        var highestPriority = int.MinValue;

        foreach (var template in templates)
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

        return selectedTemplate;
    }
}
