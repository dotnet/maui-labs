using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI;

namespace AI.Sample;

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
            """;

        // Register the frontend tool that changes this page's background
        session.RegisterTool(AIFunctionFactory.Create(
            [Description("Change the background color of the page. Use any valid CSS color name or hex value.")]
            (
                [Description("Color to set, e.g. 'LightBlue', '#FFE0E0', 'Salmon'")] string color
            ) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        var parsed = Color.FromArgb(color.StartsWith('#') ? color : ColorNameToHex(color));
                        PageRoot.BackgroundColor = parsed;
                    }
                    catch
                    {
                        // If parsing fails, try as a named color
                        if (Color.TryParse(color, out var parsed))
                            PageRoot.BackgroundColor = parsed;
                    }
                });
                return $"Background changed to {color}.";
            },
            "change_background",
            "Change the background color of the page."));

        ChatView.Session = session;
        ChatView.SuggestionPrompts =
        [
            new Suggestion("Change background", "Change background to light blue"),
            new Suggestion("Sunset colors", "Change the background to a warm sunset orange"),
            new Suggestion("Dark mode", "Change the background to a dark slate color"),
        ];
    }

    private static string ColorNameToHex(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "lightblue" => "#ADD8E6",
            "salmon" => "#FA8072",
            "lavender" => "#E6E6FA",
            "mint" => "#98FF98",
            "peach" => "#FFDAB9",
            "coral" => "#FF7F50",
            "gold" => "#FFD700",
            "skyblue" => "#87CEEB",
            _ => name
        };
    }
}
