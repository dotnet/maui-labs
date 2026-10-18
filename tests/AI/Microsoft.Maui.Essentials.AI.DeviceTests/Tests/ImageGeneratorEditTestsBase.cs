using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

#pragma warning disable MEAI001 // IImageGenerator is experimental in the pinned SDK.

// Bind only providers/deployments that support single-original editing; do not skip unsupported operations at runtime.
public abstract class ImageGeneratorEditTestsBase
{
	protected abstract IImageGenerator CreateImageGenerator();

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GenerateAsync_WithOriginalImage_ReturnsImageContent()
	{
		using var generator = CreateImageGenerator();
		using var timeout = new CancellationTokenSource(ImageGeneratorTestData.RequestTimeout);
		var original = ImageGeneratorTestData.LoadOriginal();
		var request = new ImageGenerationRequest(
			ImageGeneratorTestData.EditPrompt, [new DataContent(original.Bytes, original.MediaType)]);

		var response = await generator.GenerateAsync(request, cancellationToken: timeout.Token);

		ImageGeneratorTestData.AssertImageContent(response);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task SampleService_WithOriginalImage_ReturnsUsableImages()
	{
		using var generator = CreateImageGenerator();
		using var timeout = new CancellationTokenSource(ImageGeneratorTestData.RequestTimeout);

		var images = await new ImageGenerationService().GenerateAsync(
			generator, ImageGeneratorTestData.EditPrompt, ImageGeneratorTestData.LoadOriginal(),
			cancellationToken: timeout.Token);

		ImageGeneratorTestData.AssertSampleImages(images);
	}
}

#pragma warning restore MEAI001
