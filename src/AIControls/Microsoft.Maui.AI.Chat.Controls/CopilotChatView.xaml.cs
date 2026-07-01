using System.Collections.ObjectModel;
using Microsoft.Maui.AI.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;

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
    private ActivityIndicator? _busyIndicatorPart;
    private Layout? _suggestionsPart;
    private ContentView? _footerPart;
    private Entry? _inputEntryPart;
    private Button? _sendButtonPart;
    private Border? _inputAreaPart;

    private readonly ObservableCollection<ContentTemplate> _contentTemplates = [];

    private IDisposable? _statusChangedReg;

    public IList<ContentTemplate> ContentTemplates => _contentTemplates;

    public CopilotChatView()
    {
        InitializeComponent();
        _contentTemplates.CollectionChanged += (_, _) => SyncContentTemplates();

        // Bind the default ControlTemplate via DynamicResource so it resolves
        // once the theme dictionary is available in the resource tree.
        // The actual theme loading is done by UseChatControls() at startup
        // or deferred to OnParentSet to avoid mutating app resources during
        // XAML parsing (which causes NullRef in the generated InitializeComponent).
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

        // Unhook old parts
        if (_inputEntryPart is not null)
            _inputEntryPart.Completed -= OnInputCompleted;
        if (_sendButtonPart is not null)
            _sendButtonPart.Clicked -= OnSendButtonClicked;

        // Resolve named parts
        _headerPart = GetTemplateChild("PART_Header") as ContentView;
        _messageListPart = GetTemplateChild("PART_MessageList") as MessageListView;
        _welcomePanelPart = GetTemplateChild("PART_WelcomePanel") as View;
        _welcomeIconPart = GetTemplateChild("PART_WelcomeIcon") as Label;
        _welcomeMessagePart = GetTemplateChild("PART_WelcomeMessage") as Label;
        _busyIndicatorPart = GetTemplateChild("PART_BusyIndicator") as ActivityIndicator;
        _suggestionsPart = GetTemplateChild("PART_Suggestions") as Layout;
        _footerPart = GetTemplateChild("PART_Footer") as ContentView;
        _inputEntryPart = GetTemplateChild("PART_InputEntry") as Entry;
        _sendButtonPart = GetTemplateChild("PART_SendButton") as Button;
        _inputAreaPart = GetTemplateChild("PART_InputArea") as Border;

        // Hook up new parts
        if (_inputEntryPart is not null)
            _inputEntryPart.Completed += OnInputCompleted;
        if (_sendButtonPart is not null)
            _sendButtonPart.Clicked += OnSendButtonClicked;

        // Wire the nested message list — forward session, templates and options.
        if (_messageListPart is not null)
        {
            _messageListPart.ItemsChanged -= OnMessageItemsChanged;
            _messageListPart.ItemsChanged += OnMessageItemsChanged;
            _messageListPart.Session = Session;
            _messageListPart.ShowToolCalls = ShowToolCalls;
            _messageListPart.ShowToolResults = ShowToolResults;
            SyncContentTemplates();
        }

        // Apply state
        ApplyInputStyling();
        ApplyHeaderTemplate();
        ApplyFooterTemplate();
        UpdateWelcomeVisibility();
        OnIsBusyChanged();
    }

    // ── Content templates ──

    private void SyncContentTemplates()
    {
        if (_messageListPart is null)
            return;

        _messageListPart.ContentTemplates.Clear();
        foreach (var t in _contentTemplates)
            _messageListPart.ContentTemplates.Add(t);
    }

    private void OnMessageItemsChanged(object? sender, EventArgs e) => UpdateWelcomeVisibility();

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
        var itemCount = _messageListPart?.ItemCount ?? 0;
        var showWelcome = !string.IsNullOrEmpty(WelcomeMessage) && itemCount == 0;

        if (_welcomePanelPart is not null)
            _welcomePanelPart.IsVisible = showWelcome;
        if (_welcomeIconPart is not null)
            _welcomeIconPart.Text = WelcomeIcon;
        if (_welcomeMessagePart is not null)
            _welcomeMessagePart.Text = WelcomeMessage;
        if (_messageListPart is not null)
            _messageListPart.IsVisible = !showWelcome;

        UpdateSuggestionsVisibility();
    }

    private void UpdateSuggestionsVisibility()
    {
        var itemCount = _messageListPart?.ItemCount ?? 0;
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
            chip.SetDynamicResource(Button.BackgroundColorProperty, "ExtensionsAI.Suggestion.Background");
            chip.SetDynamicResource(Button.TextColorProperty, "ExtensionsAI.Suggestion.TextColor");
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
            _headerPart.Content = HeaderTemplate.CreateContent() as View;
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
            _footerPart.Content = FooterTemplate.CreateContent() as View;
            _footerPart.IsVisible = true;
        }
        else
        {
            _footerPart.Content = null;
            _footerPart.IsVisible = false;
        }
    }

    // ── Input styling ──

    internal void ApplyInputStyling()
    {
        if (_sendButtonPart is not null && SendButtonBackgroundColor is not null)
            _sendButtonPart.BackgroundColor = SendButtonBackgroundColor;

        if (_inputAreaPart is not null)
        {
            if (InputAreaBackgroundColor is not null)
                _inputAreaPart.BackgroundColor = InputAreaBackgroundColor;

            if (_inputAreaPart.StrokeShape is RoundRectangle rr)
                rr.CornerRadius = new CornerRadius(InputAreaCornerRadius);
        }
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
}
