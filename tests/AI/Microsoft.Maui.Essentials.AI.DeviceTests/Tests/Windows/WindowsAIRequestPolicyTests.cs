#if WINDOWS
#pragma warning disable MEAI001 // Exercise explicitly rejected experimental MEAI options and content.
using System.Text.Json;
using Microsoft.Extensions.AI;
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
	[InlineData(null)]
	[InlineData(false)]
	public void ValidateOptions_NoStatefulRequest_Accepts(bool? background) =>
		WindowsAIChatClient.ValidateOptions(new ChatOptions { AllowBackgroundResponses = background });

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void BlankUserTurn_IsNotMistakenForMissingPrompt(bool unknownOnly)
	{
		var (_, history) = WindowsAIChatClient.NormalizeChatMessages(
			[new(ChatRole.User, unknownOnly ? [new CustomContent()] : [])]);
		Assert.Equal("User: ", WindowsAIChatClient.ConvertToPrompt(history));
	}

	[Fact]
	public void EmptyRecognizedPayload_RejectBeforeAnyNativeWork()
	{
		AIContent content = new DataContent(ReadOnlyMemory<byte>.Empty, "image/png");
		Assert.Throws<ArgumentException>(() =>
			WindowsAIChatClient.NormalizeChatMessages([new(ChatRole.User, [content])]));
	}

	[Fact]
	public void UnknownCustomContent_OmitsOnlyUnknownNeighbors()
	{
		var text = WindowsAIChatClient.ConvertToPrompt(
			[new(ChatRole.User, [new CustomContent(), new TextContent("Keep"), new CustomContent()])]);
		Assert.Equal("User: Keep", text);
	}

	private sealed class CustomContent : AIContent;
}
#endif
