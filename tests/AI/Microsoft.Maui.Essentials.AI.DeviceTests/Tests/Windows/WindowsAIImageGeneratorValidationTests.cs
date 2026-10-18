#if WINDOWS
using Size = System.Drawing.Size;
using Microsoft.Maui.Essentials.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

#pragma warning disable MEAI001 // IImageGenerator is experimental in the pinned SDK.

public class WindowsAIImageGeneratorValidationTests
{
	// Windows ImageGenerator needs an explicitly identified mask for multi-image operations; other providers may accept multiple originals.
	[Theory]
	[InlineData(2)]
	[InlineData(3)]
	public async Task GenerateAsync_MultipleImages_RejectsAmbiguousMaskBeforeSetup(int imageCount)
	{
		using var generator = new WindowsAIImageGenerator();
		var image = new DataContent(new byte[] { 0 }, "image/png");

		var request = new ImageGenerationRequest("Use both images",
			Enumerable.Repeat<AIContent>(image, imageCount));
		var error = await Assert.ThrowsAsync<NotSupportedException>(() => generator.GenerateAsync(request));

		Assert.Contains("mask", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	// WindowsAIImageBuffers has no WebP encoder; this must not constrain cloud providers that support WebP.
	[Fact]
	public async Task GenerateAsync_UnsupportedMediaType_RejectsBeforeSetup()
	{
		using var generator = new WindowsAIImageGenerator();

		var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
			generator.GenerateAsync(
				new ImageGenerationRequest("A blue sailboat on a lake."),
				new ImageGenerationOptions { MediaType = "image/webp" }));

		Assert.Contains("image/webp", error.Message, StringComparison.Ordinal);
	}

	// Windows GenerateImageFromTextPrompt has no size parameter, unlike providers with selectable output sizes.
	[Fact]
	public async Task GenerateAsync_RequestedSize_RejectsBeforeSetup()
	{
		using var generator = new WindowsAIImageGenerator();

		var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
			generator.GenerateAsync(
				new ImageGenerationRequest("A blue sailboat on a lake."),
				new ImageGenerationOptions { ImageSize = new Size(1024, 1024) }));

		Assert.Contains("size", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	// Windows returns locally encoded pixels and cannot host a URI; cloud image generators may return hosted images.
	[Fact]
	public async Task GenerateAsync_UriResponseFormat_RejectsBeforeSetup()
	{
		using var generator = new WindowsAIImageGenerator();

		var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
			generator.GenerateAsync(
				new ImageGenerationRequest("A blue sailboat on a lake."),
				new ImageGenerationOptions { ResponseFormat = ImageGenerationResponseFormat.Uri }));

		Assert.Contains("URI", error.Message, StringComparison.OrdinalIgnoreCase);
	}
}

#pragma warning restore MEAI001
#endif
