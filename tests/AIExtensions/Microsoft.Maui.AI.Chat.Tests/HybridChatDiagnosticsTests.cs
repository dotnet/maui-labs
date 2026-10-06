using System.ClientModel;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using static Microsoft.Maui.AI.Chat.Tests.HybridChatClientTests;

namespace Microsoft.Maui.AI.Chat.Tests;

[Collection("Chat diagnostics")]
public sealed class HybridChatDiagnosticsTests
{
    [Theory]
    [InlineData(false, "local")]
    [InlineData(true, "local")]
    [InlineData(false, "cloud")]
    [InlineData(true, "cloud")]
    [InlineData(false, "fallback")]
    [InlineData(true, "fallback")]
    [InlineData(false, "unconfigured")]
    [InlineData(true, "unconfigured")]
    public async Task BuiltInLeafDiagnostics_ExposeEveryAttemptWithoutPayloads(bool streaming, string route)
    {
        const string variable = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "true");
            using var receiver = new ChatDiagnostics();
            using var factory = CreateFactory(receiver);
            var local = new FakeClient("apple-intelligence")
            {
                Decision = route == "local" ? LocalDecision : CloudDecision,
                Usage = new UsageDetails { InputTokenCount = 11, OutputTokenCount = 7 },
            };
            var cloud = new FakeClient("actual-cloud-model")
            {
                Failure = route == "fallback" ? RetryExhaustion() : null,
                Usage = new UsageDetails { InputTokenCount = 11, OutputTokenCount = 7 },
            };
            using var client = CreateClient(local, route == "unconfigured" ? null : cloud, factory);
            var expectedModel = route == "cloud" ? "actual-cloud-model" : "apple-intelligence";
            Assert.Equal(expectedModel, await AnswerModel(client, streaming));

