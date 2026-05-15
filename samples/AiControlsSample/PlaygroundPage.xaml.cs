using Microsoft.Extensions.AI;
using Microsoft.Maui.AI;
using Microsoft.Maui.AI.Controls.Controls;

namespace AiControlsSample;

public partial class PlaygroundPage : ContentPage
{
    private const double WideBreakpoint = 800;
    private bool _settingsVisible;

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

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateLayout(width);
    }

    private void UpdateLayout(double width)
    {
        var isWide = width >= WideBreakpoint;

        if (_settingsVisible)
        {
            if (isWide)
            {
                // Sidebar mode: chat + settings side-by-side
                RootLayout.ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(280)];
                Grid.SetColumn(SettingsPanel, 1);
                SettingsPanel.HorizontalOptions = LayoutOptions.Fill;
                SettingsPanel.VerticalOptions = LayoutOptions.Fill;
            }
            else
            {
                // Overlay mode: settings overlays chat
                RootLayout.ColumnDefinitions = [new ColumnDefinition(GridLength.Star)];
                Grid.SetColumn(SettingsPanel, 0);
                SettingsPanel.HorizontalOptions = LayoutOptions.End;
                SettingsPanel.VerticalOptions = LayoutOptions.Fill;
            }
            SettingsPanel.IsVisible = true;
        }
        else
        {
            RootLayout.ColumnDefinitions = [new ColumnDefinition(GridLength.Star)];
            SettingsPanel.IsVisible = false;
        }
    }

    private void OnSettingsToggleClicked(object? sender, EventArgs e)
    {
        _settingsVisible = !_settingsVisible;
        UpdateLayout(Width);
    }

    private void OnCloseSettingsClicked(object? sender, EventArgs e)
    {
        _settingsVisible = false;
        UpdateLayout(Width);
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
