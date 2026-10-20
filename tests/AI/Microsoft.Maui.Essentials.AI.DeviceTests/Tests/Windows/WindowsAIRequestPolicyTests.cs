#if WINDOWS
#pragma warning disable MEAI001 // Exercise explicitly rejected experimental MEAI options and content.
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Windows.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

// These policies describe Windows LanguageModel's text-only, stateless native boundary.
public class WindowsAIRequestPolicyTests
{
	public static IEnumerable<object[]> SupportedToolModes()
	{
		yield return [ChatToolMode.Auto];
		yield return [ChatToolMode.None];
		yield return [new AutoChatToolMode()];
		yield return [new NoneChatToolMode()];
		yield return [JsonSerializer.Deserialize<ChatToolMode>("""{"$type":"auto"}""", AIJsonUtilities.DefaultOptions)!];
		yield return [JsonSerializer.Deserialize<ChatToolMode>("""{"$type":"none"}""", AIJsonUtilities.DefaultOptions)!];
	}

	[Theory]
	[MemberData(nameof(SupportedToolModes))]
	public void ValidateOptions_AutoAndNone_UseTypeSemantics(ChatToolMode mode) =>
		WindowsAIChatClient.ValidateOptions(new ChatOptions { ToolMode = mode });

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ValidateOptions_ConstructedRequired_Rejects(bool named)
	{
		var mode = new RequiredChatToolMode(named ? "lookup" : null);
		Assert.Throws<NotSupportedException>(() =>
			WindowsAIChatClient.ValidateOptions(new ChatOptions { ToolMode = mode }));
	}

	[Theory]
	[InlineData("ConversationId", false)]
	[InlineData("ConversationId", true)]
	[InlineData("ContinuationToken", false)]
	[InlineData("AllowBackgroundResponses", false)]
	public async Task StatefulOptions_RejectBeforeAnyNativeWork(string name, bool empty)
	{
		var options = new ChatOptions();
		if (name == "ConversationId") options.ConversationId = empty ? "" : "conversation";
		else if (name == "ContinuationToken") options.ContinuationToken = ResponseContinuationToken.FromBytes(new byte[] { 1 });
		else options.AllowBackgroundResponses = true;
		using var client = NoNativeClient();
		var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
			client.GetResponseAsync([new(ChatRole.User, "Hello")], options));
		Assert.Contains(name, error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(null)]
	[InlineData(false)]
	public void ValidateOptions_NoStatefulRequest_Accepts(bool? background) =>
		WindowsAIChatClient.ValidateOptions(new ChatOptions { AllowBackgroundResponses = background });

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task InstructionsOnly_RejectBeforeAnyNativeWork(bool systemMessage, bool structured)
	{
		using var client = NoNativeClient();
		var options = new ChatOptions
		{
			Instructions = "Be helpful",
			ResponseFormat = structured ? ChatResponseFormat.ForJsonSchema<Payload>() : null
		};
		ChatMessage[] messages = systemMessage ? [new(ChatRole.System, "Answer")] : [];
		var error = await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync(messages, options));
		Assert.Contains("User or Assistant", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task BlankUserTurn_IsNotMistakenForMissingPrompt(bool unknownOnly)
	{
		using var client = new WindowsAIChatClient(() => AIFeatureReadyState.NotReady,
			() => throw new InvalidOperationException("Must not create"));
		var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
			client.GetResponseAsync([new(ChatRole.User, unknownOnly ? [new CustomContent()] : [])]));
		Assert.Contains("NotReady", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task SchemaFreeJson_RejectBeforeAnyNativeWork(bool streaming)
	{
		using var client = NoNativeClient();
		ChatMessage[] messages = [new(ChatRole.User, "Return JSON")];
		var options = new ChatOptions { ResponseFormat = ChatResponseFormat.Json };
		var error = await Assert.ThrowsAsync<NotSupportedException>(() => streaming
			? client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync()
			: client.GetResponseAsync(messages, options));
		Assert.Contains("explicit JSON schema", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TypedPromptFallback_RejectBeforeAnyNativeWork()
	{
		using var client = NoNativeClient();
		var error = await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync<Payload>(
			[new ChatMessage(ChatRole.User, "Return a message")], useJsonSchemaResponseFormat: false));
		Assert.Contains("schema", error.Message, StringComparison.Ordinal);
	}

	public static IEnumerable<object[]> UnsupportedContent()
	{
		yield return [new DataContent(new byte[] { 1 }, "audio/ogg")];
		yield return [new DataContent(new byte[] { 1 }, "application/pdf")];
		yield return [new UriContent("https://example.invalid/audio", "audio/ogg")];
		yield return [new HostedFileContent("file-1")];
		yield return [new HostedVectorStoreContent("store-1")];
		yield return [new TextReasoningContent("reasoning")];
		yield return [new UsageContent()];
		yield return [new ErrorContent("error")];
		yield return [new CodeInterpreterToolCallContent("call-1")];
		yield return [new CodeInterpreterToolResultContent("call-1")];
		yield return [new ToolApprovalRequestContent("approval-1", new FunctionCallContent("call-1", "lookup"))];
	}

	[Theory]
	[MemberData(nameof(UnsupportedContent))]
	public async Task StandardUnsupportedContent_RejectBeforeAnyNativeWork(AIContent content)
	{
		using var client = NoNativeClient();
		var error = await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync(
			[new(ChatRole.User, [new TextContent("Keep"), content])]));
		Assert.Contains(content.GetType().Name, error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task EmptyRecognizedPayload_RejectBeforeAnyNativeWork()
	{
		using var client = NoNativeClient();
		AIContent content = new DataContent(ReadOnlyMemory<byte>.Empty, "image/png");
		await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync(
			[new(ChatRole.User, [content])]));
	}

	[Theory]
	[InlineData("system", true)]
	[InlineData("user", true)]
	[InlineData("tool", true)]
	[InlineData("system", false)]
	[InlineData("user", false)]
	[InlineData("assistant", false)]
	public async Task MisplacedFunctionContent_RejectBeforeAnyNativeWork(string role, bool call)
	{
		using var client = NoNativeClient();
		AIContent content = call ? new FunctionCallContent("call-1", "lookup") : new FunctionResultContent("call-1", "result");
		await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync(
			[new(new ChatRole(role), [content]), new(ChatRole.User, "Hello")]));
	}

	[Fact]
	public void UnknownCustomContent_OmitsOnlyUnknownNeighbors()
	{
		var text = WindowsAIChatClient.ConvertToPrompt(
			[new(ChatRole.User, [new CustomContent(), new TextContent("Keep"), new CustomContent()])]);
		Assert.Equal("User: Keep", text);
	}

	private static WindowsAIChatClient NoNativeClient() => new(
		() => throw new InvalidOperationException("Must reject before native status read"),
		() => throw new InvalidOperationException("Must reject before native creation"));

	private sealed class CustomContent : AIContent;
	public record Payload(string Message);
}
#endif
