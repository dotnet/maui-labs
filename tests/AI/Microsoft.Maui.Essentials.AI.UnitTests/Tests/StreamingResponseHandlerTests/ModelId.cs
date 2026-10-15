using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public partial class StreamingResponseHandlerTests
{
	public class ModelIdTests
	{
		[Theory]
		[InlineData(null)]
		[InlineData("apple-intelligence")]
		[InlineData("another-model")]
		public async Task ChunkedUpdates_UseConfiguredModelId_IncludingToolsAndFlushes(string? modelId)
		{
			var handler = new StreamingResponseHandler(new JsonStreamChunker(), modelId);

			handler.ProcessContent("{\"greeting\":\"Hello\"}");
			handler.ProcessToolCall("call-1", "GetWeather", "{\"location\":\"Boston\"}");
			handler.ProcessToolResult("call-1", "Sunny");
			handler.ProcessContent("{\"greeting\":\"Goodbye\"}");
			handler.Complete();

			var updates = await ReadAll(handler);

			Assert.Equal(6, updates.Count);
			Assert.All(updates, update => Assert.Equal(modelId, update.ModelId));
			Assert.Equal(ChatRole.Assistant, updates[0].Role);
			Assert.Equal(ChatRole.Assistant, updates[1].Role);
			var call = Assert.IsType<FunctionCallContent>(Assert.Single(updates[2].Contents));
			Assert.True(call.InformationalOnly);
			Assert.Equal("GetWeather", call.Name);
			Assert.Equal("Boston", call.Arguments!["location"]?.ToString());
			Assert.Equal(ChatRole.Tool, updates[3].Role);
			var result = Assert.IsType<FunctionResultContent>(Assert.Single(updates[3].Contents));
			Assert.Equal("call-1", result.CallId);
			Assert.Equal("Sunny", result.Result);

			using var beforeTool = JsonDocument.Parse(updates[0].Text + updates[1].Text);
			Assert.Equal("Hello", beforeTool.RootElement.GetProperty("greeting").GetString());
			using var afterTool = JsonDocument.Parse(updates[4].Text + updates[5].Text);
			Assert.Equal("Goodbye", afterTool.RootElement.GetProperty("greeting").GetString());
		}

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

		[Theory]
		[InlineData(null)]
		[InlineData("delta-provider-model")]
		[InlineData("custom-provider-model")]
		public async Task PassthroughUpdates_WithConfiguredModelId_PreserveContentToolsAndUsage(string? modelId)
		{
			var handler = new StreamingResponseHandler(modelId);
			var usage = new UsageDetails { InputTokenCount = 5, OutputTokenCount = 3, TotalTokenCount = 8 };

			handler.ProcessContent("Hello");
			handler.ProcessToolCall("call-1", "GetWeather", "{\"location\":\"Boston\"}");
			handler.ProcessToolResult("call-1", "Sunny");
			handler.Complete(usage);

			var updates = await ReadAll(handler);

			Assert.Equal(4, updates.Count);
			Assert.All(updates, update => Assert.Equal(modelId, update.ModelId));
			Assert.Equal("Hello", updates[0].Text);
			Assert.Equal(ChatRole.Assistant, updates[0].Role);
			var call = Assert.IsType<FunctionCallContent>(Assert.Single(updates[1].Contents));
			Assert.Equal(ChatRole.Assistant, updates[1].Role);
			Assert.Equal("call-1", call.CallId);
			Assert.Equal("GetWeather", call.Name);
			Assert.Equal("Boston", call.Arguments!["location"]?.ToString());
			Assert.True(call.InformationalOnly);
			var result = Assert.IsType<FunctionResultContent>(Assert.Single(updates[2].Contents));
			Assert.Equal(ChatRole.Tool, updates[2].Role);
			Assert.Equal("call-1", result.CallId);
			Assert.Equal("Sunny", result.Result);
			Assert.Same(usage, Assert.IsType<UsageContent>(Assert.Single(updates[3].Contents)).Details);

			var response = updates.ToChatResponse();
			Assert.Equal(modelId, response.ModelId);
			Assert.Equal("Hello", response.Text);
			Assert.Single(response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
			Assert.Single(response.Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
			Assert.Equal(8, response.Usage?.TotalTokenCount);
		}
	}
}
