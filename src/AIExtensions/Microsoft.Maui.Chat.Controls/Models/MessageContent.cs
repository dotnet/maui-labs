namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// Base class for one piece of content inside a <see cref="ConversationMessage"/> (text, media, or a
/// consumer-defined kind).
/// </summary>
/// <remarks>
/// <para>
/// Content is mutated in place — streaming text appends to the same instance rather than replacing
/// it — so views update without the list rebuilding a cell. Derived types call
/// <see cref="RaiseContentChanged"/> after a mutation; <see cref="ContentChanged"/> is the signal the
/// controls listen to.
/// </para>
/// <para>
/// Content must be re-readable: never hold an open <see cref="System.IO.Stream"/>, because a view can
/// render the same content many times as cells are recycled.
/// </para>
/// <para>
/// Instances are single-thread affine and are not thread-safe. Create, read, and mutate them on the
/// UI thread only; <see cref="ContentChanged"/> is raised synchronously on the mutating thread.
/// </para>
/// </remarks>
public abstract class MessageContent : BindableObject
{
    /// <summary>Backing property for <see cref="Presentation"/>.</summary>
    public static readonly BindableProperty PresentationProperty =
        BindableProperty.Create(
            nameof(Presentation),
            typeof(ChatContentPresentation),
            typeof(MessageContent),
            ChatContentPresentation.Bubble,
            propertyChanged: static (bindable, _, _) =>
                ((MessageContent)bindable).RaiseContentChanged());

    /// <summary>Creates a content instance.</summary>
    /// <param name="id">A stable identifier. When <see langword="null"/> or whitespace, a new unique identifier is generated.</param>
    protected MessageContent(string? id = null) =>
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id;

    /// <summary>Gets the stable identifier of this content. Never changes.</summary>
    public string Id { get; }

    /// <summary>
    /// Gets or sets whether this content uses the standard bubble or renders bare inside the standard
    /// message chrome. Defaults to <see cref="ChatContentPresentation.Bubble"/>.
    /// </summary>
    public ChatContentPresentation Presentation
    {
        get => (ChatContentPresentation)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    /// <summary>
    /// Raised after this content changed in place, for example when streamed text was appended.
    /// Handlers run synchronously, in subscription order, on the mutating thread.
    /// </summary>
    public event EventHandler? ContentChanged;

    /// <summary>
    /// Raises <see cref="ContentChanged"/>. Call this from derived types after mutating content in place.
    /// </summary>
    protected void RaiseContentChanged() => ContentChanged?.Invoke(this, EventArgs.Empty);
}
