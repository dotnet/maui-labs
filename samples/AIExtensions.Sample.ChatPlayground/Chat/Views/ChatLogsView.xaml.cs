namespace AIExtensions.Sample.ChatPlayground;

public partial class ChatLogsView : ContentView
{
    private readonly ChatDiagnostics _diagnostics;
    private readonly Action _close;
    private readonly IDispatcherTimer _timer;
    private long _version = -1;

    public ChatDiagnosticGroups Groups { get; } = [];

    public ChatLogsView(ChatDiagnostics diagnostics, Action close)
    {
        _diagnostics = diagnostics;
        _close = close;
        InitializeComponent();
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        var snapshot = _diagnostics.Snapshot();
        if (snapshot.Version == _version)
            return;
        _version = snapshot.Version;
        Groups.ApplySnapshot(snapshot.Entries);
    }

    private void ClearClicked(object? sender, EventArgs e)
    {
        _diagnostics.Clear();
        Refresh();
    }

    private void CloseClicked(object? sender, EventArgs e) => _close();
}
