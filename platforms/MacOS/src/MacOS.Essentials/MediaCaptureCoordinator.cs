using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

internal interface IMacOSMediaCaptureProvider
{
    bool IsSupported { get; }
    Task<string?> CapturePhotoAsync(string? title);
    Task<string?> CaptureVideoAsync(string? title);
}

internal sealed class MediaCaptureCoordinator(IMacOSMediaCaptureProvider provider)
{
    public bool IsSupported => provider.IsSupported;

    public Task<string?> CapturePhotoAsync(string? title)
    {
        EnsureSupported("photo");
        return provider.CapturePhotoAsync(title);
    }

    public Task<string?> CaptureVideoAsync(string? title)
    {
        EnsureSupported("video");
        return provider.CaptureVideoAsync(title);
    }

    void EnsureSupported(string mediaKind)
    {
        if (!IsSupported)
            throw new FeatureNotSupportedException(
                $"macOS {mediaKind} capture requires a camera and NSCameraUsageDescription in the app manifest.");
    }
}
