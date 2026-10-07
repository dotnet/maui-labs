using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class AppleVisionRecognizeDocumentsClientTests
{
	private static async IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot> Pages(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		for (var number = 1; number <= 2; number++)
		{
			await Task.Yield();
			cancellationToken.ThrowIfCancellationRequested();
			using var json = JsonDocument.Parse("""[{"transcript":"Page text","nodes":[{"path":"p","kind":"paragraph","text":"Text"}]}]""");
			yield return new AppleVisionRecognizeDocumentsPageSnapshot(number, 2, json.RootElement);
		}
	}

	[Fact]
	public async Task ExtractPagesAsync_ReportsStandardProgressWithoutProviderMetadata()
	{
		using var source = new MemoryStream([1]);
		using var client = new AppleVisionRecognizeDocumentsClient((stream, mediaType, token) =>
		{
			Assert.Same(source, stream);
			Assert.Equal("application/pdf", mediaType);
			return Pages(token);
		});
		var updates = new List<DocumentExtractionPageResult>();
		await foreach (var update in client.ExtractPagesAsync(source, "application/pdf"))
			updates.Add(update);
		Assert.Equal([1, 2], updates.Select(update => update.PagesProcessed));
		Assert.All(updates, update =>
		{
			Assert.Equal(2, update.TotalPages);
			Assert.Null(update.Usage);
			Assert.Null(update.AdditionalProperties);
			Assert.Null(update.Page.AdditionalProperties);
			Assert.Equal(JsonValueKind.Array, Assert.IsType<JsonElement>(update.Page.RawRepresentation).ValueKind);
		});
		Assert.True(source.CanRead);
	}

	[Fact]
	public async Task ExtractAsync_UsesUpstreamPageReduction()
	{
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, token) => Pages(token));
		using var source = new MemoryStream([1]);
		var result = await client.ExtractAsync(source, "application/pdf");
		Assert.Equal([1, 2], result.Pages.Select(page => page.PageNumber));
		Assert.Equal("Page text\n\nPage text", result.Text);
		Assert.Null(result.AdditionalProperties);
		Assert.Null(result.Usage);
		Assert.True(source.CanRead);
	}

	[Fact]
	public async Task ExtractAsync_ModelSelectionIsUnsupportedAndDoesNotInvokeVision()
	{
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, _) =>
			throw new InvalidOperationException("Recognition must not start."));
		using var source = new MemoryStream([1]);
		await Assert.ThrowsAsync<NotSupportedException>(() =>
			client.ExtractAsync(source, "image/png", new DocumentExtractionOptions { ModelId = "other" }));
		Assert.Equal(0, source.Position);
	}

	[Fact]
	public async Task ExtractAsync_UnmappedAdditionalOptionsFailWithoutMutation()
	{
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, token) => Pages(token));
		using var source = new MemoryStream([1]);
		var options = new DocumentExtractionOptions { AdditionalProperties = new() { ["caller.option"] = 42 } };
		await Assert.ThrowsAsync<NotSupportedException>(() => client.ExtractAsync(source, "image/png", options));
		Assert.Equal(42, options.AdditionalProperties["caller.option"]);
	}

	[Fact]
	public void GetService_UsesStandardMetadataAndKeySemantics()
	{
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, token) => Pages(token));
		Assert.Throws<ArgumentNullException>(() => client.GetService(null!));
		var metadata = client.GetService<DocumentExtractionClientMetadata>();
		Assert.NotNull(metadata);
		Assert.Equal("apple.vision", metadata.ProviderName);
		Assert.Null(metadata.DefaultModelId);
		Assert.Same(metadata, client.GetService<DocumentExtractionClientMetadata>());
		Assert.Same(client, client.GetService<IDocumentExtractionClient>());
		Assert.Same(client, client.GetService<AppleVisionRecognizeDocumentsClient>());
		Assert.Null(client.GetService(typeof(DocumentExtractionClientMetadata), "key"));
		Assert.Null(client.GetService(typeof(string)));
		client.Dispose();
	}

	[Fact]
	public async Task ExtractAsync_AlreadyCancelled_DoesNotInvokeRecognition()
	{
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, _) =>
			throw new InvalidOperationException("Recognition must not start."));
		using var source = new MemoryStream([1]);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			client.ExtractAsync(source, "image/png", cancellationToken: cancellation.Token));
		Assert.Equal(0, source.Position);
	}

	[Fact]
	public async Task ExtractAsync_UnsupportedMedia_DoesNotInvokeRecognition()
	{
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, _) =>
			throw new InvalidOperationException("Recognition must not start."));
		using var source = new MemoryStream([1]);
		await Assert.ThrowsAsync<NotSupportedException>(() => client.ExtractAsync(source, "text/plain"));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ExtractPagesAsync_CancelledOrStopped_DisposesEnumeratorNotSource(bool cancel)
	{
		using var source = new MemoryStream([1]);
		using var cancellation = new CancellationTokenSource();
		var disposed = false;
		async IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot> Recognize(
			[EnumeratorCancellation] CancellationToken token)
		{
			try
			{
				await foreach (var page in Pages(token))
					yield return page;
			}
			finally
			{
				disposed = true;
			}
		}
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, token) => Recognize(token));
		await using (var enumerator = client.ExtractPagesAsync(source, "application/pdf",
			cancellationToken: cancellation.Token).GetAsyncEnumerator())
		{
			Assert.True(await enumerator.MoveNextAsync());
			if (cancel)
			{
				cancellation.Cancel();
				await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
			}
		}
		Assert.True(disposed);
		Assert.True(source.CanRead);
	}
}
