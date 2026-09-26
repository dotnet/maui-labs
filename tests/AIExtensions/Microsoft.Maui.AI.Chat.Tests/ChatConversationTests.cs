using System.Runtime.CompilerServices;
using System.Text.Json;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests;

#pragma warning disable MEAI001 // Image-generation tool content is experimental in the installed SDK.
public sealed class ChatConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteTurn_ToolCallAndReasoning_EmitOrderedTranscriptChanges(bool streaming)
    {
        var call = new FunctionCallContent("call-1", "calculate",
            new Dictionary<string, object?> { ["expression"] = "2+2" });
        var result = new FunctionResultContent("call-1", 4);
        var client = new ScriptedClient(
            new ChatResponse([
                new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("Calculating"), call]),
                new ChatMessage(ChatRole.Tool, [result]),
                new ChatMessage(ChatRole.Assistant, "Answer: 4"),
            ]),
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("Calculating"), call]),
                new ChatResponseUpdate(ChatRole.Assistant, [call]),
                new ChatResponseUpdate(ChatRole.Tool, [result]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Answer: ")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("4")]),
            ]);
        var processor = new ChatConversation();
        var events = new List<TranscriptChange>();
        await foreach (var change in processor.SendTurnAsync(
            client, new ChatMessage(ChatRole.User, "What is 2+2?"), "What is 2+2?",
            new ChatOptions { Instructions = "Answer briefly" }, streaming, structuredJson: false))
            events.Add(change);

        Assert.Collection(events.Take(2),
            change => Assert.Equal(TranscriptEntryKind.System, Assert.IsType<TranscriptChange.EntryAdded>(change).EntryKind),
            change => Assert.Equal(TranscriptEntryKind.User, Assert.IsType<TranscriptChange.EntryAdded>(change).EntryKind));
        Assert.Equal("What is 2+2?", Assert.Single(client.ReceivedMessages!).Text);
        Assert.Single(events.OfType<TranscriptChange.EntryAdded>(), change =>
            change is { EntryKind: TranscriptEntryKind.Tool, Label: "Tool call" });
        var finished = Assert.Single(events.OfType<TranscriptChange.ToolCallResolved>());
        Assert.Contains("Result:\n4", finished.Details);
        Assert.True(events.IndexOf(finished) <
            events.IndexOf(events.OfType<TranscriptChange.EntryAdded>().Last(change =>
                change.EntryKind == TranscriptEntryKind.Assistant)));
        Assert.Contains(events.OfType<TranscriptChange.EntryAdded>(), change =>
            change is { EntryKind: TranscriptEntryKind.Reasoning, Text: "Calculating" });
        Assert.Contains(events, change => change is
            TranscriptChange.EntryAdded { EntryKind: TranscriptEntryKind.Assistant, Text: "Answer: 4" }
            or TranscriptChange.EntryTextChanged { Text: "Answer: 4" });
        if (streaming)
        {
            Assert.True(call.InformationalOnly);
            Assert.Contains(events, change => change is TranscriptChange.EntryStreamingStopped);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteTurn_StructuredJson_EmitsFormattedFinalText(bool streaming)
    {
        const string json = """{"summary":"Done","keyPoints":["one"],"category":"test","sentiment":"neutral"}""";
        var client = new ScriptedClient(
            new ChatResponse([new ChatMessage(ChatRole.Assistant, json)]),
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(json[..20])]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(json[20..])]),
            ]);
        var processor = new ChatConversation();
        var events = new List<TranscriptChange>();
        await foreach (var change in processor.SendTurnAsync(
            client, new ChatMessage(ChatRole.User, "Respond with JSON"), "Respond with JSON",
            null, streaming, structuredJson: true))
            events.Add(change);

        var finalText = streaming
            ? events.OfType<TranscriptChange.EntryTextChanged>().Last().Text
            : Assert.Single(events.OfType<TranscriptChange.EntryAdded>(), change =>
                change is { EntryKind: TranscriptEntryKind.Assistant, Label: "Structured JSON" }).Text;
        using var document = JsonDocument.Parse(finalText);
        Assert.Equal("Done", document.RootElement.GetProperty("summary").GetString());
        Assert.DoesNotContain(events.OfType<TranscriptChange.EntryAdded>(), change =>
            change is { EntryKind: TranscriptEntryKind.Assistant, Label: "Text" });
    }

    [Fact]
    public async Task ExecuteTurn_ImageAndToolResult_EmitInlineBytesAndPreserveHistory()
    {
        var image = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "playground_sample.png"));
        var generated = new ImageGenerationToolResultContent("image-1")
        {
            Outputs = [new DataContent(image, "image/png")],
        };
        var client = new ScriptedClient(
            new ChatResponse([
                new ChatMessage(ChatRole.Tool, [generated]),
                new ChatMessage(ChatRole.Assistant, "Created."),
            ]),
            [new ChatResponseUpdate(ChatRole.Tool, [generated])]);
        var processor = new ChatConversation();
        var events = new List<TranscriptChange>();
        await foreach (var change in processor.SendTurnAsync(
            client, new ChatMessage(ChatRole.User, [new TextContent("Edit this"), new DataContent(image, "image/png")]),
            "Edit this", null, streaming: false, structuredJson: false))
            events.Add(change);
        Assert.True(processor.HistoryContainsImage);
        Assert.Equal(2, events.OfType<TranscriptChange.EntryAdded>().Count(change => change.ImageBytes is not null));
        Assert.All(events.OfType<TranscriptChange.EntryAdded>().Where(change => change.ImageBytes is not null),
            change => Assert.Equal(image, change.ImageBytes));
        Assert.Contains(client.ReceivedMessages!, message => message.Contents.OfType<DataContent>()
            .Any(content => content.Data.ToArray().SequenceEqual(image)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteTurn_RepeatedNativeReasoning_DoesNotReachNextRequest(bool streaming)
    {
        var nativeItem = new object();
        var firstReasoning = new TextReasoningContent("Preparing the image")
        {
            ProtectedData = "opaque reasoning",
            RawRepresentation = nativeItem,
        };
        var repeatedReasoning = new TextReasoningContent("Image prepared")
        {
            ProtectedData = "opaque reasoning",
            RawRepresentation = nativeItem,
        };
        var finalText = new TextContent("Done.") { RawRepresentation = new object() };
        var client = new ScriptedClient(
            new ChatResponse([
                new ChatMessage(ChatRole.Assistant, [firstReasoning]) { RawRepresentation = nativeItem },
                new ChatMessage(ChatRole.Assistant, [repeatedReasoning, finalText])
                {
                    RawRepresentation = nativeItem,
                },
            ]),
            [
                new ChatResponseUpdate(ChatRole.Assistant, [firstReasoning]),
                new ChatResponseUpdate(ChatRole.Assistant, [repeatedReasoning, finalText]),
            ]);
        var conversation = new ChatConversation();
        await foreach (var _ in conversation.SendTurnAsync(
            client, new ChatMessage(ChatRole.User, "Create an image"), "Create an image",
            null, streaming, structuredJson: false)) { }
        await foreach (var _ in conversation.SendTurnAsync(
            client, new ChatMessage(ChatRole.User, "Describe it"), "Describe it",
            null, streaming: false, structuredJson: false)) { }

        var history = Assert.IsType<ChatMessage[]>(client.ReceivedMessages);
        Assert.Equal(2, history.SelectMany(message => message.Contents)
            .OfType<TextReasoningContent>().Count(content => content.ProtectedData == "opaque reasoning"));
        Assert.All(history, message =>
        {
            Assert.Null(message.RawRepresentation);
            Assert.All(message.Contents, content => Assert.Null(content.RawRepresentation));
        });
    }

    [Fact]
    public async Task ExecuteTurn_InterruptedStream_EndsVisibleBubbleAndPropagatesFailure()
    {
        var client = new ScriptedClient(
            new ChatResponse([]),
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Partial")])],
            failAfterUpdates: true);
        var processor = new ChatConversation();
        var events = new List<TranscriptChange>();

        async Task DrainAsync()
        {
            await foreach (var change in processor.SendTurnAsync(
                client, new ChatMessage(ChatRole.User, "Say hello"), "Say hello",
                null, streaming: true, structuredJson: false))
                events.Add(change);
        }
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(DrainAsync);

        Assert.Equal("The stream failed.", exception.Message);
        var started = Assert.Single(events.OfType<TranscriptChange.EntryAdded>(), change => change.IsStreaming);
        Assert.Contains(events, change => change is TranscriptChange.EntryStreamingStopped ended && ended.EntryId == started.EntryId);
    }

    [Fact]
    public async Task ReadTurnChanges_ProducerDoesNotWaitForTranscriptConsumer()
    {
        var client = new ScriptedClient(
            new ChatResponse([]),
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("One")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Two")]),
            ]);
        var conversation = new ChatConversation();

        await using var reader = conversation.SendTurnAsync(
            client, new ChatMessage(ChatRole.User, "Count"), "Count",
            null, streaming: true, structuredJson: false).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        var changes = new List<TranscriptChange> { reader.Current };
        await client.StreamCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        while (await reader.MoveNextAsync())
            changes.Add(reader.Current);
        Assert.Contains(changes, change => change is TranscriptChange.EntryTextChanged { Text: "OneTwo" });
    }

    [Fact]
    public async Task ReadTurnChanges_ReaderStopsEarly_CancelsProducer()
    {
        var client = new ScriptedClient(
            new ChatResponse([]),
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Partial")])],
            waitForCancellation: true);
        var conversation = new ChatConversation();

        var reader = conversation.SendTurnAsync(
            client, new ChatMessage(ChatRole.User, "Count"), "Count",
            null, streaming: true, structuredJson: false).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        await client.StreamCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await reader.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(client.StreamCancelled.Task.IsCompletedSuccessfully);
    }

    private sealed class ScriptedClient(
        ChatResponse response,
        ChatResponseUpdate[] updates,
        bool failAfterUpdates = false,
        bool waitForCancellation = false) : IChatClient
    {
        public ChatMessage[]? ReceivedMessages { get; private set; }
        public TaskCompletionSource<bool> StreamCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> StreamCancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            ReceivedMessages = messages.ToArray();
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ReceivedMessages = messages.ToArray();
            foreach (var update in updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return update;
                await Task.Yield();
            }
            StreamCompleted.TrySetResult(true);
            if (waitForCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    StreamCancelled.TrySetResult(true);
                    throw;
                }
            }
            if (failAfterUpdates)
                throw new InvalidOperationException("The stream failed.");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType == typeof(IChatClient) ? this : null;

        public void Dispose() { }
    }
}
#pragma warning restore MEAI001
