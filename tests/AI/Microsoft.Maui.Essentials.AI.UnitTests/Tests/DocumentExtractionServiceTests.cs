using System.Text.Json;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class DocumentExtractionServiceTests
{
    [Fact]
    public async Task ExtractAsync_UsesCompletedExchangeWithoutReadingNativeObjectsUntilInspection()
    {
        using var client = new Client();
        var result = await Run(client);
        Assert.Equal(2, result.PageCount);
        Assert.Contains("ExtractionDocumentTableElement", result.Output);
        Assert.Contains("\"IsCovered\": true", result.Output);
        Assert.Contains("\"State\": \"covered\"", result.Output);
        Assert.Contains("\"State\": \"empty\"", result.Output);
        Assert.Contains("\"RowSpan\": 2", result.Output);
        Assert.Contains("\"ColumnSpan\": 2", result.Output);
        Assert.Contains("actual-role", result.Output);
        Assert.Contains("0.123456789012345", result.Output);
        Assert.DoesNotContain("nativeOnly", result.Output);
        Assert.True(result.HasRawOutput);
        Assert.Equal(0, client.NativeReads);
        Assert.False(client.Input!.CanRead);
        var raw = await result.InspectRawAsync();
        Assert.Contains("nativeOnly", raw);
        Assert.Equal(1, client.NativeReads);
        Assert.Equal(raw, await result.InspectRawAsync());
        Assert.Equal(2, client.NativeReads);
        Assert.True(client.Borrowed.CanRead);
    }

    [Fact]
    public async Task ExtractAsync_NativeInspectionShowsCompleteStructuredOutput()
    {
        using var client = new Client { LargeRaw = true };
        var result = await Run(client);
        var raw = await result.InspectRawAsync();
        using var json = JsonDocument.Parse(raw);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(new string('x', 500_000), json.RootElement[0].GetProperty("nativeOnly").GetString());
    }

    [Fact]
    public async Task ExtractAsync_NativeInspectionReportsGetterFailureWithoutAffectingSemanticResult()
    {
        using var client = new Client { ThrowRaw = true };
        var result = await Run(client);
        Assert.Equal(0, client.NativeReads);
        Assert.Equal(2, result.PageCount);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => result.InspectRawAsync());
        Assert.Contains("Native getter failure.", error.Message);
        Assert.DoesNotContain("inspection failed", result.Output);
        client.ThrowRaw = false;
        Assert.Contains("nativeOnly", await result.InspectRawAsync());
        Assert.Equal(2, client.NativeReads);
    }

    [Fact]
    public async Task ExtractAsync_PassesOptionsDirectlyToTheClient()
    {
        using var client = new Client();
        var options = new DocumentExtractionOptions { ExtractorName = "deployment" };
        await new DocumentExtractionService().ExtractAsync(client, new("input.png", "image/png", [1]), options);
        Assert.Same(options, client.Options);
    }

    [Fact]
    public async Task ExtractAsync_NoNativeOutputIsNotInvented()
    {
        using var client = new Client { NoNative = true };
        var result = await Run(client);
        Assert.Contains("0.123456789012345", result.Output);
        Assert.DoesNotContain("nativeOnly", await result.InspectRawAsync());
        Assert.Equal(0, client.NativeReads);
    }

    [Fact]
    public async Task ExtractionOrInspectionCancellationIsPreservedWithoutDisposingBorrowedObjects()
    {
        using var client = new Client();
        var result = await Run(client);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result.InspectRawAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DocumentExtractionService().ExtractAsync(client, new("input.png", "image/png", [1]), cancellationToken: cancellation.Token));
        Assert.True(client.Borrowed.CanRead);
    }

    [Fact]
    public async Task ExtractAsync_CyclesFailExplicitlyAndBorrowedNativeIsNotDisposed()
    {
        using var client = new Client { Cyclic = true };
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(client));
        Assert.True(client.Borrowed.CanRead);
    }

    private static Task<DocumentExtractionRunResult> Run(Client client) =>
        new DocumentExtractionService().ExtractAsync(client, new("input.png", "image/png", [1]));

    private sealed class Client : DocumentExtractionClient
    {
        public Stream? Input { get; private set; }
        public DocumentExtractionOptions? Options { get; private set; }
        public int NativeReads { get; private set; }
        public MemoryStream Borrowed { get; } = new([1]);
        public bool LargeRaw { get; init; }
        public bool ThrowRaw { get; set; }
        public bool NoNative { get; init; }
        public bool Cyclic { get; init; }
        public override Task<ExtractionDocument> ExtractAsync(
            Stream document, string identifier, string mediaType, DocumentExtractionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Input = document;
            Options = options;
            cancellationToken.ThrowIfCancellationRequested();
            var result = new ExtractionDocument(identifier)
            {
                RawRepresentation = NoNative ? null : new Native(() =>
                {
                    NativeReads++;
                    if (ThrowRaw) throw new InvalidOperationException("Native getter failure.");
                    return LargeRaw ? new string('x', 500_000) : "raw";
                }),
            };
            for (var number = 1; number <= 2; number++)
            {
                var frame = new ExtractionDocumentSourcePage($"page/{number}", number, new(1, 1),
                    ExtractionDocumentCoordinateUnit.Normalized, ExtractionDocumentCoordinateOrigin.BottomLeft);
                result.SourcePages.Add(frame);
                var section = new ExtractionDocumentSection();
                var table = new ExtractionDocumentTableElement(null, 2, 2);
                var cell = new ExtractionDocumentTableCell(2, 2, "actual-role");
                cell.Sources.Add(new(frame, [new(0.123456789012345, 0), new(1, 0), new(1, 1)]));
                table.Cells[0, 0] = cell;
                section.Elements.Add(table);
                if (Cyclic)
                    section.Elements.Add(section);
                result.Sections.Add(section);
            }
            return Task.FromResult(result);
        }
    }
    private sealed class Native(Func<string> getter)
    {
        public string nativeOnly => getter();
    }
}
