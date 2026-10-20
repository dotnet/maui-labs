#if WINDOWS
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class WindowsAIImageHistoryTests
{
	// Windows omits unrecognized image-generation markers but retains the resulting blank Assistant turn.
	[Fact]
	public void ConvertToPrompt_ImageGenerationHistory_DoesNotInventToolActivity()
	{
#pragma warning disable MEAI001 // The image-generation tool content API is experimental.
		var history = new ChatMessage[]
		{
			new(ChatRole.User, "Generate an image."),
			new(ChatRole.Assistant, [new ImageGenerationToolCallContent("image-call")]),
			new(ChatRole.Tool, [new ImageGenerationToolResultContent("image-call")
			{
				Outputs = [new DataContent(new byte[] { 0 }, "image/png")],
			}]),
			new(ChatRole.User, "What did I request?"),
		};
#pragma warning restore MEAI001

		Assert.Equal(
			string.Join(Environment.NewLine + Environment.NewLine,
				"User: Generate an image.", "Assistant: ", "User: What did I request?"),
			WindowsAIChatClient.ConvertToPrompt(history));
	}
}
#endif
