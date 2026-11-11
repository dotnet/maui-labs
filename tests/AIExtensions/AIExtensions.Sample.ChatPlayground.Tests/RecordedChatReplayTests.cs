using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

#pragma warning disable MEAI001 // Image generation tool content is experimental in the installed SDK.
public sealed class RecordedChatReplayTests
{
    [Theory]
    [InlineData("no-tools.json", 0, false)]
    [InlineData("one-tool.json", 1, true)]
    [InlineData("multiple-tools.json", 2, true)]
    [InlineData("image-input.json", 0, true)]
    [InlineData("reasoning.json", 0, false)]
    [InlineData("generated-image.json", 0, false)]
    [InlineData("structured.json", 0, false)]
    [InlineData("structured-streaming.json", 0, true)]
    [InlineData("multi-turn-tools.json", 2, true)]
    [InlineData("multi-turn-image-in-out.json", 0, true)]
    [InlineData("multi-turn-structured.json", 0, true)]
    public async Task SavedLiveChat_ReplaysEveryMessageAndUpdateWithoutCallingModel(
        string fileName, int expectedToolCalls, bool hasStreamingInteraction)
    {
        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        await using (var source = File.OpenRead(FixturePath(fileName)))
            await recording.LoadFileAsync(source);

        var saved = ChatRecordingSerializer.Deserialize(File.ReadAllText(FixturePath(fileName)));
        Assert.NotEmpty(saved.Interactions);
        Assert.Equal(hasStreamingInteraction, saved.Interactions.Any(interaction => interaction.IsStreaming));
        var toolCalls = 0;
        var replay = new ReplayChatClient(recording);

        foreach (var interaction in saved.Interactions)
        {
            var messages = ChatRecordingSerializer.ReadRequestMessages(interaction.Request);
            var options = ChatRecordingSerializer.ReadOptions(interaction.Request);
            if (interaction.IsStreaming)
            {
                var actual = new List<JsonObject>();
                await foreach (var update in replay.GetStreamingResponseAsync(messages, options))
                {
                    actual.Add(ChatRecordingSerializer.Update(update));
                    toolCalls += update.Contents.OfType<FunctionCallContent>().Count();
                }

                Assert.Equal(interaction.Updates.Count, actual.Count);
                for (var index = 0; index < actual.Count; index++)
                    Assert.True(JsonNode.DeepEquals(interaction.Updates[index], actual[index]));
            }
            else
            {
                var response = await replay.GetResponseAsync(messages, options);
                Assert.True(JsonNode.DeepEquals(interaction.Response, ChatRecordingSerializer.Response(response)));
                toolCalls += response.Messages.SelectMany(message => message.Contents)
                    .OfType<FunctionCallContent>().Count();
            }
        }

        Assert.Equal(expectedToolCalls, toolCalls);
        if (fileName == "image-input.json")
        {
            var sampleImage = File.ReadAllBytes(FixturePath("playground_sample.png"));
            Assert.Contains(saved.Interactions.SelectMany(interaction => ChatRecordingSerializer.ReadRequestMessages(interaction.Request))
                .SelectMany(message => message.Contents), content =>
                    content is DataContent data && data.Data.ToArray().SequenceEqual(sampleImage));
        }
        if (fileName == "reasoning.json")
        {
            var response = Assert.Single(saved.Interactions).Response!;
            var summary = Assert.Single(ChatRecordingSerializer.ReadResponse(response).Messages
                .SelectMany(message => message.Contents).OfType<TextReasoningContent>());
            Assert.False(string.IsNullOrWhiteSpace(summary.Text));
            Assert.Null(summary.ProtectedData);
        }
        if (fileName == "generated-image.json")
        {
            var response = Assert.Single(saved.Interactions).Response!;
            var image = Assert.Single(ChatRecordingSerializer.ReadResponse(response).Messages
                .SelectMany(message => message.Contents).OfType<DataContent>());
            Assert.StartsWith("image/", image.MediaType);
            Assert.True(image.Data.Length > 1_000, "Expected actual generated image bytes, not a placeholder.");
        }
        if (fileName.StartsWith("structured", StringComparison.Ordinal))
        {
            var interaction = Assert.Single(saved.Interactions);
            Assert.True(ChatRecordingSerializer.IsStructuredJson(interaction.Request));
            var contents = interaction.IsStreaming
                ? interaction.Updates.SelectMany(update => ChatRecordingSerializer.ReadUpdate(update).Contents)
                : ChatRecordingSerializer.ReadResponse(interaction.Response!).Messages.SelectMany(message => message.Contents);
            var json = string.Concat(contents.OfType<TextContent>().Select(content => content.Text));
            using var document = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(new[] { "category", "keyPoints", "sentiment", "summary" },
                document.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        }

        Assert.Equal(saved.Interactions.Count, recording.ReplayPosition);
        Assert.False(recording.HasReplayRemaining);
        recording.RestartReplay();
        Assert.Equal(0, recording.ReplayPosition);
        Assert.True(recording.HasReplayRemaining);
    }

    [Fact]
    public void MultiTurnTools_RecordsBothTransportModesAndPriorFunctionResults()
    {
        var saved = ChatRecordingSerializer.Deserialize(File.ReadAllText(FixturePath("multi-turn-tools.json")));
        Assert.Equal(2, saved.Interactions.Count);
        var first = saved.Interactions[0];
        var second = saved.Interactions[1];
        Assert.True(first.IsStreaming);
        Assert.False(second.IsStreaming);

        var firstContents = first.Updates.SelectMany(update => ChatRecordingSerializer.ReadUpdate(update).Contents);
        var calculatorCall = Assert.Single(firstContents.OfType<FunctionCallContent>());
        var calculatorResult = Assert.Single(firstContents.OfType<FunctionResultContent>());
        Assert.Equal("calculate", calculatorCall.Name);
        Assert.Equal(calculatorCall.CallId, calculatorResult.CallId);

        var priorContents = ChatRecordingSerializer.ReadRequestMessages(second.Request)
            .SelectMany(message => message.Contents).ToArray();
        Assert.Contains(priorContents.OfType<FunctionCallContent>(),
            call => call.CallId == calculatorCall.CallId);
        Assert.Contains(priorContents.OfType<FunctionResultContent>(),
            result => result.CallId == calculatorCall.CallId);
        var nextContents = ChatRecordingSerializer.ReadResponse(second.Response!).Messages
            .SelectMany(message => message.Contents).ToArray();
        var dateCall = Assert.Single(nextContents.OfType<FunctionCallContent>());
        Assert.Equal("get_current_local_datetime", dateCall.Name);
        Assert.Equal(dateCall.CallId, Assert.Single(nextContents.OfType<FunctionResultContent>()).CallId);
    }

    [Fact]
    public async Task MultiTurnTools_ReconstructedHistoryMatchesNextRecordedRequest()
    {
        using var harness = new PlaygroundChatHarness();
        await harness.LoadAsync("multi-turn-tools.json");
        var saved = ChatRecordingSerializer.Deserialize(File.ReadAllText(FixturePath("multi-turn-tools.json")));

        await harness.Driver.ReplayAsync(playAll: false);
        await harness.Driver.ReplayAsync(playAll: false);

        Assert.Equal(2, harness.Recording.ReplayPosition);
        Assert.Equal(2, harness.Session.Turns.Count);

        // The engine sent the second turn with the first turn's call and result, as the recorded request did.
        var recorded = ChatRecordingSerializer.ReadRequestMessages(saved.Interactions[1].Request);
        var reconstructed = harness.ReplayRequests.Requests[1];
        Assert.Equal(
            recorded.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Select(call => (call.CallId, call.Name)),
            reconstructed.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Select(call => (call.CallId, call.Name)));
        Assert.Equal(
            recorded.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Select(result => result.CallId),
            reconstructed.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Select(result => result.CallId));
        Assert.Equal(Texts(recorded, ChatRole.User), Texts(reconstructed, ChatRole.User));
        Assert.Equal(Texts(recorded, ChatRole.Assistant), Texts(reconstructed, ChatRole.Assistant));

        static string[] Texts(IEnumerable<ChatMessage> messages, ChatRole role) =>
            [.. messages.Where(message => message.Role == role && message.Text.Length > 0).Select(message => message.Text)];
    }

    [Fact]
    public void MultiTurnImageGeneration_PreservesRealInputAndOutputAcrossTurns()
    {
        var saved = ChatRecordingSerializer.Deserialize(File.ReadAllText(FixturePath("multi-turn-image-in-out.json")));
        Assert.Equal(2, saved.Interactions.Count);
        var first = saved.Interactions[0];
        var second = saved.Interactions[1];
        Assert.False(first.IsStreaming);
        Assert.True(second.IsStreaming);

        var packagedImage = File.ReadAllBytes(FixturePath("playground_sample.png"));
        var input = Assert.Single(ChatRecordingSerializer.ReadRequestMessages(first.Request)
            .SelectMany(message => message.Contents).OfType<DataContent>());
        Assert.Equal(packagedImage, input.Data.ToArray());

        var firstContents = ChatRecordingSerializer.ReadResponse(first.Response!).Messages
            .SelectMany(message => message.Contents).ToArray();
        var imageCall = Assert.Single(firstContents.OfType<ImageGenerationToolCallContent>());
        var imageResult = Assert.Single(firstContents.OfType<ImageGenerationToolResultContent>());
        Assert.Equal(imageCall.CallId, imageResult.CallId);
        var generated = Assert.Single(imageResult.Outputs!.OfType<DataContent>());
        var generatedBytes = generated.Data.ToArray();
        Assert.StartsWith("image/", generated.MediaType);
        Assert.True(generatedBytes.Length > 1_000, "Expected a real image generated by the configured model.");
        Assert.False(generatedBytes.SequenceEqual(packagedImage), "The output must differ from the attached input.");

        var nextContents = ChatRecordingSerializer.ReadRequestMessages(second.Request)
            .SelectMany(message => message.Contents).ToArray();
        Assert.Contains(nextContents.OfType<ImageGenerationToolResultContent>(), result =>
            result.Outputs?.OfType<DataContent>().Any(image => image.Data.ToArray().SequenceEqual(generatedBytes)) == true);
        Assert.Contains(nextContents.OfType<DataContent>(),
            image => image.Data.ToArray().SequenceEqual(packagedImage));
        Assert.All(nextContents.OfType<TextReasoningContent>(), reasoning => Assert.Null(reasoning.ProtectedData));
        Assert.NotEmpty(second.Updates.SelectMany(update => ChatRecordingSerializer.ReadUpdate(update).Contents)
            .OfType<TextContent>());
    }

    [Fact]
    public async Task MultiTurnStructured_ReplaysFormatAndInstructionChangesWithoutResettingTranscript()
    {
        var fileName = "multi-turn-structured.json";
        var saved = ChatRecordingSerializer.Deserialize(File.ReadAllText(FixturePath(fileName)));
        Assert.Equal(new[] { true, false, true },
            saved.Interactions.Select(interaction => interaction.IsStreaming));
        Assert.Equal(new[] { true, true, false },
            saved.Interactions.Select(interaction => ChatRecordingSerializer.IsStructuredJson(interaction.Request)));
        Assert.Equal(new string?[]
        {
            "Keep the response concise and practical.",
            "Include offline replay and avoid publishing sensitive recordings.",
            null,
        }, saved.Interactions.Select(interaction => ChatRecordingSerializer.ReadInstructions(interaction.Request)));

        foreach (var interaction in saved.Interactions.Take(2))
        {
            var contents = interaction.IsStreaming
                ? interaction.Updates.SelectMany(update => ChatRecordingSerializer.ReadUpdate(update).Contents)
                : ChatRecordingSerializer.ReadResponse(interaction.Response!).Messages.SelectMany(message => message.Contents);
            var json = string.Concat(contents.OfType<TextContent>().Select(content => content.Text));
            using var document = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(new[] { "category", "keyPoints", "sentiment", "summary" },
                document.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        }

        using var harness = new PlaygroundChatHarness();
        await harness.LoadAsync(fileName);
        var instructions = new List<string?>();
        harness.Driver.InteractionStarting += (_, interaction) =>
            instructions.Add(ChatRecordingSerializer.ReadInstructions(interaction.Request));
        var turnsAdded = 0;
        harness.Session.RegisterOnTurnAdded(_ => turnsAdded++);

        var result = await harness.Driver.ReplayAsync(playAll: true);

        // The instructions shown above the messages follow each interaction, and the turns accumulate in one
        // conversation instead of being reset.
        Assert.Equal(saved.Interactions.Select(interaction => ChatRecordingSerializer.ReadInstructions(interaction.Request)),
            instructions);
        Assert.Equal(3, result.Turns);
        Assert.Equal(3, turnsAdded);
        var template = new StructuredResponseTemplate();
        Assert.Equal([true, true, false], harness.Session.Turns.Select(turn =>
            template.When(Assert.Single(turn.ResponseBlocks.OfType<TextContentBlock>()))));
        Assert.Equal(3, harness.Recording.ReplayPosition);
    }

    [Fact]
    public async Task GeneratedImageToolResult_SavesAndReplaysInlineImageInBothResponseModes()
    {
        var image = File.ReadAllBytes(FixturePath("playground_sample.png"));
        var result = new ImageGenerationToolResultContent("image-call")
        {
            Outputs = [new DataContent(image, "image/png")],
        };
        var response = new ChatResponse(
        [
            new ChatMessage(ChatRole.Assistant, [new ImageGenerationToolCallContent("image-call")]),
            new ChatMessage(ChatRole.Tool, [result]),
        ]);
        var update = new ChatResponseUpdate(ChatRole.Tool, [result]);
        var request = ChatRecordingSerializer.Request(
            [new ChatMessage(ChatRole.User, "Generate an image")],
            new ChatOptions { Tools = [new HostedImageGenerationTool()] });

        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        recording.AddResponse(request, response);
        var streaming = recording.BeginStreaming(request);
        recording.AddUpdate(streaming, ChatRecordingSerializer.Update(update));
        recording.CompleteStreaming(streaming);

        var serialized = File.ReadAllText(recording.AutosavePath);
        var saved = ChatRecordingSerializer.Deserialize(serialized);
        Assert.Equal(2, saved.Interactions.Count);
        using (var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(serialized)))
            await recording.LoadFileAsync(input);
        using var replay = new ReplayChatClient(recording);
        var replayedUpdates = new List<ChatResponseUpdate>();
        await foreach (var replayedUpdate in replay.GetStreamingResponseAsync([]))
            replayedUpdates.Add(replayedUpdate);
        Assert.Equal(image, Assert.Single(Assert.Single(replayedUpdates
            .SelectMany(item => item.Contents).OfType<ImageGenerationToolResultContent>())
            .Outputs!.OfType<DataContent>()).Data.ToArray());
        Assert.Equal(1, recording.ReplayPosition);

        var replayedResponse = await replay.GetResponseAsync([]);
        Assert.Equal(image, Assert.Single(Assert.Single(replayedResponse.Messages
            .SelectMany(message => message.Contents).OfType<ImageGenerationToolResultContent>())
            .Outputs!.OfType<DataContent>()).Data.ToArray());
        Assert.Equal(2, recording.ReplayPosition);
    }
    #pragma warning restore MEAI001

    [Fact]
    public async Task Replay_DifferentMessage_ReturnsSavedResponse()
    {
        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        await using (var source = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(source);

        var saved = ChatRecordingSerializer.Deserialize(File.ReadAllText(FixturePath("no-tools.json")));
        using var replay = new ReplayChatClient(recording);
        var response = await replay.GetResponseAsync([new ChatMessage(ChatRole.User, "not the recorded prompt")]);

        Assert.True(JsonNode.DeepEquals(
            saved.Interactions[0].Response, ChatRecordingSerializer.Response(response)));
        Assert.Equal(1, recording.ReplayPosition);
    }

    [Fact]
    public async Task Replay_IncompleteStream_DoesNotAdvance()
    {
        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        await using (var source = File.OpenRead(FixturePath("one-tool.json")))
            await recording.LoadFileAsync(source);

        var saved = ChatRecordingSerializer.Deserialize(File.ReadAllText(FixturePath("one-tool.json")));
        var interaction = Assert.Single(saved.Interactions);
        var replay = new ReplayChatClient(recording);
        await using (var enumerator = replay.GetStreamingResponseAsync(
            ChatRecordingSerializer.ReadRequestMessages(interaction.Request),
            ChatRecordingSerializer.ReadOptions(interaction.Request)).GetAsyncEnumerator())
        {
            Assert.True(await enumerator.MoveNextAsync());
        }

        Assert.Equal(0, recording.ReplayPosition);
    }

    [Fact]
    public async Task Replay_ConvertsBothResponseModesAndIgnoresCallerMessages()
    {
        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        await using (var source = File.OpenRead(FixturePath("multi-turn-tools.json")))
            await recording.LoadFileAsync(source);

        using var replay = new ReplayChatClient(recording);
        var wrongMessages = new[] { new ChatMessage(ChatRole.User, "Not the recorded conversation") };
        var firstResponse = await replay.GetResponseAsync(wrongMessages);
        var firstContents = firstResponse.Messages.SelectMany(message => message.Contents).ToArray();
        var calculatorCall = Assert.Single(firstContents.OfType<FunctionCallContent>());
        Assert.Equal("calculate", calculatorCall.Name);
        Assert.Equal(calculatorCall.CallId, Assert.Single(firstContents.OfType<FunctionResultContent>()).CallId);
        Assert.Equal(1, recording.ReplayPosition);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in replay.GetStreamingResponseAsync(wrongMessages))
            updates.Add(update);
        var secondContents = updates.SelectMany(update => update.Contents).ToArray();
        var dateCall = Assert.Single(secondContents.OfType<FunctionCallContent>());
        Assert.Equal("get_current_local_datetime", dateCall.Name);
        Assert.Equal(dateCall.CallId, Assert.Single(secondContents.OfType<FunctionResultContent>()).CallId);
        Assert.Equal(2, recording.ReplayPosition);
    }

    [Fact]
    public async Task Replay_IncompleteConvertedStream_DoesNotAdvance()
    {
        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        await using (var source = File.OpenRead(FixturePath("multi-turn-tools.json")))
            await recording.LoadFileAsync(source);

        using var replay = new ReplayChatClient(recording);
        _ = await replay.GetResponseAsync([]);
        Assert.Equal(1, recording.ReplayPosition);

        await using (var enumerator = replay.GetStreamingResponseAsync([]).GetAsyncEnumerator())
            Assert.True(await enumerator.MoveNextAsync());

        Assert.Equal(1, recording.ReplayPosition);
    }

    [Theory]
    [InlineData("no-tools.json")]
    [InlineData("one-tool.json")]
    [InlineData("multiple-tools.json")]
    [InlineData("image-input.json")]
    [InlineData("reasoning.json")]
    [InlineData("generated-image.json")]
    [InlineData("structured.json")]
    [InlineData("structured-streaming.json")]
    [InlineData("multi-turn-tools.json")]
    [InlineData("multi-turn-image-in-out.json")]
    [InlineData("multi-turn-structured.json")]
    [InlineData("approval-reject-then-approve.json")]
    public async Task Replay_RendersCompleteConversationsThroughTheLiveChatPath(string fileName)
    {
        // The live client is selected but never called; a text-only one, so a chat with images cannot continue.
        using var model = new ScriptedChatClient(ScriptedResponse.Text("Continued live."));
        using var harness = new PlaygroundChatHarness(
            model, PlaygroundChatHarness.LiveDescriptor with { SupportsImageInput = false });
        await harness.LoadAsync(fileName);
        var saved = ChatRecordingSerializer.Deserialize(File.ReadAllText(FixturePath(fileName)));
        var userMessages = saved.Interactions
            .Select(interaction => ChatRecordingSerializer.ReadRequestMessages(interaction.Request)[^1])
            .Where(ChatReplayDriver.IsUserTurn)
            .ToArray();

        var result = await harness.Driver.ReplayAsync(playAll: true);

        Assert.Empty(model.Requests);
        Assert.Equal(saved.Interactions.Count, harness.Recording.ReplayPosition);
        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        Assert.Empty(result.UnansweredApprovals);
        Assert.Equal(0, result.RestoredInteractions);
        Assert.Equal(userMessages.Length, result.Turns);
        Assert.Equal(ExpectedResponses[fileName].Length, harness.Session.Turns.Count);
        for (var index = 0; index < harness.Session.Turns.Count; index++)
        {
            var turn = harness.Session.Turns[index];
            var user = userMessages[index];
            Assert.False(turn.IsStopped);
            Assert.Equal(user.Text, ChatReplayDriverTests.UserText(turn));
            Assert.Equal(
                user.Contents.OfType<DataContent>().Select(data => data.Data.ToArray()),
                turn.RequestBlocks.OfType<MediaContentBlock>().Select(block => Assert.IsType<DataContent>(block.Content).Data.ToArray()));
            Assert.Equal(ExpectedResponses[fileName][index], turn.ResponseBlocks.Select(Describe));
            Assert.All(turn.RequestBlocks.Concat(turn.ResponseBlocks),
                block => Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState));
        }

        // A live chat continues the replayed conversation, unless it has images the selected client cannot send.
        await harness.Session.SendMessageAsync("One more question");
        if (fileName is "image-input.json" or "generated-image.json" or "multi-turn-image-in-out.json")
        {
            Assert.Equal(ConversationStatus.Error, harness.Session.Status);
            Assert.IsType<NotSupportedException>(harness.Session.Error);
            Assert.Empty(model.Requests);
        }
        else
        {
            Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
            Assert.Equal(
                userMessages.Select(message => message.Text).Append("One more question"),
                Assert.Single(model.Requests).Messages.Where(ChatReplayDriver.IsUserTurn).Select(message => message.Text));
        }
    }

