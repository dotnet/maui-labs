#if WINDOWS
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class PhiSilicaImageHistoryTests
{
	[Fact]
	public async Task GetPromptFitAsync_ImageGenerationHistory_KeepsToolActivityWithoutDecodingImage()
	{
		using var client = new PhiSilicaChatClient();
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

		var fit = await client.GetPromptFitAsync(history);
		var expected = string.Join(Environment.NewLine,
			"User: Generate an image.",
			"Assistant: [Image generation requested]",
			"[Image generation result: 1 image(s)]",
			"User: What did I request?");
		Assert.Equal(expected.Length, fit.PromptLength);
	}
}
#endif
