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
	protected override bool? SupportsRequiredToolMode => true;
	protected override bool SupportsDeterministicChainedToolCalls => true;
	protected override bool PreservesToolActivityContext => true;
	protected override bool PreservesToolCancellation => true;

	protected override IChatClient EnableFunctionCalling(OpenAIChatClient client, Action<ChatOptions>? configureOptions = null)
	{
		var builder = client.AsBuilder().UseFunctionInvocation();
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
	// Responses may forward hosted items or omit metadata; it has no local fail-fast content policy.
	protected override bool RejectsUnsupportedMedia => false;
	protected override bool SupportsBinaryMedia(string mediaType) => mediaType is "image/png" or "application/pdf";
	protected override bool RequiresPrompt => false;
	protected override bool RejectsUnsupportedStandardContent(string kind, ChatRole role) => false;
	protected override bool RejectsMisplacedFunctionContent(ChatRole role, bool isCall) => false;
	protected override bool SupportsTrailingSystemInstruction => true;

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
	// Responses supports conversations and continuation/background operations.
	protected override bool RejectsStatefulConversationOptions => false;
	protected override bool RequiresExplicitJsonSchema => false;

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public override async Task GetResponseAsync_WithExtremeTemperature_HandlesGracefully()
	{
		try
		{
			await base.GetResponseAsync_WithExtremeTemperature_HandlesGracefully();
		}
		catch (ClientResultException error)
		{
			Assert.Equal(400, error.Status);
			Assert.Contains("temperature", error.Message, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("not supported", error.Message, StringComparison.OrdinalIgnoreCase);
		}
	}
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
	protected override bool? RequiresExplicitJsonSchema => false;
}

#endif
