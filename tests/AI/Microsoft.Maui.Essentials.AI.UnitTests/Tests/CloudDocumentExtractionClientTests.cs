using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Core.Pipeline;
using CommunityToolkit.DocumentExtraction.Mistral;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using NSubstitute;
using Xunit;
using static AIExtensions.Sample.ChatPlayground.CloudDocumentTestFixtures;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class CloudDocumentExtractionClientTests
{
    [Fact]
    public async Task Azure_ToolkitUsesSdkOptionsAndPreservesUnicodeNativeLayoutAndCells()
    {
        var paragraph = DocumentIntelligenceModelFactory.DocumentParagraph(content: "A😀é", role: ParagraphRole.Title,
            spans: [Span(0, 4)], boundingRegions: [Region(1)]);
        var native = DocumentIntelligenceModelFactory.AnalyzeResult(content: "A😀é Value",
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1, width: 8, height: 11,
                unit: LengthUnit.Inch, spans: [Span(0, 10)])],
            paragraphs: [paragraph],
            tables: [DocumentIntelligenceModelFactory.DocumentTable(rowCount: 2, columnCount: 2,
                boundingRegions: [Region(1)], spans: [Span(5, 5)], cells:
                [DocumentIntelligenceModelFactory.DocumentTableCell(content: "Value", columnSpan: 2,
                    kind: DocumentTableCellKind.Content, boundingRegions: [Region(1)]),
                 DocumentIntelligenceModelFactory.DocumentTableCell(rowIndex: 1, content: "", boundingRegions: [Region(1)])])]);
        var sdk = AzureSdk((options, token) =>
        {
            Assert.Equal("custom-layout", options.ModelId);
            Assert.Equal(DocumentContentFormat.Markdown, options.OutputContentFormat);
            Assert.Equal(new byte[] { 1, 2 }, options.BytesSource.ToArray());
            Assert.Equal("en-US", options.Locale);
            Assert.Equal("1", options.Pages);
            Assert.True(token.CanBeCanceled);
            return Task.FromResult(native);
        });
        using var client = new AzureDocumentIntelligenceExtractionClient(sdk);
        using var input = new MemoryStream([1, 2]);
        var options = new DocumentExtractionOptions { ExtractorName = "custom-layout" };
        options.AdditionalProperties["locale"] = "en-US";
        options.AdditionalProperties["pages"] = "1";
        var result = await client.ExtractAsync(input, "source", "application/pdf", options);

        Assert.True(input.CanRead);
        Assert.Same(sdk, client.GetService(typeof(DocumentIntelligenceClient)));
        Assert.Null(client.GetService(typeof(DocumentIntelligenceClient), "key"));
        Assert.Same(native, result.RawRepresentation);
        Assert.Empty(result.Metadata);
        var frame = Assert.Single(result.SourcePages);
        Assert.Equal(new ExtractionDocumentPageDimensions(8, 11), frame.Dimensions);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Inch, frame.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.TopLeft, frame.CoordinateOrigin);
        var title = Assert.IsType<ExtractionDocumentTextElement>(result.Sections[0].Elements[0]);
        Assert.Equal("A😀é", Assert.IsType<TextContent>(title.Content).Text);
        Assert.Equal("heading", title.Role);
        Assert.Null(title.HeadingLevel);
        Assert.Same(paragraph, title.Sources[0].GetRawRepresentation());
        var table = Assert.IsType<ExtractionDocumentTableElement>(result.Sections[0].Elements[1]);
        Assert.Equal(2, table.Cells[0, 0]!.ColumnSpan);
        Assert.Null(table.Cells[0, 1]);
        Assert.Equal("content", table.Cells[0, 0]!.Role);
        Assert.Equal("Value", Assert.IsType<TextContent>(table.Cells[0, 0]!.Content!.Content).Text);
        Assert.Same(frame, table.Cells[0, 0]!.Sources[0].Page);
        Assert.Equal(new ExtractionDocumentPoint(1, 2), table.Cells[0, 0]!.Sources[0].Polygon[0]);
        Assert.IsType<DocumentTableCell>(table.Cells[0, 0]!.Sources[0].GetRawRepresentation());
        Assert.Equal("", Assert.IsType<TextContent>(table.Cells[1, 0]!.Content!.Content).Text);
    }

    [Fact]
    public async Task Azure_TransportUsesOfficialSdkWireRequestAndPropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = Http((request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("prebuilt-layout:analyze", request.RequestUri!.AbsolutePath);
            Assert.Contains("outputContentFormat=markdown", request.RequestUri.Query);
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("Ocp-Apim-Subscription-Key")));
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Ok("{}");
        });
        var sdk = new DocumentIntelligenceClient(new Uri("https://example.invalid"), new AzureKeyCredential("test-key"),
            new DocumentIntelligenceClientOptions { Transport = new HttpClientTransport(http) });
        using var client = new AzureDocumentIntelligenceExtractionClient(sdk);
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ExtractAsync(input, "source", "application/pdf", cancellationToken: cancellation.Token));
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Azure_FiguresAndPageObservationsRetainNativeSources()
    {
        var figure = DocumentIntelligenceModelFactory.DocumentFigure(
            caption: DocumentIntelligenceModelFactory.DocumentCaption(content: "Caption"),
            boundingRegions: [Region(1), Region(3)]);
        var page = DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1,
            words: [DocumentIntelligenceModelFactory.DocumentWord(content: "Word", confidence: 0.81f)],
            selectionMarks: [DocumentIntelligenceModelFactory.DocumentSelectionMark(state: DocumentSelectionMarkState.Selected,
                confidence: 0.72f, polygon: [1, 0, 2, 0, 2, 1])],
            barcodes: [DocumentIntelligenceModelFactory.DocumentBarcode(kind: DocumentBarcodeKind.QrCode,
                value: "code", confidence: 0.91f, polygon: [2, 0, 3, 0, 3, 1])]);
        using var client = new AzureDocumentIntelligenceExtractionClient(AzureSdk(
            DocumentIntelligenceModelFactory.AnalyzeResult(pages: [page,
                DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 3)], figures: [figure])));
        using var input = new MemoryStream([1]);
        var result = await client.ExtractAsync(input, "source", "image/png");
        var image = Assert.IsType<ExtractionDocumentImageElement>(Assert.Single(result.Sections[0].Elements));
        Assert.Equal("Caption", image.AlternativeText);
        Assert.Null(image.Content);
        Assert.Null(image.Text);
        Assert.Equal([1, 3], image.Sources.Select(source => source.Page.PageNumber));
        Assert.All(image.Sources, source => Assert.Same(figure, source.GetRawRepresentation()));
        Assert.Same(page, result.Sections[0].Sources[0].GetRawRepresentation());
        Assert.Equal("Word", page.Words[0].Content);
        Assert.Equal(3, result.Sections[0].Sources.Count);
    }

    [Fact]
    public async Task Foundry_ExactRouteBearerHeaderOptionsAndDetachedImagePayload()
    {
        var content = new TrackingContent(MistralResponse);
        using var http = Http(async (request, token) =>
        {
            Assert.Equal(OcrEndpoint, request.RequestUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-key", request.Headers.Authorization.Parameter);
            Assert.False(request.Headers.Contains("api-key"));
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(token));
            Assert.Equal("override", body.RootElement.GetProperty("model").GetString());
            Assert.True(body.RootElement.GetProperty("include_image_base64").GetBoolean());
            var document = body.RootElement.GetProperty("document");
            Assert.Equal("document_url", document.GetProperty("type").GetString());
            Assert.Equal("data:application/pdf;base64,AQID", document.GetProperty("document_url").GetString());
            Assert.False(body.RootElement.TryGetProperty("include_blocks", out _));
            Assert.False(body.RootElement.TryGetProperty("table_format", out _));
            Assert.False(body.RootElement.TryGetProperty("extract_header", out _));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var client = Mistral(http);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await client.ExtractAsync(input, "source", "application/pdf", new() { ExtractorName = "override" });
        Assert.True(content.Disposed);
        Assert.True(input.CanRead);
        Assert.Null(result.Content);
        Assert.Equal("reported", Assert.IsType<JsonElement>(result.RawRepresentation).GetProperty("model").GetString());
        Assert.Equal([1, 3], result.SourcePages.Select(page => page.PageNumber));
        var elements = result.Sections[0].Elements;
        Assert.Equal("Before\n", Assert.IsType<TextContent>(elements[0].Content).Text);
        var table = Assert.IsType<ExtractionDocumentTableElement>(elements[1]);
        Assert.Equal(0, table.Cells.RowCount);
        Assert.Equal(0, table.Cells.ColumnCount);
        Assert.Equal("<table><tr><td>Native</td></tr></table>", Assert.IsType<TextContent>(table.Content).Text);
        var image = Assert.Single(elements.OfType<ExtractionDocumentImageElement>());
        var data = Assert.IsType<DataContent>(image.Content);
        Assert.Equal("image/png", data.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, data.Data.ToArray());
        Assert.Equal(1.123456789012345, image.Sources[0].Polygon[0].X);
        Assert.Null(image.AlternativeText);
        Assert.Null(image.Text);
        Assert.Equal(new ExtractionDocumentPageDimensions(600, 800), result.SourcePages[0].Dimensions);
        Assert.Equal("Third", Assert.IsType<TextContent>(Assert.Single(result.Sections[1].Elements).Content).Text);
    }

    [Theory]
    [InlineData("image/png", "image_url")]
    [InlineData("application/pdf", "document_url")]
    public async Task Foundry_ToolkitUsesInputSpecificDocumentType(string mediaType, string type)
    {
        using var http = Http(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var document = body.RootElement.GetProperty("document");
            Assert.Equal(type, document.GetProperty("type").GetString());
            Assert.Equal($"data:{mediaType};base64,AQ==", document.GetProperty(type).GetString());
            return Ok("""{"pages":[{"index":0,"markdown":"Body"}]}""");
        });
        using var client = Mistral(http);
        using var input = new MemoryStream([1]);
        await client.ExtractAsync(input, "source", mediaType);
    }

    [Fact]
    public async Task Clients_ConcurrentOverridesAreSnapshottedAndDoNotChangeDefault()
    {
        var azureModels = new ConcurrentBag<string>();
        var mistralModels = new ConcurrentBag<string>();
        using var http = Http(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            mistralModels.Add(body.RootElement.GetProperty("model").GetString()!);
            await Task.Yield();
            return Ok("""{"pages":[{"index":0,"markdown":"Body"}]}""");
        });
        using var mistral = Mistral(http);
        using var azure = new AzureDocumentIntelligenceExtractionClient(AzureSdk(async (options, _) =>
        {
            azureModels.Add(options.ModelId);
            await Task.Yield();
            return DocumentIntelligenceModelFactory.AnalyzeResult(
                pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1)]);
        }));
        foreach (var client in new DocumentExtractionClient[] { azure, mistral })
        {
            var options = new DocumentExtractionOptions { ExtractorName = "first" };
            using var firstInput = new MemoryStream([1]);
            using var secondInput = new MemoryStream([2]);
            var first = client.ExtractAsync(firstInput, "a", "application/pdf", options);
            options.ExtractorName = "changed";
            await Task.WhenAll(first, client.ExtractAsync(secondInput, "b", "application/pdf",
                new() { ExtractorName = "second" }));
            using var thirdInput = new MemoryStream([3]);
            await client.ExtractAsync(thirdInput, "c", "application/pdf");
        }
        Assert.Equal(["first", "prebuilt-layout", "second"], azureModels.Order().ToArray());
        Assert.Equal(["deployment", "first", "second"], mistralModels.Order().ToArray());
    }

    [Fact]
    public async Task Clients_UnsupportedOptionsLimitsMediaAndCancellationKeepCallerStreamsOpen()
    {
        var requests = 0;
        using var http = Http((_, _) => { requests++; return Ok(MistralResponse); });
        using var mistral = Mistral(http);
        using var azure = new AzureDocumentIntelligenceExtractionClient(AzureSdk((_, _) =>
        {
            requests++;
            throw new InvalidOperationException("Unexpected upload");
        }));
        foreach (var client in new DocumentExtractionClient[] { azure, mistral })
        {
            using var input = new MemoryStream([1]);
            var options = new DocumentExtractionOptions();
            options.AdditionalProperties["unknown"] = true;
            await Assert.ThrowsAsync<NotSupportedException>(() => client.ExtractAsync(input, "source", "image/png", options));
            using var oversized = new RepeatingStream(20 * 1024 * 1024 + 1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.ExtractAsync(oversized, "source", "image/jpeg"));
            Assert.True(oversized.CanRead);
            await Assert.ThrowsAsync<NotSupportedException>(() => client.ExtractAsync(input, "source", "text/plain"));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.ExtractAsync(input, "source", "application/pdf", cancellationToken: cancelled.Token));
            Assert.True(input.CanRead);
        }
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task Policy_DeadlineCancelsProviderAndLeavesCallerStreamOpen()
    {
        using var http = Http(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Ok("{}");
        });
        using var provider = new MistralDocumentExtractionClient(
            new() { OcrEndpoint = OcrEndpoint, DefaultModel = "deployment" }, "test-key", http);
        using var policy = new CloudDocumentExtractionPolicy(provider, TimeSpan.FromMilliseconds(50));
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            policy.ExtractAsync(input, "source", "application/pdf"));
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Foundry_ToolkitRetryHonorsCancellationWithoutAdditionalRetryLayer()
    {
        var requests = 0;
        using var cancellation = new CancellationTokenSource();
        using var http = Http((_, _) =>
        {
            requests++;
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("") };
        });
        using var client = Mistral(http);
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ExtractAsync(input, "source", "image/png", cancellationToken: cancellation.Token));
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task Foundry_Toolkit503RetryCanRecoverWithSameBodyAndAuthentication()
    {
        var requests = 0;
        var bodies = new List<string>();
        using var http = Http(async (request, token) =>
        {
            requests++;
            bodies.Add(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("test-key", request.Headers.Authorization!.Parameter);
            return requests == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) :
                Ok("""{"pages":[{"index":0,"markdown":"Recovered"}]}""");
        });
        using var extraction = Mistral(http);
        using var input = new MemoryStream([1]);
        var result = await extraction.ExtractAsync(input, "source", "image/png");
        Assert.Equal(2, requests);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Equal("Recovered", Assert.IsType<TextContent>(Assert.Single(result.Sections[0].Elements).Content).Text);
    }

    [Fact]
    public async Task Azure_SpansOnlyContentRemainsNativeRatherThanBeingRemappedLocally()
    {
        var native = DocumentIntelligenceModelFactory.AnalyzeResult(content: "A😀é",
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1, spans: [Span(0, 4)])]);
        using var extraction = new AzureDocumentIntelligenceExtractionClient(AzureSdk(native));
        using var input = new MemoryStream([1]);
        var result = await extraction.ExtractAsync(input, "source", "application/pdf");
        Assert.Equal("A😀é", Assert.IsType<AnalyzeResult>(result.RawRepresentation).Content);
        Assert.Null(result.Content);
        Assert.Null(result.Sections[0].Content);
        Assert.Empty(result.Sections[0].Elements);
    }

    [Fact]
    public async Task Foundry_EmptyUntypedImageRemainsEmptyPngWithNativeRecord()
    {
        using var http = Http((_, _) => Ok("""{"pages":[{"index":0,"images":[{"id":"empty","image_base64":""}]}]}"""));
        using var extraction = Mistral(http);
        using var input = new MemoryStream([1]);
        var result = await extraction.ExtractAsync(input, "source", "image/png");
        var image = Assert.IsType<ExtractionDocumentImageElement>(Assert.Single(result.Sections[0].Elements));
        var data = Assert.IsType<DataContent>(image.Content);
        Assert.Equal("image/png", data.MediaType);
        Assert.Empty(data.Data.ToArray());
        Assert.Equal("", Assert.IsType<JsonElement>(image.Sources[0].GetRawRepresentation()).GetProperty("image_base64").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("actual-server-model")]
    public async Task Foundry_NativeModelIsOnlyTheActualResponseModel(string? reported)
    {
        using var http = Http((_, _) => Ok(JsonSerializer.Serialize(new
        {
            model = reported,
            pages = new[] { new { index = 0, markdown = "Body" } },
        })));
        using var extraction = Mistral(http, "configured-alias");
        using var input = new MemoryStream([1]);
        var result = await extraction.ExtractAsync(input, "source", "image/png",
            new() { ExtractorName = "request-alias" });
        Assert.Equal(reported, Assert.IsType<JsonElement>(result.RawRepresentation).GetProperty("model").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Foundry_FailureOrCancellationDisposesResponseAndKeepsCallerStreamOpen(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var content = new TrackingContent("invalid JSON SECRET");
        using var http = Http((_, _) =>
        {
            if (cancel)
                cancellation.Cancel();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var client = Mistral(http);
        using var input = new MemoryStream([1]);
        var error = await Record.ExceptionAsync(() =>
            client.ExtractAsync(input, "source", "application/pdf", cancellationToken: cancellation.Token));
        Assert.NotNull(error);
        if (cancel)
            Assert.IsAssignableFrom<OperationCanceledException>(error);
        else
            Assert.IsType<InvalidDataException>(error);
        Assert.DoesNotContain("SECRET", error.ToString());
        Assert.True(content.Disposed);
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Dispose_LeavesBorrowedSdkAndHttpClientUsable()
    {
        using var http = Http((_, _) => Ok(MistralResponse));
        var client = Mistral(http);
        client.Dispose();
        client.Dispose();
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ExtractAsync(input, "source", "image/png"));
        Assert.Throws<ObjectDisposedException>(() => client.GetService(typeof(DocumentExtractionClient)));
        using var response = await http.GetAsync(OcrEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sdk = AzureSdk(DocumentIntelligenceModelFactory.AnalyzeResult());
        new AzureDocumentIntelligenceExtractionClient(sdk).Dispose();
        await sdk.AnalyzeDocumentAsync(WaitUntil.Completed, new AnalyzeDocumentOptions("model", BinaryData.FromBytes(new byte[] { 1 })));
        await sdk.Received(1).AnalyzeDocumentAsync(WaitUntil.Completed, Arg.Any<AnalyzeDocumentOptions>(), Arg.Any<CancellationToken>());
    }

    private const string MistralResponse = """
        {"model":"reported","pages":[{"index":0,"markdown":"Before\n[t](t)\n![i](i)\nAfter",
          "dimensions":{"width":600,"height":800},
          "tables":[{"id":"t","content":"<table><tr><td>Native</td></tr></table>"}],
          "images":[{"id":"i","image_base64":"data:image/png;base64,AQID",
            "top_left_x":1.123456789012345,"top_left_y":2,"bottom_right_x":10,"bottom_right_y":20}]},
          {"index":2,"markdown":"Third"}]}
        """;
}
