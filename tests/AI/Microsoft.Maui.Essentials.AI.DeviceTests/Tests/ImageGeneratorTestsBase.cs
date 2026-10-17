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

internal static class ImageGeneratorTestData
{
	internal static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);
	internal const string Prompt = "A blue sailboat on a calm lake, watercolor painting.";
	internal const string EditPrompt = "Make the sky a warm sunset while keeping the main subject.";

	internal static ImageAttachment LoadOriginal()
	{
		using var stream = typeof(ImageGeneratorTestData).Assembly.GetManifestResourceStream(
			"Microsoft.Maui.Essentials.AI.DeviceTests.ImageInput.png")
			?? throw new InvalidOperationException("The linked sample image resource is missing.");
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return new ImageAttachment("playground_sample.png", "image/png", buffer.ToArray());
	}

	internal static void AssertImageContent(ImageGenerationResponse response)
	{
		Assert.NotNull(response);
		Assert.NotEmpty(response.Contents);
		Assert.All(response.Contents, content =>
		{
			switch (content)
			{
				case DataContent data:
					Assert.True(data.HasTopLevelMediaType("image"));
					Assert.False(data.Data.IsEmpty);
					break;
				case UriContent uri:
					Assert.True(uri.HasTopLevelMediaType("image"));
					Assert.True(uri.Uri.IsAbsoluteUri);
					Assert.True(uri.Uri.Scheme is "https" or "http");
					break;
				default:
					Assert.Fail($"Expected image content, received {content.GetType().Name}.");
					break;
			}
		});
	}

	internal static void AssertSampleImages(IReadOnlyList<GeneratedImage> images)
	{
		Assert.NotEmpty(images);
		Assert.All(images, image =>
		{
			Assert.StartsWith("image/", image.MediaType, StringComparison.OrdinalIgnoreCase);
			Assert.True((image.Bytes is not null) != (image.Uri is not null));
			if (image.Bytes is not null)
				Assert.NotEmpty(image.Bytes);
			if (image.Uri is not null)
			{
				Assert.True(image.Uri.IsAbsoluteUri);
				Assert.True(image.Uri.Scheme is "https" or "http");
			}
		});
	}
}

#pragma warning restore MEAI001
