using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Base class for ChatClient response tests.
/// Provides common tests for any IChatClient implementation.
/// </summary>
/// <typeparam name="T">The concrete ChatClient type to test.</typeparam>
public abstract class ChatClientResponseTestsBase<T>
	where T : class, IChatClient, new()
{
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_IncludesModelId()
	{
		using var client = new T();
		var response = await client.GetResponseAsync(
			[new ChatMessage(ChatRole.User, "Say hello in one sentence.")]);

		Assert.False(string.IsNullOrWhiteSpace(response.ModelId),
			"A successful response must identify the model that produced it.");
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_ReturnsNonNullResponse()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Hello")
		};

		var response = await client.GetResponseAsync(messages);

		Assert.NotNull(response);
	}
}
