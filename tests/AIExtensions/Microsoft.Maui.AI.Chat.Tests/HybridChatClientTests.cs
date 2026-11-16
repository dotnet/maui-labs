using System.Runtime.CompilerServices;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.AI.Chat.Tests;

[Collection("Chat diagnostics")]
public sealed class HybridChatClientTests
{
    [Theory]
    [InlineData("local", "local-model")]
    [InlineData(" local\n", "local-model")]
    [InlineData("cloud", "cloud-model")]
    [InlineData("Local.", "local-model")]
    [InlineData("local because it is a greeting", "cloud-model")]
    public async Task ClassifierLabel_SelectsProviderAndForwardsOriginalRequest(string label, string expectedModel)
    {
        foreach (var streaming in new[] { false, true })
        {
            var local = new FakeClient("local-model") { Label = label };
            var cloud = new FakeClient("cloud-model");
            using var hybrid = new HybridChatClient(local, cloud);
            ChatMessage[] messages =
            [
                new(ChatRole.User, "Earlier question"),
                new(ChatRole.Assistant, "Earlier answer"),
                new(ChatRole.User, "Hi"),
            ];
            var tools = new List<AITool> { AIFunctionFactory.Create(() => "tool", "test_tool") };
            var options = new ChatOptions { Instructions = "Be brief.", Tools = tools, ResponseFormat = ChatResponseFormat.Json };

            Assert.Equal(expectedModel, await AnswerModel(hybrid, streaming, messages, options));

            var classifier = Assert.Single(local.Classifications);
            Assert.Equal([ChatRole.System, ChatRole.User], classifier.Select(message => message.Role));
            Assert.Equal("Hi", classifier[1].Text);
            Assert.Null(local.ClassifierOptions!.Instructions);
            Assert.Null(local.ClassifierOptions.Tools);
            Assert.Null(local.ClassifierOptions.ResponseFormat);

            var selected = expectedModel == "local-model" ? local : cloud;
            var other = expectedModel == "local-model" ? cloud : local;
            Assert.Equal(1, selected.Answers);
            Assert.Equal(0, other.Answers);
            Assert.Equal(messages, selected.Received);
            Assert.Equal("Be brief.", selected.ReceivedOptions!.Instructions);
            Assert.Equal(tools, selected.ReceivedOptions.Tools);
            Assert.Same(options.ResponseFormat, selected.ReceivedOptions.ResponseFormat);
        }
    }

