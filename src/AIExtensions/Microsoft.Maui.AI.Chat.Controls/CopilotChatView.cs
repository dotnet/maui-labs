using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Maui.AI.Chat;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// A drop-in MAUI chat control for AI agents.
/// <para>
/// The entire visual tree is defined by a <see cref="ControlTemplate"/> and can be
/// replaced wholesale. Individual sections (header, messages, welcome, busy indicator,
/// suggestions, footer, input area) are located by well-known <c>PART_*</c> names:
/// </para>
/// <list type="bullet">
/// <item><c>PART_Header</c> — <see cref="ContentView"/> for header content</item>
/// <item><c>PART_MessageList</c> — <see cref="MessageListView"/> that renders chat messages</item>
/// <item><c>PART_WelcomePanel</c> — <see cref="View"/> shown when there are no messages</item>
/// <item><c>PART_WelcomeIcon</c> — <see cref="Label"/> for the welcome icon</item>
/// <item><c>PART_WelcomeMessage</c> — <see cref="Label"/> for the welcome text</item>
/// <item><c>PART_EmptyView</c> — <see cref="ContentView"/> host for a custom <see cref="EmptyViewTemplate"/></item>
/// <item><c>PART_BusyIndicator</c> — <see cref="ActivityIndicator"/> for the busy state</item>
/// <item><c>PART_Suggestions</c> — <see cref="Layout"/> for suggestion chips</item>
/// <item><c>PART_Footer</c> — <see cref="ContentView"/> for footer content</item>
/// <item><c>PART_InputEntry</c> — <see cref="Entry"/> for user text input</item>
/// <item><c>PART_SendButton</c> — <see cref="Button"/> to send the message</item>
/// <item><c>PART_InputArea</c> — <see cref="Border"/> wrapping the input row</item>
/// </list>
/// </summary>
[ContentProperty(nameof(ContentTemplates))]
public partial class CopilotChatView : TemplatedView
{
    // ── Core bindable properties ──

    public static readonly BindableProperty SessionProperty =
        BindableProperty.Create(
            nameof(Session),
            typeof(AgentContext),
            typeof(CopilotChatView),
            propertyChanged: OnSessionChanged);

    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(
            nameof(Text),
            typeof(string),
            typeof(CopilotChatView),
            default(string),
            BindingMode.TwoWay);

    public static readonly BindableProperty IsBusyProperty =
        BindableProperty.Create(
            nameof(IsBusy),
            typeof(bool),
            typeof(CopilotChatView),
            false,
            propertyChanged: (b, _, _) => ((CopilotChatView)b).OnIsBusyChanged());

