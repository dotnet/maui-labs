namespace CopilotSdkSample;

public partial class MainPage : ContentPage
{
    private readonly ChatViewModel _viewModel;

    public MainPage(ChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        var text = MessageEntry.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        MessageEntry.Text = "";
        await _viewModel.SendMessageAsync(text);

        if (_viewModel.Messages.Count > 0)
            MessagesView.ScrollTo(_viewModel.Messages.Count - 1);
    }

    private async void OnResetClicked(object? sender, EventArgs e)
    {
        await _viewModel.ResetSessionAsync();
    }
}
