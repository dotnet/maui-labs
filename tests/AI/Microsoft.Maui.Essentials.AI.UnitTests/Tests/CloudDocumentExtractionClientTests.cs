using System.Net;
using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Core.Pipeline;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using NSubstitute;
using Xunit;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class CloudDocumentExtractionClientTests
{
    [Fact]
    public async Task Azure_SdkTransportUsesUtf16SpansAndPreservesUnicode()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = Http((request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("stringIndexType=utf16CodeUnit", request.RequestUri!.Query);
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException();
        });
        var sdk = new DocumentIntelligenceClient(new Uri("https://example.invalid"), new AzureKeyCredential("test"),
            new DocumentIntelligenceClientOptions { Transport = new HttpClientTransport(http) });
        using var adapter = new AzureDocumentIntelligenceExtractionClient(sdk);
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await adapter.ExtractAsync(input, "input", "application/pdf", cancellationToken: cancellation.Token));
        var result = AzureDocumentIntelligenceExtractionClient.MapResult(
            DocumentIntelligenceModelFactory.AnalyzeResult(content: "A😀é",
                pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1, spans: [Span(0, 4)])]));
        Assert.Null(result.Content);
        Assert.Equal("A😀é", Assert.IsType<TextContent>(result.Sections[0].Content).Text);
        Assert.Empty(result.Sections[0].Elements);
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Azure_MapsGeometryRolesSpansEmptyCellAndNativeResult()
    {
        var native = AzureResult();
        var operation = Substitute.For<Operation<AnalyzeResult>>();
        operation.Value.Returns(native);
        var sdk = Substitute.For<DocumentIntelligenceClient>();
        sdk.AnalyzeDocumentAsync(WaitUntil.Completed, Arg.Any<string>(), Arg.Any<BinaryData>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(operation));
        using var adapter = new AzureDocumentIntelligenceExtractionClient(sdk);
        using var input = new MemoryStream([1]);
        var result = await adapter.ExtractAsync(input, "input", "application/pdf",
            new() { ExtractorName = "custom-layout" });
        Assert.True(input.CanRead);
        Assert.Same(sdk, adapter.GetService(typeof(DocumentIntelligenceClient)));
        Assert.Same(adapter, adapter.GetService(typeof(DocumentExtractionClient)));
        Assert.Null(adapter.GetService(typeof(DocumentIntelligenceClient), "key"));
        var frame = Assert.Single(result.SourcePages);
        Assert.Equal(new ExtractionDocumentPageDimensions(8, 11), frame.Dimensions);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Inch, frame.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.TopLeft, frame.CoordinateOrigin);
        var section = Assert.Single(result.Sections);
        Assert.Null(section.Content);
        var title = Assert.IsType<ExtractionDocumentTextElement>(section.Elements[0]);
        Assert.Equal("heading", title.Role);
        Assert.Null(title.HeadingLevel);
        var paragraph = Assert.IsType<DocumentParagraph>(title.Content!.RawRepresentation);
        Assert.Equal(ParagraphRole.Title, paragraph.Role);
        Assert.Equal(0, paragraph.Spans[0].Offset);
        var table = Assert.IsType<ExtractionDocumentTableElement>(section.Elements[1]);
        Assert.Null(table.Content);
        var cell = table.Cells[0, 0]!;
        Assert.Equal(2, cell.ColumnSpan);
        Assert.Null(table.Cells[0, 1]);
        Assert.Equal("content", cell.Role);
        var cellSection = Assert.IsType<ExtractionDocumentSection>(cell.Content);
        Assert.Null(cellSection.Content);
        Assert.Equal("A", Payload(Assert.Single(cellSection.Elements)));
        Assert.Same(frame, cell.Sources[0].Page);
        Assert.Equal(new ExtractionDocumentPoint(1, 2), cell.Sources[0].Polygon[0]);
        Assert.IsType<DocumentTableCell>(cell.Sources[0].GetRawRepresentation());
        var empty = table.Cells[1, 0]!;
        Assert.Null(empty.Content);
        Assert.Null(empty.Role);
        Assert.Single(empty.Sources);
        Assert.Equal(4, empty.Sources[0].Polygon.Count);
        Assert.Same(native, result.RawRepresentation);
        Assert.Empty(result.Metadata);
        await sdk.Received().AnalyzeDocumentAsync(WaitUntil.Completed, "custom-layout", Arg.Any<BinaryData>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Azure_FigureKeepsCaptionAndAllReportedPageRegionsWithoutInventingBytes()
    {
        var figure = DocumentIntelligenceModelFactory.DocumentFigure(
            caption: DocumentIntelligenceModelFactory.DocumentCaption(content: "Native caption"),
            boundingRegions: [Region(1), Region(3)]);
        var result = AzureDocumentIntelligenceExtractionClient.MapResult(DocumentIntelligenceModelFactory.AnalyzeResult(
            content: "", pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1),
                DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 3)], figures: [figure]));
        var image = Assert.IsType<ExtractionDocumentImageElement>(Assert.Single(result.Sections[0].Elements));
        Assert.Equal("Native caption", image.AlternativeText);
        Assert.Null(image.Text);
        Assert.Null(image.Content);
        Assert.Null(image.Source);
        Assert.Equal([1, 3], image.Sources.Select(source => source.Page.PageNumber));
        Assert.All(image.Sources, source => Assert.Same(figure, source.GetRawRepresentation()));
        Assert.Equal([1, 3], result.SourcePages.Select(page => page.PageNumber));
        Assert.Empty(result.Sections[1].Elements);
    }

    [Fact]
    public void Azure_WordsSelectionMarksAndBarcodesRemainExplicitNativeObservations()
    {
        var page = DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1,
            words: [DocumentIntelligenceModelFactory.DocumentWord(content: "Word", confidence: 0.81f, polygon: [0, 0, 1, 0, 1, 1])],
            selectionMarks: [DocumentIntelligenceModelFactory.DocumentSelectionMark(state: DocumentSelectionMarkState.Selected,
                confidence: 0.72f, polygon: [1, 0, 2, 0, 2, 1])],
            barcodes: [DocumentIntelligenceModelFactory.DocumentBarcode(kind: DocumentBarcodeKind.QrCode, value: "Actual code",
                confidence: 0.91f, polygon: [2, 0, 3, 0, 3, 1])]);
        var result = AzureDocumentIntelligenceExtractionClient.MapResult(
            DocumentIntelligenceModelFactory.AnalyzeResult(content: "", pages: [page]));
        var section = Assert.Single(result.Sections);
        var native = Assert.IsType<Azure.AI.DocumentIntelligence.DocumentPage>(section.Sources[0].GetRawRepresentation());
        Assert.Same(page, native);
        Assert.Equal(0.81f, native.Words[0].Confidence);
        Assert.Equal("Word", native.Words[0].Content);
        Assert.Equal(DocumentSelectionMarkState.Selected, native.SelectionMarks[0].State);
        Assert.Equal(0.72f, native.SelectionMarks[0].Confidence);
        Assert.Equal("Actual code", native.Barcodes[0].Value);
        Assert.Equal(0.91f, native.Barcodes[0].Confidence);
        Assert.Empty(section.Elements);
        Assert.Empty(result.Metadata);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text")]
    [InlineData("markdown")]
    public void Azure_PageContentIsOneLocalPayloadWithNativeFormat(string? format)
    {
        const string content = "# Content";
        var native = DocumentIntelligenceModelFactory.AnalyzeResult(content: content,
            contentFormat: format is null ? default : new DocumentContentFormat(format),
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 2, spans: [Span(0, content.Length)])]);
        var result = AzureDocumentIntelligenceExtractionClient.MapResult(native);
        Assert.Equal(content, Assert.IsType<TextContent>(result.Sections[0].Content).Text);
        Assert.Empty(result.Sections[0].Elements);
        Assert.Null(result.Content);
        Assert.Equal(native.ContentFormat, Assert.IsType<AnalyzeResult>(result.RawRepresentation).ContentFormat);
    }

    [Fact]
    public void Foundry_BlockBoxesAndNativeConfidenceReferringContentSurviveParserDisposal()
    {
        ExtractionDocument result;
        using (var json = JsonDocument.Parse("""
            {"model":"reported","pages":[{"index":2,"dimensions":{"width":600,"height":800},"blocks":[
              {"type":"text","content":"Actual text","top_left_x":1.123456789012345,"top_left_y":2,
               "bottom_right_x":3,"bottom_right_y":4,"confidence_scores":{"min":78.125,"mean":93.5,"max":99.875}},
              {"type":"table","table_id":"t","content":"Native block table summary",
               "top_left_x":10,"top_left_y":20,"bottom_right_x":30,"bottom_right_y":40,
               "confidence_scores":{"mean":0.123456789012345}},
              {"type":"image","image_id":"i","content":"Native block image caption",
               "top_left_x":50,"top_left_y":60,"bottom_right_x":70,"bottom_right_y":80,
               "confidence_scores":{"min":42.25}}],
              "tables":[{"id":"t","content":"<table><tr><td>Actual auxiliary HTML</td></tr></table>"}],
              "images":[{"id":"i","caption":"Actual auxiliary caption","ocr_text":"Actual OCR",
                "image_base64":"data:image/png;base64,AQID"}]}]}
            """))
            result = FoundryMistralDocumentExtractionClient.MapResult(json.RootElement, "source");
        var frame = Assert.Single(result.SourcePages);
        Assert.Equal("source/page/3", frame.Identifier);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Pixel, frame.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.TopLeft, frame.CoordinateOrigin);
        var section = result.Sections[0];
        var text = Assert.IsType<ExtractionDocumentTextElement>(section.Elements[0]);
        Assert.Equal(1.123456789012345, text.Sources[0].Polygon[0].X);
        Assert.Same(frame, text.Sources[0].Page);
        var confidence = Native(text.Sources[0]).GetProperty("confidence_scores");
        Assert.Equal(78.125, confidence.GetProperty("min").GetDouble());
        Assert.Equal(93.5, confidence.GetProperty("mean").GetDouble());
        Assert.Equal(99.875, confidence.GetProperty("max").GetDouble());
        Assert.Equal("Actual text", Assert.IsType<JsonElement>(text.Content!.RawRepresentation).GetProperty("content").GetString());
        var table = Assert.IsType<ExtractionDocumentTableElement>(section.Elements[1]);
        Assert.Contains("Actual auxiliary HTML", Payload(table));
        Assert.Equal(2, table.Sources.Count);
        Assert.Equal("Native block table summary", Native(table.Sources[1]).GetProperty("content").GetString());
        Assert.Equal(new ExtractionDocumentPoint(10, 20), table.Sources[1].Polygon[0]);
        Assert.Equal(0.123456789012345, Native(table.Sources[1]).GetProperty("confidence_scores").GetProperty("mean").GetDouble());
        var image = Assert.IsType<ExtractionDocumentImageElement>(section.Elements[2]);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<DataContent>(image.Content).Data.ToArray());
        Assert.Equal("Actual OCR", image.Text);
        Assert.Equal("Native block image caption", image.AlternativeText);
        Assert.Equal("Actual auxiliary caption", Native(image.Sources[0]).GetProperty("caption").GetString());
        Assert.Equal(new ExtractionDocumentPoint(50, 60), image.Sources[1].Polygon[0]);
        Assert.Equal(42.25, Native(image.Sources[1]).GetProperty("confidence_scores").GetProperty("min").GetDouble());
        Assert.Equal("reported", Assert.IsType<JsonElement>(result.RawRepresentation).GetProperty("model").GetString());
        Assert.Empty(result.Metadata);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"blocks\":[],")]
    [InlineData("\"blocks\":[{\"type\":\"text\",\"content\":\"\"}],")]
    [InlineData("\"blocks\":[{\"type\":\"table\",\"table_id\":\"t\"},{\"type\":\"image\",\"image_id\":\"i\"}],")]
    public void Foundry_UnrepresentedPageMarkdownSurvivesMarginsAndAuxiliaryImagesOrTables(string blocks)
    {
        using var json = JsonDocument.Parse($$"""
            {"pages":[{"index":0,{{blocks}}"markdown":"Actual body markdown","header":"Actual header","footer":"Actual footer",
              "images":[{"id":"i","image_base64":"data:image/png;base64,AQID"}],
              "tables":[{"id":"t","content":"<table><tr><td>Actual table</td></tr></table>"}]}]}
            """);
        var result = FoundryMistralDocumentExtractionClient.MapResult(json.RootElement);
        var elements = result.EnumerateContent().ToArray();
        Assert.Single(elements.OfType<ExtractionDocumentTextElement>(), text => Payload(text) == "Actual body markdown");
        Assert.Contains(elements.OfType<ExtractionDocumentTextElement>(), text => text.Role == "pageHeader" && Payload(text) == "Actual header");
        Assert.Contains(elements.OfType<ExtractionDocumentTextElement>(), text => text.Role == "pageFooter" && Payload(text) == "Actual footer");
        Assert.Contains("Actual table", Payload(Assert.Single(elements.OfType<ExtractionDocumentTableElement>())));
        Assert.Single(elements.OfType<ExtractionDocumentImageElement>());
        Assert.Null(result.Content);
        Assert.Null(result.Sections[0].Content);
    }

    [Fact]
    public void Foundry_AggregateMarkdownIsNotDuplicatedAlongsideTableOrImage()
    {
        using var json = JsonDocument.Parse("""
            {"pages":[{"index":0,"markdown":"<table><tr><td>A</td></tr></table>\n![i](i)",
              "tables":[{"id":"t","content":"<table><tr><td>A</td></tr></table>"}],
              "images":[{"id":"i","image_base64":""}]}]}
            """);
        var result = FoundryMistralDocumentExtractionClient.MapResult(json.RootElement);
        Assert.Empty(result.EnumerateContent().OfType<ExtractionDocumentTextElement>());
        Assert.Null(result.Sections[0].Content);
        Assert.Equal(2, result.Sections[0].Elements.Count);
        Assert.Contains("markdown", Assert.IsType<JsonElement>(result.RawRepresentation).GetRawText());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("actual-server-model")]
    public async Task Foundry_NativeModelIsOnlyTheActualResponseModel(string? reported)
    {
        using var http = Http((request, _) =>
        {
            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("request-deployment-alias", payload.RootElement.GetProperty("model").GetString());
            return Ok(JsonSerializer.Serialize(new { model = reported, pages = new[] { new { index = 0, markdown = "Body" } } }));
        });
        using var adapter = new FoundryMistralDocumentExtractionClient(http, "configured-alias");
        using var input = new MemoryStream([1]);
        var result = await adapter.ExtractAsync(input, "source", "image/png",
            new() { ExtractorName = "request-deployment-alias" });
        Assert.Equal(reported, Assert.IsType<JsonElement>(result.RawRepresentation).GetProperty("model").GetString());
    }

    [Fact]
    public async Task Foundry_PreservesImagePayloadMimeAndSourcePagesWithoutClaimingWireBody()
    {
        var responseContent = new TrackingContent(MistralResponse);
        using var http = Http((request, _) =>
        {
            Assert.Equal("/resource/providers/mistral/azure/ocr", request.RequestUri!.AbsolutePath);
            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("override", payload.RootElement.GetProperty("model").GetString());
            Assert.True(payload.RootElement.GetProperty("include_image_base64").GetBoolean());
            Assert.True(payload.RootElement.GetProperty("include_blocks").GetBoolean());
            Assert.True(payload.RootElement.GetProperty("extract_header").GetBoolean());
            Assert.True(payload.RootElement.GetProperty("extract_footer").GetBoolean());
            Assert.Equal("html", payload.RootElement.GetProperty("table_format").GetString());
            return new(HttpStatusCode.OK) { Content = responseContent };
        });
        using var adapter = new FoundryMistralDocumentExtractionClient(http, "configured");
        using var input = new MemoryStream([1]);
        var result = await adapter.ExtractAsync(input, "input", "application/pdf", new() { ExtractorName = "override" });
        Assert.True(input.CanRead);
        Assert.True(responseContent.Disposed);
        Assert.Equal([1, 3], result.SourcePages.Select(page => page.PageNumber));
        Assert.Equal("reported-model", Assert.IsType<JsonElement>(result.RawRepresentation).GetProperty("model").GetString());
        var page = result.Sections[0];
        var table = Assert.IsType<ExtractionDocumentTableElement>(page.Elements[2]);
        Assert.Equal(0, table.Cells.RowCount);
        Assert.Equal("<table><tr><td>Native</td></tr></table>", Payload(table));
        var image = Assert.IsType<ExtractionDocumentImageElement>(page.Elements[3]);
        Assert.Null(image.Text);
        Assert.Null(image.AlternativeText);
        Assert.Null(image.Source);
        var data = Assert.IsType<DataContent>(image.Content);
        Assert.Equal("image/png", data.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, data.Data.ToArray());
        Assert.Equal(1.123456789012345, image.Sources[0].Polygon[0].X);
        Assert.Equal(new ExtractionDocumentPageDimensions(600, 800), result.SourcePages[0].Dimensions);
        Assert.Equal("Third", Payload(Assert.Single(result.Sections[1].Elements)));
        Assert.Null(result.Content);
        Assert.Null(page.Content);
        Assert.Empty(result.Metadata);
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(ExtractionDocument)));
    }

    [Fact]
    public async Task Foundry_ConcurrentExtractorOverridesAreIsolatedAndNeverMutateClientDefaults()
    {
        var models = new System.Collections.Concurrent.ConcurrentBag<string>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        using var http = new HttpClient(new AsyncHandler(async (request, token) =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            models.Add(payload.RootElement.GetProperty("model").GetString()!);
            var arrival = Interlocked.Increment(ref arrivals);
            if (arrival == 2) ready.TrySetResult();
            if (arrival <= 2) await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            return Ok(MistralResponse);
        })) { BaseAddress = new Uri("https://example.invalid/resource/") };
        using var adapter = new FoundryMistralDocumentExtractionClient(http, "configured");
        var options = new DocumentExtractionOptions { ExtractorName = "first" };
        using var inputA = new MemoryStream([1]);
        using var inputB = new MemoryStream([2]);
        var first = adapter.ExtractAsync(inputA, "a", "image/png", options);
        options.ExtractorName = "changed";
        var calls = await Task.WhenAll(first,
            adapter.ExtractAsync(inputB, "b", "image/png", new() { ExtractorName = "second" }));
        using var inputC = new MemoryStream([3]);
        var third = await adapter.ExtractAsync(inputC, "c", "image/png");
        Assert.Equal(["configured", "first", "second"], models.Order().ToArray());
        Assert.Equal("reported-model", Assert.IsType<JsonElement>(calls[0].RawRepresentation).GetProperty("model").GetString());
        Assert.NotEqual(calls[0].Identifier, third.Identifier);
    }

    [Fact]
    public void Foundry_EmptyImageContentIsRetainedAsDetachedData()
    {
        ExtractionDocument result;
        using (var json = JsonDocument.Parse("""
            {"pages":[{"index":0,"images":[{"id":"empty","image_base64":""}]}]}
            """))
            result = FoundryMistralDocumentExtractionClient.MapResult(json.RootElement);
        var image = Assert.IsType<ExtractionDocumentImageElement>(Assert.Single(result.Sections[0].Elements));
        var data = Assert.IsType<DataContent>(image.Content);
        Assert.Empty(data.Data.ToArray());
        Assert.Equal("application/octet-stream", data.MediaType);
        Assert.Null(image.Text);
        Assert.Equal("", Native(image.Sources[0]).GetProperty("image_base64").GetString());
    }

    [Fact]
    public async Task Azure_ConcurrentRequestsSnapshotExtractorsWithoutChangingDefault()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        async Task<Operation<AnalyzeResult>> Analyze(string model)
        {
            var arrival = Interlocked.Increment(ref arrivals);
            if (arrival == 2) ready.TrySetResult();
            if (arrival <= 2) await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var operation = Substitute.For<Operation<AnalyzeResult>>();
            operation.Value.Returns(DocumentIntelligenceModelFactory.AnalyzeResult(content: model,
                pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1, spans: [Span(0, model.Length)])]));
            return operation;
        }
        var sdk = Substitute.For<DocumentIntelligenceClient>();
        sdk.AnalyzeDocumentAsync(WaitUntil.Completed, Arg.Any<string>(), Arg.Any<BinaryData>(), Arg.Any<CancellationToken>())
            .Returns(call => Analyze(call.ArgAt<string>(1)));
        using var adapter = new AzureDocumentIntelligenceExtractionClient(sdk);
        using var inputA = new MemoryStream([1]);
        using var inputB = new MemoryStream([2]);
        var options = new DocumentExtractionOptions { ExtractorName = "first" };
        var first = adapter.ExtractAsync(inputA, "a", "application/pdf", options);
        options.ExtractorName = "changed";
        var calls = await Task.WhenAll(first,
            adapter.ExtractAsync(inputB, "b", "application/pdf", new() { ExtractorName = "second" }));
        Assert.Equal("first", Assert.IsType<TextContent>(calls[0].Sections[0].Content).Text);
        Assert.Equal("second", Assert.IsType<TextContent>(calls[1].Sections[0].Content).Text);
        using var inputC = new MemoryStream([3]);
        var third = await adapter.ExtractAsync(inputC, "c", "application/pdf");
        Assert.Equal("prebuilt-layout", Assert.IsType<TextContent>(third.Sections[0].Content).Text);
    }

    [Fact]
    public async Task Adapters_UnsupportedOptionsLimitsMediaAndCancellationKeepCallerStreamsOpen()
    {
        var requests = 0;
        using var http = Http((_, _) => { requests++; return Ok(MistralResponse); });
        using var foundry = new FoundryMistralDocumentExtractionClient(http, "deployment");
        using var azure = new AzureDocumentIntelligenceExtractionClient(Substitute.For<DocumentIntelligenceClient>());
        foreach (var adapter in new DocumentExtractionClient[] { foundry, azure })
        {
            using var input = new MemoryStream([1]);
            var options = new DocumentExtractionOptions();
            options.AdditionalProperties["unknown"] = true;
            await Assert.ThrowsAsync<NotSupportedException>(async () => await adapter.ExtractAsync(input, "input", "image/png", options));
            using var oversized = new MemoryStream(new byte[20 * 1024 * 1024 + 1]);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await adapter.ExtractAsync(oversized, "input", "image/jpeg"));
            Assert.True(oversized.CanRead);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await adapter.ExtractAsync(input, "input", "text/plain"));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await adapter.ExtractAsync(input, "input", "application/pdf", cancellationToken: cancelled.Token));
            Assert.True(input.CanRead);
        }
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task Foundry_RetryCancellationSafeErrorsAndResponseCapsRemain()
    {
        using var cancelled = new CancellationTokenSource();
        var requests = 0;
        using var retry = Http((_, _) => { requests++; cancelled.Cancel(); return new(HttpStatusCode.ServiceUnavailable); });
        using var adapter = new FoundryMistralDocumentExtractionClient(retry, "deployment");
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await adapter.ExtractAsync(input, "input", "image/png", cancellationToken: cancelled.Token));
        Assert.Equal(1, requests);
        foreach (var response in new[] { Ok("SECRET-invalid"), new HttpResponseMessage(HttpStatusCode.BadRequest)
            { ReasonPhrase = "SECRET", Content = new StringContent("SECRET") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[32 * 1024 * 1024 + 1]) } })
        {
            using var http = Http((_, _) => response);
            using var failing = new FoundryMistralDocumentExtractionClient(http, "deployment");
            using var document = new MemoryStream([1]);
            var error = await Record.ExceptionAsync(async () => await failing.ExtractAsync(document, "input", "image/png"));
            Assert.NotNull(error);
            Assert.DoesNotContain("SECRET", error.ToString());
            Assert.True(document.CanRead);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Foundry_FailureOrCancellationDisposesResponseAndKeepsCallerStreamOpen(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var content = new TrackingContent("invalid JSON");
        using var http = Http((_, _) =>
        {
            if (cancel) cancellation.Cancel();
            return new(HttpStatusCode.OK) { Content = content };
        });
        using var adapter = new FoundryMistralDocumentExtractionClient(http, "deployment");
        using var input = new MemoryStream([1]);
        var error = await Record.ExceptionAsync(async () =>
            await adapter.ExtractAsync(input, "input", "application/pdf", cancellationToken: cancellation.Token));
        Assert.NotNull(error);
        if (cancel) Assert.IsAssignableFrom<OperationCanceledException>(error);
        else Assert.IsType<InvalidDataException>(error);
        Assert.True(content.Disposed);
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Dispose_DefaultLeavesHttpClientAliveExplicitOwnershipDisposesIt()
    {
        using var http = Http((_, _) => Ok(MistralResponse));
        var adapter = new FoundryMistralDocumentExtractionClient(http, "deployment");
        adapter.Dispose();
        adapter.Dispose();
        Assert.Throws<ObjectDisposedException>(() => adapter.GetService(typeof(HttpClient)));
        using var input = new MemoryStream();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await adapter.ExtractAsync(input, "input", "image/png"));
        http.DefaultRequestHeaders.Add("still-alive", "yes");
        var owned = Http((_, _) => Ok(MistralResponse));
        new FoundryMistralDocumentExtractionClient(owned, "deployment", ownsHttpClient: true).Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => owned.GetAsync("ocr"));
        using var azure = new AzureDocumentIntelligenceExtractionClient(Substitute.For<DocumentIntelligenceClient>());
        azure.Dispose();
        Assert.Throws<ObjectDisposedException>(() => azure.GetService(typeof(DocumentIntelligenceClient)));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await azure.ExtractAsync(input, "input", "image/png"));
    }

    private static string Payload(ExtractionDocumentElement element) => Assert.IsType<TextContent>(element.Content).Text;
    private static JsonElement Native(ExtractionDocumentSourceReference source) => Assert.IsType<JsonElement>(source.GetRawRepresentation());
    private static DocumentSpan Span(int offset, int length) => DocumentIntelligenceModelFactory.DocumentSpan(offset: offset, length: length);
    private static BoundingRegion Region(int page) => DocumentIntelligenceModelFactory.BoundingRegion(pageNumber: page, polygon: [1, 2, 3, 2, 3, 4, 1, 4]);
    private static AnalyzeResult AzureResult() => DocumentIntelligenceModelFactory.AnalyzeResult(content: "Title A After",
        pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1, width: 8, height: 11, unit: LengthUnit.Inch, spans: [Span(0, 13)])],
        paragraphs: [DocumentIntelligenceModelFactory.DocumentParagraph(content: "After", spans: [Span(8, 5)]),
            DocumentIntelligenceModelFactory.DocumentParagraph(content: "A", spans: [Span(6, 1)]),
            DocumentIntelligenceModelFactory.DocumentParagraph(content: "Title", role: ParagraphRole.Title, spans: [Span(0, 5)])],
        tables: [DocumentIntelligenceModelFactory.DocumentTable(rowCount: 2, columnCount: 2, spans: [Span(6, 1)], cells:
            [DocumentIntelligenceModelFactory.DocumentTableCell(content: "A", columnSpan: 2, kind: Azure.AI.DocumentIntelligence.DocumentTableCellKind.Content,
                spans: [Span(6, 1)], boundingRegions: [Region(1)]),
             DocumentIntelligenceModelFactory.DocumentTableCell(rowIndex: 1, content: "", boundingRegions: [Region(1)])])]);

    private const string MistralResponse = """
        {"model":"reported-model","pages":[{"index":0,"markdown":"# Title\nText\n<table>native</table>",
          "dimensions":{"width":600,"height":800,"dpi":72},
          "blocks":[{"type":"title","content":"Title"},{"type":"text","content":"Text"},
                    {"type":"table","table_id":"t"},{"type":"image","image_id":"i"}],
          "tables":[{"id":"t","content":"<table><tr><td>Native</td></tr></table>"}],
          "images":[{"id":"i","image_base64":"data:image/png;base64,AQID",
                     "top_left_x":1.123456789012345,"top_left_y":2,"bottom_right_x":10,"bottom_right_y":20}]},
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
    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
    private sealed class TrackingContent(string value) : StringContent(value)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
