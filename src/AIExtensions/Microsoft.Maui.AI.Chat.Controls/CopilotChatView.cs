using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls.Themes;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// A drop-in AI chat control backed by <see cref="AgentContext"/>.
/// </summary>
/// <remarks>
/// The composer, empty state, suggestions, attachments, and virtualized message surface come from
/// <see cref="ChatView"/>. This type only adapts an AI session and keeps the AI-specific content
/// template and appearance aliases used by existing XAML.
/// </remarks>
[ContentProperty(nameof(ContentTemplates))]
public class CopilotChatView : ChatView
{
    /// <summary>Backing property for <see cref="Session"/>.</summary>
    public static readonly BindableProperty SessionProperty =
        BindableProperty.Create(
            nameof(Session),
            typeof(AgentContext),
            typeof(CopilotChatView),
            default(AgentContext),
            propertyChanged: static (bindable, oldValue, newValue) =>
                ((CopilotChatView)bindable).OnSessionChanged(
                    (AgentContext?)oldValue,
                    (AgentContext?)newValue));

    /// <summary>AI-typed content templates retained for source-compatible XAML.</summary>
    public new static readonly BindableProperty ContentTemplatesProperty =
        BindableProperty.Create(
            nameof(ContentTemplates),
            typeof(IList<ContentTemplate>),
            typeof(CopilotChatView),
            defaultValueCreator: static _ => new ObservableCollection<ContentTemplate>(),
            propertyChanged: static (bindable, oldValue, newValue) =>
                ((CopilotChatView)bindable).OnContentTemplatesChanged(
                    oldValue as IList<ContentTemplate>,
                    newValue as IList<ContentTemplate>));

    /// <summary>Backing property for <see cref="UseDefaultContentTemplates"/>.</summary>
    public new static readonly BindableProperty UseDefaultContentTemplatesProperty =
        BindableProperty.Create(
            nameof(UseDefaultContentTemplates),
            typeof(bool),
            typeof(CopilotChatView),
            true,
            propertyChanged: static (bindable, _, value) =>
                ((CopilotChatView)bindable).OnUseDefaultContentTemplatesChanged(
                    (bool)value));

    /// <summary>Backing property for <see cref="ShowAvatars"/>.</summary>
    public static readonly BindableProperty ShowAvatarsProperty =
        BindableProperty.Create(
            nameof(ShowAvatars),
            typeof(bool),
            typeof(CopilotChatView),
            false,
            propertyChanged: OnAppearanceAliasChanged);

    /// <summary>Backing property for <see cref="AvatarSize"/>.</summary>
    public static readonly BindableProperty AvatarSizeProperty =
        BindableProperty.Create(
            nameof(AvatarSize),
            typeof(double),
            typeof(CopilotChatView),
            28.0,
            propertyChanged: OnAppearanceAliasChanged);

    /// <summary>Backing property for <see cref="UserDisplayName"/>.</summary>
    public static readonly BindableProperty UserDisplayNameProperty =
        BindableProperty.Create(
            nameof(UserDisplayName),
            typeof(string),
            typeof(CopilotChatView),
            "You",
            propertyChanged: OnParticipantAliasChanged);

    /// <summary>Backing property for <see cref="AssistantDisplayName"/>.</summary>
    public static readonly BindableProperty AssistantDisplayNameProperty =
        BindableProperty.Create(
            nameof(AssistantDisplayName),
            typeof(string),
            typeof(CopilotChatView),
            "Assistant",
            propertyChanged: OnParticipantAliasChanged);

    /// <summary>Backing property for <see cref="ShowTimestamps"/>.</summary>
    public static readonly BindableProperty ShowTimestampsProperty =
        BindableProperty.Create(
            nameof(ShowTimestamps),
            typeof(bool),
            typeof(CopilotChatView),
            false,
            propertyChanged: OnAppearanceAliasChanged);

