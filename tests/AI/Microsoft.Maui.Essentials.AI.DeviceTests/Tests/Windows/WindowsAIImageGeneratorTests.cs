#if WINDOWS
using Microsoft.Maui.Essentials.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class WindowsAIImageGeneratorTests
{
	[Theory]
	[InlineData(2)]
	[InlineData(3)]
	public async Task GenerateAsync_MultipleImages_RejectsAmbiguousMaskBeforeSetup(int imageCount)
	{
		using var generator = new WindowsAIImageGenerator();
		var image = new DataContent(new byte[] { 0 }, "image/png");

#pragma warning disable MEAI001 // The image-generation request API is experimental.
		var request = new ImageGenerationRequest("Use both images",
			Enumerable.Repeat<AIContent>(image, imageCount));
		var error = await Assert.ThrowsAsync<NotSupportedException>(() => generator.GenerateAsync(request));
#pragma warning restore MEAI001

		Assert.Contains("mask", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task GenerateAsync_CancelledBeforeSetup_DoesNotStartModel()
	{
		using var generator = new WindowsAIImageGenerator();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

#pragma warning disable MEAI001 // The image-generation request API is experimental.
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			generator.GenerateAsync(
				new ImageGenerationRequest("A blue sailboat on a lake."),
				cancellationToken: cancellation.Token));
#pragma warning restore MEAI001
	}
}
#endif
