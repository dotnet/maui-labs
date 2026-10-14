using System.Runtime.CompilerServices;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class ProposedDocumentPipelineTests
{
	[Fact]
	public async Task Builder_LoggingDelegationAndDiscoveryUseActualLatestProposal()
	{
		var leaf = new TestClient();
		using var pipeline = new DocumentExtractionClientBuilder(leaf)
			.Use(inner => new TestDelegatingClient(inner))
			.UseLogging(NullLoggerFactory.Instance).Build();
		using var source = new MemoryStream([1, 2, 3]);
		using var result = await pipeline.ExtractAsync(source, "document", "image/png");
		Assert.Equal("document", leaf.Identifier);
		Assert.Equal("document", result.Document.Identifier);
		Assert.Equal("image/png", leaf.MediaType);
		Assert.Equal([1, 2, 3], leaf.Bytes);
		Assert.Same(leaf, pipeline.GetService(typeof(TestClient)));
		Assert.Null(pipeline.GetService(typeof(TestClient), "key"));
		Assert.True(source.CanRead);
	}

	[Fact]
	public async Task KeyedRegistration_BuildsMiddlewareAndDisposesInnerClient()
	{
		var services = new ServiceCollection();
		var leaf = new TestClient();
		services.AddKeyedDocumentExtractionClient("ocr", _ => leaf).UseLogging(NullLoggerFactory.Instance);
		using (var provider = services.BuildServiceProvider())
		{
			var client = provider.GetRequiredKeyedService<IDocumentExtractionClient>("ocr");
			using var source = new MemoryStream([1]);
			using var result = await client.ExtractAsync(source, "identity", "image/png");
			Assert.Same(leaf, client.GetService(typeof(TestClient)));
			Assert.Null(client.GetService(typeof(TestClient), "other"));
		}
		Assert.True(leaf.Disposed);
	}

	[Fact]
	public async Task LocalReaderProjection_UsesFinalizedStandardElementsAndLeavesOwnershipWithResult()
	{
		using var leaf = new TestClient();
		using var source = new MemoryStream([1]);
		using var result = await leaf.ExtractAsync(source, "document", "image/png");
		var document = AppleVisionRecognizeDocumentsIngestionMapper.MapDocument(result);
		Assert.Equal("document", document.Identifier);
		var section = Assert.Single(document.Sections);
		var table = Assert.IsType<IngestionDocumentTable>(Assert.Single(section.Elements));
		Assert.Equal("Cell", table.Cells[0, 0]!.Text);
		Assert.Equal(1, table.PageNumber);
		Assert.False(table.HasMetadata);
		Assert.False(result.IsDisposed);
		Assert.True(source.CanRead);
	}

	[Fact]
	public async Task PageReducer_FinalizesCanonicalDocumentAndIdentity()
	{
		using var leaf = new TestClient();
		using var source = new MemoryStream([1]);
		using var result = await leaf.ExtractPagesAsync(source, "identity", "image/png").ToDocumentExtractionResultAsync("identity");
		Assert.Single(result.Pages);
		Assert.Equal("identity", result.Document.Identifier);
		Assert.IsType<DocumentTableElement>(Assert.Single(Assert.Single(result.Document.Sections).Elements));
	}

	private sealed class TestDelegatingClient(IDocumentExtractionClient inner) : DelegatingDocumentExtractionClient(inner);

	private sealed class TestClient : IDocumentExtractionClient
	{
		internal string? Identifier { get; private set; }
		internal string? MediaType { get; private set; }
		internal byte[] Bytes { get; private set; } = [];
		internal bool Disposed { get; private set; }

		public async IAsyncEnumerable<DocumentExtractionPage> ExtractPagesAsync(
			Stream document, string identifier, string mediaType,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			Identifier = identifier;
			MediaType = mediaType;
			using var output = new MemoryStream();
			await document.CopyToAsync(output, cancellationToken);
			Bytes = output.ToArray();
			yield return new DocumentExtractionPage(1, [new DocumentExtractionTable("table", 0, "| Cell |", 1, 1,
				[new(0, 0, new DocumentExtractionText("cell", 0, DocumentTextKind.Paragraph, "Cell", "Cell"))])]);
		}

		public object? GetService(Type serviceType, object? serviceKey = null) =>
			serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

		public void Dispose() => Disposed = true;
	}
}
