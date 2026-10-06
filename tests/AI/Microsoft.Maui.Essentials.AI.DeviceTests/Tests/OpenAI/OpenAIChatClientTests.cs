#if ENABLE_OPENAI_CLIENT

using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class OpenAIChatClient : DelegatingChatClient
{
	public OpenAIChatClient()
		: base(IPlatformApplication.Current!.Services.GetRequiredService<OpenAI.Chat.ChatClient>().AsIChatClient())
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
}
public class OpenAIChatClientOptionsTests : ChatClientOptionsTestsBase<OpenAIChatClient>
{
}
public class OpenAIChatClientResponseTests : ChatClientResponseTestsBase<OpenAIChatClient>
{
}
public class OpenAIChatClientStreamingTests(ITestOutputHelper output) : ChatClientStreamingTestsBase<OpenAIChatClient>
{
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_NativeCompletionUpdatesIncludeModelId()
	{
		var client = IPlatformApplication.Current!.Services.GetRequiredService<OpenAI.Chat.ChatClient>();
		var updates = new List<OpenAI.Chat.StreamingChatCompletionUpdate>();
		await foreach (var update in client.CompleteChatStreamingAsync(
			[new OpenAI.Chat.UserChatMessage("Say hello in one sentence.")]))
		{
			output.WriteLine($"Update {updates.Count}: model='{update.Model}', content parts={update.ContentUpdate.Count}, completion ID present={!string.IsNullOrEmpty(update.CompletionId)}");
			updates.Add(update);
		}

		var completions = updates.Where(update => !string.IsNullOrEmpty(update.CompletionId)).ToList();
		Assert.NotEmpty(completions);
		Assert.All(completions, update =>
		{
			Assert.False(string.IsNullOrWhiteSpace(update.Model),
				"Native completion updates must identify the model; prompt annotations are not completions.");
			Assert.Equal(completions[0].Model, update.Model);
		});
	}
}
public class OpenAIChatClientJsonSchemaTests : ChatClientJsonSchemaTestsBase<OpenAIChatClient>
{
}

#endif
