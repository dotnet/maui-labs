using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Media;

namespace Essentials.Tests;

public sealed class MediaPickerTests
{
	[Fact]
	public async Task PickerCancellation_ReturnsNull_WithoutHidingOtherFailures()
	{
		Assert.Null(await LinuxMediaPicker.PickOrCancelAsync<string>(
			() => Task.FromException<string?>(new OperationCanceledException())));
		var failure = new InvalidOperationException("The backend dismissed a failed operation.");
		Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
			LinuxMediaPicker.PickOrCancelAsync<string>(() => Task.FromException<string?>(failure))));
		Assert.Equal("selected", await LinuxMediaPicker.PickOrCancelAsync(() => Task.FromResult<string?>("selected")));
		Assert.Null(await LinuxMediaPicker.PickOrCancelAsync(() => Task.FromResult<string?>(null)));
	}

	[NativeGtkFact]
	public async Task GtkPickerDismissal_ReturnsNull_ButOtherGErrorsPropagate()
	{
		GLib.Module.Initialize();
		using var dismissedText = GLib.Internal.NonNullableUtf8StringOwnedHandle.Create("Dismissed by user");
		using var dismissed = new GLib.GException(GLib.Internal.Error.NewLiteral(
			GLib.Functions.QuarkFromString("gtk-dialog-error-quark"), 2, dismissedText));
		Assert.Null(await LinuxMediaPicker.PickOrCancelAsync<string>(() => Task.FromException<string?>(dismissed)));

		using var failureText = GLib.Internal.NonNullableUtf8StringOwnedHandle.Create("Dialog dismissed because the file portal failed");
		using var failure = new GLib.GException(GLib.Internal.Error.NewLiteral(
			GLib.Functions.QuarkFromString("gtk-dialog-error-quark"), 0, failureText));
		Assert.Same(failure, await Assert.ThrowsAsync<GLib.GException>(() =>
			LinuxMediaPicker.PickOrCancelAsync<string>(() => Task.FromException<string?>(failure))));
	}

	[Theory]
	[InlineData(0x04000001u, 0u, true)]
	[InlineData(0x01000001u, 0u, true)]
	[InlineData(0x04001000u, 0u, true)]
	[InlineData(0x04000002u, 0u, false)]
	[InlineData(0x04800000u, 0u, false)]
	[InlineData(0x00000001u, 0u, false)]
	[InlineData(0x84000001u, 0x04800000u, false)]
	[InlineData(0x84000001u, 0x04000001u, true)]
	public void V4l2Probe_RequiresPerDeviceCaptureAndIoCapabilities(
		uint capabilities, uint deviceCapabilities, bool expected) =>
		Assert.Equal(expected, V4l2DeviceProbe.HasCaptureCapability(capabilities, deviceCapabilities));

	[Fact]
	public void DeviceSelection_SkipsUnusableNodes_AndFindsLaterCamera()
	{
		var probed = new List<string>();
		var selected = GStreamerMediaCaptureProvider.FindVideoDevice(
			["/dev/video2", "/dev/video0", "/dev/video1"],
			path =>
			{
				probed.Add(path);
				return path == "/dev/video2";
			});
		Assert.Equal("/dev/video2", selected);
		Assert.Equal(["/dev/video0", "/dev/video1", "/dev/video2"], probed);
		Assert.Null(GStreamerMediaCaptureProvider.FindVideoDevice(["/dev/video0"], _ => false));
	}

	[NativeGtkFact]
	public void V4l2Probe_RejectsMissingNodesAndNonVideoDescriptors()
	{
		Assert.False(V4l2DeviceProbe.CanCapture("/dev/no-such-maui-camera"));
		Assert.False(V4l2DeviceProbe.CanCapture("/dev/null"));
	}

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
