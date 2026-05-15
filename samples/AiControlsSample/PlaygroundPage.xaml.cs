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
        Loaded += (_, _) => WireTextSettings();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0)
        {
            // Hide sidebar on narrow screens, show on wide
            var isWide = width >= 700;
            SettingsPanel.IsVisible = isWide;
            SettingsPanel.WidthRequest = isWide ? SidebarWidth : 0;
        }
    }

    private void OnClearChatClicked(object? sender, EventArgs e) => ChatView.ClearMessages();

    private void WireTextSettings()
    {
        WelcomeIconEntry.TextChanged += (_, e) => ChatView.WelcomeIcon = e.NewTextValue ?? "🤖";
        WelcomeTitleEntry.TextChanged += (_, e) => ChatView.WelcomeTitle = e.NewTextValue ?? "";
        PlaceholderEntry.TextChanged += (_, e) => ChatView.Placeholder = e.NewTextValue ?? "";

        UserNameEntry.TextChanged += (_, e) => ChatView.UserDisplayName = e.NewTextValue ?? "You";
        UserAvatarEntry.TextChanged += (_, e) => ChatView.UserAvatarText = e.NewTextValue ?? "";
        AssistantNameEntry.TextChanged += (_, e) => ChatView.AssistantDisplayName = e.NewTextValue ?? "Assistant";
        AssistantAvatarEntry.TextChanged += (_, e) => ChatView.AssistantAvatarText = e.NewTextValue ?? "";

        ShowAvatarsSwitch.Toggled += (_, e) => ChatView.ShowAvatars = e.Value;
    }
}
