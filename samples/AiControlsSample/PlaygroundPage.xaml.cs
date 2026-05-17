using Microsoft.Maui.AI.Chat;

namespace AiControlsSample;

public partial class PlaygroundPage : ContentPage
{
    private const double SidebarWidth = 300;

    public ChatSession ChatSession { get; }

    public PlaygroundPage(ChatSession chatSession)
    {
        ChatSession = chatSession;
        ChatSession.SystemPrompt = "You are a helpful assistant with access to tools for weather, math, facts, and app info.";

        InitializeComponent();

        SettingsPanel.WidthRequest = SidebarWidth;
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

    private void OnClearChatClicked(object? sender, EventArgs e) => ChatSession.Clear();

    private void OnApplySystemPromptClicked(object? sender, EventArgs e)
    {
        ChatSession.SystemPrompt = SystemPromptEditor.Text;
    }

    private async void OnQuickPromptClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && !string.IsNullOrWhiteSpace(btn.Text))
        {
            // Strip leading emoji + space from button text
            var prompt = btn.Text.Length > 2 ? btn.Text[2..].Trim() : btn.Text;
            await ChatSession.SendAsync(prompt);
        }
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
}
