using Microsoft.Extensions.DataIngestion;
using Xunit;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class DocumentReadingServiceTests
{
    [Fact]
    public async Task ReadAsync_ForwardsInputAndFormatsStandardElements()
    {
        Stream? captured = null;
        using var cancellation = new CancellationTokenSource();
        var reader = new StubReader(async (source, identifier, mediaType, token) =>
        {
            captured = source;
            Assert.Equal("selected.pdf", identifier);
            Assert.Equal("application/pdf", mediaType);
            Assert.Equal(cancellation.Token, token);
            Assert.False(source.CanWrite);
            using var copy = new MemoryStream();
            await source.CopyToAsync(copy, token);
            Assert.Equal(new byte[] { 1, 2, 3 }, copy.ToArray());
            var document = new IngestionDocument(identifier);
            var section = new IngestionDocumentSection { PageNumber = 1 };
            section.Elements.Add(new IngestionDocumentHeader("# Heading") { Text = "Heading", PageNumber = 1 });
            section.Elements.Add(new IngestionDocumentParagraph("**Body**") { Text = "Body", PageNumber = 1 });
            section.Elements.Add(new IngestionDocumentTable("| Value |", new IngestionDocumentElement?[1, 1])
            {
                Text = "Value",
                PageNumber = 1,
            });
            document.Sections.Add(section);
            return document;
        });

        var result = await new DocumentReadingService().ReadAsync(
            reader, new SelectedDocument("selected.pdf", "application/pdf", [1, 2, 3]), cancellation.Token);

        Assert.Equal(1, result.PageCount);
        Assert.Contains("Document: selected.pdf", result.Output);
        Assert.Contains("Page 1", result.Output);
        Assert.Contains("Header: Heading", result.Output);
        Assert.Contains("Paragraph: Body", result.Output);
        Assert.Contains("Table: Value", result.Output);
        Assert.Contains("**Body**", result.Output);
        Assert.NotNull(captured);
        Assert.False(captured.CanRead);
    }

    [Fact]
    public async Task ReadAsync_CancellationPreventsResultsEvenWhenReaderIgnoresToken()
    {
        using var cancellation = new CancellationTokenSource();
        var reader = new StubReader((_, identifier, _, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(new IngestionDocument(identifier));
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DocumentReadingService().ReadAsync(
            reader, new SelectedDocument("input.png", "image/png", [1]), cancellation.Token));
    }

    [Fact]
    public async Task ReadAsync_PreCancelledRequestDoesNotInvokeReader()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var reader = new StubReader((_, _, _, _) => throw new InvalidOperationException("Must not run."));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DocumentReadingService().ReadAsync(
            reader, new SelectedDocument("input.png", "image/png", [1]), cancellation.Token));
    }

    [Fact]
    public async Task ReadAsync_ReaderFailurePropagatesWithoutSuccessResult()
    {
        var expected = new InvalidDataException("Invalid document.");
        var reader = new StubReader((_, _, _, _) => throw expected);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new DocumentReadingService().ReadAsync(
            reader, new SelectedDocument("input.png", "image/png", [1])));
        Assert.Same(expected, error);
    }

    private sealed class StubReader(Func<Stream, string, string, CancellationToken, Task<IngestionDocument>> read)
        : IngestionDocumentReader
    {
        public override Task<IngestionDocument> ReadAsync(
            Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default) =>
            read(source, identifier, mediaType, cancellationToken);
    }
}
