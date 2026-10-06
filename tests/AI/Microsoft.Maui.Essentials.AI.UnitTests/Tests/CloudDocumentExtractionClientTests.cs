using System.Net;
using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Core.Pipeline;
using Microsoft.Extensions.DocumentExtraction;
using NSubstitute;
using Xunit;
using ExtractionTable = Microsoft.Extensions.DocumentExtraction.DocumentTable;
using ExtractionCellKind = Microsoft.Extensions.DocumentExtraction.DocumentTableCellKind;

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
            adapter.ExtractAsync(new MemoryStream([1]), "application/pdf", cancellationToken: cancellation.Token));
        var result = AzureDocumentIntelligenceExtractionClient.MapResult(
            DocumentIntelligenceModelFactory.AnalyzeResult(content: "A😀é",
                pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1, spans:
                    [DocumentIntelligenceModelFactory.DocumentSpan(offset: 0, length: 4)])]));
        Assert.Equal("A😀é", result.Text);
    }

    [Fact]
    public async Task Azure_ExtractAndStream_UseModelAndPreserveNativeLayout()
    {
        var native = AzureResult();
        var operation = Substitute.For<Operation<AnalyzeResult>>();
        operation.Value.Returns(native);
        var sdk = Substitute.For<DocumentIntelligenceClient>();
        sdk.AnalyzeDocumentAsync(WaitUntil.Completed, Arg.Any<string>(), Arg.Any<BinaryData>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(operation));
        using var adapter = new AzureDocumentIntelligenceExtractionClient(sdk);
        var metadata = Assert.IsType<DocumentExtractionClientMetadata>(
            adapter.GetService(typeof(DocumentExtractionClientMetadata)));
        Assert.Equal("prebuilt-layout", metadata.DefaultModelId);
        Assert.Same(sdk, adapter.GetService(typeof(DocumentIntelligenceClient)));
        Assert.Same(adapter, adapter.GetService(typeof(IDocumentExtractionClient)));
        Assert.Null(adapter.GetService(typeof(DocumentIntelligenceClient), "key"));
        using var input = new MemoryStream([1, 2]);

        var result = await adapter.ExtractAsync(input, "application/pdf", new() { ModelId = "custom-layout" });

        Assert.True(input.CanRead);
        Assert.Equal("Title A After", result.Text);
        Assert.Null(result.Usage);
        Assert.Null(result.AdditionalProperties);
        Assert.Same(native, result.RawRepresentation);
        var page = Assert.Single(result.Pages);
        Assert.Equal(new DocumentPageDimensions(8, 11), page.Dimensions);
        Assert.Equal(DocumentCoordinateUnit.Inch, page.CoordinateUnit);
        Assert.Equal(DocumentCoordinateOrigin.TopLeft, page.CoordinateOrigin);
        Assert.Equal(3, page.Elements.Count);
        Assert.Equal(DocumentBlockKind.Title, Assert.IsType<DocumentBlock>(page.Elements[0]).Kind);
        var table = Assert.IsType<ExtractionTable>(page.Elements[1]);
        var cell = Assert.Single(table.Cells!);
        Assert.Equal(ExtractionCellKind.Content, cell.Kind);
        Assert.Equal("A", cell.Content);
        Assert.Equal("A", Assert.IsType<DocumentBlock>(Assert.Single(cell.Elements!)).Text);
        Assert.Equal(new DocumentBoundingBox(1, 2, 3, 4), cell.BoundingRegion!.GetBounds());
        Assert.Null(cell.Confidence);
        Assert.Null(cell.AdditionalProperties);
        Assert.All(page.Elements, element => Assert.Null(element.AdditionalProperties));
        await sdk.Received().AnalyzeDocumentAsync(
            WaitUntil.Completed, "custom-layout", Arg.Any<BinaryData>(), Arg.Any<CancellationToken>());

        var streamed = new List<DocumentExtractionPageResult>();
        await foreach (var item in adapter.ExtractPagesAsync(new MemoryStream([1]), "image/png"))
            streamed.Add(item);
        Assert.Equal(1, Assert.Single(streamed).PagesProcessed);
        Assert.Equal(1, streamed[0].TotalPages);
        Assert.Null(streamed[0].Usage);
        Assert.Equal(result.Text, streamed[0].Page.Text);
    }

    [Fact]
    public void Azure_AbsentCellKindAndUnknownParagraphRole_AreNotInvented()
    {
        var native = DocumentIntelligenceModelFactory.AnalyzeResult(
            content: "Heading Value",
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1)],
            paragraphs: [DocumentIntelligenceModelFactory.DocumentParagraph(content: "Heading", role: ParagraphRole.SectionHeading)],
            tables: [DocumentIntelligenceModelFactory.DocumentTable(rowCount: 1, columnCount: 1,
                cells: [DocumentIntelligenceModelFactory.DocumentTableCell(content: "Value")])]);
        var page = Assert.Single(AzureDocumentIntelligenceExtractionClient.MapResult(native).Pages);
        Assert.Null(Assert.IsType<DocumentBlock>(page.Elements[0]).Kind);
        Assert.Null(Assert.Single(Assert.IsType<ExtractionTable>(page.Elements[1]).Cells!).Kind);
    }

    [Fact]
    public async Task Foundry_UsesSharedRequestAndDeploymentOverride_WithNoPropertyBags()
    {
        using var http = Http((request, _) =>
        {
            Assert.Equal("/resource/providers/mistral/azure/ocr", request.RequestUri!.AbsolutePath);
            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("override-deployment", payload.RootElement.GetProperty("model").GetString());
            Assert.False(payload.RootElement.GetProperty("include_image_base64").GetBoolean());
            Assert.Equal("html", payload.RootElement.GetProperty("table_format").GetString());
            Assert.True(payload.RootElement.GetProperty("include_blocks").GetBoolean());
            return Ok(MistralResponse);
        });
        using var adapter = new FoundryMistralDocumentExtractionClient(http, "configured");
        using var input = new MemoryStream([1]);
        var result = await adapter.ExtractAsync(input, "application/pdf", new() { ModelId = "override-deployment" });

        Assert.True(input.CanRead);
        Assert.Same(http, adapter.GetService(typeof(HttpClient)));
        Assert.Same(adapter, adapter.GetService(typeof(IDocumentExtractionClient)));
        var metadata = Assert.IsType<DocumentExtractionClientMetadata>(
            adapter.GetService(typeof(DocumentExtractionClientMetadata)));
        Assert.Equal("configured", metadata.DefaultModelId);
        Assert.Equal(http.BaseAddress, metadata.ProviderUri);
        Assert.Null(adapter.GetService(typeof(HttpClient), "key"));
        Assert.Null(result.AdditionalProperties);
        Assert.Equal(2, result.Usage!.PagesProcessed);
        var page = result.Pages[0];
        Assert.Equal(1, page.PageNumber);
        Assert.Equal("# Title\nText\n<table>native</table>", page.Text);
        Assert.Equal(DocumentBlockKind.Title, Assert.IsType<DocumentBlock>(page.Elements[0]).Kind);
        Assert.Equal(DocumentBlockKind.Paragraph, Assert.IsType<DocumentBlock>(page.Elements[1]).Kind);
        var table = Assert.IsType<ExtractionTable>(page.Elements[2]);
        Assert.Equal(0, table.RowCount);
        Assert.Equal(0, table.ColumnCount);
        Assert.Null(table.Cells);
        Assert.Equal("<table><tr><td>Native</td></tr></table>", table.MarkdownRepresentation);
        Assert.Null(table.BoundingRegion);
        Assert.Null(table.Confidence);
        var image = Assert.IsType<DocumentImage>(page.Elements[3]);
        Assert.Equal("Actual caption", image.Caption);
        Assert.Equal("image/png", image.Content!.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, image.Content.Data.ToArray());
        Assert.Equal(new DocumentBoundingBox(1, 2, 10, 20), image.BoundingRegion!.GetBounds());
        Assert.Equal(new DocumentPageDimensions(600, 800), page.Dimensions);
        Assert.Equal(DocumentCoordinateUnit.Pixel, page.CoordinateUnit);
        Assert.Equal(3, result.Pages[1].PageNumber);
        Assert.All(page.Elements, element => { Assert.Null(element.AdditionalProperties); Assert.Null(element.Confidence); });
        var raw = Assert.IsType<JsonElement>(result.RawRepresentation);
        Assert.Equal("unknown", raw.GetProperty("pages")[0].GetProperty("blocks")[4].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Foundry_Stream_ReportsEnumerationProgress_NotPageIndexOrInventedUsage()
    {
        using var http = Http((_, _) => Ok(MistralResponse));
        using var adapter = new FoundryMistralDocumentExtractionClient(http, "configured");
        var pages = new List<DocumentExtractionPageResult>();
        await foreach (var item in adapter.ExtractPagesAsync(new MemoryStream([1]), "image/tiff"))
            pages.Add(item);
        Assert.Equal([1, 2], pages.Select(item => item.PagesProcessed).ToArray());
        Assert.Equal([1, 3], pages.Select(item => item.Page.PageNumber).ToArray());
        Assert.All(pages, item => { Assert.Equal(2, item.TotalPages); Assert.Null(item.AdditionalProperties); });
        Assert.Null(pages[0].Usage);
        Assert.Equal(2, pages[1].Usage!.PagesProcessed);
        using var noUsage = JsonDocument.Parse("""{"pages":[{"index":0,"markdown":"Only"}]}""");
        var result = FoundryMistralDocumentExtractionClient.MapResult(noUsage.RootElement);
        Assert.Null(result.Usage);
        Assert.Empty(Assert.Single(result.Pages).Elements);
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
            await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ExtractAsync(oversized, "image/jpeg"));
            Assert.True(oversized.CanRead);
            using var input = new MemoryStream([1]);
            await Assert.ThrowsAsync<NotSupportedException>(() => adapter.ExtractAsync(input, "text/plain"));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                adapter.ExtractAsync(input, "application/pdf", cancellationToken: cancelled.Token));
            Assert.True(input.CanRead);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                adapter.ExtractAsync(input, "application/pdf", new() { ModelId = "" }));
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
            adapter.ExtractAsync(new MemoryStream([1]), "image/png", cancellationToken: cancelled.Token));
        Assert.Equal(1, requests);
        using var errors = Http((_, _) => new(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "SECRET",
            Content = new StringContent("SECRET"),
        });
        using var failing = new FoundryMistralDocumentExtractionClient(errors, "deployment");
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            failing.ExtractAsync(new MemoryStream([1]), "image/png"));
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
                adapter.ExtractAsync(new MemoryStream([1]), "application/pdf"));
            Assert.DoesNotContain("SECRET", error.ToString());
        }

        using var large = Http((_, _) => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[32 * 1024 * 1024 + 1]),
        });
        using var bounded = new FoundryMistralDocumentExtractionClient(large, "deployment");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            bounded.ExtractAsync(new MemoryStream([1]), "application/pdf"));
    }

    [Fact]
    public async Task Dispose_DefaultLeavesHttpClientAlive_ExplicitOwnershipDisposesIt()
    {
        using var http = Http((_, _) => Ok(MistralResponse));
        var adapter = new FoundryMistralDocumentExtractionClient(http, "deployment");
        adapter.Dispose();
        adapter.Dispose();
        Assert.Throws<ObjectDisposedException>(() => adapter.GetService(typeof(HttpClient)));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => adapter.ExtractAsync(new MemoryStream(), "image/png"));
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
        var error = await Assert.ThrowsAsync<RequestFailedException>(() => adapter.ExtractAsync(input, "application/pdf"));
        Assert.Equal(400, error.Status);
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Foundry_StreamCancellationBetweenPages_StopsEnumeration()
    {
        using var http = Http((_, _) => Ok(MistralResponse));
        using var adapter = new FoundryMistralDocumentExtractionClient(http, "deployment");
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = adapter.ExtractPagesAsync(new MemoryStream([1]), "application/pdf",
            cancellationToken: cancellation.Token).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
    }

    [Fact]
    public void Azure_Figure_MapsOnlyReturnedCaptionAndGeometry()
    {
        var figure = DocumentIntelligenceModelFactory.DocumentFigure(
            caption: DocumentIntelligenceModelFactory.DocumentCaption(content: "Native caption"),
            boundingRegions: [DocumentIntelligenceModelFactory.BoundingRegion(pageNumber: 1, polygon: [1, 2, 3, 2, 3, 4, 1, 4])]);
        var result = AzureDocumentIntelligenceExtractionClient.MapResult(DocumentIntelligenceModelFactory.AnalyzeResult(
            content: "", pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1)], figures: [figure]));
        var image = Assert.IsType<DocumentImage>(Assert.Single(Assert.Single(result.Pages).Elements));
        Assert.Equal("Native caption", image.Caption);
        Assert.Equal(new DocumentBoundingBox(1, 2, 3, 4), image.BoundingRegion!.GetBounds());
        Assert.Null(image.Content);
        Assert.Null(image.Confidence);
        Assert.Same(figure, image.RawRepresentation);
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
