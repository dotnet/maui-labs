using Microsoft.Maui.Controls.Xaml;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Base class for a view that renders one <see cref="ContentContext"/> (a wrapped
/// <see cref="ContentBlock"/>). Override <c>RefreshFromContentContext</c> to
/// (re)build the UI as the block streams or changes.
/// </summary>
/// <remarks>A view is chosen per block by a <see cref="ContentTemplate"/> via the <see cref="ContentTemplateSelector"/>.</remarks>
[ContentProperty(nameof(Content))]
public abstract class ContentContextView : ContentView, IContentContextAware
{
    public static readonly BindableProperty ContentContextProperty =
        BindableProperty.Create(
            nameof(ContentContext),
            typeof(ContentContext),
            typeof(ContentContextView),
            default(ContentContext),
            propertyChanged: OnContentContextChanged);

    public ContentContext? ContentContext
    {
        get => (ContentContext?)GetValue(ContentContextProperty);
        set => SetValue(ContentContextProperty, value);
    }

    public void ApplyContentContext(ContentContext context) => ContentContext = context;

    private static void OnContentContextChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var view = (ContentContextView)bindable;

        view.OnContentContextAssigned((ContentContext?)oldValue, (ContentContext?)newValue);
        view.RefreshFromContentContext();
    }

    protected virtual void OnContentContextAssigned(ContentContext? oldContext, ContentContext? newContext)
    {
    }

    protected abstract void RefreshFromContentContext();
}
