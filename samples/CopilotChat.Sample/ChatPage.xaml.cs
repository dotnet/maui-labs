using Microsoft.Extensions.AI;

namespace CopilotChat.Sample;

public partial class ChatPage : ContentPage
{
    public ChatPage(IChatClient chatClient, SampleTools tools, IServiceProvider services)
    {
        InitializeComponent();

        // Wrap with function invocation middleware so tools actually execute
        var wrappedClient = new ChatClientBuilder(chatClient)
            .UseFunctionInvocation()
            .Build(services);

        ChatView.ChatClient = wrappedClient;
        ChatView.Tools = tools.GetTools();
        ChatView.SuggestionPrompts = new List<string>
        {
            "What's the weather in Tokyo?",
            "Calculate (42 * 3) + 7",
            "Tell me a random fact",
            "Navigate to the settings page",
            "What app am I running?",
        };
    }
}
