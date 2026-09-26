using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.AI.Chat.Tests;

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
        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        await using (var source = File.OpenRead(FixturePath("multi-turn-tools.json")))
            await recording.LoadFileAsync(source);

        var saved = ChatRecordingSerializer.Deserialize(File.ReadAllText(FixturePath("multi-turn-tools.json")));
        var conversation = new ChatConversation();
        using var replay = new ReplayChatClient(recording);
        var first = saved.Interactions[0];
        await foreach (var _ in conversation.ReplayTurnAsync(replay, first.Request,
            ChatRecordingSerializer.ReadOptions(first.Request), first.IsStreaming, structuredJson: false)) { }

        var second = saved.Interactions[1];
        var userMessage = ChatRecordingSerializer.ReadRequestMessages(second.Request).Last();
        var changes = new List<TranscriptChange>();
        await foreach (var change in conversation.SendTurnAsync(replay, userMessage, userMessage.Text,
            ChatRecordingSerializer.ReadOptions(second.Request), second.IsStreaming, structuredJson: false))
            changes.Add(change);

        Assert.Equal(2, recording.ReplayPosition);
        Assert.DoesNotContain(changes, change => change is TranscriptChange.Cleared);
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

        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        await using (var source = File.OpenRead(FixturePath(fileName)))
            await recording.LoadFileAsync(source);
        var conversation = new ChatConversation();
        using var replay = new ReplayChatClient(recording);
        long systemEntryId = 0;
        foreach (var interaction in saved.Interactions)
        {
            var changes = new List<TranscriptChange>();
            await foreach (var change in conversation.ReplayTurnAsync(replay, interaction.Request,
                ChatRecordingSerializer.ReadOptions(interaction.Request), interaction.IsStreaming,
                ChatRecordingSerializer.IsStructuredJson(interaction.Request)))
                changes.Add(change);

            if (interaction.Sequence == 0)
            {
                var system = Assert.Single(changes.OfType<TranscriptChange.EntryAdded>(),
                    change => change.EntryKind == TranscriptEntryKind.System);
                systemEntryId = system.EntryId;
            }
            else if (interaction.Sequence == 1)
            {
                Assert.Contains(changes, change => change is TranscriptChange.EntryTextChanged updated &&
                    updated.EntryId == systemEntryId &&
                    updated.Text == "Include offline replay and avoid publishing sensitive recordings.");
            }
            else
            {
                Assert.Contains(changes, change => change is TranscriptChange.EntryRemoved removed &&
                    removed.EntryId == systemEntryId);
            }
        }

        Assert.Equal(3, recording.ReplayPosition);
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
    public async Task ChatConversation_ReplaysCompleteConversationsThroughOneTransportPath(string fileName)
    {
        using var directory = new RecordingDirectory();
        var recording = directory.CreateService();
        await using (var source = File.OpenRead(FixturePath(fileName)))
            await recording.LoadFileAsync(source);

        var saved = ChatRecordingSerializer.Deserialize(File.ReadAllText(FixturePath(fileName)));
        var processor = new ChatConversation();
        using var replay = new ReplayChatClient(recording);
        foreach (var interaction in saved.Interactions)
        {
            var changes = new List<TranscriptChange>();
            await foreach (var change in processor.ReplayTurnAsync(
                replay,
                interaction.Request,
                ChatRecordingSerializer.ReadOptions(interaction.Request),
                interaction.IsStreaming,
                ChatRecordingSerializer.IsStructuredJson(interaction.Request)))
                changes.Add(change);
            Assert.Equal(interaction.Sequence == 0, changes.OfType<TranscriptChange.Cleared>().Any());
            Assert.Contains(changes, change => change is TranscriptChange.EntryAdded { EntryKind: TranscriptEntryKind.User });
            Assert.NotEmpty(changes);
        }

        Assert.Equal(saved.Interactions.Count, recording.ReplayPosition);
        Assert.Equal(fileName is "image-input.json" or "generated-image.json" or "multi-turn-image-in-out.json",
            processor.HistoryContainsImage);
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
            new RecordingChatClient(new EchoChatClient(), recording),
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

    private sealed class EchoChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Hello back")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Hello back")]);
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