    public AgentContext? Session
    {
        get => (AgentContext?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    // ── Template parts (resolved in OnApplyTemplate) ──

    private ContentView? _headerPart;
    private MessageListView? _messageListPart;
    private View? _welcomePanelPart;
    private Label? _welcomeIconPart;
    private Label? _welcomeMessagePart;
    private ContentView? _emptyViewHostPart;
    private ActivityIndicator? _busyIndicatorPart;
    private Layout? _suggestionsPart;
    private ContentView? _footerPart;
    private Entry? _inputEntryPart;
    private Button? _sendButtonPart;

    public static readonly BindableProperty ContentTemplatesProperty =
        BindableProperty.Create(
            nameof(ContentTemplates),
            typeof(IList<ContentTemplate>),
            typeof(CopilotChatView),
            defaultValueCreator: _ => new ObservableCollection<ContentTemplate>(),
            propertyChanged: (b, o, n) =>
            {
                var self = (CopilotChatView)b;
                if (o is System.Collections.Specialized.INotifyCollectionChanged oldNcc)
                    oldNcc.CollectionChanged -= self.OnContentTemplatesChanged;
                if (n is System.Collections.Specialized.INotifyCollectionChanged newNcc)
                    newNcc.CollectionChanged += self.OnContentTemplatesChanged;
                self.SyncContentTemplates();
            });

    public static readonly BindableProperty UseDefaultContentTemplatesProperty =
        BindableProperty.Create(
            nameof(UseDefaultContentTemplates),
            typeof(bool),
            typeof(CopilotChatView),
            true,
            propertyChanged: (b, _, _) => ((CopilotChatView)b).SyncContentTemplates());

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

    private IDisposable? _statusChangedReg;

    public IList<ContentTemplate> ContentTemplates
    {
        get => (IList<ContentTemplate>)GetValue(ContentTemplatesProperty);
        set => SetValue(ContentTemplatesProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the nested message list uses the built-in text, approval, media, thinking, and
    /// error templates when no consumer template matches. Set this to <see langword="false"/> for strict
    /// allow-list rendering using only <see cref="ContentTemplates"/>.
    /// </summary>
    public bool UseDefaultContentTemplates
    {
        get => (bool)GetValue(UseDefaultContentTemplatesProperty);
        set => SetValue(UseDefaultContentTemplatesProperty, value);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public Color? EffectiveSendButtonBackgroundColor =>
        (Color?)GetValue(EffectiveSendButtonBackgroundColorProperty);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public Color? EffectiveInputAreaBackgroundColor =>
        (Color?)GetValue(EffectiveInputAreaBackgroundColorProperty);

    public CopilotChatView()
    {
        if (ContentTemplates is System.Collections.Specialized.INotifyCollectionChanged ctNcc)
            ctNcc.CollectionChanged += OnContentTemplatesChanged;

        SetDynamicResource(MaxBubbleWidthProperty, Themes.ChatThemeKeys.BubbleMaxWidth);
        RestoreEffectiveInputColor(
            EffectiveSendButtonBackgroundColorProperty,
            Themes.ChatThemeKeys.SendBackground);
        RestoreEffectiveInputColor(
            EffectiveInputAreaBackgroundColorProperty,
            Themes.ChatThemeKeys.InputBackground);

        // Bind the default ControlTemplate via DynamicResource so it resolves
        // once the theme dictionary is available in the resource tree. The actual
        // theme loading is done by UseChatControls() at startup or deferred to
        // OnParentSet to avoid mutating app resources while the tree is being built.
        SetDynamicResource(ControlTemplateProperty, Themes.ChatThemeKeys.CopilotChatViewTemplate);

        // Subscribe to collection changes on the default ObservableCollection so
        // XAML-added items (via .Add()) trigger suggestion chip rebuilds.
        if (SuggestionPrompts is System.Collections.Specialized.INotifyCollectionChanged ncc)
            ncc.CollectionChanged += OnSuggestionPromptsCollectionChanged;

        // XAML child items (SuggestionPrompts) may be added after OnApplyTemplate
        // due to DynamicResource-based template resolution timing. Re-evaluate once loaded.
        Loaded += (_, _) =>
        {
            // The Loaded event fires after the visual tree is fully constructed
            // and rendered — guaranteed that template parts are resolved and
            // all XAML-set collection items have been added.
            UpdateWelcomeVisibility();
        };
    }

    private void OnSuggestionPromptsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Items may be added before the template is applied; defer to ensure parts are resolved.
        if (_suggestionsPart is null)
            Dispatcher.Dispatch(UpdateSuggestionsVisibility);
        else
            UpdateSuggestionsVisibility();
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();

        // Deferred theme loading: ensure the ChatTheme resources are merged
        // into the app-level dictionary when the control joins the visual tree.
        // We cannot do this in the constructor because modifying
        // Application.Resources.MergedDictionaries during XAML parsing
        // triggers resource re-evaluation on partially-constructed pages.
        if (Parent is not null && Application.Current is { } app)
            ChatThemeLoader.EnsureLoaded(app.Resources);
    }

    // ── Template application ──

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Resolve named parts
        _headerPart = GetTemplateChild("PART_Header") as ContentView;
        _welcomePanelPart = GetTemplateChild("PART_WelcomePanel") as View;
        _welcomeIconPart = GetTemplateChild("PART_WelcomeIcon") as Label;
        _welcomeMessagePart = GetTemplateChild("PART_WelcomeMessage") as Label;
        _emptyViewHostPart = GetTemplateChild("PART_EmptyView") as ContentView;
        _busyIndicatorPart = GetTemplateChild("PART_BusyIndicator") as ActivityIndicator;
        _suggestionsPart = GetTemplateChild("PART_Suggestions") as Layout;
        _footerPart = GetTemplateChild("PART_Footer") as ContentView;
        AttachInputParts(
            GetTemplateChild("PART_InputEntry") as Entry,
            GetTemplateChild("PART_SendButton") as Button);

        AttachMessageListPart(GetTemplateChild("PART_MessageList") as MessageListView);

        // Apply state
        ApplyHeaderTemplate();
        ApplyFooterTemplate();
        ApplyEmptyViewTemplate();
        UpdateWelcomeVisibility();
        OnIsBusyChanged();
    }

    // ── Content templates ──

    private void OnContentTemplatesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        SyncContentTemplates();

    private void SyncContentTemplates()
    {
        if (_messageListPart is null)
            return;

        _messageListPart.ContentTemplates.Clear();
        foreach (var t in ContentTemplates)
            _messageListPart.ContentTemplates.Add(t);

        _messageListPart.UseDefaultContentTemplates = UseDefaultContentTemplates;
        SyncMessageAppearance();
    }

    private void SyncMessageAppearance()
    {
        if (_messageListPart is null)
            return;

        _messageListPart.ShowAvatars = ShowAvatars;
        _messageListPart.AvatarSize = AvatarSize;
        _messageListPart.UserDisplayName = UserDisplayName;
        _messageListPart.AssistantDisplayName = AssistantDisplayName;
        _messageListPart.ShowTimestamps = ShowTimestamps;
        _messageListPart.BubbleCornerRadius = BubbleCornerRadius;
        _messageListPart.BubbleStrokeThickness = BubbleStrokeThickness;
        _messageListPart.BubbleStrokeColor = BubbleStrokeColor;
        _messageListPart.MaxBubbleWidth = MaxBubbleWidth;
    }

    private static void OnMessageAppearanceChanged(BindableObject bindable, object? oldValue, object? newValue) =>
        ((CopilotChatView)bindable).SyncMessageAppearance();

    private void OnMessageItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        UpdateWelcomeVisibility();

    internal void AttachMessageListPart(MessageListView? messageList)
    {
        if (_messageListPart is not null)
        {
            ((System.Collections.Specialized.INotifyCollectionChanged)_messageListPart.Items)
                .CollectionChanged -= OnMessageItemsChanged;
            _messageListPart.Session = null;
        }

        _messageListPart = messageList;

        if (_messageListPart is null)
            return;

        ((System.Collections.Specialized.INotifyCollectionChanged)_messageListPart.Items)
            .CollectionChanged += OnMessageItemsChanged;
        _messageListPart.Session = Session;
        SyncContentTemplates();
    }

    // ── Session management ──
    // Message rendering lives in the nested MessageListView. Here we only
    // track status to drive IsBusy and forward the session to the list.

    private static void OnSessionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (CopilotChatView)bindable;
        control.UnsubscribeFromSession();

        if (newValue is AgentContext ctx)
            control.SubscribeToSession(ctx);

        if (control._messageListPart is not null)
            control._messageListPart.Session = newValue as AgentContext;

        control.UpdateWelcomeVisibility();
    }

    private void SubscribeToSession(AgentContext ctx)
    {
        _statusChangedReg = ctx.RegisterOnStatusChanged(status =>
            Dispatcher.Dispatch(() => OnStatusChanged(status)));
    }

    private void UnsubscribeFromSession()
    {
        _statusChangedReg?.Dispose();
        _statusChangedReg = null;
    }

    private void OnStatusChanged(ConversationStatus status)
    {
        IsBusy = status is ConversationStatus.Streaming or ConversationStatus.AwaitingInput;

        if (status == ConversationStatus.Error && Session?.Error is Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CopilotChatView] Error: {ex}");
        }

        // When the session is cleared, refresh welcome/suggestions visibility.
        if (status == ConversationStatus.Idle && Session?.Turns.Count == 0)
            UpdateWelcomeVisibility();
    }

    // ── Welcome ──

    internal void UpdateWelcomeVisibility()
    {
        var itemCount = _messageListPart?.Items.Count ?? 0;
        var isEmpty = itemCount == 0;

        // A custom EmptyViewTemplate (when supplied and hostable) takes precedence
        // over the default welcome icon/message labels.
        var hasCustomEmpty = EmptyViewTemplate is not null && _emptyViewHostPart is not null;
        var showCustomEmpty = hasCustomEmpty && isEmpty;
        var showWelcome = !hasCustomEmpty && !string.IsNullOrEmpty(WelcomeMessage) && isEmpty;

        if (_emptyViewHostPart is not null)
            _emptyViewHostPart.IsVisible = showCustomEmpty;
        if (_welcomePanelPart is not null)
            _welcomePanelPart.IsVisible = showWelcome;
        if (_welcomeIconPart is not null)
            _welcomeIconPart.Text = WelcomeIcon;
        if (_welcomeMessagePart is not null)
            _welcomeMessagePart.Text = WelcomeMessage;
        if (_messageListPart is not null)
            _messageListPart.IsVisible = !(showWelcome || showCustomEmpty);

        UpdateSuggestionsVisibility();
    }

    private void ApplyEmptyViewTemplate()
    {
        if (_emptyViewHostPart is null)
            return;

        _emptyViewHostPart.Content = CreateTemplatedContent(EmptyViewTemplate);
        UpdateWelcomeVisibility();
    }

    // Consumer-supplied DataTemplates (empty view, header, footer) should bind against the
    // control's data context (e.g. the host ViewModel), NOT the templated parent. Inside a
    // ControlTemplate the BindingContext of parts is the control itself, so created content
    // would otherwise inherit the control as its context and bindings like "{Binding MyProp}"
    // would silently resolve to nothing. We set the content's BindingContext explicitly and
    // keep it in sync via OnBindingContextChanged.
    private View? CreateTemplatedContent(DataTemplate? template)
    {
        if (template?.CreateContent() is not View view)
            return null;

        view.BindingContext = BindingContext;
        return view;
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        var ctx = BindingContext;
        if (_emptyViewHostPart?.Content is View emptyContent)
            emptyContent.BindingContext = ctx;
        if (_headerPart?.Content is View headerContent)
            headerContent.BindingContext = ctx;
        if (_footerPart?.Content is View footerContent)
            footerContent.BindingContext = ctx;
    }

    private void UpdateSuggestionsVisibility()
    {
        var itemCount = _messageListPart?.Items.Count ?? 0;
        var showSuggestions = SuggestionPrompts is { Count: > 0 } && itemCount == 0;

        if (_suggestionsPart is not null)
        {
            _suggestionsPart.IsVisible = showSuggestions;
            if (showSuggestions)
                BuildSuggestionChips();
        }
    }

    private void BuildSuggestionChips()
    {
        if (_suggestionsPart is null)
            return;

        _suggestionsPart.Children.Clear();
        if (SuggestionPrompts is null)
            return;

        foreach (var prompt in SuggestionPrompts)
        {
            var chip = new Button
            {
                Text = prompt,
                FontSize = 12,
                Padding = new Thickness(12, 6),
                CornerRadius = 16,
                Margin = new Thickness(4, 2),
                BackgroundColor = Color.FromArgb("#EEF2FF"),
                TextColor = Color.FromArgb("#4338CA"),
            };
            chip.SetDynamicResource(Button.BackgroundColorProperty, Themes.ChatThemeKeys.SuggestionBackground);
            chip.SetDynamicResource(Button.TextColorProperty, Themes.ChatThemeKeys.SuggestionTextColor);
            chip.Clicked += async (_, _) =>
            {
                if (Session is not null && !IsBusy)
                    await Session.SendMessageAsync(prompt);
            };
            _suggestionsPart.Children.Add(chip);
        }
    }

    // ── Header / Footer ──

    private void ApplyHeaderTemplate()
    {
        if (_headerPart is null)
            return;

        if (HeaderTemplate is not null)
        {
            _headerPart.Content = CreateTemplatedContent(HeaderTemplate);
            _headerPart.IsVisible = true;
        }
        else
        {
            _headerPart.Content = null;
            _headerPart.IsVisible = false;
        }
    }

    private void ApplyFooterTemplate()
    {
        if (_footerPart is null)
            return;

        if (FooterTemplate is not null)
        {
            _footerPart.Content = CreateTemplatedContent(FooterTemplate);
            _footerPart.IsVisible = true;
        }
        else
        {
            _footerPart.Content = null;
            _footerPart.IsVisible = false;
        }
    }

    internal void AttachInputParts(Entry? inputEntry, Button? sendButton)
    {
        if (_inputEntryPart is not null)
            _inputEntryPart.Completed -= OnInputCompleted;
        if (_sendButtonPart is not null)
            _sendButtonPart.Clicked -= OnSendButtonClicked;

        _inputEntryPart = inputEntry;
        _sendButtonPart = sendButton;

        if (_inputEntryPart is not null)
            _inputEntryPart.Completed += OnInputCompleted;
        if (_sendButtonPart is not null)
            _sendButtonPart.Clicked += OnSendButtonClicked;
    }

    // ── Busy ──

    private void OnIsBusyChanged()
    {
        if (_busyIndicatorPart is not null)
        {
            _busyIndicatorPart.IsRunning = IsBusy;
            _busyIndicatorPart.IsVisible = IsBusy;
        }
    }

    // ── Send ──

    private async void OnSendButtonClicked(object? sender, EventArgs e)
    {
        await SendCurrentTextAsync();
    }

    private async void OnInputCompleted(object? sender, EventArgs e)
    {
        await SendCurrentTextAsync();
    }

    private async Task SendCurrentTextAsync()
    {
        if (Session is null || IsBusy || string.IsNullOrWhiteSpace(Text))
            return;

        var nextMessage = Text.Trim();
        Text = string.Empty;
        await Session.SendMessageAsync(nextMessage);
    }

    // ═══════════════════════════════════════════════════════════════
    //  BINDABLE CUSTOMIZATION PROPERTIES
    // ═══════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════
    //  INPUT AREA
    // ═══════════════════════════════════════════════════════════════

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(CopilotChatView), "Type a message...");

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly BindableProperty SendButtonTextProperty =
        BindableProperty.Create(nameof(SendButtonText), typeof(string), typeof(CopilotChatView), "\u27A4");

