#if WINDOWS
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
using Microsoft.Windows.AI.Text;
using Xunit;
using Xunit.Abstractions;

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
	// Windows LanguageModel takes one instruction string; exact hoisting order and history identity are adapter details.
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("OPTIONS")]
	public void NormalizeChatMessages_WithInterleavedSystems_CollectsInstructionsInOrder(string? instructions)
	{
		var user = new ChatMessage(ChatRole.User, "Hello");
		var assistant = new ChatMessage(ChatRole.Assistant, "Hi");
		var tool = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "Done")]);
		ChatMessage[] messages =
		[
			new(ChatRole.System, "FIRST"),
			user,
			new(ChatRole.System, "SECOND"),
			assistant,
			new(ChatRole.System, "LAST"),
			tool
		];

		var (systemPrompt, history) = WindowsAIChatClient.NormalizeChatMessages(
			messages, new ChatOptions { Instructions = instructions });

		var expected = string.IsNullOrEmpty(instructions)
			? new[] { "FIRST", "SECOND", "LAST" }
			: new[] { instructions, "FIRST", "SECOND", "LAST" };
		Assert.Equal(string.Join(Environment.NewLine + Environment.NewLine, expected), systemPrompt);
		Assert.Collection(history,
			message => Assert.Same(user, message),
			message => Assert.Same(assistant, message),
			message => Assert.Same(tool, message));
		Assert.Equal(6, messages.Length);
		Assert.Equal(ChatRole.System, messages[0].Role);
	}

	// Windows normalization drops whitespace-only system text rather than passing an empty native instruction.
	[Fact]
	public void NormalizeChatMessages_WithBlankInstructions_DoesNotCreateSystemPrompt()
	{
		var user = new ChatMessage(ChatRole.User, "Hello");

		var (systemPrompt, history) = WindowsAIChatClient.NormalizeChatMessages(
			[new(ChatRole.System, " \t"), user], new ChatOptions { Instructions = "\r\n" });

		Assert.Empty(systemPrompt);
		Assert.Same(user, Assert.Single(history));
	}
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

public class WindowsAIChatClientUsageTests(ITestOutputHelper output) : ChatClientUsageTestsBase<WindowsAIChatClient>(output)
{
	protected override bool IsUsageAvailable => false;
}

// Structured output is handled natively by WindowsAIChatClient via
// LanguageModel.GenerateStructuredJsonResponseAsync, so no wrapper is needed.
public class WindowsAIChatClientJsonSchemaTests : ChatClientJsonSchemaTestsBase<WindowsAIChatClient>
{
	// Windows LanguageModel constraints require recursive strictification; this inspects the native schema, not public JSON output.
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

	// GetConstraintSchema selects Windows ordinary versus constrained generation; other providers use different dispatch paths.
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
	// Native Windows status codes must not turn policy failures or partial responses into successful chat completions.
	[Theory]
	[InlineData(LanguageModelResponseStatus.Error)]
	[InlineData(LanguageModelResponseStatus.BlockedByPolicy)]
	[InlineData(LanguageModelResponseStatus.PromptBlockedByContentModeration)]
	[InlineData(LanguageModelResponseStatus.ResponseBlockedByContentModeration)]
	[InlineData(LanguageModelResponseStatus.UnsupportedLanguage)]
	[InlineData(LanguageModelResponseStatus.LanguageMismatch)]
	[InlineData((LanguageModelResponseStatus)int.MaxValue)]
	public void GetResponseText_NativeFailure_PreservesStatusAndCause(LanguageModelResponseStatus status)
	{
		var cause = new InvalidOperationException("Native failure");
		var error = Assert.Throws<InvalidOperationException>(() =>
			WindowsAIChatClient.GetResponseText(status, "Partial response", cause, isEmptyPrompt: false));

		Assert.Contains(status.ToString(), error.Message, StringComparison.Ordinal);
		Assert.Same(cause, error.InnerException);
	}

	// E_INVALIDARG is a Windows empty-prompt quirk, not a general permission to ignore native errors.
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void GetResponseText_EmptyPrompt_OnlyToleratesInvalidArgument(bool hasExtendedError)
	{
		Assert.Equal("", WindowsAIChatClient.GetResponseText(
			LanguageModelResponseStatus.Error, "", hasExtendedError ? new ArgumentException() : null, isEmptyPrompt: true));

		Assert.Throws<InvalidOperationException>(() => WindowsAIChatClient.GetResponseText(
			LanguageModelResponseStatus.Error, "", new ArgumentException(), isEmptyPrompt: false));
		Assert.Throws<InvalidOperationException>(() => WindowsAIChatClient.GetResponseText(
			LanguageModelResponseStatus.BlockedByPolicy, "", null, isEmptyPrompt: true));
		Assert.Throws<InvalidOperationException>(() => WindowsAIChatClient.GetResponseText(
			LanguageModelResponseStatus.Error, "", new InvalidOperationException(), isEmptyPrompt: true));
	}

	// Windows constrained generation has distinct native status codes and must preserve failure details too.
	[Theory]
	[InlineData(GenerateStructuredJsonResponseStatus.Error)]
	[InlineData((GenerateStructuredJsonResponseStatus)int.MaxValue)]
	public void GetStructuredResponseText_NativeFailure_PreservesStatusAndCause(GenerateStructuredJsonResponseStatus status)
	{
		var cause = new InvalidOperationException("Native failure");
		var error = Assert.Throws<InvalidOperationException>(() =>
			WindowsAIChatClient.GetStructuredResponseText(status, "Partial JSON", cause));

		Assert.Contains(status.ToString(), error.Message, StringComparison.Ordinal);
		Assert.Same(cause, error.InnerException);
	}

