#if WINDOWS
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class WindowsAIImageHistoryTests
{
	[Fact]
	public async Task GetResponseAsync_ImageGenerationHistory_KeepsToolActivityWithoutDecodingImage()
	{
		using var client = new WindowsAIChatClient();
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

		var response = await client.GetResponseAsync(history);
		Assert.NotNull(response);
	}
}
#endif
