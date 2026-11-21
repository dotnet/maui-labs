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
	public void GtkPickerDismissal_ReturnsNull_ButOtherGErrorsPropagate()
	{
		GLib.Module.Initialize();
		foreach (var code in new[] { Gtk.DialogError.Dismissed, Gtk.DialogError.Cancelled })
		{
			using var translatedText = GLib.Internal.NonNullableUtf8StringOwnedHandle.Create("Translated user cancellation");
			using var error = GLib.Internal.Error.NewLiteral(
				GLib.Functions.QuarkFromString("gtk-dialog-error-quark"), (int)code, translatedText);
			Assert.Null(GtkMediaFileDialog.GetResult<string>(0, error, _ => throw new InvalidOperationException()));
		}

		foreach (var domain in new[] { "gtk-dialog-error-quark", "other-error-domain" })
		{
			using var misleadingText = GLib.Internal.NonNullableUtf8StringOwnedHandle.Create("Dismissed by user");
			using var error = GLib.Internal.Error.NewLiteral(
				GLib.Functions.QuarkFromString(domain),
				domain == "gtk-dialog-error-quark" ? (int)Gtk.DialogError.Failed : (int)Gtk.DialogError.Dismissed,
				misleadingText);
			using var failure = Assert.Throws<GLib.GException>(() =>
				GtkMediaFileDialog.GetResult<string>(0, error, _ => throw new InvalidOperationException()));
			Assert.Equal("Dismissed by user", failure.Message);
		}
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
