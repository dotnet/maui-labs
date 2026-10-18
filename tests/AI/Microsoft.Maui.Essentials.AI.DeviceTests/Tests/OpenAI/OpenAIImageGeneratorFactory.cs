#if ENABLE_OPENAI_IMAGE_TESTS
using Microsoft.Extensions.AI;
using OpenAI.Images;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

#pragma warning disable MEAI001 // IImageGenerator is experimental in the pinned SDK.

internal static class OpenAIImageGeneratorFactory
{
	internal static IImageGenerator Create()
	{
		// Each test owns its adapter, not a DelegatingImageGenerator around a DI-owned IImageGenerator.
		// The SDK ImageClient stays shared, matching the sample's GetImageClient(...).AsIImageGenerator() path.
		var client = IPlatformApplication.Current!.Services.GetRequiredService<ImageClient>();
		return client.AsIImageGenerator();
	}
}

#pragma warning restore MEAI001
#endif
