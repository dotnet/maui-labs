using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class ProposedDocumentPipelineTests
{
    [Fact]
    public async Task Builder_DelegationLoggingDiscoveryAndPerCallOptionsUseOriginalPublicTypes()
    {
        var leaf = new TestClient();
        var logger = new CapturingLogger();
        using var pipeline = new DocumentExtractionClientBuilder(leaf)
            .Use(inner => new ForwardingClient(inner)).UseLogging(logger).Build();
        var borrowed = new object();
        var options = new DocumentExtractionOptions { ExtractorName = "request" };
        options.AdditionalProperties["borrowed"] = borrowed;
        using var source = new MemoryStream([1]);
        var result = await pipeline.ExtractAsync(source, "input", "image/png", options);
        Assert.Equal("input", result.Identifier);
        Assert.Same(borrowed, leaf.Options!.AdditionalProperties["borrowed"]);
        Assert.Equal("request", leaf.Options.ExtractorName);
        Assert.Same(leaf, pipeline.GetService(typeof(TestClient)));
        Assert.Null(pipeline.GetService(typeof(TestClient), "other"));
        Assert.True(source.CanRead);
        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("PRIVATE", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("RAW-SECRET", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeyedRegistrationBuildsMiddlewareAndDisposesInnerClient()
    {
        var services = new ServiceCollection();
        var leaf = new TestClient();
        services.AddKeyedDocumentExtractionClient("documents", _ => leaf).Use(inner => new ForwardingClient(inner));
        using (var provider = services.BuildServiceProvider())
        {
            var client = provider.GetRequiredKeyedService<DocumentExtractionClient>("documents");
            using var input = new MemoryStream([1]);
            var result = await client.ExtractAsync(input, "input", "image/png");
            Assert.Single(result.Sections);
            Assert.Same(leaf, client.GetService(typeof(TestClient)));
        }
        Assert.True(leaf.Disposed);
    }

    [Fact]
    public async Task Logging_PreservesPrimaryFailureAndDoesNotLogSensitiveExceptionMessages()
    {
        var logger = new CapturingLogger();
        var failure = new InvalidDataException("PRIVATE provider failure");
        using var client = new LoggingDocumentExtractionClient(new TestClient { Failure = failure }, logger);
        using var input = new MemoryStream([1]);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidDataException>(() => client.ExtractAsync(input, "input", "image/png")));
        Assert.True(input.CanRead);
        Assert.Contains(logger.Messages, message => message.Contains("failed", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("PRIVATE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Logging_PreservesCancellation()
    {
        var logger = new CapturingLogger();
        using var pipeline = new LoggingDocumentExtractionClient(new TestClient(), logger);
        using var input = new MemoryStream([1]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pipeline.ExtractAsync(input, "input", "image/png", cancellationToken: cancellation.Token));
        Assert.Contains(logger.Messages, message => message.Contains("canceled", StringComparison.Ordinal));
    }

    private sealed class TestClient : DocumentExtractionClient
    {
        public DocumentExtractionOptions? Options { get; private set; }
        public bool Disposed { get; private set; }
        public Exception? Failure { get; init; }
        public override async Task<ExtractionDocument> ExtractAsync(
            Stream document, string identifier, string mediaType, DocumentExtractionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Options = options;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is { } failure)
                throw failure;
            var result = new ExtractionDocument(identifier) { RawRepresentation = "RAW-SECRET" };
            var section = new ExtractionDocumentSection();
            section.Elements.Add(new ExtractionDocumentTextElement(new TextContent("PRIVATE-TEXT")));
            result.Sections.Add(section);
            return result;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class ForwardingClient(DocumentExtractionClient inner) : DelegatingDocumentExtractionClient(inner);
    private sealed class CapturingLogger : ILoggerFactory, ILogger
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
        public void Dispose() { }
    }
}
