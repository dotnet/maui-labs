using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

#pragma warning disable MEAI001 // IImageGenerator is experimental in the pinned SDK.

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
