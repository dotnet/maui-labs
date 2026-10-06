
namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Hosts the chat playground tab.</summary>
public partial class ChatPage : ContentPage
{
    private bool _restored;

    /// <summary>Initializes the page with its view model.</summary>
    public ChatPage(ChatViewModel viewModel, ChatDiagnostics diagnostics)
    {
        InitializeComponent();
        BindingContext = viewModel;
        ChatLayout.DiagnosticsContent = new ChatLogsView(diagnostics, ChatLayout.CloseDiagnostics);
        ChatLayout.ToggleDiagnostics();
        Loaded += PageLoaded;
    }

    private void ToggleLogsClicked(object? sender, EventArgs e) => ChatLayout.ToggleDiagnostics();

    private async void PageLoaded(object? sender, EventArgs e)
    {
        if (_restored)
            return;

        _restored = true;
        await ((ChatViewModel)BindingContext).RestoreCachedChatAsync();
    }
}