    private static readonly Dictionary<string, string[][]> ExpectedResponses = new()
    {
        ["no-tools.json"] = [["text"]],
        ["one-tool.json"] = [["calculate = 42", "text"]],
        ["multiple-tools.json"] = [["calculate = 42", "calculate = 42", "text"]],
        ["image-input.json"] = [["text"]],
        ["reasoning.json"] = [["reasoning", "text"]],
        ["generated-image.json"] = [["image"]],
        ["structured.json"] = [["structured"]],
        ["structured-streaming.json"] = [["reasoning", "structured"]],
        ["multi-turn-tools.json"] = [["calculate = 102", "text"], ["get_current_local_datetime", "text"]],
        ["multi-turn-image-in-out.json"] = [["image", "text"], ["reasoning", "text"]],
        ["multi-turn-structured.json"] = [["structured"], ["structured"], ["text"]],
        ["approval-reject-then-approve.json"] = [["approval calculate Rejected", "reasoning", "approval calculate Approved", "text"]],
    };

    private static string Describe(ContentBlock block)
    {
        switch (block)
        {
            case TextContentBlock text:
                Assert.False(string.IsNullOrWhiteSpace(text.RawText));
                return new StructuredResponseTemplate().When(text) ? "structured" : "text";
            case ReasoningContentBlock reasoning:
                Assert.False(string.IsNullOrWhiteSpace(reasoning.Text));
                return "reasoning";
            case FunctionInvocationContentBlock tool:
                Assert.True(tool.HasResult);
                return tool.ToolName == "calculate"
                    ? $"calculate = {CalculatorDisplay.FormatResult(tool.Result, CultureInfo.InvariantCulture)}"
                    : tool.ToolName!;
            case ToolApprovalBlock approval:
                Assert.True(approval.HasResult);
                return $"approval {approval.ToolName} {approval.Status}";
            case MediaContentBlock { Content: DataContent image } when image.MediaType.StartsWith("image/", StringComparison.Ordinal):
                Assert.True(image.Data.Length > 1_000, "Expected real image bytes, not a placeholder.");
                return "image";
            default:
                return block.GetType().Name;
        }
    }

