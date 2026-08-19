using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// AI-specific content template bridge. Existing consumers keep matching <see cref="ContentContext"/>
/// while the neutral selector operates on <see cref="ChatContentItem"/>.
/// </summary>
public abstract class ContentTemplate : ChatContentTemplate
{
    /// <summary>Backing property for <see cref="UseMessageChrome"/>.</summary>
    public static readonly BindableProperty UseMessageChromeProperty =
        BindableProperty.Create(
            nameof(UseMessageChrome),
            typeof(bool),
            typeof(ContentTemplate),
            true,
            propertyChanged: static (bindable, _, _) =>
                ((ContentTemplate)bindable).InvalidateTemplate());

    /// <summary>Backing property for <see cref="Presentation"/>.</summary>
    public static readonly BindableProperty PresentationProperty =
        BindableProperty.Create(
            nameof(Presentation),
            typeof(ChatContentPresentation?),
            typeof(ContentTemplate),
            propertyChanged: static (bindable, _, _) =>
                ((ContentTemplate)bindable).InvalidateTemplate());

    /// <summary>
    /// Gets or sets whether the AI body view uses the provider-neutral message chrome.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool UseMessageChrome
    {
        get => (bool)GetValue(UseMessageChromeProperty);
        set => SetValue(UseMessageChromeProperty, value);
    }

    /// <summary>
    /// Gets or sets an optional bubble-presentation override. A <see langword="null"/> value uses the
    /// mapped <see cref="MessageContent.Presentation"/>.
    /// </summary>
    public ChatContentPresentation? Presentation
    {
        get => (ChatContentPresentation?)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

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

    /// <inheritdoc />
    protected override DataTemplate CreateTemplate()
    {
        var viewType = ViewType
            ?? throw new InvalidOperationException(
                $"{GetType().Name} has no {nameof(ViewType)} set.");
        return CreateMessageTemplate(() => CreateView(viewType));
    }

    /// <summary>Creates a stable template for an AI message body.</summary>
    /// <param name="createView">Creates the body view.</param>
    /// <returns>The message template.</returns>
    protected DataTemplate CreateMessageTemplate(Func<View> createView)
    {
        ArgumentNullException.ThrowIfNull(createView);

        return new DataTemplate(() =>
        {
            var body = PrepareDataTemplateView(createView());
            return UseMessageChrome
                ? WrapInMessageChrome(body, Presentation)
                : body;
        });
    }

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
