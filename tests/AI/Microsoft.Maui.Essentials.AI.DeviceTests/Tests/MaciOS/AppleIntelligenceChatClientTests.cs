#if IOS || MACCATALYST
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class AppleIntelligenceChatClientCancellationTests : ChatClientCancellationTestsBase<AppleIntelligenceChatClient>
{
	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_AcceptsCancellationToken()
		=> base.GetResponseAsync_AcceptsCancellationToken();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_CancelAfterStart_ThrowsOperationCanceledException()
		=> base.GetResponseAsync_CancelAfterStart_ThrowsOperationCanceledException();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_WithTimeout_ThrowsOperationCanceledException()
		=> base.GetResponseAsync_WithTimeout_ThrowsOperationCanceledException();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_AcceptsCancellationToken()
		=> base.GetStreamingResponseAsync_AcceptsCancellationToken();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_CancelDuringStreaming_ThrowsOperationCanceledException()
		=> base.GetStreamingResponseAsync_CancelDuringStreaming_ThrowsOperationCanceledException();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_WithTimeout_ThrowsOperationCanceledException()
		=> base.GetStreamingResponseAsync_WithTimeout_ThrowsOperationCanceledException();
}

public class AppleIntelligenceChatClientFunctionCallingTestsBase : ChatClientFunctionCallingTestsBase<AppleIntelligenceChatClient>
{
	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_CallsFunctionAndReturnsResult()
		=> base.GetResponseAsync_CallsFunctionAndReturnsResult();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_HandlesMultipleFunctionCalls()
		=> base.GetResponseAsync_HandlesMultipleFunctionCalls();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_CallsFunctionAndStreamsUpdates()
		=> base.GetStreamingResponseAsync_CallsFunctionAndStreamsUpdates();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_StreamsToolCallContent()
		=> base.GetStreamingResponseAsync_StreamsToolCallContent();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_StreamsToolResultContent()
		=> base.GetStreamingResponseAsync_StreamsToolResultContent();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_ToolResultHasCorrectRole()
		=> base.GetStreamingResponseAsync_ToolResultHasCorrectRole();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_StreamsToolCallBeforeToolResult()
		=> base.GetStreamingResponseAsync_StreamsToolCallBeforeToolResult();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_HandlesMultipleFunctionCalls()
		=> base.GetStreamingResponseAsync_HandlesMultipleFunctionCalls();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_FunctionWithComplexParameters()
		=> base.GetResponseAsync_FunctionWithComplexParameters();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_FunctionWithComplexParameters()
		=> base.GetStreamingResponseAsync_FunctionWithComplexParameters();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_ChainedFunctionCalls_TimeAndWeather()
		=> base.GetResponseAsync_ChainedFunctionCalls_TimeAndWeather();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_ChainedFunctionCalls_TimeAndWeather()
		=> base.GetStreamingResponseAsync_ChainedFunctionCalls_TimeAndWeather();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_FunctionWithEnumParameter_CallsToolCorrectly()
		=> base.GetResponseAsync_FunctionWithEnumParameter_CallsToolCorrectly();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_FunctionWithEnumParameter_CallsToolCorrectly()
		=> base.GetStreamingResponseAsync_FunctionWithEnumParameter_CallsToolCorrectly();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_MultiTurnConversationWithToolCalling_SucceedsOnFollowUp()
		=> base.GetResponseAsync_MultiTurnConversationWithToolCalling_SucceedsOnFollowUp();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_MultiTurnConversationWithToolCalling_SucceedsOnFollowUp()
		=> base.GetStreamingResponseAsync_MultiTurnConversationWithToolCalling_SucceedsOnFollowUp();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_MultiTurnWithToolCalling_HistoryBuiltFromStreamedContent_SucceedsOnFollowUp()
		=> base.GetStreamingResponseAsync_MultiTurnWithToolCalling_HistoryBuiltFromStreamedContent_SucceedsOnFollowUp();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_MultiTurnWithToolCalling_HistoryBuiltFromStreamedContent_ToolResultsPreservedInContext()
		=> base.GetStreamingResponseAsync_MultiTurnWithToolCalling_HistoryBuiltFromStreamedContent_ToolResultsPreservedInContext();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_MultiTurnConversationWithToolCalling_ToolResultsPreservedInContext()
		=> base.GetResponseAsync_MultiTurnConversationWithToolCalling_ToolResultsPreservedInContext();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_WithToolCalling_NoNullTextContent()
		=> base.GetStreamingResponseAsync_WithToolCalling_NoNullTextContent();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_WithToolCalling_StreamOrderIsToolsBeforeResponse()
		=> base.GetStreamingResponseAsync_WithToolCalling_StreamOrderIsToolsBeforeResponse();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_WithToolCalling_StreamOrderPreservedThroughFICC()
		=> base.GetStreamingResponseAsync_WithToolCalling_StreamOrderPreservedThroughFICC();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_InformationalOnlyFunctionCalls_NotInvokedByFICC()
		=> base.GetStreamingResponseAsync_InformationalOnlyFunctionCalls_NotInvokedByFICC();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_MultiTurnWithToolCalling_ContentOrderPreserved()
		=> base.GetStreamingResponseAsync_MultiTurnWithToolCalling_ContentOrderPreserved();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_WithToolCalling_NoNullTextBeforeToolCalls()
		=> base.GetStreamingResponseAsync_WithToolCalling_NoNullTextBeforeToolCalls();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_ViewModelSimulation_ThinkingBubbleRemovedBeforeToolCalls()
		=> base.GetStreamingResponseAsync_ViewModelSimulation_ThinkingBubbleRemovedBeforeToolCalls();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_ViewModelSimulation_NoNullTextInStream_RawClient()
		=> base.GetStreamingResponseAsync_ViewModelSimulation_NoNullTextInStream_RawClient();
}
public class AppleIntelligenceChatClientGetServiceTests : ChatClientGetServiceTestsBase<AppleIntelligenceChatClient>
{
	protected override string ExpectedProviderName => "apple";
	protected override string ExpectedDefaultModelId => "apple-intelligence";
}
public class AppleIntelligenceChatClientInstantiationTests : ChatClientInstantiationTestsBase<AppleIntelligenceChatClient>
{
}

