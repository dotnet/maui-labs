#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Comet;
using Comet.Reactive;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;

namespace CometSamples.BaristaNotes;

/// <summary>
/// App-scoped <see cref="IBaristaPhotoWorkflowCallbacks"/> that manages busy state,
/// vision results, intent choice, and routing for the photo workflow.
/// Per-request TCS ensures each workflow gets its own completion source.
/// </summary>
public sealed class AppPhotoWorkflowCallbacks : IBaristaPhotoWorkflowCallbacks, IDisposable
{
    readonly BaristaNavigationCoordinator _navigation;
    readonly BaristaServices _services;
    readonly Signal<bool> _isBusy;
    readonly Signal<string?> _statusMessage;
    readonly Signal<string?> _errorMessage;

    TaskCompletionSource<PhotoIntentChoice>? _pendingIntentChoice;
    bool _disposed;

    /// <summary>True when <see cref="ChoosePhotoIntentAsync"/> is waiting for user input.</summary>
    public bool HasPendingIntentChoice => _pendingIntentChoice is { Task.IsCompleted: false };

    public AppPhotoWorkflowCallbacks(
        BaristaNavigationCoordinator navigation,
        BaristaServices services,
        Signal<bool> isBusy,
        Signal<string?> statusMessage,
        Signal<string?> errorMessage)
    {
        _navigation = navigation;
        _services = services;
        _isBusy = isBusy;
        _statusMessage = statusMessage;
        _errorMessage = errorMessage;
    }

    public void ResetPresentation()
    {
        RunOnMainThread(() =>
        {
            _statusMessage.Value = null;
            _errorMessage.Value = null;
        });
    }

    public void PublishAcquisitionResult(
        PhotoSource source,
        PhotoOperationResult result)
    {
        var feedback = PhotoOperationFeedback.FromResult(source, result);
        RunOnMainThread(() =>
        {
            _statusMessage.Value = feedback.IsError ? null : feedback.Message;
            _errorMessage.Value = feedback.IsError ? feedback.Message : null;
        });
    }

    public void SetPhotoWorkflowBusy(bool isBusy)
    {
        RunOnMainThread(() => _isBusy.Value = isBusy);
    }

    public Task VisionResultAsync(VisionCallback result, CancellationToken cancellationToken)
    {
        RunOnMainThread(() =>
        {
            if (result.Status == VisionRequestStatus.Success)
                _statusMessage.Value = $"Detected: {result.Intent}";
            else if (result.ErrorMessage is not null)
                _statusMessage.Value = result.ErrorMessage;
        });
        return Task.CompletedTask;
    }

    public Task<PhotoIntentChoice> ChoosePhotoIntentAsync(
        PhotoAsset photo,
        PhotoClassificationResult classification,
        CancellationToken cancellationToken)
    {
        // Per-request TCS — cancel any stale one
        _pendingIntentChoice?.TrySetCanceled();
        var tcs = new TaskCompletionSource<PhotoIntentChoice>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingIntentChoice = tcs;

        // Register cancellation to abort the wait
        var registration = cancellationToken.Register(() => tcs.TrySetCanceled());

        // Show the post-capture intent modal (Coffee/Profile/Room/Retake/Cancel)
        RunOnMainThread(() =>
            _navigation.ShowModal(BaristaModalKind.PhotoIntent));

        return tcs.Task.ContinueWith(t =>
        {
            registration.Dispose();
            return t.IsCanceled ? PhotoIntentChoice.Cancel : t.Result;
        }, TaskScheduler.Default);
    }

    /// <summary>Called from the modal UI when user selects an intent choice.</summary>
    public void ResolveIntentChoice(PhotoIntentChoice choice)
    {
        _pendingIntentChoice?.TrySetResult(choice);
        _pendingIntentChoice = null;
    }