    [Fact]
    public void DescribedReplay_ExposesDescriptorAndReplayIdentityWithoutRecording()
    {
        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        using var client = new DescribedChatClient(
            new ReplayChatClient(recording),
            new ChatClientDescriptor("replay", "Replay", "Offline", IsReplay: true));

        var descriptor = Assert.IsType<ChatClientDescriptor>(client.GetService<ChatClientDescriptor>());
        Assert.True(descriptor.IsReplay);
        Assert.Equal("Replay", descriptor.DisplayName);
        Assert.Null(client.GetService<RecordingChatClient>());
    }

    [Fact]
    public async Task RecordingClient_RecordsBothResponseModesAndExposesDescriptor()
    {
        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        using var client = new DescribedChatClient(
            new RecordingChatClient(new EchoChatClient("echo-model"), recording),
            new ChatClientDescriptor("echo", "Echo", "Ready"));

        var messages = new[] { new ChatMessage(ChatRole.User, "Hello") };
        var response = await client.GetResponseAsync(messages);
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(messages))
            updates.Add(update);

        var descriptor = Assert.IsType<ChatClientDescriptor>(client.GetService<ChatClientDescriptor>());
        Assert.Equal("Echo", descriptor.DisplayName);
        Assert.False(descriptor.IsReplay);
        Assert.Equal(2, recording.InteractionCount);
        var saved = ChatRecordingSerializer.Deserialize(File.ReadAllText(recording.AutosavePath));
        Assert.True(JsonNode.DeepEquals(saved.Interactions[0].Response, ChatRecordingSerializer.Response(response)));
        Assert.True(JsonNode.DeepEquals(saved.Interactions[1].Updates[0], ChatRecordingSerializer.Update(Assert.Single(updates))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordedModelId_LabelsTheReplayedTurnButNotItsEarlierHistory(bool recordedStreaming)
    {
        const string modelId = "recorded-model";
        using var harness = new PlaygroundChatHarness(new EchoChatClient(modelId), record: true);
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, "Be helpful"),
            new ChatMessage(ChatRole.User, "Earlier question"),
            new ChatMessage(ChatRole.Assistant, "Earlier response"),
            new ChatMessage(ChatRole.User, "Hello"),
        };
        var options = new ChatOptions { ModelId = "requested-model" };
        if (recordedStreaming)
        {
            await foreach (var _ in harness.Live!.GetStreamingResponseAsync(messages, options)) { }
        }
        else
        {
            await harness.Live!.GetResponseAsync(messages, options);
        }

        var saved = ChatRecordingSerializer.Deserialize(File.ReadAllText(harness.Recording.AutosavePath));
        var interaction = Assert.Single(saved.Interactions);
        Assert.Equal(modelId, recordedStreaming
            ? ChatRecordingSerializer.ReadUpdate(Assert.Single(interaction.Updates)).ModelId
            : ChatRecordingSerializer.ReadResponse(interaction.Response!).ModelId);

        var result = await harness.Driver.ReplayAsync(playAll: true);

        // The recording starts part-way through a conversation: the earlier messages are shown without a model, the
        // system message is not shown, and the replayed answer carries the recorded model.
        Assert.Equal(1, result.Turns);
        Assert.Equal(1, harness.Recording.ReplayPosition);
        Assert.Collection(harness.Session.Turns,
            earlier =>
            {
                Assert.Null(earlier.ModelId);
                Assert.Equal("Earlier question", ChatReplayDriverTests.UserText(earlier));
                Assert.Equal("Earlier response", Assert.IsType<TextContentBlock>(Assert.Single(earlier.ResponseBlocks)).RawText);
            },
            replayed =>
            {
                Assert.Equal(modelId, replayed.ModelId);
                Assert.Equal("Hello", ChatReplayDriverTests.UserText(replayed));
                Assert.Equal("Hello back", Assert.IsType<TextContentBlock>(Assert.Single(replayed.ResponseBlocks)).RawText);
            });
        Assert.DoesNotContain(harness.Session.Turns.SelectMany(turn => turn.RequestBlocks.Concat(turn.ResponseBlocks)),
            block => block is TextContentBlock { RawText: "Be helpful" });
    }

