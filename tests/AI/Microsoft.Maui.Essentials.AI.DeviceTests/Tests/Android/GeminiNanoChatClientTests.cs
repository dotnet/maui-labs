#if ANDROID
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class GeminiNanoChatClientGetServiceTests : ChatClientGetServiceTestsBase<GeminiNanoChatClient>
{
	protected override string ExpectedProviderName => "google";
	protected override string ExpectedDefaultModelId => "gemini-nano";
}

public class GeminiNanoChatClientInstantiationTests : ChatClientInstantiationTestsBase<GeminiNanoChatClient>
{
}

public class GeminiNanoChatClientMessagesTests : ChatClientMessagesTestsBase<GeminiNanoChatClient>
{
}

public class GeminiNanoChatClientOptionsTests : ChatClientOptionsTestsBase<GeminiNanoChatClient>
{
}

public class GeminiNanoChatClientResponseTests : ChatClientResponseTestsBase<GeminiNanoChatClient>
{
}

public class GeminiNanoChatClientStreamingTests : ChatClientStreamingTestsBase<GeminiNanoChatClient>
{
}

public class GeminiNanoChatClientJsonSchemaTests : ChatClientJsonSchemaTestsBase<GeminiNanoChatClient>
{
}

#endif