	// Context overflow is a Windows native result, not a managed cancellation or successful truncated response.
	[Fact]
	public void ResponseStatus_ContextOverflow_ExplainsLimit()
	{
		var cause = new InvalidOperationException("Native overflow");
		var textError = Assert.Throws<InvalidOperationException>(() => WindowsAIChatClient.GetResponseText(
			LanguageModelResponseStatus.PromptLargerThanContext, "", cause, isEmptyPrompt: false));
		var jsonError = Assert.Throws<InvalidOperationException>(() => WindowsAIChatClient.GetStructuredResponseText(
			GenerateStructuredJsonResponseStatus.PromptLargerThanContext, "", cause));

		Assert.Contains("context window", textError.Message, StringComparison.Ordinal);
		Assert.Contains("context window", jsonError.Message, StringComparison.Ordinal);
		Assert.Same(cause, textError.InnerException);
		Assert.Same(cause, jsonError.InnerException);
	}

	// Success statuses must pass native text through without duplicating or rewriting it.
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("Hello")]
	public void ResponseStatus_Complete_PreservesText(string? text)
	{
		Assert.Equal(text, WindowsAIChatClient.GetResponseText(
			LanguageModelResponseStatus.Complete, text, null, isEmptyPrompt: false));
		Assert.Equal(text, WindowsAIChatClient.GetStructuredResponseText(
			GenerateStructuredJsonResponseStatus.Complete, text, null));
	}

	// Windows LanguageModel rejects every tool request; tool-capable providers need not reject this same way.
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

	// Windows has no tool execution API and must reject even valid AIFunctions, unlike Apple and cloud clients.
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

	// Windows LanguageModel cannot honor ToolMode even without a tool list; this is not a shared tool-capable contract.
	[Fact]
	public async Task GetResponseAsync_WithToolMode_ThrowsInsteadOfIgnoringIt()
	{
		using var client = new WindowsAIChatClient();
		var options = new ChatOptions { ToolMode = ChatToolMode.RequireAny };

		await Assert.ThrowsAsync<NotSupportedException>(() =>
			client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options));
	}

	// Windows replays tool history as named pretty-JSON text, not native tool messages; exact formatting is adapter-specific.
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void ConvertToPrompt_WithToolHistory_FormatsNameAndJson(bool functionCall)
	{
		using var result = JsonDocument.Parse("""{"temperature":23}""");
		AIContent historyContent = functionCall
			? new FunctionCallContent("call-1", "GetWeather", new Dictionary<string, object?> { ["city"] = "Paris" })
			: new FunctionResultContent("call-1", result.RootElement);
		var messages = new ChatMessage[]
		{
			new(functionCall ? ChatRole.Assistant : ChatRole.Tool, [historyContent]),
			new(ChatRole.User, [new DataContent(new byte[] { 0 }, "image/png"), new TextContent("Hello")]),
		};

		var separator = Environment.NewLine + Environment.NewLine;
		var argumentsJson = string.Join(Environment.NewLine, "{", "  \"city\": \"Paris\"", "}");
		var resultJson = string.Join(Environment.NewLine, "{", "  \"temperature\": 23", "}");
		var expected = functionCall
			? string.Join(separator, "Tool call:", "GetWeather", argumentsJson, "User: Hello")
			: string.Join(separator, "Tool result:", "call-1", resultJson, "User: Hello");

		Assert.Equal(expected, WindowsAIChatClient.ConvertToPrompt(messages));
	}

	// Windows's text replay resolves result call IDs to function names; native/cloud tool envelopes do not use this format.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ConvertToPrompt_WithMatchedToolResult_UsesFunctionName(bool dictionaryResult)
	{
		using var result = JsonDocument.Parse("""{"temperature":23}""");
		object value = dictionaryResult
			? new Dictionary<string, object?> { ["temperature"] = 23 }
			: result.RootElement;
		ChatMessage[] messages =
		[
			new(ChatRole.Assistant, [new FunctionCallContent("call-1", "GetWeather",
				new Dictionary<string, object?> { ["city"] = "Paris" })]),
			new(ChatRole.Tool, [new FunctionResultContent("call-1", value)])
		];
		var separator = Environment.NewLine + Environment.NewLine;
		var argumentsJson = string.Join(Environment.NewLine, "{", "  \"city\": \"Paris\"", "}");
		var resultJson = string.Join(Environment.NewLine, "{", "  \"temperature\": 23", "}");

		Assert.Equal(
			string.Join(separator, "Tool call:", "GetWeather", argumentsJson,
				"Tool result:", "GetWeather", resultJson),
			WindowsAIChatClient.ConvertToPrompt(messages));
	}

	// Windows's text replay JSON-encodes scalar results and labels unmatched IDs; cloud history may reject unmatched results.
	[Theory]
	[InlineData(null, "null")]
	[InlineData("Sunny", "\"Sunny\"")]
	public void ConvertToPrompt_WithScalarToolResult_UsesJson(string? result, string json)
	{
		var message = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", result)]);

		Assert.Equal(
			string.Join(Environment.NewLine + Environment.NewLine, "Tool result:", "call-1", json),
			WindowsAIChatClient.ConvertToPrompt([message]));
	}

	// Windows treats orphaned results as text context; cloud tool protocols can reject this malformed history.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
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

	// Windows tolerates empty function names in text replay; native/cloud tool protocols need not accept them.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
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

	private sealed class UnsupportedToolForTesting : AITool;
}

#endif
