using Microsoft.Extensions.AI;

namespace CopilotChat.Sample;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        Suggestions = ["What can you do?", "Tell me a joke", "Write a haiku about .NET MAUI"];
        BindingContext = this;
    }

    public List<string> Suggestions { get; }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Resolve IChatClient from DI and set on the control
        var chatClient = Handler?.MauiContext?.Services.GetService<IChatClient>();
        if (chatClient is not null)
            ChatView.ChatClient = chatClient;
    }
}