            var entries = receiver.Snapshot().Entries;
            var spans = entries.Where(entry => entry.Heading.Contains("| Telemetry |")).ToArray();
            var expectedCount = route == "fallback" ? 4 : route == "unconfigured" ? 2 : 3;
            Assert.Equal(expectedCount, spans.Length);
            Assert.Equal(route == "unconfigured" ? 0 : 1, local.ClassifierCalls);
            Assert.Equal(route is "cloud" or "fallback" ? 1 : 0, cloud.AnswerCalls + cloud.StreamCalls);
            Assert.Equal(route == "cloud" ? 0 : 1, local.AnswerCalls + local.StreamCalls);
            Assert.Contains($"gen_ai.response.model: {expectedModel}", spans[^1].Details);
            Assert.Equal(route == "cloud" ? 1 : route == "unconfigured" ? 2 : 3,
                spans.Count(span => span.Details.Contains("gen_ai.response.model: apple-intelligence")));
            if (route == "cloud")
            {
                Assert.Equal(2, spans.Count(span => span.Details.Contains("gen_ai.response.model: actual-cloud-model")));
                Assert.Equal(Summary, Assert.Single(cloud.ReceivedMessages!).Text);
            }
            if (route == "fallback")
            {
                Assert.Contains("| Error", spans[1].Message);
                Assert.Contains("AggregateException", spans[1].Details);
                Assert.Contains(entries, entry => entry.Heading.Contains("| Error |") &&
                    entry.Details.Contains("ClientResultException"));
                Assert.Equal("private-original-prompt", Assert.Single(local.ReceivedMessages!).Text);
            }
            Assert.Equal(expectedCount * 2, entries.Count(entry => entry.Heading.Contains("LoggingChatClient")));
            AssertTraceAndPrivacy(entries, spans, expectUsage: true);
            Assert.Equal(0, local.DisposeCalls + cloud.DisposeCalls);
            client.Dispose();
            Assert.Equal(1, local.DisposeCalls);
            Assert.Equal(route == "unconfigured" ? 0 : 1, cloud.DisposeCalls);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamingFailureAfterAnyOutput_ReportsCloudFailureWithoutLocalRecovery(bool emptyUpdate)
    {
        using var receiver = new ChatDiagnostics();
        using var factory = CreateFactory(receiver);
        var local = new FakeClient("apple-intelligence") { Decision = CloudDecision };
        var failure = RetryExhaustion();
        var cloud = new FakeClient("actual-cloud-model")
        {
            FailureAfterFirstUpdate = failure,
            EmptyUpdate = emptyUpdate,
        };
        using var client = CreateClient(local, cloud, factory);
        var updates = new List<ChatResponseUpdate>();
        Assert.Same(failure, await Record.ExceptionAsync(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync(
                [new(ChatRole.User, "private-original-prompt")]))
                updates.Add(update);
        }));
        Assert.Equal("actual-cloud-model", Assert.Single(updates).ModelId);
        Assert.Equal(0, local.AnswerCalls + local.StreamCalls);
        var entries = receiver.Snapshot().Entries;
        var spans = entries.Where(entry => entry.Heading.Contains("| Telemetry |")).ToArray();
        Assert.Equal(3, spans.Length);
        Assert.Equal(2, spans.Count(span => span.Message.Contains("| Error")));
        AssertTraceAndPrivacy(entries, spans);
    }

    private static ILoggerFactory CreateFactory(ChatDiagnostics receiver) =>
        LoggerFactory.Create(builder => builder.AddProvider(receiver)
            .AddFilter<ChatDiagnostics>("Microsoft.Extensions.AI", LogLevel.Debug));

    private static IChatClient CreateClient(FakeClient local, FakeClient? cloud, ILoggerFactory factory)
    {
        var localLeaf = local.AsBuilder().UsePlaygroundDiagnostics(factory).Build();
        var cloudLeaf = cloud?.AsBuilder().UsePlaygroundDiagnostics(factory).Build();
        Assert.False(localLeaf.GetService<OpenTelemetryChatClient>()!.EnableSensitiveData);
        if (cloudLeaf is not null)
            Assert.False(cloudLeaf.GetService<OpenTelemetryChatClient>()!.EnableSensitiveData);
        var client = new HybridChatClient(localLeaf, cloudLeaf)
            .AsBuilder().UsePlaygroundDiagnostics(factory).Build();
        Assert.False(client.GetService<OpenTelemetryChatClient>()!.EnableSensitiveData);
        return client;
    }

    private static AggregateException RetryExhaustion() =>
        new(new ClientResultException("SDK retry exhausted", innerException: new HttpRequestException("transport unavailable")),
            new TimeoutException("SDK transport timeout"));

    private static async Task<string?> AnswerModel(IChatClient client, bool streaming)
    {
        ChatMessage[] messages = [new(ChatRole.User, "private-original-prompt")];
        if (!streaming)
            return (await client.GetResponseAsync(messages)).ModelId;
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(messages))
            updates.Add(update);
        return Assert.Single(updates).ModelId;
    }

    private static void AssertTraceAndPrivacy(ChatDiagnosticEntry[] entries, ChatDiagnosticEntry[] spans,
        bool expectUsage = false)
    {
        var trace = spans[^1].Details.Split('\n')[0].Split(" | ")[0];
        Assert.All(entries, entry => Assert.Contains(trace, entry.Details));
        Assert.Equal(spans.Length, spans.Select(span => span.Details.Split('\n')[0]).Distinct().Count());
        var text = string.Join("\n", entries.Select(entry => entry.Heading + entry.Message + entry.Details));
        foreach (var payload in new[] { "private-original-prompt", Summary, "Simple greeting", "Complex task",
            "routing classifier", "Transient cloud failure before output; answering locally." })
            Assert.DoesNotContain(payload, text);
        Assert.DoesNotContain("| Trace |", text);
        Assert.DoesNotContain("| Warning |", text);
        if (expectUsage)
        {
            Assert.All(spans.Where(span => !span.Message.Contains("| Error")), span =>
            {
                Assert.Contains("gen_ai.usage.input_tokens: 11", span.Details);
                Assert.Contains("gen_ai.usage.output_tokens: 7", span.Details);
            });
        }
        else
            Assert.DoesNotContain("gen_ai.usage", text);
        Assert.All(entries, entry => Assert.True(entry.Heading.Contains("| Telemetry |") ||
            entry.Heading.Contains("Microsoft.Extensions.AI.LoggingChatClient")));
    }
}
