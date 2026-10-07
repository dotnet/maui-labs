using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class ProposedDocumentPipelineTests
{
	[Fact]
	public async Task FullBuilder_ConfigurationLoggingTelemetryAndDiscoveryUseActualProposal()
	{
		using var leaf = new TestClient();
		using var pipeline = leaf.AsBuilder()
			.ConfigureOptions(options => options.ModelId ??= "configured")
			.UseLogging(NullLoggerFactory.Instance)
			.UseOpenTelemetry(sourceName: "proposal-test")
			.Build();
		var options = new DocumentExtractionOptions { AdditionalProperties = new() { ["caller"] = 42 } };
		using var source = new MemoryStream([1, 2, 3]);
		var result = await pipeline.ExtractAsync(source, "image/png", options);
		Assert.Equal("Actual page", Assert.Single(result.Pages).Text);
		Assert.Equal("configured", leaf.Options!.ModelId);
		Assert.Null(options.ModelId);
		Assert.NotSame(options, leaf.Options);
		Assert.Equal(42, options.AdditionalProperties["caller"]);
		Assert.Same(leaf, pipeline.GetService<TestClient>());
		Assert.Equal("test", pipeline.GetService<DocumentExtractionClientMetadata>()!.ProviderName);
		Assert.NotNull(pipeline.GetService<ActivitySource>());
		Assert.True(source.CanRead);
	}

	[Fact]
	public async Task KeyedRegistration_BuildsRealMiddlewareAndDisposesInnerClient()
	{
		var services = new ServiceCollection();
		var leaf = new TestClient();
		services.AddKeyedDocumentExtractionClient("ocr", leaf).ConfigureOptions(options => options.ModelId = "configured");
		using (var provider = services.BuildServiceProvider())
		{
			var client = provider.GetRequiredKeyedService<IDocumentExtractionClient>("ocr");
			using var source = new MemoryStream([1]);
			await client.ExtractAsync(source, "image/png");
			Assert.Equal("configured", leaf.Options!.ModelId);
			Assert.Same(leaf, client.GetService<TestClient>());
			Assert.Null(client.GetService(typeof(TestClient), "other"));
		}
		Assert.True(leaf.Disposed);
	}

	[Fact]
	public async Task OriginalDataContentAndDataUriExtensionsReachTheStreamContract()
	{
		using var leaf = new TestClient();
		var binary = new DataContent(new byte[] { 1, 2, 3 }, "image/png");
		await leaf.ExtractAsync(binary);
		Assert.Equal("image/png", leaf.MediaType);
		Assert.Equal([1, 2, 3], leaf.Bytes);
		await leaf.ExtractAsync(new UriContent("data:image/png;base64,AQID", "image/png"));
		Assert.Equal([1, 2, 3], leaf.Bytes);
		await Assert.ThrowsAsync<NotSupportedException>(async () =>
			await leaf.ExtractAsync(new UriContent("https://example.com/input.png", "image/png")));
	}

	[Fact]
	public async Task OriginalOcrReaderUsesWholePageTextAndDoesNotInventTypedTables()
	{
		using var leaf = new TestClient();
		using var source = new MemoryStream([1]);
		var document = await new OcrDocumentReader(leaf).ReadAsync(source, "document", "image/png");
		Assert.Equal("document", document.Identifier);
		var section = Assert.Single(document.Sections);
		var paragraph = Assert.IsType<IngestionDocumentParagraph>(Assert.Single(section.Elements));
		Assert.Equal("Actual page", paragraph.Text);
		Assert.Equal(1, paragraph.PageNumber);
		Assert.True(source.CanRead);
	}

	[Fact]
	public async Task OriginalPageReducerRetainsTypedPagesAndReportedUsage()
	{
		using var leaf = new TestClient();
		using var source = new MemoryStream([1]);
		var result = await leaf.ExtractPagesAsync(source, "image/png").ToDocumentExtractionResultAsync();
		Assert.Single(result.Pages);
		Assert.Equal(1, result.Usage!.PagesProcessed);
		Assert.Equal(7, result.Usage.InputTokenCount);
		Assert.Equal("Actual page", result.Text);
	}

	private sealed class TestClient : IDocumentExtractionClient
	{
		internal DocumentExtractionOptions? Options { get; private set; }
		internal string? MediaType { get; private set; }
		internal byte[] Bytes { get; private set; } = [];
		internal bool Disposed { get; private set; }

		public async Task<DocumentExtractionResult> ExtractAsync(
			Stream document, string mediaType, DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
		{
			Options = options;
			MediaType = mediaType;
			using var output = new MemoryStream();
			await document.CopyToAsync(output, cancellationToken);
			Bytes = output.ToArray();
			return new DocumentExtractionResult([new DocumentPage(1, "Actual page")
			{
				Elements = [new DocumentTable(1, 1, [new DocumentTableCell(0, 0, "Cell")])],
			}]);
		}

		public async IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
			Stream document, string mediaType, DocumentExtractionOptions? options = null,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			var result = await ExtractAsync(document, mediaType, options, cancellationToken);
			yield return new DocumentExtractionPageResult(result.Pages[0])
			{
				PagesProcessed = 1, TotalPages = 1,
				Usage = new DocumentExtractionUsage { PagesProcessed = 1, InputTokenCount = 7 },
			};
		}

		public object? GetService(Type serviceType, object? serviceKey = null) =>
			serviceKey is not null ? null :
			serviceType == typeof(DocumentExtractionClientMetadata) ? new DocumentExtractionClientMetadata("test") :
			serviceType.IsInstanceOfType(this) ? this : null;

		public void Dispose() => Disposed = true;
	}
}
