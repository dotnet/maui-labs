using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DataIngestion;
using Xunit;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class FoundryMistralDocumentReaderTests
{
    [Fact]
    public async Task ReadAsync_PostsInlineDocumentAndPreservesIdentifierAndPageNumbers()
    {
        var requests = 0;
        using var client = Client((request, token) =>
        {
            requests++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/resource/providers/mistral/azure/ocr", request.RequestUri!.AbsolutePath);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            using var body = JsonDocument.Parse(request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            var root = body.RootElement;
            Assert.Equal("deployment", root.GetProperty("model").GetString());
            Assert.Equal("document_url", root.GetProperty("document").GetProperty("type").GetString());
            Assert.Equal("data:application/pdf;base64,AQID", root.GetProperty("document").GetProperty("document_url").GetString());
            Assert.False(root.GetProperty("include_image_base64").GetBoolean());
            Assert.True(root.GetProperty("include_blocks").GetBoolean());
            Assert.Equal("html", root.GetProperty("table_format").GetString());
            Assert.True(root.GetProperty("extract_header").GetBoolean());
            Assert.True(root.GetProperty("extract_footer").GetBoolean());
            Assert.False(root.TryGetProperty("confidence_scores_granularity", out _));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"pages":[{"index":0,"blocks":[{"type":"text","content":"First"}]},{"index":2,"blocks":[{"type":"text","content":"Third"}]}]}"""),
            };
        });
        var reader = new FoundryMistralDocumentReader(client, "deployment");

        var document = await reader.ReadAsync(new MemoryStream([1, 2, 3]), "source.pdf", "application/pdf");

        Assert.Equal(1, requests);
        Assert.Equal("source.pdf", document.Identifier);
        Assert.Equal([1, 3], document.Sections.Select(section => section.PageNumber).ToArray());
        Assert.Equal(3, Assert.Single(document.Sections[1].Elements).PageNumber);
        Assert.False(Assert.Single(document.Sections[1].Elements).HasMetadata);
    }

    [Fact]
    public void MapResult_PreservesOrderAndMapsHeadingsEntitiesAndMergedCells()
    {
        using var json = JsonDocument.Parse("""
        {"pages":[{"index":1,"header":"Header","blocks":[
          {"type":"title","content":"## Section"},
          {"type":"text","content":"Some **markdown** &amp; text"},
          {"type":"table","table_id":"t1"},
          {"type":"text","content":"After"}],
          "tables":[{"id":"t1","content":"<table><tr><th colspan='2'>A &amp; B</th></tr><tr><td rowspan='2'>Top</td><td>Right</td></tr><tr><td>Bottom</td></tr></table>"}],
          "footer":"Footer"}]}
        """);

        var result = FoundryMistralDocumentReader.MapResult(json.RootElement, "one.pdf");
        var elements = Assert.Single(result.Sections).Elements;

        Assert.Equal(new[] { "Header", "Section", "Some markdown & text", "A & B Top Right Bottom", "After", "Footer" },
            elements.Select(element => element.Text ?? "").ToArray());
        Assert.Equal(2, Assert.IsType<IngestionDocumentHeader>(elements[1]).Level);
        Assert.Contains("**markdown**", elements[2].GetMarkdown());
        var table = Assert.IsType<IngestionDocumentTable>(elements[3]);
        Assert.Contains("<th colspan=\"2\">A &amp; B</th>", table.GetMarkdown());
        Assert.Contains("<td rowspan=\"2\">Top</td>", table.GetMarkdown());
        Assert.Equal(3, table.Cells.GetLength(0));
        Assert.Equal(2, table.Cells.GetLength(1));
        Assert.Equal("A & B", table.Cells[0, 0]!.Text);
        Assert.Null(table.Cells[0, 1]);
        Assert.Null(table.Cells[2, 0]);
        Assert.All(elements, element => { Assert.Equal(2, element.PageNumber); Assert.False(element.HasMetadata); });
        Assert.Equal(2, table.Cells[1, 0]!.PageNumber);
        Assert.False(table.Cells[1, 0]!.HasMetadata);
    }

    [Fact]
    public void MapResult_FallbackAndUnreferencedTableAreNotDropped()
    {
        using var json = JsonDocument.Parse("""
        {"pages":[{"index":0,"markdown":"Page text","tables":[{"id":"t","content":"<table><tr><td>Only</td></tr></table>"}]},
                  {"index":1,"blocks":[],"tables":[{"id":"t","content":"<table><tr><td>Alone</td></tr></table>"}]}]}
        """);
        var document = FoundryMistralDocumentReader.MapResult(json.RootElement, "input");
        Assert.Equal(2, document.Sections[0].Elements.Count);
        Assert.Equal("Page text", document.Sections[0].Elements[0].Text);
        Assert.IsType<IngestionDocumentTable>(document.Sections[0].Elements[1]);
        Assert.Equal("Alone", Assert.IsType<IngestionDocumentTable>(
            Assert.Single(document.Sections[1].Elements)).Cells[0, 0]!.Text);
    }

    [Fact]
    public void MapResult_EmptyBlocksRetainsMarkdownAndBlankTablePositions()
    {
        using var json = JsonDocument.Parse("""
        {"pages":[{"index":0,"blocks":[],"markdown":"Still readable",
          "tables":[{"id":"t","content":"<table><tr><td></td><td>Value</td></tr></table>"}]}]}
        """);
        var elements = FoundryMistralDocumentReader.MapResult(json.RootElement, "input").Sections[0].Elements;
        Assert.Equal("Still readable", elements[0].Text);
        var table = Assert.IsType<IngestionDocumentTable>(elements[1]);
        Assert.Null(table.Cells[0, 0]);
        Assert.Equal("Value", table.Cells[0, 1]!.Text);
        Assert.Equal(1, table.Cells[0, 1]!.PageNumber);
    }

    [Fact]
    public void MapResult_UnusableBlocksRetainsAvailableMarkdown()
    {
        using var json = JsonDocument.Parse("""
        {"pages":[{"index":0,"markdown":"Invoice total: $100","blocks":[{"type":"text","content":""}]}]}
        """);
        var element = Assert.Single(FoundryMistralDocumentReader.MapResult(json.RootElement, "input").Sections[0].Elements);
        Assert.Equal("Invoice total: $100", element.Text);
    }

    [Fact]
    public void MapResult_TableCellPreservesLineAndBlockSeparators()
    {
        using var json = JsonDocument.Parse("""
        {"pages":[{"index":0,"tables":[{"id":"t","content":"<table><tr><td>10<br>20</td><td><p>First</p><div>Second</div>Third</td></tr></table>"}]}]}
        """);
        var table = Assert.IsType<IngestionDocumentTable>(
            Assert.Single(FoundryMistralDocumentReader.MapResult(json.RootElement, "input").Sections[0].Elements));
        Assert.Equal("10\n20", table.Cells[0, 0]!.Text);
        Assert.Equal("First\nSecond\nThird", table.Cells[0, 1]!.Text);
        Assert.Contains("<td>10<br>20</td>", table.GetMarkdown());
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"pages":[]}""")]
    [InlineData("""{"pages":[{"index":-1}]}""")]
    [InlineData("""{"pages":[{"index":0},{"index":0}]}""")]
    [InlineData("""{"pages":[{"index":0,"blocks":[{"type":"table","table_id":"missing"}]}]}""")]
    [InlineData("""{"pages":[{"index":0,"tables":[{"id":"x","content":"<p>not a table</p>"}]}]}""")]
    [InlineData("""{"pages":[{"index":0,"tables":[{"id":"x","content":"<table><tr><td rowspan='2'>out</td></tr></table>"}]}]}""")]
    [InlineData("""{"pages":[{"index":0,"tables":[{"id":"x","content":"<table><tr><td colspan='0'>bad</td></tr></table>"}]}]}""")]
    [InlineData("""{"pages":[{"index":0,"tables":[{"id":"x","content":"<table><tr><td colspan='101'>out</td></tr></table>"}]}]}""")]
    public void MapResult_InvalidResponsesRejectRatherThanLoseData(string response)
    {
        using var json = JsonDocument.Parse(response);
        Assert.Throws<InvalidDataException>(() => FoundryMistralDocumentReader.MapResult(json.RootElement, "id"));
    }

    [Fact]
    public async Task ReadAsync_RejectsUnsupportedAndOversizedDocumentsBeforeRequest()
    {
        var requests = 0;
        using var client = Client((_, _) => { requests++; return new HttpResponseMessage(HttpStatusCode.OK); });
        var reader = new FoundryMistralDocumentReader(client, "deployment");
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            reader.ReadAsync(new MemoryStream([1]), "id", "text/plain"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            reader.ReadAsync(new MemoryStream([1], writable: false) { Position = 0 }, "id", ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reader.ReadAsync(new RepeatingStream(20 * 1024 * 1024 + 1), "id", "image/png"));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ReadAsync_CancelsStreamReadAndRetryDelay()
    {
        using var client = Client((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var reader = new FoundryMistralDocumentReader(client, "deployment");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reader.ReadAsync(new MemoryStream([1]), "id", "image/jpeg", cancelled.Token));

        using var duringRetry = new CancellationTokenSource();
        using var retryClient = Client((_, _) =>
        {
            duringRetry.Cancel();
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FoundryMistralDocumentReader(retryClient, "deployment")
                .ReadAsync(new MemoryStream([1]), "id", "image/jpeg", duringRetry.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ReadAsync_StatusErrorsDoNotExposeResponseOrReason(HttpStatusCode status)
    {
        using var client = Client((_, _) => new HttpResponseMessage(status)
        {
            ReasonPhrase = "SECRET-REASON",
            Content = new StringContent("SECRET-BODY"),
        });
        var reader = new FoundryMistralDocumentReader(client, "deployment");
        using var cancellation = new CancellationTokenSource();
        if (status == HttpStatusCode.ServiceUnavailable)
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            reader.ReadAsync(new MemoryStream([1]), "id", "image/png", cancellation.Token));
        Assert.DoesNotContain("SECRET", exception.ToString());
        if (status == HttpStatusCode.BadRequest)
            Assert.Contains("400", exception.Message);
    }

    [Fact]
    public async Task ReadAsync_BoundsResponseAndRejectsMalformedJson()
    {
        using var oversizedClient = Client((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[32 * 1024 * 1024 + 1]),
        });
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new FoundryMistralDocumentReader(oversizedClient, "deployment")
                .ReadAsync(new MemoryStream([1]), "id", "image/tiff"));
        using var invalidClient = Client((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-json SECRET"),
        });
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new FoundryMistralDocumentReader(invalidClient, "deployment")
                .ReadAsync(new MemoryStream([1]), "id", "image/heic"));
        Assert.DoesNotContain("SECRET", exception.ToString());
    }

    [Fact]
    public async Task ReadAsync_TransportErrorDoesNotRevealRequestDetails()
    {
        using var client = Client((_, _) => throw new HttpRequestException("SECRET-KEY"));
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new FoundryMistralDocumentReader(client, "deployment")
                .ReadAsync(new MemoryStream([1]), "id", "application/pdf"));
        Assert.DoesNotContain("SECRET", exception.ToString());
    }

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) =>
        new(new StubHandler(handler)) { BaseAddress = new Uri("https://example.invalid/resource/") };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(handler(request, token));
    }

    private sealed class RepeatingStream(long length) : Stream
    {
        private long _remaining = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(_remaining, buffer.Length);
            buffer.Span[..count].Fill(1);
            _remaining -= count;
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
