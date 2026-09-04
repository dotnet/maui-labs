namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// A declarative template that matches rows by content type, participant kind, and direction, so common
/// customisations need no subclass.
/// </summary>
/// <remarks>
/// All filters are optional and combine with AND. Each filter that is set also boosts the priority, so a
/// more specific template beats a broader one declared next to it:
/// <code>
/// &lt;chat:GenericChatContentTemplate ContentType="{x:Type chat:MediaMessageContent}"
///                                 ViewType="{x:Type local:MyMediaView}" /&gt;
/// &lt;chat:GenericChatContentTemplate ParticipantKind="Agent"
///                                 ViewType="{x:Type local:AgentBubbleView}" /&gt;
/// </code>
/// </remarks>
public class GenericChatContentTemplate : ChatContentTemplate
{
    /// <summary>Backing property for <see cref="ContentType"/>.</summary>
    public static readonly BindableProperty ContentTypeProperty =
        BindableProperty.Create(nameof(ContentType), typeof(Type), typeof(GenericChatContentTemplate));

    /// <summary>Backing property for <see cref="ParticipantKind"/>.</summary>
    public static readonly BindableProperty ParticipantKindProperty =
        BindableProperty.Create(
            nameof(ParticipantKind),
            typeof(ChatParticipantKind?),
            typeof(GenericChatContentTemplate));

    /// <summary>Backing property for <see cref="IsOutgoing"/>.</summary>
    public static readonly BindableProperty IsOutgoingProperty =
        BindableProperty.Create(nameof(IsOutgoing), typeof(bool?), typeof(GenericChatContentTemplate));

    /// <summary>Backing property for <see cref="UseMessageChrome"/>.</summary>
    public static readonly BindableProperty UseMessageChromeProperty =
        BindableProperty.Create(
            nameof(UseMessageChrome),
            typeof(bool),
            typeof(GenericChatContentTemplate),
            true,
            propertyChanged: static (bindable, _, _) =>
                ((GenericChatContentTemplate)bindable).InvalidateTemplate());

    /// <summary>Backing property for <see cref="Presentation"/>.</summary>
    public static readonly BindableProperty PresentationProperty =
        BindableProperty.Create(
            nameof(Presentation),
            typeof(ChatContentPresentation?),
            typeof(GenericChatContentTemplate),
            propertyChanged: static (bindable, _, _) =>
                ((GenericChatContentTemplate)bindable).InvalidateTemplate());

    /// <summary>Gets or sets the content type filter. Matches the type and its subclasses.</summary>
    public Type? ContentType
    {
        get => (Type?)GetValue(ContentTypeProperty);
        set => SetValue(ContentTypeProperty, value);
    }

    /// <summary>Gets or sets the participant kind filter.</summary>
    public ChatParticipantKind? ParticipantKind
    {
        get => (ChatParticipantKind?)GetValue(ParticipantKindProperty);
        set => SetValue(ParticipantKindProperty, value);
    }

    /// <summary>Gets or sets the direction filter: <see langword="true"/> for outgoing rows, <see langword="false"/> for incoming.</summary>
    public bool? IsOutgoing
    {
        get => (bool?)GetValue(IsOutgoingProperty);
        set => SetValue(IsOutgoingProperty, value);
    }

    /// <summary>
    /// Gets or sets whether a custom body view is wrapped in the standard avatar, participant name,
    /// bubble, timestamp, grouping, and delivery-status chrome. Defaults to <see langword="true"/>.
    /// A <see cref="ChatBubbleView"/> is never wrapped twice.
    /// </summary>
    public bool UseMessageChrome
    {
        get => (bool)GetValue(UseMessageChromeProperty);
        set => SetValue(UseMessageChromeProperty, value);
    }

    /// <summary>
    /// Gets or sets an optional bubble-presentation override. A <see langword="null"/> value uses
    /// <see cref="MessageContent.Presentation"/> from each rendered content instance.
    /// </summary>
    public ChatContentPresentation? Presentation
    {
        get => (ChatContentPresentation?)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    /// <inheritdoc />
    public override bool When(ChatContentItem item)
    {
        if (item is null)
            return false;

        if (ContentType is { } contentType && !contentType.IsInstanceOfType(item.Content))
            return false;

        if (ParticipantKind is { } kind && item.Participant.Kind != kind)
            return false;

        if (IsOutgoing is { } outgoing && item.IsOutgoing != outgoing)
            return false;

        return true;
    }

    /// <inheritdoc />
    public override int GetPriority(ChatContentItem item)
    {
        var boost = 0;

        if (ContentType is not null)
            boost += 100;
        if (ParticipantKind is not null)
            boost += 50;
        if (IsOutgoing is not null)
            boost += 25;

        return base.GetPriority(item) + boost;
    }

    /// <inheritdoc />
    protected override DataTemplate CreateTemplate()
    {
        var viewType = ViewType
            ?? throw new InvalidOperationException(
                $"{GetType().Name} has no {nameof(ViewType)} set. Set one, or override {nameof(CreateTemplate)}.");

        if (!UseMessageChrome)
            return base.CreateTemplate();

        return new DataTemplate(() =>
        {
            var body = CreateView(viewType);
            return WrapInMessageChrome(body, Presentation);
        });
    }
}
