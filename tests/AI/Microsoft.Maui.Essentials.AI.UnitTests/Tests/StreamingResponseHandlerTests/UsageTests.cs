using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public partial class StreamingResponseHandlerTests
{
	[Fact]
	public async Task Complete_SuppliedUsage_PreservesCountsAndModelIdentity()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
		handler.SetModelId("local-model");
		handler.ProcessContent("hello");
		var usage = new UsageDetails { InputTokenCount = 12, OutputTokenCount = 7, TotalTokenCount = 19 };
		handler.Complete(usage);

		var updates = await ReadAll(handler);
		Assert.All(updates, update => Assert.Equal("local-model", update.ModelId));
		var supplied = Assert.Single(updates.SelectMany(update => update.Contents).OfType<UsageContent>());
		Assert.Same(usage, supplied.Details);
		Assert.Equal("hello", string.Concat(updates.SelectMany(update => update.Contents).OfType<TextContent>().Select(text => text.Text)));
		Assert.Equal(19, updates.ToChatResponse().Usage?.TotalTokenCount);
	}

	[Fact]
	public async Task Complete_UsageAbsent_DoesNotInventUsage()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker(), "apple-intelligence");
		handler.ProcessContent("hello");
		handler.Complete();

		var updates = await ReadAll(handler);
		Assert.DoesNotContain(updates.SelectMany(update => update.Contents), content => content is UsageContent);
		Assert.Null(updates.ToChatResponse().Usage);
	}
}
