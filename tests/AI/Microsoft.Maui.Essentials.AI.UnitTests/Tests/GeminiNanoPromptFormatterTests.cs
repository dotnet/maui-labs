using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class GeminiNanoPromptFormatterTests
{
	[Fact]
	public void GetSystemInstruction_CombinesOptionsMessagesAndResponseInstruction()
	{
		var messages = new[]
		{
			new ChatMessage(ChatRole.System, "System message"),
			new ChatMessage(ChatRole.User, "Hello"),
		};
		var options = new ChatOptions { Instructions = "Options instruction" };

		var result = GeminiNanoPromptFormatter.GetSystemInstruction(
			messages,
			options,
			"JSON only");

		Assert.Equal(
			"Options instruction\n\nSystem message\n\nJSON only",
			result);
	}

	[Fact]
	public void FormatRole_ToolRole_Throws()
	{
		var exception = Assert.Throws<NotSupportedException>(
			() => GeminiNanoPromptFormatter.FormatRole(ChatRole.Tool));

		Assert.Contains("tool", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ValidateOptions_DefaultPlaygroundRequestWithToolsDisabled_Succeeds()
	{
		GeminiNanoPromptFormatter.ValidateOptions(new ChatOptions { ToolMode = ChatToolMode.None });
	}

	[Fact]
	public void ValidateOptions_NoneDisablesSuppliedToolsLikeAppleClient()
	{
		GeminiNanoPromptFormatter.ValidateOptions(new ChatOptions
		{
			ToolMode = ChatToolMode.None,
			Tools = [AIFunctionFactory.Create(() => "result", "test")],
			AllowMultipleToolCalls = true,
		});
	}

	[Fact]
	public void ValidateOptions_EnabledTools_Throws()
	{
		Assert.Throws<NotSupportedException>(() => GeminiNanoPromptFormatter.ValidateOptions(
			new ChatOptions { Tools = [AIFunctionFactory.Create(() => "result", "test")] }));
		Assert.Throws<NotSupportedException>(() => GeminiNanoPromptFormatter.ValidateOptions(
			new ChatOptions { ToolMode = ChatToolMode.Auto }));
		Assert.Throws<NotSupportedException>(() => GeminiNanoPromptFormatter.ValidateOptions(
			new ChatOptions { ToolMode = ChatToolMode.RequireAny }));
	}

	[Theory]
	[InlineData(float.NaN)]
	[InlineData(float.PositiveInfinity)]
	[InlineData(float.NegativeInfinity)]
	[InlineData(-0.1f)]
	[InlineData(1.1f)]
	public void ValidateOptions_InvalidTemperature_Throws(float temperature)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => GeminiNanoPromptFormatter.ValidateOptions(
			new ChatOptions { Temperature = temperature }));
	}

	[Theory]
	[InlineData(0f)]
	[InlineData(1f)]
	public void ValidateOptions_TemperatureBoundaries_Succeed(float temperature)
	{
		GeminiNanoPromptFormatter.ValidateOptions(new ChatOptions { Temperature = temperature });
	}

	[Fact]
	public void ValidateOptions_InvalidNumericOptions_Throw()
	{
		foreach (var options in new ChatOptions[]
		{
			new() { TopK = 0 },
			new() { Seed = -1 },
			new() { Seed = (long)int.MaxValue + 1 },
			new() { MaxOutputTokens = 0 },
			new() { MaxOutputTokens = 4097 },
			new() { Reasoning = new() { Effort = (ReasoningEffort)999 } },
			new() { Reasoning = new() { Output = (ReasoningOutput)999 } },
		})
			Assert.Throws<ArgumentOutOfRangeException>(() => GeminiNanoPromptFormatter.ValidateOptions(options));
	}

	[Fact]
	public void ValidateMessages_ToolAndUnknownRoles_ThrowBeforeInference()
	{
		foreach (var role in new[] { ChatRole.Tool, new ChatRole("custom") })
			Assert.Throws<NotSupportedException>(() => GeminiNanoPromptFormatter.ValidateMessages(
				[new(ChatRole.User, "hello"), new(role, "unsupported")]));
	}

	[Fact]
	public void ValidateMessages_ToolContent_ThrowsEvenWhenToolsDisabled()
	{
		Assert.Throws<NotSupportedException>(() => GeminiNanoPromptFormatter.ValidateMessages(
			[new(ChatRole.User, "hello"), new(ChatRole.Assistant, [new FunctionCallContent("id", "test")])]));
	}

	[Fact]
	public void ValidateMessages_UnsupportedSystemContent_Throws()
	{
		Assert.Throws<NotSupportedException>(() => GeminiNanoPromptFormatter.ValidateMessages(
			[new(ChatRole.User, "hello"), new(ChatRole.System, [new DataContent(new byte[] { 1 }, "image/png")])]));
	}

	[Fact]
	public void ValidateMessages_UnsupportedImageAndMedia_Throw()
	{
		foreach (var content in new AIContent[]
		{
			new DataContent(new byte[] { 1 }, "audio/wav"),
			new DataContent(Array.Empty<byte>(), "image/png"),
			new UriContent(new Uri("https://example.com/image.png"), "image/png"),
		})
			Assert.Throws<NotSupportedException>(() => GeminiNanoPromptFormatter.ValidateMessages(
				[new(ChatRole.User, [content])]));
	}

	[Fact]
	public void ValidateMessages_UserReasoningContent_Throws()
	{
		Assert.Throws<NotSupportedException>(() => GeminiNanoPromptFormatter.ValidateMessages(
			[new(ChatRole.User, [new TextReasoningContent("not user input")])]));
	}

	[Fact]
	public void GetConversationMessages_ReasoningOnlyHistoryDoesNotCreateEmptyRoleBlocks()
	{
		ChatMessage[] messages =
		[
			new(ChatRole.System, "instructions"),
			new(ChatRole.Assistant, [new TextReasoningContent("old thought")]),
			new(ChatRole.Assistant, ""),
			new(ChatRole.User, "question"),
		];

		GeminiNanoPromptFormatter.ValidateMessages(messages);

		Assert.Same(messages[3], Assert.Single(GeminiNanoPromptFormatter.GetConversationMessages(messages)));
	}

	[Fact]
	public void ValidateMessages_NoConversationContent_Throws()
	{
		Assert.Throws<ArgumentException>(() => GeminiNanoPromptFormatter.ValidateMessages([]));
		Assert.Throws<ArgumentException>(() => GeminiNanoPromptFormatter.ValidateMessages(
			[new(ChatRole.System, "instructions"), new(ChatRole.Assistant, [new TextReasoningContent("thought")])]));
	}
}
