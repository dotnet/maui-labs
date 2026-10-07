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
	[Fact]
	public override async Task GetResponseAsync_WithExtremeTemperature_HandlesGracefully()
	{
		using IChatClient client = new GeminiNanoChatClient();
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
			client.GetResponseAsync([new(ChatRole.User, "Hello")], new() { Temperature = 2.0f }));
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public override Task GetResponseAsync_WithResponseFormat_AcceptsJsonFormat() =>
		base.GetResponseAsync_WithResponseFormat_AcceptsJsonFormat();

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public override Task GetStreamingResponseAsync_WithResponseFormat_AcceptsJsonFormat() =>
		base.GetStreamingResponseAsync_WithResponseFormat_AcceptsJsonFormat();
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
