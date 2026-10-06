using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        Assert.Equal("test", document.RootElement.GetProperty("category").GetString());
        Assert.Equal("neutral", document.RootElement.GetProperty("sentiment").GetString());
        Assert.Contains('\n', finalText);
        Assert.DoesNotContain(events.OfType<TranscriptChange.EntryAdded>(), change =>
            change is { EntryKind: TranscriptEntryKind.Assistant, Label: "Text" });
    }

    [Theory]
    [InlineData("""{"status":"ok","extra":{"values":[1,true,null]}}""")]
    [InlineData("""[{"kind":"custom"},42]""")]
    [InlineData("42")]
    [InlineData("null")]
    public async Task ExecuteTurn_StructuredJson_FormatsAnyJsonValue(string json)
    {
        var client = new ScriptedClient(
            new ChatResponse([new ChatMessage(ChatRole.Assistant, json)]), []);
        var processor = new ChatConversation();
        var events = new List<TranscriptChange>();
        await foreach (var change in processor.SendTurnAsync(
            client, new ChatMessage(ChatRole.User, "Respond with JSON"), "Respond with JSON",
            null, streaming: false, structuredJson: true))
            events.Add(change);

        var formatted = Assert.Single(events.OfType<TranscriptChange.EntryAdded>(), change =>
            change is { EntryKind: TranscriptEntryKind.Assistant, Label: "Structured JSON" }).Text;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(formatted)));
        if (json[0] is '{' or '[')
            Assert.Contains('\n', formatted);
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ExecuteTurn_ModelId_LabelsAllProviderOutputsButNotInput(bool streaming, bool structuredJson)
    {
        var image = new DataContent(new byte[] { 1, 2, 3 }, "image/png");
        var contents = new AIContent[]
        {
            new TextContent(structuredJson ? """{"answer":4}""" : "Answer: 4"),
            new TextReasoningContent("Calculated"),
            new FunctionCallContent("call", "calculate"),
            new FunctionResultContent("call", 4),
            new FunctionResultContent("unknown", null) { Exception = new InvalidOperationException("Tool failed") },
            image,
            new ImageGenerationToolResultContent("image") { Outputs = [image] },
        };
        using var client = new ScriptedClient(
            new ChatResponse([new ChatMessage(ChatRole.Assistant, contents)])
            {
                ModelId = "actual-model",
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 },
            },
            [
                new ChatResponseUpdate(ChatRole.Assistant, contents) { ModelId = "actual-model" },
                new ChatResponseUpdate { Contents = [new UsageContent(new UsageDetails { OutputTokenCount = 5 })] },
            ]);
        var changes = await CollectChangesAsync(client, streaming, structuredJson);

        var entries = changes.OfType<TranscriptChange.EntryAdded>().ToArray();
        Assert.All(entries.Where(entry => entry.EntryKind is TranscriptEntryKind.User or TranscriptEntryKind.System),
            entry => Assert.Null(entry.ModelId));
        var outputs = entries.Where(entry => entry.EntryKind is not (TranscriptEntryKind.User or TranscriptEntryKind.System)).ToArray();
        Assert.Equal(6, outputs.Length);
        Assert.All(outputs, entry => Assert.Equal("actual-model", entry.ModelId));
        Assert.Contains(outputs, entry => entry.EntryKind == TranscriptEntryKind.Assistant);
        Assert.Contains(outputs, entry => entry.EntryKind == TranscriptEntryKind.Reasoning);
        Assert.Equal(2, outputs.Count(entry => entry.EntryKind == TranscriptEntryKind.Tool));
        Assert.Equal(2, outputs.Count(entry => entry.EntryKind == TranscriptEntryKind.Image));
        Assert.Single(changes.OfType<TranscriptChange.ToolCallResolved>());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ExecuteTurn_AdjacentAssistantMessages_KeepTranscriptAndHistorySeparate(
        bool streaming, bool supplyMessageIds)
    {
        var firstId = supplyMessageIds ? "first" : null;
        var secondId = supplyMessageIds ? "second" : null;
        using var client = new ScriptedClient(
            new ChatResponse([
                new ChatMessage(ChatRole.Assistant, "First") { MessageId = firstId },
                new ChatMessage(ChatRole.Assistant, "Second") { MessageId = secondId },
            ]) { ModelId = "actual-model" },
            [
                new ChatResponseUpdate(ChatRole.Assistant, "First") { MessageId = firstId, ModelId = "actual-model" },
                new ChatResponseUpdate(ChatRole.Assistant, "Second") { MessageId = secondId, ModelId = "actual-model" },
            ]);
        var conversation = new ChatConversation();
        var changes = await CollectChangesAsync(client, streaming, conversation: conversation);

        var entries = changes.OfType<TranscriptChange.EntryAdded>()
            .Where(entry => entry.EntryKind == TranscriptEntryKind.Assistant).ToArray();
        Assert.Equal(2, entries.Length);
        Assert.All(entries, entry =>
        {
            Assert.Equal("actual-model", entry.ModelId);
            Assert.Equal(streaming, entry.IsStreaming);
        });
        if (streaming)
        {
            Assert.Collection(changes.OfType<TranscriptChange.EntryTextChanged>(),
                change => { Assert.Equal(entries[0].EntryId, change.EntryId); Assert.Equal("First", change.Text); },
                change => { Assert.Equal(entries[1].EntryId, change.EntryId); Assert.Equal("Second", change.Text); });
            Assert.Equal(entries.Select(entry => entry.EntryId),
                changes.OfType<TranscriptChange.EntryStreamingStopped>().Select(change => change.EntryId));
        }
        else
        {
            Assert.Equal(new[] { "First", "Second" }, entries.Select(entry => entry.Text));
        }

        using var nextClient = new ScriptedClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Next")]), []);
        await CollectChangesAsync(nextClient, streaming: false, conversation: conversation);
        var history = nextClient.ReceivedMessages!.Where(message => message.Role == ChatRole.Assistant).ToArray();
        Assert.Equal(new[] { "First", "Second" }, history.Select(message => message.Text));
        Assert.Equal(streaming ? new string?[] { null, null } : [firstId, secondId],
            history.Select(message => message.MessageId));
    }

    [Fact]
    public async Task ExecuteTurn_SameMessageIdFragments_CombineTranscriptAndHistory()
    {
        using var client = new ScriptedClient(new ChatResponse([]),
        [
            new ChatResponseUpdate(ChatRole.Assistant, "First ") { MessageId = "message", ModelId = "actual-model" },
            new ChatResponseUpdate { MessageId = "message", ModelId = "actual-model" },
            new ChatResponseUpdate(ChatRole.Assistant, "second") { MessageId = "message", ModelId = "actual-model" },
        ]);
        var conversation = new ChatConversation();
        var changes = await CollectChangesAsync(client, streaming: true, conversation: conversation);
        var entry = Assert.Single(changes.OfType<TranscriptChange.EntryAdded>(),
            entry => entry.EntryKind == TranscriptEntryKind.Assistant);
        Assert.Equal("actual-model", entry.ModelId);
        Assert.Equal("First second", changes.OfType<TranscriptChange.EntryTextChanged>().Last().Text);
        Assert.Equal(entry.EntryId, Assert.Single(changes.OfType<TranscriptChange.EntryStreamingStopped>()).EntryId);

        using var nextClient = new ScriptedClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Next")]), []);
        await CollectChangesAsync(nextClient, streaming: false, conversation: conversation);
        var history = Assert.Single(nextClient.ReceivedMessages!, message => message.Role == ChatRole.Assistant);
        Assert.Equal("First second", history.Text);
        Assert.Null(history.MessageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteTurn_DifferentMessageIds_KeepReasoningEntriesSeparate(bool streaming)
    {
        using var client = new ScriptedClient(
            new ChatResponse([
                new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("First thought")]) { MessageId = "first" },
                new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("Second thought")]) { MessageId = "second" },
            ]) { ModelId = "actual-model" },
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("First ")])
                    { MessageId = "first", ModelId = "actual-model" },
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("thought")])
                    { MessageId = "first", ModelId = "actual-model" },
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("Second thought")])
                    { MessageId = "second", ModelId = "actual-model" },
                new ChatResponseUpdate { MessageId = "third" },
            ]);
        var changes = await CollectChangesAsync(client, streaming);
        var entries = changes.OfType<TranscriptChange.EntryAdded>()
            .Where(entry => entry.EntryKind == TranscriptEntryKind.Reasoning).ToArray();
        Assert.Equal(2, entries.Length);
        Assert.All(entries, entry => Assert.Equal("actual-model", entry.ModelId));
        Assert.Equal("Second thought", entries[1].Text);
        if (streaming)
        {
            var combined = Assert.Single(changes.OfType<TranscriptChange.EntryTextChanged>());
            Assert.Equal(entries[0].EntryId, combined.EntryId);
            Assert.Equal("First thought", combined.Text);
        }
        else
        {
            Assert.Equal("First thought", entries[0].Text);
        }
        Assert.DoesNotContain(changes.OfType<TranscriptChange.EntryAdded>(),
            entry => entry.EntryKind == TranscriptEntryKind.Assistant);
    }

    [Fact]
    public async Task ExecuteTurn_TextBeforeToolWithoutFinalText_PreservesCompletionPlaceholder()
    {
        using var client = new ScriptedClient(new ChatResponse([]),
        [
            new ChatResponseUpdate(ChatRole.Assistant,
                [new TextContent("Calculating"), new FunctionCallContent("call", "calculate")])
                { ModelId = "actual-model" },
            new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent("call", 4)])
                { ModelId = "actual-model" },
        ]);
        var changes = await CollectChangesAsync(client, streaming: true);
        var entries = changes.OfType<TranscriptChange.EntryAdded>()
            .Where(entry => entry.EntryKind == TranscriptEntryKind.Assistant).ToArray();
        Assert.Equal(2, entries.Length);
        Assert.True(entries[0].IsStreaming);
        Assert.False(entries[1].IsStreaming);
        Assert.Equal("(The provider completed tool activity without a final text response.)", entries[1].Text);
        Assert.Equal("actual-model", entries[1].ModelId);
        Assert.Equal(entries[0].EntryId, Assert.Single(changes.OfType<TranscriptChange.EntryStreamingStopped>()).EntryId);
    }

    [Fact]
    public async Task ExecuteTurn_TrailingWhitespaceMessage_StopsEveryStreamingBubble()
    {
        using var client = new ScriptedClient(new ChatResponse([]),
        [
            new ChatResponseUpdate(ChatRole.Assistant, "Answer") { MessageId = "first", ModelId = "actual-model" },
            new ChatResponseUpdate(ChatRole.Assistant, " ") { MessageId = "second", ModelId = "actual-model" },
        ]);
        var changes = await CollectChangesAsync(client, streaming: true);
        var entries = changes.OfType<TranscriptChange.EntryAdded>()
            .Where(entry => entry.EntryKind == TranscriptEntryKind.Assistant).ToArray();

        Assert.Equal(2, entries.Length);
        Assert.Equal(entries.Select(entry => entry.EntryId),
            changes.OfType<TranscriptChange.EntryStreamingStopped>().Select(change => change.EntryId));
    }

    [Fact]
    public async Task ExecuteTurn_FilteredUpdates_PreserveMetadataAndOriginalContents()
    {
        var call = new FunctionCallContent("call", "calculate");
        var result = new FunctionResultContent("call", 4);
        var usage = new UsageContent(new UsageDetails { OutputTokenCount = 5 });
        var source = new ChatResponseUpdate(ChatRole.Assistant, [call, new TextContent("First"), result])
        {
            MessageId = "message",
            ModelId = "actual-model",
            ResponseId = "response",
            ConversationId = "conversation",
            AuthorName = "author",
            CreatedAt = DateTimeOffset.UtcNow,
            FinishReason = ChatFinishReason.Stop,
            RawRepresentation = new object(),
            AdditionalProperties = new() { ["provider"] = "value" },
        };
        var duplicates = new ChatResponseUpdate(ChatRole.Assistant, [call, usage, new TextContent("Second"), result])
            { MessageId = "message", ModelId = "actual-model" };
        using var client = new ScriptedClient(new ChatResponse([]), [source, duplicates]);
        var updates = new List<ChatResponseUpdate>();
        await new ChatTurnExecutor().ExecuteTurnAsync(client, null, true, updates.Add, CancellationToken.None);

        Assert.Equal(2, updates.Count);
        Assert.NotSame(source, updates[0]);
        Assert.NotSame(source.Contents, updates[0].Contents);
        Assert.Equal(source.Contents, updates[0].Contents);
        Assert.Equal(source.MessageId, updates[0].MessageId);
        Assert.Equal(source.Role, updates[0].Role);
        Assert.Equal(source.ModelId, updates[0].ModelId);
        Assert.Equal(source.ResponseId, updates[0].ResponseId);
        Assert.Equal(source.ConversationId, updates[0].ConversationId);
        Assert.Equal(source.AuthorName, updates[0].AuthorName);
        Assert.Equal(source.CreatedAt, updates[0].CreatedAt);
        Assert.Equal(source.FinishReason, updates[0].FinishReason);
        Assert.Same(source.RawRepresentation, updates[0].RawRepresentation);
        Assert.Same(source.AdditionalProperties, updates[0].AdditionalProperties);
        Assert.Equal("Second", Assert.IsType<TextContent>(Assert.Single(updates[1].Contents)).Text);
        Assert.Equal(4, duplicates.Contents.Count);
        Assert.Same(call, duplicates.Contents[0]);
        Assert.Same(usage, duplicates.Contents[1]);
        Assert.Same(result, duplicates.Contents[3]);
        Assert.True(call.InformationalOnly);
    }

    [Fact]
    public async Task ExecuteTurn_CompleteResponseWithRepeatedToolContent_EmitsOneCallAndResult()
    {
        var call = new FunctionCallContent("call", "calculate");
        var result = new FunctionResultContent("call", 4);
        using var client = new ScriptedClient(
            new ChatResponse([
                new ChatMessage(ChatRole.Assistant, [new TextContent("Before"), call, call]),
                new ChatMessage(ChatRole.Tool, [result, result]),
                new ChatMessage(ChatRole.Assistant, [call, new TextContent("After")]),
            ]) { ModelId = "actual-model" }, []);

        var changes = await CollectChangesAsync(client, streaming: false);

        Assert.Single(changes.OfType<TranscriptChange.EntryAdded>(),
            entry => entry.EntryKind == TranscriptEntryKind.Tool);
        Assert.Single(changes.OfType<TranscriptChange.ToolCallResolved>());
        Assert.Equal(new[] { "Before", "After" }, changes.OfType<TranscriptChange.EntryAdded>()
            .Where(entry => entry.EntryKind == TranscriptEntryKind.Assistant).Select(entry => entry.Text));
    }

    [Fact]
    public async Task ExecuteTurn_MetadataOnlyUpdate_DoesNotCreateTranscriptEntry()
    {
        using var client = new ScriptedClient(new ChatResponse([]),
        [
            new ChatResponseUpdate { ModelId = "actual-model" },
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Answer")]) { ModelId = "actual-model" },
        ]);
        var changes = await CollectChangesAsync(client, streaming: true);
        var output = Assert.Single(changes.OfType<TranscriptChange.EntryAdded>(),
            entry => entry.EntryKind == TranscriptEntryKind.Assistant);
        Assert.Equal("actual-model", output.ModelId);
        Assert.Contains(changes, change => change is TranscriptChange.EntryTextChanged { Text: "Answer" });
    }

    [Fact]
    public async Task ExecuteTurn_ModelId_DoesNotCarryIntoNextTurn()
    {
        var conversation = new ChatConversation();
        using var first = new ScriptedClient(
            new ChatResponse([new ChatMessage(ChatRole.Assistant, "First")]) { ModelId = "first-model" }, []);
        await CollectChangesAsync(first, streaming: false, conversation: conversation);
        using var second = new ScriptedClient(new ChatResponse([]),
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Second")]) { ModelId = "second-model" }]);
        var changes = await CollectChangesAsync(second, streaming: true, conversation: conversation);
        Assert.Equal("second-model", Assert.Single(changes.OfType<TranscriptChange.EntryAdded>(),
            entry => entry.EntryKind == TranscriptEntryKind.Assistant).ModelId);
    }

    private static async Task<List<TranscriptChange>> CollectChangesAsync(
        IChatClient client, bool streaming, bool structuredJson = false, ChatConversation? conversation = null)
    {
        var changes = new List<TranscriptChange>();
        await foreach (var change in (conversation ?? new ChatConversation()).SendTurnAsync(
            client, new ChatMessage(ChatRole.User, "Question"), "Question",
            new ChatOptions { ModelId = "requested-model", Instructions = "Answer briefly" }, streaming, structuredJson))
            changes.Add(change);
        return changes;
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
