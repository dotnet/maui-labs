using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class HybridChatClientTests
{
    [Theory]
    [InlineData(false, "local")]
    [InlineData(true, "local")]
    [InlineData(false, " \tlocal\r\n")]
    [InlineData(true, " \tlocal\r\n")]
    public async Task LocalLabel_WithOptionalWhitespace_SelectsLocalProvider(bool streaming, string decision)
    {
        var local = new FakeClient("apple-model") { Decision = decision };
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
    public async Task CloudDecision_ForwardsOriginalContextAndCallerOptions(bool streaming)
    {
        var local = new FakeClient("apple-model") { Decision = CloudDecision };
        var cloud = new FakeClient("azure-model");
        using var hybrid = Create(local, cloud);
        var original = new ChatMessage(ChatRole.User, "Compare scheduler crash recovery.");
        ChatMessage[] history =
        [
            new(ChatRole.System, "Explain clearly."),
            new(ChatRole.User, "First question"),
            new(ChatRole.Assistant, "First answer"),
            original,
        ];
        var options = new ChatOptions
        {
            Instructions = "Explain the tradeoffs.",
            StopSequences = ["done"],
            ResponseFormat = ChatResponseFormat.Json,
            Tools = [AIFunctionFactory.Create(() => "tool", "test_tool")],
        };

        Assert.Equal("azure-model", await AnswerModel(hybrid, streaming, history, options));
        Assert.Equal(history, cloud.ReceivedMessages);
        Assert.Same(original, cloud.ReceivedMessages![^1]);
        Assert.Equal(options.Tools, cloud.ReceivedOptions!.Tools);
        Assert.Equal(options.Instructions, cloud.ReceivedOptions.Instructions);
        Assert.Equal(options.StopSequences, cloud.ReceivedOptions.StopSequences);
        Assert.Same(options.ResponseFormat, cloud.ReceivedOptions.ResponseFormat);
        AssertClassifierRequest(local, original.Text);
        Assert.Equal(1, local.ClassifierCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Routing_EachRequestClassifiesOnlyLastUserMessageAndCanChangeLeaf(bool streaming)
    {
        var local = new FakeClient("apple-model") { Decision = LocalDecision };
        var cloud = new FakeClient("azure-model");
        using var hybrid = Create(local, cloud);
        ChatMessage[] messages =
        [
            new(ChatRole.System, "Earlier system instruction"),
            new(ChatRole.User, "Earlier complex task"),
            new(ChatRole.Assistant, "Earlier answer"),
            new(ChatRole.User, "Hi"),
            new(ChatRole.Assistant, "Trailing assistant message"),
        ];
        var options = new ChatOptions { Instructions = "Answer instructions" };

        Assert.Equal("apple-model", await AnswerModel(hybrid, streaming, messages, options));
        AssertClassifierRequest(local, "Hi");

        local.Decision = CloudDecision;
        messages = [.. messages, new(ChatRole.User, "Compare distributed job scheduler architectures")];
        Assert.Equal("azure-model", await AnswerModel(hybrid, streaming, messages, options));
        AssertClassifierRequest(local, messages[^1].Text);
        Assert.Equal(messages.Select(message => message.Text),
            cloud.ReceivedMessages!.Select(message => message.Text));
        Assert.Equal(options.Instructions, cloud.ReceivedOptions!.Instructions);

        local.Decision = LocalDecision;
        messages = [.. messages, new(ChatRole.User, "Hi again")];
        Assert.Equal("apple-model", await AnswerModel(hybrid, streaming, messages, options));
        AssertClassifierRequest(local, "Hi again");
        Assert.Equal(3, local.ClassifierCalls);
        Assert.Equal(2, local.AnswerCalls + local.StreamCalls);
        Assert.Equal(1, cloud.AnswerCalls + cloud.StreamCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrBlankLastUserMessage_AnswersLocallyWithoutClassification(bool streaming)
    {
        var local = new FakeClient("apple-model") { Decision = "invalid" };
        var cloud = new FakeClient("azure-model");
        using var hybrid = Create(local, cloud);
        foreach (var messages in new ChatMessage[][]
        {
            [new(ChatRole.System, "System instruction"), new(ChatRole.Assistant, "Previous answer")],
            [new(ChatRole.User, "")],
            [new(ChatRole.User, "Earlier question"), new(ChatRole.User, "   ")],
        })
            Assert.Equal("apple-model", await AnswerModel(hybrid, streaming, messages));

        Assert.Equal(0, local.ClassifierCalls);
        Assert.Equal(3, local.AnswerCalls + local.StreamCalls);
        Assert.Equal(0, cloud.AnswerCalls + cloud.StreamCalls);
    }

    [Theory]
    [InlineData("cloud")]
    [InlineData("")]
    [InlineData("Local")]
    [InlineData("local because this is a greeting")]
    public async Task NonLocalLabel_DefaultsToCloudInBothModes(string decision)
    {
        foreach (var streaming in new[] { false, true })
        {
            var local = new FakeClient("apple-model") { Decision = decision };
            var cloud = new FakeClient("azure-model");
            using var hybrid = Create(local, cloud);
            Assert.Equal("azure-model", await AnswerModel(hybrid, streaming));
            Assert.Equal(1, local.ClassifierCalls);
            Assert.Equal(1, cloud.AnswerCalls + cloud.StreamCalls);
            Assert.Equal(0, local.AnswerCalls + local.StreamCalls);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloudFailure_FallsBackOnOriginalFullMessagesAndOptions(bool streaming)
    {
        var local = new FakeClient("apple-model") { Decision = CloudDecision };
        var cloud = new FakeClient("azure-model") { Failure = new HttpRequestException("Offline") };
        using var hybrid = Create(local, cloud);
        var message = new ChatMessage(ChatRole.User,
            [new TextContent("Original task"), new TextReasoningContent("private thought")])
        {
            RawRepresentation = new object(),
        };
        ChatMessage[] history =
        [
            new(ChatRole.System, "Explain clearly."),
            new(ChatRole.User, "Earlier question"),
            new(ChatRole.Assistant, "Earlier answer"),
            message,
        ];
        var options = new ChatOptions { Instructions = "Apply locally", StopSequences = ["done"] };
        Assert.Equal("apple-model", await AnswerModel(hybrid, streaming, history, options));
        Assert.Equal(1, local.ClassifierCalls);
        Assert.Equal(1, cloud.AnswerCalls + cloud.StreamCalls);
        Assert.Equal(1, local.AnswerCalls + local.StreamCalls);
        Assert.Equal(history, cloud.ReceivedMessages);
        Assert.Same(message, cloud.ReceivedMessages![^1]);
        Assert.Equal(options.Instructions, cloud.ReceivedOptions!.Instructions);
        Assert.Equal(options.StopSequences, cloud.ReceivedOptions.StopSequences);
        Assert.Equal(history, local.ReceivedMessages);
        Assert.Same(message, local.ReceivedMessages![^1]);
        Assert.Equal("Apply locally", local.ReceivedOptions!.Instructions);
        Assert.Equal(["done"], local.ReceivedOptions.StopSequences);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloudMutationBeforeFailure_PreservesLocalRecoveryAndCallerOptions(bool streaming)
    {
        var local = new FakeClient("apple-model") { Decision = CloudDecision };
        var cloud = new FakeClient("azure-model") { Failure = new IOException("Cloud failed before output") };
        cloud.OnAnswerCall = () =>
        {
            var received = cloud.ReceivedOptions!;
            received.Instructions = "Cloud mutation";
            received.StopSequences!.Clear();
        };
        using var hybrid = Create(local, cloud);
        var message = new ChatMessage(ChatRole.User, "Original task");
        var options = new ChatOptions
        {
            Instructions = "Original instructions",
            StopSequences = ["done"],
        };

        Assert.Equal("apple-model", await AnswerModel(hybrid, streaming, [message], options));
        var recovered = Assert.IsType<ChatOptions>(local.ReceivedOptions);
        Assert.Equal("Original instructions", recovered.Instructions);
        Assert.Equal(["done"], recovered.StopSequences);
        Assert.Equal("Original instructions", options.Instructions);
        Assert.Equal(["done"], options.StopSequences);
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

    [Fact]
    public async Task CallerInput_ForwardsToolHistoryImagesAndReasoningTransparently()
    {
        var local = new FakeClient("apple-model") { Decision = LocalDecision };
        var cloud = new FakeClient("azure-model");
        using var hybrid = Create(local, cloud);
        var user = new ChatMessage(ChatRole.User,
            [new TextContent("question"), new TextReasoningContent("private thought"),
                new DataContent(new byte[] { 1, 2 }, "image/png")]);
        ChatMessage[] messages =
        [
            new(ChatRole.Assistant, [new FunctionCallContent("prior-call", "test_tool")]),
            new(ChatRole.Tool, [new FunctionResultContent("prior-call", "prior result")]),
            user,
        ];
        await hybrid.GetResponseAsync(messages);
        Assert.Equal(messages, local.ReceivedMessages);
        Assert.Equal(3, user.Contents.Count);
        AssertClassifierRequest(local, "question");
        Assert.Equal(1, local.ClassifierCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ProviderFunctionInvocation_ExecutesToolOnceOnSelectedProvider(bool streaming, bool cloudRoute)
    {
        var local = new FakeClient("apple-model") { Decision = cloudRoute ? CloudDecision : LocalDecision };
        var cloud = new FakeClient("azure-model");
        var selected = cloudRoute ? cloud : local;
        selected.ToolName = "test_tool";
        using var localPipeline = local.AsBuilder().UseFunctionInvocation(NullLoggerFactory.Instance).Build();
        using var cloudPipeline = cloud.AsBuilder().UseFunctionInvocation(NullLoggerFactory.Instance).Build();
        using var hybrid = new HybridChatClient(localPipeline, cloudPipeline, NullLoggerFactory.Instance);
        var invocations = 0;
        var options = new ChatOptions
        {
            Instructions = "private caller instruction",
            Tools = [AIFunctionFactory.Create(() => ++invocations, "test_tool")],
        };
        ChatMessage[] messages = [new(ChatRole.User, "Use a tool")];
        var response = streaming
            ? await hybrid.GetStreamingResponseAsync(messages, options).ToChatResponseAsync()
            : await hybrid.GetResponseAsync(messages, options);

        Assert.Equal(1, invocations);
        Assert.Equal(cloudRoute ? "azure-model" : "apple-model", response.ModelId);
        Assert.Equal(2, selected.AnswerCalls + selected.StreamCalls);
        Assert.Equal(0, (cloudRoute ? local : cloud).AnswerCalls + (cloudRoute ? local : cloud).StreamCalls);
        var result = Assert.Single(selected.ReceivedMessages!.SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>());
        Assert.Equal("tool-call", result.CallId);
        Assert.Contains(response.Messages.SelectMany(message => message.Contents), content =>
            content is FunctionCallContent call && call.Name == "test_tool");
        AssertClassifierRequest(local, "Use a tool");
        Assert.Equal(1, local.ClassifierCalls);
        Assert.Equal(options.Tools, selected.ReceivedOptions!.Tools);
        Assert.Equal(options.Instructions, selected.ReceivedOptions.Instructions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloudFailureAfterActualToolExecution_RecoversOnlyBeforeOutput(bool streaming)
    {
        var failure = new IOException("Cloud failed after executing tool");
        var local = new FakeClient("apple-model") { Decision = CloudDecision, ToolName = "test_tool" };
        var cloud = new FakeClient("azure-model") { ToolName = "test_tool", FailureAfterToolResult = failure };
        using var localPipeline = local.AsBuilder().UseFunctionInvocation(NullLoggerFactory.Instance).Build();
        using var cloudPipeline = cloud.AsBuilder().UseFunctionInvocation(NullLoggerFactory.Instance).Build();
        using var hybrid = new HybridChatClient(localPipeline, cloudPipeline, NullLoggerFactory.Instance);
        var invocations = 0;
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(() => ++invocations, "test_tool")] };

        ChatMessage[] messages = [new(ChatRole.User, "Use a tool")];
        if (streaming)
        {
            Assert.Same(failure, await Record.ExceptionAsync(() =>
                hybrid.GetStreamingResponseAsync(messages, options).ToChatResponseAsync()));
            Assert.Equal(1, invocations);
            Assert.Equal(0, local.AnswerCalls + local.StreamCalls);
        }
        else
        {
            Assert.Equal("apple-model", (await hybrid.GetResponseAsync(messages, options)).ModelId);
            Assert.Equal(2, invocations);
            Assert.Equal(2, local.AnswerCalls + local.StreamCalls);
        }
        Assert.Equal(2, cloud.AnswerCalls + cloud.StreamCalls);
        AssertClassifierRequest(local, "Use a tool");
    }

    [Fact]
    public async Task ConcurrentRequests_KeepRoutingAndFallbackPayloadsIsolated()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var local = new ConcurrentClient((messages, options) =>
        {
            if (messages.FirstOrDefault() is { Role: var role } system && role == ChatRole.System &&
                system.Text.Contains("routing classifier", StringComparison.Ordinal))
            {
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, CloudDecision)));
            }
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, messages[0].Text))
                { ModelId = "local-model" });
        });
        var cloud = new ConcurrentClient(async (messages, options) =>
        {
            if (messages[0].Text == "first")
            {
                await gate.Task;
                throw new IOException("First request offline");
            }
            gate.TrySetResult();
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, messages[0].Text)) { ModelId = "cloud-model" };
        });
        using var hybrid = new HybridChatClient(local, cloud, NullLoggerFactory.Instance);
        var first = hybrid.GetResponseAsync([new(ChatRole.User, "first")]);
        var second = hybrid.GetResponseAsync([new(ChatRole.User, "second")]);
        var results = await Task.WhenAll(first, second);
        Assert.Equal("first", results[0].Text);
        Assert.Equal("local-model", results[0].ModelId);
        Assert.Equal("second", results[1].Text);
        Assert.Equal("cloud-model", results[1].ModelId);
    }

    internal const string CloudDecision = "cloud";
    internal const string LocalDecision = "local";

    private static HybridChatClient Create(FakeClient local, FakeClient cloud) =>
        new(local, cloud, NullLoggerFactory.Instance);

    private static async Task<string?> AnswerModel(IChatClient client, bool streaming,
        IEnumerable<ChatMessage>? messages = null, ChatOptions? options = null)
    {
        messages ??= [new(ChatRole.User, "Question")];
        if (!streaming)
            return (await client.GetResponseAsync(messages, options)).ModelId;
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(messages, options))
            updates.Add(update);
        return Assert.Single(updates).ModelId;
    }

    private static void AssertClassifierRequest(FakeClient local, string query)
    {
        var messages = Assert.IsType<List<ChatMessage>>(local.ClassifierMessages);
        Assert.Equal(2, messages.Count);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Contains("routing classifier", messages[0].Text);
        var user = messages[^1];
        Assert.Equal(ChatRole.User, user.Role);
        Assert.Equal(query, user.Text);
        var options = Assert.IsType<ChatOptions>(local.ClassifierOptions);
        Assert.Null(options.Instructions);
        Assert.Null(options.Tools);
        Assert.Null(options.StopSequences);
        Assert.Null(options.ResponseFormat);
    }

    internal sealed class FakeClient(string modelId) : IChatClient
    {
        public string? Decision { get; set; }
        public Action? OnClassifierCall { get; set; }
        public Action? OnAnswerCall { get; set; }
        public Exception? Failure { get; set; }
        public Exception? FailureAfterFirstUpdate { get; set; }
        public string? ToolName { get; set; }
        public Exception? FailureAfterToolResult { get; set; }
        public bool EmptyUpdate { get; set; }
        public UsageDetails? Usage { get; set; }
        public int ClassifierCalls { get; private set; }
        public int AnswerCalls { get; private set; }
        public int StreamCalls { get; private set; }
        public List<ChatMessage>? ClassifierMessages { get; private set; }
        public ChatOptions? ClassifierOptions { get; private set; }
        public List<ChatMessage>? ReceivedMessages { get; private set; }
        public ChatOptions? ReceivedOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = messages.ToList();
            if (Decision is not null &&
                snapshot.FirstOrDefault() is { Role: var role } system && role == ChatRole.System &&
                system.Text.Contains("routing classifier", StringComparison.Ordinal))
            {
                ClassifierCalls++;
                ClassifierMessages = snapshot;
                ClassifierOptions = options;
                OnClassifierCall?.Invoke();
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Decision))
                {
                    ModelId = modelId,
                    Usage = Usage,
                });
            }
            AnswerCalls++;
            ReceivedMessages = snapshot;
            ReceivedOptions = options;
            OnAnswerCall?.Invoke();
            if (Failure is { } failure)
                throw failure;
            if (ToolName is { } toolName)
            {
                if (!snapshot.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Any())
                    return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("tool-call", toolName)]))
                    {
                        ModelId = modelId,
                        FinishReason = ChatFinishReason.ToolCalls,
                    });
                if (FailureAfterToolResult is { } toolFailure)
                    throw toolFailure;
            }
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer"))
            {
                ModelId = modelId,
                Usage = Usage,
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
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
            if (ToolName is { } toolName)
            {
                if (!ReceivedMessages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Any())
                {
                    yield return new ChatResponseUpdate(ChatRole.Assistant,
                        [new FunctionCallContent("tool-call", toolName)])
                    {
                        ModelId = modelId,
                        FinishReason = ChatFinishReason.ToolCalls,
                    };
                    yield break;
                }
                if (FailureAfterToolResult is { } toolFailure)
                    throw toolFailure;
            }
            var update = new ChatResponseUpdate(ChatRole.Assistant, EmptyUpdate ? "" : "answer") { ModelId = modelId };
            if (Usage is not null)
                update.Contents.Add(new UsageContent(Usage));
            yield return update;
            await Task.Yield();
            if (FailureAfterFirstUpdate is { } subsequent)
                throw subsequent;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
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

}
