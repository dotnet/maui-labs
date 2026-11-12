using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;
using static AIExtensions.Sample.ChatPlayground.CloudDocumentTestFixtures;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class CloudDocumentExtractionClientTests
{
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
    public async Task Foundry_ConcurrentOverridesAreSnapshottedAndDoNotChangeDefault()
    {
        var mistralModels = new ConcurrentBag<string>();
        using var http = Http(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            mistralModels.Add(body.RootElement.GetProperty("model").GetString()!);
            await Task.Yield();
            return Ok("""{"pages":[{"index":0,"markdown":"Body"}]}""");
        });
        using var client = Mistral(http);
        var options = new DocumentExtractionOptions { ExtractorName = "first" };
        using var firstInput = new MemoryStream([1]);
        using var secondInput = new MemoryStream([2]);
        var first = client.ExtractAsync(firstInput, "a", "application/pdf", options);
        options.ExtractorName = "changed";
        await Task.WhenAll(first, client.ExtractAsync(secondInput, "b", "application/pdf",
            new() { ExtractorName = "second" }));
        using var thirdInput = new MemoryStream([3]);
        await client.ExtractAsync(thirdInput, "c", "application/pdf");
        Assert.Equal(["deployment", "first", "second"], mistralModels.Order().ToArray());
    }

    [Fact]
    public async Task Foundry_UnsupportedOptionsAndCancellationKeepCallerStreamsOpen()
    {
        var requests = 0;
        using var http = Http((_, _) => { requests++; return Ok(MistralResponse); });
        using var client = Mistral(http);
        using var input = new MemoryStream([1]);
        var options = new DocumentExtractionOptions();
        options.AdditionalProperties["unknown"] = true;
        await Assert.ThrowsAsync<NotSupportedException>(() => client.ExtractAsync(input, "source", "image/png", options));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ExtractAsync(input, "source", "application/pdf", cancellationToken: cancelled.Token));
        Assert.True(input.CanRead);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task Foundry_CallerCancellationReachesTransportAndLeavesCallerStreamOpen()
    {
        using var http = Http(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Ok("{}");
        });
        using var client = Mistral(http);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ExtractAsync(input, "source", "application/pdf", cancellationToken: cancellation.Token));
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

    [Fact]
    public async Task Foundry_InvalidJsonDisposesResponseAndKeepsCallerStreamOpen()
    {
        var content = new TrackingContent("not JSON");
        using var http = Http((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        using var client = Mistral(http);
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAnyAsync<JsonException>(() => client.ExtractAsync(input, "source", "application/pdf"));
        Assert.True(content.Disposed);
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Dispose_LeavesBorrowedHttpClientUsable()
    {
        using var http = Http((_, _) => Ok(MistralResponse));
        var client = Mistral(http);
        client.Dispose();
        client.Dispose();
        using var response = await http.GetAsync(OcrEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Toolkit_UnprojectedBlocksMarginsAndConfidenceRemainInDetachedNativeJson()
    {
        using var http = Http((_, _) => Ok("""
            {"pages":[{"index":0,"markdown":"Readable body","header":"Native header","footer":"Native footer",
              "blocks":[{"type":"text","content":"Native block","top_left_x":1,
                "confidence_scores":{"min":78.125,"mean":93.5,"max":99.875}}]}]}
            """));
        using var extraction = Mistral(http);
        using var input = new MemoryStream([1]);
        var result = await extraction.ExtractAsync(input, "source", "application/pdf");
        var native = Assert.IsType<JsonElement>(result.RawRepresentation).GetProperty("pages")[0];
        Assert.Equal("Native header", native.GetProperty("header").GetString());
        Assert.Equal("Native footer", native.GetProperty("footer").GetString());
        Assert.Equal(93.5, native.GetProperty("blocks")[0].GetProperty("confidence_scores").GetProperty("mean").GetDouble());
        Assert.Equal("Readable body", Assert.IsType<TextContent>(Assert.Single(result.Sections[0].Elements).Content).Text);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"pages":[{"index":0},{"index":0}]}""")]
    public async Task ToolkitRejectsMissingPagesAndDuplicatePageNumbers(string response)
    {
        using var http = Http((_, _) => Ok(response));
        using var extraction = Mistral(http);
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            extraction.ExtractAsync(input, "source", "application/pdf"));
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
