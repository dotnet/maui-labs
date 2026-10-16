using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Base class for ChatClient message handling tests.
/// Provides common tests for any IChatClient implementation.
/// </summary>
/// <typeparam name="T">The concrete ChatClient type to test.</typeparam>
public abstract class ChatClientMessagesTestsBase<T>
	where T : class, IChatClient, new()
{
	[Fact]
	public virtual async Task GetResponseAsync_WithEmptyMessages_ThrowsException()
	{
		var client = new T();
		var messages = new List<ChatMessage>();

		await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync(messages));
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithOnlyNullTextContent_DoesNotThrow()
	{
		using var client = new T();
		var msg = new ChatMessage(ChatRole.User, [new TextContent(null)]);
		var messages = new List<ChatMessage> { msg };

		var response = await client.GetResponseAsync(messages);
		Assert.NotNull(response);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithUnsupportedContentAlongsideText_IgnoresUnsupportedContent()
	{
		using var client = new T();
		var message = new ChatMessage(ChatRole.User,
			[new TextContent("Reply with only the word HELLO."), new UnsupportedContentForTesting()]);

		var response = await client.GetResponseAsync([message]);

		Assert.NotNull(response);
		Assert.NotEmpty(response.Messages);
		Assert.Contains("HELLO", response.Text, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithOnlyUnsupportedContent_AcceptsEmptyContent()
	{
		using var client = new T();
		var msg = new ChatMessage(ChatRole.User, [new UnsupportedContentForTesting()]);
		var messages = new List<ChatMessage> { msg };

		var response = await client.GetResponseAsync(messages);

		Assert.NotNull(response);
		Assert.NotNull(response.Messages);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithUnsupportedRoleAmongValidMessages_IgnoresUnsupportedRole()
	{
		using var client = new T();
		var messages = new List<ChatMessage>
		{
			new(new ChatRole("custom"), "The secret word is PAPAYA."),
			new(ChatRole.User, "What is the secret word? If none was supplied, reply only NONE.")
		};

		var response = await client.GetResponseAsync(messages);

		Assert.NotNull(response);
		Assert.NotEmpty(response.Messages);
		Assert.Equal("NONE", response.Text.Trim(), ignoreCase: true);
		Assert.DoesNotContain("PAPAYA", response.Text, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData("text/plain", false)]
	[InlineData("application/octet-stream", false)]
	[InlineData("audio/ogg", false)]
	[InlineData("text/plain", true)]
	[InlineData("application/pdf", true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithUnrecognizedMediaType_IgnoresContent(string mediaType, bool useUri)
	{
		using var client = new T();
		AIContent content = useUri
			? new UriContent("https://example.invalid/baseline.txt", mediaType)
			: new DataContent("Unrecognized baseline payload"u8.ToArray(), mediaType);
		var message = new ChatMessage(ChatRole.User,
			[new TextContent("Reply with exactly HELLO and nothing else."), content]);

		var response = await client.GetResponseAsync([message]);

		Assert.NotNull(response);
		Assert.Contains("HELLO", response.Text, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithSystemMessage_AcceptsSystemRole()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.System, "You are a helpful assistant"),
			new(ChatRole.User, "Hello")
		};

		await client.GetResponseAsync(messages);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithConversationHistory_AcceptsMultipleMessages()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "What's 2 + 2?"),
			new(ChatRole.Assistant, "4"),
			new(ChatRole.User, "And times 3?")
		};

		await client.GetResponseAsync(messages);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithLongMessage_HandlesGracefully()
	{
		var client = new T();
		var longText = string.Join(" ", Enumerable.Repeat("This is a test message.", 100));
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, longText)
		};

		await client.GetResponseAsync(messages);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithEmptyMessageContent_HandlesGracefully()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "")
		};

		await client.GetResponseAsync(messages);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithSpecialCharacters_HandlesGracefully()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Test with special chars: 你好 🌍 émojis & symbols!")
		};

		await client.GetResponseAsync(messages);
	}

	[Fact]
	public async Task GetStreamingResponseAsync_WithEmptyMessages_ThrowsException()
	{
		var client = new T();
		var messages = new List<ChatMessage>();

		await Assert.ThrowsAnyAsync<Exception>(async () =>
		{
			await foreach (var update in client.GetStreamingResponseAsync(messages))
			{
				// Process updates
			}
		});
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_WithSystemMessage_AcceptsSystemRole()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.System, "You are a helpful assistant"),
			new(ChatRole.User, "Hello")
		};

		await foreach (var update in client.GetStreamingResponseAsync(messages))
		{
			// Process updates
		}
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_WithConversationHistory_AcceptsMultipleMessages()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "What's 2 + 2?"),
			new(ChatRole.Assistant, "4"),
			new(ChatRole.User, "And times 3?")
		};

		await foreach (var update in client.GetStreamingResponseAsync(messages))
		{
			// Process updates
		}
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_WithLongMessage_HandlesGracefully()
	{
		var client = new T();
		var longText = string.Join(" ", Enumerable.Repeat("This is a test message.", 100));
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, longText)
		};

		await foreach (var update in client.GetStreamingResponseAsync(messages))
		{
			// Process updates
		}
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_WithEmptyMessageContent_HandlesGracefully()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "")
		};

		await foreach (var update in client.GetStreamingResponseAsync(messages))
		{
			// Process updates
		}
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_WithSpecialCharacters_HandlesGracefully()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Test with special chars: 你好 🌍 émojis & symbols!")
		};

		await foreach (var update in client.GetStreamingResponseAsync(messages))
		{
			// Process updates
		}
	}

	[Theory]
	[InlineData("image/png", false)]
	[InlineData("image/png", true)]
	[InlineData("application/pdf", false)]
	[InlineData("application/pdf", true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithBinaryContentAndText_ConvertsOrIgnoresContent(string mediaType, bool streaming)
	{
		var base64 = mediaType == "image/png"
			? "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAAEnQAABJ0Ad5mH3gAAACJSURBVHhe5cgxAQAADMOg+je9CYgEDh623dkSmoQmoUloEpqEJqFJaBKahCahSWgSmoQmoUloEpqEJqFJaBKahCahSWgSmoQmoUloEpqEJqFJaBKahCahSWgSmoQmoUloEpqEJqFJaBKahCahSWgSmoQmoUloEpqEJqFJaBKahCahSWgSmoQmYXlhqOHSNEsP9wAAAABJRU5ErkJggg=="
			: "JVBERi0xLjQKMSAwIG9iago8PCAvVHlwZSAvQ2F0YWxvZyAvUGFnZXMgMiAwIFIgPj4KZW5kb2JqCjIgMCBvYmoKPDwgL1R5cGUgL1BhZ2VzIC9LaWRzIFszIDAgUl0gL0NvdW50IDEgPj4KZW5kb2JqCjMgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCAxMDAgMTAwXSAvQ29udGVudHMgNCAwIFIgL1Jlc291cmNlcyA8PCAvRm9udCA8PCAvRjEgNSAwIFIgPj4gPj4gPj4KZW5kb2JqCjQgMCBvYmoKPDwgL0xlbmd0aCAzNSA+PgpzdHJlYW0KQlQgL0YxIDEyIFRmIDEwIDUwIFRkIChCTFVFKSBUaiBFVAplbmRzdHJlYW0KZW5kb2JqCjUgMCBvYmoKPDwgL1R5cGUgL0ZvbnQgL1N1YnR5cGUgL1R5cGUxIC9CYXNlRm9udCAvSGVsdmV0aWNhID4+CmVuZG9iagp4cmVmCjAgNgowMDAwMDAwMDAwIDY1NTM1IGYgCjAwMDAwMDAwMDkgMDAwMDAgbiAKMDAwMDAwMDA1OCAwMDAwMCBuIAowMDAwMDAwMTE1IDAwMDAwIG4gCjAwMDAwMDAyNDEgMDAwMDAgbiAKMDAwMDAwMDMyNSAwMDAwMCBuIAp0cmFpbGVyCjw8IC9TaXplIDYgL1Jvb3QgMSAwIFIgPj4Kc3RhcnR4cmVmCjM5NQolJUVPRgo=";
		using var client = new T();
		var content = new DataContent(Convert.FromBase64String(base64), mediaType)
		{
			Name = mediaType == "image/png" ? "baseline.png" : "baseline.pdf"
		};
		var message = new ChatMessage(ChatRole.User,
			[content, new TextContent("Reply with exactly HELLO and nothing else.")]);

		var response = streaming
			? await client.GetStreamingResponseAsync([message]).ToChatResponseAsync()
			: await client.GetResponseAsync([message]);

		Assert.NotNull(response);
		Assert.Contains("HELLO", response.Text, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithFunctionHistory_PreservesUserText()
	{
		using var client = new T();
		ChatMessage[] messages =
		[
			new(ChatRole.User, "Get the weather in Paris."),
			new(ChatRole.Assistant, [new FunctionCallContent("call_baseline", "GetWeather",
				new Dictionary<string, object?> { ["city"] = "Paris" })]),
			new(ChatRole.Tool, [new FunctionResultContent("call_baseline", "Sunny")]),
			new(ChatRole.User, "Reply with exactly HELLO and nothing else.")
		];

		var response = await client.GetResponseAsync(messages);

		Assert.NotNull(response);
		Assert.Contains("HELLO", response.Text, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithFunctionHistory_RetainsToolResultContext()
	{
		using var client = new T();
		ChatMessage[] messages =
		[
			new(ChatRole.User, "Get the weather."),
			new(ChatRole.Assistant, [new FunctionCallContent("call_context", "GetWeather",
				new Dictionary<string, object?> { ["city"] = "Paris" })]),
			new(ChatRole.Tool, [new FunctionResultContent("call_context",
				new Dictionary<string, object?> { ["city"] = "Paris", ["temperatureCelsius"] = 23 })]),
			new(ChatRole.User, "What city and temperature did the tool return? Answer briefly.")
		];

		var response = await client.GetResponseAsync(messages);

		Assert.NotNull(response);
		Assert.Contains("Paris", response.Text, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("23", response.Text, StringComparison.Ordinal);
	}

	protected sealed class UnsupportedContentForTesting : AIContent;
}
