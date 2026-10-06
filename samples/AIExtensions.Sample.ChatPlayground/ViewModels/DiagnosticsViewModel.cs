using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Presents the shared diagnostic buffer with independent sidebar state for each tab.</summary>
public sealed partial class DiagnosticsViewModel(ChatDiagnostics diagnostics) : ObservableObject
{
    private long _version = -1;

    public ChatDiagnosticGroups Groups { get; } = [];

    [ObservableProperty]
    private bool isOpen = true;

    /// <summary>Refreshes UI-bound groups on the calling UI thread.</summary>
    public void Refresh()
    {
        var snapshot = diagnostics.Snapshot();
        if (snapshot.Version == _version)
            return;

        _version = snapshot.Version;
        Groups.ApplySnapshot(snapshot.Entries);
    }

    [RelayCommand]
    private void Clear()
    {
        diagnostics.Clear();
        Refresh();
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void Close() => IsOpen = false;
}
