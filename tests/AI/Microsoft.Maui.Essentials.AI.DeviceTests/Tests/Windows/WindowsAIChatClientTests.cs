#if WINDOWS
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class WindowsAIChatClientCancellationTests : ChatClientCancellationTestsBase<WindowsAIChatClient>
{
}
public class WindowsAIChatClientGetServiceTests : ChatClientGetServiceTestsBase<WindowsAIChatClient>
{
	protected override string ExpectedProviderName => "windows";
	protected override string ExpectedDefaultModelId => "windows-ai-language-model";
}
public class WindowsAIChatClientInstantiationTests : ChatClientInstantiationTestsBase<WindowsAIChatClient>
{
}
public class WindowsAIChatClientMessagesTests : ChatClientMessagesTestsBase<WindowsAIChatClient>
{
}
public class WindowsAIChatClientOptionsTests : ChatClientOptionsTestsBase<WindowsAIChatClient>
{
}
public class WindowsAIChatClientResponseTests : ChatClientResponseTestsBase<WindowsAIChatClient>
{
}
public class WindowsAIChatClientStreamingTests : ChatClientStreamingTestsBase<WindowsAIChatClient>
{
}
// Structured output is handled natively by WindowsAIChatClient via
// LanguageModel.GenerateStructuredJsonResponseAsync, so no wrapper is needed.
public class WindowsAIChatClientJsonSchemaTests : ChatClientJsonSchemaTestsBase<WindowsAIChatClient>
{
	[Fact]
	public void GetConstraintSchema_OptionalNestedProperties_RequiresAllNativeFields()
	{
		using var source = JsonDocument.Parse("""
			{
			  "type": "object",
			  "properties": {
			    "summary": { "type": "string" },
			    "details": {
			      "type": "object",
			      "properties": {
			        "category": { "type": "string" },
			        "items": {
			          "type": "array",
			          "items": {
			            "type": "object",
			            "properties": {
			              "name": { "type": "string" },
			              "score": { "type": "integer" }
			            }
			          }
			        }
			      }
			    }
			  },
			  "required": ["summary"]
			}
			""");
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema(source.RootElement)
		};

		var nativeSchema = WindowsAIChatClient.GetConstraintSchema(options);

		Assert.NotNull(nativeSchema);
		using var transformed = JsonDocument.Parse(nativeSchema);
		var root = transformed.RootElement;
		AssertStrictObject(root, "summary", "details");
		var details = root.GetProperty("properties").GetProperty("details");
		AssertStrictObject(details, "category", "items");
		var item = details.GetProperty("properties").GetProperty("items").GetProperty("items");
		AssertStrictObject(item, "name", "score");
		Assert.Equal(["summary"], source.RootElement.GetProperty("required").EnumerateArray()
			.Select(value => value.GetString()));
	}

	[Fact]
	public void GetConstraintSchema_WithoutSchema_UsesOrdinaryGeneration()
	{
		Assert.Null(WindowsAIChatClient.GetConstraintSchema(null));
		Assert.Null(WindowsAIChatClient.GetConstraintSchema(new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		}));
	}

	private static void AssertStrictObject(JsonElement schema, params string[] properties)
	{
		Assert.Equal(properties, schema.GetProperty("required").EnumerateArray()
			.Select(value => value.GetString()));
		Assert.Equal(properties, schema.GetProperty("properties").EnumerateObject()
			.Select(value => value.Name));
		Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
	}
}
public class WindowsAIChatClientValidationTests
{
	[Fact]
	public async Task GetResponseAsync_WithNonAIFunctionTool_ThrowsNotSupportedException()
	{
		var client = new WindowsAIChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Hello")
		};
		var options = new ChatOptions
		{
			Tools = [new UnsupportedToolForTesting()]
		};

		await Assert.ThrowsAsync<NotSupportedException>(
			() => client.GetResponseAsync(messages, options));
	}

	[Fact]
	public async Task GetResponseAsync_WithAIFunctionTool_ThrowsInsteadOfIgnoringIt()
	{
		using var client = new WindowsAIChatClient();
		var options = new ChatOptions
		{
			Tools = [AIFunctionFactory.Create(() => "unexpected", name: "get_time")],
		};

		var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
			client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options));

		Assert.Contains("does not support tool calling", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task GetResponseAsync_WithToolMode_ThrowsInsteadOfIgnoringIt()
	{
		using var client = new WindowsAIChatClient();
		var options = new ChatOptions { ToolMode = ChatToolMode.RequireAny };

		await Assert.ThrowsAsync<NotSupportedException>(() =>
			client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options));
	}

	[Fact]
	public async Task GetResponseAsync_WithImage_RejectsWithoutDescribing()
	{
		using var client = new WindowsAIChatClient();
		var message = new ChatMessage(ChatRole.User,
			[new DataContent(new byte[] { 0 }, "image/png"), new TextContent("What is shown?")]);

		var error = await Assert.ThrowsAsync<NotSupportedException>(
			() => client.GetResponseAsync([message]));

		Assert.Contains("text only", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task GetResponseAsync_WithOnlyNullTextContent_DoesNotThrow()
	{
		var client = new WindowsAIChatClient();
		var msg = new ChatMessage(ChatRole.User, [new TextContent(null)]);
		var messages = new List<ChatMessage> { msg };

		var response = await client.GetResponseAsync(messages);
		Assert.NotNull(response);
	}

	[Fact]
	public async Task GetResponseAsync_WithUnsupportedContentType_ThrowsArgumentException()
	{
		var client = new WindowsAIChatClient();
		var msg = new ChatMessage(ChatRole.User, [new UnsupportedContentForTesting()]);
		var messages = new List<ChatMessage> { msg };

		await Assert.ThrowsAsync<ArgumentException>(
			() => client.GetResponseAsync(messages));
	}

	[Fact]
	public async Task GetResponseAsync_WithOrphanedFunctionResult_DoesNotThrow()
	{
		var client = new WindowsAIChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "What's the weather?"),
			new(ChatRole.Assistant, [new FunctionCallContent("call-1", "GetWeather")]),
			new(ChatRole.Tool, [new FunctionResultContent("call-1", "Sunny")]),
			new(ChatRole.Tool, [new FunctionResultContent("call-999", "Unknown result")]),
			new(ChatRole.User, "Tell me more")
		};

		var response = await client.GetResponseAsync(messages);
		Assert.NotNull(response);
	}

	[Fact]
	public async Task GetResponseAsync_WithFunctionCallEmptyName_DoesNotThrow()
	{
		var client = new WindowsAIChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "What's the weather?"),
			new(ChatRole.Assistant, [new FunctionCallContent("call-1", "")]),
			new(ChatRole.Tool, [new FunctionResultContent("call-1", "Sunny")]),
			new(ChatRole.User, "Tell me more")
		};

		var response = await client.GetResponseAsync(messages);
		Assert.NotNull(response);
	}

	[Fact]
	public async Task GetResponseAsync_WithInstructions_Succeeds()
	{
		var client = new WindowsAIChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Hello")
		};
		var options = new ChatOptions
		{
			Instructions = "You are a helpful assistant."
		};

		var response = await client.GetResponseAsync(messages, options);
		Assert.NotNull(response);
		Assert.NotEmpty(response.Messages);
	}

	[Fact]
	public void GetService_WithNullServiceType_ThrowsArgumentNullException()
	{
		var client = new WindowsAIChatClient();

		Assert.Throws<ArgumentNullException>(() =>
			((IChatClient)client).GetService(null!, null));
	}

	private sealed class UnsupportedToolForTesting : AITool;

	private sealed class UnsupportedContentForTesting : AIContent;
}

#endif
