#if ENABLE_OPENAI_IMAGE_TESTS && ENABLE_OPENAI_IMAGE_EDIT_TESTS
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

#pragma warning disable MEAI001 // IImageGenerator is experimental in the pinned SDK.

// Azure/OpenAI editing is deployment-specific (for example, GPT image versus DALL-E 3).
// Opt in separately with EnableOpenAIImageEditTests; generation-only deployments keep the common generation suite.
public class OpenAIImageGeneratorEditTests : ImageGeneratorEditTestsBase
{
	protected override IImageGenerator CreateImageGenerator() => OpenAIImageGeneratorFactory.Create();
}

#pragma warning restore MEAI001
#endif
