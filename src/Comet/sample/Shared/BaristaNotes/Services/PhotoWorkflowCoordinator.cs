#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public sealed class PhotoWorkflowCoordinator
{
    readonly IBaristaPhotoService _photos;
    readonly IBaristaVisionAnalyzer _vision;
    readonly IBaristaPhotoWorkflowCallbacks _callbacks;
    readonly SemaphoreSlim _workflowGate = new(1, 1);

    public PhotoWorkflowCoordinator(
        IBaristaPhotoService photos,
        IBaristaVisionAnalyzer vision,
        IBaristaPhotoWorkflowCallbacks callbacks)
    {
        _photos = photos ?? throw new ArgumentNullException(nameof(photos));
        _vision = vision ?? throw new ArgumentNullException(nameof(vision));
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public Task<PhotoWorkflowOutcome> RunCameraAsync(CancellationToken cancellationToken = default) =>
        RunAsync(PhotoSource.Camera, cancellationToken);

    public Task<PhotoWorkflowOutcome> RunGalleryAsync(CancellationToken cancellationToken = default) =>
        RunAsync(PhotoSource.Gallery, cancellationToken);

    async Task<PhotoWorkflowOutcome> RunAsync(
        PhotoSource initialSource,
        CancellationToken cancellationToken)
    {
        if (!await _workflowGate.WaitAsync(0, cancellationToken))
        {
            await _callbacks.PhotoWorkflowFailedAsync(
                new PhotoWorkflowError(
                    "workflow_busy",
                    "A photo workflow is already running.",
                    Source: initialSource),
                CancellationToken.None);
            return PhotoWorkflowOutcome.Error;
        }

        try
        {
            var source = initialSource;
            while (true)
            {
                var acquisition = source == PhotoSource.Camera
                    ? await _photos.CapturePhotoAsync(cancellationToken)
                    : await _photos.PickPhotoAsync(cancellationToken);

                if (acquisition.Status == PhotoOperationStatus.Cancelled)
                {
                    await NotifyCancelledAsync(
                        source == PhotoSource.Camera
                            ? PhotoWorkflowCancellation.CaptureCancelled
                            : PhotoWorkflowCancellation.GalleryCancelled);
                    return PhotoWorkflowOutcome.Cancelled;
                }

                if (acquisition.Status != PhotoOperationStatus.Success || acquisition.Photo is null)
                {
                    await NotifyErrorAsync(
                        source,
                        acquisition.Status == PhotoOperationStatus.Success
                            ? PhotoOperationResult.Error(
                                "photo_missing",
                                "The photo provider returned no image.")
                            : acquisition);
                    return PhotoWorkflowOutcome.Error;
                }

                var photo = acquisition.Photo;
                _callbacks.SetPhotoWorkflowBusy(true);
                var classification = await _vision.ClassifyPhotoAsync(photo, cancellationToken);
                await _callbacks.VisionResultAsync(
                    new VisionCallback(
                        VisionOperation.Classification,
                        classification.Status,
                        classification.Analysis.Intent,
                        classification.CoffeeDetails,
                        null,
                        null,
                        classification.ErrorMessage),
                    cancellationToken);

                if (classification.Status == VisionRequestStatus.Cancelled
                    || cancellationToken.IsCancellationRequested)
                {
                    await NotifyCancelledAsync(PhotoWorkflowCancellation.RequestCancelled);
                    return PhotoWorkflowOutcome.Cancelled;
                }

                PhotoIntentChoice choice;
                if (classification.Status == VisionRequestStatus.Success
                    && classification.Analysis.Success
                    && classification.Analysis.IsObvious
                    && classification.Analysis.Intent != PhotoWorkflowIntent.Unknown)
                {
                    choice = ToChoice(classification.Analysis.Intent);
                }
                else
                {
                    _callbacks.SetPhotoWorkflowBusy(false);
                    choice = await _callbacks.ChoosePhotoIntentAsync(
                        photo,
                        classification,
                        cancellationToken);
                }

                if (choice == PhotoIntentChoice.Cancel)
                {
                    await NotifyCancelledAsync(PhotoWorkflowCancellation.IntentCancelled);
                    return PhotoWorkflowOutcome.Cancelled;
                }

                if (choice == PhotoIntentChoice.Retake)
                {
                    _callbacks.SetPhotoWorkflowBusy(false);
                    source = PhotoSource.Camera;
                    continue;
                }

                return await RouteAsync(choice, photo, classification, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await NotifyCancelledAsync(PhotoWorkflowCancellation.RequestCancelled);
            return PhotoWorkflowOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            await _callbacks.PhotoWorkflowFailedAsync(
                new PhotoWorkflowError(
                    "photo_workflow_failed",
                    "The photo workflow failed.",
                    ex,
                    initialSource),
                CancellationToken.None);
            return PhotoWorkflowOutcome.Error;
        }
        finally
        {
            _workflowGate.Release();
            _callbacks.SetPhotoWorkflowBusy(false);
        }
    }

    async Task<PhotoWorkflowOutcome> RouteAsync(
        PhotoIntentChoice choice,
        PhotoAsset photo,
        PhotoClassificationResult classification,
        CancellationToken cancellationToken)
    {
        switch (choice)
        {
            case PhotoIntentChoice.Coffee:
                var details = HasCoffeeDetails(classification.CoffeeDetails)
                    ? classification.CoffeeDetails
                    : null;
                if (details is null)
                {
                    var extraction = await _vision.ExtractBeanLabelAsync(photo, cancellationToken);
                    details = extraction.Status == VisionRequestStatus.Success
                        ? extraction.Extraction
                        : null;
                    await _callbacks.VisionResultAsync(
                        new VisionCallback(
                            VisionOperation.BeanLabelExtraction,
                            extraction.Status,
                            PhotoWorkflowIntent.Coffee,
                            extraction.Extraction,
                            null,
                            null,
                            extraction.ErrorMessage),
                        cancellationToken);
                    if (extraction.Status == VisionRequestStatus.Cancelled
                        || cancellationToken.IsCancellationRequested)
                    {
                        await NotifyCancelledAsync(PhotoWorkflowCancellation.RequestCancelled);
                        return PhotoWorkflowOutcome.Cancelled;
                    }
                }
                _callbacks.SetPhotoWorkflowBusy(false);
                await _callbacks.RouteCoffeeAsync(photo, details, cancellationToken);
                return PhotoWorkflowOutcome.Coffee;

            case PhotoIntentChoice.Profile:
                _callbacks.SetPhotoWorkflowBusy(false);
                await _callbacks.RouteProfileAsync(photo, cancellationToken);
                return PhotoWorkflowOutcome.Profile;

            case PhotoIntentChoice.Room:
                var room = await _vision.AnalyzeRoomAsync(photo, cancellationToken);
                await _callbacks.VisionResultAsync(
                    new VisionCallback(
                        VisionOperation.RoomAnalysis,
                        room.Status,
                        PhotoWorkflowIntent.Room,
                        null,
                        room.Analysis,
                        null,
                        room.ErrorMessage),
                    cancellationToken);
                if (room.Status == VisionRequestStatus.Cancelled
                    || cancellationToken.IsCancellationRequested)
                {
                    await NotifyCancelledAsync(PhotoWorkflowCancellation.RequestCancelled);
                    return PhotoWorkflowOutcome.Cancelled;
                }
                _callbacks.SetPhotoWorkflowBusy(false);
                if (room.Status != VisionRequestStatus.Success || room.Analysis?.Success != true)
                {
                    await NotifyErrorAsync(
                        photo.Source,
                        room.Status == VisionRequestStatus.Unavailable
                            ? PhotoOperationResult.Unavailable(
                                "vision_unavailable",
                                room.ErrorMessage
                                    ?? room.Analysis?.ErrorMessage
                                    ?? "The room photo could not be analyzed.")
                            : PhotoOperationResult.Error(
                                "room_analysis_failed",
                                room.ErrorMessage
                                    ?? room.Analysis?.ErrorMessage
                                    ?? "The room photo could not be analyzed."));
                    return PhotoWorkflowOutcome.Error;
                }
                await _callbacks.RouteRoomAsync(photo, room.Analysis, cancellationToken);
                return PhotoWorkflowOutcome.Room;

            default:
                await NotifyCancelledAsync(PhotoWorkflowCancellation.IntentCancelled);
                return PhotoWorkflowOutcome.Cancelled;
        }
    }

    Task NotifyCancelledAsync(PhotoWorkflowCancellation reason) =>
        _callbacks.PhotoWorkflowCancelledAsync(reason, CancellationToken.None);

    Task NotifyErrorAsync(PhotoSource source, PhotoOperationResult result) =>
        _callbacks.PhotoWorkflowFailedAsync(
            new PhotoWorkflowError(
                result.ErrorCode ?? "photo_acquisition_failed",
                result.ErrorMessage ?? "The photo could not be acquired.",
                Source: source,
                Status: result.Status),
            CancellationToken.None);

    static PhotoIntentChoice ToChoice(PhotoWorkflowIntent intent) =>
        intent switch
        {
            PhotoWorkflowIntent.Coffee => PhotoIntentChoice.Coffee,
            PhotoWorkflowIntent.Profile => PhotoIntentChoice.Profile,
            PhotoWorkflowIntent.Room => PhotoIntentChoice.Room,
            _ => PhotoIntentChoice.Cancel,
        };

    static bool HasCoffeeDetails(BeanLabelExtraction? details) =>
        details is not null
        && (!string.IsNullOrWhiteSpace(details.Name)
            || !string.IsNullOrWhiteSpace(details.Roaster)
            || !string.IsNullOrWhiteSpace(details.Origin)
            || details.RoastDate.HasValue
            || !string.IsNullOrWhiteSpace(details.Notes));
}
