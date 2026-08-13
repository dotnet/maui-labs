using Microsoft.Maui.Chat.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// AI-specific view bridge over <see cref="ChatContentView"/>. Existing custom views continue to
/// receive <see cref="ContentContext"/> and refresh hooks.
/// </summary>
[ContentProperty(nameof(Content))]
public abstract class ContentContextView : ChatContentView, IContentContextAware
{
    /// <summary>Backing property for <see cref="ContentContext"/>.</summary>
    public static readonly BindableProperty ContentContextProperty =
        BindableProperty.Create(
            nameof(ContentContext),
            typeof(ContentContext),
            typeof(ContentContextView),
            default(ContentContext),
            propertyChanged: static (bindable, _, newValue) =>
            {
                var view = (ContentContextView)bindable;
                if (!ReferenceEquals(view.Item, newValue))
                    view.Item = (ContentContext?)newValue;
            });

    /// <summary>Gets or sets the AI content context.</summary>
    public ContentContext? ContentContext
    {
        get => (ContentContext?)GetValue(ContentContextProperty);
        set => SetValue(ContentContextProperty, value);
    }

    /// <summary>Assigns an AI content context.</summary>
    public void ApplyContentContext(ContentContext context) =>
        ContentContext = context;

    /// <inheritdoc />
    protected sealed override void OnItemChanged(
        ChatContentItem? oldItem,
        ChatContentItem? newItem)
    {
        var oldContext = oldItem as ContentContext;
        var newContext = newItem as ContentContext;
        if (!ReferenceEquals(ContentContext, newContext))
            SetValue(ContentContextProperty, newContext);
        OnContentContextAssigned(oldContext, newContext);
    }

    /// <summary>Called when a recycled cell receives a different AI context.</summary>
    protected virtual void OnContentContextAssigned(
        ContentContext? oldContext,
        ContentContext? newContext)
    {
    }

    /// <inheritdoc />
    protected sealed override void RefreshContent() =>
        RefreshFromContentContext();

    /// <inheritdoc />
    protected sealed override void OnItemPropertyUpdated(string? propertyName)
    {
        // ContentContext relays its block-derived properties for XAML bindings. The underlying
        // AgentBlockContent raises ContentChanged in the same call, which is the one refresh this
        // imperative compatibility view needs.
        if (string.IsNullOrEmpty(propertyName))
            return;

        base.OnItemPropertyUpdated(propertyName);
    }

    /// <inheritdoc />
    protected override void OnContentUpdated() =>
        RefreshFromContentContext();

    /// <summary>Refreshes the view from <see cref="ContentContext"/>.</summary>
    protected abstract void RefreshFromContentContext();
}
