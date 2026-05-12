using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.CopilotChat;
using Microsoft.Maui.CopilotChat.Controls;

namespace CopilotChat.Sample;

public partial class MainPage : ContentPage
{
    private const double SidebarBreakpoint = 700;
    private bool _chatOverlayVisible;

    private static readonly (string Name, Color Color)[] AccentPresets =
    [
        ("Indigo", Color.FromArgb("#6366F1")),
        ("Blue", Color.FromArgb("#2563EB")),
        ("Emerald", Color.FromArgb("#059669")),
        ("Rose", Color.FromArgb("#E11D48")),
        ("Amber", Color.FromArgb("#D97706")),
        ("Slate", Color.FromArgb("#475569")),
    ];

    private static readonly (string Name, Color Color)[] UserBubblePresets =
    [
        ("Indigo", Color.FromArgb("#E0E7FF")),
        ("Blue", Color.FromArgb("#DBEAFE")),
        ("Green", Color.FromArgb("#D1FAE5")),
        ("Rose", Color.FromArgb("#FFE4E6")),
        ("Amber", Color.FromArgb("#FEF3C7")),
        ("Gray", Color.FromArgb("#F1F5F9")),
    ];

    private static readonly (string Name, Color Color)[] AssistantBubblePresets =
    [
        ("White", Color.FromArgb("#FFFFFF")),
        ("Snow", Color.FromArgb("#F8FAFC")),
        ("Cream", Color.FromArgb("#FFFBEB")),
        ("Mint", Color.FromArgb("#F0FDF4")),
        ("Lavender", Color.FromArgb("#F5F3FF")),
        ("Slate", Color.FromArgb("#F1F5F9")),
    ];

    public MainPage(IChatClient chatClient, SampleTools tools)
    {
        InitializeComponent();

        ChatView.ChatClient = chatClient;
        ChatView.Tools = tools.GetTools();
        ChatView.SuggestionPrompts =
        [
            "What's the weather in Tokyo?",
            "Calculate (42 * 3) + 7",
            "Tell me a random fact",
            "What app am I running?",
        ];

        BuildColorSwatches(AccentSwatches, AccentPresets, "CopilotAccent");
        BuildColorSwatches(UserBubbleSwatches, UserBubblePresets, "CopilotUserBubbleLight");
        BuildColorSwatches(AssistantBubbleSwatches, AssistantBubblePresets, "CopilotAssistantBubbleLight");

        WireTextSettings();
        WireSliders();
    }

