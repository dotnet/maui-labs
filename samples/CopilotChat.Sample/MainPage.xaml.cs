using Microsoft.Extensions.AI;

namespace CopilotChat.Sample;

public partial class MainPage : ContentPage
{
    public MainPage(IChatClient chatClient)
    {
        InitializeComponent();
        Suggestions = ["What can you do?", "Tell me a joke", "Write a haiku about .NET MAUI"];
        BindingContext = this;
        ChatView.ChatClient = chatClient;
    }

    public List<string> Suggestions { get; }
}
