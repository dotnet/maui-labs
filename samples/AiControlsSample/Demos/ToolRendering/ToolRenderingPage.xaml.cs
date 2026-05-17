using Microsoft.Maui.AI.Chat;

namespace AiControlsSample;

public partial class ToolRenderingPage : ContentPage
{
    public ChatSession ChatSession { get; }

    public ToolRenderingPage(ChatSession chatSession)
    {
        ChatSession = chatSession;
        ChatSession.SystemPrompt = """
            You are a helpful assistant with access to tools.
            When asked about the weather, use the GetCurrentWeather tool.
            When asked to calculate something, use the calculate tool.
            Always explain what you found after using a tool.
            """;

        InitializeComponent();
    }
}
