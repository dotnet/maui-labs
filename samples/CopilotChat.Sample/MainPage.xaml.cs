using Microsoft.Extensions.AI;

namespace CopilotChat.Sample;

public partial class MainPage : ContentPage
{
    private const double SidebarBreakpoint = 700;
    private bool _chatOverlayVisible;

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
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateLayout(width);
    }

    private void UpdateLayout(double width)
    {
        var isWide = width >= SidebarBreakpoint;

        if (isWide)
        {
            // Side-by-side: content on left, chat sidebar on right
            RootLayout.ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(400)];
            Grid.SetColumn(ContentArea, 0);
            Grid.SetColumn(ChatSidebar, 1);
            ChatSidebar.IsVisible = true;
            ChatFab.IsVisible = false;

            // If overlay was showing, hide it
            _chatOverlayVisible = false;
        }
        else
        {
            // Narrow: content full width, FAB to open chat overlay
            RootLayout.ColumnDefinitions = [new ColumnDefinition(GridLength.Star)];
            Grid.SetColumn(ContentArea, 0);
            Grid.SetColumn(ChatSidebar, 0);
            ChatSidebar.IsVisible = _chatOverlayVisible;
            ChatFab.IsVisible = !_chatOverlayVisible;
        }
    }

    private void OnFabClicked(object? sender, EventArgs e)
    {
        _chatOverlayVisible = true;
        ChatSidebar.IsVisible = true;
        ChatSidebar.WidthRequest = -1; // Full width on narrow
        ChatFab.IsVisible = false;
    }

    private void OnSettingChanged(object? sender, EventArgs e)
    {
        ChatView.UserDisplayName = UserNameEntry.Text;
        ChatView.UserAvatarText = UserNameEntry.Text;
        ChatView.AssistantDisplayName = AssistantNameEntry.Text;
        ChatView.AssistantAvatarText = AssistantNameEntry.Text.Length >= 2
            ? AssistantNameEntry.Text[..2]
            : AssistantNameEntry.Text;
        ChatView.ShowAvatars = ShowAvatarsSwitch.IsToggled;
        ChatView.ShowTimestamps = ShowTimestampsSwitch.IsToggled;
    }

    private void OnClearChatClicked(object? sender, EventArgs e)
    {
        ChatView.ClearMessages();
    }
}
