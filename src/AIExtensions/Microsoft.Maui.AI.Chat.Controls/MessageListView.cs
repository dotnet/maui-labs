using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls.Themes;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Displays the content blocks of an <see cref="AgentContext"/> using the neutral,
/// virtualized chat-message projection.
/// </summary>
public class MessageListView : ChatMessagesView
{
    /// <summary>AI-typed content templates retained for source-compatible XAML.</summary>
    public new static readonly BindableProperty ContentTemplatesProperty =
        BindableProperty.Create(
            nameof(ContentTemplates),
            typeof(IList<ContentTemplate>),
            typeof(MessageListView),
            defaultValueCreator: static _ => new ObservableCollection<ContentTemplate>(),
            propertyChanged: static (bindable, oldValue, newValue) =>
                ((MessageListView)bindable).OnContentTemplatesChanged(
                    oldValue as IList<ContentTemplate>,
                    newValue as IList<ContentTemplate>));

    /// <summary>Backing property for <see cref="Session"/>.</summary>
    public static readonly BindableProperty SessionProperty =
        BindableProperty.Create(
            nameof(Session),
            typeof(AgentContext),
            typeof(MessageListView),
            default(AgentContext),
            propertyChanged: static (bindable, oldValue, newValue) =>
                ((MessageListView)bindable).OnSessionChanged(
                    (AgentContext?)oldValue,
                    (AgentContext?)newValue));

    /// <summary>Backing property for <see cref="ShowAvatars"/>.</summary>
    public static readonly BindableProperty ShowAvatarsProperty =
        BindableProperty.Create(
            nameof(ShowAvatars),
            typeof(bool),
            typeof(MessageListView),
            false,
            propertyChanged: OnAppearanceAliasChanged);

    /// <summary>Backing property for <see cref="AvatarSize"/>.</summary>
    public static readonly BindableProperty AvatarSizeProperty =
        BindableProperty.Create(
            nameof(AvatarSize),
            typeof(double),
            typeof(MessageListView),
            28.0,
            propertyChanged: OnAppearanceAliasChanged);

    /// <summary>Backing property for <see cref="UserDisplayName"/>.</summary>
    public static readonly BindableProperty UserDisplayNameProperty =
        BindableProperty.Create(
            nameof(UserDisplayName),
            typeof(string),
            typeof(MessageListView),
            "You",
            propertyChanged: OnParticipantAliasChanged);

    /// <summary>Backing property for <see cref="AssistantDisplayName"/>.</summary>
    public static readonly BindableProperty AssistantDisplayNameProperty =
        BindableProperty.Create(
            nameof(AssistantDisplayName),
            typeof(string),
            typeof(MessageListView),
            "Assistant",
            propertyChanged: OnParticipantAliasChanged);

    /// <summary>Backing property for <see cref="ShowTimestamps"/>.</summary>
    public static readonly BindableProperty ShowTimestampsProperty =
        BindableProperty.Create(
            nameof(ShowTimestamps),
            typeof(bool),
            typeof(MessageListView),
            false,
            propertyChanged: OnAppearanceAliasChanged);

    /// <summary>Backing property for <see cref="BubbleCornerRadius"/>.</summary>
    public static readonly BindableProperty BubbleCornerRadiusProperty =
        BindableProperty.Create(
            nameof(BubbleCornerRadius),
            typeof(double),
            typeof(MessageListView),
            16.0,
            propertyChanged: OnAppearanceAliasChanged);

    /// <summary>Backing property for <see cref="BubbleStrokeThickness"/>.</summary>
    public static readonly BindableProperty BubbleStrokeThicknessProperty =
        BindableProperty.Create(
            nameof(BubbleStrokeThickness),
            typeof(double),
            typeof(MessageListView),
            0.0,
            propertyChanged: OnAppearanceAliasChanged);

    /// <summary>Backing property for <see cref="BubbleStrokeColor"/>.</summary>
    public static readonly BindableProperty BubbleStrokeColorProperty =
        BindableProperty.Create(
            nameof(BubbleStrokeColor),
            typeof(Color),
            typeof(MessageListView),
            propertyChanged: OnAppearanceAliasChanged);

    /// <summary>Backing property for <see cref="MaxBubbleWidth"/>.</summary>
    public static readonly BindableProperty MaxBubbleWidthProperty =
        BindableProperty.Create(
            nameof(MaxBubbleWidth),
            typeof(double),
            typeof(MessageListView),
            340.0,
            propertyChanged: OnAppearanceAliasChanged);

