using System.Runtime.CompilerServices;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class DocumentExtractionRunnerTests
{
    [Theory]
    [InlineData("scan.png", "image/png")]
    [InlineData("photo.JPEG", "image/jpeg")]
    [InlineData("page.heic", "image/heic")]
    [InlineData("archive.tiff", "image/tiff")]
    [InlineData("report.PDF", "application/pdf")]
    [InlineData("notes.txt", null)]
    public void GetMediaType_UsesSupportedDocumentExtensions(
        string fileName,
        string? expected) =>
        Assert.Equal(expected, DocumentExtractionRunner.GetMediaType(fileName));

    [Fact]
    public async Task ExtractAsync_StreamsPagesAndReportsProgress()
    {
        var client = new TestDocumentClient(
        [
            new DocumentExtractionPageResult(new DocumentPage(1, "first"))
            {
                PagesProcessed = 1,
                TotalPages = 2,
            },
            new DocumentExtractionPageResult(new DocumentPage(2, "second"))
            {
                PagesProcessed = 2,
                TotalPages = 2,
            },
        ]);
        var provider = new TestProvider(client);
        var runner = new DocumentExtractionRunner(provider);
        var progress = new CollectingProgress<DocumentExtractionProgress>();
        var input = new DocumentInput("report.pdf", "application/pdf", [1, 2, 3]);
        var settings = new DocumentExtractionSettings(
            DetectBarcodes: false,
            AutomaticallyDetectLanguage: true);

        var result = await runner.ExtractAsync(input, settings, progress);

        Assert.Equal([1, 2], result.Pages.Select(static page => page.PageNumber));
        Assert.Equal(["first", "second"], result.Pages.Select(static page => page.Text));
        Assert.Equal([1, 2], progress.Values.Select(static update => update.PagesProcessed));
        Assert.All(progress.Values, static update => Assert.Equal(2, update.TotalPages));
        Assert.Equal("application/pdf", client.MediaType);
        Assert.Equal([1, 2, 3], client.Input);
        Assert.Equal(settings, provider.Settings);
        Assert.True(client.Disposed);
    }

    [Fact]
    public async Task ExtractAsync_UnavailableProvider_FailsBeforeCreatingClient()
    {
        var provider = new TestProvider(
            new TestDocumentClient([]),
            new("Unavailable", "No provider is registered.", IsAvailable: false));
        var runner = new DocumentExtractionRunner(provider);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            runner.ExtractAsync(
                new DocumentInput("page.png", "image/png", [1]),
                new(true, true)));

        Assert.Equal("No provider is registered.", exception.Message);
        Assert.False(provider.ClientCreated);
    }

    [Fact]
    public async Task ExtractAsync_CancellationStopsPageStream()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new TestDocumentClient(
            [],
            async cancellationToken =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });
        var runner = new DocumentExtractionRunner(new TestProvider(client));
        using var cancellation = new CancellationTokenSource();

        var extraction = runner.ExtractAsync(
            new DocumentInput("page.png", "image/png", [1]),
            new(true, true),
            cancellationToken: cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => extraction.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(client.Disposed);
    }

    private sealed class TestProvider : IDocumentExtractionProvider
    {
        private readonly TestDocumentClient _client;

        public TestProvider(
            TestDocumentClient client,
            DocumentProviderDescriptor? descriptor = null)
        {
            _client = client;
            Descriptor = descriptor ?? new("Test", "Test provider", IsAvailable: true);
        }

        public DocumentProviderDescriptor Descriptor { get; }
        public DocumentExtractionSettings? Settings { get; private set; }
        public bool ClientCreated { get; private set; }

        public IDocumentExtractionClient CreateClient(string mediaType)
        {
            ClientCreated = true;
            return _client;
        }

        public DocumentExtractionOptions CreateOptions(DocumentExtractionSettings settings)
        {
            Settings = settings;
            return new DocumentExtractionOptions();
        }

        public string GetCapabilitiesSummary() => "Test capabilities";
    }

    private sealed class TestDocumentClient(
        IReadOnlyList<DocumentExtractionPageResult> pages,
        Func<CancellationToken, Task>? beforeYield = null)
        : IDocumentExtractionClient
    {
        public byte[]? Input { get; private set; }
        public string? MediaType { get; private set; }
        public bool Disposed { get; private set; }

        public Task<DocumentExtractionResult> ExtractAsync(
            Stream document,
            string mediaType,
            DocumentExtractionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            ExtractPagesAsync(document, mediaType, options, cancellationToken)
                .ToDocumentExtractionResultAsync(cancellationToken);

        public async IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
            Stream document,
            string mediaType,
            DocumentExtractionOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var output = new MemoryStream();
            await document.CopyToAsync(output, cancellationToken);
            Input = output.ToArray();
            MediaType = mediaType;
            if (beforeYield is not null)
                await beforeYield(cancellationToken);
            foreach (var page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return page;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() => Disposed = true;
    }

    private sealed class CollectingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
