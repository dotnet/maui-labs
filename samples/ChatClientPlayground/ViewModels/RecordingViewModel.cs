using ChatClientPlayground.Features.Recording;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChatClientPlayground.ViewModels;

/// <summary>Exposes the sample-contained recording tape controls in the settings pane.</summary>
public sealed partial class RecordingViewModel : ObservableObject
{
    private readonly ChatRecordingFeature _feature;

    public RecordingViewModel(ChatRecordingFeature feature)
    {
        _feature = feature;
        _feature.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(Refresh);
        Refresh();
    }

    [ObservableProperty] private RecordingMode mode;
    [ObservableProperty] private string status = "Live requests are not recorded.";
    [ObservableProperty] private int interactionCount;
    [ObservableProperty] private int replayPosition;
    [ObservableProperty] private string storagePath = string.Empty;

    public bool IsLive { get => Mode == RecordingMode.Live; set { if (value) Mode = RecordingMode.Live; } }
    public bool IsRecord { get => Mode == RecordingMode.Record; set { if (value) Mode = RecordingMode.Record; } }
    public bool IsReplay { get => Mode == RecordingMode.Replay; set { if (value) Mode = RecordingMode.Replay; } }
    public bool CanRestartReplay => InteractionCount > 0;
    public string TapeSummary => $"{InteractionCount} interaction{(InteractionCount == 1 ? string.Empty : "s")} · replay {ReplayPosition}/{InteractionCount}";
    public string StorageInfo => $"Recordings are saved app-locally. Path: {StoragePath}";

    [RelayCommand]
    private void NewRecording()
    {
        _feature.NewRecording();
        Status = "New in-memory recording created.";
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            _feature.Save();
            Status = $"Saved {InteractionCount} interaction(s).";
        }
        catch (Exception exception)
        {
            Status = $"Save failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            _feature.Load();
            Status = $"Loaded {InteractionCount} interaction(s); replay is ready.";
        }
        catch (Exception exception)
        {
            Status = $"Load failed: {exception.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestartReplay))]
    private void RestartReplay()
    {
        _feature.RestartReplay();
        Status = "Replay restarted at interaction 1.";
    }

    partial void OnModeChanged(RecordingMode value)
    {
        _feature.SetMode(value);
        Status = value switch
        {
            RecordingMode.Live => "Live requests are not recorded.",
            RecordingMode.Record => "Requests will be recorded in memory.",
            _ => InteractionCount == 0
                ? "Replay needs a loaded or recorded interaction."
                : $"Replay is ready at interaction {ReplayPosition + 1} of {InteractionCount}.",
        };
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(IsRecord));
        OnPropertyChanged(nameof(IsReplay));
    }

    private void Refresh()
    {
        Mode = _feature.Mode;
        InteractionCount = _feature.InteractionCount;
        ReplayPosition = _feature.ReplayPosition;
        StoragePath = _feature.Path;
        OnPropertyChanged(nameof(TapeSummary));
        OnPropertyChanged(nameof(StorageInfo));
        RestartReplayCommand.NotifyCanExecuteChanged();
    }
}