    /// <summary>Backing property for <see cref="BubbleCornerRadius"/>.</summary>
    public static readonly BindableProperty BubbleCornerRadiusProperty =
        BindableProperty.Create(
            nameof(BubbleCornerRadius),
            typeof(double),
            typeof(CopilotChatView),
            16.0,
            propertyChanged: OnAppearanceAliasChanged);

    /// <summary>Backing property for <see cref="BubbleStrokeThickness"/>.</summary>
    public static readonly BindableProperty BubbleStrokeThicknessProperty =
        BindableProperty.Create(
            nameof(BubbleStrokeThickness),
            typeof(double),
            typeof(CopilotChatView),
            0.0,
            propertyChanged: OnAppearanceAliasChanged);

    /// <summary>Backing property for <see cref="BubbleStrokeColor"/>.</summary>
    public static readonly BindableProperty BubbleStrokeColorProperty =
        BindableProperty.Create(
            nameof(BubbleStrokeColor),
            typeof(Color),
            typeof(CopilotChatView),
            propertyChanged: OnAppearanceAliasChanged);

    /// <summary>Backing property for <see cref="MaxBubbleWidth"/>.</summary>
    public static readonly BindableProperty MaxBubbleWidthProperty =
        BindableProperty.Create(
            nameof(MaxBubbleWidth),
            typeof(double),
            typeof(CopilotChatView),
            560.0,
            propertyChanged: OnAppearanceAliasChanged);

    /// <summary>Backing property for <see cref="SendButtonBackgroundColor"/>.</summary>
    internal static readonly BindableProperty EffectiveSendButtonBackgroundColorProperty =
        BindableProperty.Create(
            nameof(EffectiveSendButtonBackgroundColor),
            typeof(Color),
            typeof(CopilotChatView));

    internal static readonly BindableProperty EffectiveInputAreaBackgroundColorProperty =
        BindableProperty.Create(
            nameof(EffectiveInputAreaBackgroundColor),
            typeof(Color),
            typeof(CopilotChatView));

    public static readonly BindableProperty SendButtonBackgroundColorProperty =
        BindableProperty.Create(
            nameof(SendButtonBackgroundColor),
            typeof(Color),
            typeof(CopilotChatView),
            propertyChanged: static (bindable, _, value) =>
                ((CopilotChatView)bindable).UpdateEffectiveInputColor(
                    EffectiveSendButtonBackgroundColorProperty,
                    (Color?)value,
                    ChatThemeKeys.SendBackground));

    /// <summary>Backing property for <see cref="InputAreaBackgroundColor"/>.</summary>
    public static readonly BindableProperty InputAreaBackgroundColorProperty =
        BindableProperty.Create(
            nameof(InputAreaBackgroundColor),
            typeof(Color),
            typeof(CopilotChatView),
            propertyChanged: static (bindable, _, value) =>
                ((CopilotChatView)bindable).UpdateEffectiveInputColor(
                    EffectiveInputAreaBackgroundColorProperty,
                    (Color?)value,
                    ChatThemeKeys.InputBackground));

    /// <summary>Backing property for <see cref="InputAreaCornerRadius"/>.</summary>
    public static readonly BindableProperty InputAreaCornerRadiusProperty =
        BindableProperty.Create(
            nameof(InputAreaCornerRadius),
            typeof(double),
            typeof(CopilotChatView),
            14.0);

    private AgentChatConversation? _agentConversation;
    private MessageListView? _messageList;

    /// <summary>Initializes a new AI chat view.</summary>
    public CopilotChatView()
    {
        if (ContentTemplates is INotifyCollectionChanged templates)
            templates.CollectionChanged += OnContentTemplatesCollectionChanged;

        SyncContentTemplates();
        base.UseDefaultContentTemplates = UseDefaultContentTemplates;
        UpdateAppearanceAliases();
        RestoreEffectiveInputColor(
            EffectiveSendButtonBackgroundColorProperty,
            ChatThemeKeys.SendBackground);
        RestoreEffectiveInputColor(
            EffectiveInputAreaBackgroundColorProperty,
            ChatThemeKeys.InputBackground);
        SetDynamicResource(ControlTemplateProperty, ChatThemeKeys.CopilotChatViewTemplate);
    }

