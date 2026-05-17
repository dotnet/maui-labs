using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;
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
/// <item><c>PART_Messages</c> — <see cref="CollectionView"/> for chat messages</item>
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
            typeof(IChatSession),
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

    public IChatSession? Session
    {
        get => (IChatSession?)GetValue(SessionProperty);
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
    private CollectionView? _messagesPart;
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
    private readonly ObservableCollection<ContentContext> _items = [];

    public IList<ContentTemplate> ContentTemplates => _contentTemplates;

    public CopilotChatView()
    {
        InitializeComponent();
        _contentTemplates.CollectionChanged += (_, _) => RebuildTemplateSelector();

        // Bind the default ControlTemplate via DynamicResource so it resolves
        // once the theme dictionary is available in the resource tree.
        // The actual theme loading is done by UseChatControls() at startup
        // or deferred to OnParentSet to avoid mutating app resources during
        // XAML parsing (which causes NullRef in the generated InitializeComponent).
        SetDynamicResource(ControlTemplateProperty, Themes.ChatThemeKeys.CopilotChatViewTemplate);
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
        _messagesPart = GetTemplateChild("PART_Messages") as CollectionView;
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

        // Wire message list
        if (_messagesPart is not null)
        {
            _messagesPart.ItemsSource = _items;
            RebuildTemplateSelector();
        }

        // Apply state
        ApplyInputStyling();
        ApplyHeaderTemplate();
        ApplyFooterTemplate();
        UpdateWelcomeVisibility();
        OnIsBusyChanged();
        RebuildFromSession();
    }

    // ── Template selector ──

    private void RebuildTemplateSelector()
    {
        if (_messagesPart is null)
            return;

        var selector = new ContentTemplateSelector();
        foreach (var t in _contentTemplates)
            selector.Templates.Add(t);
        _messagesPart.ItemTemplate = selector;
    }

    // ── Session management ──

    private static void OnSessionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (CopilotChatView)bindable;

        if (oldValue is IChatSession oldSession)
            oldSession.Changed -= control.OnSessionStateChanged;

        if (newValue is IChatSession newSession)
            newSession.Changed += control.OnSessionStateChanged;

        control.RebuildFromSession();
    }

    private void OnSessionStateChanged(object? sender, ChatSessionChangedEventArgs e)
    {
        Dispatcher.Dispatch(() => ApplySessionChange(sender as IChatSession, e));
    }

    private void RebuildFromSession()
    {
        _items.Clear();

        if (Session is null)
        {
            IsBusy = false;
            UpdateWelcomeVisibility();
            return;
        }

        foreach (var entry in Session.Messages)
        {
            if (ShouldShowEntry(entry))
                _items.Add(new ContentContext(Session, entry));
        }

        IsBusy = Session.IsBusy;
        UpdateWelcomeVisibility();
        ScrollToLatestMessage();
    }

    private void ApplySessionChange(IChatSession? session, ChatSessionChangedEventArgs e)
    {
        if (session is null)
            return;

        IsBusy = session.IsBusy;

        switch (e.Kind)
        {
            case ChatSessionChangeKind.Reset:
                _items.Clear();
                UpdateWelcomeVisibility();
                break;

            case ChatSessionChangeKind.MessageAdded:
                if (e.Entry is null || !ShouldShowEntry(e.Entry))
                    break;

                var addIndex = Math.Clamp(e.Index ?? _items.Count, 0, _items.Count);
                _items.Insert(addIndex, new ContentContext(session, e.Entry));
                UpdateWelcomeVisibility();
                ScrollToLatestMessage();
                break;

            case ChatSessionChangeKind.MessageUpdated:
                if (e.Entry is null || e.Index is null)
                    break;

                if (!ShouldShowEntry(e.Entry))
                    break;

                if (e.Index.Value >= 0 && e.Index.Value < _items.Count)
                    _items[e.Index.Value] = new ContentContext(session, e.Entry);
                else
                    RebuildFromSession();

                ScrollToLatestMessage();
                break;
        }
    }

    private bool ShouldShowEntry(ChatEntry entry)
    {
        if (!ShowToolCalls && entry.Content is FunctionCallContent)
            return false;
        if (!ShowToolResults && entry.Content is FunctionResultContent)
            return false;
        return true;
    }

    // ── Welcome ──

    internal void UpdateWelcomeVisibility()
    {
        var showWelcome = !string.IsNullOrEmpty(WelcomeMessage) && _items.Count == 0;

        if (_welcomePanelPart is not null)
            _welcomePanelPart.IsVisible = showWelcome;
        if (_welcomeIconPart is not null)
            _welcomeIconPart.Text = WelcomeIcon;
        if (_welcomeMessagePart is not null)
            _welcomeMessagePart.Text = WelcomeMessage;
        if (_messagesPart is not null)
            _messagesPart.IsVisible = !showWelcome;

        UpdateSuggestionsVisibility();
    }

    private void UpdateSuggestionsVisibility()
    {
        var showSuggestions = SuggestionPrompts is { Count: > 0 } && _items.Count == 0;

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
                    await Session.SendAsync(prompt);
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
        await Session.SendAsync(nextMessage);
    }

    // ── Scroll ──

    private void ScrollToLatestMessage()
    {
        if (_messagesPart is null || _items.Count == 0)
            return;

        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
        {
            if (_items.Count == 0 || _messagesPart is null)
                return;

            _messagesPart.ScrollTo(_items.Count - 1, position: ScrollToPosition.End, animate: false);
        });
    }
}
