using System.Runtime.CompilerServices;
using System.Text.Json;
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
	public async Task ExtractPagesAsync_ReturnsObservedPagesWithoutOpaqueNativeData()
	{
		using var source = new MemoryStream([1]);
		using var client = new AppleVisionRecognizeDocumentsClient((stream, mediaType, token) =>
		{
			Assert.Same(source, stream);
			Assert.Equal("application/pdf", mediaType);
			return Pages(token);
		});
		var pages = new List<DocumentExtractionPage>();
		await foreach (var page in client.ExtractPagesAsync(source, "input.pdf", "application/pdf"))
			pages.Add(page);
		Assert.Equal([1, 2], pages.Select(page => page.PageNumber));
		Assert.All(pages, page =>
		{
			Assert.Null(page.Markdown);
			Assert.Empty(page.PageReferences);
			Assert.All(page.Observations, observation =>
			{
				Assert.Empty(observation.Evidence);
				Assert.Empty(observation.Artifacts);
				Assert.Empty(observation.PageReferences);
			});
		});
		Assert.Equal(2, pages.SelectMany(page => page.Observations).Select(item => item.ProviderId).Distinct().Count());
		Assert.True(source.CanRead);
	}

	[Fact]
	public async Task ExtractAsync_FinalizesSharedDocumentWithCallerIdentityAndOwnership()
	{
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, token) => Pages(token));
		using var source = new MemoryStream([1]);
		var result = await client.ExtractAsync(source, "input.pdf", "application/pdf");
		Assert.Equal([1, 2], result.Pages.Select(page => page.PageNumber));
		Assert.Equal("input.pdf", result.Document.Identifier);
		Assert.Equal(["Text", "Text"], result.Document.EnumerateContent().Select(element => element.Text));
		Assert.False(result.Document.HasMetadata);
		Assert.Empty(result.Document.Artifacts);
		Assert.True(source.CanRead);
		result.Dispose();
		Assert.True(result.IsDisposed);
		Assert.True(result.Document.IsDisposed);
		Assert.True(source.CanRead);
	}

	[Fact]
	public void GetService_UsesIdentityAndKeySemanticsAndRejectsDisposedClient()
	{
		var client = new AppleVisionRecognizeDocumentsClient((_, _, token) => Pages(token));
		Assert.Throws<ArgumentNullException>(() => client.GetService(null!));
		Assert.Same(client, client.GetService(typeof(IDocumentExtractionClient)));
		Assert.Same(client, client.GetService(typeof(AppleVisionRecognizeDocumentsClient)));
		Assert.Null(client.GetService(typeof(IDocumentExtractionClient), "key"));
		Assert.Null(client.GetService(typeof(string)));
		client.Dispose();
		client.Dispose();
		Assert.Throws<ObjectDisposedException>(() => client.GetService(typeof(IDocumentExtractionClient)));
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	public async Task ExtractAsync_InvalidIdentity_DoesNotInvokeRecognition(string identifier)
	{
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, _) =>
			throw new InvalidOperationException("Recognition must not start."));
		using var source = new MemoryStream([1]);
		await Assert.ThrowsAnyAsync<ArgumentException>(() => client.ExtractAsync(source, identifier, "image/png"));
		Assert.Equal(0, source.Position);
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
			client.ExtractAsync(source, "input", "image/png", cancellation.Token));
		Assert.Equal(0, source.Position);
	}

	[Fact]
	public async Task ExtractAsync_UnsupportedMedia_DoesNotInvokeRecognition()
	{
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, _) =>
			throw new InvalidOperationException("Recognition must not start."));
		using var source = new MemoryStream([1]);
		await Assert.ThrowsAsync<NotSupportedException>(() => client.ExtractAsync(source, "input", "text/plain"));
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
		await using (var enumerator = client.ExtractPagesAsync(source, "input", "application/pdf", cancellation.Token)
			.GetAsyncEnumerator())
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
