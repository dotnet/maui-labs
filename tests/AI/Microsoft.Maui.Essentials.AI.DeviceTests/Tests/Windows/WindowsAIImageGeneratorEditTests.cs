#if WINDOWS
using Microsoft.Maui.Essentials.AI;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

#pragma warning disable MEAI001 // IImageGenerator is experimental in the pinned SDK.

public class WindowsAIImageGeneratorEditTests : ImageGeneratorEditTestsBase
{
	protected override IImageGenerator CreateImageGenerator() => new WindowsAIImageGenerator();
}

#pragma warning restore MEAI001
#endif
