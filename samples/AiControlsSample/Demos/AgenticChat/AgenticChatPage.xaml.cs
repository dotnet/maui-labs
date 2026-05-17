using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AiControlsSample;

public partial class AgenticChatPage : ContentPage
{
    public ChatSession ChatSession { get; }

    public AgenticChatPage(IChatClient chatClient)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
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
                            if (Color.TryParse($"#{color}", out var hexParsed))
                                PageRoot.BackgroundColor = hexParsed;
                        }
                    });
                    return $"Background changed to {color}.";
                },
                "change_background",
                "Change the background color of the page.")
        };

        ChatSession = new ChatSession(tools, chatClient)
        {
            SystemPrompt = """
                You are a helpful assistant that can change the background color of the app.
                When the user asks you to change the background, use the change_background tool.
                Be creative with color suggestions if the user is vague.
                After changing the background, briefly describe what you did.
                """
        };

        InitializeComponent();
    }
}
