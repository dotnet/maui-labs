using System.Diagnostics;
using System.Runtime.CompilerServices;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.AI.Chat.Tests;

[CollectionDefinition("Chat diagnostics", DisableParallelization = true)]
public sealed class ChatDiagnosticsCollection;

[Collection("Chat diagnostics")]
public sealed class ChatDiagnosticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuiltInMiddleware_CapturesLifecycleAndActualModelWithoutPayloads(bool streaming)
    {
        using var receiver = new ChatDiagnostics();
        using var factory = LoggerFactory.Create(builder =>
            builder.AddProvider(receiver).AddFilter<ChatDiagnostics>("Microsoft.Extensions.AI", LogLevel.Debug));
        using var client = new FakeClient().AsBuilder()
            .UsePlaygroundTelemetry().UseLogging(factory).Build();
        await Invoke(client, streaming);

        var entries = receiver.Snapshot().Entries;
        Assert.Equal(2, entries.Count(entry => entry.Heading.Contains("| Debug |")));
        var span = Assert.Single(entries, entry => entry.Heading.Contains("| Telemetry |"));
        Assert.Contains("gen_ai.request.model: requested-model", span.Details);
        Assert.Contains("gen_ai.response.model: actual-model", span.Details);
        Assert.Contains("gen_ai.response.model: actual-model", span.Message);
        Assert.Contains("gen_ai.usage.input_tokens: 11", span.Details);
        Assert.Contains("gen_ai.usage.output_tokens: 7", span.Details);
        Assert.Contains(" ms |", span.Message);
        var trace = span.Details.Split('\n')[0].Split(" | ")[0];
        Assert.All(entries, entry => Assert.Contains(trace, entry.Details));
        Assert.DoesNotContain(entries, entry => (entry.Message + entry.Details).Contains("private-prompt"));
        Assert.DoesNotContain(entries, entry => (entry.Message + entry.Details).Contains("private-answer"));
        Assert.DoesNotContain(entries, entry => entry.Heading.Contains("| Trace |"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task BuiltInMiddleware_CapturesFailureAndCancellation(bool streaming, bool canceled)
    {
        using var receiver = new ChatDiagnostics();
        using var factory = LoggerFactory.Create(builder =>
            builder.AddProvider(receiver).SetMinimumLevel(LogLevel.Debug));
        using var client = new FakeClient(canceled ? new OperationCanceledException("canceled")
            : new InvalidOperationException("leaf failure")).AsBuilder()
            .UsePlaygroundTelemetry().UseLogging(factory).Build();
        await Assert.ThrowsAnyAsync<Exception>(() => Invoke(client, streaming));
        var entries = receiver.Snapshot().Entries;
        Assert.Contains(entries, entry => entry.Heading.Contains(canceled ? "| Debug |" : "| Error |")
            && (entry.Message + entry.Details).Contains(canceled ? "cancel" : "leaf failure",
                StringComparison.OrdinalIgnoreCase));
        var span = Assert.Single(entries, entry => entry.Heading.Contains("| Telemetry |"));
        if (!canceled)
            Assert.Contains("| Error", span.Message);
    }

    [Fact]
    public async Task Buffer_BoundsConcurrentWritesClearAndDisposal()
    {
        var receiver = new ChatDiagnostics();
        var logger = receiver.CreateLogger("Microsoft.Extensions.AI.LoggingChatClient");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(writer => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                logger.LogDebug("writer {Writer} item {Item}", writer, i);
                if (i % 37 == 0)
                    receiver.Clear();
            }
        })));
        for (var i = 0; i < 600; i++)
            logger.LogDebug("entry {Index}", i);
        var snapshot = receiver.Snapshot();
        Assert.Equal(ChatDiagnostics.Capacity, snapshot.Entries.Length);
        Assert.Equal(snapshot.Entries.OrderBy(entry => entry.Sequence), snapshot.Entries);
        Assert.Equal("entry 100", snapshot.Entries[0].Message);
        receiver.Clear();
        Assert.Empty(receiver.Snapshot().Entries);
        Assert.Equal(ChatDiagnostics.Capacity, snapshot.Entries.Length);
        logger.LogError(new InvalidOperationException(new string('x', 20_000)), "important failure");
        Assert.Contains("important failure", Assert.Single(receiver.Snapshot().Entries).Message);
        Assert.Contains("[truncated]", Assert.Single(receiver.Snapshot().Entries).Details);
        using var source = new ActivitySource(ChatDiagnostics.SourceName);
        using var pending = source.StartActivity("in flight");
        receiver.Dispose();
        var disposedVersion = receiver.Snapshot().Version;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            logger.LogError("after disposal");
            receiver.Clear();
            receiver.Dispose();
        })));
        pending?.Stop();
        Assert.False(logger.IsEnabled(LogLevel.Error));
        Assert.Empty(receiver.Snapshot().Entries);
        Assert.Equal(disposedVersion, receiver.Snapshot().Version);
        Assert.Null(source.StartActivity("after disposal"));
    }

    [Fact]
    public void Receiver_IgnoresOtherSourcesAndTraceAndSnapshotsTags()
    {
        using var receiver = new ChatDiagnostics();
        receiver.CreateLogger("Other.Category").LogError("ignored");
        receiver.CreateLogger("Microsoft.Extensions.AI.LoggingChatClient").LogTrace("private-prompt");
        using var other = new ActivitySource("Other.Source");
        Assert.Null(other.StartActivity("ignored"));
        using var source = new ActivitySource(ChatDiagnostics.SourceName);
        using var activity = source.StartActivity("observed")!;
        activity.SetTag("gen_ai.response.model", "emitted-model");
        activity.Stop();
        activity.SetTag("gen_ai.response.model", "mutated-model");
        var entry = Assert.Single(receiver.Snapshot().Entries);
        Assert.Contains("emitted-model", entry.Details);
        Assert.DoesNotContain("mutated-model", entry.Details);
        Assert.DoesNotContain("gen_ai.usage", entry.Details);
    }

    private static async Task Invoke(IChatClient client, bool streaming)
    {
        var messages = new[] { new ChatMessage(ChatRole.User, "private-prompt") };
        var options = new ChatOptions { ModelId = "requested-model" };
        if (streaming)
        {
            await foreach (var _ in client.GetStreamingResponseAsync(messages, options)) { }
        }
        else
            await client.GetResponseAsync(messages, options);
    }

    [Fact]
    public async Task Telemetry_ExplicitlyDisablesEnvironmentEnabledMessageCapture()
    {
        const string variable = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "true");
            using var receiver = new ChatDiagnostics();
            using var client = new FakeClient().AsBuilder().UsePlaygroundTelemetry().Build();
            Assert.False(client.GetService<OpenTelemetryChatClient>()!.EnableSensitiveData);
            await Invoke(client, streaming: false);
            var span = Assert.Single(receiver.Snapshot().Entries);
            Assert.DoesNotContain("private-prompt", span.Details);
            Assert.DoesNotContain("private-answer", span.Details);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    private sealed class FakeClient(Exception? failure = null) : IChatClient
    {
        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(ChatClientMetadata)
                ? new ChatClientMetadata("fake", new Uri("https://example.invalid"), "default-model") : null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (failure is not null)
                throw failure;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "private-answer"))
            {
                ModelId = "actual-model",
                Usage = new UsageDetails { InputTokenCount = 11, OutputTokenCount = 7 },
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (failure is not null)
                throw failure;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "private-answer") { ModelId = "actual-model" };
            yield return new ChatResponseUpdate
            {
                Contents = [new UsageContent(new UsageDetails { InputTokenCount = 11, OutputTokenCount = 7 })],
            };
        }

        public void Dispose() { }
    }
}
