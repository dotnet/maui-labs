namespace AIExtensions.Sample.ChatPlayground;

public partial class ChatLogsView : ContentView
{
    public static readonly BindableProperty AutomationPrefixProperty = BindableProperty.Create(
        nameof(AutomationPrefix), typeof(string), typeof(ChatLogsView), "Chat");

    private readonly IDispatcherTimer _timer;

    public string AutomationPrefix
    {
        get => (string)GetValue(AutomationPrefixProperty);
        set => SetValue(AutomationPrefixProperty, value);
    }

    public ChatLogsView()
    {
        InitializeComponent();
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        if (BindingContext is DiagnosticsViewModel viewModel)
            viewModel.Refresh();
    }
}
