using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DataIngestion;
using Xunit;
using static AIExtensions.Sample.ChatPlayground.CloudDocumentTestFixtures;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class FoundryMistralDocumentReaderTests
{
    [Fact]
    public async Task ReadAsync_UsesToolkitAndPreservesIdentifierSourcePagesAndLiteralFormatting()
    {
        using var http = Http((_, _) => Ok("""
            {"pages":[{"index":0,"markdown":"# Native Markdown\n**Text** &amp; text\n[t](t)\nAfter",
              "tables":[{"id":"t","content":"<table><tr><th colspan='2'>A &amp; B</th></tr></table>"}]},
              {"index":2,"markdown":"Third"}]}
            """));
        using var extraction = Mistral(http);
        var reader = new ExtractedDocumentReader(extraction);
        using var input = new MemoryStream([1, 2, 3]);
        var document = await reader.ReadAsync(input, "source.pdf", "application/pdf");

        Assert.Equal("source.pdf", document.Identifier);
        Assert.Equal([1, 3], document.Sections.Select(section => section.PageNumber));
        var first = document.Sections[0].Elements;
        Assert.Equal("# Native Markdown\n**Text** &amp; text\n", first[0].GetMarkdown());
        Assert.IsType<IngestionDocumentParagraph>(first[0]);
        var table = Assert.IsType<IngestionDocumentTable>(first[1]);
        Assert.Equal("<table><tr><th colspan='2'>A &amp; B</th></tr></table>", table.GetMarkdown());
        Assert.Equal(0, table.Cells.GetLength(0));
        Assert.Equal(0, table.Cells.GetLength(1));
        Assert.Equal("\nAfter", first[2].Text);
        Assert.Equal("Third", Assert.Single(document.Sections[1].Elements).Text);
        Assert.All(first, element =>
        {
            Assert.Equal(1, element.PageNumber);
            Assert.False(element.HasMetadata);
        });
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task ReadAsync_UnreferencedTablesAndMarkdownAreNotDropped()
    {
        using var http = Http((_, _) => Ok("""
            {"pages":[{"index":0,"markdown":"Page text","tables":[{"id":"t","content":"Native table"}]},
              {"index":1,"tables":[{"id":"t","content":"<table><tr><td>Alone</td></tr></table>"}]}]}
            """));
        using var extraction = Mistral(http);
        var document = await new ExtractedDocumentReader(extraction)
            .ReadAsync(new MemoryStream([1]), "source", "image/png");
        Assert.Equal(2, document.Sections[0].Elements.Count);
        Assert.Equal("Page text", document.Sections[0].Elements[0].Text);
        Assert.Equal("Native table", Assert.IsType<IngestionDocumentTable>(document.Sections[0].Elements[1]).Text);
        Assert.Equal("<table><tr><td>Alone</td></tr></table>",
            Assert.IsType<IngestionDocumentTable>(Assert.Single(document.Sections[1].Elements)).GetMarkdown());
    }

    [Theory]
    [InlineData("<table><tr><td></td><td>Value</td></tr></table>")]
    [InlineData("<table><tr><th colspan='2'>A &amp; B</th></tr><tr><td rowspan='2'>Top</td><td>Right</td></tr><tr><td>Bottom</td></tr></table>")]
    [InlineData("<table><tr><td>10<br>20</td><td><p>First</p><div>Second</div>Third</td></tr></table>")]
    [InlineData("<table><tr><td colspan='0'>Provider HTML is not parsed locally</td></tr></table>")]
    public async Task ReadAsync_LiteralTableContentRetainsEntitiesSpansBlankCellsAndSeparators(string tableContent)
    {
        using var http = Http((_, _) => Ok(JsonSerializer.Serialize(new
        {
            pages = new[] { new { index = 0, tables = new[] { new { id = "t", content = tableContent } } } },
        })));
        using var extraction = Mistral(http);
        using var input = new MemoryStream([1]);
        var document = await new ExtractedDocumentReader(extraction).ReadAsync(input, "source", "image/png");
        var table = Assert.IsType<IngestionDocumentTable>(Assert.Single(document.Sections[0].Elements));
        Assert.Equal(tableContent, table.GetMarkdown());
        Assert.Equal(0, table.Cells.GetLength(0));
        Assert.Equal(0, table.Cells.GetLength(1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"blocks\":[],")]
    [InlineData("\"blocks\":[{\"type\":\"text\",\"content\":\"\"}],")]
    public async Task ReadAsync_AvailableMarkdownIsRetainedWhenBlocksAreAbsentEmptyOrUnusable(string blocks)
    {
        using var http = Http((_, _) => Ok($$"""{"pages":[{"index":0,{{blocks}}"markdown":"Still readable"}]}"""));
        using var extraction = Mistral(http);
        using var input = new MemoryStream([1]);
        var result = await new ExtractedDocumentReader(extraction).ReadAsync(input, "source", "application/pdf");
        Assert.Equal("Still readable", Assert.Single(result.Sections[0].Elements).Text);
    }

    [Fact]
    public async Task ReadAsync_PreservesImageBytesMimeAndSourceWithoutInventingCaptionOrOcr()
    {
        using var http = Http((_, _) => Ok("""
            {"pages":[{"index":2,"markdown":"![img](img)",
              "images":[{"id":"img","image_base64":"data:image/jpeg;base64,AQID"}]}]}
            """));
        using var extraction = Mistral(http);
        var result = await new ExtractedDocumentReader(extraction)
            .ReadAsync(new MemoryStream([1]), "source", "image/png");
        var image = Assert.IsType<IngestionDocumentImage>(Assert.Single(result.Sections[0].Elements));
        Assert.Equal(new byte[] { 1, 2, 3 }, image.Content!.Value.ToArray());
        Assert.Equal("image/jpeg", image.MediaType);
        Assert.Equal(3, image.PageNumber);
        Assert.Null(image.AlternativeText);
        Assert.Null(image.Text);
        Assert.Contains("data:image/jpeg;base64,AQID", image.GetMarkdown());
    }

    [Fact]
    public async Task ReadAsync_RejectsUnsupportedAndOversizedDocumentsBeforeRequest()
    {
        var requests = 0;
        using var http = Http((_, _) => { requests++; return Ok("{}"); });
        using var extraction = Mistral(http);
        var reader = new ExtractedDocumentReader(extraction);
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAsync<NotSupportedException>(() => reader.ReadAsync(input, "source", "text/plain"));
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadAsync(input, "source", ""));
        using var oversized = new RepeatingStream(20 * 1024 * 1024 + 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(oversized, "source", "image/tiff"));
        Assert.True(oversized.CanRead);
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAsync_BoundsKnownAndStreamingResponses(bool streaming)
    {
        using var http = Http((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = streaming ? new StreamContent(new RepeatingStream(32 * 1024 * 1024 + 1)) :
                new ByteArrayContent(new byte[32 * 1024 * 1024 + 1]),
        });
        using var extraction = Mistral(http);
        using var input = new MemoryStream([1]);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExtractedDocumentReader(extraction).ReadAsync(input, "source", "image/tiff"));
        Assert.Contains("32 MB", error.Message);
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task ReadAsync_StatusAndTransportErrorsDoNotExposeResponseReasonOrCredentials()
    {
        foreach (var transportFailure in new[] { false, true })
        {
            using var http = Http((_, _) => transportFailure ? throw new HttpRequestException("SECRET-key") :
                new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    ReasonPhrase = "SECRET-reason",
                    Content = new StringContent("SECRET-body"),
                });
            using var extraction = Mistral(http);
            using var input = new MemoryStream([1]);
            var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
                new ExtractedDocumentReader(extraction).ReadAsync(input, "source", "image/heic"));
            Assert.DoesNotContain("SECRET", error.ToString());
            if (!transportFailure)
            {
                Assert.Contains("400", error.Message);
                Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
            }
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"pages":[{"index":0},{"index":0}]}""")]
    public async Task ReadAsync_ToolkitRejectsMissingPagesAndDuplicatePageNumbers(string response)
    {
        using var http = Http((_, _) => Ok(response));
        using var extraction = Mistral(http);
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ExtractedDocumentReader(extraction).ReadAsync(input, "source", "application/pdf"));
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
        var document = CloudDocumentIngestionProjection.Project(result);
        Assert.Equal("Readable body", Assert.Single(document.Sections[0].Elements).Text);
    }
}