    [Fact]
    public async Task RestoredRequestHistory_KeepsMessageBoundariesWithoutModelLabels()
    {
        using var harness = new PlaygroundChatHarness();
        harness.Recording.AddResponse(
            ChatRecordingSerializer.Request(
            [
                new ChatMessage(ChatRole.Assistant, "First") { MessageId = "first" },
                new ChatMessage(ChatRole.Assistant,
                    [new TextReasoningContent("Checking"), new FunctionCallContent("call", "calculate")]),
                new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call", 4)]),
                new ChatMessage(ChatRole.Assistant, "Second") { MessageId = "second" },
                new ChatMessage(ChatRole.User, "Continue"),
            ],
            new ChatOptions { Instructions = "Be concise" }),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Continued.")) { ModelId = "recorded-model" });
        string? instructions = null;
        harness.Driver.InteractionStarting += (_, interaction) =>
            instructions = ChatRecordingSerializer.ReadInstructions(interaction.Request);

        await harness.Driver.ReplayAsync(playAll: true);

        Assert.Equal("Be concise", instructions);
        Assert.Collection(harness.Session.Turns,
            earlier =>
            {
                // Each message keeps its own block. The order of the reasoning and the call, which share a message,
                // is covered by PlaygroundChatTurnTests.BlocksFromOneMessage_FollowTheContentOrder.
                Assert.Empty(earlier.RequestBlocks);
                Assert.Null(earlier.ModelId);
                Assert.Equal(4, earlier.ResponseBlocks.Count);
                Assert.Equal(["First", "Second"], earlier.ResponseBlocks.OfType<TextContentBlock>().Select(block => block.RawText));
                Assert.Same(earlier.ResponseBlocks[0], earlier.ResponseBlocks.OfType<TextContentBlock>().First());
                Assert.Same(earlier.ResponseBlocks[^1], earlier.ResponseBlocks.OfType<TextContentBlock>().Last());
                Assert.Equal("Checking", Assert.Single(earlier.ResponseBlocks.OfType<ReasoningContentBlock>()).Text);
                Assert.Equal("4", CalculatorDisplay.FormatResult(
                    Assert.Single(earlier.ResponseBlocks.OfType<FunctionInvocationContentBlock>()).Result,
                    CultureInfo.InvariantCulture));
            },
            replayed =>
            {
                Assert.Equal("recorded-model", replayed.ModelId);
                Assert.Equal("Continue", ChatReplayDriverTests.UserText(replayed));
                Assert.Equal("Continued.", Assert.IsType<TextContentBlock>(Assert.Single(replayed.ResponseBlocks)).RawText);
            });
    }

    [Fact]
    public async Task Load_InvalidSchema_LeavesCurrentChatIntact()
    {
        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        await using (var source = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(source);

        var wrongVersion = JsonNode.Parse(File.ReadAllText(FixturePath("no-tools.json")))!.AsObject();
        wrongVersion["schemaVersion"] = 1;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(wrongVersion.ToJsonString()));
        await Assert.ThrowsAsync<InvalidDataException>(() => recording.LoadFileAsync(stream));
        Assert.Equal(1, recording.InteractionCount);
        Assert.Equal(0, recording.ReplayPosition);
    }

    [Fact]
    public async Task NewRecording_RemainsActiveAfterRestart()
    {
        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);
        var currentPath = recording.AutosavePath;
        recording.NewRecording();

        var restored = directory.CreateService();
        Assert.Equal(0, restored.InteractionCount);
        Assert.Equal(currentPath, restored.AutosavePath);
        Assert.Empty(ChatRecordingSerializer.Deserialize(File.ReadAllText(currentPath)).Interactions);
    }

