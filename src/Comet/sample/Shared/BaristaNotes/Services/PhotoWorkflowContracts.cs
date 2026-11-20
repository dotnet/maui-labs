#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public enum PhotoSource
{
    Camera,
    Gallery,
}

public enum PhotoOperationStatus
{
    Success,
    Cancelled,
    Unavailable,
    PermissionDenied,
    Error,
}

public sealed record PhotoAsset(
    byte[] Bytes,
    string ContentType,
    string? FileName,
    PhotoSource Source);

public sealed record PhotoOperationResult(
    PhotoOperationStatus Status,
    PhotoAsset? Photo,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static PhotoOperationResult Success(PhotoAsset photo) =>
        new(PhotoOperationStatus.Success, photo, null, null);

    public static PhotoOperationResult Cancelled() =>
        new(PhotoOperationStatus.Cancelled, null, "cancelled", null);

    public static PhotoOperationResult Unavailable(string code, string message) =>
        new(PhotoOperationStatus.Unavailable, null, code, message);

    public static PhotoOperationResult PermissionDenied(string message) =>
        new(PhotoOperationStatus.PermissionDenied, null, "permission_denied", message);

    public static PhotoOperationResult Error(string code, string message) =>
        new(PhotoOperationStatus.Error, null, code, message);
}

public sealed record PhotoOperationFeedback(
    PhotoSource Source,
    PhotoOperationStatus Status,
    string Code,
    string Message)
{
    public bool IsError =>
        Status is PhotoOperationStatus.Unavailable
            or PhotoOperationStatus.PermissionDenied
            or PhotoOperationStatus.Error;

    public static PhotoOperationFeedback FromResult(
        PhotoSource source,
        PhotoOperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var label = source == PhotoSource.Camera ? "Camera" : "Gallery";
        var detail = result.ErrorMessage?.Trim();

        return result.Status switch
        {
            PhotoOperationStatus.Success => new(
                source,
                result.Status,
                "success",
                "Photo ready."),
            PhotoOperationStatus.Cancelled => new(
                source,
                result.Status,
                result.ErrorCode ?? "cancelled",
                $"{label} cancelled."),
            PhotoOperationStatus.Unavailable => new(
                source,
                result.Status,
                result.ErrorCode ?? "unavailable",
                Format(label, "unavailable", detail)),
            PhotoOperationStatus.PermissionDenied => new(
                source,
                result.Status,
                result.ErrorCode ?? "permission_denied",
                Format(label, "permission denied", detail)),
            _ => new(
                source,
                PhotoOperationStatus.Error,
                result.ErrorCode ?? "photo_error",
                Format(label, "error", detail)),
        };
    }

    static string Format(string label, string status, string? detail) =>
        string.IsNullOrWhiteSpace(detail)
            ? $"{label} {status}."
            : $"{label} {status}: {detail}";
}

public interface IBaristaPhotoService
{
    bool IsCameraAvailable { get; }
    bool IsGalleryAvailable { get; }

    Task<PhotoOperationResult> CapturePhotoAsync(CancellationToken cancellationToken = default);
    Task<PhotoOperationResult> PickPhotoAsync(CancellationToken cancellationToken = default);
}

public enum VisionAvailabilityState
{
    Ready,
    NotConfigured,
}

public sealed record VisionAvailability(VisionAvailabilityState State, string? Reason)
{
    public bool IsAvailable => State == VisionAvailabilityState.Ready;
}

public enum VisionRequestStatus
{
    Success,
    Unavailable,
    Cancelled,
    Error,
}

public sealed record PhotoClassificationResult(
    VisionRequestStatus Status,
    PhotoWorkflowAnalysis Analysis,
    BeanLabelExtraction? CoffeeDetails,
    string? ErrorMessage);

public sealed record BeanLabelResult(
    VisionRequestStatus Status,
    BeanLabelExtraction? Extraction,
    string? ErrorMessage);

