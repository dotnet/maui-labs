using System.ComponentModel;
using System.Diagnostics;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Essentials.AI;

namespace Microsoft.Maui.AI.Chat.Tests;

[Collection("Chat diagnostics")]
public sealed class ModelReadinessViewModelTests
{
    [Fact]
    public void NoStatusCallback_DoesNotPretendToCheckOrBlockTheClient()
    {
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);

        viewModel.SetClient(new("azure", "Azure", "Configured cloud client"));

        Assert.Null(viewModel.Status);
        Assert.False(viewModel.IsVisible);
        Assert.False(viewModel.IsSendBlocked);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.False(viewModel.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public async Task NativeStatus_LogsAvailabilityOnlyWhenChanged()
    {
        using var diagnostics = new ChatDiagnostics();
        using var factory = LoggerFactory.Create(builder => builder
            .AddProvider(diagnostics)
            .AddFilter<ChatDiagnostics>(ChatDiagnostics.SourceName, LogLevel.Debug));
        var viewModel = new ModelReadinessViewModel(factory.CreateLogger<ModelReadinessViewModel>());
        var status = new ChatModelStatus(ChatModelAvailability.Downloading, null);
        await SelectAsync(viewModel, new("android", "Gemini Nano", "Native",
            GetModelStatusAsync: (_, _) => Task.FromResult(status)));

        Assert.Contains("in progress", viewModel.Message);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        var entry = Assert.Single(diagnostics.Snapshot().Entries, entry => entry.Heading.Contains("ModelReadiness"));
        Assert.Contains("Downloading", entry.Message);

        status = Available;
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, diagnostics.Snapshot().Entries.Count(entry => entry.Heading.Contains("ModelReadiness")));
    }

    [Fact]
    public async Task SingleAction_SwitchesCommandsWithoutReplacingTheButton()
    {
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, Descriptor(NotReady,
            (_, token) => Task.Delay(Timeout.Infinite, token)));

        Assert.Equal("Download", viewModel.ActionLabel);
        Assert.Same(viewModel.PrepareCommand, viewModel.ActionCommand);
        Assert.Equal(0, viewModel.DeterminateProgressOpacity);
        Assert.Equal(0, viewModel.IndeterminateProgressOpacity);

        var preparation = viewModel.PrepareCommand.ExecuteAsync(null);

        Assert.Equal("Cancel", viewModel.ActionLabel);
        Assert.Same(viewModel.CancelCommand, viewModel.ActionCommand);
        Assert.Equal(0, viewModel.DeterminateProgressOpacity);
        Assert.Equal(1, viewModel.IndeterminateProgressOpacity);
        viewModel.ActionCommand.Execute(null);
        await preparation;

        await SelectAsync(viewModel, Descriptor(NotReady));