    public string SendButtonText
    {
        get => (string)GetValue(SendButtonTextProperty);
        set => SetValue(SendButtonTextProperty, value);
    }

    public static readonly BindableProperty SendButtonBackgroundColorProperty =
        BindableProperty.Create(
            nameof(SendButtonBackgroundColor),
            typeof(Color),
            typeof(CopilotChatView),
            propertyChanged: (b, _, value) => ((CopilotChatView)b).UpdateEffectiveInputColor(
                EffectiveSendButtonBackgroundColorProperty,
                (Color?)value,
                Themes.ChatThemeKeys.SendBackground));

    public Color? SendButtonBackgroundColor
    {
        get => (Color?)GetValue(SendButtonBackgroundColorProperty);
        set => SetValue(SendButtonBackgroundColorProperty, value);
    }

    public static readonly BindableProperty InputAreaBackgroundColorProperty =
        BindableProperty.Create(
            nameof(InputAreaBackgroundColor),
            typeof(Color),
            typeof(CopilotChatView),
            propertyChanged: (b, _, value) => ((CopilotChatView)b).UpdateEffectiveInputColor(
                EffectiveInputAreaBackgroundColorProperty,
                (Color?)value,
                Themes.ChatThemeKeys.InputBackground));

    public Color? InputAreaBackgroundColor
    {
        get => (Color?)GetValue(InputAreaBackgroundColorProperty);
        set => SetValue(InputAreaBackgroundColorProperty, value);
    }

