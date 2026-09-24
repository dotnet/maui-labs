using System.Text.Json.Nodes;
using AIExtensions.Sample.ChatPlayground.Features.Library;
using AIExtensions.Sample.ChatPlayground.Features.Recording;
using AIExtensions.Sample.ChatPlayground.Features.Search;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class ChatSearchServiceTests
{
    private const string AppleId = "apple-test";
    private const string AzureId = "azure-test";

    [Fact]
    public async Task DescribedGenerator_ExposesMetadataBeforeInitializingTheModel()
    {
        var initialized = false;
        using var generator = new DescribedEmbeddingGenerator(
            () =>
            {
                initialized = true;
                return new TestEmbeddings(TextVector);
            },
            new ChatSearchDescriptor(AppleId, "Apple test", "On-device embedding",
                "apple/test", ChatSearchDataLocation.OnDevice));

        Assert.Equal(AppleId, generator.GetService<ChatSearchDescriptor>()!.Id);
        Assert.False(initialized);
        await generator.GenerateAsync(["cobalt"]);
        Assert.True(initialized);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DuplicateProviderIdOrModelIdentity_IsRejected(bool duplicateId)
    {
        using var directory = new SearchDirectory();
        var providers = new[]
        {
            TestProvider("first", "model/first", ChatSearchDataLocation.OnDevice,
                new TestEmbeddings(TextVector)),
            TestProvider(duplicateId ? "first" : "second",
                duplicateId ? "model/second" : "model/first",
                ChatSearchDataLocation.Remote, new TestEmbeddings(TextVector)),
        };

        Assert.Throws<ArgumentException>(() => new ChatSearchService(
            directory.CreateRecording(), NullLogger<ChatSearchService>.Instance,
            directory.DataDirectory, providers));
    }

    [Fact]
    public async Task BrowsingSavedChats_ShowsAssistantExcerptInsteadOfRepeatingTitle()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        using var search = directory.CreateSearch(recording);
        var result = await search.SearchAsync(string.Empty, ChatSearchDescriptor.ContainsId);

        var hit = Assert.Single(result.Hits);
        Assert.StartsWith("Reply with exactly", hit.Title);
        Assert.Equal("Assistant: cobalt", hit.Snippet);
    }

    [Fact]
    public async Task SearchAsync_OnlyIndexesNewTurnText_NotPreviousHistoryOrImageBytes()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("multi-turn-image-in-out.json")))
            await recording.LoadFileAsync(input);

        var generator = new TestEmbeddings(TextVector);
        using var search = directory.CreateSearch(recording, local: () => generator);
        await search.SearchAsync(string.Empty, AppleId);

        Assert.Single(generator.Inputs, text => text.Contains("Use image generation to edit"));
        Assert.DoesNotContain(generator.Inputs, text => text.Contains("data:image", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(generator.Inputs, text => text.Contains("Describing the generated image"));
        Assert.All(generator.Inputs, text => Assert.InRange(text.Length, 1, 360));
    }

    [Fact]
    public async Task ContainsSearch_MatchesAcrossVectorChunkBoundaries_WithoutCreatingEmbeddings()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        recording.AddResponse(
            ChatRecordingSerializer.Request([
                new ChatMessage(ChatRole.System, "System-only text"),
                new ChatMessage(ChatRole.User, new string('x', 353) + " cross boundary"),
            ], null),
            new ChatResponse([
                new ChatMessage(ChatRole.Assistant, "First reply"),
                new ChatMessage(ChatRole.Assistant, "Second reply"),
            ]));

        using var search = directory.CreateSearch(recording,
            local: () => throw new InvalidOperationException("Contains must not initialize embeddings."));
        var userMatch = await search.SearchAsync("cross boundary", ChatSearchDescriptor.ContainsId);
        Assert.Contains("cross boundary", Assert.Single(userMatch.Hits).Snippet);
        Assert.False(userMatch.IsSemantic);
        Assert.Contains("Second reply", Assert.Single(
            (await search.SearchAsync("Second reply", ChatSearchDescriptor.ContainsId)).Hits).Snippet);
        Assert.Empty((await search.SearchAsync("System-only", ChatSearchDescriptor.ContainsId)).Hits);
    }

    [Fact]
    public async Task EveryUserAndAssistantMessage_IsIndexedOnce_WithoutSystemToolsReasoningOrImages()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        var request = new List<ChatMessage>
        {
            new(ChatRole.System, "system secret"),
            new(ChatRole.User, [new TextContent("first user"), new DataContent(new byte[] { 1, 2, 3 }, "image/png")]),
            new(ChatRole.Assistant, "assistant context"),
            new(ChatRole.Tool, "tool secret"),
            new(ChatRole.User, "repeat question"),
        };
        var firstResponse = new ChatResponse([
            new ChatMessage(ChatRole.Assistant, [
                new TextContent("first answer"), new TextReasoningContent("reasoning secret"),
            ]),
            new ChatMessage(ChatRole.Assistant, "second answer"),
        ]);
        recording.AddResponse(ChatRecordingSerializer.Request(request, null), firstResponse);

        var next = recording.BeginStreaming(ChatRecordingSerializer.Request([
            .. request, .. firstResponse.Messages,
            new ChatMessage(ChatRole.User, "repeat question"),
            new ChatMessage(ChatRole.User, "new question"),
        ], null));
        recording.AddUpdate(next, ChatRecordingSerializer.Update(
            new ChatResponseUpdate(ChatRole.Tool, [new TextContent("streamed tool secret")])));
        recording.AddUpdate(next, ChatRecordingSerializer.Update(
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("stream ")])));
        recording.AddUpdate(next, ChatRecordingSerializer.Update(
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("stream reasoning secret")])));
        recording.AddUpdate(next, ChatRecordingSerializer.Update(
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("answer")])));
        recording.CompleteStreaming(next);

        var generator = new TestEmbeddings(TextVector);
        using var search = directory.CreateSearch(recording, local: () => generator);
        await search.SearchAsync(string.Empty, AppleId);

        Assert.Equal(8, generator.Inputs.Count);
        Assert.Equal(2, generator.Inputs.Count(text => text == "repeat question"));
        Assert.Contains("assistant context", generator.Inputs);
        Assert.Contains("first answer", generator.Inputs);
        Assert.Contains("second answer", generator.Inputs);
        Assert.Contains("new question", generator.Inputs);
        Assert.Contains("stream answer", generator.Inputs);
        Assert.DoesNotContain(generator.Inputs, text => text.Contains("secret", StringComparison.Ordinal));
        Assert.DoesNotContain(generator.Inputs, text => text.Contains("reasoning", StringComparison.Ordinal));
        Assert.DoesNotContain(generator.Inputs, text => text.Contains("image", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_FindsSemanticMatchAndReusesPersistedVectors()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        var generator = new TestEmbeddings(TextVector);
        using (var search = directory.CreateSearch(recording, local: () => generator))
        {
            Assert.Empty((await search.SearchAsync("pigment", ChatSearchDescriptor.ContainsId)).Hits);
            var semantic = await search.SearchAsync("pigment", AppleId);
            Assert.True(semantic.IsSemantic);
            Assert.Equal(recording.ActiveChatId, Assert.Single(semantic.Hits).Id);
            Assert.Contains(generator.Inputs, text => text.Contains("cobalt"));
        }

        var restarted = new TestEmbeddings(TextVector);
        using var reloaded = directory.CreateSearch(directory.CreateRecording(), local: () => restarted);
        Assert.Single((await reloaded.SearchAsync("pigment", AppleId)).Hits);
        Assert.Equal(["pigment"], restarted.Inputs);
    }

    [Fact]
    public async Task SelectingAppleOrAzure_IndexesAndQueriesOnlyWithThatBackend()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        var apple = new TestEmbeddings(TextVector);
        var azure = new TestEmbeddings(text =>
            TextVector(text) is [1f, 0f] ? [0f, 1f] : [1f, 0f]);
        using var search = directory.CreateSearch(recording,
            local: () => apple, azure: () => azure);
        await search.SearchAsync("cobalt", ChatSearchDescriptor.ContainsId);
        Assert.Empty(apple.Inputs);
        Assert.Empty(azure.Inputs);

        await search.SearchAsync("pigment", AppleId);
        var appleInputs = apple.Inputs.Count;
        Assert.Contains("cobalt", apple.Inputs);
        Assert.Empty(azure.Inputs);

        await search.SearchAsync("pigment", AzureId);
        Assert.Equal(appleInputs, apple.Inputs.Count);
        Assert.Contains("cobalt", azure.Inputs);
        var azureInputs = azure.Inputs.Count;

        await search.SearchAsync("violet", AppleId);
        Assert.Equal(appleInputs + 1, apple.Inputs.Count);
        Assert.Equal("violet", apple.Inputs.Last());
        Assert.Equal(azureInputs, azure.Inputs.Count);
        Assert.Equal(3, Directory.GetFiles(directory.IndexDirectory,
            recording.ActiveChatId + ".json", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task MultipleLocalAndRemoteGenerators_UseOnlyTheSelectedDescriptor()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        var unusedLocal = new TestEmbeddings(TextVector);
        var selectedLocal = new TestEmbeddings(TextVector);
        var unusedRemote = new TestEmbeddings(TextVector);
        var selectedRemote = new TestEmbeddings(TextVector);
        var providers = new[]
        {
            TestProvider("local-1", "local/model-1", ChatSearchDataLocation.OnDevice, unusedLocal),
            TestProvider("local-2", "local/model-2", ChatSearchDataLocation.OnDevice, selectedLocal),
            TestProvider("remote-1", "remote/model-1", ChatSearchDataLocation.Remote, unusedRemote),
            TestProvider("remote-2", "remote/model-2", ChatSearchDataLocation.Remote, selectedRemote),
        };
        using var search = new ChatSearchService(recording, NullLogger<ChatSearchService>.Instance,
            directory.DataDirectory, providers);
        Assert.Equal(5, search.SearchModes.Count);

        await search.SearchAsync("pigment", "local-2");
        Assert.NotEmpty(selectedLocal.Inputs);
        Assert.Empty(unusedLocal.Inputs);
        Assert.Empty(unusedRemote.Inputs);
        Assert.Empty(selectedRemote.Inputs);

        await search.SearchAsync("pigment", "remote-2");
        Assert.NotEmpty(selectedRemote.Inputs);
        Assert.Empty(unusedLocal.Inputs);
        Assert.Empty(unusedRemote.Inputs);
    }

    [Fact]
    public async Task ReplacingModelBehindSameProviderId_RebuildsEvenWithUnchangedDimensions()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        using (var original = directory.CreateSearch(recording,
            local: () => new TestEmbeddings(TextVector), localIdentity: "local/model-v1"))
        {
            await original.SearchAsync("pigment", AppleId);
        }

        var replacement = new TestEmbeddings(TextVector);
        using var changed = directory.CreateSearch(directory.CreateRecording(),
            local: () => replacement, localIdentity: "local/model-v2");
        await changed.SearchAsync("pigment", AppleId);
        Assert.Contains("cobalt", replacement.Inputs);
        Assert.Equal(2, Directory.GetFiles(directory.IndexDirectory,
            recording.ActiveChatId + ".json", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task RequestedDimensions_AreSharedByIndexAndQueryButNeverReuseAnotherSpace()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        var generator = new TestEmbeddings(TextVector);
        using var search = directory.CreateSearch(recording, local: () => generator);
        await search.SearchAsync("pigment", AppleId);
        var defaultCalls = generator.Inputs.Count;
        await search.SearchAsync("pigment", AppleId, dimensions: 3);
        Assert.Equal(defaultCalls * 2, generator.Inputs.Count);
        Assert.All(generator.RequestedDimensions.Take(defaultCalls), dimension => Assert.Null(dimension));
        Assert.All(generator.RequestedDimensions.Skip(defaultCalls), dimension => Assert.Equal(3, dimension));
        Assert.Equal(2, Directory.GetFiles(directory.IndexDirectory,
            recording.ActiveChatId + ".json", SearchOption.AllDirectories).Length);

        await search.SearchAsync("pigment", AppleId);
        Assert.Equal(defaultCalls + 1, generator.RequestedDimensions.Count(dimension => dimension is null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            search.SearchAsync("pigment", AppleId, dimensions: 0));
    }

    [Fact]
    public async Task ReadOnlyLibrary_IsEnoughToSearchWithoutARecordingCoordinator()
    {
        using var directory = new SearchDirectory();
        var saved = ChatRecordingSerializer.Deserialize(File.ReadAllText(FixturePath("no-tools.json")));
        var library = new InlineLibrary(saved);
        using var search = new ChatSearchService(library, NullLogger<ChatSearchService>.Instance,
            directory.DataDirectory, []);

        var result = await search.SearchAsync("cobalt", ChatSearchDescriptor.ContainsId);
        Assert.Single(result.Hits);
        Assert.Equal(1, library.ReadCount);
    }

    [Fact]
    public async Task LazyIndexing_ReportsChatAndChunkProgress()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        recording.AddResponse(
            ChatRecordingSerializer.Request([new ChatMessage(ChatRole.User, "Long response")], null),
            new ChatResponse([new ChatMessage(ChatRole.Assistant, new string('x', 360 * 34))]));
        recording.NewRecording();
        recording.AddResponse(
            ChatRecordingSerializer.Request([new ChatMessage(ChatRole.User, "Short response")], null),
            new ChatResponse([new ChatMessage(ChatRole.Assistant, "Done.")]));

        var reports = new List<ChatSearchProgress>();
        using var search = directory.CreateSearch(recording,
            local: () => new TestEmbeddings(TextVector));
        await search.SearchAsync(string.Empty, AppleId,
            progress: new InlineProgress<ChatSearchProgress>(reports.Add));

        Assert.Contains(reports, report => report.IsIndexing && report.TotalChats == 2 &&
            report.TotalChunks > 32 && report.IndexedChunks == 32);
        Assert.Contains(reports, report => !report.IsIndexing && report.CompletedChats == 2 &&
            report.TotalChats == 2);
    }

    [Fact]
    public async Task CloudEmbeddings_RequireExplicitSelectionAndNeverReadImageBytes()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("multi-turn-image-in-out.json")))
            await recording.LoadFileAsync(input);

        var azure = new TestEmbeddings(TextVector);
        using var search = directory.CreateSearch(recording, azure: () => azure);
        var privateSearch = await search.SearchAsync("watercolor", ChatSearchDescriptor.ContainsId);
        Assert.False(privateSearch.IsSemantic);
        Assert.Empty(azure.Inputs);

        var consented = await search.SearchAsync("watercolor", AzureId);
        Assert.True(consented.IsSemantic);
        Assert.NotEmpty(azure.Inputs);
        Assert.DoesNotContain(azure.Inputs, text => text.Contains("data:image", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(azure.Inputs, text => text.Contains("Describing the generated image"));
    }

    [Fact]
    public async Task DamagedIndex_IsRebuiltWithVisibleNotice_WithoutChangingRecording()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);
        var original = File.ReadAllBytes(recording.AutosavePath);

        using (var search = directory.CreateSearch(recording, local: () => new TestEmbeddings(TextVector)))
            await search.SearchAsync("pigment", AppleId);

        var indexPath = Assert.Single(Directory.GetFiles(directory.IndexDirectory,
            recording.ActiveChatId + ".json", SearchOption.AllDirectories));
        File.WriteAllText(indexPath, "{ damaged");

        using var restarted = directory.CreateSearch(directory.CreateRecording(),
            local: () => new TestEmbeddings(TextVector));
        var result = await restarted.SearchAsync("pigment", AppleId);
        Assert.Single(result.Hits);
        Assert.Contains("Rebuilt a damaged search index", result.Notice);
        Assert.Equal(original, File.ReadAllBytes(recording.AutosavePath));
    }

    [Fact]
    public async Task IndexWithNullChunk_IsRebuiltWithoutChangingRecording()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);
        var original = File.ReadAllBytes(recording.AutosavePath);

        using (var search = directory.CreateSearch(recording, local: () => new TestEmbeddings(TextVector)))
            await search.SearchAsync("pigment", AppleId);

        var indexPath = Assert.Single(Directory.GetFiles(directory.IndexDirectory,
            recording.ActiveChatId + ".json", SearchOption.AllDirectories));
        var index = JsonNode.Parse(File.ReadAllText(indexPath))!;
        index["Chunks"]!.AsArray().Add(null);
        File.WriteAllText(indexPath, index.ToJsonString());

        using var restarted = directory.CreateSearch(directory.CreateRecording(),
            local: () => new TestEmbeddings(TextVector));
        var result = await restarted.SearchAsync("pigment", AppleId);
        Assert.Single(result.Hits);
        Assert.Contains("Rebuilt a damaged search index", result.Notice);
        Assert.Equal(original, File.ReadAllBytes(recording.AutosavePath));
    }

    [Fact]
    public async Task EmptyEmbedding_LeavesRecordingIntactAndReportsError()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        using var search = directory.CreateSearch(recording,
            local: () => new TestEmbeddings(_ => [0f, 0f]));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            search.SearchAsync(string.Empty, AppleId));
        Assert.Equal(1, recording.InteractionCount);
        Assert.Single((await search.SearchAsync("cobalt", ChatSearchDescriptor.ContainsId)).Hits);
    }

    [Fact]
    public async Task MissingEmbedding_ReportsFailureInsteadOfIndexingInvalidAppleVector()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        using var search = directory.CreateSearch(recording,
            local: () => new TestEmbeddings(_ => []));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            search.SearchAsync(string.Empty, AppleId));
        Assert.Single(recording.ListChats(), chat => chat.InteractionCount == 1);
    }

    [Fact]
    public async Task NewTurn_AppendsOnlyItsOwnVectors()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        var generator = new TestEmbeddings(TextVector);
        using var search = directory.CreateSearch(recording, local: () => generator);
        await search.SearchAsync(string.Empty, AppleId);
        var originalInputs = generator.Inputs.Count;

        recording.AddResponse(
            ChatRecordingSerializer.Request([new ChatMessage(ChatRole.User, "Describe violet")], null),
            new ChatResponse([new ChatMessage(ChatRole.Assistant, "Violet is a color.")]));
        await search.SearchAsync(string.Empty, AppleId);

        Assert.Equal(originalInputs + 2, generator.Inputs.Count);
        Assert.Single(generator.Inputs, text => text.StartsWith("Reply with exactly", StringComparison.Ordinal));
        Assert.Contains(generator.Inputs, text => text.Contains("Violet is a color."));
    }

    [Fact]
    public async Task ChangedEmbeddingDimensions_RebuildVectorsInsteadOfComparingDifferentSpaces()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        using (var search = directory.CreateSearch(recording, local: () => new TestEmbeddings(TextVector)))
            await search.SearchAsync("pigment", AppleId);

        var replacement = new TestEmbeddings(text => [.. TextVector(text), 0.5f]);
        using var restarted = directory.CreateSearch(directory.CreateRecording(),
            local: () => replacement);
        var result = await restarted.SearchAsync("pigment", AppleId);

        Assert.Single(result.Hits);
        Assert.Contains("Rebuilt the outdated search vectors", result.Notice);
        Assert.Contains(replacement.Inputs, text => text.Contains("cobalt"));
    }

    private static float[] TextVector(string text) =>
        text.Contains("cobalt", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("pigment", StringComparison.OrdinalIgnoreCase)
            ? [1f, 0f]
            : [0f, 1f];

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", name);

    private static IEmbeddingGenerator<string, Embedding<float>> TestProvider(
        string id, string identity, ChatSearchDataLocation location, TestEmbeddings generator) =>
        new DescribedEmbeddingGenerator(() => generator,
            new ChatSearchDescriptor(id, id, "Test embedding model", identity, location));

    private sealed class InlineLibrary(ChatRecording recording) : IChatLibrary
    {
        public int ReadCount { get; private set; }

        public IReadOnlyList<SavedChat> ListChats() =>
            [new SavedChat("chat", "Test chat", DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow, recording.Interactions.Count)];

        public ChatRecording ReadChat(string id)
        {
            Assert.Equal("chat", id);
            ReadCount++;
            return recording;
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class SearchDirectory : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "chat-search-" + Guid.NewGuid().ToString("N"));

        public string DataDirectory => Path.Combine(_root, "data");
        public string IndexDirectory => Path.Combine(DataDirectory, "chat-playground", "indexes");

        public ChatLibraryService CreateRecording() =>
            new(NullLogger<ChatLibraryService>.Instance, DataDirectory, Path.Combine(_root, "cache"));

        public ChatSearchService CreateSearch(
            ChatLibraryService recording,
            Func<IEmbeddingGenerator<string, Embedding<float>>>? local = null,
            Func<IEmbeddingGenerator<string, Embedding<float>>>? azure = null,
            string? localIdentity = null)
        {
            var providers = new List<IEmbeddingGenerator<string, Embedding<float>>>();
            if (local is not null)
                providers.Add(new DescribedEmbeddingGenerator(local,
                    new ChatSearchDescriptor(AppleId, "Apple test", "Local search",
                        localIdentity ?? "apple/natural-language/en/test", ChatSearchDataLocation.OnDevice)));
            if (azure is not null)
                providers.Add(new DescribedEmbeddingGenerator(azure,
                    new ChatSearchDescriptor(AzureId, "Azure test", "Remote search",
                        "azure/text-embedding-3-small/test", ChatSearchDataLocation.Remote)));
            return new ChatSearchService(recording, NullLogger<ChatSearchService>.Instance,
                DataDirectory, providers);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
