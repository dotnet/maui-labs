using Microsoft.Extensions.AI;
using Microsoft.Maui.AI;

namespace AiControlsSample;

public partial class ToolRenderingPage : ContentPage
{
    public ToolRenderingPage(IAgentSessionFactory sessionFactory, IChatClient chatClient, SampleTools tools)
    {
        InitializeComponent();

        var session = sessionFactory.Create(chatClient);
        session.SystemInstructions = """
            You are a helpful assistant with access to tools.
            When asked about the weather, use the get_weather tool.
            When asked to calculate something, use the calculate tool.
            Always explain what you found after using a tool.
            """;

        session.RegisterTools([.. tools.GetTools()]);

        ChatView.Session = session;
        ChatView.SuggestionPrompts =
        [
            new Suggestion("Weather in San Francisco", "What's the weather like in San Francisco?"),
            new Suggestion("Weather in Tokyo", "What's the weather like in Tokyo?"),
            new Suggestion("Calculate", "Calculate (100 * 3.14) / 2"),
            new Suggestion("Random Fact", "Tell me a random fun fact"),
        ];
    }
}
