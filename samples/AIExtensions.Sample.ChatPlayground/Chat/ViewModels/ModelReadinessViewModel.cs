using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Essentials.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Checks model readiness and tracks preparation for the selected client.</summary>
public sealed class ModelReadinessViewModel : ObservableObject
{
    private ChatClientDescriptor? _descriptor;
    private string? _modelId;
    private CancellationTokenSource? _operation;
    private long _generation;
    private bool _isInteractionBusy;
    private readonly ILogger<ModelReadinessViewModel> _logger;

    public ModelReadinessViewModel(ILogger<ModelReadinessViewModel> logger)
    {
        _logger = logger;
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(prepare: false),
            () => HasStatus && !IsBusy && !IsInteractionBusy, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        PrepareCommand = new AsyncRelayCommand(() => RunAsync(prepare: true),
            () => CanPrepare, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        CancelCommand = new RelayCommand(CancelPreparation, () => IsPreparing);
    }

    public ChatModelStatus? Status { get; private set; }
    public bool IsChecking { get; private set; }
    public bool IsPreparing { get; private set; }
    public string? ErrorMessage { get; private set; }
    public ModelPreparationProgress? Progress { get; private set; }
    public double? BytesPerSecond { get; private set; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand PrepareCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public bool IsInteractionBusy
    {
        get => _isInteractionBusy;
        set
        {
            if (SetProperty(ref _isInteractionBusy, value))
                NotifyStateChanged();
        }
    }

    private bool HasStatus => _descriptor?.GetModelStatusAsync is not null;
    public bool IsBusy => _operation is not null;
    public bool IsSendBlocked => HasStatus && (IsPreparing || Status?.Availability != ChatModelAvailability.Available);
    public bool IsVisible => HasStatus && (IsSendBlocked || ErrorMessage is not null);
    public bool IsSystemPreparing => Status?.Availability == ChatModelAvailability.Downloading;
    public bool ShowPreparationAction => !IsPreparing && _descriptor?.PrepareModelAsync is not null &&
        Status?.Availability == ChatModelAvailability.NotReady;
    public bool CanPrepare => ShowPreparationAction && !IsBusy && !IsInteractionBusy;
    public bool ShouldPoll => HasStatus && !IsBusy && !IsInteractionBusy && ErrorMessage is null &&
        (Status is null || Status.Availability is ChatModelAvailability.NotReady or ChatModelAvailability.Downloading);
    public bool HasDeterminateProgress => IsPreparing && Progress is { BytesDownloaded: >= 0, TotalBytes: > 0 };
    public bool IsIndeterminateProgress => ErrorMessage is null &&
        (IsPreparing || IsSystemPreparing) && !HasDeterminateProgress;
    public double DeterminateProgressOpacity => HasDeterminateProgress ? 1 : 0;
    public double IndeterminateProgressOpacity => IsIndeterminateProgress ? 1 : 0;
    public double ProgressFraction => Progress is { BytesDownloaded: >= 0 and var downloaded, TotalBytes: > 0 and var total }
        ? Math.Clamp((double)downloaded / total, 0, 1) : 0;
    public string PreparationActionLabel => _descriptor?.PreparationActionLabel ?? "Prepare";
    public string ActionLabel => IsPreparing ? "Cancel" : ShowPreparationAction ? PreparationActionLabel : "Check again";
    public IRelayCommand ActionCommand => IsPreparing ? CancelCommand : ShowPreparationAction ? PrepareCommand : RefreshCommand;

    public string Message
    {
        get
        {
            if (ErrorMessage is not null)
                return ErrorMessage;
            var name = _descriptor?.DisplayName ?? "Model";
            if (IsChecking)
                return $"Checking {name} availability...";
            if (IsPreparing)
                return $"Preparing {name}...";
            if (IsSystemPreparing)
                return $"{name} model preparation is in progress.";
            if (ShowPreparationAction)
                return $"{name} needs model preparation before chatting.";
            return Status?.Availability switch
            {
                ChatModelAvailability.Available => string.Empty,
                ChatModelAvailability.NotReady => $"{name} is not ready. The system manages model preparation.",
                ChatModelAvailability.Unavailable => $"{name} is unavailable on this device or its system configuration is not ready.",
                ChatModelAvailability.Disabled => $"{name} is disabled. Enable it in system settings.",
                ChatModelAvailability.NotSupported => $"{name} is not supported on this device.",
                ChatModelAvailability.RequirementsNotMet => $"{name} needs additional system setup or an OS update.",
                _ => $"{name} availability is unknown.",
            };
        }
    }

    public string ProgressText
    {
        get
        {
            if (!IsPreparing || ErrorMessage is not null || Progress?.BytesDownloaded is not >= 0)
                return string.Empty;
            var counts = Progress.TotalBytes is > 0 and var total
                ? $"{Progress.BytesDownloaded / 1048576d:F1} / {total / 1048576d:F1} MiB"
                : $"{Progress.BytesDownloaded / 1048576d:F1} MiB downloaded";
            return BytesPerSecond is { } rate ? $"{counts} ({rate / 1048576d:F1} MiB/s)" : counts;
        }
    }

    public string ProgressLine => ProgressText is { Length: > 0 } text ? $"{Environment.NewLine}{text}" : string.Empty;

    public void SetClient(ChatClientDescriptor? descriptor, string? modelId = null)
    {
        CancelPendingOperation();
        _descriptor = descriptor;
        _modelId = modelId;
        Status = null;
        ErrorMessage = null;
        Progress = null;
        BytesPerSecond = null;
        NotifyStateChanged();
        if (RefreshCommand.CanExecute(null))
            RefreshCommand.Execute(null);
    }

    public void CancelPendingOperation()
    {
        _generation++;
        _operation?.Cancel();
        _operation = null;
        if (IsPreparing)
            ErrorMessage = "Preparation cancelled. A shared system download may continue.";
        IsChecking = false;
        IsPreparing = false;
        Progress = null;
        BytesPerSecond = null;
        NotifyStateChanged();
    }

    private void CancelPreparation()
    {
        ErrorMessage = "Preparation cancelled. A shared system download may continue.";
        _operation?.Cancel();
        NotifyStateChanged();
    }

    private async Task RunAsync(bool prepare)
    {
        if (IsBusy || IsInteractionBusy || _descriptor?.GetModelStatusAsync is not { } getStatus)
            return;

        var descriptor = _descriptor;
        var modelId = _modelId;
        var generation = ++_generation;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        IsChecking = !prepare && Status is null;
        IsPreparing = prepare;
        ErrorMessage = null;
        Progress = null;
        BytesPerSecond = null;
        NotifyStateChanged();
        bool IsCurrent() => generation == _generation && !operation.IsCancellationRequested;

        try
        {
            var status = await getStatus(modelId, operation.Token);
            if (!IsCurrent())
                return;
            UpdateStatus(status);
            if (prepare && status.Availability == ChatModelAvailability.NotReady &&
                descriptor.PrepareModelAsync is { } prepareModel)
            {
                IsChecking = false;
                IsPreparing = true;
                NotifyStateChanged();
                _logger.LogInformation("Preparing {Model} ({ModelId}).", descriptor.DisplayName, modelId);
                long? firstBytes = null;
                var transfer = new Stopwatch();
                var progress = new Progress<ModelPreparationProgress>(update =>
                {
                    if (IsCurrent() && IsPreparing)
                    {
                        if (update.BytesDownloaded is >= 0 and var downloaded)
                        {
                            if (firstBytes is null || downloaded < firstBytes.Value ||
                                Progress?.BytesDownloaded is { } previous && downloaded < previous)
                            {
                                firstBytes = downloaded;
                                transfer.Restart();
                                BytesPerSecond = null;
                            }
                            else
                            {
                                var seconds = transfer.Elapsed.TotalSeconds;
                                BytesPerSecond = seconds > 0 ? (downloaded - firstBytes.Value) / seconds : null;
                            }
                        }
                        else
                        {
                            BytesPerSecond = null;
                        }
                        Progress = update;
                        _logger.LogDebug("{Model} preparation progress: {DownloadedBytes} / {TotalBytes} bytes.",
                            descriptor.DisplayName, update.BytesDownloaded, update.TotalBytes);
                        NotifyStateChanged();
                    }
                });
                await prepareModel(modelId, progress, operation.Token);
                if (!IsCurrent())
                    return;
                _logger.LogInformation("{Model} preparation call completed.", descriptor.DisplayName);
                status = await getStatus(modelId, operation.Token);
                if (!IsCurrent())
                    return;
                UpdateStatus(status);
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            if (generation == _generation)
                ErrorMessage = prepare
                    ? "Preparation cancelled. A shared system download may continue."
                    : "Status check cancelled.";
        }
        catch (Exception exception)
        {
            if (IsCurrent())
            {
                ErrorMessage = $"{(prepare ? "Model preparation" : "Status check")} failed: {exception.Message}";
                _logger.LogWarning(exception, "{Operation} failed for {Model}.",
                    prepare ? "Model preparation" : "Status check", descriptor.DisplayName);
            }
        }
        finally
        {
            if (generation == _generation)
            {
                _operation = null;
                IsChecking = false;
                IsPreparing = false;
                Progress = null;
                BytesPerSecond = null;
                NotifyStateChanged();
            }
        }
    }

    private void UpdateStatus(ChatModelStatus status)
    {
        var previous = Status;
        if (previous is null || previous.Availability != status.Availability)
        {
            _logger.LogDebug("{Model} ({ModelId}) model status: {Availability}",
                _descriptor?.DisplayName, _modelId, status.Availability);
        }
        Status = status;
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(string.Empty);
        RefreshCommand.NotifyCanExecuteChanged();
        PrepareCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
}
