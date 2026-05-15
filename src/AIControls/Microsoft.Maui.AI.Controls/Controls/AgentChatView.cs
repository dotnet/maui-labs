using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Controls.Templates;
using Microsoft.Maui.AI.Controls.Themes;

namespace Microsoft.Maui.AI.Controls.Controls;

/// <summary>
/// Drop-in MAUI chat control backed by <see cref="IAgentSession"/>.
/// <para>
/// The control is a <see cref="ContentView"/> with a default <see cref="ControlTemplate"/>
/// defined in <c>DefaultTheme.xaml</c>. Every sub-component (message bubbles, input area,
/// suggestion chips) is replaceable via DataTemplate BindableProperties.
/// The entire visual tree can be replaced by setting <see cref="ControlTemplate"/>.
/// </para>
/// <para>
/// The control never touches <see cref="BindableObject.BindingContext"/>. All internal bindings
/// use <c>{Binding Source={RelativeSource TemplatedParent}}</c> inside the ControlTemplate.
/// </para>
/// </summary>
public class AgentChatView : ContentView
{
    private CollectionView? _messagesView;
    private Layout? _pendingLayout;
    private Editor? _inputEditor;
    private Button? _sendButton;
    private Layout? _suggestionsLayout;

    public AgentChatView()
    {
        var theme = new DefaultThemeResourceDictionary();
        Resources.MergedDictionaries.Add(theme);
        SetDynamicResource(ControlTemplateProperty, "CopilotChatViewDefaultTemplate");

        SetDynamicResource(UserMessageTemplateProperty, "CopilotDefaultUserMessageTemplate");
        SetDynamicResource(AssistantMessageTemplateProperty, "CopilotDefaultAssistantMessageTemplate");
        SetDynamicResource(ToolMessageTemplateProperty, "CopilotDefaultToolMessageTemplate");
        SetDynamicResource(SystemMessageTemplateProperty, "CopilotDefaultSystemMessageTemplate");
        SetDynamicResource(ErrorMessageTemplateProperty, "CopilotDefaultErrorMessageTemplate");
        SetDynamicResource(SuggestionItemTemplateProperty, "CopilotDefaultSuggestionItemTemplate");

        InternalSendCommand = new Command(async () => await HandleSendAsync());
        InternalSuggestionCommand = new Command<Suggestion>(async suggestion =>
        {
            var text = suggestion.Message ?? suggestion.Text;
            InputText = text;
            await HandleSendAsync();
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  SESSION
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty SessionProperty =
        BindableProperty.Create(nameof(Session), typeof(IAgentSession), typeof(AgentChatView),
            propertyChanged: OnSessionChanged);

    public IAgentSession? Session
    {
        get => (IAgentSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  STATE
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty MessagesProperty =
        BindableProperty.Create(nameof(Messages), typeof(ObservableCollection<ChatMessageViewModel>),
            typeof(AgentChatView));

    public ObservableCollection<ChatMessageViewModel>? Messages
    {
        get => (ObservableCollection<ChatMessageViewModel>?)GetValue(MessagesProperty);
        private set => SetValue(MessagesProperty, value);
    }

    public static readonly BindableProperty IsBusyProperty =
        BindableProperty.Create(nameof(IsBusy), typeof(bool), typeof(AgentChatView), false);

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        private set => SetValue(IsBusyProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  INPUT
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty InputTextProperty =
        BindableProperty.Create(nameof(InputText), typeof(string), typeof(AgentChatView), "",
            BindingMode.TwoWay);

    public string InputText
    {
        get => (string)GetValue(InputTextProperty);
        set => SetValue(InputTextProperty, value);
    }

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(AgentChatView), "Ask anything...",
            propertyChanged: (b, _, v) => { if (b is AgentChatView c && c._inputEditor is not null) c._inputEditor.Placeholder = v as string; });

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly BindableProperty SuggestionPromptsProperty =
        BindableProperty.Create(nameof(SuggestionPrompts), typeof(IList<Suggestion>), typeof(AgentChatView));

    public IList<Suggestion>? SuggestionPrompts
    {
        get => (IList<Suggestion>?)GetValue(SuggestionPromptsProperty);
        set => SetValue(SuggestionPromptsProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  TEMPLATES
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty UserMessageTemplateProperty =
        BindableProperty.Create(nameof(UserMessageTemplate), typeof(DataTemplate), typeof(AgentChatView),
            propertyChanged: OnMessageTemplateChanged);

    public DataTemplate? UserMessageTemplate
    {
        get => (DataTemplate?)GetValue(UserMessageTemplateProperty);
        set => SetValue(UserMessageTemplateProperty, value);
    }

    public static readonly BindableProperty AssistantMessageTemplateProperty =
        BindableProperty.Create(nameof(AssistantMessageTemplate), typeof(DataTemplate), typeof(AgentChatView),
            propertyChanged: OnMessageTemplateChanged);

    public DataTemplate? AssistantMessageTemplate
    {
        get => (DataTemplate?)GetValue(AssistantMessageTemplateProperty);
        set => SetValue(AssistantMessageTemplateProperty, value);
    }

    public static readonly BindableProperty ToolMessageTemplateProperty =
        BindableProperty.Create(nameof(ToolMessageTemplate), typeof(DataTemplate), typeof(AgentChatView),
            propertyChanged: OnMessageTemplateChanged);

    public DataTemplate? ToolMessageTemplate
    {
        get => (DataTemplate?)GetValue(ToolMessageTemplateProperty);
        set => SetValue(ToolMessageTemplateProperty, value);
    }

    public static readonly BindableProperty SystemMessageTemplateProperty =
        BindableProperty.Create(nameof(SystemMessageTemplate), typeof(DataTemplate), typeof(AgentChatView),
            propertyChanged: OnMessageTemplateChanged);

    public DataTemplate? SystemMessageTemplate
    {
        get => (DataTemplate?)GetValue(SystemMessageTemplateProperty);
        set => SetValue(SystemMessageTemplateProperty, value);
    }

    public static readonly BindableProperty ErrorMessageTemplateProperty =
        BindableProperty.Create(nameof(ErrorMessageTemplate), typeof(DataTemplate), typeof(AgentChatView),
            propertyChanged: OnMessageTemplateChanged);

    public DataTemplate? ErrorMessageTemplate
    {
        get => (DataTemplate?)GetValue(ErrorMessageTemplateProperty);
        set => SetValue(ErrorMessageTemplateProperty, value);
    }

    public static readonly BindableProperty SuggestionItemTemplateProperty =
        BindableProperty.Create(nameof(SuggestionItemTemplate), typeof(DataTemplate), typeof(AgentChatView));

    public DataTemplate? SuggestionItemTemplate
    {
        get => (DataTemplate?)GetValue(SuggestionItemTemplateProperty);
        set => SetValue(SuggestionItemTemplateProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  APPEARANCE
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty WelcomeTitleProperty =
        BindableProperty.Create(nameof(WelcomeTitle), typeof(string), typeof(AgentChatView), "Welcome");

    public string WelcomeTitle
    {
        get => (string)GetValue(WelcomeTitleProperty);
        set => SetValue(WelcomeTitleProperty, value);
    }

    public static readonly BindableProperty WelcomeMessageProperty =
        BindableProperty.Create(nameof(WelcomeMessage), typeof(string), typeof(AgentChatView),
            "Ask me anything, or tap a suggestion to get started.");

    public string WelcomeMessage
    {
        get => (string)GetValue(WelcomeMessageProperty);
        set => SetValue(WelcomeMessageProperty, value);
    }

    public static readonly BindableProperty WelcomeIconProperty =
        BindableProperty.Create(nameof(WelcomeIcon), typeof(string), typeof(AgentChatView), "💬");

    public string WelcomeIcon
    {
        get => (string)GetValue(WelcomeIconProperty);
        set => SetValue(WelcomeIconProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  AVATARS & IDENTITY
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty ShowAvatarsProperty =
        BindableProperty.Create(nameof(ShowAvatars), typeof(bool), typeof(AgentChatView), true,
            propertyChanged: (b, _, _) => (b as AgentChatView)?.RedecorateAllMessages());

    public bool ShowAvatars
    {
        get => (bool)GetValue(ShowAvatarsProperty);
        set => SetValue(ShowAvatarsProperty, value);
    }

    public static readonly BindableProperty AvatarSizeProperty =
        BindableProperty.Create(nameof(AvatarSize), typeof(double), typeof(AgentChatView), 28.0);

    public double AvatarSize
    {
        get => (double)GetValue(AvatarSizeProperty);
        set => SetValue(AvatarSizeProperty, value);
    }

    public static readonly BindableProperty UserDisplayNameProperty =
        BindableProperty.Create(nameof(UserDisplayName), typeof(string), typeof(AgentChatView), "You");

    public string UserDisplayName
    {
        get => (string)GetValue(UserDisplayNameProperty);
        set => SetValue(UserDisplayNameProperty, value);
    }

    public static readonly BindableProperty AssistantDisplayNameProperty =
        BindableProperty.Create(nameof(AssistantDisplayName), typeof(string), typeof(AgentChatView), "Assistant");

    public string AssistantDisplayName
    {
        get => (string)GetValue(AssistantDisplayNameProperty);
        set => SetValue(AssistantDisplayNameProperty, value);
    }

    public static readonly BindableProperty UserAvatarSourceProperty =
        BindableProperty.Create(nameof(UserAvatarSource), typeof(ImageSource), typeof(AgentChatView));

    public ImageSource? UserAvatarSource
    {
        get => (ImageSource?)GetValue(UserAvatarSourceProperty);
        set => SetValue(UserAvatarSourceProperty, value);
    }

    public static readonly BindableProperty AssistantAvatarSourceProperty =
        BindableProperty.Create(nameof(AssistantAvatarSource), typeof(ImageSource), typeof(AgentChatView));

    public ImageSource? AssistantAvatarSource
    {
        get => (ImageSource?)GetValue(AssistantAvatarSourceProperty);
        set => SetValue(AssistantAvatarSourceProperty, value);
    }

    public static readonly BindableProperty UserAvatarTextProperty =
        BindableProperty.Create(nameof(UserAvatarText), typeof(string), typeof(AgentChatView), "You",
            propertyChanged: (b, _, _) => (b as AgentChatView)?.RedecorateAllMessages());

    public string UserAvatarText
    {
        get => (string)GetValue(UserAvatarTextProperty);
        set => SetValue(UserAvatarTextProperty, value);
    }

    public static readonly BindableProperty AssistantAvatarTextProperty =
        BindableProperty.Create(nameof(AssistantAvatarText), typeof(string), typeof(AgentChatView), "AI",
            propertyChanged: (b, _, _) => (b as AgentChatView)?.RedecorateAllMessages());

    public string AssistantAvatarText
    {
        get => (string)GetValue(AssistantAvatarTextProperty);
        set => SetValue(AssistantAvatarTextProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  TIMESTAMPS & DISPLAY
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty ShowTimestampsProperty =
        BindableProperty.Create(nameof(ShowTimestamps), typeof(bool), typeof(AgentChatView), false,
            propertyChanged: (b, _, _) => (b as AgentChatView)?.RedecorateAllMessages());

    public bool ShowTimestamps
    {
        get => (bool)GetValue(ShowTimestampsProperty);
        set => SetValue(ShowTimestampsProperty, value);
    }

    public static readonly BindableProperty ShowToolMessagesProperty =
        BindableProperty.Create(nameof(ShowToolMessages), typeof(bool), typeof(AgentChatView), true);

    public bool ShowToolMessages
    {
        get => (bool)GetValue(ShowToolMessagesProperty);
        set => SetValue(ShowToolMessagesProperty, value);
    }

    public static readonly BindableProperty BubbleCornerRadiusProperty =
        BindableProperty.Create(nameof(BubbleCornerRadius), typeof(double), typeof(AgentChatView), 12.0);

    public double BubbleCornerRadius
    {
        get => (double)GetValue(BubbleCornerRadiusProperty);
        set => SetValue(BubbleCornerRadiusProperty, value);
    }

    public static readonly BindableProperty BubbleStrokeThicknessProperty =
        BindableProperty.Create(nameof(BubbleStrokeThickness), typeof(double), typeof(AgentChatView), 0.0);

    public double BubbleStrokeThickness
    {
        get => (double)GetValue(BubbleStrokeThicknessProperty);
        set => SetValue(BubbleStrokeThicknessProperty, value);
    }

    public static readonly BindableProperty ShowReasoningProperty =
        BindableProperty.Create(nameof(ShowReasoning), typeof(bool), typeof(AgentChatView), true);

    public bool ShowReasoning
    {
        get => (bool)GetValue(ShowReasoningProperty);
        set => SetValue(ShowReasoningProperty, value);
    }

    public static readonly BindableProperty ShowNewChatButtonProperty =
        BindableProperty.Create(nameof(ShowNewChatButton), typeof(bool), typeof(AgentChatView), false);

    /// <summary>Whether to display a "New Chat" button that calls <see cref="ClearMessages"/>.</summary>
    public bool ShowNewChatButton
    {
        get => (bool)GetValue(ShowNewChatButtonProperty);
        set => SetValue(ShowNewChatButtonProperty, value);
    }

    public static readonly BindableProperty CustomContentTemplateSelectorProperty =
        BindableProperty.Create(nameof(CustomContentTemplateSelector), typeof(ContentTemplateSelector), typeof(AgentChatView));

    /// <summary>
    /// Optional <see cref="ContentTemplateSelector"/> for rendering rich inline content
    /// (weather cards, plan cards, etc.) based on <see cref="AIContent"/> type or function name.
    /// When set, messages that contain matching content types will use these templates
    /// instead of the default text bubble.
    /// </summary>
    public ContentTemplateSelector? CustomContentTemplateSelector
    {
        get => (ContentTemplateSelector?)GetValue(CustomContentTemplateSelectorProperty);
        set => SetValue(CustomContentTemplateSelectorProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  LOCALIZABLE TEXT
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty SendButtonTextProperty =
        BindableProperty.Create(nameof(SendButtonText), typeof(string), typeof(AgentChatView), "Send",
            propertyChanged: (b, _, v) => { if (b is AgentChatView c && c._sendButton is not null) c._sendButton.Text = v as string; });

    public string SendButtonText
    {
        get => (string)GetValue(SendButtonTextProperty);
        set => SetValue(SendButtonTextProperty, value);
    }

    public static readonly BindableProperty TypingIndicatorTextProperty =
        BindableProperty.Create(nameof(TypingIndicatorText), typeof(string), typeof(AgentChatView), "Thinking…");

    public string TypingIndicatorText
    {
        get => (string)GetValue(TypingIndicatorTextProperty);
        set => SetValue(TypingIndicatorTextProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  INTERNAL COMMANDS (for templates to bind to)
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty InternalSendCommandProperty =
        BindableProperty.Create(nameof(InternalSendCommand), typeof(ICommand), typeof(AgentChatView));

    public ICommand InternalSendCommand
    {
        get => (ICommand)GetValue(InternalSendCommandProperty);
        private set => SetValue(InternalSendCommandProperty, value);
    }

    public static readonly BindableProperty InternalSuggestionCommandProperty =
        BindableProperty.Create(nameof(InternalSuggestionCommand), typeof(ICommand), typeof(AgentChatView));

    public ICommand InternalSuggestionCommand
    {
        get => (ICommand)GetValue(InternalSuggestionCommandProperty);
        private set => SetValue(InternalSuggestionCommandProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  PUBLIC METHODS
    // ══════════════════════════════════════════════════════════════

    /// <summary>Send a message programmatically.</summary>
    public Task SendMessageAsync(string text)
    {
        InputText = text;
        return HandleSendAsync();
    }

    /// <summary>Clear all messages and reset the session.</summary>
    public void ClearMessages()
    {
        Session?.Reset();
    }

    // ══════════════════════════════════════════════════════════════
    //  TEMPLATE WIRING
    // ══════════════════════════════════════════════════════════════

    private static void OnMessageTemplateChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AgentChatView view)
            view.UpdateMessageSelector();
    }

    private void UpdateMessageSelector()
    {
        if (_messagesView is null) return;
        _messagesView.ItemTemplate = new ChatMessageTemplateSelector
        {
            UserTemplate = UserMessageTemplate,
            AssistantTemplate = AssistantMessageTemplate,
            ToolTemplate = ToolMessageTemplate,
            SystemTemplate = SystemMessageTemplate,
            ErrorTemplate = ErrorMessageTemplate,
        };
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Unwire previous
        if (_sendButton is not null) _sendButton.Clicked -= OnSendClicked;
        if (_inputEditor is not null) _inputEditor.Completed -= OnInputCompleted;

        // Find PART_ elements
        _messagesView = GetTemplateChild("PART_Messages") as CollectionView;
        _pendingLayout = GetTemplateChild("PART_Pending") as Layout;
        _inputEditor = GetTemplateChild("PART_Input") as Editor;
        _sendButton = GetTemplateChild("PART_Send") as Button;

        // Wire suggestion chips from code (ControlTemplate boundary prevents XAML binding)
        if (_suggestionsLayout is not null) _suggestionsLayout.ChildAdded -= OnSuggestionChildAdded;
        _suggestionsLayout = GetTemplateChild("PART_Suggestions") as Layout;
        if (_suggestionsLayout is not null)
        {
            _suggestionsLayout.ChildAdded += OnSuggestionChildAdded;
            foreach (var child in _suggestionsLayout.Children)
                WireSuggestionTap(child as View);
        }

        // Wire message selector from code (CLR properties can't be bound in XAML ControlTemplate)
        UpdateMessageSelector();

        // Wire events
        if (_sendButton is not null) _sendButton.Clicked += OnSendClicked;
        if (_inputEditor is not null) _inputEditor.Completed += OnInputCompleted;

        // Sync properties that template bindings may not propagate correctly
        SyncTemplateProperties();

        // Bind to session if already set
        BindToSession(Session);
    }

    private void SyncTemplateProperties()
    {
        if (_inputEditor is not null)
            _inputEditor.Placeholder = Placeholder;
        if (_sendButton is not null)
            _sendButton.Text = SendButtonText;
    }

    private void OnSendClicked(object? sender, EventArgs e) => _ = HandleSendAsync();
    private void OnInputCompleted(object? sender, EventArgs e) => _ = HandleSendAsync();

    private void OnSuggestionChildAdded(object? sender, ElementEventArgs e)
        => WireSuggestionTap(e.Element as View);

    private void WireSuggestionTap(View? view)
    {
        if (view is null) return;

        if (view is Button button)
        {
            button.Clicked += (s, e) =>
            {
                if (button.BindingContext is Suggestion suggestion)
                    InternalSuggestionCommand?.Execute(suggestion);
            };
        }
        else
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) =>
            {
                if (view.BindingContext is Suggestion suggestion)
                    InternalSuggestionCommand?.Execute(suggestion);
            };
            view.GestureRecognizers.Add(tap);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  SESSION BINDING
    // ══════════════════════════════════════════════════════════════

    private static void OnSessionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AgentChatView view)
        {
            view.UnbindFromSession(oldValue as IAgentSession);
            view.BindToSession(newValue as IAgentSession);
        }
    }

    private void BindToSession(IAgentSession? session)
    {
        if (session is null)
        {
            Messages = null;
            IsBusy = false;
            if (_pendingLayout is not null)
                BindableLayout.SetItemsSource(_pendingLayout, null);
            return;
        }

        Messages = session.Messages;
        IsBusy = session.IsProcessing;

        if (_messagesView is not null)
            _messagesView.ItemsSource = session.Messages;

        if (_pendingLayout is not null)
        {
            BindableLayout.SetItemsSource(_pendingLayout, session.PendingMessages);
            BindableLayout.SetItemTemplateSelector(_pendingLayout, new ChatMessageTemplateSelector
            {
                UserTemplate = UserMessageTemplate,
                AssistantTemplate = AssistantMessageTemplate,
                ToolTemplate = ToolMessageTemplate,
                SystemTemplate = SystemMessageTemplate,
                ErrorTemplate = ErrorMessageTemplate,
            });
        }

        session.Messages.CollectionChanged += OnMessagesCollectionChanged;
        session.PendingMessages.CollectionChanged += OnMessagesCollectionChanged;
        session.PropertyChanged += OnSessionPropertyChanged;
    }

    private void UnbindFromSession(IAgentSession? session)
    {
        if (session is null) return;
        session.Messages.CollectionChanged -= OnMessagesCollectionChanged;
        session.PendingMessages.CollectionChanged -= OnMessagesCollectionChanged;
        session.PropertyChanged -= OnSessionPropertyChanged;
    }

    private void OnMessagesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null) return;
        foreach (var item in e.NewItems)
        {
            if (item is ChatMessageViewModel vm)
                DecorateMessage(vm);
        }
    }

    private void DecorateMessage(ChatMessageViewModel vm)
    {
        vm.ShowTimestamp = ShowTimestamps;

        if (vm.IsUser)
        {
            vm.AvatarText = UserAvatarText;
            vm.AvatarSource = UserAvatarSource;
            vm.ShowAvatar = ShowAvatars;
            vm.AuthorName = UserDisplayName;
        }
        else
        {
            vm.AvatarText = AssistantAvatarText;
            vm.AvatarSource = AssistantAvatarSource;
            vm.ShowAvatar = ShowAvatars;
            vm.AuthorName = AssistantDisplayName;
        }
    }

    private void RedecorateAllMessages()
    {
        if (Session is null) return;
        foreach (var vm in Session.Messages)
            DecorateMessage(vm);
        foreach (var vm in Session.PendingMessages)
            DecorateMessage(vm);
    }

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IAgentSession.IsProcessing) && Session is not null)
        {
            IsBusy = Session.IsProcessing;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  SEND (delegates to IAgentSession)
    // ══════════════════════════════════════════════════════════════

    private async Task HandleSendAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrWhiteSpace(text) || IsBusy || Session is null)
            return;

        InputText = string.Empty;

        await Session.SendAsync(new ChatMessage(ChatRole.User, text));

        // Auto-scroll to the last message
        if (_messagesView is not null && Session.Messages.Count > 0)
        {
            var lastMsg = Session.Messages[^1];
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
            {
                try { _messagesView.ScrollTo(lastMsg, position: ScrollToPosition.End, animate: true); }
                catch { }
            });
        }
    }
}
