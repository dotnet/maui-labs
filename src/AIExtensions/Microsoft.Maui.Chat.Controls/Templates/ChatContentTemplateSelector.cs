namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// Picks the <see cref="ChatContentTemplate"/> for each <see cref="ChatContentItem"/>, preferring
/// consumer templates over the built-in fallbacks.
/// </summary>
/// <remarks>
/// <para>
/// Selection has two tiers. Every template in <see cref="Templates"/> is asked first, so any explicit
/// consumer match wins regardless of its numeric priority; only when none match are the
/// <see cref="FallbackTemplates"/> consulted. Within a tier the highest
/// <see cref="ChatContentTemplate.GetPriority"/> wins and declaration order breaks ties.
/// </para>
/// <para>
/// Templates are the allow-list. When nothing matches — a custom content type with no template, for
/// example — the row renders as a hidden, zero-height view instead of leaking a placeholder into the UI.
/// </para>
/// </remarks>
[ContentProperty(nameof(Templates))]
public class ChatContentTemplateSelector : DataTemplateSelector
{
    private static readonly DataTemplate HiddenTemplate =
        new(static () => new ContentView
        {
            IsVisible = false,
            HeightRequest = 0,
            Margin = 0,
            Padding = 0,
        });

    /// <summary>Gets the consumer templates. Any match here outranks every fallback template.</summary>
    public IList<ChatContentTemplate> Templates { get; } = [];

    /// <summary>Gets the built-in templates used when no consumer template matches.</summary>
    public IList<ChatContentTemplate> FallbackTemplates { get; } = [];

    /// <summary>Gets the template used for rows nothing matched: hidden and zero-height.</summary>
    /// <returns>The shared hidden template.</returns>
    public static DataTemplate GetHiddenTemplate() => HiddenTemplate;

    /// <inheritdoc />
    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        if (item is not ChatContentItem contentItem)
            return HiddenTemplate;

        return SelectBest(Templates, contentItem)?.GetTemplate()
            ?? SelectBest(FallbackTemplates, contentItem)?.GetTemplate()
            ?? HiddenTemplate;
    }

    private static ChatContentTemplate? SelectBest(
        IList<ChatContentTemplate> templates,
        ChatContentItem item)
    {
        ChatContentTemplate? selected = null;
        var highest = 0;

        for (var i = 0; i < templates.Count; i++)
        {
            var template = templates[i];
            if (template is null || !template.When(item))
                continue;

            var priority = template.GetPriority(item);
            if (selected is null || priority > highest)
            {
                selected = template;
                highest = priority;
            }
        }

        return selected;
    }
}