    [Fact]
    public async Task ImportingFile_ReplacesTheCurrentChatAndPersistsAcrossRestart()
    {
        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        await using (var first = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(first);
        Assert.Equal(1, recording.InteractionCount);
        await using (var second = File.OpenRead(FixturePath("multi-turn-tools.json")))
            await recording.LoadFileAsync(second);
        Assert.Equal(2, recording.InteractionCount);

        var restored = directory.CreateService();
        Assert.Equal(2, restored.InteractionCount);
        Assert.Equal(0, restored.ReplayPosition);
        Assert.Equal(2, ChatRecordingSerializer.Deserialize(File.ReadAllText(restored.Export())).Interactions.Count);
    }

    [Fact]
    public async Task LoadedChat_AppendedResponseIsAutosaved()
    {
        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        await using (var source = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(source);

        recording.AddResponse(
            ChatRecordingSerializer.Request([new ChatMessage(ChatRole.User, "One more question")], null),
            new ChatResponse([new ChatMessage(ChatRole.Assistant, "Another answer")]));

        var restored = directory.CreateService();
        Assert.Equal(2, restored.InteractionCount);
        Assert.Equal(2, ChatRecordingSerializer.Deserialize(File.ReadAllText(restored.AutosavePath)).Interactions.Count);
    }

    [Fact]
    public async Task DamagedCurrentChat_ReportsRestoreErrorWithoutOverwritingIt()
    {
        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        await using (var source = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(source);
        var path = recording.AutosavePath;
        File.WriteAllText(path, "{ damaged");
        var damaged = File.ReadAllBytes(path);

        var restored = directory.CreateService();
        Assert.Contains("Could not restore the current chat", restored.RestoreError);
        Assert.Throws<InvalidOperationException>(() => restored.AddResponse(
            ChatRecordingSerializer.Request([new ChatMessage(ChatRole.User, "Hello")], null),
            new ChatResponse([new ChatMessage(ChatRole.Assistant, "Hello")])));
        Assert.Equal(damaged, File.ReadAllBytes(path));
        restored.NewRecording();
        Assert.Null(restored.RestoreError);
        Assert.Empty(ChatRecordingSerializer.Deserialize(File.ReadAllText(path)).Interactions);
    }

    [Fact]
    public async Task CurrentChatWithNullInteraction_CanBeReplacedByAnImportedFile()
    {
        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        await using (var source = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(source);
        var path = recording.AutosavePath;
        var current = JsonNode.Parse(File.ReadAllText(path))!;
        current["interactions"]!.AsArray().Add(null);
        File.WriteAllText(path, current.ToJsonString());
        var damaged = File.ReadAllBytes(path);

        var restored = directory.CreateService();
        Assert.Contains("Could not restore the current chat", restored.RestoreError);
        Assert.Equal(damaged, File.ReadAllBytes(path));
        await using var source2 = File.OpenRead(FixturePath("no-tools.json"));
        await restored.LoadFileAsync(source2);
        Assert.Null(restored.RestoreError);
        Assert.Equal(1, restored.InteractionCount);
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    private sealed class EchoChatClient(string modelId) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Hello back")]) { ModelId = modelId });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Hello back")]) { ModelId = modelId };
            await Task.Yield();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType == typeof(IChatClient) ? this : null;

        public void Dispose() { }
    }

    private sealed class RecordingDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"chat-playground-tests-{Guid.NewGuid():N}");

        public string DataDirectory => Path.Combine(_path, "data");
        public string ExportDirectory => Path.Combine(_path, "cache");

        public ChatSessionService CreateService() =>
            new(NullLogger<ChatSessionService>.Instance, DataDirectory, ExportDirectory);

        public void Dispose()
        {
            if (Directory.Exists(_path))
                Directory.Delete(_path, recursive: true);
        }
    }
}
