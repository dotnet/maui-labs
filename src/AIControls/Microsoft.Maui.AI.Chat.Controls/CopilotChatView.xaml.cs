using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Components.AI;
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

    private IDisposable? _turnAddedReg;
    private IDisposable? _statusChangedReg;
    private IDisposable? _blockAddedReg;
    private readonly List<IDisposable> _blockSubscriptions = [];

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
        control.UnsubscribeFromSession();

        if (newValue is AgentContext ctx)
            control.SubscribeToSession(ctx);

        control.RebuildFromSession();
    }

    private void SubscribeToSession(AgentContext ctx)
    {
        _turnAddedReg = ctx.RegisterOnTurnAdded(turn =>
            Dispatcher.Dispatch(() => OnTurnAdded(turn)));

        _statusChangedReg = ctx.RegisterOnStatusChanged(status =>
            Dispatcher.Dispatch(() => OnStatusChanged(status)));

        _blockAddedReg = ctx.RegisterOnBlockAdded((turn, block) =>
            Dispatcher.Dispatch(() => OnBlockAdded(turn, block)));
    }

    private void UnsubscribeFromSession()
    {
        _turnAddedReg?.Dispose();
        _statusChangedReg?.Dispose();
        _blockAddedReg?.Dispose();
        _turnAddedReg = null;
        _statusChangedReg = null;
        _blockAddedReg = null;

        // Dispose all per-block change subscriptions (Bug 1 fix: memory leak)
        foreach (var sub in _blockSubscriptions)
            sub.Dispose();
        _blockSubscriptions.Clear();
    }

    private void OnTurnAdded(ConversationTurn turn)
    {
        // Blocks will arrive via OnBlockAdded
    }

    private void OnStatusChanged(ConversationStatus status)
    {
        IsBusy = status is ConversationStatus.Streaming or ConversationStatus.AwaitingInput;

        if (status == ConversationStatus.Error && Session?.Error is Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CopilotChatView] Error: {ex}");
        }

        // If session was cleared (idle with no turns), rebuild to show welcome state
        if (status == ConversationStatus.Idle && Session?.Turns.Count == 0)
        {
            RebuildFromSession();
        }
    }

    private void OnBlockAdded(ConversationTurn turn, ContentBlock block)
    {
        if (Session is null)
            return;

        if (!ShouldShowBlock(block))
        {
            // Still subscribe — the block may become visible later (e.g., tool result arrives)
            var sub = block.OnChanged(() => Dispatcher.Dispatch(() => OnBlockChanged(block)));
            _blockSubscriptions.Add(sub);
            return;
        }

        _items.Add(new ContentContext(Session, block));
        UpdateWelcomeVisibility();
        ScrollToLatestMessage();

        // Subscribe to block changes for streaming updates
        var subscription = block.OnChanged(() => Dispatcher.Dispatch(() => OnBlockChanged(block)));
        _blockSubscriptions.Add(subscription);
    }

    private void OnBlockChanged(ContentBlock block)
    {
        if (Session is null)
            return;

        // Check if this block is already displayed
        for (int i = 0; i < _items.Count; i++)
        {
            if (ReferenceEquals(_items[i].Block, block))
            {
                if (!ShouldShowBlock(block))
                {
                    // Block should no longer be shown — remove it
                    _items.RemoveAt(i);
                    UpdateWelcomeVisibility();
                }
                else
                {
                    // Update the existing item to trigger UI refresh
                    _items[i] = new ContentContext(Session, block);
                    ScrollToLatestMessage();
                }
                return;
            }
        }

        // Block isn't in the list yet — check if it should now be shown
        // (e.g., tool result arrived when ShowToolResults=true but ShowToolCalls=false)
        if (ShouldShowBlock(block))
        {
            _items.Add(new ContentContext(Session, block));
            UpdateWelcomeVisibility();
            ScrollToLatestMessage();
        }
    }

    private void RebuildFromSession()
    {
        // Dispose existing block subscriptions before clearing
        foreach (var sub in _blockSubscriptions)
            sub.Dispose();
        _blockSubscriptions.Clear();

        _items.Clear();

        if (Session is null)
        {
            IsBusy = false;
            UpdateWelcomeVisibility();
            return;
        }

        foreach (var turn in Session.Turns)
        {
            foreach (var block in turn.RequestBlocks)
            {
                if (ShouldShowBlock(block))
                    _items.Add(new ContentContext(Session, block));
            }
            foreach (var block in turn.ResponseBlocks)
            {
                if (ShouldShowBlock(block))
                    _items.Add(new ContentContext(Session, block));
            }
        }

        IsBusy = Session.Status == ConversationStatus.Streaming;
        UpdateWelcomeVisibility();
        ScrollToLatestMessage();
    }

    private bool ShouldShowBlock(ContentBlock block)
    {
        if (!ShowToolCalls && block is FunctionInvocationContentBlock ficb && ficb.Result is null)
            return false;
        if (!ShowToolResults && block is FunctionInvocationContentBlock ficbr && ficbr.Result is not null)
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