    private static readonly BindablePropertyKey ItemsPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(Items),
            typeof(ReadOnlyObservableCollection<ContentContext>),
            typeof(MessageListView),
            default(ReadOnlyObservableCollection<ContentContext>));

    /// <summary>Backing property for <see cref="Items"/>.</summary>
    public new static readonly BindableProperty ItemsProperty =
        ItemsPropertyKey.BindableProperty;

    private AgentChatConversation? _agentConversation;
    private readonly ObservableCollection<ContentContext> _typedItems = [];

    /// <summary>Initializes a new message list.</summary>
    public MessageListView()
    {
        SetValue(
            ItemsPropertyKey,
            new ReadOnlyObservableCollection<ContentContext>(_typedItems));
        ((INotifyCollectionChanged)base.Items).CollectionChanged +=
            OnProjectedItemsChanged;

        if (ContentTemplates is INotifyCollectionChanged templates)
            templates.CollectionChanged += OnContentTemplatesCollectionChanged;

        SyncContentTemplates();
        SetDynamicResource(ControlTemplateProperty, ChatThemeKeys.MessageListViewTemplate);
        SetDynamicResource(MaxBubbleWidthProperty, ChatThemeKeys.BubbleMaxWidth);
        UpdateAppearanceAliases();
    }

    /// <summary>Gets or sets the AI agent session to display.</summary>
    public AgentContext? Session
    {
        get => (AgentContext?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>Gets the AI-typed view of the neutral projected rows.</summary>
    public new ReadOnlyObservableCollection<ContentContext> Items =>
        (ReadOnlyObservableCollection<ContentContext>)GetValue(ItemsProperty);

    /// <summary>Gets or sets the AI-specific block templates.</summary>
    public new IList<ContentTemplate> ContentTemplates
    {
        get => (IList<ContentTemplate>)GetValue(ContentTemplatesProperty);
        set => SetValue(ContentTemplatesProperty, value);
    }

    /// <summary>Gets or sets whether participant avatars are shown.</summary>
    public bool ShowAvatars
    {
        get => (bool)GetValue(ShowAvatarsProperty);
        set => SetValue(ShowAvatarsProperty, value);
    }

    /// <summary>Gets or sets the participant avatar size.</summary>
    public double AvatarSize
    {
        get => (double)GetValue(AvatarSizeProperty);
        set => SetValue(AvatarSizeProperty, value);
    }

    /// <summary>Gets or sets the user display name.</summary>
    public string UserDisplayName
    {
        get => (string)GetValue(UserDisplayNameProperty);
        set => SetValue(UserDisplayNameProperty, value);
    }

    /// <summary>Gets or sets the assistant display name.</summary>
    public string AssistantDisplayName
    {
        get => (string)GetValue(AssistantDisplayNameProperty);
        set => SetValue(AssistantDisplayNameProperty, value);
    }

    /// <summary>Gets or sets whether message timestamps are shown.</summary>
    public bool ShowTimestamps
    {
        get => (bool)GetValue(ShowTimestampsProperty);
        set => SetValue(ShowTimestampsProperty, value);
    }

    /// <summary>Gets or sets the bubble corner radius.</summary>
    public double BubbleCornerRadius
    {
        get => (double)GetValue(BubbleCornerRadiusProperty);
        set => SetValue(BubbleCornerRadiusProperty, value);
    }

    /// <summary>Gets or sets the bubble stroke thickness.</summary>
    public double BubbleStrokeThickness
    {
        get => (double)GetValue(BubbleStrokeThicknessProperty);
        set => SetValue(BubbleStrokeThicknessProperty, value);
    }

    /// <summary>Gets or sets the bubble stroke color.</summary>
    public Color? BubbleStrokeColor
    {
        get => (Color?)GetValue(BubbleStrokeColorProperty);
        set => SetValue(BubbleStrokeColorProperty, value);
    }

    /// <summary>Gets or sets the maximum bubble width.</summary>
    public double MaxBubbleWidth
    {
        get => (double)GetValue(MaxBubbleWidthProperty);
        set => SetValue(MaxBubbleWidthProperty, value);
    }

    /// <inheritdoc />
    protected override ChatContentItem CreateItem(
        ConversationMessage message,
        MessageContent content,
        ChatConversation? conversation,
        ChatAppearance appearance)
    {
        var agentConversation = _agentConversation ?? conversation as AgentChatConversation;
        if (agentConversation is not null &&
            content is AgentBlockContent blockContent)
        {
            return new ContentContext(
                agentConversation.Session,
                message,
                blockContent,
                agentConversation,
                appearance,
                this);
        }

        return base.CreateItem(message, content, conversation, appearance);
    }

    /// <inheritdoc />
    protected override ChatContentTemplateSelector CreateTemplateSelector()
    {
        var fallbacks = new ContentTemplate[]
        {
            new RichTextContentTemplate(),
            new TextContentTemplate
            {
                Role = ChatRole.User.Value
            },
            new TextContentTemplate
            {
                Role = ChatRole.Assistant.Value
            },
            new ToolApprovalTemplate(),
            new UIActionContentTemplate(),
            new ReasoningContentTemplate(),
            new MediaContentTemplate(),
            new ThinkingContentTemplate(),
            new ErrorContentTemplate()
        };

        var selector = new ChatContentTemplateSelector();
        foreach (var template in base.ContentTemplates)
            selector.Templates.Add(template);
        if (UseDefaultContentTemplates)
        {
            foreach (var fallback in fallbacks)
                selector.FallbackTemplates.Add(fallback);
        }
        return selector;
    }

    internal ChatContentTemplateSelector CreateAiTemplateSelector() =>
        CreateTemplateSelector();

    private void OnSessionChanged(
        AgentContext? oldSession,
        AgentContext? newSession)
    {
        _ = oldSession;
        _agentConversation?.Dispose();
        _agentConversation = newSession is null
            ? null
            : new AgentChatConversation(newSession);
        _agentConversation?.UpdateParticipantNames(
            UserDisplayName,
            AssistantDisplayName);
        Conversation = _agentConversation;
    }

    private static void OnAppearanceAliasChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        _ = oldValue;
        _ = newValue;
        ((MessageListView)bindable).UpdateAppearanceAliases();
    }

    private static void OnParticipantAliasChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        _ = oldValue;
        _ = newValue;
        var list = (MessageListView)bindable;
        list.UpdateAppearanceAliases();
        list._agentConversation?.UpdateParticipantNames(
            list.UserDisplayName,
            list.AssistantDisplayName);
    }

    private void UpdateAppearanceAliases()
    {
        Appearance.ShowAvatars = ShowAvatars;
        Appearance.AvatarSize = AvatarSize;
        Appearance.ShowTimestamps = ShowTimestamps;
        Appearance.BubbleCornerRadius = BubbleCornerRadius;
        Appearance.BubbleStrokeThickness = BubbleStrokeThickness;
        Appearance.BubbleStrokeColor = BubbleStrokeColor;
        Appearance.MaxBubbleWidth = MaxBubbleWidth;
    }

    private void OnContentTemplatesChanged(
        IList<ContentTemplate>? oldTemplates,
        IList<ContentTemplate>? newTemplates)
    {
        if (oldTemplates is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= OnContentTemplatesCollectionChanged;
        if (newTemplates is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += OnContentTemplatesCollectionChanged;
        SyncContentTemplates();
    }

    private void OnContentTemplatesCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        SyncContentTemplates();
    }

    private void SyncContentTemplates()
    {
        base.ContentTemplates.Clear();
        foreach (var template in ContentTemplates)
            base.ContentTemplates.Add(template);
    }

    private void OnProjectedItemsChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        _ = sender;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                for (var i = 0; i < e.NewItems!.Count; i++)
                {
                    if (e.NewItems[i] is not ContentContext context)
                        continue;

                    var baseIndex = e.NewStartingIndex + i;
                    var typedIndex = 0;
                    for (var itemIndex = 0; itemIndex < baseIndex; itemIndex++)
                    {
                        if (base.Items[itemIndex] is ContentContext)
                            typedIndex++;
                    }
                    _typedItems.Insert(typedIndex, context);
                }
                break;

            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
                if (e.OldItems is not null)
                {
                    foreach (var item in e.OldItems)
                    {
                        if (item is ContentContext context)
                        {
                            context.Dispose();
                            _typedItems.Remove(context);
                        }
                    }
                }
                if (e.Action == NotifyCollectionChangedAction.Replace)
                    RebuildTypedItems();
                break;

            case NotifyCollectionChangedAction.Move:
                RebuildTypedItems();
                break;

            case NotifyCollectionChangedAction.Reset:
                foreach (var context in _typedItems)
                    context.Dispose();
                RebuildTypedItems();
                break;
        }
    }

    private void RebuildTypedItems()
    {
        _typedItems.Clear();
        foreach (var item in base.Items)
        {
            if (item is ContentContext context)
                _typedItems.Add(context);
        }
    }
}
