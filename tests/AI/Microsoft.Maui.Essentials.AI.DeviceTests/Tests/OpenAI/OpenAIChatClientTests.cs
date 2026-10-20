#if ENABLE_OPENAI_CLIENT

using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
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
	protected override bool SupportsRequiredToolMode => true;

	protected override IChatClient EnableFunctionCalling(OpenAIChatClient client, Action<ChatOptions>? configureOptions = null)
	{
		var builder = client.AsBuilder().UseFunctionInvocation();
		// Configure each provider round trip inside the invocation loop, including requests after tool results.
		if (configureOptions is not null)
			builder.ConfigureOptions(configureOptions);
		return builder.Build();
	}
}

public class OpenAIChatClientGetServiceTests : ChatClientGetServiceTestsBase<OpenAIChatClient>
{
	protected override string ExpectedProviderName => "openai";
	protected override string ExpectedDefaultModelId =>
		IPlatformApplication.Current!.Services.GetRequiredService<IConfiguration>()["AI:DeploymentName"]
			?? throw new InvalidOperationException("AI:DeploymentName is required by the OpenAI test fixture.");
}

public class OpenAIChatClientInstantiationTests : ChatClientInstantiationTestsBase<OpenAIChatClient>
{
}

public class OpenAIChatClientMessagesTests : ChatClientMessagesTestsBase<OpenAIChatClient>
{
	// The OpenAI baseline adapter may forward or omit non-image media; native providers must explicitly reject unsupported media.
	protected override bool RejectsUnsupportedMedia => false;

	// OpenAI consumes PDF input; its supported payload is covered by the shared binary-content tests.
	protected override bool SupportsBinaryMedia(string mediaType) => mediaType == "application/pdf";

	// Responses accepts instructions as input items; it does not require a separate current User prompt.
	protected override bool RequiresPrompt => false;

	// Responses may forward hosted items or omit response metadata. This opts out of local fail-fast rejection,
	// not into claiming Azure supports every recognized kind in every role (which requires live endpoint coverage).
	protected override bool RejectsUnsupportedStandardContent(string kind, ChatRole role) => false;

	// Azure/OpenAI rejects empty input with ClientResultException HTTP 400, not the native clients' ArgumentException.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public override async Task GetResponseAsync_WithEmptyMessages_ThrowsException()
	{
		using var client = new OpenAIChatClient();

		var error = await Assert.ThrowsAsync<ClientResultException>(
			() => client.GetResponseAsync([]));

		Assert.Equal(400, error.Status);
		Assert.Contains("input", error.Message, StringComparison.OrdinalIgnoreCase);
	}
}

public class OpenAIChatClientOptionsTests : ChatClientOptionsTestsBase<OpenAIChatClient>
{
	// Responses supports server-side conversations and continuation/background operations.
	protected override bool RejectsStatefulConversationOptions => false;
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
