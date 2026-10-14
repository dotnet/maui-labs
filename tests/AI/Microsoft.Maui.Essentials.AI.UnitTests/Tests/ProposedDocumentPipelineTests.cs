using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class ProposedDocumentPipelineTests
{
	[Fact]
	public async Task Builder_DelegationLoggingDiscoveryAndPerCallSnapshotUseOriginalPublicTypes()
	{
		var leaf = new TestClient();
		var logger = new CapturingLogger();
		using var pipeline = new DocumentExtractionClientBuilder(leaf)
			.Use(inner => new ForwardingClient(inner))
			.UseLogging(logger).Build();
		var borrowed = new object();
		var options = new DocumentExtractionOptions { ModelId = "request", CaptureRawOutput = false };
		options.AdditionalProperties["borrowed"] = borrowed;
		using var source = new MemoryStream([1]);
		using var result = await pipeline.ExtractAsync(source, "input", "image/png", options);
		Assert.Equal("input", result.Document.Identifier);
		Assert.NotSame(options, leaf.Options);
		Assert.NotSame(options.AdditionalProperties, leaf.Options!.AdditionalProperties);
		Assert.Same(borrowed, leaf.Options.AdditionalProperties["borrowed"]);
		Assert.False(leaf.Options.CaptureRawOutput);
		Assert.Equal("request", leaf.Options.ModelId);
		Assert.Same(leaf, pipeline.GetService(typeof(TestClient)));
		Assert.True(source.CanRead);
		Assert.NotEmpty(logger.Messages);
		Assert.DoesNotContain(logger.Messages, message => message.Contains("PRIVATE-TEXT", StringComparison.Ordinal));
		Assert.DoesNotContain(logger.Messages, message => message.Contains("RAW-SECRET", StringComparison.Ordinal));
	}

	[Fact]
	public async Task KeyedRegistrationBuildsMiddlewareAndDisposesInnerClient()
	{
		var services = new ServiceCollection();
		var leaf = new TestClient();
		services.AddKeyedDocumentExtractionClient("documents", _ => leaf)
			.Use(inner => new ForwardingClient(inner));
		using (var provider = services.BuildServiceProvider())
		{
			var client = provider.GetRequiredKeyedService<IDocumentExtractionClient>("documents");
			using var input = new MemoryStream([1]);
			using var result = await client.ExtractAsync(input, "input", "image/png");
			Assert.Single(result.Pages);
			Assert.Same(leaf, client.GetService(typeof(TestClient)));
			Assert.Null(client.GetService(typeof(TestClient), "other"));
		}
		Assert.True(leaf.Disposed);
	}

	[Fact]
	public async Task CoreExtraction_SnapshotsBeforeFirstPageAndCleansUpWhenCancelled()
	{
		var leaf = new TestClient();
		using var input = new MemoryStream([1]);
		using var cancellation = new CancellationTokenSource();
		var options = new DocumentExtractionOptions { ModelId = "initial" };
		var pending = leaf.ExtractAsync(input, "input", "image/png", options, cancellation.Token);
		options.ModelId = "changed";
		using var result = await pending;
		Assert.Equal("initial", leaf.Options!.ModelId);
		Assert.Single(result.Document.SourceOutputs);
		Assert.Equal(DocumentRawContentStatus.Retained, result.Document.SourceOutputs[0].NativeStatus);
	}

	private sealed class TestClient : IDocumentExtractionClient
	{
		public DocumentExtractionOptions? Options { get; private set; }
		public bool Disposed { get; private set; }
		public async IAsyncEnumerable<DocumentExtractionPage> ExtractPagesAsync(
			Stream document, string identifier, string mediaType, DocumentExtractionOptions? options = null,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			Options = options;
			await Task.Yield();
			cancellationToken.ThrowIfCancellationRequested();
			var output = new DocumentSourceOutput("operation", rawRepresentation: options?.CaptureRawOutput != false ? "RAW-SECRET" : null,
				captureDisabled: options?.CaptureRawOutput == false);
			yield return new(1, [new DocumentExtractionText("paragraph", 0, DocumentTextKind.Paragraph, "PRIVATE-TEXT")],
				sourceOutputs: [output]);
		}

		public object? GetService(Type serviceType, object? serviceKey = null) =>
			serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
		public void Dispose() => Disposed = true;
	}

	private sealed class ForwardingClient(IDocumentExtractionClient inner) : DelegatingDocumentExtractionClient(inner);

	private sealed class CapturingLogger : ILoggerFactory, ILogger
	{
		public List<string> Messages { get; } = [];
		public ILogger CreateLogger(string categoryName) => this;
		public void AddProvider(ILoggerProvider provider) { }
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			Messages.Add(formatter(state, exception));
		public void Dispose() { }
	}
}
