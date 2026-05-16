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
public partial class AgentChatView : ContentView
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

        InternalSendCommand = new Command(OnSendOrStopClicked);
        InternalSuggestionCommand = new Command<Suggestion>(async suggestion =>
        {
            var text = suggestion.Message ?? suggestion.Text;
            InputText = text;
            await HandleSendAsync();
        });
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
        if (_sendButton is not null) _sendButton.Clicked -= OnSendButtonClicked;
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
        if (_sendButton is not null) _sendButton.Clicked += OnSendButtonClicked;
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
        SyncSendButtonState();
    }

    private void SyncSendButtonState()
    {
        if (_sendButton is null) return;
        _sendButton.Text = IsBusy ? StopButtonText : SendButtonText;
    }

    private void OnSendButtonClicked(object? sender, EventArgs e) => OnSendOrStopClicked();
    private void OnInputCompleted(object? sender, EventArgs e) => _ = HandleSendAsync();

    private void OnSendOrStopClicked()
    {
        if (IsBusy)
        {
            Session?.Cancel();
        }
        else
        {
            _ = HandleSendAsync();
        }
    }

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
            SyncSendButtonState();
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
