namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// Base class for the view that renders one projected row (<see cref="ChatContentItem"/>).
/// </summary>
/// <remarks>
/// <para>
/// The host binds <see cref="Item"/> to the row. Because list cells are recycled, a view must survive
/// being handed a different row at any time: the base class unsubscribes from the previous content,
/// subscribes to the new one, calls <see cref="OnItemChanged"/> so per-cell state can be reset, and then
/// calls <see cref="RefreshContent"/>.
/// </para>
/// <para>
/// While content streams, <see cref="OnContentUpdated"/> is called in place. Views whose visuals are
/// bound to the content need to do nothing; views that build visuals imperatively should override it and
/// update rather than rebuild.
/// </para>
/// <para>Views are single-thread affine: assign <see cref="Item"/> and mutate content on the UI thread only.</para>
/// </remarks>
public abstract class ChatContentView : ContentView
{
    /// <summary>Backing property for <see cref="Item"/>.</summary>
    public static readonly BindableProperty ItemProperty =
        BindableProperty.Create(
            nameof(Item),
            typeof(ChatContentItem),
            typeof(ChatContentView),
            propertyChanged: static (bindable, oldValue, newValue) =>
                ((ChatContentView)bindable).ApplyItem(oldValue as ChatContentItem, newValue as ChatContentItem));

    private ChatContentItem? _subscribedItem;
    private MessageContent? _subscribedContent;

    /// <summary>Gets or sets the row this view renders.</summary>
    public ChatContentItem? Item
    {
        get => (ChatContentItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    /// <summary>Gets the appearance of the current row, falling back to <see cref="ChatAppearance.Default"/>.</summary>
    protected ChatAppearance Appearance => Item?.Appearance ?? ChatAppearance.Default;

    /// <summary>
    /// Called before <see cref="RefreshContent"/> whenever a different row is assigned. Reset any
    /// per-cell state here — a recycled cell must never show remnants of the previous row.
    /// </summary>
    /// <param name="oldItem">The row that was rendered, if any.</param>
    /// <param name="newItem">The row that will be rendered, if any.</param>
    protected virtual void OnItemChanged(ChatContentItem? oldItem, ChatContentItem? newItem)
    {
    }

    /// <summary>Rebuilds the visuals for the current <see cref="Item"/>.</summary>
    protected abstract void RefreshContent();

    /// <summary>
    /// Called when the current content changed in place, for example when streamed text was appended.
    /// The default implementation calls <see cref="RefreshContent"/>.
    /// </summary>
    protected virtual void OnContentUpdated() => RefreshContent();

    /// <summary>
    /// Called when a property on the projected row changes. The default refreshes content changes through
    /// <see cref="OnContentUpdated"/> and performs a full refresh for grouping, appearance, and status changes.
    /// </summary>
    /// <param name="propertyName">The changed property name, or an empty value for all properties.</param>
    protected virtual void OnItemPropertyUpdated(string? propertyName)
    {
        if (propertyName == nameof(ChatContentItem.Content))
            OnContentUpdated();
        else
            RefreshContent();
    }

    private void ApplyItem(ChatContentItem? oldItem, ChatContentItem? newItem)
    {
        Unsubscribe();

        if (newItem is not null)
        {
            _subscribedItem = newItem;
            _subscribedItem.PropertyChanged += OnItemPropertyChanged;
            _subscribedContent = newItem.Content;
            _subscribedContent.ContentChanged += OnContentChanged;
        }

        OnItemChanged(oldItem, newItem);
        RefreshContent();
    }

    private void Unsubscribe()
    {
        if (_subscribedItem is not null)
        {
            _subscribedItem.PropertyChanged -= OnItemPropertyChanged;
            _subscribedItem = null;
        }

        if (_subscribedContent is null)
            return;

        _subscribedContent.ContentChanged -= OnContentChanged;
        _subscribedContent = null;
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnItemPropertyUpdated(e.PropertyName);
    }

    private void OnContentChanged(object? sender, EventArgs e) => OnContentUpdated();
}
