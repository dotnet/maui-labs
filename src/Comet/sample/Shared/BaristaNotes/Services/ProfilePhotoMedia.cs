#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Services;

namespace CometSamples.BaristaNotes.Components;

/// <summary>
/// Platform hosts provide the library picker callback and return normalized in-memory image data.
/// </summary>
public static class ProfilePhotoMedia
{
    public static Func<CancellationToken, Task<PhotoOperationResult>>? CaptureFromCamera { get; set; }
    public static Func<CancellationToken, Task<PhotoOperationResult>>? PickFromLibrary { get; set; }

    public static Task<PhotoOperationResult> CaptureAsync(
        CancellationToken cancellationToken = default)
        => CaptureFromCamera?.Invoke(cancellationToken)
            ?? Task.FromResult(PhotoOperationResult.Unavailable(
                "camera_not_connected",
                "Profile photo capture is not connected on this platform."));

    public static Task<PhotoOperationResult> PickAsync(
        CancellationToken cancellationToken = default)
        => PickFromLibrary?.Invoke(cancellationToken)
            ?? Task.FromResult(PhotoOperationResult.Unavailable(
                "gallery_not_connected",
                "Profile photo library selection is not connected on this platform."));
}
