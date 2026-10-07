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
}
