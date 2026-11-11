using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Media;

namespace Essentials.Tests;

public sealed class MediaPickerTests
{
	[Fact]
	public async Task CaptureWrapsProviderFiles()
	{
		var provider = new TestCaptureProvider("/capture/photo.jpg", "/capture/video.webm");
		var picker = new LinuxMediaPicker(provider);

		var photo = await picker.CapturePhotoAsync(null);
		var video = await picker.CaptureVideoAsync(null);

		Assert.Equal("/capture/photo.jpg", photo?.FullPath);
		Assert.Equal("/capture/video.webm", video?.FullPath);
	}

	[Fact]
	public async Task CaptureCancellationReturnsNull()
	{
		var picker = new LinuxMediaPicker(new TestCaptureProvider(null, null));

		Assert.Null(await picker.CapturePhotoAsync(null));
		Assert.Null(await picker.CaptureVideoAsync(null));
	}

	[Fact]
	public async Task MissingGStreamerIsExplicit()
	{
		var picker = new LinuxMediaPicker(new TestCaptureProvider(null, null, false));

		await Assert.ThrowsAsync<FeatureNotSupportedException>(() => picker.CapturePhotoAsync(null));
		await Assert.ThrowsAsync<FeatureNotSupportedException>(() => picker.CaptureVideoAsync(null));
	}

	[Fact]
	public async Task ProviderFailuresAreNotConvertedToCancellation()
	{
		var failure = new InvalidOperationException("camera failed");
		var picker = new LinuxMediaPicker(new FailingCaptureProvider(failure));

		Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
			() => picker.CapturePhotoAsync(null)));
		Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
			() => picker.CaptureVideoAsync(null)));
	}

	[Fact]
	public void GStreamerContractRequiresCameraCodecsAndContainer()
	{
		Assert.Equal(
			["v4l2src", "videoconvert", "jpegenc", "vp8enc", "webmmux"],
			GStreamerMediaCaptureProvider.RequiredPluginNames);
	}

	[Fact]
	public void GStreamerPipelinesSelectV4l2DeviceWithoutSyntheticFallback()
	{
		var photo = GStreamerMediaCaptureProvider.CreatePhotoArguments(
			"/dev/video7",
			"/capture/photo.jpg");
		var video = GStreamerMediaCaptureProvider.CreateVideoArguments(
			"/dev/video7",
			"/capture/video.webm");

		Assert.Contains("v4l2src", photo);
		Assert.Contains("device=/dev/video7", photo);
		Assert.Contains("num-buffers=1", photo);
		Assert.Contains("v4l2src", video);
		Assert.Contains("device=/dev/video7", video);
		Assert.DoesNotContain("autovideosrc", photo);
		Assert.DoesNotContain("autovideosrc", video);
		Assert.DoesNotContain("videotestsrc", photo);
		Assert.DoesNotContain("videotestsrc", video);
	}

	sealed class TestCaptureProvider(
		string? photoPath,
		string? videoPath,
		bool isSupported = true) : ILinuxMediaCaptureProvider
	{
		public bool IsSupported => isSupported;

		public Task<string?> CapturePhotoAsync(string? title) =>
			Task.FromResult(photoPath);

		public Task<string?> CaptureVideoAsync(string? title) =>
			Task.FromResult(videoPath);
	}

	sealed class FailingCaptureProvider(Exception failure) : ILinuxMediaCaptureProvider
	{
		public bool IsSupported => true;

		public Task<string?> CapturePhotoAsync(string? title) =>
			Task.FromException<string?>(failure);

		public Task<string?> CaptureVideoAsync(string? title) =>
			Task.FromException<string?>(failure);
	}
}