    public async Task RouteCoffeeAsync(
        PhotoAsset photo,
        BeanLabelExtraction? coffeeDetails,
        CancellationToken cancellationToken)
    {
        // Gold: open a prefilled AddCoffee flow for user review.
        // Route into the active ShotLoggingPage's real AddCoffee picker form.
        var prefill = coffeeDetails?.Success == true ? coffeeDetails : new BeanLabelExtraction { Success = true };

        RunOnMainThread(() =>
        {
            _navigation.HideModal();
            _navigation.SwitchTo(BaristaSection.NewDrink);
            // Open the real AddCoffee form with prefilled data on the active page
            _navigation.ActiveShotEditor?.OpenAddCoffeeFromPhoto(prefill);
        });
    }

    public Task RouteProfileAsync(PhotoAsset photo, CancellationToken cancellationToken)
    {
        // Gold: navigate to a new ProfileForm with captured bytes staged as avatar.
        RunOnMainThread(() =>
        {
            _navigation.HideModal();
            _navigation.OpenProfileFromPhoto(photo.Bytes);
        });
        return Task.CompletedTask;
    }

    public Task RouteRoomAsync(
        PhotoAsset photo,
        VisionAnalysisResult analysis,
        CancellationToken cancellationToken)
    {
        // Gold: show an "Analysis Complete" result dialog with people/cups/beans.
        var message = analysis.Message
            ?? $"I see {analysis.PeopleCount} {(analysis.PeopleCount == 1 ? "person" : "people")}. " +
               $"You need {analysis.CupsNeeded} {(analysis.CupsNeeded == 1 ? "cup" : "cups")} of coffee, " +
               $"which requires about {analysis.BeansNeededGrams}g of beans.";

        RunOnMainThread(() =>
        {
            _navigation.HideModal();
            _navigation.ShowRoomAnalysisResult("Analysis Complete", message);
        });
        return Task.CompletedTask;
    }

    public Task PhotoWorkflowCancelledAsync(
        PhotoWorkflowCancellation reason,
        CancellationToken cancellationToken)
    {
        _pendingIntentChoice?.TrySetCanceled();
        _pendingIntentChoice = null;
        RunOnMainThread(() =>
        {
            _navigation.HideModal();
            var feedback = reason switch
            {
                PhotoWorkflowCancellation.CaptureCancelled =>
                    PhotoOperationFeedback.FromResult(
                        PhotoSource.Camera,
                        PhotoOperationResult.Cancelled()),
                PhotoWorkflowCancellation.GalleryCancelled =>
                    PhotoOperationFeedback.FromResult(
                        PhotoSource.Gallery,
                        PhotoOperationResult.Cancelled()),
                PhotoWorkflowCancellation.IntentCancelled => new PhotoOperationFeedback(
                    PhotoSource.Camera,
                    PhotoOperationStatus.Cancelled,
                    "intent_cancelled",
                    "Photo action cancelled."),
                _ => new PhotoOperationFeedback(
                    PhotoSource.Camera,
                    PhotoOperationStatus.Cancelled,
                    "request_cancelled",
                    "Photo request cancelled."),
            };
            _statusMessage.Value = feedback.Message;
            _errorMessage.Value = null;
        });
        return Task.CompletedTask;
    }

    public Task PhotoWorkflowFailedAsync(
        PhotoWorkflowError error,
        CancellationToken cancellationToken)
    {
        _pendingIntentChoice?.TrySetCanceled();
        _pendingIntentChoice = null;
        RunOnMainThread(() =>
        {
            _navigation.HideModal();
            var source = error.Source ?? PhotoSource.Camera;
            var feedback = PhotoOperationFeedback.FromResult(
                source,
                new PhotoOperationResult(
                    error.Status,
                    null,
                    error.Code,
                    error.Message));
            _statusMessage.Value = null;
            _errorMessage.Value = feedback.Message;
        });
        return Task.CompletedTask;
    }

    void RunOnMainThread(Action action)
    {
        if (_disposed)
            return;
        ThreadHelper.RunOnMainThread(() =>
        {
            if (!_disposed)
                action();
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pendingIntentChoice?.TrySetCanceled();
        _pendingIntentChoice = null;
    }
}
