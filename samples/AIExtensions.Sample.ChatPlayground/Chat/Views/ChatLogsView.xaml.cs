using System.Collections.ObjectModel;

namespace AIExtensions.Sample.ChatPlayground;

public partial class ChatLogsView : ContentView
{
    private readonly ChatDiagnostics _diagnostics;
    private readonly Action _close;
    private readonly IDispatcherTimer _timer;
    private long _version = -1;

    public ObservableCollection<ChatDiagnosticEntry> Entries { get; } = [];

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
        if (Entries.Count > 0 && (snapshot.Entries.Length == 0 ||
            snapshot.Entries[0].Sequence > Entries[^1].Sequence))
            Entries.Clear();
        while (Entries.Count > 0 && Entries[0].Sequence < snapshot.Entries[0].Sequence)
            Entries.RemoveAt(0);
        var last = Entries.Count == 0 ? -1 : Entries[^1].Sequence;
        foreach (var entry in snapshot.Entries)
            if (entry.Sequence > last)
                Entries.Add(entry);
    }

    private void ClearClicked(object? sender, EventArgs e)
    {
        _diagnostics.Clear();
        Refresh();
    }

    private void CloseClicked(object? sender, EventArgs e) => _close();
}
