using System.Net;
using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Core.Pipeline;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using NSubstitute;
using Xunit;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class CloudDocumentExtractionClientTests
{
    [Fact]
    public async Task Azure_SdkTransportUsesUtf16Spans_AndPreservesUnicode()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = Http((request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("stringIndexType=utf16CodeUnit", request.RequestUri!.Query);
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Expected cancellation.");
        });
        var sdk = new DocumentIntelligenceClient(new Uri("https://example.invalid"), new AzureKeyCredential("test"),
            new DocumentIntelligenceClientOptions { Transport = new HttpClientTransport(http) });
        using var adapter = new AzureDocumentIntelligenceExtractionClient(sdk);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.ExtractAsync(new MemoryStream([1]), "input", "application/pdf", cancellation.Token));
        var page = Assert.Single(AzureDocumentIntelligenceExtractionClient.MapPages(
            DocumentIntelligenceModelFactory.AnalyzeResult(content: "A😀é",
                contentFormat: DocumentContentFormat.Markdown,
                pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1, spans:
                    [DocumentIntelligenceModelFactory.DocumentSpan(offset: 0, length: 4)])])));
        Assert.Equal("A😀é", page.Markdown);
    }

    [Fact]
    public async Task Azure_ExtractAndStream_UseConfiguredModelAndSupportedNativeContent()
    {
        var native = AzureResult();
        var operation = Substitute.For<Operation<AnalyzeResult>>();
        operation.Value.Returns(native);
        var sdk = Substitute.For<DocumentIntelligenceClient>();
        sdk.AnalyzeDocumentAsync(WaitUntil.Completed, Arg.Any<string>(), Arg.Any<BinaryData>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(operation));
        using var adapter = new AzureDocumentIntelligenceExtractionClient(sdk, "custom-layout");
        Assert.Same(sdk, adapter.GetService(typeof(DocumentIntelligenceClient)));
        Assert.Same(adapter, adapter.GetService(typeof(IDocumentExtractionClient)));
        Assert.Null(adapter.GetService(typeof(DocumentIntelligenceClient), "key"));
        using var input = new MemoryStream([1, 2]);

        using var result = await adapter.ExtractAsync(input, "identity", "application/pdf");

        Assert.True(input.CanRead);
        Assert.Equal("identity", result.Document.Identifier);
        Assert.False(result.Document.HasMetadata);
        Assert.Empty(result.Document.Artifacts);
        Assert.Empty(result.Document.Evidence);
        var page = Assert.Single(result.Pages);
        Assert.Null(page.Markdown);
        Assert.Equal(3, page.Observations.Count);
        var title = Assert.IsType<DocumentExtractionText>(page.Observations[0]);
        Assert.Equal(DocumentTextKind.Header, title.Kind);
        Assert.Null(title.HeaderLevel);
        var table = Assert.IsType<DocumentExtractionTable>(page.Observations[1]);
        var cell = Assert.Single(table.Cells);
        Assert.Equal("A", Assert.IsType<DocumentExtractionText>(cell.Content).Text);
        Assert.Equal("After", Assert.IsType<DocumentExtractionText>(page.Observations[2]).Text);
        Assert.Equal([0, 1, 2], page.Observations.Select(observation => observation.Order));
        Assert.All(page.Observations, AssertNoOpaqueData);
        AssertNoOpaqueData(cell.Content);
        Assert.Equal("Title", result.Document.Sections[0].Elements[0].Text);
        Assert.IsType<DocumentTableElement>(result.Document.Sections[0].Elements[1]);
        Assert.Equal("After", result.Document.Sections[0].Elements[2].Text);
        await sdk.Received().AnalyzeDocumentAsync(
            WaitUntil.Completed, "custom-layout", Arg.Any<BinaryData>(), Arg.Any<CancellationToken>());

        var streamed = new List<DocumentExtractionPage>();
        using var streamInput = new MemoryStream([1]);
        await foreach (var item in adapter.ExtractPagesAsync(streamInput, "stream", "image/png"))
            streamed.Add(item);
        Assert.Equal(1, Assert.Single(streamed).PageNumber);
        Assert.Equal(3, streamed[0].Observations.Count);
        Assert.True(streamInput.CanRead);
    }

    [Fact]
    public void Azure_ActualSectionHeadingHasNoInventedLevelOrCellRoles()
    {
        var native = DocumentIntelligenceModelFactory.AnalyzeResult(
            content: "Heading Value",
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1)],
            paragraphs: [DocumentIntelligenceModelFactory.DocumentParagraph(content: "Heading", role: ParagraphRole.SectionHeading)],
            tables: [DocumentIntelligenceModelFactory.DocumentTable(rowCount: 1, columnCount: 1,
                cells: [DocumentIntelligenceModelFactory.DocumentTableCell(content: "Value")])]);
        var page = Assert.Single(AzureDocumentIntelligenceExtractionClient.MapPages(native));
        var header = Assert.IsType<DocumentExtractionText>(page.Observations[0]);
        Assert.Equal(DocumentTextKind.Header, header.Kind);
        Assert.Null(header.HeaderLevel);
        var cell = Assert.Single(Assert.IsType<DocumentExtractionTable>(page.Observations[1]).Cells);
        Assert.Equal("Value", Assert.IsType<DocumentExtractionText>(cell.Content).Text);
        AssertNoOpaqueData(cell.Content);
    }

    [Fact]
    public async Task Foundry_UsesSharedRequestAndConfiguredDeployment_WithNoOpaqueEscapes()
    {
        using var http = Http((request, _) =>
        {
            Assert.Equal("/resource/providers/mistral/azure/ocr", request.RequestUri!.AbsolutePath);
            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("configured", payload.RootElement.GetProperty("model").GetString());
            Assert.False(payload.RootElement.GetProperty("include_image_base64").GetBoolean());
            Assert.Equal("html", payload.RootElement.GetProperty("table_format").GetString());
            Assert.True(payload.RootElement.GetProperty("include_blocks").GetBoolean());
            return Ok(MistralResponse);
        });
        using var adapter = new FoundryMistralDocumentExtractionClient(http, "configured");
        using var input = new MemoryStream([1]);
        using var result = await adapter.ExtractAsync(input, "input.pdf", "application/pdf");

        Assert.True(input.CanRead);
        Assert.Same(http, adapter.GetService(typeof(HttpClient)));
        Assert.Same(adapter, adapter.GetService(typeof(IDocumentExtractionClient)));
        Assert.Null(adapter.GetService(typeof(HttpClient), "key"));
        Assert.Equal("input.pdf", result.Document.Identifier);
        Assert.False(result.Document.HasMetadata);
        Assert.Empty(result.Document.Artifacts);
        Assert.Empty(result.Document.Evidence);
        var page = result.Pages[0];
        Assert.Equal(1, page.PageNumber);
        Assert.Equal("# Title\nText\n<table>native</table>", page.Markdown);
        var title = Assert.IsType<DocumentExtractionText>(page.Observations[0]);
        Assert.Equal(DocumentTextKind.Header, title.Kind);
        Assert.Null(title.HeaderLevel);
        Assert.Equal(DocumentTextKind.Paragraph, Assert.IsType<DocumentExtractionText>(page.Observations[1]).Kind);
        var table = Assert.IsType<DocumentExtractionTable>(page.Observations[2]);
        Assert.Equal(0, table.RowCount);
        Assert.Equal(0, table.ColumnCount);
        Assert.Empty(table.Cells);
        Assert.Equal("<table><tr><td>Native</td></tr></table>", table.Markdown);
        var image = Assert.IsType<DocumentExtractionImage>(page.Observations[3]);
        Assert.Equal("Actual caption", image.Text);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, image.Content!.Value.ToArray());
        Assert.Equal(3, result.Pages[1].PageNumber);
        Assert.All(page.Observations, AssertNoOpaqueData);
        Assert.Equal(4, page.Observations.Count);
        Assert.Equal(4, page.Observations.Select(observation => observation.ProviderId).Distinct().Count());
    }

    [Fact]
    public async Task Foundry_Stream_ReturnsActualPageNumbersAndExactMarkdown()
    {
        using var http = Http((_, _) => Ok(MistralResponse));
        using var adapter = new FoundryMistralDocumentExtractionClient(http, "configured");
        var pages = new List<DocumentExtractionPage>();
        using var input = new MemoryStream([1]);
        await foreach (var item in adapter.ExtractPagesAsync(input, "input", "image/tiff"))
            pages.Add(item);
        Assert.Equal([1, 3], pages.Select(item => item.PageNumber).ToArray());
        Assert.Equal("Third", pages[1].Markdown);
        Assert.All(pages, item => Assert.Empty(item.PageReferences));
        using var noUsage = JsonDocument.Parse("""{"pages":[{"index":0,"markdown":"Only"}]}""");
        var page = Assert.Single(FoundryMistralDocumentExtractionClient.MapPages(noUsage.RootElement));
        Assert.Empty(page.Observations);
        Assert.Equal("Only", page.Markdown);
    }

    [Fact]
    public async Task Adapters_InputLimitsAndCancellation_KeepCallerStreamsOpen()
    {
        var requests = 0;
        using var http = Http((_, _) => { requests++; return Ok(MistralResponse); });
        using var foundry = new FoundryMistralDocumentExtractionClient(http, "deployment");
        using var azure = new AzureDocumentIntelligenceExtractionClient(Substitute.For<DocumentIntelligenceClient>());
        foreach (IDocumentExtractionClient adapter in new IDocumentExtractionClient[] { foundry, azure })
        {
            using var oversized = new MemoryStream(new byte[20 * 1024 * 1024 + 1]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ExtractAsync(oversized, "input", "image/jpeg"));
            Assert.True(oversized.CanRead);
            using var input = new MemoryStream([1]);
            await Assert.ThrowsAsync<NotSupportedException>(() => adapter.ExtractAsync(input, "input", "text/plain"));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                adapter.ExtractAsync(input, "input", "application/pdf", cancelled.Token));
            Assert.True(input.CanRead);
            await Assert.ThrowsAnyAsync<ArgumentException>(() =>
                adapter.ExtractAsync(input, "", "application/pdf"));
        }
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task Foundry_RetryCancellationAndSafeErrors_ArePreserved()
    {
        using var cancelled = new CancellationTokenSource();
        var requests = 0;
        using var retry = Http((_, _) =>
        {
            requests++;
            cancelled.Cancel();
            return new(HttpStatusCode.ServiceUnavailable);
        });
        using var adapter = new FoundryMistralDocumentExtractionClient(retry, "deployment");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.ExtractAsync(new MemoryStream([1]), "input", "image/png", cancelled.Token));
        Assert.Equal(1, requests);
        using var errors = Http((_, _) => new(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "SECRET",
            Content = new StringContent("SECRET"),
        });
        using var failing = new FoundryMistralDocumentExtractionClient(errors, "deployment");
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            failing.ExtractAsync(new MemoryStream([1]), "input", "image/png"));
        Assert.Contains("400", exception.Message);
        Assert.DoesNotContain("SECRET", exception.ToString());
    }

    [Fact]
    public async Task Foundry_InvalidAndOversizedResponses_RejectSafely()
    {
        foreach (var response in new[] { "SECRET-invalid", """{"pages":[{"index":0},{"index":0}]}""" })
        {
            using var http = Http((_, _) => Ok(response));
            using var adapter = new FoundryMistralDocumentExtractionClient(http, "deployment");
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                adapter.ExtractAsync(new MemoryStream([1]), "input", "application/pdf"));
            Assert.DoesNotContain("SECRET", error.ToString());
        }

        using var large = Http((_, _) => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[32 * 1024 * 1024 + 1]),
        });
        using var bounded = new FoundryMistralDocumentExtractionClient(large, "deployment");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            bounded.ExtractAsync(new MemoryStream([1]), "input", "application/pdf"));
    }

    [Fact]
    public async Task Dispose_DefaultLeavesHttpClientAlive_ExplicitOwnershipDisposesIt()
    {
        using var http = Http((_, _) => Ok(MistralResponse));
        var adapter = new FoundryMistralDocumentExtractionClient(http, "deployment");
        adapter.Dispose();
        adapter.Dispose();
        Assert.Throws<ObjectDisposedException>(() => adapter.GetService(typeof(HttpClient)));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => adapter.ExtractAsync(new MemoryStream(), "input", "image/png"));
        http.DefaultRequestHeaders.Add("still-alive", "yes");
        var owned = Http((_, _) => Ok(MistralResponse));
        new FoundryMistralDocumentExtractionClient(owned, "deployment", ownsHttpClient: true).Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => owned.GetAsync("ocr"));
    }

    [Fact]
    public async Task Azure_ServiceFailure_PropagatesWithoutTakingStreamOwnership()
    {
        var sdk = Substitute.For<DocumentIntelligenceClient>();
        sdk.AnalyzeDocumentAsync(WaitUntil.Completed, Arg.Any<string>(), Arg.Any<BinaryData>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Operation<AnalyzeResult>>(new RequestFailedException(400, "Invalid document")));
        using var adapter = new AzureDocumentIntelligenceExtractionClient(sdk);
        using var input = new MemoryStream([1]);
        var error = await Assert.ThrowsAsync<RequestFailedException>(() => adapter.ExtractAsync(input, "input", "application/pdf"));
        Assert.Equal(400, error.Status);
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Foundry_StreamCancellationBetweenPages_StopsEnumeration()
    {
        using var http = Http((_, _) => Ok(MistralResponse));
        using var adapter = new FoundryMistralDocumentExtractionClient(http, "deployment");
        using var cancellation = new CancellationTokenSource();
        using var input = new MemoryStream([1]);
        await using var enumerator = adapter.ExtractPagesAsync(input, "input", "application/pdf",
            cancellationToken: cancellation.Token).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
    }

    [Fact]
    public void Azure_FigureWithoutNativeImageContent_IsNotInventedAsAnImage()
    {
        var figure = DocumentIntelligenceModelFactory.DocumentFigure(
            caption: DocumentIntelligenceModelFactory.DocumentCaption(content: "Native caption"),
            boundingRegions: [DocumentIntelligenceModelFactory.BoundingRegion(pageNumber: 1, polygon: [1, 2, 3, 2, 3, 4, 1, 4])]);
        var pages = AzureDocumentIntelligenceExtractionClient.MapPages(DocumentIntelligenceModelFactory.AnalyzeResult(
            content: "", pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1)], figures: [figure]));
        Assert.Empty(Assert.Single(pages).Observations);
    }

    private static void AssertNoOpaqueData(DocumentExtractionObservation observation)
    {
        Assert.Empty(observation.PageReferences);
        Assert.Empty(observation.Evidence);
        Assert.Empty(observation.Artifacts);
    }

    [Fact]
    public void Azure_MergedCellsKeepAnchoredContentWithoutRepeatedCoveredCells()
    {
        var native = DocumentIntelligenceModelFactory.AnalyzeResult(
            content: "Merged",
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1)],
            tables: [DocumentIntelligenceModelFactory.DocumentTable(rowCount: 2, columnCount: 2,
                cells: [DocumentIntelligenceModelFactory.DocumentTableCell(content: "Merged", rowIndex: 0, columnIndex: 0,
                    rowSpan: 2, columnSpan: 2)])]);
        using var result = AzureDocumentIntelligenceExtractionClient.MapPages(native).ToDocumentExtractionResult("identity");
        var table = Assert.IsType<DocumentTableElement>(Assert.Single(result.Document.Sections[0].Elements));
        Assert.Equal("Merged", table.Cells[0, 0]!.Content.Text);
        Assert.Null(table.Cells[0, 1]);
        Assert.Null(table.Cells[1, 0]);
        Assert.Null(table.Cells[1, 1]);
        Assert.False(table.HasMetadata);
        Assert.DoesNotContain("rowspan", table.GetMarkdown());
        Assert.DoesNotContain("colspan", table.GetMarkdown());
    }

    [Fact]
    public void Foundry_RepeatedNativeIdsAcrossPagesDoNotCollideInCanonicalDocument()
    {
        using var json = JsonDocument.Parse("""
            {"pages":[
            {"index":0,"tables":[{"id":"t","content":"<table>First</table>"}],
                "blocks":[{"type":"title","content":"Title"}]},
            {"index":1,"tables":[{"id":"t","content":"<table>Second</table>"}],
                "blocks":[{"type":"title","content":"Title"}]}]}
            """);
        var pages = FoundryMistralDocumentExtractionClient.MapPages(json.RootElement);
        using var result = pages.ToDocumentExtractionResult("identity");
        Assert.Equal(2, result.Document.Sections.Count);
        var ids = pages.SelectMany(page => page.Observations).Select(item => item.ProviderId).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void Foundry_ImagesWithoutNativeMarkdownOrCaptionAreNotGivenInventedRepresentations()
    {
        using var json = JsonDocument.Parse("""
            {"pages":[{"index":0,"markdown":"Actual page",
            "images":[{"id":"i","image_base64":"data:image/png;base64,AQID"}],
            "blocks":[{"type":"image","image_id":"i"},{"type":"text","content":""}]}]}
            """);
        var page = Assert.Single(FoundryMistralDocumentExtractionClient.MapPages(json.RootElement));
        Assert.Empty(page.Observations);
        Assert.Equal("Actual page", page.Markdown);
        Assert.Empty(page.PageReferences);
    }

    [Fact]
    public void Foundry_ActualMarginsKeepTheirReportedKindsWithoutGuessedHierarchy()
    {
        using var json = JsonDocument.Parse("""{"pages":[{"index":0,"header":"Header","footer":"Footer"}]}""");
        var page = Assert.Single(FoundryMistralDocumentExtractionClient.MapPages(json.RootElement));
        var header = Assert.IsType<DocumentExtractionText>(page.Observations[0]);
        Assert.Equal(DocumentTextKind.Header, header.Kind);
        Assert.Null(header.HeaderLevel);
        Assert.Equal(DocumentTextKind.Footer, Assert.IsType<DocumentExtractionText>(page.Observations[1]).Kind);
        Assert.Null(page.Markdown);
    }

    private static AnalyzeResult AzureResult()
    {
        static DocumentSpan Span(int offset, int length) =>
            DocumentIntelligenceModelFactory.DocumentSpan(offset: offset, length: length);
        return DocumentIntelligenceModelFactory.AnalyzeResult(
            content: "Title A After",
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1, width: 8, height: 11,
                unit: LengthUnit.Inch, spans: [Span(0, 13)])],
            paragraphs:
            [
                DocumentIntelligenceModelFactory.DocumentParagraph(content: "After", spans: [Span(8, 5)]),
                DocumentIntelligenceModelFactory.DocumentParagraph(content: "A", spans: [Span(6, 1)]),
                DocumentIntelligenceModelFactory.DocumentParagraph(content: "Title", role: ParagraphRole.Title, spans: [Span(0, 5)]),
            ],
            tables: [DocumentIntelligenceModelFactory.DocumentTable(rowCount: 1, columnCount: 1,
                spans: [Span(6, 1)],
                cells: [DocumentIntelligenceModelFactory.DocumentTableCell(content: "A",
                    kind: Azure.AI.DocumentIntelligence.DocumentTableCellKind.Content,
                    spans: [Span(6, 1)], boundingRegions:
                    [DocumentIntelligenceModelFactory.BoundingRegion(pageNumber: 1, polygon: [1, 2, 3, 2, 3, 4, 1, 4])])])]);
    }

    private const string MistralResponse = """
        {"pages":[{"index":0,"markdown":"# Title\nText\n<table>native</table>",
          "dimensions":{"width":600,"height":800,"dpi":72},
          "blocks":[{"type":"title","content":"Title"},{"type":"text","content":"Text"},
                    {"type":"table","table_id":"t"},{"type":"image","image_id":"i"},
                    {"type":"unknown","content":"Do not invent a kind"}],
          "tables":[{"id":"t","content":"<table><tr><td>Native</td></tr></table>"}],
          "images":[{"id":"i","image_base64":"data:image/png;base64,AQID","caption":"Actual caption",
                     "top_left_x":1,"top_left_y":2,"bottom_right_x":10,"bottom_right_y":20}]},
          {"index":2,"markdown":"Third"}],"usage_info":{"pages_processed":2,"doc_size_bytes":123}}
        """;

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private static HttpClient Http(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) =>
        new(new Handler(respond)) { BaseAddress = new Uri("https://example.invalid/resource/") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request, cancellationToken));
    }
}