    [Fact]
    public async Task NoUserText_AnswersLocallyWithoutClassifying()
    {
        var local = new FakeClient("local-model");
        var cloud = new FakeClient("cloud-model");
        using var hybrid = new HybridChatClient(local, cloud);

        Assert.Equal("local-model", await AnswerModel(hybrid, false, [new(ChatRole.Assistant, "Hello")]));
        Assert.Equal("local-model", await AnswerModel(hybrid, true, [new(ChatRole.User, "  ")]));

        Assert.Empty(local.Classifications);
        Assert.Equal(2, local.Answers);
        Assert.Equal(0, cloud.Answers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloudFailureBeforeOutput_AnswersLocallyWithOriginalRequest(bool streaming)
    {
        var local = new FakeClient("local-model");
        var cloud = new FakeClient("cloud-model")
        {
            Failure = new HttpRequestException("Offline"),
            OnAnswer = options => options!.Instructions = "changed by cloud",
        };
        using var hybrid = new HybridChatClient(local, cloud);
        ChatMessage[] messages = [new(ChatRole.User, "Explain this.")];
        var options = new ChatOptions { Instructions = "Be brief." };

        Assert.Equal("local-model", await AnswerModel(hybrid, streaming, messages, options));

        Assert.Equal(1, cloud.Answers);
        Assert.Equal(1, local.Answers);
        Assert.Equal(messages, local.Received);
        Assert.Equal("Be brief.", local.ReceivedOptions!.Instructions);
        Assert.Equal("Be brief.", options.Instructions);
    }

    [Fact]
    public async Task CloudFailureAfterOutput_PropagatesWithoutLocalAnswer()
    {
        var failure = new IOException("Dropped");
        var local = new FakeClient("local-model");
        var cloud = new FakeClient("cloud-model") { FailureAfterOutput = failure };
        using var hybrid = new HybridChatClient(local, cloud);

        Assert.Same(failure, await Record.ExceptionAsync(() => AnswerModel(hybrid, streaming: true)));
        Assert.Equal(0, local.Answers);
    }

    [Fact]
    public async Task ClassifierFailure_PropagatesWithoutAnswering()
    {
        var failure = new IOException("Classifier unavailable");
        var local = new FakeClient("local-model") { ClassifierFailure = failure };
        var cloud = new FakeClient("cloud-model");
        using var hybrid = new HybridChatClient(local, cloud);

        Assert.Same(failure, await Record.ExceptionAsync(() => AnswerModel(hybrid, streaming: false)));
        Assert.Equal(0, local.Answers);
        Assert.Equal(0, cloud.Answers);
    }

    [Theory]
    [InlineData("local", 3)]
    [InlineData("cloud", 3)]
    [InlineData("fallback", 4)]
    public async Task Diagnostics_ShowEveryAttemptInOneTraceWithoutPayloads(string route, int expectedSpans)
    {
        const string variable = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "true");
        try
        {
            using var receiver = new ChatDiagnostics();
            using var factory = LoggerFactory.Create(builder => builder.AddProvider(receiver)
                .AddFilter<ChatDiagnostics>("Microsoft.Extensions.AI", LogLevel.Debug)
                .AddFilter<ChatDiagnostics>(ChatDiagnostics.SourceName, LogLevel.Debug));
            var local = new FakeClient("local-model") { Label = route == "local" ? "local" : "cloud" };
            var cloud = new FakeClient("cloud-model") { Failure = route == "fallback" ? new IOException("Offline") : null };
            using var client = new HybridChatClient(Instrument(local, factory), Instrument(cloud, factory),
                    factory.CreateLogger<HybridChatClient>())
                .AsBuilder().UsePlaygroundTelemetry().UseLogging(factory).Build();

            await client.GetResponseAsync([new(ChatRole.User, "private-prompt")],
                new ChatOptions { Instructions = "private-instructions" });

            var entries = receiver.Snapshot().Entries;
            var spans = entries.Where(entry => entry.Heading.Contains("| Telemetry |")).ToArray();
            Assert.Equal(expectedSpans, spans.Length);
            Assert.Single(entries.Select(entry => entry.TraceId).Distinct());
            var decision = Assert.Single(entries, entry => entry.Heading.Contains(typeof(HybridChatClient).FullName!));
            Assert.Equal(route == "local" ? "Selected local client." : "Selected cloud client.", decision.Message);
            Assert.Equal(route == "fallback", spans.Any(span => span.Details.Contains("IOException")));

            var text = string.Join("\n", entries.Select(entry => entry.Heading + entry.Message + entry.Details));
            Assert.DoesNotContain("private-prompt", text);
            Assert.DoesNotContain("private-instructions", text);
            Assert.DoesNotContain("routing classifier", text);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public async Task Recording_SavesOnlyTheAnswer()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hybrid-recording-{Guid.NewGuid():N}");
        try
        {
            var recording = new ChatSessionService(NullLogger<ChatSessionService>.Instance,
                Path.Combine(directory, "data"), Path.Combine(directory, "cache"));
            var local = new FakeClient("local-model");
            var cloud = new FakeClient("cloud-model") { Failure = new IOException("Offline") };
            using var client = new RecordingChatClient(new HybridChatClient(local, cloud), recording);

            await client.GetResponseAsync([new(ChatRole.User, "Question")]);

            var saved = ChatRecordingSerializer.Deserialize(File.ReadAllText(recording.AutosavePath));
            var interaction = Assert.Single(saved.Interactions);
            Assert.Equal("Question", Assert.Single(ChatRecordingSerializer.ReadRequestMessages(interaction.Request)).Text);
            Assert.Equal("local-model", ChatRecordingSerializer.ReadResponse(interaction.Response!).ModelId);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static IChatClient Instrument(IChatClient client, ILoggerFactory factory) =>
        client.AsBuilder().UsePlaygroundTelemetry().UseLogging(factory).Build();

    private static async Task<string?> AnswerModel(IChatClient client, bool streaming,
        ChatMessage[]? messages = null, ChatOptions? options = null)
    {
        messages ??= [new(ChatRole.User, "Question")];
        return streaming
            ? (await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync()).ModelId
            : (await client.GetResponseAsync(messages, options)).ModelId;
    }

    private sealed class FakeClient(string modelId) : IChatClient
    {
        public string Label { get; init; } = "cloud";
        public Exception? ClassifierFailure { get; init; }
        public Exception? Failure { get; init; }
        public Exception? FailureAfterOutput { get; init; }
        public Action<ChatOptions?>? OnAnswer { get; init; }
        public List<List<ChatMessage>> Classifications { get; } = [];
        public ChatOptions? ClassifierOptions { get; private set; }
        public int Answers { get; private set; }
        public List<ChatMessage>? Received { get; private set; }
        public ChatOptions? ReceivedOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var request = messages.ToList();
            if (request is [{ Role.Value: "system" } system, ..] && system.Text.Contains("routing classifier"))
            {
                Classifications.Add(request);
                ClassifierOptions = options;
                if (ClassifierFailure is not null)
                    throw ClassifierFailure;
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Label)) { ModelId = modelId });
            }

            StartAnswer(request, options);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")) { ModelId = modelId });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StartAnswer(messages.ToList(), options);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "answer") { ModelId = modelId };
            await Task.Yield();
            if (FailureAfterOutput is not null)
                throw FailureAfterOutput;
        }

        private void StartAnswer(List<ChatMessage> messages, ChatOptions? options)
        {
            Answers++;
            Received = messages;
            ReceivedOptions = options;
            OnAnswer?.Invoke(options);
            if (Failure is not null)
                throw Failure;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