public class AppleIntelligenceChatClientMessagesTests : ChatClientMessagesTestsBase<AppleIntelligenceChatClient>
{
	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_WithSystemMessage_AcceptsSystemRole()
		=> base.GetResponseAsync_WithSystemMessage_AcceptsSystemRole();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_WithConversationHistory_AcceptsMultipleMessages()
		=> base.GetResponseAsync_WithConversationHistory_AcceptsMultipleMessages();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_WithLongMessage_HandlesGracefully()
		=> base.GetResponseAsync_WithLongMessage_HandlesGracefully();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_WithEmptyMessageContent_HandlesGracefully()
		=> base.GetResponseAsync_WithEmptyMessageContent_HandlesGracefully();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_WithSpecialCharacters_HandlesGracefully()
		=> base.GetResponseAsync_WithSpecialCharacters_HandlesGracefully();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_WithSystemMessage_AcceptsSystemRole()
		=> base.GetStreamingResponseAsync_WithSystemMessage_AcceptsSystemRole();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_WithConversationHistory_AcceptsMultipleMessages()
		=> base.GetStreamingResponseAsync_WithConversationHistory_AcceptsMultipleMessages();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_WithLongMessage_HandlesGracefully()
		=> base.GetStreamingResponseAsync_WithLongMessage_HandlesGracefully();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_WithEmptyMessageContent_HandlesGracefully()
		=> base.GetStreamingResponseAsync_WithEmptyMessageContent_HandlesGracefully();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_WithSpecialCharacters_HandlesGracefully()
		=> base.GetStreamingResponseAsync_WithSpecialCharacters_HandlesGracefully();
}

public class AppleIntelligenceChatClientOptionsTests : ChatClientOptionsTestsBase<AppleIntelligenceChatClient>
{
	/// <summary>
	/// Apple Intelligence requires a JSON schema for structured responses.
	/// Unlike the base test, this expects an InvalidOperationException when using ChatResponseFormat.Json without a schema.
	/// </summary>
	[Fact]
	public override async Task GetResponseAsync_WithResponseFormat_AcceptsJsonFormat()
	{
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Generate a JSON object")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		};

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => client.GetResponseAsync(messages, options));

