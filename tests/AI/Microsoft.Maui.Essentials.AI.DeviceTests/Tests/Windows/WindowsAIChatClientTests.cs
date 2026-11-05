#if WINDOWS
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
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
	protected override bool RequiresPrompt => true;
	protected override bool RejectsUnsupportedMedia => true;
	protected override bool RejectsUnsupportedStandardContent(string kind, ChatRole role) => true;
	protected override bool RejectsMisplacedFunctionContent(ChatRole role, bool isCall) =>
		isCall ? role != ChatRole.Assistant : role != ChatRole.Tool;
	protected override bool SupportsTrailingSystemInstruction => true;

	protected override void AssertMissingPromptError(ArgumentException error) =>
		Assert.Contains("User or Assistant", error.Message, StringComparison.Ordinal);

	// Windows LanguageModel preserves instruction semantics but may normalize identifier casing/separators.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public override async Task GetResponseAsync_WithTrailingSystemMessage_AppliesInstruction()
	{
		using var client = new WindowsAIChatClient();
		ChatMessage[] messages =
		[
			new(ChatRole.User, "Remember the word PINEAPPLE."),
			new(ChatRole.System, "Reply with exactly TRAILING_SYSTEM.")
		];

		var response = await client.GetResponseAsync(messages);

		Assert.NotNull(response);
		Assert.Contains(response.Messages, message => message.Role == ChatRole.Assistant);
		var normalized = new string(response.Text.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
		Assert.Equal("TRAILINGSYSTEM", normalized);
		Assert.DoesNotContain("PINEAPPLE", response.Text, StringComparison.OrdinalIgnoreCase);
	}

	// LanguageModel has no image input API, unlike the OpenAI baseline.
	[Theory]
	[InlineData("image/png", false)]
	[InlineData("image/png", true)]
	[InlineData("application/pdf", false)]
	[InlineData("application/pdf", true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public override async Task GetResponseAsync_WithBinaryContentAndText_AppliesProviderPolicy(string mediaType, bool streaming)
	{
		using var client = new WindowsAIChatClient();
		ChatMessage[] messages = [new(ChatRole.User,
			[new DataContent(new byte[] { 0 }, mediaType), new TextContent("Hello")])];
		await Assert.ThrowsAsync<NotSupportedException>(async () =>
		{
			if (streaming)
				await client.GetStreamingResponseAsync(messages).ToChatResponseAsync();
			else
				await client.GetResponseAsync(messages);
		});
	}

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
	protected override bool RejectsStatefulConversationOptions => true;
	protected override bool RequiresExplicitJsonSchema => true;
	protected override bool SupportsReasoningEffortNone => true;

	// LanguageModelOptions has no MaxOutputTokens equivalent; even invalid values are ignored.
	[Fact]
	public override Task GetResponseAsync_WithZeroMaxTokens_ThrowsException()
	{
		WindowsAIChatClient.ValidateOptions(new ChatOptions { MaxOutputTokens = 0 });
		return Task.CompletedTask;
	}
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
	protected override bool? RequiresExplicitJsonSchema => true;

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
	public void GetConstraintSchema_WithoutSchema_RejectsJsonButAllowsText()
	{
		Assert.Null(WindowsAIChatClient.GetConstraintSchema(null));
		Assert.Null(WindowsAIChatClient.GetConstraintSchema(new ChatOptions { ResponseFormat = ChatResponseFormat.Text }));
		Assert.Throws<NotSupportedException>(() => WindowsAIChatClient.GetConstraintSchema(new ChatOptions
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
	// LanguageModel ignores optional definitions, including tools it cannot register, without executing them.
	[Fact]
	public void ValidateOptions_WithOptionalToolDefinitions_IgnoresThem()
	{
		foreach (var mode in new ChatToolMode?[] { null, ChatToolMode.Auto, ChatToolMode.None })
		{
			foreach (var multiple in new bool?[] { null, false, true })
				WindowsAIChatClient.ValidateOptions(new ChatOptions
				{
					ToolMode = mode,
					AllowMultipleToolCalls = multiple,
					Tools = [new UnsupportedToolForTesting(),
						AIFunctionFactory.Create((Func<string>)(() => throw new InvalidOperationException("Must not execute")), name: "get_time")]
				});
		}
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
			new(ChatRole.User, [new UnsupportedContentForTesting(), new TextContent("Hello")]),
		};

		var separator = Environment.NewLine + Environment.NewLine;
		var argumentsJson = string.Join(Environment.NewLine, "{", "  \"city\": \"Paris\"", "}");
		var resultJson = string.Join(Environment.NewLine, "{", "  \"temperature\": 23", "}");
		var expected = functionCall
			? string.Join(separator, "Tool call:", "Call ID: call-1", "Name: GetWeather", argumentsJson, "User: Hello")
			: string.Join(separator, "Tool result:", "Call ID: call-1", resultJson, "User: Hello");

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
			string.Join(separator, "Tool call:", "Call ID: call-1", "Name: GetWeather", argumentsJson,
				"Tool result:", "Call ID: call-1", "Name: GetWeather", resultJson),
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
			string.Join(Environment.NewLine + Environment.NewLine, "Tool result:", "Call ID: call-1", json),
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

	// Windows validates recognized history before native model acquisition.
	[Fact]
	public async Task GetResponseAsync_WithFunctionCallEmptyName_Rejects()
	{
		var client = new WindowsAIChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "What's the weather?"),
			new(ChatRole.Assistant, [new FunctionCallContent("call-1", "")]),
			new(ChatRole.Tool, [new FunctionResultContent("call-1", "Sunny")]),
			new(ChatRole.User, "Tell me more")
		};

		await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync(messages));
	}

	private sealed class UnsupportedToolForTesting : AITool;
	private sealed class UnsupportedContentForTesting : AIContent;
}

#endif
