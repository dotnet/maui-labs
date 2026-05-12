using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Extensions.AI;
using Microsoft.Maui.CopilotChat.Themes;

namespace Microsoft.Maui.CopilotChat.Controls;

/// <summary>
/// Drop-in MAUI chat control backed by <see cref="IChatClient"/>.
/// <para>
/// The control is a <see cref="ContentView"/> with a default <see cref="ControlTemplate"/>
/// defined in <c>DefaultTheme.xaml</c>. Every sub-component (message bubbles, input area,
/// approval bar, suggestion chips) is replaceable via DataTemplate BindableProperties.
/// The entire visual tree can be replaced by setting <see cref="ControlTemplate"/>.
/// </para>
/// <para>
/// The control never touches <see cref="BindableObject.BindingContext"/>. All internal bindings
/// use <c>{Binding Source={RelativeSource TemplatedParent}}</c> inside the ControlTemplate.
/// </para>
/// </summary>
public class CopilotChatView : ContentView
{
    private CollectionView? _messagesView;
    private Entry? _inputEntry;
    private Button? _sendButton;
    private Button? _approveButton;
    private Button? _rejectButton;
    private Layout? _suggestionsLayout;
    private List<ChatMessage> _history = [];
    private CancellationTokenSource _cts = new();
    private FunctionApprovalRequestContent? _pendingApproval;

