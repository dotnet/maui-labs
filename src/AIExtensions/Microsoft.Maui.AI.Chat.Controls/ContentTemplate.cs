using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// AI-specific content template bridge. Existing consumers keep matching <see cref="ContentContext"/>
/// while the neutral selector operates on <see cref="ChatContentItem"/>.
/// </summary>
public abstract class ContentTemplate : ChatContentTemplate
{
    /// <inheritdoc />
    public sealed override bool When(ChatContentItem item) =>
        item is ContentContext context && When(context);

    /// <summary>Gets whether this template handles an AI content context.</summary>
    public abstract bool When(ContentContext context);

    /// <inheritdoc />
    public sealed override int GetPriority(ChatContentItem item) =>
        item is ContentContext context
            ? GetPriority(context)
            : int.MinValue;

    internal virtual int GetPriority(ContentContext context) =>
        Priority;

    internal static new View CreateView(
        Type type,
        IServiceProvider? services = null) =>
        ChatContentTemplate.CreateView(type, services);

    internal static T PrepareDataTemplateView<T>(T view)
        where T : View
    {
        if (view is ContentContextView contextView)
        {
            contextView.SetBinding(
                ContentContextView.ContentContextProperty,
                new Binding("."));
        }
        else if (view is ChatContentView contentView)
        {
            contentView.SetBinding(
                ChatContentView.ItemProperty,
                new Binding("."));
        }

        return view;
    }
}
