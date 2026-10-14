using System.Runtime.CompilerServices;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class DocumentExtractionServiceTests
{
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ExtractAsync_ReturnsIndependentDisplayAndDisposesOwnedArtifactsAndInput(bool stream)
	{
		using var client = new Client();
		var progress = new ProgressCollector();
		var input = new SelectedDocument("input.png", "image/png", [1, 2, 3]);
		var result = await new DocumentExtractionService().ExtractAsync(client, input, stream, progress);
		Assert.Equal("input.png", client.Identifier);
		Assert.Equal(2, result.PageCount);
		Assert.Equal(2, result.ObservationCount);
		Assert.Contains("\"Type\": \"text\"", result.Output);
		Assert.Contains("\"Identifier\": \"input.png\"", result.Output);
		Assert.DoesNotContain("RawRepresentation", result.Output);
		Assert.DoesNotContain("Metadata", result.Output);
		Assert.True(client.Artifact.IsDisposed);
		Assert.False(client.Source!.CanRead);
		Assert.False(client.Disposed);
		Assert.True(client.EnumeratorDisposed);
		Assert.Equal(stream ? new[] { 1, 2 } : [], progress.Pages);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ExtractAsync_StreamingFailureDisposesArtifactsWithoutDisposingBorrowedClient(bool stream)
	{
		using var client = new Client { FailAfterPage = true };
		await Assert.ThrowsAsync<InvalidDataException>(() =>
			new DocumentExtractionService().ExtractAsync(client, new SelectedDocument("input.png", "image/png", [1]), stream));
		Assert.True(client.Artifact.IsDisposed);
		Assert.False(client.Source!.CanRead);
		Assert.False(client.Disposed);
		Assert.True(client.EnumeratorDisposed);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ExtractAsync_CancellationDisposesArtifactsAndInput(bool stream)
	{
		using var cancellation = new CancellationTokenSource();
		using var client = new Client { CancelAfterPage = cancellation };
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			new DocumentExtractionService().ExtractAsync(client, new SelectedDocument("input.png", "image/png", [1]), stream,
				cancellationToken: cancellation.Token));
		Assert.True(client.Artifact.IsDisposed);
		Assert.False(client.Source!.CanRead);
		Assert.True(client.EnumeratorDisposed);
	}

	[Fact]
	public async Task ExtractAsync_AlreadyCancelledDoesNotCallClient()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		using var client = new Client();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			new DocumentExtractionService().ExtractAsync(client, new SelectedDocument("input.png", "image/png", [1]), true,
				cancellationToken: cancellation.Token));
		Assert.Null(client.Source);
	}

	private sealed class ProgressCollector : IProgress<DocumentExtractionProgress>
	{
		internal List<int> Pages { get; } = [];
		public void Report(DocumentExtractionProgress value) => Pages.Add(value.PagesProcessed);
	}

	private sealed class Client : IDocumentExtractionClient
	{
		internal DocumentArtifact Artifact { get; } = new("image/png", new MemoryStream([1]));
		internal Stream? Source { get; private set; }
		internal string? Identifier { get; private set; }
		internal bool Disposed { get; private set; }
		internal bool EnumeratorDisposed { get; private set; }
		internal bool FailAfterPage { get; init; }
		internal CancellationTokenSource? CancelAfterPage { get; init; }

		public async IAsyncEnumerable<DocumentExtractionPage> ExtractPagesAsync(
			Stream document, string identifier, string mediaType,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			Source = document;
			Identifier = identifier;
			try
			{
				await Task.Yield();
				yield return new DocumentExtractionPage(1,
					[new DocumentExtractionText("page-1", 0, DocumentTextKind.Paragraph, "Text", "Text", artifacts: [Artifact])]);
				if (FailAfterPage)
					throw new InvalidDataException("Stream failed.");
				CancelAfterPage?.Cancel();
				cancellationToken.ThrowIfCancellationRequested();
				yield return new DocumentExtractionPage(3,
					[new DocumentExtractionText("page-3", 0, DocumentTextKind.Paragraph, "Other", "Other")]);
			}
			finally
			{
				EnumeratorDisposed = true;
			}
		}

		public object? GetService(Type serviceType, object? serviceKey = null) => null;
		public void Dispose()
		{
			Disposed = true;
			Artifact.Dispose();
		}
	}
}
