using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Controls.Chat;

namespace Microsoft.Maui.AI.Controls.Controls;

public partial class ChatPanelControl : ContentView
{
    public static readonly BindableProperty SessionProperty =
        BindableProperty.Create(
            nameof(Session),
            typeof(IChatSession),
            typeof(ChatPanelControl),
            propertyChanged: OnSessionChanged);

    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(
            nameof(Text),
            typeof(string),
            typeof(ChatPanelControl),
            default(string),
            BindingMode.TwoWay);

    public static readonly BindableProperty IsBusyProperty =
        BindableProperty.Create(
            nameof(IsBusy),
            typeof(bool),
            typeof(ChatPanelControl),
            false);

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

    private readonly ObservableCollection<ContentTemplate> _contentTemplates = [];
    private readonly ObservableCollection<ContentContext> _items = [];

    public IList<ContentTemplate> ContentTemplates => _contentTemplates;

    public ChatPanelControl()
    {
        InitializeComponent();
        ChatMessages.ItemsSource = _items;
        _contentTemplates.CollectionChanged += (_, _) => RebuildTemplateSelector();
    }

    private void RebuildTemplateSelector()
    {
        var selector = new ContentTemplateSelector();
        foreach (var t in _contentTemplates)
            selector.Templates.Add(t);
        ChatMessages.ItemTemplate = selector;
    }

    private static void OnSessionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (ChatPanelControl)bindable;

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

    internal void UpdateWelcomeVisibility()
    {
        var showWelcome = !string.IsNullOrEmpty(WelcomeMessage) && _items.Count == 0;
        WelcomePanel.IsVisible = showWelcome;
        WelcomeIconLabel.Text = WelcomeIcon;
        WelcomeMessageLabel.Text = WelcomeMessage;
        ChatMessages.IsVisible = !showWelcome;

        UpdateSuggestionsVisibility();
    }

    private void UpdateSuggestionsVisibility()
    {
        var showSuggestions = SuggestionPrompts is { Count: > 0 } && _items.Count == 0;
        SuggestionsPanel.IsVisible = showSuggestions;

        if (showSuggestions)
            BuildSuggestionChips();
    }

    private void BuildSuggestionChips()
    {
        SuggestionsPanel.Children.Clear();
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
            SuggestionsPanel.Children.Add(chip);
        }
    }

    private void ApplyHeaderTemplate()
    {
        if (HeaderTemplate is not null)
        {
            HeaderContent.Content = HeaderTemplate.CreateContent() as View;
            HeaderContent.IsVisible = true;
        }
        else
        {
            HeaderContent.Content = null;
            HeaderContent.IsVisible = false;
        }
    }

    private void ApplyFooterTemplate()
    {
        if (FooterTemplate is not null)
        {
            FooterContent.Content = FooterTemplate.CreateContent() as View;
            FooterContent.IsVisible = true;
        }
        else
        {
            FooterContent.Content = null;
            FooterContent.IsVisible = false;
        }
    }

    /// <summary>
    /// Applies color/radius overrides from bindable properties onto the XAML elements.
    /// Called when property-changed fires for input/send styling properties.
    /// </summary>
    internal void ApplyInputStyling()
    {
        if (SendButtonBackgroundColor is not null)
            SendButton.BackgroundColor = SendButtonBackgroundColor;

        if (InputAreaBackgroundColor is not null)
            InputAreaBorder.BackgroundColor = InputAreaBackgroundColor;

        InputAreaShape.CornerRadius = new CornerRadius(InputAreaCornerRadius);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        ApplyInputStyling();
        ApplyHeaderTemplate();
        ApplyFooterTemplate();
        UpdateWelcomeVisibility();
    }

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

    private void ScrollToLatestMessage()
    {
        var messageCount = _items.Count;
        if (messageCount == 0)
            return;

        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
        {
            if (_items.Count == 0)
                return;

            ChatMessages.ScrollTo(_items.Count - 1, position: ScrollToPosition.End, animate: false);
        });
    }
}