    /// <summary>Gets or sets the AI session displayed and driven by the control.</summary>
    public AgentContext? Session
    {
        get => (AgentContext?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>Gets or sets the AI-specific block templates.</summary>
    public new IList<ContentTemplate> ContentTemplates
    {
        get => (IList<ContentTemplate>)GetValue(ContentTemplatesProperty);
        set => SetValue(ContentTemplatesProperty, value);
    }

    /// <summary>Gets or sets whether built-in AI templates are used as fallbacks.</summary>
    public new bool UseDefaultContentTemplates
    {
        get => (bool)GetValue(UseDefaultContentTemplatesProperty);
        set => SetValue(UseDefaultContentTemplatesProperty, value);
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

    /// <summary>Gets or sets the local user display name.</summary>
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

    /// <summary>Gets or sets the message bubble corner radius.</summary>
    public double BubbleCornerRadius
    {
        get => (double)GetValue(BubbleCornerRadiusProperty);
        set => SetValue(BubbleCornerRadiusProperty, value);
    }

    /// <summary>Gets or sets the message bubble stroke thickness.</summary>
    public double BubbleStrokeThickness
    {
        get => (double)GetValue(BubbleStrokeThicknessProperty);
        set => SetValue(BubbleStrokeThicknessProperty, value);
    }

    /// <summary>Gets or sets the message bubble stroke color.</summary>
    public Color? BubbleStrokeColor
    {
        get => (Color?)GetValue(BubbleStrokeColorProperty);
        set => SetValue(BubbleStrokeColorProperty, value);
    }

    /// <summary>Gets or sets the maximum message bubble width.</summary>
    public double MaxBubbleWidth
    {
        get => (double)GetValue(MaxBubbleWidthProperty);
        set => SetValue(MaxBubbleWidthProperty, value);
    }

    /// <summary>Gets or sets an optional send-button color override.</summary>
    public Color? SendButtonBackgroundColor
    {
        get => (Color?)GetValue(SendButtonBackgroundColorProperty);
        set => SetValue(SendButtonBackgroundColorProperty, value);
    }

    /// <summary>Gets or sets an optional composer background color override.</summary>
    public Color? InputAreaBackgroundColor
    {
        get => (Color?)GetValue(InputAreaBackgroundColorProperty);
        set => SetValue(InputAreaBackgroundColorProperty, value);
    }

    /// <summary>Gets or sets the composer corner radius.</summary>
    public double InputAreaCornerRadius
    {
        get => (double)GetValue(InputAreaCornerRadiusProperty);
        set => SetValue(InputAreaCornerRadiusProperty, value);
    }

    /// <summary>Gets the resolved send-button color.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Color? EffectiveSendButtonBackgroundColor =>
        (Color?)GetValue(EffectiveSendButtonBackgroundColorProperty);

    /// <summary>Gets the resolved composer background color.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Color? EffectiveInputAreaBackgroundColor =>
        (Color?)GetValue(EffectiveInputAreaBackgroundColorProperty);

    /// <inheritdoc />
    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent is not null && Application.Current is { } application)
            ChatThemeLoader.EnsureLoaded(application.Resources);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        AttachMessageListPart(FindPart<MessageListView>(MessageListPartName));
    }

    internal void AttachMessageListPart(MessageListView? messageList)
    {
        _messageList = messageList;
        if (_messageList is null)
            return;

        _messageList.Conversation = Conversation;
        ((ChatMessagesView)_messageList).ContentTemplates =
            new ObservableCollection<ChatContentTemplate>(base.ContentTemplates);
        _messageList.UseDefaultContentTemplates = UseDefaultContentTemplates;
        _messageList.Appearance = Appearance;
        _messageList.AutoScrollToLatest = AutoScrollToLatest;
        SyncMessageListAliases();
    }

    internal new void AttachSuggestionsPart(Layout? suggestionsPart) =>
        base.AttachSuggestionsPart(suggestionsPart);

    internal void UpdateWelcomeVisibility()
    {
        // State is maintained by ChatView's conversation and suggestion subscriptions.
    }

    internal Task SendCurrentTextAsync() =>
        SendAsync();

    internal Task PickAttachmentsFromButtonAsync() =>
        PickAttachmentsAsync();

    internal ChatMessage? TakePendingMessage()
    {
        var draft = CreateDraft();
        if (draft.IsEmpty)
            return null;

        var contents = new List<AIContent>();
        if (draft.HasText)
            contents.Add(new TextContent(draft.Text));

        foreach (var attachment in draft.Attachments)
        {
            if (attachment is ChatAttachment agentAttachment)
            {
                contents.Add(agentAttachment.Content);
            }
            else if (attachment.Uri is { } uri)
            {
                contents.Add(new UriContent(uri, attachment.MediaType));
            }
            else
            {
                contents.Add(new DataContent(
                    attachment.Data,
                    attachment.MediaType)
                {
                    Name = attachment.FileName
                });
            }
        }

        Text = string.Empty;
        ClearAttachments();
        return new ChatMessage(ChatRole.User, contents);
    }

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
        if (_messageList is not null)
            _messageList.Conversation = _agentConversation;
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

        if (_messageList is null)
            return;

        var nestedTemplates =
            ((ChatMessagesView)_messageList).ContentTemplates;
        nestedTemplates.Clear();
        foreach (var template in base.ContentTemplates)
            nestedTemplates.Add(template);
    }

