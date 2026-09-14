using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public partial class StreamingResponseHandlerTests
{
	/// <summary>
	/// Tests for completion and error handling.
	/// </summary>
	public class CompletionTests
	{
		[Fact]
		public async Task Complete_FlushesRemainingContent()
		{
			var handler = new StreamingResponseHandler(new PlainTextStreamChunker());

			handler.ProcessContent("Hello");
			handler.Complete();

			var updates = await ReadAll(handler);

			Assert.Single(updates);
			Assert.Equal("Hello", updates[0].Contents.OfType<Microsoft.Extensions.AI.TextContent>().Single().Text);
		}

		[Fact]
		public async Task CompleteWithError_SurfacesExceptionToReader()
		{
			var handler = new StreamingResponseHandler(new PlainTextStreamChunker());

			handler.ProcessContent("Hello");
			handler.CompleteWithError(new InvalidOperationException("test error"));

			await Assert.ThrowsAsync<InvalidOperationException>(async () => await ReadAll(handler));
		}

		[Fact]
		public void DoubleComplete_DoesNotThrow()
		{
			var handler = new StreamingResponseHandler(new PlainTextStreamChunker());

			handler.CompleteWithError(new InvalidOperationException("first error"));
			handler.Complete();
			handler.CompleteWithError(new InvalidOperationException("second error"));
		}

		[Fact]
		public async Task Complete_WithJsonChunker_FlushesRemainingJsonContent()
		{
			// Use JsonStreamChunker to ensure the Flush()-on-Complete path is exercised.
			// JsonStreamChunker expects complete valid JSON at each step and tracks partial state.
			var handler = new StreamingResponseHandler(new JsonStreamChunker());

			// Feed progressive complete JSON snapshots — the chunker tracks partial strings
			handler.ProcessContent("{\"greeting\":\"Hello\"}");
			handler.ProcessContent("{\"greeting\":\"Hello world\"}");

			// Complete should flush remaining content from JsonStreamChunker
			handler.Complete();

			var updates = await ReadAll(handler);

			// Should have text updates from the progressive JSON
			Assert.NotEmpty(updates);
			var allText = string.Concat(updates
				.SelectMany(u => u.Contents.OfType<Microsoft.Extensions.AI.TextContent>())
				.Select(tc => tc.Text));
			Assert.Contains("Hello", allText, StringComparison.Ordinal);
		}

		[Fact]
		public async Task Complete_WithUsage_EmitsUsageContent()
		{
			var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
			var usage = new UsageDetails
			{
				InputTokenCount = 12,
				OutputTokenCount = 5,
				TotalTokenCount = 17,
				CachedInputTokenCount = 3,
				ReasoningTokenCount = 2
			};

			handler.ProcessContent("Hello");
			handler.Complete(usage);

			var updates = await ReadAll(handler);

			Assert.Equal(2, updates.Count);
			Assert.Equal("Hello", updates[0].Text);
			Assert.Same(usage, Assert.IsType<UsageContent>(Assert.Single(updates[1].Contents)).Details);
		}

		[Fact]
		public async Task Complete_WithUsage_AggregatesIntoChatResponse()
		{
			var handler = new StreamingResponseHandler();
			handler.ProcessContent("Hello");
			handler.Complete(new UsageDetails
			{
				InputTokenCount = 12,
				OutputTokenCount = 5,
				TotalTokenCount = 17,
				CachedInputTokenCount = 3,
				ReasoningTokenCount = 2
			});

			var response = await handler
				.ReadAllAsync(CancellationToken.None)
				.ToChatResponseAsync();

			Assert.Equal("Hello", response.Text);
			Assert.NotNull(response.Usage);
			Assert.Equal(12, response.Usage.InputTokenCount);
			Assert.Equal(5, response.Usage.OutputTokenCount);
			Assert.Equal(17, response.Usage.TotalTokenCount);
			Assert.Equal(3, response.Usage.CachedInputTokenCount);
			Assert.Equal(2, response.Usage.ReasoningTokenCount);
		}
	}
}