		Assert.Contains("JSON schema", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Apple Intelligence requires a JSON schema for structured responses.
	/// Unlike the base test, this expects an InvalidOperationException when using ChatResponseFormat.Json without a schema.
	/// </summary>
	[Fact]
	public override async Task GetStreamingResponseAsync_WithResponseFormat_AcceptsJsonFormat()
	{
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Generate a JSON object")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		};

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
		{
			await foreach (var update in client.GetStreamingResponseAsync(messages, options))
			{
				// Should not reach here
			}
		});

		Assert.Contains("JSON schema", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_AcceptsNullOptions()
		=> base.GetResponseAsync_AcceptsNullOptions();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_WithChatOptions_AcceptsValidOptions()
		=> base.GetResponseAsync_WithChatOptions_AcceptsValidOptions();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_WithExtremeTemperature_HandlesGracefully()
		=> base.GetResponseAsync_WithExtremeTemperature_HandlesGracefully();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_AcceptsNullOptions()
		=> base.GetStreamingResponseAsync_AcceptsNullOptions();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_WithChatOptions_AcceptsValidOptions()
		=> base.GetStreamingResponseAsync_WithChatOptions_AcceptsValidOptions();
}
public class AppleIntelligenceChatClientResponseTests : ChatClientResponseTestsBase<AppleIntelligenceChatClient>
{
	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_ReturnsNonNullResponse()
		=> base.GetResponseAsync_ReturnsNonNullResponse();
}

public class AppleIntelligenceChatClientStreamingTests : ChatClientStreamingTestsBase<AppleIntelligenceChatClient>
{
	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_ReturnsStreamingUpdates()
		=> base.GetStreamingResponseAsync_ReturnsStreamingUpdates();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_DeliversMultipleIncrementalUpdates()
		=> base.GetStreamingResponseAsync_DeliversMultipleIncrementalUpdates();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_CanBuildCompleteResponseFromUpdates()
		=> base.GetStreamingResponseAsync_CanBuildCompleteResponseFromUpdates();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_ConcatenatedTextMatchesNonStreaming()
		=> base.GetStreamingResponseAsync_ConcatenatedTextMatchesNonStreaming();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_UpdatesHaveContents()
		=> base.GetStreamingResponseAsync_UpdatesHaveContents();
}

public class AppleIntelligenceChatClientJsonSchemaTests : ChatClientJsonSchemaTestsBase<AppleIntelligenceChatClient>
{
	[Fact(Skip = "Apple Intelligence requires a JSON schema for structured responses, so this test is not applicable.")]
	public override Task GetResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow()
	{
		return base.GetResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow();
	}

	[Fact(Skip = "Apple Intelligence requires a JSON schema for structured responses, so this test is not applicable.")]
	public override Task GetStreamingResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow()
	{
		return base.GetStreamingResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow();
	}

	[Fact]
	public async Task GetResponseAsync_WithJsonFormatWithoutSchema_ThrowsInvalidOperationException()
	{
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Generate a JSON object")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		};

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => client.GetResponseAsync(messages, options));

		Assert.Contains("JSON schema", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task GetStreamingResponseAsync_WithJsonFormatWithoutSchema_ThrowsInvalidOperationException()
	{
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Generate a JSON object")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		};

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
		{
			await foreach (var update in client.GetStreamingResponseAsync(messages, options))
			{
				// Should not reach here
			}
		});

		Assert.Contains("JSON schema", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsyncT_ReturnsStructuredResponse()
		=> base.GetResponseAsyncT_ReturnsStructuredResponse();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsyncT_WithChatOptions_ReturnsStructuredResponse()
		=> base.GetResponseAsyncT_WithChatOptions_ReturnsStructuredResponse();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsyncT_WithComplexType_ReturnsStructuredResponse()
		=> base.GetResponseAsyncT_WithComplexType_ReturnsStructuredResponse();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsyncT_WithMessageList_ReturnsStructuredResponse()
		=> base.GetResponseAsyncT_WithMessageList_ReturnsStructuredResponse();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsyncT_WithSimpleType_ReturnsDeserializedResult()
		=> base.GetResponseAsyncT_WithSimpleType_ReturnsDeserializedResult();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_WithJsonSchemaFormat_ReturnsStructuredResponse()
		=> base.GetResponseAsync_WithJsonSchemaFormat_ReturnsStructuredResponse();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_WithJsonSchemaFormat_ReturnsValidJson()
		=> base.GetResponseAsync_WithJsonSchemaFormat_ReturnsValidJson();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetStreamingResponseAsync_WithJsonSchemaFormat_StreamsValidJson()
		=> base.GetStreamingResponseAsync_WithJsonSchemaFormat_StreamsValidJson();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_WithComplexJsonSchema_ReturnsStructuredResponse()
		=> base.GetResponseAsync_WithComplexJsonSchema_ReturnsStructuredResponse();

	[Fact]
	[Trait("RequiresModel", "true")]
	public override Task GetResponseAsync_WithJsonSchemaFormatAndCustomOptions_Works()
		=> base.GetResponseAsync_WithJsonSchemaFormatAndCustomOptions_Works();

}

#endif
