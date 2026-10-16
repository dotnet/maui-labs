#if ENABLE_OPENAI_CLIENT

using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class OpenAIChatClient : DelegatingChatClient
{
	public OpenAIChatClient()
		: base(IPlatformApplication.Current!.Services.GetRequiredService<IChatClient>())
	{
	}
}
public class OpenAIChatClientCancellationTests : ChatClientCancellationTestsBase<OpenAIChatClient>
{
}
public class OpenAIChatClientFunctionCallingTestsBase : ChatClientFunctionCallingTestsBase<OpenAIChatClient>
{
	protected override IChatClient EnableFunctionCalling(OpenAIChatClient client)
	{
		return client.AsBuilder()
			.UseFunctionInvocation()
			.Build();
	}
}
public class OpenAIChatClientGetServiceTests : ChatClientGetServiceTestsBase<OpenAIChatClient>
{
	protected override string ExpectedProviderName => "openai";
	protected override string ExpectedDefaultModelId => "gpt-4o";
}
public class OpenAIChatClientInstantiationTests : ChatClientInstantiationTestsBase<OpenAIChatClient>
{
}
public class OpenAIChatClientMessagesTests : ChatClientMessagesTestsBase<OpenAIChatClient>
{
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public override async Task GetResponseAsync_WithUnsupportedContentType()
	{
		using var client = new OpenAIChatClient();
		// The OpenAI adapter explicitly ignores unrecognized content instead of rejecting it.
		var message = new ChatMessage(ChatRole.User,
			[new TextContent("Say hello"), new UnsupportedContentForTesting()]);

		var response = await client.GetResponseAsync([message]);

		Assert.NotNull(response);
		Assert.NotEmpty(response.Messages);
	}
}
public class OpenAIChatClientOptionsTests : ChatClientOptionsTestsBase<OpenAIChatClient>
{
}
public class OpenAIChatClientResponseTests : ChatClientResponseTestsBase<OpenAIChatClient>
{
}
public class OpenAIChatClientStreamingTests : ChatClientStreamingTestsBase<OpenAIChatClient>
{
}
public class OpenAIChatClientUsageTests(ITestOutputHelper output) : ChatClientUsageTestsBase<OpenAIChatClient>(output)
{
}
public class OpenAIChatClientJsonSchemaTests : ChatClientJsonSchemaTestsBase<OpenAIChatClient>
{
}

#endif
