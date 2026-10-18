#if ENABLE_OPENAI_IMAGE_TESTS
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

#pragma warning disable MEAI001 // IImageGenerator is experimental in the pinned SDK.

public class OpenAIImageGeneratorTests : ImageGeneratorTestsBase
{
	protected override IImageGenerator CreateImageGenerator() => OpenAIImageGeneratorFactory.Create();
}

#pragma warning restore MEAI001
#endif
