using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI;

namespace AiControlsSample;

public partial class AgenticChatPage : ContentPage
{
    public AgenticChatPage(IAgentSessionFactory sessionFactory, IChatClient chatClient)
    {
        InitializeComponent();

        var session = sessionFactory.Create(chatClient);
        session.SystemInstructions = """
            You are a helpful assistant that can change the background color of the app.
            When the user asks you to change the background, use the change_background tool.
            Be creative with color suggestions if the user is vague.
            After changing the background, briefly describe what you did.
            """;

        // Register the frontend tool that changes this page's background
        session.RegisterTool(AIFunctionFactory.Create(
            [Description("Change the background color of the page. Use any valid color name or hex value like '#ADD8E6' or 'LightBlue'.")]
            (
                [Description("Color to set, e.g. 'LightBlue', '#FFE0E0', 'Salmon', '#ADD8E6'")] string color
            ) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (Color.TryParse(color, out var parsed))
                    {
                        PageRoot.BackgroundColor = parsed;
                    }
                    else
                    {
                        // Try with # prefix for hex values without it
                        if (Color.TryParse($"#{color}", out var hexParsed))
                            PageRoot.BackgroundColor = hexParsed;
                    }
                });
                return $"Background changed to {color}.";
            },
            "change_background",
            "Change the background color of the page."));

        ChatView.Session = session;
        ChatView.SuggestionPrompts =
        [
            new Suggestion("Change background", "Change the background to light blue"),
            new Suggestion("Sunset colors", "Change the background to a warm sunset orange"),
            new Suggestion("Dark mode", "Change the background to a dark slate color"),
        ];
    }
}
