using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;

namespace Essentials.Tests;

public sealed class MediaPickerTests
{
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Capture_ReturnsProviderFile(bool photo)
	{
		var kind = photo ? WpfMediaCaptureKind.Photo : WpfMediaCaptureKind.Video;
		var provider = new TestCaptureProvider("/captured/file.jpg");
		var picker = new WPFMediaPicker(provider);

		var result = photo
			? await picker.CapturePhotoAsync()
			: await picker.CaptureVideoAsync();

		Assert.Equal("/captured/file.jpg", result?.FullPath);
		Assert.Equal(kind, provider.LastKind);
	}

	[Fact]
	public async Task Capture_CancellationReturnsNull()
	{
		var picker = new WPFMediaPicker(new TestCaptureProvider(null));

		Assert.Null(await picker.CapturePhotoAsync());
	}

	[Fact]
	public async Task Capture_UnsupportedIsExplicit()
	{
		var picker = new WPFMediaPicker(new TestCaptureProvider(null, isSupported: false));

		await Assert.ThrowsAsync<FeatureNotSupportedException>(() => picker.CapturePhotoAsync());
	}

	[Fact]
	public async Task DefaultProvider_DoesNotClaimUnsupportedUwpCapture()
	{
		var picker = new WPFMediaPicker();

		Assert.False(picker.IsCaptureSupported);
		var error = await Assert.ThrowsAsync<FeatureNotSupportedException>(
			() => picker.CaptureVideoAsync());
		Assert.Contains("Windows App SDK", error.Message, StringComparison.Ordinal);
	}

	sealed class TestCaptureProvider(string? path, bool isSupported = true) : IWpfMediaCaptureProvider
	{
		public bool IsSupported => isSupported;
		public WpfMediaCaptureKind? LastKind { get; private set; }

		public Task<string?> CaptureAsync(WpfMediaCaptureKind kind)
		{
			LastKind = kind;
			return Task.FromResult(path);
		}
	}
}
