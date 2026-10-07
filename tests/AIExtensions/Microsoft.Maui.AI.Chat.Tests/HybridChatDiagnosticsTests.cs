using System.Text.Json;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
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
                Failure = route == "fallback" ? new IOException("Cloud unavailable") : null,
                Usage = new UsageDetails { InputTokenCount = 11, OutputTokenCount = 7 },
            };
            using var owner = CreateProvider(local, cloud, factory);
            var client = owner.GetRequiredService<IChatClient>();
            var expectedModel = route == "cloud" ? "actual-cloud-model" : "apple-intelligence";
            Assert.Equal(expectedModel, await AnswerModel(client, streaming));

            var entries = receiver.Snapshot().Entries;
            var spans = entries.Where(entry => entry.Heading.Contains("| Telemetry |")).ToArray();
            var expectedCount = route == "fallback" ? 4 : 3;
            Assert.Equal(expectedCount, spans.Length);
            Assert.Equal(1, local.ClassifierCalls);
            Assert.Equal(route is "cloud" or "fallback" ? 1 : 0, cloud.AnswerCalls + cloud.StreamCalls);
            Assert.Equal(route == "cloud" ? 0 : 1, local.AnswerCalls + local.StreamCalls);
            Assert.Contains($"gen_ai.response.model: {expectedModel}", spans[^1].Details);
            Assert.Equal(route == "cloud" ? 1 : 3,
                spans.Count(span => span.Details.Contains("gen_ai.response.model: apple-intelligence")));
            if (route == "cloud")
            {
                Assert.Equal(2, spans.Count(span => span.Details.Contains("gen_ai.response.model: actual-cloud-model")));
                Assert.Equal("private-original-prompt", Assert.Single(cloud.ReceivedMessages!).Text);
            }
            if (route == "fallback")
            {
                Assert.Contains("| Error", spans[1].Message);
                Assert.Contains("IOException", spans[1].Details);
                Assert.Contains(entries, entry => entry.Heading.Contains("| Error |") &&
                    entry.Details.Contains("IOException"));
                Assert.Equal("private-original-prompt", Assert.Single(local.ReceivedMessages!).Text);
            }
            Assert.Equal(expectedCount * 2, entries.Count(entry => entry.Heading.Contains("LoggingChatClient")));
            AssertTraceAndPrivacy(entries, spans, expectUsage: true);
            var decisions = entries.Where(entry => entry.Heading.Contains(ChatDiagnostics.HybridLogCategory)).ToArray();
            var decision = Assert.Single(decisions);
            Assert.Contains(route == "local" ? "Selected local client: Simple greeting." :
                "Selected cloud client: Complex task.", decision.Message);
            Assert.Equal(spans[^1].TraceId, decision.TraceId);
            Assert.Equal(spans[^1].Details.Split('\n')[0], decision.Details);
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
        var failure = new IOException("Cloud unavailable");
        var cloud = new FakeClient("actual-cloud-model")
        {
            FailureAfterFirstUpdate = failure,
            EmptyUpdate = emptyUpdate,
        };
        using var owner = CreateProvider(local, cloud, factory);
        var client = owner.GetRequiredService<IChatClient>();
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecisionLog_ExposesAuthorizedModelReasonButNotOtherPayloads(bool streaming)
    {
        using var receiver = new ChatDiagnostics();
        using var factory = CreateFactory(receiver);
        var local = new FakeClient("apple-model")
        {
            Decision = """{"route":"local","reason":"Greeting contains private-original-prompt"}""",
        };
        var cloud = new FakeClient("azure-model");
        using var owner = CreateProvider(local, cloud, factory);
        var client = owner.GetRequiredService<IChatClient>();
        await AnswerModel(client, streaming);

        var entries = receiver.Snapshot().Entries;
        var decision = Assert.Single(entries, entry => entry.Heading.Contains(ChatDiagnostics.HybridLogCategory));
        Assert.Contains("| Debug |", decision.Heading);
        Assert.Equal("Selected local client: Greeting contains private-original-prompt.", decision.Message);
        Assert.All(entries.Where(entry => entry != decision), entry =>
            Assert.DoesNotContain("private-original-prompt", entry.Message + entry.Details));
        var outer = entries.Last(entry => entry.Heading.Contains("| Telemetry |"));
        Assert.Equal(outer.TraceId, decision.TraceId);
        Assert.Equal(outer.Details.Split('\n')[0], decision.Details);
    }

    [Fact]
    public async Task DecisionWithoutOuterTelemetry_DoesNotInventCorrelation()
    {
        using var receiver = new ChatDiagnostics();
        using var factory = CreateFactory(receiver);
        var local = new FakeClient("apple-model") { Decision = LocalDecision };
        using var client = new HybridChatClient(local, new FakeClient("azure-model"), factory);
        await client.GetResponseAsync([new(ChatRole.User, "Hi")]);
        factory.CreateLogger(ChatDiagnostics.HybridLogCategory).LogTrace("private trace payload");
        factory.CreateLogger(ChatDiagnostics.HybridLogCategory + ".Other").LogError("unrelated category");

        var entry = Assert.Single(receiver.Snapshot().Entries);
        Assert.Equal("Selected local client: Simple greeting.", entry.Message);
        Assert.Null(entry.TraceId);
        Assert.Empty(entry.Details);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"route":"local","reason":42}""")]
    public async Task InvalidClassification_DoesNotLogSelectedRoute(string decision)
    {
        using var receiver = new ChatDiagnostics();
        using var factory = CreateFactory(receiver);
        var local = new FakeClient("apple-model") { Decision = decision };
        using var owner = CreateProvider(local, new FakeClient("azure-model"), factory);
        var client = owner.GetRequiredService<IChatClient>();
        var exception = await Record.ExceptionAsync(() => client.GetResponseAsync([new(ChatRole.User, "Hi")]));
        Assert.True(exception is InvalidOperationException or JsonException, exception?.ToString());
        Assert.DoesNotContain(receiver.Snapshot().Entries,
            entry => entry.Heading.Contains(ChatDiagnostics.HybridLogCategory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteClassification_LogsCloudDefaultWithOuterCorrelation(bool streaming)
    {
        using var receiver = new ChatDiagnostics();
        using var factory = CreateFactory(receiver);
        var local = new FakeClient("apple-model") { Decision = """{"route":"local","reason":""}""" };
        using var owner = CreateProvider(local, new FakeClient("azure-model"), factory);
        Assert.Equal("azure-model", await AnswerModel(owner.GetRequiredService<IChatClient>(), streaming));

        var entries = receiver.Snapshot().Entries;
        var decision = Assert.Single(entries, entry => entry.Heading.Contains(ChatDiagnostics.HybridLogCategory));
        Assert.Equal("Selected cloud client: Local routing returned an incomplete decision; defaulting to cloud.", decision.Message);
        var outer = entries.Last(entry => entry.Heading.Contains("| Telemetry |"));
        Assert.Equal(outer.TraceId, decision.TraceId);
        Assert.Equal(outer.Details.Split('\n')[0], decision.Details);
    }

    private static ILoggerFactory CreateFactory(ChatDiagnostics receiver) =>
        LoggerFactory.Create(builder => builder.AddProvider(receiver)
            .AddFilter<ChatDiagnostics>("Microsoft.Extensions.AI", LogLevel.Debug)
            .AddFilter<ChatDiagnostics>(ChatDiagnostics.HybridLogCategory, LogLevel.Debug));

    private static ServiceProvider CreateProvider(FakeClient local, FakeClient cloud, ILoggerFactory factory)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChatClient>("local", (_, _) =>
            local.AsBuilder().UsePlaygroundDiagnostics(factory).Build());
        services.AddKeyedSingleton<IChatClient>("cloud", (_, _) =>
            cloud.AsBuilder().UsePlaygroundDiagnostics(factory).Build());
        services.AddSingleton<IChatClient>(provider =>
        {
            var localLeaf = provider.GetRequiredKeyedService<IChatClient>("local");
            var cloudLeaf = provider.GetRequiredKeyedService<IChatClient>("cloud");
            Assert.False(localLeaf.GetService<OpenTelemetryChatClient>()!.EnableSensitiveData);
            Assert.False(cloudLeaf.GetService<OpenTelemetryChatClient>()!.EnableSensitiveData);
            var client = new HybridChatClient(localLeaf, cloudLeaf, factory)
                .AsBuilder().UsePlaygroundDiagnostics(factory).Build();
            Assert.False(client.GetService<OpenTelemetryChatClient>()!.EnableSensitiveData);
            return client;
        });
        return services.BuildServiceProvider();
    }

    private static async Task<string?> AnswerModel(IChatClient client, bool streaming)
    {
        ChatMessage[] messages = [new(ChatRole.User, "private-original-prompt")];
        var options = new ChatOptions
        {
            Instructions = "private-caller-instructions",
            Tools = [AIFunctionFactory.Create((string private_tool_argument) => "private-tool-result",
                "test_tool", "private-tool-description")],
            ToolMode = ChatToolMode.None,
            ResponseFormat = ChatResponseFormat.Json,
        };
        if (!streaming)
            return (await client.GetResponseAsync(messages, options)).ModelId;
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(messages, options))
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
        foreach (var payload in new[] { "private-original-prompt", "private-caller-instructions",
            "private-tool-description", "private_tool_argument", "private-tool-result", "\"route\"", "\"reason\"",
            "routing classifier" })
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
            entry.Heading.Contains("Microsoft.Extensions.AI.LoggingChatClient") ||
            entry.Heading.Contains(ChatDiagnostics.HybridLogCategory)));
    }
}