public sealed record RoomAnalysisResult(
    VisionRequestStatus Status,
    VisionAnalysisResult? Analysis,
    string? ErrorMessage);

public sealed record ProfileMatchResult(
    VisionRequestStatus Status,
    PersonIdentificationResult? Match,
    string? ErrorMessage);

public interface IBaristaVisionAnalyzer
{
    VisionAvailability Availability { get; }

    Task<PhotoClassificationResult> ClassifyPhotoAsync(
        PhotoAsset photo,
        CancellationToken cancellationToken = default);

    Task<BeanLabelResult> ExtractBeanLabelAsync(
        PhotoAsset photo,
        CancellationToken cancellationToken = default);

    Task<RoomAnalysisResult> AnalyzeRoomAsync(
        PhotoAsset photo,
        CancellationToken cancellationToken = default);

    Task<ProfileMatchResult> MatchProfileAsync(
        PhotoAsset photo,
        IReadOnlyList<PersonIdentificationCandidate> candidates,
        CancellationToken cancellationToken = default);
}

public enum PhotoIntentChoice
{
    Cancel,
    Coffee,
    Profile,
    Room,
    Retake,
}

public enum VisionOperation
{
    Classification,
    BeanLabelExtraction,
    RoomAnalysis,
    ProfileMatch,
}

public sealed record VisionCallback(
    VisionOperation Operation,
    VisionRequestStatus Status,
    PhotoWorkflowIntent Intent,
    BeanLabelExtraction? CoffeeDetails,
    VisionAnalysisResult? RoomAnalysis,
    PersonIdentificationResult? ProfileMatch,
    string? ErrorMessage);

public enum PhotoWorkflowCancellation
{
    CaptureCancelled,
    GalleryCancelled,
    IntentCancelled,
    RequestCancelled,
}

public sealed record PhotoWorkflowError(
    string Code,
    string Message,
    Exception? Exception = null,
    PhotoSource? Source = null,
    PhotoOperationStatus Status = PhotoOperationStatus.Error);

public enum PhotoWorkflowOutcome
{
    Coffee,
    Profile,
    Room,
    Cancelled,
    Error,
}

public interface IBaristaPhotoWorkflowCallbacks
{
    void SetPhotoWorkflowBusy(bool isBusy);

    Task VisionResultAsync(VisionCallback result, CancellationToken cancellationToken);

    Task<PhotoIntentChoice> ChoosePhotoIntentAsync(
        PhotoAsset photo,
        PhotoClassificationResult classification,
        CancellationToken cancellationToken);

    Task RouteCoffeeAsync(
        PhotoAsset photo,
        BeanLabelExtraction? coffeeDetails,
        CancellationToken cancellationToken);

    Task RouteProfileAsync(PhotoAsset photo, CancellationToken cancellationToken);

    Task RouteRoomAsync(
        PhotoAsset photo,
        VisionAnalysisResult analysis,
        CancellationToken cancellationToken);

    Task PhotoWorkflowCancelledAsync(
        PhotoWorkflowCancellation reason,
        CancellationToken cancellationToken);

    Task PhotoWorkflowFailedAsync(
        PhotoWorkflowError error,
        CancellationToken cancellationToken);
}

public static class BaristaPlatformServices
{
    static readonly object Gate = new();
    static IBaristaPhotoService? _photos;
    static IBaristaVisionAnalyzer? _vision;

    public static bool IsConfigured
    {
        get
        {
            lock (Gate)
                return _photos is not null && _vision is not null;
        }
    }

    public static void Configure(IBaristaPhotoService photos, IBaristaVisionAnalyzer vision)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(vision);
        lock (Gate)
        {
            _photos = photos;
            _vision = vision;
        }
    }

    public static bool TryGet(
        out IBaristaPhotoService? photos,
        out IBaristaVisionAnalyzer? vision)
    {
        lock (Gate)
        {
            photos = _photos;
            vision = _vision;
            return photos is not null && vision is not null;
        }
    }
}
