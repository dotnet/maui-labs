using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Recording;
using Microsoft.Maui.AI.Chat;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.AI.Chat.Recording.Tests;

public sealed class ChatRecordingTests
{
    [Fact]
    public async Task RecordThenReplay_StreamingAndGetResponse_PreservesUpdates()
    {
        var tape = new ChatRecording();
        var session = new ChatRecordingSession(tape);
        using var recorder = new RecordingChatClient(new StubClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant, string.Empty) { MessageId = "one" },
            new ChatResponseUpdate(ChatRole.Assistant, "hello") { MessageId = "one", FinishReason = ChatFinishReason.Stop },
        ]), new ChatRecordingOptions { Mode = ChatRecordingMode.Record }, session);
        var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };

        var recorded = await recorder.GetStreamingResponseAsync(messages).ToListAsync();
        using var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape }, new ChatRecordingSession(tape));
        var replayed = await replay.GetStreamingResponseAsync(messages).ToListAsync();

        Assert.Equal(new[] { string.Empty, "hello" }, recorded.Select(x => x.Text));
        Assert.Equal(new[] { string.Empty, "hello" }, replayed.Select(x => x.Text));
        replay.Session.AssertFullyReplayed();
    }

    [Fact]
    public async Task Replay_DifferentRequest_ReportsPath()
    {
        var tape = new ChatRecording
        {
            Interactions = [new RecordedChatInteraction
            {
                Request = new JsonObject { ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["authorName"] = null, ["messageId"] = null, ["contents"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "expected" }), ["additional"] = null }), ["options"] = new JsonObject() },
            }],
        };
        using var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        var ex = await Assert.ThrowsAsync<ChatRecordingMismatchException>(async () =>
            await replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "actual")]).ToListAsync());
        Assert.Contains("$.messages[0].contents[0].text", ex.Message);
    }

    [Fact]
    public async Task Record_ProtectedReasoning_RejectsIt()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        update.Contents.Add(new TextReasoningContent("visible") { ProtectedData = "secret" });
        using var recorder = new RecordingChatClient(new StubClient([update]), new ChatRecordingOptions { Mode = ChatRecordingMode.Record });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync());
    }

    [Fact]
    public void Store_LegacyArray_LoadsOrdinalInteractions()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("[[]]"));
        var recording = ChatRecordingStore.Load(stream);
        Assert.Single(recording.Interactions);
        Assert.True(recording.Interactions[0].Legacy);
        Assert.Equal(
            "legacy-import-v1",
            recording.Metadata["sanitizerProfile"]!.GetValue<string>());
        Assert.True(recording.Manifest["legacyImport"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Store_LegacyMeaiUpdates_AreConvertedForReplay()
    {
        var updates = new List<List<ChatResponseUpdate>>
        {
            new() { new ChatResponseUpdate(ChatRole.Assistant, "legacy text") },
        };
        var json = JsonSerializer.Serialize(
            updates,
            AIJsonUtilities.DefaultOptions);
        using var stream = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(json));
        var recording = ChatRecordingStore.Load(stream);
        using var replay = new ReplayChatClient(
            new ChatRecordingOptions { Recording = recording });

        var result = await replay.GetStreamingResponseAsync([]).ToListAsync();

        Assert.Equal("legacy text", Assert.Single(result).Text);
        replay.Session.AssertFullyReplayed();
    }

    [Fact]
    public async Task RecordThenReplay_RichTextAndApproval_PreserveContent()
    {
        var richNode = new ParagraphNode();
        richNode.AddChild(new TextNode("rich"));
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        update.Contents.Add(new RichTextContent("rich", [richNode]));
        update.Contents.Add(new ToolApprovalRequestContent("request", new ToolCallContent("call")));
        var tape = new ChatRecording();
        using var recorder = new RecordingChatClient(new StubClient([update]), new ChatRecordingOptions { Mode = ChatRecordingMode.Record, Recording = tape });
        await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync();

        using var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        var replayed = await replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync();
        var rich = Assert.IsType<RichTextContent>(replayed.Single().Contents[0]);
        Assert.Equal("rich", rich.Text);
        Assert.IsType<ParagraphNode>(rich.Nodes.Single());
        Assert.IsType<ToolApprovalRequestContent>(replayed.Single().Contents[1]);
    }

    [Fact]
    public async Task Replay_EndOfTapeAndUnconsumed_AreReported()
    {
        var tape = new ChatRecording { Interactions = [new RecordedChatInteraction { Sequence = 0, Legacy = true }, new RecordedChatInteraction { Sequence = 1, Legacy = true }] };
        using var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        await replay.GetStreamingResponseAsync([]).ToListAsync();
        Assert.Throws<InvalidOperationException>(() => replay.Session.AssertFullyReplayed());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await replay.GetStreamingResponseAsync([]).ToListAsync();
            await replay.GetStreamingResponseAsync([]).ToListAsync();
        });
    }

    [Fact]
    public async Task Replay_MessageIdsAreIgnoredUnlessRequired()
    {
        var tape = await RecordAsync([new ChatResponseUpdate(ChatRole.Assistant, "ok")],
            [new ChatMessage(ChatRole.User, "question") { MessageId = "recorded" }]);
        using var ignored = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        Assert.Single(await ignored.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "question") { MessageId = "new" }]).ToListAsync());

        var strictTape = await RecordAsync([new ChatResponseUpdate(ChatRole.Assistant, "ok")],
            [new ChatMessage(ChatRole.User, "question") { MessageId = "recorded" }],
            new ChatRecordingOptions { Mode = ChatRecordingMode.Record, RequireMessageId = true });
        using var required = new ReplayChatClient(new ChatRecordingOptions { Recording = strictTape, RequireMessageId = true });
        await Assert.ThrowsAsync<ChatRecordingMismatchException>(async () =>
            await required.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "question") { MessageId = "new" }]).ToListAsync());
    }

    [Fact]
    public async Task Record_VisibleReasoning_RoundTripsWithoutProtectedData()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        update.Contents.Add(new TextReasoningContent("visible"));
        var tape = await RecordAsync([update]);
        using var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        var replayed = await replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync();
        var reasoning = Assert.IsType<TextReasoningContent>(replayed.Single().Contents.Single());
        Assert.Equal("visible", reasoning.Text);
        Assert.Null(reasoning.ProtectedData);
    }

    [Fact]
    public async Task Record_SafeUriContent_RoundTripsImageGenerationOutput()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        update.Contents.Add(new ImageGenerationToolResultContent("image-1")
        {
            Outputs = [new UriContent("https://images.example.test/generated.png", "image/png")],
        });
        var tape = await RecordAsync([update], [new ChatMessage(ChatRole.User, "draw")]);
        using var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        var output = Assert.IsType<UriContent>(Assert.IsType<ImageGenerationToolResultContent>(
            (await replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "draw")]).ToListAsync()).Single().Contents.Single()).Outputs!.Single());
        Assert.Equal("https://images.example.test/generated.png", output.Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://user:password@images.example.test/a.png")]
    [InlineData("https://images.example.test/a.png?sig=secret")]
    [InlineData("https://api.openai.com/v1/images/a.png")]
    public async Task Record_UnsafeUriContent_StrictModeRejects(string value)
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        update.Contents.Add(new UriContent(value, "image/png"));
        using var recorder = new RecordingChatClient(new StubClient([update]), new ChatRecordingOptions { Mode = ChatRecordingMode.Record });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync());
    }

    [Fact]
    public async Task Record_NonStrictStructuredSecret_RedactsNestedValues()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        update.Contents.Add(new FunctionCallContent("call", "find", new Dictionary<string, object?>
        {
            ["items"] = new object?[] { new Dictionary<string, object?> { ["authorization"] = "Bearer secret" } },
        }));
        var options = new ChatRecordingOptions { Mode = ChatRecordingMode.Record, StrictSanitizer = false };
        var tape = await RecordAsync([update], options: options);
        var value = tape.Interactions.Single().Updates.Single().Value.ToJsonString();
        Assert.DoesNotContain("secret", value, StringComparison.OrdinalIgnoreCase);
        Assert.True(tape.Manifest["redacted"]!.GetValue<int>() > 0);
    }

    [Fact]
    public async Task Record_StructuredSecretsInJsonAndLists_StrictModeRejects()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        update.Contents.Add(new FunctionResultContent("call", JsonNode.Parse("""{"items":[{"x-ms-token":"secret"}]}""")));
        using var recorder = new RecordingChatClient(new StubClient([update]), new ChatRecordingOptions { Mode = ChatRecordingMode.Record });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync());
    }

    [Fact]
    public async Task Record_JsonElementWithBearerValue_StrictModeRejects()
    {
        using var document = JsonDocument.Parse("""{"nested":["Bearer not-for-recording"]}""");
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        update.Contents.Add(new FunctionResultContent("call", document.RootElement));
        using var recorder = new RecordingChatClient(new StubClient([update]), new ChatRecordingOptions { Mode = ChatRecordingMode.Record });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync());
    }

    [Fact]
    public async Task RecordThenReplay_InputRequestAndResponse_RoundTripRequestIds()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        update.Contents.Add(new TestInputRequest("request"));
        update.Contents.Add(new TestInputResponse("request"));
        var tape = await RecordAsync([update]);
        using var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        var contents = (await replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync()).Single().Contents;
        Assert.Equal("request", Assert.IsAssignableFrom<InputRequestContent>(contents[0]).RequestId);
        Assert.Equal("request", Assert.IsAssignableFrom<InputResponseContent>(contents[1]).RequestId);
    }

    [Fact]
    public async Task RecordThenReplay_BuiltInContent_RoundTripsAllSupportedFields()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null) { FinishReason = ChatFinishReason.ToolCalls };
        update.Contents.Add(new FunctionCallContent("fc", "function", new Dictionary<string, object?> { ["n"] = 2 }) { InformationalOnly = true });
        update.Contents.Add(new FunctionResultContent("fc", new Dictionary<string, object?> { ["ok"] = true }));
        update.Contents.Add(new ToolCallContent("tc"));
        update.Contents.Add(new ToolResultContent("tc"));
        update.Contents.Add(new ToolApprovalRequestContent("approval", new ToolCallContent("tc")));
        update.Contents.Add(new ToolApprovalResponseContent("approval", false, new ToolCallContent("tc")) { Reason = "not now" });
        update.Contents.Add(new ImageGenerationToolCallContent("image"));
        update.Contents.Add(new UsageContent(new UsageDetails { InputTokenCount = 3, OutputTokenCount = 5, AdditionalCounts = new AdditionalPropertiesDictionary<long> { ["cached"] = 1 } }));
        update.Contents.Add(new ErrorContent("nonfatal") { ErrorCode = "E", Details = "detail" });
        var tape = await RecordAsync([new ChatResponseUpdate(ChatRole.Assistant, string.Empty), update]);
        using var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        var values = await replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync();
        Assert.Equal(string.Empty, values[0].Text);
        Assert.True(Assert.IsType<FunctionCallContent>(values[1].Contents[0]).InformationalOnly);
        Assert.Equal("not now", Assert.IsType<ToolApprovalResponseContent>(values[1].Contents[5]).Reason);
        Assert.Equal(5, Assert.IsType<UsageContent>(values[1].Contents[7]).Details.OutputTokenCount);
        Assert.Equal("E", Assert.IsType<ErrorContent>(values[1].Contents[8]).ErrorCode);
    }

    [Fact]
    public async Task RecordThenReplay_RichTextEveryCurrentNodeForm_PreservesTree()
    {
        var nodes = new RichTextNode[]
        {
            new ParagraphNode(), new BlockQuoteNode(), new EmphasisNode(), new StrongNode(), new StrikethroughNode(), new LineBreakNode(),
            new ThematicBreakNode(), new TableRowNode(), new TableCellNode(), new FootnoteNode(), new TextNode("text"), new HeadingNode(2),
            new CodeBlockNode("code", "c#"), new InlineCodeNode("inline"), new LinkNode("https://example.test", "title"),
            new ImageNode("https://example.test/image.png", "alt", "title"), new ListNode(true, 3), new ListItemNode { Checked = true },
            new TableNode { Alignment = [TableColumnAlignment.Left, TableColumnAlignment.Right] }, new HtmlNode("<b>x</b>"),
            new DefinitionNode { Label = "d", Url = "https://example.test/d", Title = "t" }, new LinkReferenceNode { Label = "l", ReferenceKind = ReferenceKind.Full },
            new ImageReferenceNode { Label = "i", Alt = "a", ReferenceKind = ReferenceKind.Collapsed }, new FootnoteDefinitionNode { Label = "fd" },
            new FootnoteReferenceNode { Label = "fr" },
        };
        nodes[0].AddChild(new TextNode("nested"));
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        update.Contents.Add(new RichTextContent("tree", nodes));
        var tape = await RecordAsync([update]);
        using var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        var rich = Assert.IsType<RichTextContent>((await replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync()).Single().Contents.Single());
        Assert.Equal(nodes.Select(x => x.GetType()), rich.Nodes.Select(x => x.GetType()));
        Assert.Equal("nested", Assert.IsType<TextNode>(rich.Nodes[0].Children.Single()).Text);
    }

    [Fact]
    public async Task Store_ExtractsAndVerifiesBlobsFromRequestsAndUpdates()
    {
        var root = CreateFixtureDirectory();
        try
        {
            var path = Path.Combine(root, "recording.json");
            var bytes = Enumerable.Repeat((byte)7, 128).ToArray();
            var request = new ChatMessage(ChatRole.User, [new DataContent(bytes, "application/octet-stream")]);
            var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
            update.Contents.Add(new DataContent(bytes, "application/octet-stream"));
            var tape = await RecordAsync([update], [request], new ChatRecordingOptions { Mode = ChatRecordingMode.Record, InlineDataThresholdBytes = 1 });
            ChatRecordingStore.Save(tape, path);
            Assert.Contains("\"blob\"", tape.Interactions.Single().Request.ToJsonString());
            Assert.Contains("\"blob\"", File.ReadAllText(path));

            using var replay = new ReplayChatClient(new ChatRecordingOptions { FixturePath = path });
            var replayed = await replay.GetStreamingResponseAsync([request]).ToListAsync();
            Assert.Equal(bytes, Assert.IsType<DataContent>(replayed.Single().Contents.Single()).Data.ToArray());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Replay_BlobFromFixtureStream_ExplainsSidecarsAreUnavailable()
    {
        var tape = new ChatRecording
        {
            Interactions =
            [
                new RecordedChatInteraction
                {
                    Sequence = 0,
                    Request = new JsonObject { ["messages"] = new JsonArray(), ["options"] = new JsonObject() },
                    Updates = [new RecordedChatUpdate { Sequence = 0, Value = new JsonObject { ["contents"] = new JsonArray(BlobData("../data")) } }],
                },
            ],
        };
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tape)));
        using var replay = new ReplayChatClient(new ChatRecordingOptions { FixtureStream = stream });
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () => await replay.GetStreamingResponseAsync([]).ToListAsync());
        Assert.Contains("fixture stream", error.Message);
    }

    [Fact]
    public async Task Replay_BlobTraversal_IsRejected()
    {
        var root = CreateFixtureDirectory();
        try
        {
            var path = Path.Combine(root, "recording.json");
            var tape = new ChatRecording
            {
                Interactions =
                [
                    new RecordedChatInteraction
                    {
                        Sequence = 0,
                        Request = new JsonObject { ["messages"] = new JsonArray(), ["options"] = new JsonObject() },
                        Updates = [new RecordedChatUpdate { Sequence = 0, Value = new JsonObject { ["contents"] = new JsonArray(BlobData("../outside")) } }],
                    },
                ],
            };
            ChatRecordingStore.Save(tape, path);
            using var replay = new ReplayChatClient(new ChatRecordingOptions { FixturePath = path });
            var error = await Assert.ThrowsAsync<InvalidDataException>(async () => await replay.GetStreamingResponseAsync([]).ToListAsync());
            Assert.Contains("escapes", error.Message);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Record_OutcomeTracksErrorCanceledAndAbandoned()
    {
        var errorTape = await RecordAsync(new ThrowingClient(new InvalidOperationException("failure")));
        Assert.Equal("error", errorTape.Interactions.Single().Outcome);

        var cts = new CancellationTokenSource();
        cts.Cancel();
        var canceledTape = new ChatRecording();
        using (var recorder = new RecordingChatClient(new StubClient([new ChatResponseUpdate(ChatRole.Assistant, "x")]), new ChatRecordingOptions { Mode = ChatRecordingMode.Record, Recording = canceledTape }))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")], cancellationToken: cts.Token).ToListAsync());
        Assert.Equal("canceled", canceledTape.Interactions.Single().Outcome);

        var abandonedTape = new ChatRecording();
        using (var recorder = new RecordingChatClient(new StubClient([new ChatResponseUpdate(ChatRole.Assistant, "one"), new ChatResponseUpdate(ChatRole.Assistant, "two")]), new ChatRecordingOptions { Mode = ChatRecordingMode.Record, Recording = abandonedTape }))
            await foreach (var _ in recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")])) break;
        Assert.Equal("abandoned", abandonedTape.Interactions.Single().Outcome);
    }

    [Fact]
    public async Task Replay_CanceledTapeReturnsPrefix_AndConsumerCancellationPreemptsYield()
    {
        var tape = new ChatRecording
        {
            Interactions =
            [
                new RecordedChatInteraction
                {
                    Sequence = 0, Outcome = "canceled", Request = new JsonObject { ["messages"] = new JsonArray(), ["options"] = new JsonObject() },
                    Updates = [new RecordedChatUpdate { Sequence = 0, Value = new JsonObject { ["contents"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "prefix" }) } }],
                },
            ],
        };
        using (var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape }))
            Assert.Equal("prefix", (await replay.GetStreamingResponseAsync([]).ToListAsync()).Single().Text);
        using var canceledReplay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceledReplay.GetStreamingResponseAsync([], cancellationToken: cts.Token).ToListAsync());
    }

    [Fact]
    public async Task Recording_GetResponseDrainsStreamingPath_AndDelegatesServiceAndDispose()
    {
        var inner = new StubClient([new ChatResponseUpdate(ChatRole.Assistant, "from-stream")]) { Service = "service" };
        var tape = new ChatRecording();
        var recorder = new RecordingChatClient(inner, new ChatRecordingOptions { Mode = ChatRecordingMode.Record, Recording = tape });
        var response = await recorder.GetResponseAsync([new ChatMessage(ChatRole.User, "x")]);
        Assert.Equal("from-stream", response.Text);
        Assert.Single(tape.Interactions);
        Assert.Equal("service", recorder.GetService(typeof(string)));
        recorder.Dispose();
        Assert.True(inner.Disposed);
    }

    [Fact]
    public async Task Replay_RecordedErrorAndZeroTimingCap_AreDeterministic()
    {
        var tape = new ChatRecording
        {
            Interactions =
            [
                new RecordedChatInteraction { Sequence = 0, Request = new JsonObject { ["messages"] = new JsonArray(), ["options"] = new JsonObject() }, Outcome = "error", ErrorType = "Provider", ErrorMessage = "bad", Updates = [new RecordedChatUpdate { Sequence = 0, DelayMs = 60_000, Value = new JsonObject() }] },
            ],
        };
        using var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape, ReplayTiming = ReplayTiming.Recorded, MaximumReplayDelay = TimeSpan.Zero });
        var exception = await Assert.ThrowsAsync<RecordedChatException>(async () => await replay.GetStreamingResponseAsync([]).ToListAsync());
        Assert.Equal("Provider", exception.RecordedType);
    }

    [Fact]
    public async Task Record_CustomContentAndRawCodecs_RoundTrip()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null) { RawRepresentation = new CustomRaw("raw") };
        update.Contents.Add(new CustomContent("content"));
        var codecs = new ChatRecordingOptions { Mode = ChatRecordingMode.Record };
        codecs.ContentCodecs.Add(new CustomContentCodec());
        codecs.RawCodecs.Add(new CustomRawCodec());
        var tape = await RecordAsync([update], options: codecs);
        var replayOptions = new ChatRecordingOptions { Recording = tape };
        replayOptions.ContentCodecs.Add(new CustomContentCodec());
        replayOptions.RawCodecs.Add(new CustomRawCodec());
        using var replay = new ReplayChatClient(replayOptions);
        var replayed = (await replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync()).Single();
        Assert.Equal("content", Assert.IsType<CustomContent>(replayed.Contents.Single()).Value);
        Assert.Equal("raw", Assert.IsType<CustomRaw>(replayed.RawRepresentation).Value);
    }

    [Fact]
    public async Task Record_UnknownRawAndAdditionalProperties_StrictModeRejects()
    {
        var raw = new ChatResponseUpdate(ChatRole.Assistant, "x") { RawRepresentation = new object() };
        using var rawRecorder = new RecordingChatClient(new StubClient([raw]), new ChatRecordingOptions { Mode = ChatRecordingMode.Record });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await rawRecorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync());

        var message = new ChatMessage(ChatRole.User, "x") { AdditionalProperties = new AdditionalPropertiesDictionary { ["unknown"] = "value" } };
        using var additionalRecorder = new RecordingChatClient(new StubClient([]), new ChatRecordingOptions { Mode = ChatRecordingMode.Record });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await additionalRecorder.GetStreamingResponseAsync([message]).ToListAsync());
    }

    [Fact]
    public async Task Store_RejectsInvalidSchemaAndOutOfOrderSequences_SynchronouslyAndAsynchronously()
    {
        const string invalid = """{"format":"maui-ai-chat-recording","schemaVersion":2,"metadata":{},"manifest":{},"interactions":[]}""";
        using var sync = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalid));
        Assert.Throws<InvalidDataException>(() => ChatRecordingStore.Load(sync));
        using var missingManifest = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""{"format":"maui-ai-chat-recording","schemaVersion":1,"metadata":{},"interactions":[]}"""));
        Assert.Throws<InvalidDataException>(() => ChatRecordingStore.Load(missingManifest));
        await using var asynchronous = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalid));
        await Assert.ThrowsAsync<InvalidDataException>(() => ChatRecordingStore.LoadAsync(asynchronous));
        var outOfOrder = new ChatRecording { Interactions = [new RecordedChatInteraction { Sequence = 1 }] };
        Assert.Throws<InvalidDataException>(() => ChatRecordingStore.Save(outOfOrder, new MemoryStream()));
        Assert.Throws<InvalidDataException>(() => ChatRecordingStore.Save(new ChatRecording { SchemaVersion = 2 }, new MemoryStream()));
    }

    [Fact]
    public async Task Record_RequestSnapshotPrecedesUnderlyingEnumeration_AndPreservesMultiMessageOptionsAndTools()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "rules") { AuthorName = "system" },
            new(ChatRole.User, "original") { AuthorName = "person" },
        };
        var tool = AIFunctionFactory.Create((Func<string, string>)(value => value), "echo", "Echoes a value");
        var options = new ChatOptions { Temperature = 0.3f, ModelId = "model", Tools = [tool] };
        var tape = new ChatRecording();
        using (var recorder = new RecordingChatClient(new MutatingClient(), new ChatRecordingOptions { Mode = ChatRecordingMode.Record, Recording = tape }))
            await recorder.GetStreamingResponseAsync(messages, options).ToListAsync();
        Assert.Contains("original", tape.Interactions.Single().Request.ToJsonString());
        Assert.DoesNotContain("mutated", tape.Interactions.Single().Request.ToJsonString());

        using var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        await replay.GetStreamingResponseAsync(
        [
            new ChatMessage(ChatRole.System, "rules") { AuthorName = "system" },
            new ChatMessage(ChatRole.User, "original") { AuthorName = "person" },
        ], options).ToListAsync();
    }

    [Fact]
    public async Task Record_AllowedUpdateAdditionalProperties_RoundTrip()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, "x")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { ["agui_thread_id"] = "thread" },
        };
        var options = new ChatRecordingOptions { Mode = ChatRecordingMode.Record, AllowAguiThreadId = true };
        var tape = await RecordAsync([update], options: options);
        using var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        var replayed = (await replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync()).Single();
        Assert.Equal("thread", replayed.AdditionalProperties!["agui_thread_id"]);
    }

    [Fact]
    public async Task Replay_UnknownRawAndAdditionalProperties_StrictModeRejects()
    {
        var tape = new ChatRecording
        {
            Interactions =
            [
                new RecordedChatInteraction
                {
                    Sequence = 0,
                    Request = new JsonObject { ["messages"] = new JsonArray(), ["options"] = new JsonObject() },
                    Updates =
                    [
                        new RecordedChatUpdate
                        {
                            Sequence = 0,
                            Value = new JsonObject { ["raw"] = new JsonObject { ["type"] = "unknown", ["value"] = "value" } },
                        },
                    ],
                },
            ],
        };
        using var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await replay.GetStreamingResponseAsync([]).ToListAsync());

        tape.Interactions[0].Updates[0].Value = new JsonObject { ["additional"] = new JsonObject { ["unknown"] = "value" } };
        using var additionalReplay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await additionalReplay.GetStreamingResponseAsync([]).ToListAsync());
    }

    [Fact]
    public async Task Record_UnknownContentWithoutCodec_Rejects()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        update.Contents.Add(new CustomContent("unregistered"));
        using var recorder = new RecordingChatClient(new StubClient([update]), new ChatRecordingOptions { Mode = ChatRecordingMode.Record });
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync());
    }

    [Fact]
    public async Task Record_OneShotMessages_MaterializesOnceAndForwardsExactSnapshot()
    {
        var source = new OneShotMessages(
        [
            new ChatMessage(ChatRole.System, "rules"),
            new ChatMessage(ChatRole.User, "question"),
        ]);
        var provider = new CapturingClient();
        var tape = new ChatRecording();
        using (var recorder = new RecordingChatClient(provider, new ChatRecordingOptions { Mode = ChatRecordingMode.Record, Recording = tape }))
            await recorder.GetStreamingResponseAsync(source).ToListAsync();

        Assert.Equal(new[] { "rules", "question" }, provider.Messages!.Select(message => message.Text));
        using var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        await replay.GetStreamingResponseAsync(
        [
            new ChatMessage(ChatRole.System, "rules"),
            new ChatMessage(ChatRole.User, "question"),
        ]).ToListAsync();

        var liveProvider = new CapturingClient();
        using var live = new RecordingChatClient(liveProvider, new ChatRecordingOptions { Mode = ChatRecordingMode.Live });
        await live.GetStreamingResponseAsync(new OneShotMessages(
        [
            new ChatMessage(ChatRole.System, "rules"),
            new ChatMessage(ChatRole.User, "question"),
        ])).ToListAsync();
        Assert.Equal(new[] { "rules", "question" }, liveProvider.Messages!.Select(message => message.Text));
    }

    [Theory]
    [InlineData("Bearer abc.def.ghi")]
    [InlineData("api-key=super-secret")]
    [InlineData("Authorization: super-secret")]
    [InlineData("Cookie: session=super-secret")]
    [InlineData("connection string=Server=provider.example.test;Password=super-secret")]
    [InlineData("request failed at https://api.openai.com/v1/chat")]
    public async Task Record_SecretBearingProviderErrors_SaveOnlyStableSanitizedMessage(string message)
    {
        var tape = new ChatRecording();
        using var recorder = new RecordingChatClient(
            new ThrowingClient(new InvalidOperationException(message)),
            new ChatRecordingOptions { Mode = ChatRecordingMode.Record, Recording = tape });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync());
        using var stream = new MemoryStream();
        ChatRecordingStore.Save(tape, stream);
        var saved = System.Text.Encoding.UTF8.GetString(stream.ToArray());

        Assert.Equal("ProviderError", tape.Interactions.Single().ErrorType);
        Assert.Equal("[REDACTED provider error]", tape.Interactions.Single().ErrorMessage);
        Assert.Equal(1, tape.Manifest["redacted"]!.GetValue<int>());
        Assert.DoesNotContain("super-secret", saved, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api.openai.com", saved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Record_OptionsAndUpdateContinuationTokens_MatchExactlyAndReplay()
    {
        var requestToken = ResponseContinuationToken.FromBytes(new byte[] { 1, 2, 3 });
        var updateToken = ResponseContinuationToken.FromBytes(new byte[] { 4, 5, 6 });
        using var schemaDocument = JsonDocument.Parse("""{"type":"object","properties":{"answer":{"type":"string"}}}""");
        var options = new ChatOptions
        {
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High, Output = ReasoningOutput.Summary },
            ToolMode = ChatToolMode.RequireSpecific("lookup"),
            ContinuationToken = requestToken,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(schemaDocument.RootElement, "answer", "A safe answer"),
        };
        var update = new ChatResponseUpdate(ChatRole.Assistant, "ok") { ContinuationToken = updateToken };
        var tape = await RecordAsync([update], options);
        var recordedOptions = tape.Interactions.Single().Request["options"]!.AsObject();
        Assert.Equal("High", recordedOptions["reasoning"]!["effort"]!.GetValue<string>());
        Assert.Equal("required", recordedOptions["toolMode"]!["kind"]!.GetValue<string>());
        Assert.Equal("json", recordedOptions["responseFormat"]!["kind"]!.GetValue<string>());

        using (var replay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape }))
        {
            var replayed = (await replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")], options).ToListAsync()).Single();
            Assert.Equal(updateToken.ToBytes().ToArray(), replayed.ContinuationToken!.ToBytes().ToArray());
        }

        var mismatched = options.Clone();
        mismatched.ContinuationToken = ResponseContinuationToken.FromBytes(new byte[] { 7, 8, 9 });
        using var mismatchReplay = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        var mismatch = await Assert.ThrowsAsync<ChatRecordingMismatchException>(async () =>
            await mismatchReplay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")], mismatched).ToListAsync());
        Assert.Equal("$.options.continuationToken", mismatch.Path);
    }

    [Fact]
    public async Task Record_RequestFactory_RequiresCodecAndCodecExtensionMatchesWithoutSerializingDelegate()
    {
        var options = new ChatOptions { RawRepresentationFactory = _ => new object() };
        using (var recorder = new RecordingChatClient(new StubClient([]), new ChatRecordingOptions { Mode = ChatRecordingMode.Record }))
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")], options).ToListAsync());

        var recordingOptions = new ChatRecordingOptions { Mode = ChatRecordingMode.Record };
        recordingOptions.RequestCodecs.Add(new TestRequestCodec());
        var tape = await RecordAsync([], options, recordingOptions);
        Assert.DoesNotContain("RawRepresentationFactory", tape.Interactions[0].Request.ToJsonString());
        Assert.DoesNotContain("System.Func", tape.Interactions[0].Request.ToJsonString());

        var replayOptions = new ChatRecordingOptions { Recording = tape };
        replayOptions.RequestCodecs.Add(new TestRequestCodec());
        using var replay = new ReplayChatClient(replayOptions);
        await replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")], options).ToListAsync();
    }

    [Theory]
    [InlineData("https://user:password@example.test/link", 0)]
    [InlineData("https://images.example.test/image.png?sig=secret", 1)]
    [InlineData("https://api.openai.com/definition", 2)]
    public async Task Record_UnsafeRichTextUris_StrictModeRejects(string uri, int nodeIndex)
    {
        RichTextNode node = nodeIndex switch
        {
            0 => new LinkNode(uri),
            1 => new ImageNode(uri),
            _ => new DefinitionNode { Label = "definition", Url = uri },
        };
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        update.Contents.Add(new RichTextContent("rich", [node]));
        using var recorder = new RecordingChatClient(new StubClient([update]), new ChatRecordingOptions { Mode = ChatRecordingMode.Record });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync());
    }

    [Fact]
    public async Task Record_ContentMetadataAndCodecContent_RoundTrip()
    {
        var text = new TextContent("annotated")
        {
            RawRepresentation = new CustomRaw("content-raw"),
            AdditionalProperties = new AdditionalPropertiesDictionary { ["trace"] = "safe" },
            Annotations =
            [
                new CitationAnnotation
                {
                    Title = "Source",
                    Url = new Uri("https://docs.example.test/source"),
                    Snippet = "quoted",
                    AnnotatedRegions = [new TextSpanAnnotatedRegion { StartIndex = 0, EndIndex = 4 }],
                },
            ],
        };
        var custom = new CustomContent("custom")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { ["trace"] = "custom-safe" },
        };
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        update.Contents.Add(text);
        update.Contents.Add(custom);
        var options = new ChatRecordingOptions { Mode = ChatRecordingMode.Record };
        options.AllowedContentAdditionalProperties.Add("trace");
        options.ContentCodecs.Add(new CustomContentCodec());
        options.RawCodecs.Add(new CustomRawCodec());
        var tape = await RecordAsync([update], options: options);
        var replayOptions = new ChatRecordingOptions { Recording = tape };
        replayOptions.AllowedContentAdditionalProperties.Add("trace");
        replayOptions.ContentCodecs.Add(new CustomContentCodec());
        replayOptions.RawCodecs.Add(new CustomRawCodec());
        using var replay = new ReplayChatClient(replayOptions);
        var contents = (await replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync()).Single().Contents;
        var replayedText = Assert.IsType<TextContent>(contents[0]);
        Assert.Equal("safe", replayedText.AdditionalProperties!["trace"]);
        Assert.Equal("content-raw", Assert.IsType<CustomRaw>(replayedText.RawRepresentation).Value);
        var citation = Assert.IsType<CitationAnnotation>(replayedText.Annotations!.Single());
        Assert.Equal("https://docs.example.test/source", citation.Url!.AbsoluteUri);
        Assert.Equal(4, Assert.IsType<TextSpanAnnotatedRegion>(citation.AnnotatedRegions!.Single()).EndIndex);
        Assert.Equal("custom-safe", Assert.IsType<CustomContent>(contents[1]).AdditionalProperties!["trace"]);
    }

    [Fact]
    public async Task Record_UnallowlistedContentMetadata_StrictModeRejects()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        update.Contents.Add(new TextContent("x") { AdditionalProperties = new AdditionalPropertiesDictionary { ["unknown"] = "value" } });
        using var recorder = new RecordingChatClient(new StubClient([update]), new ChatRecordingOptions { Mode = ChatRecordingMode.Record });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync());
    }

    [Fact]
    public async Task Record_UnknownAnnotation_StrictModeRejectsRatherThanDroppingIt()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        update.Contents.Add(new TextContent("x") { Annotations = [new CustomAnnotation()] });
        using var recorder = new RecordingChatClient(new StubClient([update]), new ChatRecordingOptions { Mode = ChatRecordingMode.Record });
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync());
    }

    private static async Task<ChatRecording> RecordAsync(
        IReadOnlyList<ChatResponseUpdate> updates,
        IReadOnlyList<ChatMessage>? messages = null,
        ChatRecordingOptions? options = null)
    {
        var tape = options?.Recording ?? new ChatRecording();
        options ??= new ChatRecordingOptions { Mode = ChatRecordingMode.Record };
        options.Mode = ChatRecordingMode.Record;
        options.Recording = tape;
        using var recorder = new RecordingChatClient(new StubClient(updates), options);
        await recorder.GetStreamingResponseAsync(messages ?? [new ChatMessage(ChatRole.User, "x")]).ToListAsync();
        return tape;
    }

    private static async Task<ChatRecording> RecordAsync(
        IReadOnlyList<ChatResponseUpdate> updates,
        ChatOptions chatOptions,
        ChatRecordingOptions? recordingOptions = null)
    {
        var tape = recordingOptions?.Recording ?? new ChatRecording();
        recordingOptions ??= new ChatRecordingOptions();
        recordingOptions.Mode = ChatRecordingMode.Record;
        recordingOptions.Recording = tape;
        using var recorder = new RecordingChatClient(new StubClient(updates), recordingOptions);
        await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")], chatOptions).ToListAsync();
        return tape;
    }

    private static async Task<ChatRecording> RecordAsync(IChatClient client)
    {
        var tape = new ChatRecording();
        using var recorder = new RecordingChatClient(client, new ChatRecordingOptions { Mode = ChatRecordingMode.Record, Recording = tape });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")]).ToListAsync());
        return tape;
    }

    private static JsonObject BlobData(string path) => new()
    {
        ["type"] = "data",
        ["mediaType"] = "application/octet-stream",
        ["blob"] = new JsonObject { ["path"] = path, ["sha256"] = "0", ["length"] = 0 },
    };

    private static string CreateFixtureDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ".recording-fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestInputRequest(string requestId) : InputRequestContent(requestId);
    private sealed class TestInputResponse(string requestId) : InputResponseContent(requestId);
    private sealed class CustomContent(string value) : AIContent { public string Value { get; } = value; }
    private sealed class CustomAnnotation : AIAnnotation { }
    private sealed record CustomRaw(string Value);

    private sealed class CustomContentCodec : IChatRecordingContentCodec
    {
        public bool CanWrite(AIContent content) => content is CustomContent;
        public JsonObject Write(AIContent content) => new() { ["type"] = "custom", ["value"] = ((CustomContent)content).Value };
        public bool CanRead(string type) => type == "custom";
        public AIContent Read(JsonObject value) => new CustomContent(value["value"]!.GetValue<string>());
    }

    private sealed class CustomRawCodec : IChatRecordingRawCodec
    {
        public bool CanWrite(object value) => value is CustomRaw;
        public JsonNode? Write(object value) => JsonValue.Create(((CustomRaw)value).Value);
        public bool CanRead(string type) => type == typeof(CustomRaw).FullName;
        public object? Read(string type, JsonNode? value) => new CustomRaw(value!.GetValue<string>());
    }

    private sealed class TestRequestCodec : IChatRecordingRequestCodec
    {
        public bool CanWrite(ChatOptions options) => options.RawRepresentationFactory is not null;
        public JsonObject Write(ChatOptions options) => new() { ["type"] = "test-provider", ["version"] = 1 };
        public bool CanRead(JsonObject extension) => extension["type"]?.GetValue<string>() == "test-provider";
        public JsonObject Read(JsonObject extension) => new() { ["type"] = "test-provider", ["version"] = extension["version"]?.GetValue<int>() ?? 0 };
    }

    private sealed class OneShotMessages(IReadOnlyList<ChatMessage> messages) : IEnumerable<ChatMessage>
    {
        private bool _enumerated;

        public IEnumerator<ChatMessage> GetEnumerator()
        {
            if (_enumerated) throw new InvalidOperationException("The source was enumerated twice.");
            _enumerated = true;
            return messages.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CapturingClient : IChatClient
    {
        public IReadOnlyList<ChatMessage>? Messages { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse());
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Messages = messages.ToArray();
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class ThrowingClient(Exception exception) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => Task.FromException<ChatResponse>(exception);
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (exception is null)
                yield return new ChatResponseUpdate(ChatRole.Assistant, "unreachable");
            throw exception ?? new InvalidOperationException("Missing test exception.");
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class MutatingClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse());
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var mutable = Assert.IsType<List<ChatMessage>>(messages);
            mutable[1].Contents.Clear();
            mutable[1].Contents.Add(new TextContent("mutated"));
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class StubClient(IReadOnlyList<ChatResponseUpdate> updates) : IChatClient
    {
        public object? Service { get; set; }
        public bool Disposed { get; private set; }
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            await updates.ToAsyncEnumerable().ToChatResponseAsync(cancellationToken);
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in updates) { cancellationToken.ThrowIfCancellationRequested(); yield return update; await Task.Yield(); }
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => Service;
        public void Dispose() => Disposed = true;
    }
}
