using System.Collections;
using System.ClientModel;
using System.Net;
using System.Text.Json;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class HybridChatClientTests
{
    [Fact]
    public void RoutingSchema_UsesScalarStringTypesSupportedByApple()
    {
        var format = Assert.IsType<ChatResponseFormatJson>(
            ChatResponseFormat.ForJsonSchema<HybridRoutingDecision>(HybridRoutingJsonContext.Default.Options));
        var fields = format.Schema!.Value.GetProperty("properties");
        foreach (var name in new[] { "route", "reason", "cloudSummary" })
            Assert.Equal("string", fields.GetProperty(name).GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Greeting_RoutesLocallyInBothModes(bool streaming)
    {
        var local = new FakeClient("apple-model") { Decision = LocalDecision };
        var cloud = new FakeClient("azure-model");
        using var hybrid = Create(local, cloud);

        Assert.Equal("apple-model", await AnswerModel(hybrid, streaming));
        Assert.Equal(1, local.ClassifierCalls);
        Assert.Equal(1, local.AnswerCalls + local.StreamCalls);
        Assert.Equal(0, cloud.AnswerCalls + cloud.StreamCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloudSummary_ForwardsOnlyPreparedSummaryAndSafeKnobsWithoutMutation(bool streaming)
    {
        const string secret = "jane@example.com at 123 Main Street";
        var local = new FakeClient("apple-model") { Decision = CloudDecision };
        var cloud = new FakeClient("azure-model");
        using var hybrid = Create(local, cloud);
        var original = new ChatMessage(ChatRole.User, $"Please help {secret}")
        {
            AdditionalProperties = new() { ["secret"] = secret },
            RawRepresentation = new object(),
            AuthorName = secret,
            MessageId = secret,
        };
        original.Contents[0].RawRepresentation = new object();
        var options = new ChatOptions
        {
            Instructions = $"Use {secret}",
            ModelId = secret,
            ConversationId = secret,
            AdditionalProperties = new() { ["secret"] = secret },
            RawRepresentationFactory = _ => new object(),
            Reasoning = new(),
            StopSequences = [secret],
            Temperature = 0.3f,
            MaxOutputTokens = 123,
            TopP = 0.8f,
            TopK = 40,
            FrequencyPenalty = 0.1f,
            PresencePenalty = 0.2f,
            Seed = 42,
            ResponseFormat = ChatResponseFormat.Json,
        };

        Assert.Equal("azure-model", await AnswerModel(hybrid, streaming, [original], options));
        var forwarded = Assert.Single(cloud.ReceivedMessages!);
        Assert.Equal(Summary, forwarded.Text);
        Assert.Equal(ChatRole.User, forwarded.Role);
        AssertCleanMessage(forwarded);
        AssertSafeOptions(cloud.ReceivedOptions!);
        Assert.Null(cloud.ReceivedOptions!.Instructions);
        Assert.Null(cloud.ReceivedOptions.StopSequences);
        Assert.Equal(options.Temperature, cloud.ReceivedOptions.Temperature);
        Assert.Equal(options.MaxOutputTokens, cloud.ReceivedOptions.MaxOutputTokens);
        Assert.Equal(options.TopP, cloud.ReceivedOptions.TopP);
        Assert.Equal(options.TopK, cloud.ReceivedOptions.TopK);
        Assert.Equal(options.FrequencyPenalty, cloud.ReceivedOptions.FrequencyPenalty);
        Assert.Equal(options.PresencePenalty, cloud.ReceivedOptions.PresencePenalty);
        Assert.Equal(options.Seed, cloud.ReceivedOptions.Seed);
        Assert.Same(options.ResponseFormat, cloud.ReceivedOptions.ResponseFormat);
        Assert.Contains(secret, local.ClassifierMessages![0].Text);
        Assert.Equal(1, local.ClassifierCalls);
        Assert.Equal([secret], options.StopSequences);
        Assert.Equal("Use " + secret, options.Instructions);
        Assert.NotNull(original.RawRepresentation);
        Assert.NotNull(original.Contents[0].RawRepresentation);
        Assert.Equal(secret, original.AdditionalProperties!["secret"]);
        Assert.Equal(secret, options.AdditionalProperties!["secret"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalPayload_PreservesCleanHistoryInstructionsAndStopSequences(bool streaming)
    {
        var local = new FakeClient("apple-model")
        {
            Decision = """{"route":"cloud","reason":"Complex task","cloudSummary":""}""",
        };
        var cloud = new FakeClient("azure-model");
        using var hybrid = Create(local, cloud);
        var history = new[]
        {
            new ChatMessage(ChatRole.System, "Explain clearly."),
            new ChatMessage(ChatRole.User, "First question"),
            new ChatMessage(ChatRole.Assistant, "First answer"),
            new ChatMessage(ChatRole.User, "Follow-up"),
        };
        var options = new ChatOptions
        {
            Instructions = "Write clearly.",
            StopSequences = ["the end"],
            AdditionalProperties = new() { [HybridChatClient.OriginalCloudPayloadOption] = true },
        };

        Assert.Equal("azure-model", await AnswerModel(hybrid, streaming, history, options));
        Assert.Equal(history.Select(message => message.Text), cloud.ReceivedMessages!.Select(message => message.Text));
        Assert.Equal(history.Select(message => message.Role), cloud.ReceivedMessages!.Select(message => message.Role));
        Assert.All(cloud.ReceivedMessages!, AssertCleanMessage);
        Assert.Equal("Write clearly.", cloud.ReceivedOptions!.Instructions);
        Assert.Equal(["the end"], cloud.ReceivedOptions.StopSequences);
        Assert.NotSame(options.StopSequences, cloud.ReceivedOptions.StopSequences);
        AssertSafeOptions(cloud.ReceivedOptions);
        Assert.Contains("Write clearly.", local.ClassifierMessages![0].Text);
        Assert.DoesNotContain("Write clearly.", local.ClassifierOptions!.Instructions);
        Assert.Contains("untrusted", local.ClassifierOptions.Instructions);
        Assert.NotNull(local.ClassifierOptions.ResponseFormat);
        Assert.Equal(1, local.ClassifierCalls);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"true\"", true)]
    public async Task OriginalPayloadOption_AcceptsRecordedJsonBooleans(string json, bool original)
    {
        var local = new FakeClient("apple-model") { Decision = CloudDecision };
        var cloud = new FakeClient("azure-model");
        using var hybrid = Create(local, cloud);
        var options = new ChatOptions
        {
            AdditionalProperties = new()
            {
                [HybridChatClient.OriginalCloudPayloadOption] = JsonDocument.Parse(json).RootElement.Clone(),
            },
        };
        Assert.Equal("azure-model", await AnswerModel(hybrid, false, options: options));
        Assert.Equal(original ? "Question" : Summary, Assert.Single(cloud.ReceivedMessages!).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoCloud_BypassesClassifier(bool streaming)
    {
        var local = new FakeClient("apple-model") { Decision = "invalid" };
        using var hybrid = Create(local, null);
        Assert.Equal("apple-model", await AnswerModel(hybrid, streaming));
        Assert.Equal(0, local.ClassifierCalls);
    }

    [Theory]
    [InlineData("""{"route":"cloud","reason":"X","cloudSummary":""}""")]
    [InlineData("""{"route":"Cloud","reason":"X","cloudSummary":"s"}""")]
    [InlineData("""{"route":"cloud","reason":"","cloudSummary":"s"}""")]
    [InlineData("""{"route":"cloud","reason":"X","cloudSummary":null}""")]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task InvalidDecision_FailsClosed(string decision)
    {
        foreach (var streaming in new[] { false, true })
        {
            var local = new FakeClient("apple-model") { Decision = decision };
            var cloud = new FakeClient("azure-model");
            using var hybrid = Create(local, cloud);
            await Assert.ThrowsAsync<InvalidOperationException>(() => AnswerModel(hybrid, streaming));
            Assert.Equal(0, cloud.AnswerCalls + cloud.StreamCalls);
            Assert.Equal(0, local.AnswerCalls + local.StreamCalls);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransientCloudFailure_FallsBackOnOriginalCleanTextAndOptions(bool streaming)
    {
        var local = new FakeClient("apple-model") { Decision = CloudDecision };
        var cloud = new FakeClient("azure-model") { Failure = new HttpRequestException("Offline") };
        using var hybrid = Create(local, cloud);
        var message = new ChatMessage(ChatRole.User,
            [new TextContent("Original task"), new TextReasoningContent("private thought")])
        {
            RawRepresentation = new object(),
        };
        var options = new ChatOptions { Instructions = "Apply locally", StopSequences = ["done"] };
        Assert.Equal("apple-model", await AnswerModel(hybrid, streaming, [message], options));
        Assert.Equal(1, local.ClassifierCalls);
        Assert.Equal(1, cloud.AnswerCalls + cloud.StreamCalls);
        Assert.Equal(1, local.AnswerCalls + local.StreamCalls);
        Assert.Equal(Summary, Assert.Single(cloud.ReceivedMessages!).Text);
        var fallback = Assert.Single(local.ReceivedMessages!);
        Assert.Equal("Original task", fallback.Text);
        Assert.Single(fallback.Contents);
        AssertCleanMessage(fallback);
        Assert.Equal("Apply locally", local.ReceivedOptions!.Instructions);
        Assert.Equal(["done"], local.ReceivedOptions.StopSequences);
        AssertSafeOptions(local.ReceivedOptions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SdkStatusZeroTransportFailure_FallsBackLocally(bool streaming)
    {
        var local = new FakeClient("apple-model") { Decision = CloudDecision };
        var cloud = new FakeClient("azure-model")
        {
            Failure = new ClientResultException("Transport failed", innerException: new HttpRequestException("Offline")),
        };
        using var hybrid = Create(local, cloud);
        Assert.Equal("apple-model", await AnswerModel(hybrid, streaming));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SdkRetryExhaustion_AllTransportFailures_FallsBackLocally(bool streaming)
    {
        var failure = new AggregateException("Retry failed after 4 tries.",
            Enumerable.Range(0, 4).Select(_ => new ClientResultException("Transport failed",
                innerException: new HttpRequestException("Offline"))));

        await AssertCloudFailure(failure, true, streaming);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedRetryFailures_AllTransient_FallsBackLocally(bool streaming)
    {
        var failure = new AggregateException(
            new IOException("Connection reset"),
            new AggregateException(new TimeoutException(), new StatusException(503)));

        await AssertCloudFailure(failure, true, streaming);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryFailures_WithNontransientFailure_PropagateWithoutFallback(bool streaming)
    {
        foreach (var failure in new Exception[] { new StatusException(401), new InvalidOperationException("Validation") })
            await AssertCloudFailure(
                new AggregateException(new HttpRequestException("Offline"), failure), false, streaming);
        await AssertCloudFailure(new AggregateException("No failures"), false, streaming);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(499, false)]
    [InlineData(500, true)]
    [InlineData(599, true)]
    [InlineData(600, false)]
    public async Task SdkHttpFailure_FallsBackOnlyForTransientStatus(int status, bool shouldFallback)
    {
        foreach (var streaming in new[] { false, true })
            await AssertCloudFailure(new StatusException(status), shouldFallback, streaming);
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(599, true)]
    [InlineData(600, false)]
    public async Task HttpFailure_FallsBackOnlyForTransientStatus(int status, bool shouldFallback)
    {
        foreach (var streaming in new[] { false, true })
            await AssertCloudFailure(new HttpRequestException("Failure", null, (HttpStatusCode)status), shouldFallback, streaming);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransportTimeoutsAndValidation_RespectFallbackBoundary(bool streaming)
    {
        foreach (var failure in new Exception[] { new IOException(), new TimeoutException(), new OperationCanceledException() })
            await AssertCloudFailure(failure, true, streaming);
        await AssertCloudFailure(new InvalidOperationException("Validation"), false, streaming);
        await AssertCloudFailure(new ClientResultException("No transport inner exception"), false, streaming);
        await AssertCloudFailure(new ClientResultException("Auth", innerException:
            new HttpRequestException("Auth", null, HttpStatusCode.Unauthorized)), false, streaming);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalFailure_DoesNotEscalateToCloud(bool streaming)
    {
        var failure = new IOException("Local failed");
        var local = new FakeClient("apple-model") { Decision = LocalDecision, Failure = failure };
        var cloud = new FakeClient("azure-model");
        using var hybrid = Create(local, cloud);
        Assert.Same(failure, await Record.ExceptionAsync(() => AnswerModel(hybrid, streaming)));
        Assert.Equal(0, cloud.AnswerCalls + cloud.StreamCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FallbackFailure_PropagatesWithoutThirdAttempt(bool streaming)
    {
        var failure = new IOException("Local failed");
        var local = new FakeClient("apple-model") { Decision = CloudDecision, Failure = failure };
        var cloud = new FakeClient("azure-model") { Failure = new IOException("Cloud failed") };
        using var hybrid = Create(local, cloud);
        Assert.Same(failure, await Record.ExceptionAsync(() => AnswerModel(hybrid, streaming)));
        Assert.Equal(1, cloud.AnswerCalls + cloud.StreamCalls);
        Assert.Equal(1, local.AnswerCalls + local.StreamCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClassifierFailure_DoesNotInvokeAnyAnswer(bool streaming)
    {
        var failure = new IOException("Classifier disconnected");
        var local = new FakeClient("apple-model")
        {
            Decision = CloudDecision,
            OnClassifierCall = () => throw failure,
        };
        var cloud = new FakeClient("azure-model");
        using var hybrid = Create(local, cloud);
        Assert.Same(failure, await Record.ExceptionAsync(() => AnswerModel(hybrid, streaming)));
        Assert.Equal(0, cloud.AnswerCalls + cloud.StreamCalls);
        Assert.Equal(0, local.AnswerCalls + local.StreamCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamingFailureAfterAnyUpdate_PropagatesWithoutFallback(bool emptyUpdate)
    {
        var failure = new IOException("Connection reset");
        var local = new FakeClient("apple-model") { Decision = CloudDecision };
        var cloud = new FakeClient("azure-model")
        {
            EmptyUpdate = emptyUpdate,
            FailureAfterFirstUpdate = failure,
        };
        using var hybrid = Create(local, cloud);
        var updates = new List<ChatResponseUpdate>();
        var observed = await Record.ExceptionAsync(async () =>
        {
            await foreach (var update in hybrid.GetStreamingResponseAsync([new(ChatRole.User, "Question")]))
                updates.Add(update);
        });
        Assert.Same(failure, observed);
        Assert.Equal("azure-model", Assert.Single(updates).ModelId);
        Assert.Equal(0, local.StreamCalls);
    }

    [Fact]
    public async Task StreamingFactoryFailureBeforeEnumeration_FallsBackLocally()
    {
        var local = new FakeClient("apple-model") { Decision = CloudDecision };
        var cloud = new FakeClient("azure-model") { EagerStreamFailure = new IOException("Offline") };
        using var hybrid = Create(local, cloud);
        Assert.Equal("apple-model", await AnswerModel(hybrid, true));
        Assert.Equal(1, local.StreamCalls);
        Assert.Equal(1, local.ClassifierCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCanceledRequest_NeverClassifiesOrInvokesAnAnswer(bool streaming)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var local = new FakeClient("apple-model") { Decision = CloudDecision };
        var cloud = new FakeClient("azure-model");
        using var hybrid = Create(local, cloud);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AnswerModel(hybrid, streaming, cancellationToken: cancellation.Token));
        Assert.Equal(0, local.ClassifierCalls + local.AnswerCalls + local.StreamCalls);
        Assert.Equal(0, cloud.AnswerCalls + cloud.StreamCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterClassification_NeverCallsCloudOrFallback(bool streaming)
    {
        using var cancellation = new CancellationTokenSource();
        var local = new FakeClient("apple-model") { Decision = CloudDecision, OnClassifierCall = cancellation.Cancel };
        var cloud = new FakeClient("azure-model");
        using var hybrid = Create(local, cloud);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AnswerModel(hybrid, streaming, cancellationToken: cancellation.Token));
        Assert.Equal(0, cloud.AnswerCalls + cloud.StreamCalls);
        Assert.Equal(0, local.AnswerCalls + local.StreamCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationDuringCloud_NeverFallsBack(bool streaming)
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new OperationCanceledException(cancellation.Token);
        var local = new FakeClient("apple-model") { Decision = CloudDecision };
        var cloud = new FakeClient("azure-model") { OnAnswerCall = cancellation.Cancel, Failure = failure };
        using var hybrid = Create(local, cloud);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AnswerModel(hybrid, streaming, cancellationToken: cancellation.Token));
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(0, local.AnswerCalls + local.StreamCalls);
    }

    [Fact]
    public async Task CallerInput_EnumeratedOnceAndUnsupportedContentRejectedBeforeClassification()
    {
        var local = new FakeClient("apple-model") { Decision = LocalDecision };
        var cloud = new FakeClient("azure-model");
        using var hybrid = Create(local, cloud);
        var user = new ChatMessage(ChatRole.User, [new TextContent("question"), new TextReasoningContent("private thought")]);
        var messages = new SingleEnumeration([user]);
        await hybrid.GetResponseAsync(messages);
        Assert.Equal(1, messages.Enumerations);
        Assert.Single(local.ReceivedMessages![0].Contents);
        Assert.Equal(2, user.Contents.Count);
        await Assert.ThrowsAsync<NotSupportedException>(() => hybrid.GetResponseAsync([new(ChatRole.Tool, "tool")]));
        await Assert.ThrowsAsync<NotSupportedException>(() => hybrid.GetResponseAsync(
            [new(ChatRole.User, [new DataContent(new byte[] { 1, 2 }, "image/png")])]));
        await Assert.ThrowsAsync<NotSupportedException>(() => hybrid.GetResponseAsync(
            [new(ChatRole.User, "hello")], new ChatOptions { ToolMode = ChatToolMode.RequireAny }));
        await Assert.ThrowsAsync<NotSupportedException>(() => hybrid.GetResponseAsync(
            [new(ChatRole.User, "hello")], new ChatOptions { Tools = [AIFunctionFactory.Create(() => "tool")] }));
        await Assert.ThrowsAsync<ArgumentException>(() => hybrid.GetResponseAsync(
            [new(ChatRole.User, "hello")], new ChatOptions
            {
                AdditionalProperties = new() { [HybridChatClient.OriginalCloudPayloadOption] = "not boolean" },
            }));
        Assert.Equal(1, local.ClassifierCalls);
    }

    [Fact]
    public async Task ConcurrentRequests_KeepRoutingAndFallbackPayloadsIsolated()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var local = new ConcurrentClient(async (messages, options) =>
        {
            if (options?.ResponseFormat is ChatResponseFormatJson)
            {
                var original = JsonDocument.Parse(messages[0].Text).RootElement
                    .GetProperty("conversation")[0].GetProperty("text").GetString();
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(new
                {
                    route = "cloud", reason = "Complex", cloudSummary = $"Summary {original}",
                })));
            }
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, messages[0].Text)) { ModelId = "local-model" };
        });
        var cloud = new ConcurrentClient(async (messages, options) =>
        {
            if (messages[0].Text == "Summary first")
            {
                await gate.Task;
                throw new IOException("First request offline");
            }
            gate.TrySetResult();
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, messages[0].Text)) { ModelId = "cloud-model" };
        });
        using var hybrid = new HybridChatClient(local, cloud, NullLogger<HybridChatClient>.Instance);
        var first = hybrid.GetResponseAsync([new(ChatRole.User, "first")]);
        var second = hybrid.GetResponseAsync([new(ChatRole.User, "second")]);
        var results = await Task.WhenAll(first, second);
        Assert.Equal("first", results[0].Text);
        Assert.Equal("local-model", results[0].ModelId);
        Assert.Equal("Summary second", results[1].Text);
        Assert.Equal("cloud-model", results[1].ModelId);
    }

    [Fact]
    public async Task GetService_HidesLeavesAndDisposeOwnsBothExactlyOnce()
    {
        var local = new FakeClient("apple-model") { Decision = CloudDecision };
        var cloud = new FakeClient("azure-model");
        var hybrid = Create(local, cloud);
        Assert.Same(hybrid, hybrid.GetService(typeof(IChatClient)));
        Assert.Same(hybrid, hybrid.GetService(typeof(HybridChatClient)));
        Assert.Null(hybrid.GetService(typeof(FakeClient)));
        Assert.Null(hybrid.GetService(typeof(IChatClient), "provider"));
        await AnswerModel(hybrid, false);
        await AnswerModel(hybrid, true);
        Assert.Equal(0, local.DisposeCalls + cloud.DisposeCalls);
        hybrid.Dispose();
        hybrid.Dispose();
        Assert.Equal(1, local.DisposeCalls);
        Assert.Equal(1, cloud.DisposeCalls);
        var shared = new FakeClient("shared-model") { Decision = CloudDecision };
        var sharedHybrid = Create(shared, shared);
        await AnswerModel(sharedHybrid, false);
        sharedHybrid.Dispose();
        sharedHybrid.Dispose();
        Assert.Equal(1, shared.DisposeCalls);
    }

    private const string Summary = "Explain the fictional task.";
    private const string CloudDecision = """{"route":"cloud","reason":"Complex task","cloudSummary":"Explain the fictional task."}""";
    private const string LocalDecision = """{"route":"local","reason":"Simple greeting","cloudSummary":""}""";

    private static HybridChatClient Create(FakeClient local, FakeClient? cloud) =>
        new(local, cloud, NullLogger<HybridChatClient>.Instance);

    private static async Task<string?> AnswerModel(IChatClient client, bool streaming,
        IEnumerable<ChatMessage>? messages = null, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        messages ??= [new(ChatRole.User, "Question")];
        if (!streaming)
            return (await client.GetResponseAsync(messages, options, cancellationToken)).ModelId;
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(messages, options, cancellationToken))
            updates.Add(update);
        return Assert.Single(updates).ModelId;
    }

    private static async Task AssertCloudFailure(Exception failure, bool shouldFallback, bool streaming)
    {
        var local = new FakeClient("apple-model") { Decision = CloudDecision };
        var cloud = new FakeClient("azure-model") { Failure = failure };
        using var hybrid = Create(local, cloud);
        if (shouldFallback)
            Assert.Equal("apple-model", await AnswerModel(hybrid, streaming));
        else
            Assert.Same(failure, await Record.ExceptionAsync(() => AnswerModel(hybrid, streaming)));
        Assert.Equal(shouldFallback ? 1 : 0, local.AnswerCalls + local.StreamCalls);
        Assert.Equal(1, cloud.AnswerCalls + cloud.StreamCalls);
    }

    private static void AssertCleanMessage(ChatMessage message)
    {
        Assert.Null(message.AdditionalProperties);
        Assert.Null(message.RawRepresentation);
        Assert.Null(message.AuthorName);
        Assert.Null(message.MessageId);
        Assert.All(message.Contents, content => Assert.Null(content.RawRepresentation));
    }

    private static void AssertSafeOptions(ChatOptions options)
    {
        Assert.Null(options.ModelId);
        Assert.Null(options.ConversationId);
        Assert.Null(options.AdditionalProperties);
        Assert.Null(options.RawRepresentationFactory);
        Assert.Null(options.Reasoning);
        Assert.Null(options.Tools);
        Assert.Null(options.AllowMultipleToolCalls);
        Assert.Equal(ChatToolMode.None, options.ToolMode);
    }

    private sealed class FakeClient(string modelId) : IChatClient
    {
        public string? Decision { get; set; }
        public Action? OnClassifierCall { get; set; }
        public Action? OnAnswerCall { get; set; }
        public Exception? Failure { get; set; }
        public Exception? EagerStreamFailure { get; set; }
        public Exception? FailureAfterFirstUpdate { get; set; }
        public bool EmptyUpdate { get; set; }
        public int ClassifierCalls { get; private set; }
        public int AnswerCalls { get; private set; }
        public int StreamCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public List<ChatMessage>? ClassifierMessages { get; private set; }
        public ChatOptions? ClassifierOptions { get; private set; }
        public List<ChatMessage>? ReceivedMessages { get; private set; }
        public ChatOptions? ReceivedOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = messages.ToList();
            if (options?.ResponseFormat is ChatResponseFormatJson && Decision is not null &&
                options.Instructions?.Contains("routing classifier", StringComparison.Ordinal) == true)
            {
                ClassifierCalls++;
                ClassifierMessages = snapshot;
                ClassifierOptions = options;
                OnClassifierCall?.Invoke();
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Decision)) { ModelId = modelId });
            }
            AnswerCalls++;
            ReceivedMessages = snapshot;
            ReceivedOptions = options;
            OnAnswerCall?.Invoke();
            if (Failure is { } failure)
                throw failure;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")) { ModelId = modelId });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (EagerStreamFailure is { } failure)
                throw failure;
            return StreamAsync(messages, options, cancellationToken);
        }

        private async IAsyncEnumerable<ChatResponseUpdate> StreamAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StreamCalls++;
            ReceivedMessages = messages.ToList();
            ReceivedOptions = options;
            OnAnswerCall?.Invoke();
            if (Failure is { } failure)
                throw failure;
            yield return new ChatResponseUpdate(ChatRole.Assistant, EmptyUpdate ? "" : "answer") { ModelId = modelId };
            await Task.Yield();
            if (FailureAfterFirstUpdate is { } subsequent)
                throw subsequent;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => DisposeCalls++;
    }

    private sealed class ConcurrentClient(
        Func<List<ChatMessage>, ChatOptions?, Task<ChatResponse>> respond) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => respond(messages.ToList(), options);
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class SingleEnumeration(IEnumerable<ChatMessage> messages) : IEnumerable<ChatMessage>
    {
        public int Enumerations { get; private set; }
        public IEnumerator<ChatMessage> GetEnumerator()
        {
            if (++Enumerations != 1)
                throw new InvalidOperationException("Enumerated more than once.");
            return messages.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class StatusException : ClientResultException
    {
        public StatusException(int status) : base("SDK request failed") => Status = status;
    }
}