        Assert.Equal("Check again", viewModel.ActionLabel);
        Assert.Same(viewModel.RefreshCommand, viewModel.ActionCommand);
        Assert.Equal(0, viewModel.DeterminateProgressOpacity);
        Assert.Equal(0, viewModel.IndeterminateProgressOpacity);
    }

    [Fact]
    public async Task Available_HidesBannerAndAllowsSendingWithoutPreparing()
    {
        var preparations = 0;
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, Descriptor(Available,
            (_, _) => { preparations++; return Task.CompletedTask; }));

        Assert.False(viewModel.IsVisible);
        Assert.False(viewModel.IsSendBlocked);
        Assert.False(viewModel.CanPrepare);
        Assert.False(viewModel.ShouldPoll);
        Assert.Equal(0, preparations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NotReady_OffersPreparationOnlyWhenTheDescriptorProvidesIt(bool canPrepare)
    {
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, Descriptor(NotReady, canPrepare ? (_, _) => Task.CompletedTask : null));

        Assert.True(viewModel.IsVisible);
        Assert.True(viewModel.IsSendBlocked);
        Assert.Equal(canPrepare, viewModel.CanPrepare);
        Assert.False(viewModel.IsIndeterminateProgress);
        Assert.Equal("Download", viewModel.PreparationActionLabel);
        Assert.True(viewModel.ShouldPoll);
    }

    [Theory]
    [InlineData(ChatModelAvailability.NotSupported)]
    [InlineData(ChatModelAvailability.Disabled)]
    [InlineData(ChatModelAvailability.RequirementsNotMet)]
    [InlineData(ChatModelAvailability.Unknown)]
    [InlineData(ChatModelAvailability.Unavailable)]
    public async Task Unavailable_ExplainsAvailabilityWithoutOfferingDownload(ChatModelAvailability availability)
    {
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, Descriptor(new(availability, null),
            (_, _) => throw new InvalidOperationException("Must not prepare")));

        Assert.True(viewModel.IsVisible);
        Assert.True(viewModel.IsSendBlocked);
        Assert.False(viewModel.CanPrepare);
        Assert.False(viewModel.ShouldPoll);
        Assert.Equal(availability, viewModel.Status?.Availability);
        Assert.NotEmpty(viewModel.Message);
    }

    [Fact]
    public async Task AppleNotReady_OffersCheckOnlyAndRefreshesWhenSystemBecomesReady()
    {
        var status = NotReady;
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, new("apple", "Apple Intelligence", "System managed",
            GetModelStatusAsync: (_, _) => Task.FromResult(status)));

        Assert.False(viewModel.CanPrepare);
        Assert.Contains("system manages", viewModel.Message);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
        status = Available;

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsVisible);
        Assert.False(viewModel.IsSendBlocked);
    }

    [Fact]
    public async Task SelectedModel_IsUsedForChecksPreparationAndCompletion()
    {
        const string modelId = "gemini-nano-preview-full";
        var checks = new List<string?>();
        var preparations = new List<string?>();
        var prepared = false;
        var descriptor = new ChatClientDescriptor("android", "Gemini Nano", "Native",
            GetModelStatusAsync: (id, _) =>
            {
                checks.Add(id);
                return Task.FromResult(id == modelId && !prepared ? NotReady : Available);
            },
            PrepareModelAsync: (id, _, _) =>
            {
                preparations.Add(id);
                prepared = true;
                return Task.CompletedTask;
            });
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);

        viewModel.SetClient(descriptor, modelId);
        await viewModel.RefreshCommand.ExecutionTask!;
        await viewModel.PrepareCommand.ExecuteAsync(null);

        Assert.Equal(new[] { modelId, modelId, modelId }, checks);
        Assert.Equal(modelId, Assert.Single(preparations));
        Assert.False(viewModel.IsSendBlocked);

        viewModel.SetClient(descriptor, "gemini-nano-preview-fast");
        await viewModel.RefreshCommand.ExecutionTask!;

        Assert.Equal("gemini-nano-preview-fast", checks[^1]);
        Assert.False(viewModel.IsVisible);
    }

    [Fact]
    public async Task ModelSwitchWithinSameClient_IgnoresPreviousModelsLateStatus()
    {
        var oldStatus = new TaskCompletionSource<ChatModelStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var descriptor = new ChatClientDescriptor("android", "Gemini Nano", "Native",
            GetModelStatusAsync: (id, _) => id == "gemini-nano-preview-full"
                ? oldStatus.Task : Task.FromResult(Available));
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);

        viewModel.SetClient(descriptor, "gemini-nano-preview-full");
        var oldCheck = viewModel.RefreshCommand.ExecutionTask!;
        viewModel.SetClient(descriptor, "gemini-nano-preview-fast");
        await viewModel.RefreshCommand.ExecutionTask!;
        oldStatus.SetResult(NotReady);
        await oldCheck;

        Assert.Same(Available, viewModel.Status);
        Assert.False(viewModel.IsVisible);
        Assert.False(viewModel.IsSendBlocked);
    }

    [Fact]
    public async Task Prepare_OnlyRunsExplicitlyAndRechecksBeforeAndAfterNativeWork()
    {
        var status = NotReady;
        var checks = 0;
        var preparations = 0;
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, new("android", "Gemini Nano", "Native",
            GetModelStatusAsync: (_, _) => { checks++; return Task.FromResult(status); },
            PrepareModelAsync: (_, _, _) =>
            {
                preparations++;
                status = Available;
                return Task.CompletedTask;
            }));

        Assert.Equal(0, preparations);
        await viewModel.PrepareCommand.ExecuteAsync(null);

        Assert.Equal(1, preparations);
        Assert.Equal(3, checks);
        Assert.False(viewModel.IsSendBlocked);
        Assert.False(viewModel.IsVisible);
        Assert.False(viewModel.IsPreparing);
    }

    [Theory]
    [InlineData(ChatModelAvailability.Available)]
    [InlineData(ChatModelAvailability.Disabled)]
    [InlineData(ChatModelAvailability.NotSupported)]
    [InlineData(ChatModelAvailability.RequirementsNotMet)]
    [InlineData(ChatModelAvailability.Unknown)]
    [InlineData(ChatModelAvailability.Unavailable)]
    [InlineData(ChatModelAvailability.Downloading)]
    public async Task Prepare_RechecksAndDoesNotStartDownloadIfNativeStateChanged(ChatModelAvailability updated)
    {
        var status = NotReady;
        var preparations = 0;
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, new("android", "Gemini Nano", "Native",
            GetModelStatusAsync: (_, _) => Task.FromResult(status),
            PrepareModelAsync: (_, _, _) => { preparations++; return Task.CompletedTask; }));
        status = new(updated, null);

        await viewModel.PrepareCommand.ExecuteAsync(null);

        Assert.Equal(0, preparations);
        Assert.Same(status, viewModel.Status);
    }

    [Fact]
    public async Task PreparationCompletion_DoesNotClaimReadyUnlessStatusAgrees()
    {
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, Descriptor(NotReady,
            (_, _) => Task.CompletedTask));

        await viewModel.PrepareCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsVisible);
        Assert.True(viewModel.IsSendBlocked);
        Assert.True(viewModel.CanPrepare);
    }

    [Theory]
    [InlineData(ChatModelAvailability.NotReady, false)]
    [InlineData(ChatModelAvailability.Downloading, true)]
    public async Task StatusChecks_DoNotStartOrReattachPreparation(ChatModelAvailability availability, bool downloading)
    {
        var subscriptions = 0;
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, new("android", "Gemini Nano", "Native",
            GetModelStatusAsync: (_, _) => Task.FromResult(new ChatModelStatus(availability, null)),
            PrepareModelAsync: (_, _, _) => { subscriptions++; return Task.CompletedTask; }));

        Assert.Equal(0, subscriptions);
        Assert.True(viewModel.IsSendBlocked);
        Assert.Equal(downloading, viewModel.IsIndeterminateProgress);
        Assert.Equal(downloading, viewModel.IsSystemPreparing);
        Assert.Equal(!downloading, viewModel.CanPrepare);
        Assert.Null(viewModel.Progress);
        Assert.True(viewModel.ShouldPoll);
        var message = viewModel.Message;

        await viewModel.RefreshCommand.ExecuteAsync(null);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(0, subscriptions);
        Assert.Equal(message, viewModel.Message);
        Assert.Equal(downloading ? "Check again" : "Prepare", viewModel.ActionLabel);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(0, subscriptions);
        Assert.True(viewModel.IsSendBlocked);
    }

    [Fact]
    public async Task ExistingPreparation_PollsUntilAvailable_WithoutOfferingAnotherDownload()
    {
        var status = new ChatModelStatus(ChatModelAvailability.Downloading, null);
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, new("android", "Gemini Nano", "Native",
            GetModelStatusAsync: (_, _) => Task.FromResult(status),
            PrepareModelAsync: (_, _, _) => throw new InvalidOperationException("Must not start preparation")));

        Assert.Contains("in progress", viewModel.Message);
        Assert.Equal("Check again", viewModel.ActionLabel);
        Assert.Same(viewModel.RefreshCommand, viewModel.ActionCommand);
        Assert.True(viewModel.ShouldPoll);
        Assert.False(viewModel.PrepareCommand.CanExecute(null));
        Assert.True(viewModel.IsIndeterminateProgress);
        Assert.Empty(viewModel.ProgressText);

        status = Available;
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsSystemPreparing);
        Assert.False(viewModel.IsVisible);
        Assert.False(viewModel.IsSendBlocked);
    }

    [Fact]
    public async Task Preparation_ReportsByteProgressAndSupportsCancellation()
    {
        var progressSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        viewModel.PropertyChanged += (_, _) =>
        {
            if (viewModel.HasDeterminateProgress)
                progressSeen.TrySetResult();
        };
        await SelectAsync(viewModel, Descriptor(NotReady,
            async (progress, token) =>
            {
                progress.Report(new(1048576, 2097152));
                await Task.Delay(Timeout.Infinite, token);
            }));

        var preparation = viewModel.PrepareCommand.ExecuteAsync(null);
        await progressSeen.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(viewModel.IsPreparing);
        Assert.True(viewModel.IsSendBlocked);
        Assert.Equal(0.5, viewModel.ProgressFraction);
        Assert.Equal("Preparing Gemini Nano...", viewModel.Message);
        Assert.Equal($"{1d:F1} / {2d:F1} MiB", viewModel.ProgressText);
        Assert.False(viewModel.IsIndeterminateProgress);
        Assert.Equal(1, viewModel.DeterminateProgressOpacity);
        Assert.Equal(0, viewModel.IndeterminateProgressOpacity);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.False(viewModel.PrepareCommand.CanExecute(null));
        Assert.True(viewModel.CancelCommand.CanExecute(null));
        viewModel.CancelCommand.Execute(null);
        await preparation;

        Assert.False(viewModel.IsPreparing);
        Assert.Null(viewModel.Progress);
        Assert.Null(viewModel.BytesPerSecond);
        Assert.Empty(viewModel.ProgressText);
        Assert.Contains("cancelled", viewModel.Message);
        Assert.True(viewModel.IsSendBlocked);
        Assert.True(viewModel.CanPrepare);
    }

    [Fact]
    public async Task Preparation_ShowsAverageRateAfterTwoSamplesAndResetsIfCounterRestarts()
    {
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        IProgress<ModelPreparationProgress>? reporter = null;
        await SelectAsync(viewModel, Descriptor(NotReady, (progress, token) =>
        {
            reporter = progress;
            return Task.Delay(Timeout.Infinite, token);
        }));
        var operation = viewModel.PrepareCommand.ExecuteAsync(null);

        async Task ReportAsync(ModelPreparationProgress update)
        {
            var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            PropertyChangedEventHandler handler = (_, _) =>
            {
                if (ReferenceEquals(viewModel.Progress, update))
                    handled.TrySetResult();
            };
            viewModel.PropertyChanged += handler;
            try
            {
                reporter!.Report(update);
                await handled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                viewModel.PropertyChanged -= handler;
            }
        }

        try
        {
            var firstBefore = Stopwatch.GetTimestamp();
            await ReportAsync(new(1048576, 4194304));
            var firstAfter = Stopwatch.GetTimestamp();
            Assert.Null(viewModel.BytesPerSecond);
            Assert.Equal($"{1d:F1} / {4d:F1} MiB", viewModel.ProgressText);
            Assert.Equal($"{Environment.NewLine}{viewModel.ProgressText}", viewModel.ProgressLine);

            var secondBefore = Stopwatch.GetTimestamp();
            await ReportAsync(new(3145728, 4194304));
            var secondAfter = Stopwatch.GetTimestamp();
            var rate = Assert.IsType<double>(viewModel.BytesPerSecond);
            var longestElapsed = Stopwatch.GetElapsedTime(firstBefore, secondAfter).TotalSeconds;
            var shortestElapsed = Stopwatch.GetElapsedTime(firstAfter, secondBefore).TotalSeconds;
            Assert.True(double.IsFinite(rate));
            Assert.InRange(rate, 2097152d / longestElapsed,
                shortestElapsed > 0 ? 2097152d / shortestElapsed : double.PositiveInfinity);
            Assert.Equal($"{3d:F1} / {4d:F1} MiB ({rate / 1048576d:F1} MiB/s)", viewModel.ProgressText);

            await ReportAsync(new(4194304, 4194304));
            Assert.True(viewModel.BytesPerSecond > 0);

            await ReportAsync(new(2097152, 4194304));
            Assert.Null(viewModel.BytesPerSecond);

            await ReportAsync(new(3145728));
            rate = Assert.IsType<double>(viewModel.BytesPerSecond);
            Assert.True(rate > 0 && double.IsFinite(rate));
            Assert.Equal($"{3d:F1} MiB downloaded ({rate / 1048576d:F1} MiB/s)", viewModel.ProgressText);
        }
        finally
        {
            viewModel.CancelCommand.Execute(null);
            await operation;
        }

        Assert.Null(viewModel.BytesPerSecond);
        Assert.Empty(viewModel.ProgressText);
        Assert.Empty(viewModel.ProgressLine);
    }

    [Fact]
    public async Task PreparationWithoutNativeByteCounts_UsesIndeterminateProgress()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, Descriptor(NotReady,
            async (_, token) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            }));

        var operation = viewModel.PrepareCommand.ExecuteAsync(null);
        await started.Task;

        Assert.True(viewModel.IsIndeterminateProgress);
        Assert.False(viewModel.HasDeterminateProgress);
        Assert.Empty(viewModel.ProgressText);
        Assert.Null(viewModel.BytesPerSecond);
        viewModel.CancelCommand.Execute(null);
        await operation;
    }

    [Fact]
    public async Task Cancellation_IgnoresSuccessFromProviderThatDoesNotObserveCancellation()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, Descriptor(NotReady,
            (_, _) => completed.Task));
        var operation = viewModel.PrepareCommand.ExecuteAsync(null);

        viewModel.CancelCommand.Execute(null);
        completed.SetResult();
        await operation;

        Assert.Contains("cancelled", viewModel.Message);
        Assert.False(viewModel.IsPreparing);
        Assert.True(viewModel.IsSendBlocked);
        Assert.True(viewModel.CanPrepare);
    }

    [Fact]
    public async Task FailedCheck_IsVisibleAndRetryableRatherThanAvailable()
    {
        var fail = true;
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, new("android", "Gemini Nano", "Native",
            GetModelStatusAsync: (_, _) => fail
                ? Task.FromException<ChatModelStatus>(new InvalidOperationException("AICore unavailable"))
                : Task.FromResult(Available)));

        Assert.Contains("AICore unavailable", viewModel.Message);
        Assert.True(viewModel.IsVisible);
        Assert.True(viewModel.IsSendBlocked);
        Assert.False(viewModel.ShouldPoll);
        fail = false;
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsVisible);
    }

    [Fact]
    public async Task FailedPreparation_ShowsErrorAndAllowsExplicitRetry()
    {
        using var diagnostics = new ChatDiagnostics();
        using var factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Warning)
            .AddProvider(diagnostics));
        var viewModel = new ModelReadinessViewModel(factory.CreateLogger<ModelReadinessViewModel>());
        await SelectAsync(viewModel, Descriptor(NotReady,
            (_, _) => Task.FromException(new IOException("Download failed"))));

        await viewModel.PrepareCommand.ExecuteAsync(null);

        Assert.Contains("Download failed", viewModel.Message);
        Assert.True(viewModel.CanPrepare);
        Assert.True(viewModel.IsSendBlocked);
        Assert.False(viewModel.ShouldPoll);
        var entry = Assert.Single(diagnostics.Snapshot().Entries);
        Assert.Contains("| Warning |", entry.Heading);
        Assert.Contains("Download failed", entry.Details);
    }

    [Fact]
    public async Task ProviderSwitch_CancelsAndIgnoresLateStatusResults()
    {
        var response = new TaskCompletionSource<ChatModelStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken previousToken = default;
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        viewModel.SetClient(new("old", "Old", "Native", GetModelStatusAsync: (_, token) =>
        {
            previousToken = token;
            return response.Task;
        }));
        var oldCheck = viewModel.RefreshCommand.ExecutionTask!;
        Assert.True(viewModel.IsChecking);
        await SelectAsync(viewModel, Descriptor(Available));

        response.SetResult(NotReady);
        await oldCheck;

        Assert.True(previousToken.IsCancellationRequested);
        Assert.Same(Available, viewModel.Status);
        Assert.False(viewModel.IsVisible);
    }

    [Fact]
    public async Task ProviderSwitch_IgnoresLateDownloadCompletionAndProgress()
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IProgress<ModelPreparationProgress>? oldProgress = null;
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, Descriptor(NotReady,
            (progress, _) => { oldProgress = progress; return finished.Task; }));
        var operation = viewModel.PrepareCommand.ExecuteAsync(null);
        await SelectAsync(viewModel, Descriptor(Available));

        oldProgress!.Report(new(100, 100));
        finished.SetResult();
        await operation;

        Assert.Same(Available, viewModel.Status);
        Assert.Null(viewModel.Progress);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsPreparing);
    }

    [Fact]
    public async Task LeavingPage_CancelsOperationsAndAllowsAStatusRefreshOnReturn()
    {
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, Descriptor(NotReady,
            (_, token) => Task.Delay(Timeout.Infinite, token)));
        var preparation = viewModel.PrepareCommand.ExecuteAsync(null);

        viewModel.CancelPendingOperation();
        await preparation;

        Assert.False(viewModel.IsPreparing);
        Assert.Contains("cancelled", viewModel.Message);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ChatOperation_DisablesReadinessActionsUntilItFinishes()
    {
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, Descriptor(NotReady,
            (_, _) => Task.CompletedTask));

        viewModel.IsInteractionBusy = true;

        Assert.False(viewModel.CanPrepare);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.False(viewModel.ShouldPoll);
        viewModel.IsInteractionBusy = false;
        Assert.True(viewModel.CanPrepare);
    }

    [Fact]
    public async Task InitialCheck_DeferredByBusyInteraction_IsPickedUpByPolling()
    {
        var checks = 0;
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance)
        {
            IsInteractionBusy = true,
        };
        viewModel.SetClient(new("android", "Gemini Nano", "Native", GetModelStatusAsync: (_, _) =>
        {
            checks++;
            return Task.FromResult(Available);
        }));

        Assert.Equal(0, checks);
        Assert.True(viewModel.IsSendBlocked);
        Assert.False(viewModel.ShouldPoll);

        viewModel.IsInteractionBusy = false;

        Assert.True(viewModel.ShouldPoll);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(1, checks);
        Assert.False(viewModel.IsSendBlocked);
        Assert.False(viewModel.IsVisible);
    }

    [Fact]
    public async Task RepeatedStatusCheck_KeepsBannerPresentationStable()
    {
        var response = new TaskCompletionSource<ChatModelStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var status = NotReady;
        var checks = 0;
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, new("android", "Gemini Nano", "Native",
            GetModelStatusAsync: (_, _) => ++checks == 1 ? Task.FromResult(status) : response.Task,
            PrepareModelAsync: (_, _, _) => Task.CompletedTask));
        var message = viewModel.Message;

        var refresh = viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.IsChecking);
        Assert.Equal(message, viewModel.Message);
        Assert.Same(status, viewModel.Status);
        Assert.True(viewModel.ShowPreparationAction);
        response.SetResult(status);
        await refresh;
    }

    [Fact]
    public async Task RefreshingPreviouslyAvailableModel_DoesNotFlashBannerOrBlockSend()
    {
        var response = new TaskCompletionSource<ChatModelStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var checks = 0;
        var viewModel = new ModelReadinessViewModel(NullLogger<ModelReadinessViewModel>.Instance);
        await SelectAsync(viewModel, new("android", "Gemini Nano", "Native",
            GetModelStatusAsync: (_, _) => ++checks == 1 ? Task.FromResult(Available) : response.Task));

        var refresh = viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsChecking);
        Assert.False(viewModel.IsVisible);
        Assert.False(viewModel.IsSendBlocked);
        response.SetResult(Available);
        await refresh;
    }

    private static ChatModelStatus Available { get; } = new(ChatModelAvailability.Available, null);
    private static ChatModelStatus NotReady { get; } = new(ChatModelAvailability.NotReady, null);

    private static ChatClientDescriptor Descriptor(ChatModelStatus status,
        Func<IProgress<ModelPreparationProgress>, CancellationToken, Task>? prepare = null) =>
        new("android", "Gemini Nano", "Native",
            GetModelStatusAsync: (_, _) => Task.FromResult(status),
            PrepareModelAsync: prepare is null ? null : (_, progress, token) => prepare(progress, token),
            PreparationActionLabel: "Download");

    private static async Task SelectAsync(ModelReadinessViewModel viewModel, ChatClientDescriptor descriptor)
    {
        viewModel.SetClient(descriptor);
        if (viewModel.RefreshCommand.ExecutionTask is { } check)
            await check;
    }

}
