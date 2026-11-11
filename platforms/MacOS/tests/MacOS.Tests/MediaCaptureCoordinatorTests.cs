using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Platforms.MacOS.Essentials;

namespace Microsoft.Maui.Platforms.MacOS.Tests;

public sealed class MediaCaptureCoordinatorTests
{
    [Fact]
    public async Task Capture_ProviderPathsAndCancellation_ArePreserved()
    {
        var capture = new MediaCaptureCoordinator(
            new TestProvider("/capture/photo.png", null));

        Assert.Equal("/capture/photo.png", await capture.CapturePhotoAsync("photo"));
        Assert.Null(await capture.CaptureVideoAsync("video"));
    }

    [Fact]
    public async Task Capture_Unsupported_ThrowsExplicitly()
    {
        var capture = new MediaCaptureCoordinator(
            new TestProvider(null, null, isSupported: false));

        await Assert.ThrowsAsync<FeatureNotSupportedException>(
            () => capture.CapturePhotoAsync(null));
        await Assert.ThrowsAsync<FeatureNotSupportedException>(
            () => capture.CaptureVideoAsync(null));
    }

    [Fact]
    public async Task Capture_ProviderFailure_IsNotConvertedToCancellation()
    {
        var failure = new InvalidOperationException("camera failed");
        var capture = new MediaCaptureCoordinator(new FailingProvider(failure));

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
            () => capture.CapturePhotoAsync(null)));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
            () => capture.CaptureVideoAsync(null)));
    }

    sealed class TestProvider(
        string? photoPath,
        string? videoPath,
        bool isSupported = true) : IMacOSMediaCaptureProvider
    {
        public bool IsSupported => isSupported;

        public Task<string?> CapturePhotoAsync(string? title) =>
            Task.FromResult(photoPath);

        public Task<string?> CaptureVideoAsync(string? title) =>
            Task.FromResult(videoPath);
    }

    sealed class FailingProvider(Exception failure) : IMacOSMediaCaptureProvider
    {
        public bool IsSupported => true;

        public Task<string?> CapturePhotoAsync(string? title) =>
            Task.FromException<string?>(failure);

        public Task<string?> CaptureVideoAsync(string? title) =>
            Task.FromException<string?>(failure);
    }
}
