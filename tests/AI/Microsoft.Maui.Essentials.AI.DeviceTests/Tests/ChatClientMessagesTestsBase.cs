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
	protected virtual bool RejectsUnsupportedMedia => true;

	protected virtual bool SupportsBinaryMedia(string mediaType) => false;

	protected abstract bool RequiresPrompt { get; }

	protected virtual void AssertMissingPromptError(ArgumentException error) { }

	// Opt in to a rejection contract, not an assertion that every other content/role combination is supported.
	protected abstract bool RejectsUnsupportedStandardContent(string kind, ChatRole role);

	[Theory]
	[InlineData("system", false)]
	[InlineData("system", true)]
	[InlineData("instructions", false)]
	[InlineData("instructions", true)]
	public async Task InstructionsOnly_WhenPromptRequired_Rejects(string scenario, bool streaming)
	{
		if (!RequiresPrompt)
			return;

		using var client = new T();
		ChatMessage[] messages = scenario == "system"
			? [new(ChatRole.System, "Instructions only")]
			: [];
		var options = new ChatOptions { Instructions = "Follow these instructions." };

		var error = await Assert.ThrowsAsync<ArgumentException>(async () =>
		{
			if (streaming)
				await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync();
			else
				await client.GetResponseAsync(messages, options);
		});
		AssertMissingPromptError(error);
	}

	[Fact]
	public virtual async Task GetResponseAsync_WithEmptyMessages_ThrowsException()
	{
		var client = new T();
		var messages = new List<ChatMessage>();

		await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync(messages));
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithOnlyNullTextContent_PreservesFollowingUserTurn()
	{
		using var client = new T();
		var msg = new ChatMessage(ChatRole.User, [new TextContent(null)]);
		// Keep inference meaningful; a model's policy for a completely empty prompt is not a conversion contract.
		var messages = new List<ChatMessage> { msg, new(ChatRole.User, "Reply with exactly HELLO.") };

		var response = await client.GetResponseAsync(messages);
		Assert.NotNull(response);
		Assert.Contains("HELLO", response.Text, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithUnknownContentAlongsideText_OmitsOnlyUnknownItems(bool streaming)
	{
		using var client = new T();
		var message = new ChatMessage(ChatRole.User,
			[new UnsupportedContentForTesting(), new TextContent("Reply with only the word HELLO."),
				new UnsupportedContentForTesting()]);

		var response = streaming
			? await client.GetStreamingResponseAsync([message]).ToChatResponseAsync()
			: await client.GetResponseAsync([message]);

		Assert.NotNull(response);
		Assert.NotEmpty(response.Messages);
		Assert.Contains("HELLO", response.Text, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithOnlyUnknownContent_PreservesFollowingUserTurn(bool streaming)
	{
		using var client = new T();
		var msg = new ChatMessage(ChatRole.User, [new UnsupportedContentForTesting()]);
		var messages = new List<ChatMessage> { msg, new(ChatRole.User, "Reply with exactly HELLO.") };

		var response = streaming
			? await client.GetStreamingResponseAsync(messages).ToChatResponseAsync()
			: await client.GetResponseAsync(messages);

		Assert.NotNull(response);
		Assert.NotNull(response.Messages);
		Assert.Contains("HELLO", response.Text, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithBlankConversationTurns_PreservesFollowingUserTurn(bool unknownOnly, bool streaming)
	{
		using var client = new T();
		ChatMessage[] messages =
		[
			new(ChatRole.System, ""),
			new(ChatRole.User, unknownOnly ? [new UnsupportedContentForTesting()] : []),
			new(ChatRole.Assistant, unknownOnly ? [new UnsupportedContentForTesting()] : []),
			new(ChatRole.User, "Reply with exactly HELLO.")
		];
		var options = new ChatOptions { Instructions = " \r\n\t" };
		var response = streaming
			? await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync()
			: await client.GetResponseAsync(messages, options);
		Assert.Contains("HELLO", response.Text, StringComparison.OrdinalIgnoreCase);
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
	[InlineData("application/pdf", false)]
	[InlineData("text/plain", true)]
	[InlineData("application/pdf", true)]
	[InlineData("audio/ogg", true)]
	[InlineData("application/octet-stream", true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public Task GetResponseAsync_WithKnownContentAndUnsupportedMedia_AppliesProviderPolicy(string mediaType, bool useUri)
		=> VerifyUnsupportedMediaPolicyAsync(mediaType, useUri);

	[Theory]
	[InlineData("application/pdf", false)]
	[InlineData("application/pdf", true)]
	[InlineData("audio/ogg", false)]
	[InlineData("audio/ogg", true)]
	[InlineData("text/plain", false)]
	[InlineData("text/plain", true)]
	[InlineData("application/octet-stream", false)]
	[InlineData("application/octet-stream", true)]
	public async Task UnsupportedMedia_RejectsInsteadOfOmitting(string mediaType, bool useUri)
	{
		if (!RejectsUnsupportedMedia)
			return;

		await VerifyUnsupportedMediaPolicyAsync(mediaType, useUri);
	}

	private async Task VerifyUnsupportedMediaPolicyAsync(string mediaType, bool useUri)
	{
		// Supported binary media use valid payloads in the dedicated binary-content vectors.
		if (!useUri && SupportsBinaryMedia(mediaType))
			return;

		using var client = new T();
		AIContent content = useUri
			? new UriContent("https://example.invalid/baseline.txt", mediaType)
			: new DataContent("Unrecognized baseline payload"u8.ToArray(), mediaType);
		foreach (var role in new[] { ChatRole.System, ChatRole.User, ChatRole.Assistant })
		{
			ChatMessage[] messages =
			[
				new(role, [new TextContent("before"), content, new TextContent("after")]),
				new(ChatRole.User, "Reply with exactly HELLO and nothing else.")
			];
			foreach (var streaming in new[] { false, true })
			{
				if (RejectsUnsupportedMedia)
				{
					var error = await Assert.ThrowsAsync<NotSupportedException>(async () =>
					{
						if (streaming)
							await client.GetStreamingResponseAsync(messages).ToChatResponseAsync();
						else
							await client.GetResponseAsync(messages);
					});
					Assert.Contains(mediaType, error.Message, StringComparison.Ordinal);
				}
				else
				{
					var response = streaming
						? await client.GetStreamingResponseAsync(messages).ToChatResponseAsync()
						: await client.GetResponseAsync(messages);
					Assert.NotNull(response);
					Assert.Contains("HELLO", response.Text, StringComparison.OrdinalIgnoreCase);
				}
			}
		}
	}

	[Theory]
	[InlineData("hosted-file")]
	[InlineData("hosted-vector-store")]
	[InlineData("reasoning")]
	[InlineData("usage")]
	[InlineData("error")]
	[InlineData("code-call")]
	[InlineData("code-result")]
	[InlineData("image-call")]
	[InlineData("image-result")]
	[InlineData("web-call")]
	[InlineData("web-result")]
	[InlineData("mcp-call")]
	[InlineData("mcp-result")]
	[InlineData("approval-request")]
	[InlineData("approval-response")]
	public async Task UnsupportedStandardContent_RejectsInEveryRoleBeforeInference(string kind)
	{
#pragma warning disable MEAI001 // Standard MEAI content remains recognized even when its class is experimental.
		AIContent content = kind switch
		{
			"hosted-file" => new HostedFileContent("file-1"),
			"hosted-vector-store" => new HostedVectorStoreContent("store-1"),
			"reasoning" => new TextReasoningContent("reasoning"),
			"usage" => new UsageContent(),
			"error" => new ErrorContent("error"),
			"code-call" => new CodeInterpreterToolCallContent("call-1"),
			"code-result" => new CodeInterpreterToolResultContent("call-1"),
			"image-call" => new ImageGenerationToolCallContent("call-1"),
			"image-result" => new ImageGenerationToolResultContent("call-1"),
			"web-call" => new WebSearchToolCallContent("call-1"),
			"web-result" => new WebSearchToolResultContent("call-1"),
			"mcp-call" => new McpServerToolCallContent("call-1", "server", "tool"),
			"mcp-result" => new McpServerToolResultContent("call-1"),
			"approval-request" => new ToolApprovalRequestContent("request-1", new FunctionCallContent("call-1", "sample")),
			_ => new ToolApprovalResponseContent("request-1", true, new FunctionCallContent("call-1", "sample"))
		};
#pragma warning restore MEAI001
		using var client = new T();
		foreach (var role in new[] { ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.Tool })
		{
			if (!RejectsUnsupportedStandardContent(kind, role))
				continue;

			ChatMessage[] messages = [new(role, [new UnsupportedContentForTesting(), content]),
				new(ChatRole.User, "Hello")];
			var error = await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync(messages));
			Assert.Contains(content.GetType().Name, error.Message, StringComparison.Ordinal);
			error = await Assert.ThrowsAsync<NotSupportedException>(() =>
				client.GetStreamingResponseAsync(messages).ToChatResponseAsync());
			Assert.Contains(content.GetType().Name, error.Message, StringComparison.Ordinal);
		}
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
	public virtual async Task GetResponseAsync_WithTrailingSystemMessage_AppliesInstruction()
	{
		using var client = new T();
		ChatMessage[] messages =
		[
			new(ChatRole.User, "Remember the word PINEAPPLE."),
			new(ChatRole.System, "Reply with exactly TRAILING_SYSTEM.")
		];

		var response = await client.GetResponseAsync(messages);

		Assert.NotNull(response);
		Assert.Contains(response.Messages, message => message.Role == ChatRole.Assistant);
		Assert.Equal("TRAILING_SYSTEM", response.Text.Trim());
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
	public virtual async Task GetResponseAsync_WithBinaryContentAndText_AppliesProviderPolicy(string mediaType, bool streaming)
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

		if (mediaType != "image/png" && RejectsUnsupportedMedia)
		{
			await Assert.ThrowsAsync<NotSupportedException>(async () =>
			{
				if (streaming)
					await client.GetStreamingResponseAsync([message]).ToChatResponseAsync();
				else
					await client.GetResponseAsync([message]);
			});
			return;
		}

		var response = streaming
			? await client.GetStreamingResponseAsync([message]).ToChatResponseAsync()
			: await client.GetResponseAsync([message]);

		Assert.NotNull(response);
		Assert.Contains("HELLO", response.Text, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithFunctionHistory_PreservesUserText(bool streaming)
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

		var response = streaming
			? await client.GetStreamingResponseAsync(messages).ToChatResponseAsync()
			: await client.GetResponseAsync(messages);

		Assert.NotNull(response);
		Assert.Contains("HELLO", response.Text, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithFunctionHistory_RetainsToolResultContext(bool streaming)
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

		var response = streaming
			? await client.GetStreamingResponseAsync(messages).ToChatResponseAsync()
			: await client.GetResponseAsync(messages);

		Assert.NotNull(response);
		Assert.Contains("Paris", response.Text, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("23", response.Text, StringComparison.Ordinal);
	}

	protected sealed class UnsupportedContentForTesting : AIContent;
}
