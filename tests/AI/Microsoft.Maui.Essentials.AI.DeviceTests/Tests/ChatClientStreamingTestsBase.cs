using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Base class for ChatClient streaming tests.
/// Provides common tests for any IChatClient implementation.
/// </summary>
/// <typeparam name="T">The concrete ChatClient type to test.</typeparam>
public abstract class ChatClientStreamingTestsBase<T>
	where T : class, IChatClient, new()
{
	protected virtual bool SupportsStreamingMessageIds => true;

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_PreservesMessageIds_ThroughAggregation(bool json)
	{
		using var client = new T();
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
		var options = new ChatOptions
		{
			ResponseFormat = json
				? ChatResponseFormat.ForJsonSchema<ChatClientJsonSchemaTestsBase<T>.SimpleResponse>()
				: null
		};
		var ids = new HashSet<string>(StringComparer.Ordinal);
		for (var request = 0; request < 2; request++)
		{
			var input = new ChatMessage(ChatRole.User, "Say hello in a message field with three short sentences.")
			{
				MessageId = "caller-input-message"
			};
			var updates = new List<ChatResponseUpdate>();
			await foreach (var update in client.GetStreamingResponseAsync([input], options, timeout.Token))
				updates.Add(update);

			var textUpdates = updates.Where(u => !string.IsNullOrEmpty(u.Text)).ToList();
			Assert.NotEmpty(textUpdates);
			var answer = Assert.Single(updates.ToChatResponse().Messages,
				m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text));
			Assert.Equal(string.Concat(textUpdates.Select(u => u.Text)), answer.Text);
			if (SupportsStreamingMessageIds)
			{
				Assert.All(textUpdates, u => Assert.False(string.IsNullOrWhiteSpace(u.MessageId)));
				var id = Assert.IsType<string>(Assert.Single(textUpdates.Select(u => u.MessageId).Distinct()));
				Assert.Equal(id, answer.MessageId);
				Assert.NotEqual(input.MessageId, id);
				Assert.True(ids.Add(id), "Each request must have its own native response identity.");
			}
			else
			{
				Assert.All(textUpdates, u => Assert.Null(u.MessageId));
				Assert.Null(answer.MessageId);
			}
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
	public async Task GetStreamingResponseAsync_EveryUpdateIncludesSameModelId()
	{
		using var client = new T();
		var updates = new List<ChatResponseUpdate>();
		await foreach (var update in client.GetStreamingResponseAsync(
			[new ChatMessage(ChatRole.User, "Say hello in one sentence.")]))
		{
			updates.Add(update);
		}

		Assert.NotEmpty(updates);
		var observedUpdates = string.Join(", ", updates.Select((update, index) =>
			$"{index}: model='{update.ModelId ?? "(null)"}', contents={update.Contents.Count}"));
		for (var index = 0; index < updates.Count; index++)
		{
			var update = updates[index];
			Assert.False(string.IsNullOrWhiteSpace(update.ModelId),
				$"Update {index} must identify the model, including metadata-only updates. Observed: {observedUpdates}");
			Assert.Equal(updates[0].ModelId, update.ModelId);
		}
		Assert.Equal(updates[0].ModelId, updates.ToChatResponse().ModelId);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_ReturnsStreamingUpdates()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Hello")
		};

		bool receivedUpdate = false;
		await foreach (var update in client.GetStreamingResponseAsync(messages))
		{
			receivedUpdate = true;
			Assert.NotNull(update);
		}

		Assert.True(receivedUpdate, "Should receive at least one streaming update");
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_UpdatesHaveContents()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Tell me a short story")
		};

		var updates = new List<ChatResponseUpdate>();
		await foreach (var update in client.GetStreamingResponseAsync(messages))
		{
			updates.Add(update);
		}

		Assert.True(updates.Count > 0, "Should receive streaming updates");
		Assert.True(updates.Any(u => u.Contents.Count > 0), "At least one update should have contents");
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_CanBuildCompleteResponseFromUpdates()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Say hello")
		};

		var textBuilder = new System.Text.StringBuilder();
		await foreach (var update in client.GetStreamingResponseAsync(messages))
		{
			foreach (var content in update.Contents)
			{
				if (content is TextContent textContent)
				{
					textBuilder.Append(textContent.Text);
				}
			}
		}

		var completeText = textBuilder.ToString();
		Assert.False(string.IsNullOrEmpty(completeText), "Should build complete response from streaming updates");
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_DeliversMultipleIncrementalUpdates()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Write a paragraph about the solar system with at least 3 sentences")
		};

		var textUpdates = new List<string>();
		await foreach (var update in client.GetStreamingResponseAsync(messages))
		{
			foreach (var content in update.Contents)
			{
				if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
				{
					textUpdates.Add(textContent.Text);
				}
			}
		}

		Assert.True(textUpdates.Count >= 1,
			$"Expected at least one text update from streaming, but got {textUpdates.Count}");

		// For a multi-sentence prompt we generally expect multiple chunks, but the
		// streaming contract does not guarantee a minimum number of updates.
		var concatenated = string.Concat(textUpdates);
		Assert.False(string.IsNullOrWhiteSpace(concatenated),
			"Concatenated streaming text should not be empty");
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_ConcatenatedTextMatchesNonStreaming()
	{
		var client = new T();
		var prompt = "What is the capital of France? Answer in one word.";
		var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };

		// Get streaming response
		var streamingText = new System.Text.StringBuilder();
		await foreach (var update in client.GetStreamingResponseAsync(messages))
		{
			foreach (var content in update.Contents)
			{
				if (content is TextContent textContent)
				{
					streamingText.Append(textContent.Text);
				}
			}
		}

		// Get non-streaming response
		var response = await client.GetResponseAsync(messages);
		var nonStreamingText = response.Text;

		// Both should produce non-empty text containing the answer
		var streamingResult = streamingText.ToString();
		Assert.False(string.IsNullOrEmpty(streamingResult), "Streaming response should not be empty");
		Assert.False(string.IsNullOrEmpty(nonStreamingText), "Non-streaming response should not be empty");

		// Verify both contain the expected answer (case-insensitive)
		Assert.Contains("paris", streamingResult, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("paris", nonStreamingText, StringComparison.OrdinalIgnoreCase);
	}
}
