using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace AiControlsSample;

public partial class PlaygroundPage : ContentPage
{
    private const double SidebarWidth = 300;

    private readonly IChatClient _chatClient;
    private readonly IList<AITool> _tools;

    public AgentContext Session { get; private set; }

    public PlaygroundPage(IChatClient chatClient)
    {
        _chatClient = chatClient;
        _tools = new SampleTools().GetTools();

        Session = CreateSession("You are a helpful assistant with access to tools for weather, math, facts, and app info.");

        InitializeComponent();

        SettingsPanel.WidthRequest = SidebarWidth;
    }

    private AgentContext CreateSession(string systemPrompt)
    {
        var chatOptions = new ChatOptions
        {
            Instructions = systemPrompt,
            Tools = [.. _tools]
        };
        var agent = new UIAgent(_chatClient, chatOptions);
        return new AgentContext(agent);
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

    private void OnClearChatClicked(object? sender, EventArgs e)
    {
        var old = Session;
        Session = CreateSession(SystemPromptEditor.Text);
        ChatPanel.Session = Session;
        old?.Dispose();
    }

    private void OnApplySystemPromptClicked(object? sender, EventArgs e)
    {
        var old = Session;
        Session = CreateSession(SystemPromptEditor.Text);
        ChatPanel.Session = Session;
        old?.Dispose();
    }

    private void OnPlaceholderChanged(object? sender, TextChangedEventArgs e)
    {
        ChatPanel.Placeholder = e.NewTextValue;
    }

    private void OnWelcomeMessageChanged(object? sender, TextChangedEventArgs e)
    {
        ChatPanel.WelcomeMessage = string.IsNullOrWhiteSpace(e.NewTextValue) ? null : e.NewTextValue;
    }

    private void OnTimestampsToggled(object? sender, ToggledEventArgs e)
    {
        ChatPanel.ShowTimestamps = e.Value;
    }

    private void OnToolCallsToggled(object? sender, ToggledEventArgs e)
    {
        ChatPanel.ShowToolCalls = e.Value;
    }

    private void OnToolResultsToggled(object? sender, ToggledEventArgs e)
    {
        ChatPanel.ShowToolResults = e.Value;
    }

    private void OnCornerRadiusChanged(object? sender, ValueChangedEventArgs e)
    {
        var val = Math.Round(e.NewValue);
        CornerRadiusValue.Text = val.ToString();
        ChatPanel.BubbleCornerRadius = val;
    }

    private void OnMaxWidthChanged(object? sender, ValueChangedEventArgs e)
    {
        var val = Math.Round(e.NewValue);
        MaxWidthValue.Text = val.ToString();
        ChatPanel.MaxBubbleWidth = val;
    }

    private async void OnQuickPromptClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && !string.IsNullOrWhiteSpace(btn.Text)
            && Session.Status == ConversationStatus.Idle)
        {
            var prompt = btn.Text.Length > 2 ? btn.Text[2..].Trim() : btn.Text;
            try
            {
                await Session.SendMessageAsync(prompt);
            }
            catch (InvalidOperationException)
            {
                // Session was not in a valid state to send
            }
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
