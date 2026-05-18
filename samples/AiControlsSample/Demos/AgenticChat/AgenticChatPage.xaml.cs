using System.ComponentModel;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace AiControlsSample;

public partial class AgenticChatPage : ContentPage
{
    public AgentContext Session { get; }

    public AgenticChatPage(IChatClient chatClient)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
                [Description("Change the background color of the page. Always use a CSS hex color value like '#FF8C00' or '#ADD8E6'.")]
                (
                    [Description("CSS hex color value, e.g. '#FF8C00', '#ADD8E6', '#6366F1'")] string color
                ) =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        // Try parsing as-is, then with # prefix
                        if (!Color.TryParse(color, out var parsed))
                        {
                            Color.TryParse($"#{color.TrimStart('#')}", out parsed);
                        }

                        if (parsed is not null)
                        {
                            PageRoot.BackgroundColor = parsed;
                        }
                    });
                    return $"Background changed to {color}.";
                },
                "change_background",
                "Change the background color of the page. Use CSS hex colors like '#FF8C00'.")
        };

        var chatOptions = new ChatOptions
        {
            Instructions = """
                You are a helpful assistant that can change the background color of the app.
                When the user asks you to change the background, use the change_background tool.
                IMPORTANT: Always provide colors as CSS hex values (e.g., '#FF8C00' for orange,
                '#87CEEB' for sky blue, '#FF6347' for tomato red). Never use color names.
                Be creative with color suggestions if the user is vague.
                After changing the background, briefly describe what you did.
                """,
            Tools = [.. tools]
        };
        var agent = new UIAgent(chatClient, chatOptions);
        Session = new AgentContext(agent);

        InitializeComponent();
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        Session.Clear();
        PageRoot.BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#1E1E2E")
            : Colors.White;
    }
}
