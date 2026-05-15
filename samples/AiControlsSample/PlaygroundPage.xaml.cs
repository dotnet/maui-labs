using Microsoft.Extensions.AI;
using Microsoft.Maui.AI;
using Microsoft.Maui.AI.Controls.Controls;

namespace AiControlsSample;

public partial class PlaygroundPage : ContentPage
{
    private const double SidebarWidth = 300;

    public PlaygroundPage(IAgentSessionFactory sessionFactory, IChatClient chatClient, SampleTools tools)
    {
        InitializeComponent();

        var session = sessionFactory.Create(chatClient);
        session.RegisterTools([.. tools.GetTools()]);

        ChatView.Session = session;
        ChatView.SuggestionPrompts =
        [
            new Suggestion("What's the weather in Tokyo?"),
            new Suggestion("Calculate (42 * 3) + 7"),
            new Suggestion("Tell me a random fact"),
            new Suggestion("What app am I running?"),
        ];

        SettingsPanel.WidthRequest = SidebarWidth;
        Loaded += (_, _) => WireSettings();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0)
        {
            var isWide = width >= 700;
            SettingsPanel.IsVisible = isWide;
            SettingsPanel.WidthRequest = isWide ? SidebarWidth : 0;
        }
    }

    private void OnClearChatClicked(object? sender, EventArgs e) => ChatView.ClearMessages();

    private void WireSettings()
    {
        // Welcome
        WelcomeIconEntry.TextChanged += (_, e) => ChatView.WelcomeIcon = e.NewTextValue ?? "🤖";
        WelcomeTitleEntry.TextChanged += (_, e) => ChatView.WelcomeTitle = e.NewTextValue ?? "";
        WelcomeMessageEntry.TextChanged += (_, e) => ChatView.WelcomeMessage = e.NewTextValue ?? "";
        PlaceholderEntry.TextChanged += (_, e) => ChatView.Placeholder = e.NewTextValue ?? "";

        // Identity
        UserNameEntry.TextChanged += (_, e) => ChatView.UserDisplayName = e.NewTextValue ?? "You";
        UserAvatarEntry.TextChanged += (_, e) => ChatView.UserAvatarText = e.NewTextValue ?? "";
        AssistantNameEntry.TextChanged += (_, e) => ChatView.AssistantDisplayName = e.NewTextValue ?? "Assistant";
        AssistantAvatarEntry.TextChanged += (_, e) => ChatView.AssistantAvatarText = e.NewTextValue ?? "";
        ShowAvatarsSwitch.Toggled += (_, e) => ChatView.ShowAvatars = e.Value;
        ShowTimestampsSwitch.Toggled += (_, e) => ChatView.ShowTimestamps = e.Value;

        // Appearance
        ShowToolMessagesSwitch.Toggled += (_, e) => ChatView.ShowToolMessages = e.Value;
        ShowReasoningSwitch.Toggled += (_, e) => ChatView.ShowReasoning = e.Value;
    }

    private void OnSectionHeaderTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string name)
        {
            var content = this.FindByName<VerticalStackLayout>(name);
            if (content is not null)
            {
                content.IsVisible = !content.IsVisible;
                if (sender is Label label)
                    label.Text = content.IsVisible ? $"▼ {label.Text[2..]}" : $"▶ {label.Text[2..]}";
            }
        }
    }

    private void OnBubbleStyleClicked(object? sender, EventArgs e)
    {
        var inactive = Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#334155") : Color.FromArgb("#E2E8F0");
        var active = Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#818CF8") : Color.FromArgb("#6366F1");

        BubbleSharpBtn.BackgroundColor = inactive;
        BubbleSharpBtn.TextColor = Colors.Black;
        BubbleRoundedBtn.BackgroundColor = inactive;
        BubbleRoundedBtn.TextColor = Colors.Black;
        BubblePillBtn.BackgroundColor = inactive;
        BubblePillBtn.TextColor = Colors.Black;

        if (sender == BubbleSharpBtn)
        {
            ChatView.BubbleCornerRadius = 0;
            BubbleSharpBtn.BackgroundColor = active;
            BubbleSharpBtn.TextColor = Colors.White;
        }
        else if (sender == BubbleRoundedBtn)
        {
            ChatView.BubbleCornerRadius = 12;
            BubbleRoundedBtn.BackgroundColor = active;
            BubbleRoundedBtn.TextColor = Colors.White;
        }
        else if (sender == BubblePillBtn)
        {
            ChatView.BubbleCornerRadius = 20;
            BubblePillBtn.BackgroundColor = active;
            BubblePillBtn.TextColor = Colors.White;
        }
    }

    private void OnStrokeStyleClicked(object? sender, EventArgs e)
    {
        var inactive = Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#334155") : Color.FromArgb("#E2E8F0");
        var active = Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#818CF8") : Color.FromArgb("#6366F1");

        StrokeNoneBtn.BackgroundColor = inactive;
        StrokeNoneBtn.TextColor = Colors.Black;
        StrokeThinBtn.BackgroundColor = inactive;
        StrokeThinBtn.TextColor = Colors.Black;
        StrokeBoldBtn.BackgroundColor = inactive;
        StrokeBoldBtn.TextColor = Colors.Black;

        if (sender == StrokeNoneBtn)
        {
            ChatView.BubbleStrokeThickness = 0;
            StrokeNoneBtn.BackgroundColor = active;
            StrokeNoneBtn.TextColor = Colors.White;
        }
        else if (sender == StrokeThinBtn)
        {
            ChatView.BubbleStrokeThickness = 1;
            StrokeThinBtn.BackgroundColor = active;
            StrokeThinBtn.TextColor = Colors.White;
        }
        else if (sender == StrokeBoldBtn)
        {
            ChatView.BubbleStrokeThickness = 2;
            StrokeBoldBtn.BackgroundColor = active;
            StrokeBoldBtn.TextColor = Colors.White;
        }
    }
}