    // ─── Layout ───

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        var isWide = width >= SidebarBreakpoint;
        if (isWide)
        {
            RootLayout.ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(400)];
            Grid.SetColumn(ContentArea, 0);
            Grid.SetColumn(ChatSidebar, 1);
            ChatSidebar.IsVisible = true;
            ChatSidebar.WidthRequest = 400;
            ChatFab.IsVisible = false;
            _chatOverlayVisible = false;
        }
        else
        {
            RootLayout.ColumnDefinitions = [new ColumnDefinition(GridLength.Star)];
            Grid.SetColumn(ContentArea, 0);
            Grid.SetColumn(ChatSidebar, 0);
            ChatSidebar.WidthRequest = -1;
            ChatSidebar.IsVisible = _chatOverlayVisible;
            ChatFab.IsVisible = !_chatOverlayVisible;
        }
    }

    private void OnFabClicked(object? sender, EventArgs e)
    {
        _chatOverlayVisible = true;
        ChatSidebar.IsVisible = true;
        ChatSidebar.WidthRequest = -1;
        ChatFab.IsVisible = false;
    }

    private void OnClearChatClicked(object? sender, EventArgs e) => ChatView.ClearMessages();

    // ─── Text setting wiring ───

    private void WireTextSettings()
    {
        WelcomeIconEntry.TextChanged += (_, e) => ChatView.WelcomeIcon = e.NewTextValue ?? "💬";
        WelcomeTitleEntry.TextChanged += (_, e) => ChatView.WelcomeTitle = e.NewTextValue ?? "";
        PlaceholderEntry.TextChanged += (_, e) => ChatView.Placeholder = e.NewTextValue ?? "";

        UserNameEntry.TextChanged += (_, e) =>
        {
            ChatView.UserDisplayName = e.NewTextValue ?? "You";
            foreach (var m in ChatView.Messages.Where(m => m.Kind == ChatMessageKind.User))
                m.AuthorName = ChatView.UserDisplayName;
        };
        UserAvatarEntry.TextChanged += (_, e) =>
        {
            ChatView.UserAvatarText = e.NewTextValue ?? "";
            foreach (var m in ChatView.Messages.Where(m => m.Kind == ChatMessageKind.User))
                m.AvatarText = ChatView.UserAvatarText;
        };
        AssistantNameEntry.TextChanged += (_, e) =>
        {
            ChatView.AssistantDisplayName = e.NewTextValue ?? "Assistant";
            foreach (var m in ChatView.Messages.Where(m => m.Kind == ChatMessageKind.Assistant))
                m.AuthorName = ChatView.AssistantDisplayName;
        };
        AssistantAvatarEntry.TextChanged += (_, e) =>
        {
            ChatView.AssistantAvatarText = e.NewTextValue ?? "";
            foreach (var m in ChatView.Messages.Where(m => m.Kind == ChatMessageKind.Assistant))
                m.AvatarText = ChatView.AssistantAvatarText;
        };

        ShowAvatarsSwitch.Toggled += (_, e) => ChatView.ShowAvatars = e.Value;
        ShowTimestampsSwitch.Toggled += (_, e) => ChatView.ShowTimestamps = e.Value;

        SendTextEntry.TextChanged += (_, e) => ChatView.SendButtonText = e.NewTextValue ?? "Send";
        ApproveTextEntry.TextChanged += (_, e) => ChatView.ApproveButtonText = e.NewTextValue ?? "Approve";
        RejectTextEntry.TextChanged += (_, e) => ChatView.RejectButtonText = e.NewTextValue ?? "Reject";
        TypingTextEntry.TextChanged += (_, e) => ChatView.TypingIndicatorText = e.NewTextValue ?? "Thinking…";
    }

    // ─── Slider wiring ───

    private void WireSliders()
    {
        AvatarSizeSlider.ValueChanged += (_, e) =>
        {
            var v = Math.Round(e.NewValue);
            AvatarSizeLabel.Text = v.ToString();
            ChatView.AvatarSize = v;
        };

        SpacingSlider.ValueChanged += (_, e) =>
        {
            var v = Math.Round(e.NewValue);
            SpacingLabel.Text = v.ToString();
            OverrideResource("CopilotMessageSpacing", v);
        };

        FontSizeSlider.ValueChanged += (_, e) =>
        {
            var v = Math.Round(e.NewValue);
            FontSizeLabel.Text = v.ToString();
            OverrideResource("CopilotFontSizeBody", v);
        };
    }

    // ─── Color swatches ───

    private void BuildColorSwatches(Layout host, (string Name, Color Color)[] presets, string resourceKey)
    {
        foreach (var (name, color) in presets)
        {
            var swatch = new Border
            {
                WidthRequest = 32,
                HeightRequest = 32,
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                StrokeThickness = 2,
                Stroke = Colors.White,
                BackgroundColor = color,
            };
            swatch.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => OverrideResource(resourceKey, color)),
            });
            SemanticProperties.SetDescription(swatch, $"{name} {resourceKey}");
            host.Children.Add(swatch);
        }
    }

    // ─── Resource overrides ───

    private void OverrideResource(string key, object value)
    {
        ChatView.Resources[key] = value;
        RefreshTemplates();
    }

    private void RefreshTemplates()
    {
        // StaticResource bindings in templates need a template reassignment to pick up changes
        var ct = FindResource("CopilotChatViewDefaultTemplate") as ControlTemplate;
        if (ct is not null)
        {
            ChatView.ControlTemplate = null;
            ChatView.ControlTemplate = ct;
        }

        ResetTemplate(v => v.UserMessageTemplate, "CopilotDefaultUserMessageTemplate");
        ResetTemplate(v => v.AssistantMessageTemplate, "CopilotDefaultAssistantMessageTemplate");
        ResetTemplate(v => v.ToolMessageTemplate, "CopilotDefaultToolMessageTemplate");
        ResetTemplate(v => v.SystemMessageTemplate, "CopilotDefaultSystemMessageTemplate");
        ResetTemplate(v => v.ErrorMessageTemplate, "CopilotDefaultErrorMessageTemplate");
        ResetTemplate(v => v.SuggestionItemTemplate, "CopilotDefaultSuggestionItemTemplate");
    }

    private void ResetTemplate(Func<CopilotChatView, DataTemplate?> getter, string key)
    {
        if (FindResource(key) is not DataTemplate dt) return;
        // Force MAUI to re-inflate by clearing and reassigning
        var prop = typeof(CopilotChatView).GetProperty(getter.Method.Name.Replace("get_", ""));
        prop?.SetValue(ChatView, null);
        prop?.SetValue(ChatView, dt);
    }

    private object? FindResource(string key)
    {
        if (ChatView.Resources.TryGetValue(key, out var val)) return val;
        foreach (var d in ChatView.Resources.MergedDictionaries)
            if (d.TryGetValue(key, out val)) return val;
        return null;
    }
}
