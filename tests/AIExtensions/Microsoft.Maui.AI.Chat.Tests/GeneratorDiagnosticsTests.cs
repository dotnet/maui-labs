using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.AI.Chat.Tests;

[Collection("Chat diagnostics")]
public sealed class GeneratorDiagnosticsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("failure")]
    [InlineData("canceled")]
    public async Task Embeddings_BuiltInMiddlewareCapturesCorrelatedLogsAndSpan(string? outcome)
    {
        using var receiver = new ChatDiagnostics();
        using var factory = CreateFactory(receiver);
        using var generator = new FakeEmbeddings(Failure(outcome)).AsBuilder()
            .UsePlaygroundTelemetry().UseLogging(factory).Build();
        var request = generator.GenerateAsync(["private-input"], new() { ModelId = "requested-model" });
        if (outcome is null)
            await request;
        else
            await Assert.ThrowsAnyAsync<Exception>(() => request);

        var entries = receiver.Snapshot().Entries;
        var span = Assert.Single(entries, entry => entry.Heading.Contains("| Telemetry |"));
        Assert.NotNull(span.TraceId);
        Assert.All(entries, entry => Assert.Equal(span.TraceId, entry.TraceId));
        Assert.Contains("gen_ai.operation.name: embeddings", span.Details);
        Assert.Contains("gen_ai.request.model: requested-model", span.Details);
        if (outcome is null)
        {
            Assert.Contains("gen_ai.response.model: actual-embedding-model", span.Details);
            Assert.Contains("gen_ai.usage.input_tokens: 11", span.Details);
            Assert.Contains(entries, entry => entry.Message == "GenerateAsync generated 1 embedding(s).");
        }
        else
            Assert.Contains(entries, entry => entry.Message.Contains(outcome == "canceled" ? "canceled" : "failed"));
        Assert.DoesNotContain(entries, entry => (entry.Message + entry.Details).Contains("private-input"));
    }

    [Fact]
    public async Task Embeddings_ExplicitlyDisableEnvironmentEnabledSensitiveOptionsAndResponseTags()
    {
        const string variable = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "true");
            using var receiver = new ChatDiagnostics();
            using var generator = new FakeEmbeddings().AsBuilder().UsePlaygroundTelemetry().Build();
            Assert.False(generator.GetService<OpenTelemetryEmbeddingGenerator<string, Embedding<float>>>()!.EnableSensitiveData);
            await generator.GenerateAsync(["private-input"], new()
            {
                AdditionalProperties = new() { ["private-option"] = "private-value" },
            });
            var span = Assert.Single(receiver.Snapshot().Entries);
            Assert.DoesNotContain("private", span.Message + span.Details);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("failure")]
    [InlineData("canceled")]
    public async Task Images_BuiltInLoggingCapturesLifecycleWithoutPayloadsOrInventedSpans(string? outcome)
    {
        using var receiver = new ChatDiagnostics();
        using var factory = CreateFactory(receiver);
        using var generator = new FakeImages(Failure(outcome)).AsBuilder().UseLogging(factory).Build();
        var request = generator.GenerateAsync(new("private-image-prompt"));
        if (outcome is null)
            await request;
        else
            await Assert.ThrowsAnyAsync<Exception>(() => request);

        var entries = receiver.Snapshot().Entries;
        Assert.Equal(2, entries.Length);
        Assert.All(entries, entry =>
        {
            Assert.Contains("LoggingImageGenerator", entry.Heading);
            Assert.Null(entry.TraceId);
            Assert.DoesNotContain("private-image", entry.Message + entry.Details);
        });
        Assert.Contains(entries, entry => entry.Message.Contains("invoked"));
        Assert.Contains(entries, entry => entry.Message.Contains(
            outcome is null ? "completed" : outcome == "canceled" ? "canceled" : "failed"));
    }

    private static ILoggerFactory CreateFactory(ChatDiagnostics receiver) => LoggerFactory.Create(builder =>
        builder.AddProvider(receiver).AddFilter<ChatDiagnostics>("Microsoft.Extensions.AI", LogLevel.Debug));

    private static Exception? Failure(string? outcome) => outcome switch
    {
        "failure" => new InvalidOperationException("leaf failure"),
        "canceled" => new OperationCanceledException("canceled"),
        _ => null,
    };

    private sealed class FakeEmbeddings(Exception? failure = null) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            failure is not null ? Task.FromException<GeneratedEmbeddings<Embedding<float>>>(failure)
                : Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                    [new(new float[] { 1, 0 }) { ModelId = "actual-embedding-model" }])
                {
                    Usage = new() { InputTokenCount = 11 },
                    AdditionalProperties = new() { ["private-response"] = "private-value" },
                });

        public void Dispose() { }
    }

    private sealed class FakeImages(Exception? failure = null) : IImageGenerator
    {
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ImageGenerationResponse> GenerateAsync(ImageGenerationRequest request,
            ImageGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            failure is not null ? Task.FromException<ImageGenerationResponse>(failure)
                : Task.FromResult(new ImageGenerationResponse([new DataContent(new byte[] { 1 }, "image/png")]));

        public void Dispose() { }
    }
}
