using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

#pragma warning disable MEAI001 // IImageGenerator is experimental in the pinned SDK.

public abstract class ImageGeneratorTestsBase
{
	protected abstract IImageGenerator CreateImageGenerator();

	[Fact]
	public void GetService_ExposesGeneratorAndMetadataWithoutInference()
	{
		using var generator = CreateImageGenerator();

		Assert.Same(generator, generator.GetService<IImageGenerator>());
		var metadata = generator.GetService<ImageGeneratorMetadata>();
		Assert.NotNull(metadata);
		Assert.False(string.IsNullOrWhiteSpace(metadata.ProviderName));
		Assert.False(string.IsNullOrWhiteSpace(metadata.DefaultModelId));
		Assert.Null(generator.GetService<IImageGenerator>("unregistered"));
		Assert.Null(generator.GetService(typeof(ImageGenerationService)));
	}

	[Fact]
	public async Task GenerateAsync_CancelledBeforeSetup_DoesNotStartModel()
	{
		using var generator = CreateImageGenerator();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			generator.GenerateAsync(
				new ImageGenerationRequest(ImageGeneratorTestData.Prompt),
				cancellationToken: cancellation.Token));
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GenerateAsync_TextPrompt_ReturnsImageContent()
	{
		using var generator = CreateImageGenerator();
		using var timeout = new CancellationTokenSource(ImageGeneratorTestData.RequestTimeout);

		// Leave size, encoding and response format at provider defaults: these capabilities are not universal.
		var response = await generator.GenerateAsync(
			new ImageGenerationRequest(ImageGeneratorTestData.Prompt), cancellationToken: timeout.Token);

		ImageGeneratorTestData.AssertImageContent(response);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task SampleService_TextPrompt_ReturnsUsableImages()
	{
		using var generator = CreateImageGenerator();
		using var timeout = new CancellationTokenSource(ImageGeneratorTestData.RequestTimeout);

		var images = await new ImageGenerationService().GenerateAsync(
			generator, ImageGeneratorTestData.Prompt, original: null, cancellationToken: timeout.Token);

		ImageGeneratorTestData.AssertSampleImages(images);
	}
}

#pragma warning restore MEAI001
