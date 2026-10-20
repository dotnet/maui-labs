using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI.DeviceTests;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

// These scripted controls test the shared harness, not any native or cloud implementation.
public class ChatClientConformanceTests(ITestOutputHelper output)
{
	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task RequiredTools_ExerciseInvocationAndRejection(bool streaming, bool named)
	{
		await new ToolBinding().RequiredToolCallbacks_FinishOnceAndPreserveContext(streaming, named);
		await new RejectedToolBinding().RequiredToolCallbacks_FinishOnceAndPreserveContext(streaming, named);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Tools_ExerciseFormattedNullableCancellationAndActivity(bool streaming)
	{
		var binding = new ToolBinding();
		await binding.RequiredTool_WithFormattedParametersAndDictionaryReturn_InvokesExactlyOnce(streaming);
		await binding.RequiredTool_WithNullableParameters_InvokesExactlyOnce(streaming);
		await binding.ToolInvocation_CallerCancellation_PreservesCancelledTask(streaming);
		await binding.ToolCallbacks_InheritChatTraceAndSpanAcrossAwaits(streaming);
	}

	[Fact]
	public void Metadata_ExercisesSharedHooks()
	{
		var binding = new MetadataBinding();
		binding.GetService_WithNullServiceType_ThrowsArgumentNullException();
		binding.GetService_ReturnsMetadataWithCorrectProviderAndModel();
		binding.GetService_ReturnsNullForUnknownService();
		binding.GetService_ReturnsNullForKeyedServices();
		binding.GetService_ReturnsItselfForMatchingType();
		binding.GetService_ReturnsItselfForIChatClientType();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Messages_ExercisePromptMediaAndContentPolicies(bool streaming)
	{
		var binding = new MessageBinding();
		await binding.InstructionsOnly_WhenPromptRequired_Rejects("system", streaming);
		await binding.InstructionsOnly_WhenPromptRequired_Rejects("instructions", streaming);
		await binding.GetResponseAsync_WithUnknownContentAlongsideText_OmitsOnlyUnknownItems(streaming);
		await binding.GetResponseAsync_WithOnlyUnknownContent_PreservesFollowingUserTurn(streaming);
		foreach (var media in new[] { "application/pdf", "audio/ogg", "text/plain", "application/octet-stream" })
			await binding.UnsupportedMedia_RejectsInsteadOfOmitting(media, streaming);
		foreach (var kind in new[] { "hosted-file", "hosted-vector-store", "reasoning", "usage", "error",
			"code-call", "code-result", "image-call", "image-result", "web-call", "web-result",
			"mcp-call", "mcp-result", "approval-request", "approval-response" })
			await binding.UnsupportedStandardContent_RejectsInEveryRoleBeforeInference(kind);
		foreach (var role in new[] { "system", "user", "tool" })
			await binding.MisplacedFunctionContent_RejectsBeforeInference(role, true);
		foreach (var role in new[] { "system", "user", "assistant" })
			await binding.MisplacedFunctionContent_RejectsBeforeInference(role, false);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Options_ExerciseStatefulAndExplicitSchemaPolicies(bool streaming)
	{
		var binding = new OptionsBinding();
		foreach (var name in new[] { "ContinuationToken", "ConversationId", "AllowBackgroundResponses" })
			await binding.UnsupportedConversationOptions_RejectBeforeInference(name, streaming);
		if (streaming)
			await binding.GetStreamingResponseAsync_WithResponseFormat_AcceptsJsonFormat();
		else
			await binding.GetResponseAsync_WithResponseFormat_AcceptsJsonFormat();
	}

	[Fact]
	public async Task Schema_UnsupportedMeansRejectionNotSuccessfulNoOp()
	{
		var binding = new SchemaBinding();
		await binding.GetResponseAsync_WithJsonSchemaFormat_ReturnsStructuredResponse();
		await binding.GetResponseAsync_WithJsonSchemaFormat_ReturnsValidJson();
		await binding.GetStreamingResponseAsync_WithJsonSchemaFormat_StreamsValidJson();
		await binding.GetResponseAsync_WithComplexJsonSchema_ReturnsStructuredResponse();
		await binding.GetResponseAsync_WithJsonSchemaFormatAndCustomOptions_Works();
		await binding.GetResponseAsyncT_ReturnsStructuredResponse();
		await binding.GetResponseAsyncT_WithSimpleType_ReturnsDeserializedResult();
		await binding.GetResponseAsyncT_WithComplexType_ReturnsStructuredResponse();
		await binding.GetResponseAsyncT_WithChatOptions_ReturnsStructuredResponse();
		await binding.GetResponseAsyncT_WithMessageList_ReturnsStructuredResponse();
		await binding.GetResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow();
		await binding.GetStreamingResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow();
		await binding.GetResponseAsync_WithTypedPromptFallback_UsesSchemaFreeJson();
	}

	[Fact]
	public async Task Usage_InputOnlyCountsDriveTelemetry()
	{
		var binding = new UsageBinding(output);
		await binding.GetResponseAsync_ReturnsExpectedUsage();
		await binding.GetStreamingResponseAsync_ReturnsExpectedUsage();
	}

	private sealed class ToolBinding : ChatClientFunctionCallingTestsBase<ToolControlClient>
	{
		protected override bool? SupportsRequiredToolMode => true;
		protected override bool PreservesToolActivityContext => true;
		protected override bool PreservesToolCancellation => true;
		protected override IChatClient EnableFunctionCalling(ToolControlClient client, Action<ChatOptions>? configureOptions = null)
		{
			var builder = client.AsBuilder().UseFunctionInvocation();
			if (configureOptions is not null)
				builder.ConfigureOptions(configureOptions);
			return builder.Build();
		}
	}

	private sealed class RejectedToolBinding : ChatClientFunctionCallingTestsBase<ControlClient>
	{
		protected override bool? SupportsRequiredToolMode => false;
	}

	private sealed class MetadataBinding : ChatClientGetServiceTestsBase<ControlClient>
	{
		protected override string ExpectedProviderName => "control";
		protected override string ExpectedDefaultModelId => "control-model";
		protected override void AssertProviderMetadata(ChatClientMetadata metadata) =>
			Assert.Null(metadata.ProviderUri);
	}

	private sealed class MessageBinding : ChatClientMessagesTestsBase<ControlClient>
	{
		protected override bool RequiresPrompt => true;
		protected override bool RejectsUnsupportedMedia => true;
		protected override bool RejectsUnsupportedStandardContent(string kind, ChatRole role) => true;
		protected override bool RejectsMisplacedFunctionContent(ChatRole role, bool isCall) => true;
	}

	private sealed class OptionsBinding : ChatClientOptionsTestsBase<ControlClient>
	{
		protected override bool RejectsStatefulConversationOptions => true;
		protected override bool RequiresExplicitJsonSchema => true;
	}

	private sealed class SchemaBinding : ChatClientJsonSchemaTestsBase<ControlClient>
	{
		protected override bool SupportsStructuredOutput => false;
		protected override bool? RequiresExplicitJsonSchema => true;
	}

	private sealed class UsageBinding(ITestOutputHelper output) : ChatClientUsageTestsBase<ControlClient>(output)
	{
		protected override bool IsOutputUsageAvailable => false;
	}

	private class ControlClient : IChatClient
	{
		public object? GetService(Type serviceType, object? serviceKey = null)
		{
			ArgumentNullException.ThrowIfNull(serviceType);
			if (serviceKey is not null)
				return null;
			if (serviceType == typeof(ChatClientMetadata))
				return new ChatClientMetadata("control", defaultModelId: "control-model");
			return serviceType.IsInstanceOfType(this) ? this : null;
		}

		public virtual Task<ChatResponse> GetResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(messages);
			cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable MEAI001
			if (options?.ContinuationToken is not null)
				throw new NotSupportedException(nameof(ChatOptions.ContinuationToken));
			if (options?.ConversationId is not null)
				throw new NotSupportedException(nameof(ChatOptions.ConversationId));
			if (options?.AllowBackgroundResponses is true)
				throw new NotSupportedException(nameof(ChatOptions.AllowBackgroundResponses));
#pragma warning restore MEAI001
			if (options?.ResponseFormat is not null)
				throw new NotSupportedException("An explicit JSON schema is required; structured output is unavailable.");
			if (options?.ToolMode is RequiredChatToolMode)
				throw new NotSupportedException("Required tools are unavailable.");
			var history = messages.ToArray();
			foreach (var message in history)
			{
				foreach (var content in message.Contents)
				{
					if (content is DataContent data)
						throw new NotSupportedException(data.MediaType);
					if (content is UriContent uri)
						throw new NotSupportedException(uri.MediaType);
					if (content is not TextContent && content.GetType().Assembly == typeof(AIContent).Assembly)
						throw new NotSupportedException(content.GetType().Name);
				}
			}
			if (!history.Any(message => message.Role == ChatRole.User || message.Role == ChatRole.Assistant))
				throw new ArgumentException("A prompt is required.", nameof(messages));
			return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "HELLO"))
			{
				ModelId = "control-model",
				Usage = new() { InputTokenCount = 3 }
			});
		}

		public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			await Task.Yield();
			var response = await GetResponseAsync(messages, options, cancellationToken);
			foreach (var message in response.Messages)
				yield return new ChatResponseUpdate(message.Role, message.Contents) { ModelId = response.ModelId };
			if (response.Usage is not null)
				yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(response.Usage)]) { ModelId = response.ModelId };
		}

		public void Dispose() { }
	}

	private sealed class ToolControlClient : ControlClient
	{
		public override Task<ChatResponse> GetResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var results = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
			if (results.Length > 0)
				return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
					string.Join(" ", results.Select(result => result.Result?.ToString())))));

			var name = options!.Tools![0].Name;
			Dictionary<string, object?> arguments = name switch
			{
				"lookupRequiredValue" => new() { ["key"] = "secret" },
				"capture_formatted" => new()
				{
					["id"] = "8d418a4d-fd44-47f4-813d-08b6b40abeca",
					["timestamp"] = "2026-10-20T12:34:56+00:00"
				},
				"capture_nullable" => new() { ["value"] = null, ["label"] = "OPTIONAL_LABEL" },
				"GetWeather" => new() { ["location"] = "Seattle" },
				_ => []
			};
			return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
				[new FunctionCallContent("control-call", name, arguments)])));
		}
	}
}
