using System.Collections.ObjectModel;
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
    private List<ChatMessage> _history = [];
    private CancellationTokenSource _cts = new();

    public CopilotChatView()
    {
        // Load default theme and apply default ControlTemplate
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
        BindableProperty.Create(nameof(SystemMessage), typeof(string), typeof(CopilotChatView));

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
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(CopilotChatView), "Ask anything...");

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
        BindableProperty.Create(nameof(UserMessageTemplate), typeof(DataTemplate), typeof(CopilotChatView));

    public DataTemplate? UserMessageTemplate
    {
        get => (DataTemplate?)GetValue(UserMessageTemplateProperty);
        set => SetValue(UserMessageTemplateProperty, value);
    }

    public static readonly BindableProperty AssistantMessageTemplateProperty =
        BindableProperty.Create(nameof(AssistantMessageTemplate), typeof(DataTemplate), typeof(CopilotChatView));

    public DataTemplate? AssistantMessageTemplate
    {
        get => (DataTemplate?)GetValue(AssistantMessageTemplateProperty);
        set => SetValue(AssistantMessageTemplateProperty, value);
    }

    public static readonly BindableProperty ToolMessageTemplateProperty =
        BindableProperty.Create(nameof(ToolMessageTemplate), typeof(DataTemplate), typeof(CopilotChatView));

    public DataTemplate? ToolMessageTemplate
    {
        get => (DataTemplate?)GetValue(ToolMessageTemplateProperty);
        set => SetValue(ToolMessageTemplateProperty, value);
    }

    public static readonly BindableProperty SystemMessageTemplateProperty =
        BindableProperty.Create(nameof(SystemMessageTemplate), typeof(DataTemplate), typeof(CopilotChatView));

    public DataTemplate? SystemMessageTemplate
    {
        get => (DataTemplate?)GetValue(SystemMessageTemplateProperty);
        set => SetValue(SystemMessageTemplateProperty, value);
    }

    public static readonly BindableProperty ErrorMessageTemplateProperty =
        BindableProperty.Create(nameof(ErrorMessageTemplate), typeof(DataTemplate), typeof(CopilotChatView));

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
        BindableProperty.Create(nameof(WelcomeIcon), typeof(string), typeof(CopilotChatView), ChatIcons.ChatSparkle);

    public string WelcomeIcon
    {
        get => (string)GetValue(WelcomeIconProperty);
        set => SetValue(WelcomeIconProperty, value);
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
        try { _cts.Cancel(); } catch { }
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _history.Clear();
        Messages.Clear();
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

        // Wire events
        if (_sendButton is not null) _sendButton.Clicked += OnSendClicked;
        if (_approveButton is not null) _approveButton.Clicked += OnApproveClicked;
        if (_rejectButton is not null) _rejectButton.Clicked += OnRejectClicked;
        if (_inputEntry is not null) _inputEntry.Completed += OnInputCompleted;
    }

    private void OnSendClicked(object? sender, EventArgs e) => _ = HandleSendAsync();
    private void OnApproveClicked(object? sender, EventArgs e) => _ = HandleApproveAsync();
    private void OnRejectClicked(object? sender, EventArgs e) => _ = HandleRejectAsync("User rejected");
    private void OnInputCompleted(object? sender, EventArgs e) => _ = HandleSendAsync();

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
                        var msg = AddMessage(ChatMessageKind.Tool, call.Name ?? "tool", ChatIcons.Wrench);
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
                                _ => System.Text.Json.JsonSerializer.Serialize(result.Result,
                                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
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
                }
            }
        }

        // Add response to history
        _history.AddMessages(updates);

        if (assistantMessage is not null)
            ResponseReceived?.Invoke(this, assistantMessage);
        else if (string.IsNullOrEmpty(responseText))
            AddMessage(ChatMessageKind.Assistant, "(no response)");
    }

    private Task HandleApproveAsync()
    {
        IsApprovalPending = false;
        AddMessage(ChatMessageKind.Tool, "Approved", ChatIcons.Checkmark);
        return Task.CompletedTask;
    }

    private Task HandleRejectAsync(string reason)
    {
        IsApprovalPending = false;
        AddMessage(ChatMessageKind.Tool, "Rejected", ChatIcons.Dismiss);
        return Task.CompletedTask;
    }

    private CopilotChatMessage AddMessage(ChatMessageKind kind, string text, string? icon = null)
    {
        var msg = new CopilotChatMessage(kind, text, icon);
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