    public static readonly BindableProperty InputAreaCornerRadiusProperty =
        BindableProperty.Create(
            nameof(InputAreaCornerRadius),
            typeof(double),
            typeof(CopilotChatView),
            14.0);

    public double InputAreaCornerRadius
    {
        get => (double)GetValue(InputAreaCornerRadiusProperty);
        set => SetValue(InputAreaCornerRadiusProperty, value);
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

    // ═══════════════════════════════════════════════════════════════
    //  WELCOME MESSAGE
    // ═══════════════════════════════════════════════════════════════

    public static readonly BindableProperty WelcomeMessageProperty =
        BindableProperty.Create(nameof(WelcomeMessage), typeof(string), typeof(CopilotChatView),
            propertyChanged: (b, _, _) => ((CopilotChatView)b).UpdateWelcomeVisibility());

    public string? WelcomeMessage
    {
        get => (string?)GetValue(WelcomeMessageProperty);
        set => SetValue(WelcomeMessageProperty, value);
    }

    public static readonly BindableProperty WelcomeIconProperty =
        BindableProperty.Create(nameof(WelcomeIcon), typeof(string), typeof(CopilotChatView), "💬",
            propertyChanged: (b, _, _) => ((CopilotChatView)b).UpdateWelcomeVisibility());

    public string WelcomeIcon
    {
        get => (string)GetValue(WelcomeIconProperty);
        set => SetValue(WelcomeIconProperty, value);
    }

    // ═══════════════════════════════════════════════════════════════
    //  AVATARS
    // ═══════════════════════════════════════════════════════════════

    public static readonly BindableProperty ShowAvatarsProperty =
        BindableProperty.Create(
            nameof(ShowAvatars),
            typeof(bool),
            typeof(CopilotChatView),
            false,
            propertyChanged: OnMessageAppearanceChanged);

    public bool ShowAvatars
    {
        get => (bool)GetValue(ShowAvatarsProperty);
        set => SetValue(ShowAvatarsProperty, value);
    }

    public static readonly BindableProperty AvatarSizeProperty =
        BindableProperty.Create(
            nameof(AvatarSize),
            typeof(double),
            typeof(CopilotChatView),
            28.0,
            propertyChanged: OnMessageAppearanceChanged);

    public double AvatarSize
    {
        get => (double)GetValue(AvatarSizeProperty);
        set => SetValue(AvatarSizeProperty, value);
    }

    public static readonly BindableProperty UserDisplayNameProperty =
        BindableProperty.Create(
            nameof(UserDisplayName),
            typeof(string),
            typeof(CopilotChatView),
            "You",
            propertyChanged: OnMessageAppearanceChanged);

    public string UserDisplayName
    {
        get => (string)GetValue(UserDisplayNameProperty);
        set => SetValue(UserDisplayNameProperty, value);
    }

    public static readonly BindableProperty AssistantDisplayNameProperty =
        BindableProperty.Create(
            nameof(AssistantDisplayName),
            typeof(string),
            typeof(CopilotChatView),
            "Assistant",
            propertyChanged: OnMessageAppearanceChanged);

    public string AssistantDisplayName
    {
        get => (string)GetValue(AssistantDisplayNameProperty);
        set => SetValue(AssistantDisplayNameProperty, value);
    }

    // ═══════════════════════════════════════════════════════════════
    //  TIMESTAMPS & TOOL VISIBILITY
    // ═══════════════════════════════════════════════════════════════

    public static readonly BindableProperty ShowTimestampsProperty =
        BindableProperty.Create(
            nameof(ShowTimestamps),
            typeof(bool),
            typeof(CopilotChatView),
            false,
            propertyChanged: OnMessageAppearanceChanged);

    public bool ShowTimestamps
    {
        get => (bool)GetValue(ShowTimestampsProperty);
        set => SetValue(ShowTimestampsProperty, value);
    }

    // ═══════════════════════════════════════════════════════════════
    //  MESSAGE BUBBLE STYLING
    // ═══════════════════════════════════════════════════════════════

    public static readonly BindableProperty BubbleCornerRadiusProperty =
        BindableProperty.Create(
            nameof(BubbleCornerRadius),
            typeof(double),
            typeof(CopilotChatView),
            16.0,
            propertyChanged: OnMessageAppearanceChanged);

    public double BubbleCornerRadius
    {
        get => (double)GetValue(BubbleCornerRadiusProperty);
        set => SetValue(BubbleCornerRadiusProperty, value);
    }

    public static readonly BindableProperty BubbleStrokeThicknessProperty =
        BindableProperty.Create(
            nameof(BubbleStrokeThickness),
            typeof(double),
            typeof(CopilotChatView),
            0.0,
            propertyChanged: OnMessageAppearanceChanged);

    public double BubbleStrokeThickness
    {
        get => (double)GetValue(BubbleStrokeThicknessProperty);
        set => SetValue(BubbleStrokeThicknessProperty, value);
    }

    public static readonly BindableProperty BubbleStrokeColorProperty =
        BindableProperty.Create(
            nameof(BubbleStrokeColor),
            typeof(Color),
            typeof(CopilotChatView),
            propertyChanged: OnMessageAppearanceChanged);

    public Color? BubbleStrokeColor
    {
        get => (Color?)GetValue(BubbleStrokeColorProperty);
        set => SetValue(BubbleStrokeColorProperty, value);
    }

    public static readonly BindableProperty MaxBubbleWidthProperty =
        BindableProperty.Create(
            nameof(MaxBubbleWidth),
            typeof(double),
            typeof(CopilotChatView),
            340.0,
            propertyChanged: OnMessageAppearanceChanged);

    public double MaxBubbleWidth
    {
        get => (double)GetValue(MaxBubbleWidthProperty);
        set => SetValue(MaxBubbleWidthProperty, value);
    }

    // ═══════════════════════════════════════════════════════════════
    //  LAYOUT TEMPLATES
    // ═══════════════════════════════════════════════════════════════

    public static readonly BindableProperty HeaderTemplateProperty =
        BindableProperty.Create(nameof(HeaderTemplate), typeof(DataTemplate), typeof(CopilotChatView),
            propertyChanged: (b, _, _) => ((CopilotChatView)b).ApplyHeaderTemplate());

    public DataTemplate? HeaderTemplate
    {
        get => (DataTemplate?)GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    public static readonly BindableProperty FooterTemplateProperty =
        BindableProperty.Create(nameof(FooterTemplate), typeof(DataTemplate), typeof(CopilotChatView),
            propertyChanged: (b, _, _) => ((CopilotChatView)b).ApplyFooterTemplate());

    public DataTemplate? FooterTemplate
    {
        get => (DataTemplate?)GetValue(FooterTemplateProperty);
        set => SetValue(FooterTemplateProperty, value);
    }

    public static readonly BindableProperty EmptyViewTemplateProperty =
        BindableProperty.Create(nameof(EmptyViewTemplate), typeof(DataTemplate), typeof(CopilotChatView),
            propertyChanged: (b, _, _) => ((CopilotChatView)b).ApplyEmptyViewTemplate());

    /// <summary>
    /// A <see cref="DataTemplate"/> shown in the welcome slot while the conversation is empty.
    /// When set (and the control template exposes a <c>PART_EmptyView</c> host), this content
    /// replaces the default <see cref="WelcomeIcon"/>/<see cref="WelcomeMessage"/> labels. When
    /// <see langword="null"/>, the default welcome labels are used instead.
    /// </summary>
    public DataTemplate? EmptyViewTemplate
    {
        get => (DataTemplate?)GetValue(EmptyViewTemplateProperty);
        set => SetValue(EmptyViewTemplateProperty, value);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SUGGESTIONS
    // ═══════════════════════════════════════════════════════════════

    public static readonly BindableProperty SuggestionPromptsProperty =
        BindableProperty.Create(nameof(SuggestionPrompts), typeof(IList<string>), typeof(CopilotChatView),
            defaultValueCreator: _ => new System.Collections.ObjectModel.ObservableCollection<string>(),
            propertyChanged: (b, o, n) =>
            {
                var self = (CopilotChatView)b;
                if (o is System.Collections.Specialized.INotifyCollectionChanged oldNcc)
                    oldNcc.CollectionChanged -= self.OnSuggestionPromptsCollectionChanged;
                if (n is System.Collections.Specialized.INotifyCollectionChanged newNcc)
                    newNcc.CollectionChanged += self.OnSuggestionPromptsCollectionChanged;
                self.UpdateSuggestionsVisibility();
            });

    public IList<string> SuggestionPrompts
    {
        get => (IList<string>)GetValue(SuggestionPromptsProperty);
        set => SetValue(SuggestionPromptsProperty, value);
    }
}
