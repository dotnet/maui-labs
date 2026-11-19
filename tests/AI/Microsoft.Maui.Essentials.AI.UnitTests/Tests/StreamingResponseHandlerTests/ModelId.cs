using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public partial class StreamingResponseHandlerTests
{
	public class ModelIdTests
	{
		[Fact]
		public async Task PassthroughUpdates_WithoutConfiguredModelId_RemainUntagged()
		{
			var handler = new StreamingResponseHandler();

			handler.ProcessContent("Hello");
			handler.ProcessToolCall("call-1", "GetWeather", "{}");
			handler.ProcessToolResult("call-1", "Sunny");
			handler.Complete();

			var updates = await ReadAll(handler);

			Assert.Equal(3, updates.Count);
			Assert.All(updates, update => Assert.Null(update.ModelId));
			Assert.Equal("Hello", updates[0].Text);
			Assert.IsType<FunctionCallContent>(Assert.Single(updates[1].Contents));
			Assert.IsType<FunctionResultContent>(Assert.Single(updates[2].Contents));
		}

		[Fact]
		public async Task PassthroughUpdates_PropagateConfiguredModelId_ToEveryUpdateAndResponse()
		{
			const string modelId = "delta-provider-model";
			var handler = new StreamingResponseHandler(modelId);
			var usage = new UsageDetails { InputTokenCount = 5, OutputTokenCount = 3, TotalTokenCount = 8 };

			handler.ProcessContent("Hello");
			handler.ProcessToolCall("call-1", "GetWeather", "{\"location\":\"Boston\"}");
			handler.ProcessToolResult("call-1", "Sunny");
			handler.Complete(usage);

			var updates = await ReadAll(handler);

			Assert.Collection(updates,
				update => Assert.IsType<TextContent>(Assert.Single(update.Contents)),
				update => Assert.IsType<FunctionCallContent>(Assert.Single(update.Contents)),
				update => Assert.IsType<FunctionResultContent>(Assert.Single(update.Contents)),
				update => Assert.IsType<UsageContent>(Assert.Single(update.Contents)));
			Assert.All(updates, update => Assert.Equal(modelId, update.ModelId));
			Assert.Equal(modelId, updates.ToChatResponse().ModelId);
		}
	}
}
