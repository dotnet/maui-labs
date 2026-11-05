
namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Hosts the chat playground tab.</summary>
public partial class ChatPage : ContentPage
{
    private bool _restored;
    private IDispatcherTimer? _statusTimer;

    /// <summary>Initializes the page with its view model.</summary>
    public ChatPage(ChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        Loaded += PageLoaded;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _statusTimer ??= CreateStatusTimer();
        _statusTimer.Start();
        RefreshStatus();
    }

    protected override void OnDisappearing()
    {
        _statusTimer?.Stop();
        ((ChatViewModel)BindingContext).Readiness.CancelPendingOperation();
        base.OnDisappearing();
    }

    private IDispatcherTimer CreateStatusTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(5);
        timer.Tick += async (_, _) =>
        {
            var readiness = ((ChatViewModel)BindingContext).Readiness;
            if (readiness.ShouldPoll)
                await readiness.RefreshCommand.ExecuteAsync(null);
        };
        return timer;
    }

    private void RefreshStatus()
    {
        var command = ((ChatViewModel)BindingContext).Readiness.RefreshCommand;
        if (command.CanExecute(null))
            command.Execute(null);
    }

    private async void PageLoaded(object? sender, EventArgs e)
    {
        if (_restored)
            return;

        _restored = true;
        await ((ChatViewModel)BindingContext).RestoreCachedChatAsync();
    }
}
