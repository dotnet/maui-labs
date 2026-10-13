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
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_PreservesMessageIds_ForTextAndJson(bool json)
	{
		using var client = new T();
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
		ChatMessage[] messages = [new(ChatRole.User, "Say hello in a message field.")
		{
			MessageId = "caller-input-message"
		}];
		var options = new ChatOptions
		{
			ResponseFormat = json
				? ChatResponseFormat.ForJsonSchema<ChatClientJsonSchemaTestsBase<T>.SimpleResponse>()
				: null
		};
		var ids = new HashSet<string>(StringComparer.Ordinal);

		for (var request = 0; request < 2; request++)
		{
			var response = await client.GetResponseAsync(messages, options, timeout.Token);
			var answer = Assert.Single(response.Messages,
				m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text));
			var id = Assert.IsType<string>(answer.MessageId);
			Assert.False(string.IsNullOrWhiteSpace(id));
			Assert.NotEqual(messages[0].MessageId, id);
			Assert.True(ids.Add(id), "A new response must not reuse a previous message's identity.");

			if (json)
			{
				var parsed = System.Text.Json.JsonSerializer.Deserialize<ChatClientJsonSchemaTestsBase<T>.SimpleResponse>(
					answer.Text, System.Text.Json.JsonSerializerOptions.Web);
				Assert.NotNull(parsed);
				Assert.False(string.IsNullOrWhiteSpace(parsed.Message));
			}
		}
	}

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
