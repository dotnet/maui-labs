using System.Runtime.CompilerServices;
using System.Text.Json;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.DocumentExtraction;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class DocumentExtractionServiceTests
{
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ExtractAsync_UsesTheSelectedActualApiAndKeepsRawSeparate(bool stream)
	{
		using var client = new Client();
		var input = new SelectedDocument("input.png", "image/png", [1, 2, 3]);
		var result = await new DocumentExtractionService().ExtractAsync(client, input, stream);
		Assert.Equal(stream, client.StreamCalled);
		Assert.Equal(!stream, client.SingleCalled);
		Assert.Equal(2, result.Result.Pages.Count);
		Assert.Contains("\"$type\": \"block\"", result.Output);
		Assert.DoesNotContain("nativeOnly", result.Output);
		Assert.Contains("nativeOnly", result.RawOutput);
		Assert.Contains("rootOnly", result.RawOutput);
		Assert.DoesNotContain("AdditionalProperties\": {", result.Output);
	}

	[Fact]
	public async Task ExtractAsync_CancellationIsNotReportedAsSuccess()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		using var client = new Client();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			new DocumentExtractionService().ExtractAsync(client,
				new SelectedDocument("input.png", "image/png", [1]), true, cancellationToken: cancellation.Token));
		Assert.False(client.StreamCalled);
	}

	private sealed class Client : IDocumentExtractionClient
	{
		private readonly object _raw = new { rootOnly = "document", nativeOnly = "complete response" };
		internal bool StreamCalled { get; private set; }
		internal bool SingleCalled { get; private set; }

		public Task<DocumentExtractionResult> ExtractAsync(
			Stream document, string mediaType, DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
		{
			SingleCalled = true;
			using var root = JsonDocument.Parse("""{"rootOnly":"document","nativeOnly":"complete response"}""");
			return Task.FromResult(new DocumentExtractionResult([Page(1), Page(2)])
			{
				RawRepresentation = root.RootElement.Clone(),
			});
		}

		public async IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
			Stream document, string mediaType, DocumentExtractionOptions? options = null,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			StreamCalled = true;
			for (var number = 1; number <= 2; number++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await Task.Yield();
				yield return new DocumentExtractionPageResult(Page(number))
				{
					PagesProcessed = number, TotalPages = 2, RawRepresentation = _raw,
				};
			}
		}

		private static DocumentPage Page(int number)
		{
			using var json = JsonDocument.Parse("""{"nativeOnly":"original"}""");
			return new DocumentPage(number, "Text")
			{
				Elements = [new DocumentBlock("Text") { Kind = DocumentBlockKind.Paragraph }],
				RawRepresentation = json.RootElement.Clone(),
			};
		}

		public object? GetService(Type serviceType, object? serviceKey = null) => null;
		public void Dispose() { }
	}
}
