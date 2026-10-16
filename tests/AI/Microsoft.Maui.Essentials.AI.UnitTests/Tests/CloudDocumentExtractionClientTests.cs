using System.Net;
using System.Text;
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
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await adapter.ExtractAsync(new MemoryStream([1]), "input", "application/pdf", cancellationToken: cancellation.Token));
        using var result = AzureDocumentIntelligenceExtractionClient.MapResult(
            DocumentIntelligenceModelFactory.AnalyzeResult(content: "A😀é",
                pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1, spans: [Span(0, 4)])]));
        Assert.Null(result.Pages[0].Markdown);
        Assert.Null(result.Document.StoredMarkdown);
        Assert.Equal("A😀é", result.Document.ToMarkdown());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Azure_MapsGeometryRolesSpansEmptyCellAndHonestRawAvailability(bool capture)
    {
        var native = AzureResult();
        var operation = Substitute.For<Operation<AnalyzeResult>>();
        operation.Value.Returns(native);
        var sdk = Substitute.For<DocumentIntelligenceClient>();
        sdk.AnalyzeDocumentAsync(WaitUntil.Completed, Arg.Any<string>(), Arg.Any<BinaryData>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(operation));
        using var adapter = new AzureDocumentIntelligenceExtractionClient(sdk);
        using var input = new MemoryStream([1]);
        using var result = await adapter.ExtractAsync(input, "input", "application/pdf",
            new() { ModelId = "custom-layout", CaptureRawOutput = capture });
        Assert.True(input.CanRead);
        Assert.Same(sdk, adapter.GetService(typeof(DocumentIntelligenceClient)));
        Assert.Null(adapter.GetService(typeof(DocumentIntelligenceClient), "key"));
        var frame = Assert.Single(result.Document.SourcePages);
        Assert.Equal(new DocumentPageDimensions(8, 11), frame.Dimensions);
        Assert.Equal(DocumentCoordinateUnit.Inch, frame.CoordinateUnit);
        Assert.Equal(DocumentCoordinateOrigin.TopLeft, frame.CoordinateOrigin);
        var section = Assert.Single(result.Document.Sections);
        var title = Assert.IsType<DocumentTextElement>(section.Elements[0]);
        Assert.Equal(DocumentTextKind.Header, title.Kind);
        Assert.Null(title.HeaderLevel);
        Assert.Equal("title", title.Annotations[0].Role);
        var table = Assert.IsType<DocumentTableElement>(section.Elements[1]);
        var cell = table.Cells[0, 0]!;
        Assert.Equal(2, cell.ColumnSpan);
        Assert.True(table.Cells.IsCovered(0, 1));
        Assert.Equal("content", cell.Kind);
        Assert.Equal("A", cell.Content!.Text);
        Assert.Same(frame, cell.Annotations[0].Regions[0].Page);
        Assert.Equal(new DocumentPoint(1, 2), cell.Annotations[0].Regions[0].Polygon[0]);
        Assert.Null(cell.Annotations[0].Confidence);
        var empty = table.Cells[1, 0]!;
        Assert.Null(empty.Content);
        Assert.Null(empty.Kind);
        Assert.Single(empty.Annotations[0].Regions);
        var output = Assert.Single(result.Document.SourceOutputs);
        Assert.Equal(capture ? DocumentRawContentStatus.Retained : DocumentRawContentStatus.Disabled, output.NativeStatus);
        Assert.Equal(capture ? DocumentRawContentStatus.Unavailable : DocumentRawContentStatus.Disabled, output.ContentStatus);
        Assert.Null(output.GetRawContent());
        if (capture) Assert.Same(native, output.GetRawRepresentation());
        else AssertNoRaw(result);
        await sdk.Received().AnalyzeDocumentAsync(WaitUntil.Completed, "custom-layout", Arg.Any<BinaryData>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Azure_FigureKeepsCaptionAndAllReportedPageRegionsWithoutInventingBytes()
    {
        var figure = DocumentIntelligenceModelFactory.DocumentFigure(
            caption: DocumentIntelligenceModelFactory.DocumentCaption(content: "Native caption"),
            boundingRegions: [Region(1), Region(3)]);
        using var result = AzureDocumentIntelligenceExtractionClient.MapResult(DocumentIntelligenceModelFactory.AnalyzeResult(
            content: "", pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1),
                DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 3)], figures: [figure]));
        var image = Assert.IsType<DocumentImageElement>(Assert.Single(result.Document.Sections[0].Elements));
        Assert.Equal("Native caption", image.Text);
        Assert.Null(image.Content);
        Assert.Equal([1, 3], image.Annotations[0].Regions.Select(region => region.Page.PageNumber));
        Assert.Null(image.Annotations[0].Confidence);
    }

    [Fact]
    public void Azure_ReportedWordSelectionAndBarcodeValuesSurviveRawOptOut()
    {
        var page = DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1,
            words: [DocumentIntelligenceModelFactory.DocumentWord(content: "Word", confidence: 0.81f, polygon: [0, 0, 1, 0, 1, 1])],
            selectionMarks: [DocumentIntelligenceModelFactory.DocumentSelectionMark(state: DocumentSelectionMarkState.Selected,
                confidence: 0.72f, polygon: [1, 0, 2, 0, 2, 1])],
            barcodes: [DocumentIntelligenceModelFactory.DocumentBarcode(kind: DocumentBarcodeKind.QrCode, value: "Actual code",
                confidence: 0.91f, polygon: [2, 0, 3, 0, 3, 1])]);
        using var result = AzureDocumentIntelligenceExtractionClient.MapResult(
            DocumentIntelligenceModelFactory.AnalyzeResult(content: "", pages: [page]), captureRawOutput: false);
        var section = result.Document.Sections[0];
        var word = section.Annotations.Single(item => item.Kind == "azure.documentIntelligence.word");
        Assert.Equal((double)0.81f, word.Confidence!.Value);
        Assert.Equal("Word", word.Value);
        Assert.Equal("selected", section.Annotations.Single(item => item.Kind == "azure.documentIntelligence.selectionMark").Value);
        var barcode = Assert.IsType<DocumentExtensionElement>(Assert.Single(section.Elements));
        Assert.Equal("Actual code", barcode.Text);
        Assert.Equal((double)0.91f, barcode.Annotations[0].Confidence!.Value);
        AssertNoRaw(result);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("text", false)]
    [InlineData("markdown", true)]
    public void Azure_PageMarkdownIsOnlyTheReportedMarkdownFormat(string? format, bool markdown)
    {
        const string content = "# Content";
        var native = DocumentIntelligenceModelFactory.AnalyzeResult(content: content,
            contentFormat: format is null ? default : new DocumentContentFormat(format),
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 2, spans: [Span(0, content.Length)])]);
        using var result = AzureDocumentIntelligenceExtractionClient.MapResult(native, captureRawOutput: false);
        Assert.Equal(markdown ? content : null, result.Pages[0].Markdown);
        Assert.Equal(markdown ? content : null, result.Document.StoredMarkdown);
        Assert.Equal(content, result.Document.ToMarkdown());
        Assert.Equal(content, result.Document.Sections[0].Annotations[0].Value);
    }

    [Fact]
    public void Foundry_BlockBoxesConfidenceAndReferringContentSurviveWithoutRawOutput()
    {
        using var json = JsonDocument.Parse("""
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
              "images":[{"id":"i","caption":"Actual auxiliary caption","image_base64":"data:image/png;base64,AQID"}]}]}
            """);
        using var result = FoundryMistralDocumentExtractionClient.MapResult(json.RootElement, "source", false);
        var frame = Assert.Single(result.Document.SourcePages);
        Assert.Equal("source/page/3", frame.Identifier);
        Assert.Equal(DocumentCoordinateUnit.Pixel, frame.CoordinateUnit);
        Assert.Equal(DocumentCoordinateOrigin.TopLeft, frame.CoordinateOrigin);
        var section = result.Document.Sections[0];
        var text = Assert.IsType<DocumentTextElement>(section.Elements[0]);
        Assert.Equal(1.123456789012345, text.Annotations[0].Regions[0].Polygon[0].X);
        Assert.Same(frame, text.Annotations[0].Regions[0].Page);
        Assert.Equal(78.125, text.Annotations.Single(item => item.Kind == "mistral.confidence_scores.min").Confidence!.Value);
        Assert.Equal(93.5, text.Annotations.Single(item => item.Kind == "mistral.confidence_scores.mean").Confidence!.Value);
        Assert.Equal(99.875, text.Annotations.Single(item => item.Kind == "mistral.confidence_scores.max").Confidence!.Value);
        Assert.Null(text.Annotations.Single(item => item.Kind == "mistral.confidence_scores.mean").Confidence!.Maximum);
        var table = Assert.IsType<DocumentTableElement>(section.Elements[1]);
        Assert.Contains("Actual auxiliary HTML", table.StoredMarkdown);
        Assert.Contains(table.Annotations, item => Equals(item.Value, "Native block table summary"));
        Assert.Equal(new DocumentPoint(10, 20), table.Annotations.Single(item => item.Kind == "mistral.ocr" && item.Regions.Count > 0).Regions[0].Polygon[0]);
        Assert.Equal(0.123456789012345, table.Annotations.Single(item => item.Kind == "mistral.confidence_scores.mean").Confidence!.Value);
        var image = Assert.IsType<DocumentImageElement>(section.Elements[2]);
        Assert.Equal(new byte[] { 1, 2, 3 }, image.Content!.Value.ToArray());
        Assert.Equal("Native block image caption", image.Text);
        Assert.Contains(image.Annotations, item => item.Kind == "mistral.caption" && Equals(item.Value, "Actual auxiliary caption"));
        Assert.Equal(new DocumentPoint(50, 60), image.Annotations.Single(item => item.Kind == "mistral.ocr" && item.Regions.Count > 0).Regions[0].Polygon[0]);
        Assert.Equal(42.25, image.Annotations.Single(item => item.Kind == "mistral.confidence_scores.min").Confidence!.Value);
        AssertNoRaw(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"blocks\":[],")]
    [InlineData("\"blocks\":[{\"type\":\"text\",\"content\":\"\"}],")]
    public void Foundry_PageMarkdownFallbackSurvivesMarginsAndAuxiliaryImagesOrTables(string blocks)
    {
        using var json = JsonDocument.Parse($$"""
            {"pages":[{"index":0,{{blocks}}"markdown":"Actual body markdown","header":"Actual header","footer":"Actual footer",
              "images":[{"id":"i","image_base64":"data:image/png;base64,AQID"}],
              "tables":[{"id":"t","content":"<table><tr><td>Actual table</td></tr></table>"}]}]}
            """);
        using var result = FoundryMistralDocumentExtractionClient.MapResult(json.RootElement, captureRawOutput: false);
        var rendered = result.Document.ToMarkdown();
        Assert.Contains("Actual body markdown", rendered);
        Assert.Contains("Actual header", rendered);
        Assert.Contains("Actual footer", rendered);
        Assert.Contains("Actual table", rendered);
        Assert.Single(result.Document.EnumerateContent().OfType<DocumentImageElement>());
        Assert.Single(result.Document.EnumerateContent().OfType<DocumentTableElement>());
        Assert.Equal("Actual body markdown", result.Pages[0].Markdown);
        Assert.Equal(1, rendered.Split("Actual body markdown", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("actual-server-model")]
    public async Task Foundry_SourceModelIdIsOnlyTheActualResponseModel(string? reported)
    {
        using var http = Http((request, _) =>
        {
            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("request-deployment-alias", payload.RootElement.GetProperty("model").GetString());
            return Ok(JsonSerializer.Serialize(new { model = reported, pages = new[] { new { index = 0, markdown = "Body" } } }));
        });
        using var adapter = new FoundryMistralDocumentExtractionClient(http, "configured-alias");
        using var input = new MemoryStream([1]);
        using var result = await adapter.ExtractAsync(input, "source", "image/png",
            new() { ModelId = "request-deployment-alias", CaptureRawOutput = false });
        Assert.Equal(reported, Assert.Single(result.Document.SourceOutputs).ModelId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Foundry_PreservesImagesWithoutCaptionMarkdownAndSharesOriginalBodyAcrossPages(bool capture)
    {
        using var http = Http((request, _) =>
        {
            Assert.Equal("/resource/providers/mistral/azure/ocr", request.RequestUri!.AbsolutePath);
            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("override", payload.RootElement.GetProperty("model").GetString());
            Assert.True(payload.RootElement.GetProperty("include_image_base64").GetBoolean());
            Assert.True(payload.RootElement.GetProperty("include_blocks").GetBoolean());
            Assert.Equal("html", payload.RootElement.GetProperty("table_format").GetString());
            return Ok(MistralResponse);
        });
        using var adapter = new FoundryMistralDocumentExtractionClient(http, "configured");
        using var input = new MemoryStream([1]);
        var result = await adapter.ExtractAsync(input, "input", "application/pdf", new() { ModelId = "override", CaptureRawOutput = capture });
        using (result)
        {
            Assert.True(input.CanRead);
            Assert.Equal([1, 3], result.Pages.Select(page => page.PageNumber));
            var source = Assert.Single(result.Document.SourceOutputs);
            Assert.Same(result.Pages[0].SourceOutputs[0], result.Pages[1].SourceOutputs[0]);
            Assert.Equal("reported-model", source.ModelId);
            var page = result.Document.Sections[0];
            var table = Assert.IsType<DocumentTableElement>(page.Elements[2]);
            Assert.Equal(0, table.Cells.RowCount);
            Assert.Equal("<table><tr><td>Native</td></tr></table>", table.StoredMarkdown);
            var image = Assert.IsType<DocumentImageElement>(page.Elements[3]);
            Assert.Equal("", image.StoredMarkdown);
            Assert.Null(image.Text);
            Assert.Equal("image/png", image.MediaType);
            Assert.Equal(new byte[] { 1, 2, 3 }, image.Content!.Value.ToArray());
            Assert.Equal(1.123456789012345, image.Annotations[0].Regions[0].Polygon[0].X);
            Assert.Equal(new DocumentPageDimensions(600, 800), result.Pages[0].SourcePage!.Dimensions);
            if (capture)
            {
                Assert.Equal(DocumentRawContentStatus.Retained, source.NativeStatus);
                Assert.Equal(DocumentRawContentStatus.Retained, source.ContentStatus);
                var artifact = source.GetRawContent()!;
                using var copy = new MemoryStream();
                await artifact.Content.CopyToAsync(copy);
                Assert.Equal(Encoding.UTF8.GetBytes(MistralResponse), copy.ToArray());
                Assert.True(artifact.IsDocumentOwned);
                result.Dispose();
                Assert.True(artifact.IsDisposed);
            }
            else AssertNoRaw(result);
        }
    }

    [Fact]
    public async Task Foundry_ConcurrentModelOverridesAreIsolatedAndNeverMutateClientDefaults()
    {
        var models = new System.Collections.Concurrent.ConcurrentBag<string>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        using var http = new HttpClient(new AsyncHandler(async (request, token) =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            models.Add(payload.RootElement.GetProperty("model").GetString()!);
            var arrival = Interlocked.Increment(ref arrivals);
            if (arrival == 2)
                ready.TrySetResult();
            if (arrival <= 2)
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            return Ok(MistralResponse);
        })) { BaseAddress = new Uri("https://example.invalid/resource/") };
        using var adapter = new FoundryMistralDocumentExtractionClient(http, "configured");
        var options = new DocumentExtractionOptions { ModelId = "first" };
        using var inputA = new MemoryStream([1]);
        using var inputB = new MemoryStream([2]);
        var pages = adapter.ExtractPagesAsync(inputA, "a", "image/png", options);
        options.ModelId = "changed";
        var calls = await Task.WhenAll(pages.ToDocumentExtractionResultAsync("a"),
            adapter.ExtractAsync(inputB, "b", "image/png", new() { ModelId = "second", CaptureRawOutput = false }));
        using var a = calls[0];
        using var b = calls[1];
        using var inputC = new MemoryStream([3]);
        using var c = await adapter.ExtractAsync(inputC, "c", "image/png");
        Assert.Equal(["configured", "first", "second"], models.Order().ToArray());
        Assert.Equal("reported-model", a.Document.SourceOutputs[0].ModelId);
        Assert.Equal("reported-model", b.Document.SourceOutputs[0].ModelId);
        Assert.Equal(DocumentRawContentStatus.Disabled, b.Document.SourceOutputs[0].ContentStatus);
        Assert.NotSame(a.Document.SourceOutputs[0], c.Document.SourceOutputs[0]);
    }

    [Fact]
    public void Foundry_MappedJsonIsNotClaimedAsOriginalWireBodyAndEmptyImageContentIsRetained()
    {
        using var json = JsonDocument.Parse("""
            {"pages":[{"index":0,"images":[{"id":"empty","image_base64":""}]}]}
            """);
        using var result = FoundryMistralDocumentExtractionClient.MapResult(json.RootElement);
        var output = Assert.Single(result.Document.SourceOutputs);
        Assert.Equal(DocumentRawContentStatus.Retained, output.NativeStatus);
        Assert.Equal(DocumentRawContentStatus.Unavailable, output.ContentStatus);
        Assert.Null(output.GetRawContent());
        var image = Assert.IsType<DocumentImageElement>(Assert.Single(result.Document.Sections[0].Elements));
        Assert.True(image.Content.HasValue);
        Assert.Equal(0, image.Content.Value.Length);
        Assert.Equal("", image.StoredMarkdown);
        Assert.Null(image.Text);
    }

    [Fact]
    public async Task Azure_ConcurrentRequestsSnapshotModelsAndRawSettingsWithoutChangingDefault()
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
        var options = new DocumentExtractionOptions { ModelId = "first" };
        var pages = adapter.ExtractPagesAsync(inputA, "a", "application/pdf", options);
        options.ModelId = "changed";
        var calls = await Task.WhenAll(pages.ToDocumentExtractionResultAsync("a"),
            adapter.ExtractAsync(inputB, "b", "application/pdf", new() { ModelId = "second", CaptureRawOutput = false }));
        using var a = calls[0];
        using var b = calls[1];
        Assert.Equal("first", a.Document.ToMarkdown());
        Assert.Equal("second", b.Document.ToMarkdown());
        Assert.Equal(DocumentRawContentStatus.Retained, a.Document.SourceOutputs[0].NativeStatus);
        Assert.Equal(DocumentRawContentStatus.Disabled, b.Document.SourceOutputs[0].NativeStatus);
        using var inputC = new MemoryStream([3]);
        using var c = await adapter.ExtractAsync(inputC, "c", "application/pdf");
        Assert.Equal("prebuilt-layout", c.Document.ToMarkdown());
    }

    [Fact]
    public async Task Adapters_UnsupportedOptionsLimitsMediaAndCancellationKeepCallerStreamsOpen()
    {
        var requests = 0;
        using var http = Http((_, _) => { requests++; return Ok(MistralResponse); });
        using var foundry = new FoundryMistralDocumentExtractionClient(http, "deployment");
        using var azure = new AzureDocumentIntelligenceExtractionClient(Substitute.For<DocumentIntelligenceClient>());
        foreach (var adapter in new IDocumentExtractionClient[] { foundry, azure })
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
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await adapter.ExtractAsync(new MemoryStream([1]), "input", "image/png", cancellationToken: cancelled.Token));
        Assert.Equal(1, requests);
        foreach (var response in new[] { Ok("SECRET-invalid"), new HttpResponseMessage(HttpStatusCode.BadRequest)
            { ReasonPhrase = "SECRET", Content = new StringContent("SECRET") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[32 * 1024 * 1024 + 1]) } })
        {
            using var http = Http((_, _) => response);
            using var failing = new FoundryMistralDocumentExtractionClient(http, "deployment");
            var error = await Record.ExceptionAsync(async () => await failing.ExtractAsync(new MemoryStream([1]), "input", "image/png"));
            Assert.NotNull(error);
            Assert.DoesNotContain("SECRET", error.ToString());
        }
    }

    [Fact]
    public async Task Foundry_CancelBetweenPagesFinalizerClosesSharedOriginalBody()
    {
        using var http = Http((_, _) => Ok(MistralResponse));
        using var adapter = new FoundryMistralDocumentExtractionClient(http, "deployment");
        using var cancellation = new CancellationTokenSource();
        using var input = new MemoryStream([1]);
        DocumentArtifact? body = null;
        async IAsyncEnumerable<DocumentExtractionPage> CancelPages()
        {
            await foreach (var page in adapter.ExtractPagesAsync(input, "input", "application/pdf", cancellationToken: cancellation.Token))
            {
                body = page.SourceOutputs[0].GetRawContent();
                yield return page;
                cancellation.Cancel();
            }
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await CancelPages().ToDocumentExtractionResultAsync("input", cancellation.Token));
        Assert.True(body!.IsDisposed);
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
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await adapter.ExtractAsync(new MemoryStream(), "input", "image/png"));
        http.DefaultRequestHeaders.Add("still-alive", "yes");
        var owned = Http((_, _) => Ok(MistralResponse));
        new FoundryMistralDocumentExtractionClient(owned, "deployment", ownsHttpClient: true).Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => owned.GetAsync("ocr"));
    }

    private static void AssertNoRaw(DocumentExtractionResult result)
    {
        Assert.All(result.Document.SourceOutputs, output =>
        {
            Assert.Equal(DocumentRawContentStatus.Disabled, output.NativeStatus);
            Assert.Equal(DocumentRawContentStatus.Disabled, output.ContentStatus);
            Assert.Null(output.GetRawRepresentation());
            Assert.Null(output.GetRawContent());
        });
        foreach (var element in result.Document.EnumerateContent())
        {
            Assert.All(element.Annotations, item => Assert.Null(item.GetRawRepresentation()));
            if (element is DocumentTableElement table)
                foreach (var cell in table.Cells)
                    if (cell is not null)
                        Assert.All(cell.Annotations, item => Assert.Null(item.GetRawRepresentation()));
        }
        Assert.All(result.Document.Sections, section => Assert.All(section.Annotations, item => Assert.Null(item.GetRawRepresentation())));
        Assert.Empty(result.Document.Artifacts);
    }

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
}