    private void OnUseDefaultContentTemplatesChanged(bool useDefaults)
    {
        base.UseDefaultContentTemplates = useDefaults;
        if (_messageList is not null)
            _messageList.UseDefaultContentTemplates = useDefaults;
    }

    private static void OnAppearanceAliasChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        _ = oldValue;
        _ = newValue;
        ((CopilotChatView)bindable).UpdateAppearanceAliases();
    }

    private static void OnParticipantAliasChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        _ = oldValue;
        _ = newValue;
        var view = (CopilotChatView)bindable;
        view.UpdateAppearanceAliases();
        view._agentConversation?.UpdateParticipantNames(
            view.UserDisplayName,
            view.AssistantDisplayName);
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
        SyncMessageListAliases();
    }

    private void SyncMessageListAliases()
    {
        if (_messageList is null)
            return;

        _messageList.ShowAvatars = ShowAvatars;
        _messageList.AvatarSize = AvatarSize;
        _messageList.UserDisplayName = UserDisplayName;
        _messageList.AssistantDisplayName = AssistantDisplayName;
        _messageList.ShowTimestamps = ShowTimestamps;
        _messageList.BubbleCornerRadius = BubbleCornerRadius;
        _messageList.BubbleStrokeThickness = BubbleStrokeThickness;
        _messageList.BubbleStrokeColor = BubbleStrokeColor;
        _messageList.MaxBubbleWidth = MaxBubbleWidth;
    }

    private void UpdateEffectiveInputColor(
        BindableProperty effectiveProperty,
        Color? color,
        string fallbackResourceKey)
    {
        if (color is not null)
        {
            SetValue(effectiveProperty, color);
            return;
        }

        RestoreEffectiveInputColor(effectiveProperty, fallbackResourceKey);
    }

    private void RestoreEffectiveInputColor(
        BindableProperty effectiveProperty,
        string fallbackResourceKey)
    {
        ClearValue(effectiveProperty);
        SetDynamicResource(effectiveProperty, fallbackResourceKey);
    }
}
