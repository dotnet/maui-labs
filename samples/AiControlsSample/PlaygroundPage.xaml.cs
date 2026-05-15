using Microsoft.Extensions.AI;
using Microsoft.Maui.AI;
using Microsoft.Maui.AI.Controls.Controls;

namespace AiControlsSample;

public partial class PlaygroundPage : ContentPage
{
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

        Loaded += (_, _) => WireTextSettings();
    }

    private void OnSettingsToggleClicked(object? sender, EventArgs e)
    {
        SettingsPanel.IsVisible = true;
    }

    private void OnCloseSettingsClicked(object? sender, EventArgs e)
    {
        SettingsPanel.IsVisible = false;
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