    public CopilotChatView()
    {
        // Load default theme as fallback. To override, merge your own ResourceDictionary
        // with Copilot* keys into App.Resources BEFORE the control loads (app-level wins
        // because MAUI resolves up the visual tree: control → page → app).
        var theme = new DefaultThemeResourceDictionary();
        Resources.MergedDictionaries.Add(theme);
        SetDynamicResource(ControlTemplateProperty, "CopilotChatViewDefaultTemplate");

        // Initialize default templates from theme
        SetDynamicResource(UserMessageTemplateProperty, "CopilotDefaultUserMessageTemplate");
        SetDynamicResource(AssistantMessageTemplateProperty, "CopilotDefaultAssistantMessageTemplate");
        SetDynamicResource(ToolMessageTemplateProperty, "CopilotDefaultToolMessageTemplate");
        SetDynamicResource(SystemMessageTemplateProperty, "CopilotDefaultSystemMessageTemplate");
        SetDynamicResource(ErrorMessageTemplateProperty, "CopilotDefaultErrorMessageTemplate");
        SetDynamicResource(SuggestionItemTemplateProperty, "CopilotDefaultSuggestionItemTemplate");

        // Internal commands
        InternalSendCommand = new Command(async () => await HandleSendAsync());
        InternalApproveCommand = new Command(async () => await HandleApproveAsync());
        InternalRejectCommand = new Command(async () => await HandleRejectAsync("User rejected"));
        InternalSuggestionCommand = new Command<string>(async prompt =>
        {
            InputText = prompt;
            await HandleSendAsync();
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  BACKEND
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty ChatClientProperty =
        BindableProperty.Create(nameof(ChatClient), typeof(IChatClient), typeof(CopilotChatView),
            propertyChanged: OnChatClientChanged);

    public IChatClient? ChatClient
    {
        get => (IChatClient?)GetValue(ChatClientProperty);
        set => SetValue(ChatClientProperty, value);
    }

    public static readonly BindableProperty SystemMessageProperty =
        BindableProperty.Create(nameof(SystemMessage), typeof(string), typeof(CopilotChatView),
            propertyChanged: (b, _, _) => ((CopilotChatView)b).InitializeHistory());

    public string? SystemMessage
    {
        get => (string?)GetValue(SystemMessageProperty);
        set => SetValue(SystemMessageProperty, value);
    }

    public static readonly BindableProperty ToolsProperty =
        BindableProperty.Create(nameof(Tools), typeof(IList<AITool>), typeof(CopilotChatView));

    public IList<AITool>? Tools
    {
        get => (IList<AITool>?)GetValue(ToolsProperty);
        set => SetValue(ToolsProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  STATE
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty MessagesProperty =
        BindableProperty.Create(nameof(Messages), typeof(ObservableCollection<CopilotChatMessage>),
            typeof(CopilotChatView), defaultValueCreator: _ => new ObservableCollection<CopilotChatMessage>());

    public ObservableCollection<CopilotChatMessage> Messages
    {
        get => (ObservableCollection<CopilotChatMessage>)GetValue(MessagesProperty);
        set => SetValue(MessagesProperty, value);
    }

    public static readonly BindableProperty IsBusyProperty =
        BindableProperty.Create(nameof(IsBusy), typeof(bool), typeof(CopilotChatView), false);

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        private set => SetValue(IsBusyProperty, value);
    }

    public static readonly BindableProperty IsApprovalPendingProperty =
        BindableProperty.Create(nameof(IsApprovalPending), typeof(bool), typeof(CopilotChatView), false);

    public bool IsApprovalPending
    {
        get => (bool)GetValue(IsApprovalPendingProperty);
        private set => SetValue(IsApprovalPendingProperty, value);
    }

    public static readonly BindableProperty ApprovalTextProperty =
        BindableProperty.Create(nameof(ApprovalText), typeof(string), typeof(CopilotChatView), "");

    public string ApprovalText
    {
        get => (string)GetValue(ApprovalTextProperty);
        private set => SetValue(ApprovalTextProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  INPUT
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty InputTextProperty =
        BindableProperty.Create(nameof(InputText), typeof(string), typeof(CopilotChatView), "",
            BindingMode.TwoWay);

    public string InputText
    {
        get => (string)GetValue(InputTextProperty);
        set => SetValue(InputTextProperty, value);
    }

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(CopilotChatView), "Ask anything...",
            propertyChanged: (b, _, v) => { if (b is CopilotChatView c && c._inputEntry is not null) c._inputEntry.Placeholder = v as string; });

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly BindableProperty SuggestionPromptsProperty =
        BindableProperty.Create(nameof(SuggestionPrompts), typeof(IList<string>), typeof(CopilotChatView));

    public IList<string>? SuggestionPrompts
    {
        get => (IList<string>?)GetValue(SuggestionPromptsProperty);
        set => SetValue(SuggestionPromptsProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  TEMPLATES
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty UserMessageTemplateProperty =
        BindableProperty.Create(nameof(UserMessageTemplate), typeof(DataTemplate), typeof(CopilotChatView),
            propertyChanged: OnMessageTemplateChanged);

    public DataTemplate? UserMessageTemplate
    {
        get => (DataTemplate?)GetValue(UserMessageTemplateProperty);
        set => SetValue(UserMessageTemplateProperty, value);
    }

    public static readonly BindableProperty AssistantMessageTemplateProperty =
        BindableProperty.Create(nameof(AssistantMessageTemplate), typeof(DataTemplate), typeof(CopilotChatView),
            propertyChanged: OnMessageTemplateChanged);

    public DataTemplate? AssistantMessageTemplate
    {
        get => (DataTemplate?)GetValue(AssistantMessageTemplateProperty);
        set => SetValue(AssistantMessageTemplateProperty, value);
    }

    public static readonly BindableProperty ToolMessageTemplateProperty =
        BindableProperty.Create(nameof(ToolMessageTemplate), typeof(DataTemplate), typeof(CopilotChatView),
            propertyChanged: OnMessageTemplateChanged);

    public DataTemplate? ToolMessageTemplate
    {
        get => (DataTemplate?)GetValue(ToolMessageTemplateProperty);
        set => SetValue(ToolMessageTemplateProperty, value);
    }

    public static readonly BindableProperty SystemMessageTemplateProperty =
        BindableProperty.Create(nameof(SystemMessageTemplate), typeof(DataTemplate), typeof(CopilotChatView),
            propertyChanged: OnMessageTemplateChanged);

    public DataTemplate? SystemMessageTemplate
    {
        get => (DataTemplate?)GetValue(SystemMessageTemplateProperty);
        set => SetValue(SystemMessageTemplateProperty, value);
    }

    public static readonly BindableProperty ErrorMessageTemplateProperty =
        BindableProperty.Create(nameof(ErrorMessageTemplate), typeof(DataTemplate), typeof(CopilotChatView),
            propertyChanged: OnMessageTemplateChanged);

    public DataTemplate? ErrorMessageTemplate
    {
        get => (DataTemplate?)GetValue(ErrorMessageTemplateProperty);
        set => SetValue(ErrorMessageTemplateProperty, value);
    }

    public static readonly BindableProperty SuggestionItemTemplateProperty =
        BindableProperty.Create(nameof(SuggestionItemTemplate), typeof(DataTemplate), typeof(CopilotChatView));

    public DataTemplate? SuggestionItemTemplate
    {
        get => (DataTemplate?)GetValue(SuggestionItemTemplateProperty);
        set => SetValue(SuggestionItemTemplateProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  APPEARANCE
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty WelcomeTitleProperty =
        BindableProperty.Create(nameof(WelcomeTitle), typeof(string), typeof(CopilotChatView), "Welcome");

    public string WelcomeTitle
    {
        get => (string)GetValue(WelcomeTitleProperty);
        set => SetValue(WelcomeTitleProperty, value);
    }

    public static readonly BindableProperty WelcomeMessageProperty =
        BindableProperty.Create(nameof(WelcomeMessage), typeof(string), typeof(CopilotChatView),
            "Ask me anything, or tap a suggestion to get started.");

    public string WelcomeMessage
    {
        get => (string)GetValue(WelcomeMessageProperty);
        set => SetValue(WelcomeMessageProperty, value);
    }

    public static readonly BindableProperty WelcomeIconProperty =
        BindableProperty.Create(nameof(WelcomeIcon), typeof(string), typeof(CopilotChatView), "💬");

    public string WelcomeIcon
    {
        get => (string)GetValue(WelcomeIconProperty);
        set => SetValue(WelcomeIconProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  AVATARS & IDENTITY
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty ShowAvatarsProperty =
        BindableProperty.Create(nameof(ShowAvatars), typeof(bool), typeof(CopilotChatView), true);

    public bool ShowAvatars
    {
        get => (bool)GetValue(ShowAvatarsProperty);
        set => SetValue(ShowAvatarsProperty, value);
    }

    public static readonly BindableProperty AvatarSizeProperty =
        BindableProperty.Create(nameof(AvatarSize), typeof(double), typeof(CopilotChatView), 28.0);

    public double AvatarSize
    {
        get => (double)GetValue(AvatarSizeProperty);
        set => SetValue(AvatarSizeProperty, value);
    }

    public static readonly BindableProperty UserDisplayNameProperty =
        BindableProperty.Create(nameof(UserDisplayName), typeof(string), typeof(CopilotChatView), "You");

    public string UserDisplayName
    {
        get => (string)GetValue(UserDisplayNameProperty);
        set => SetValue(UserDisplayNameProperty, value);
    }

    public static readonly BindableProperty AssistantDisplayNameProperty =
        BindableProperty.Create(nameof(AssistantDisplayName), typeof(string), typeof(CopilotChatView), "Assistant");

    public string AssistantDisplayName
    {
        get => (string)GetValue(AssistantDisplayNameProperty);
        set => SetValue(AssistantDisplayNameProperty, value);
    }

    public static readonly BindableProperty UserAvatarSourceProperty =
        BindableProperty.Create(nameof(UserAvatarSource), typeof(ImageSource), typeof(CopilotChatView));

    public ImageSource? UserAvatarSource
    {
        get => (ImageSource?)GetValue(UserAvatarSourceProperty);
        set => SetValue(UserAvatarSourceProperty, value);
    }

    public static readonly BindableProperty AssistantAvatarSourceProperty =
        BindableProperty.Create(nameof(AssistantAvatarSource), typeof(ImageSource), typeof(CopilotChatView));

    public ImageSource? AssistantAvatarSource
    {
        get => (ImageSource?)GetValue(AssistantAvatarSourceProperty);
        set => SetValue(AssistantAvatarSourceProperty, value);
    }

    public static readonly BindableProperty UserAvatarTextProperty =
        BindableProperty.Create(nameof(UserAvatarText), typeof(string), typeof(CopilotChatView), "You");

    public string UserAvatarText
    {
        get => (string)GetValue(UserAvatarTextProperty);
        set => SetValue(UserAvatarTextProperty, value);
    }

    public static readonly BindableProperty AssistantAvatarTextProperty =
        BindableProperty.Create(nameof(AssistantAvatarText), typeof(string), typeof(CopilotChatView), "AI");

    public string AssistantAvatarText
    {
        get => (string)GetValue(AssistantAvatarTextProperty);
        set => SetValue(AssistantAvatarTextProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  TIMESTAMPS & DISPLAY
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty ShowTimestampsProperty =
        BindableProperty.Create(nameof(ShowTimestamps), typeof(bool), typeof(CopilotChatView), false);

    public bool ShowTimestamps
    {
        get => (bool)GetValue(ShowTimestampsProperty);
        set => SetValue(ShowTimestampsProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  LOCALIZABLE TEXT
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty SendButtonTextProperty =
        BindableProperty.Create(nameof(SendButtonText), typeof(string), typeof(CopilotChatView), "Send",
            propertyChanged: (b, _, v) => { if (b is CopilotChatView c && c._sendButton is not null) c._sendButton.Text = v as string; });

    public string SendButtonText
    {
        get => (string)GetValue(SendButtonTextProperty);
        set => SetValue(SendButtonTextProperty, value);
    }

    public static readonly BindableProperty ApproveButtonTextProperty =
        BindableProperty.Create(nameof(ApproveButtonText), typeof(string), typeof(CopilotChatView), "Approve",
            propertyChanged: (b, _, v) => { if (b is CopilotChatView c && c._approveButton is not null) c._approveButton.Text = v as string; });

    public string ApproveButtonText
    {
        get => (string)GetValue(ApproveButtonTextProperty);
        set => SetValue(ApproveButtonTextProperty, value);
    }

    public static readonly BindableProperty RejectButtonTextProperty =
        BindableProperty.Create(nameof(RejectButtonText), typeof(string), typeof(CopilotChatView), "Reject",
            propertyChanged: (b, _, v) => { if (b is CopilotChatView c && c._rejectButton is not null) c._rejectButton.Text = v as string; });

    public string RejectButtonText
    {
        get => (string)GetValue(RejectButtonTextProperty);
        set => SetValue(RejectButtonTextProperty, value);
    }

    public static readonly BindableProperty TypingIndicatorTextProperty =
        BindableProperty.Create(nameof(TypingIndicatorText), typeof(string), typeof(CopilotChatView), "Thinking…");

    public string TypingIndicatorText
    {
        get => (string)GetValue(TypingIndicatorTextProperty);
        set => SetValue(TypingIndicatorTextProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  INTERNAL COMMANDS (for templates to bind to)
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty InternalSendCommandProperty =
        BindableProperty.Create(nameof(InternalSendCommand), typeof(ICommand), typeof(CopilotChatView));

    public ICommand InternalSendCommand
    {
        get => (ICommand)GetValue(InternalSendCommandProperty);
        private set => SetValue(InternalSendCommandProperty, value);
    }

    public static readonly BindableProperty InternalApproveCommandProperty =
        BindableProperty.Create(nameof(InternalApproveCommand), typeof(ICommand), typeof(CopilotChatView));

    public ICommand InternalApproveCommand
    {
        get => (ICommand)GetValue(InternalApproveCommandProperty);
        private set => SetValue(InternalApproveCommandProperty, value);
    }

    public static readonly BindableProperty InternalRejectCommandProperty =
        BindableProperty.Create(nameof(InternalRejectCommand), typeof(ICommand), typeof(CopilotChatView));

    public ICommand InternalRejectCommand
    {
        get => (ICommand)GetValue(InternalRejectCommandProperty);
        private set => SetValue(InternalRejectCommandProperty, value);
    }

    public static readonly BindableProperty InternalSuggestionCommandProperty =
        BindableProperty.Create(nameof(InternalSuggestionCommand), typeof(ICommand), typeof(CopilotChatView));

    public ICommand InternalSuggestionCommand
    {
        get => (ICommand)GetValue(InternalSuggestionCommandProperty);
        private set => SetValue(InternalSuggestionCommandProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  EVENTS
    // ══════════════════════════════════════════════════════════════

    public event EventHandler<CopilotChatMessage>? MessageSending;
    public event EventHandler<CopilotChatMessage>? MessageSent;
    public event EventHandler<CopilotChatMessage>? ResponseReceived;
    public event EventHandler<string>? ResponseStreaming;
    public event EventHandler<CopilotChatMessage>? ToolExecuting;
    public event EventHandler<CopilotChatMessage>? ToolExecuted;
    public event EventHandler<string>? ApprovalRequested;

    // ══════════════════════════════════════════════════════════════
    //  PUBLIC METHODS
    // ══════════════════════════════════════════════════════════════

    /// <summary>Send a message programmatically.</summary>
    public Task SendMessageAsync(string text)
    {
        InputText = text;
        return HandleSendAsync();
    }

    /// <summary>Clear all messages and reset conversation history.</summary>
    public void ClearMessages()
    {
        // Swap CTS atomically to avoid disposing while in-flight stream uses the token
        var oldCts = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        try { oldCts.Cancel(); } catch { }
        // Dispose asynchronously to avoid ObjectDisposedException in in-flight token registrations
        Task.Run(() => { try { oldCts.Dispose(); } catch { } });

        _history.Clear();
        Messages.Clear();
        _pendingApproval = null;
        IsApprovalPending = false;
        IsBusy = false;
        InitializeHistory();
    }

    /// <summary>Approve a pending tool execution.</summary>
    public Task ApproveAsync() => HandleApproveAsync();

    /// <summary>Reject a pending tool execution.</summary>
    public Task RejectAsync(string? reason = null) => HandleRejectAsync(reason ?? "User rejected");

    // ══════════════════════════════════════════════════════════════
    //  TEMPLATE WIRING
    // ══════════════════════════════════════════════════════════════

    private static void OnMessageTemplateChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CopilotChatView view)
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
        if (_approveButton is not null) _approveButton.Clicked -= OnApproveClicked;
        if (_rejectButton is not null) _rejectButton.Clicked -= OnRejectClicked;
        if (_inputEntry is not null) _inputEntry.Completed -= OnInputCompleted;

        // Find PART_ elements
        _messagesView = GetTemplateChild("PART_Messages") as CollectionView;
        _inputEntry = GetTemplateChild("PART_Input") as Entry;
        _sendButton = GetTemplateChild("PART_Send") as Button;
        _approveButton = GetTemplateChild("PART_Approve") as Button;
        _rejectButton = GetTemplateChild("PART_Reject") as Button;

        // Wire suggestion chips from code (ControlTemplate boundary prevents XAML binding)
        if (_suggestionsLayout is not null) _suggestionsLayout.ChildAdded -= OnSuggestionChildAdded;
        _suggestionsLayout = GetTemplateChild("PART_Suggestions") as Layout;
        if (_suggestionsLayout is not null)
        {
            _suggestionsLayout.ChildAdded += OnSuggestionChildAdded;
            // Wire existing children
            foreach (var child in _suggestionsLayout.Children)
                WireSuggestionTap(child as View);
        }

        // Wire message selector from code (CLR properties can't be bound in XAML ControlTemplate)
        UpdateMessageSelector();

        // Wire events
        if (_sendButton is not null) _sendButton.Clicked += OnSendClicked;
        if (_approveButton is not null) _approveButton.Clicked += OnApproveClicked;
        if (_rejectButton is not null) _rejectButton.Clicked += OnRejectClicked;
        if (_inputEntry is not null) _inputEntry.Completed += OnInputCompleted;

        // Sync properties that template bindings may not propagate correctly
        SyncTemplateProperties();
    }

    private void SyncTemplateProperties()
    {
        if (_inputEntry is not null)
            _inputEntry.Placeholder = Placeholder;
        if (_sendButton is not null)
            _sendButton.Text = SendButtonText;
        if (_approveButton is not null)
            _approveButton.Text = ApproveButtonText;
        if (_rejectButton is not null)
            _rejectButton.Text = RejectButtonText;
    }

    private void OnSendClicked(object? sender, EventArgs e) => _ = HandleSendAsync();
    private void OnApproveClicked(object? sender, EventArgs e) => _ = HandleApproveAsync();
    private void OnRejectClicked(object? sender, EventArgs e) => _ = HandleRejectAsync("User rejected");
    private void OnInputCompleted(object? sender, EventArgs e) => _ = HandleSendAsync();

    private void OnSuggestionChildAdded(object? sender, ElementEventArgs e)
        => WireSuggestionTap(e.Element as View);

    private void WireSuggestionTap(View? view)
    {
        if (view is null) return;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) =>
        {
            if (view.BindingContext is string prompt)
                InternalSuggestionCommand?.Execute(prompt);
        };
        view.GestureRecognizers.Add(tap);
    }

    // ══════════════════════════════════════════════════════════════
    //  CHAT ENGINE (uses IChatClient)
    // ══════════════════════════════════════════════════════════════

    private static void OnChatClientChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CopilotChatView view)
            view.InitializeHistory();
    }

    private void InitializeHistory()
    {
        _history.Clear();
        if (!string.IsNullOrEmpty(SystemMessage))
            _history.Add(new ChatMessage(ChatRole.System, SystemMessage));
    }

    private async Task HandleSendAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrWhiteSpace(text) || IsBusy || ChatClient is null)
            return;

        InputText = string.Empty;
        IsBusy = true;

        var userMsg = AddMessage(ChatMessageKind.User, text);
        MessageSending?.Invoke(this, userMsg);
        _history.Add(new ChatMessage(ChatRole.User, text));
        MessageSent?.Invoke(this, userMsg);

        try
        {
            await ProcessStreamingResponseAsync();
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected (e.g. ClearMessages during stream)
        }
        catch (Exception ex)
        {
            AddMessage(ChatMessageKind.Error, $"Error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ProcessStreamingResponseAsync()
    {
        if (ChatClient is null) return;

        var responseText = string.Empty;
        CopilotChatMessage? assistantMessage = null;
        var updates = new List<ChatResponseUpdate>();
        var toolCallMessages = new Dictionary<string, CopilotChatMessage>();

        var options = new ChatOptions();
        if (Tools is { Count: > 0 })
            options.Tools = [.. Tools];

        try
        {
            await foreach (var update in ChatClient.GetStreamingResponseAsync(_history, options, _cts.Token))
            {
                updates.Add(update);

                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case FunctionCallContent call:
                        {
                            var argsText = call.Arguments is not null
                                ? string.Join("\n", call.Arguments.Select(kv => $"  {kv.Key}: {kv.Value}"))
                                : "";
                            var msg = AddMessage(ChatMessageKind.Tool, call.Name ?? "tool", "🔧");
                            msg.ToolArgs = argsText;
                            if (call.CallId is not null)
                                toolCallMessages[call.CallId] = msg;
                            ToolExecuting?.Invoke(this, msg);
                            break;
                        }

                        case FunctionResultContent result:
                        {
                            string resultText;
                            try
                            {
                                resultText = result.Result switch
                                {
                                    null => "(null)",
                                    string s => s,
                                    _ => JsonSerializer.Serialize(result.Result,
                                        new JsonSerializerOptions { WriteIndented = true })
                                };
                            }
                            catch
                            {
                                resultText = result.Result?.ToString() ?? "";
                            }
                            if (result.CallId is not null && toolCallMessages.TryGetValue(result.CallId, out var toolMsg))
                            {
                                toolMsg.ToolResult = resultText;
                                ToolExecuted?.Invoke(this, toolMsg);
                            }
                            break;
                        }

                        case TextContent tc when tc.Text is not null:
                            responseText += tc.Text;
                            ResponseStreaming?.Invoke(this, tc.Text);
                            if (assistantMessage is null)
                                assistantMessage = AddMessage(ChatMessageKind.Assistant, responseText);
                            else
                                assistantMessage.Text = responseText;
                            break;

                        case FunctionApprovalRequestContent approval:
                        {
                            _pendingApproval = approval;
                            var toolName = approval.FunctionCall?.Name ?? "unknown";
                            ApprovalText = $"{toolName} — approve?";
                            IsApprovalPending = true;
                            AddMessage(ChatMessageKind.Tool, $"Approval required: {toolName}", "🔒");
                            ApprovalRequested?.Invoke(this, toolName);
                            break;
                        }
                    }
                }
            }
        }
        finally
        {
            // Always flush history, even on cancellation/error, to avoid orphan messages
            if (updates.Count > 0)
                _history.AddMessages(updates);
        }

        if (assistantMessage is not null)
        {
            assistantMessage.IsStreaming = false;
            ResponseReceived?.Invoke(this, assistantMessage);
        }
        else if (string.IsNullOrEmpty(responseText))
            AddMessage(ChatMessageKind.Assistant, "(no response)");
    }

    private async Task HandleApproveAsync()
    {
        if (_pendingApproval is null) return;

        var approval = _pendingApproval;
        _pendingApproval = null;
        IsApprovalPending = false;
        IsBusy = true;

        try
        {
            var response = approval.CreateResponse(approved: true);
            _history.Add(new ChatMessage(ChatRole.User, [response]));
            AddMessage(ChatMessageKind.Tool, "Approved", "✅");
            await ProcessStreamingResponseAsync();
        }
        catch (Exception ex)
        {
            AddMessage(ChatMessageKind.Error, $"Error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task HandleRejectAsync(string reason)
    {
        if (_pendingApproval is null) return;

        var approval = _pendingApproval;
        _pendingApproval = null;
        IsApprovalPending = false;
        IsBusy = true;

        try
        {
            var response = approval.CreateResponse(approved: false, reason);
            _history.Add(new ChatMessage(ChatRole.User, [response]));
            AddMessage(ChatMessageKind.Tool, "Rejected", "❌");
            await ProcessStreamingResponseAsync();
        }
        catch (Exception ex)
        {
            AddMessage(ChatMessageKind.Error, $"Error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private CopilotChatMessage AddMessage(ChatMessageKind kind, string text, string? icon = null)
    {
        var msg = new CopilotChatMessage(kind, text, icon)
        {
            Timestamp = DateTimeOffset.Now,
        };

        // Populate avatar/identity from control defaults
        switch (kind)
        {
            case ChatMessageKind.User:
                msg.AuthorName = UserDisplayName;
                msg.AvatarSource = UserAvatarSource;
                msg.AvatarText = UserAvatarText;
                break;
            case ChatMessageKind.Assistant:
                msg.AuthorName = AssistantDisplayName;
                msg.AvatarSource = AssistantAvatarSource;
                msg.AvatarText = AssistantAvatarText;
                msg.IsStreaming = true;
                break;
            case ChatMessageKind.Tool:
                msg.AuthorName = "Tool";
                msg.AvatarText = icon ?? "🔧";
                break;
        }

        Messages.Add(msg);

        // Auto-scroll
        if (_messagesView is not null)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
            {
                try { _messagesView.ScrollTo(msg, position: ScrollToPosition.End, animate: true); }
                catch { }
            });
        }

        return msg;
    }
}
