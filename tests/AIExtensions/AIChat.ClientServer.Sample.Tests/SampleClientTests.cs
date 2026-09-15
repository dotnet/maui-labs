using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.ClientModel;
using System.Net.Http.Headers;
using AGUI.Abstractions;
using AGUI.Client;
using AIChat.ClientServer.Sample.Client;
using AIChat.ClientServer.Sample.Shared;
using AIChat.Sample.Shared;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Presentation;
using Microsoft.Maui.AI.Chat.Recording;
using Microsoft.Maui.Chat;

namespace AIChat.ClientServer.Sample.Tests;

public sealed class SampleClientTests
{
    [Fact]
    public void Parse_EnvironmentValueWinsOverConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AI:Chat:Mode"] = "Replay" })
            .Build();

        Assert.Equal(ChatSampleMode.Live, ChatSampleModeParser.Parse(config, _ => "Live"));
    }

    [Fact]
    public void Parse_InvalidMode_ThrowsMeaningfulError()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AI:Chat:Mode"] = "offline" })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() => ChatSampleModeParser.Parse(config, _ => null));
        Assert.Contains("Live, Record, or Replay", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayFixture_IsFullyConsumed()
    {
        var tape = new ChatRecording
        {
            Interactions =
            [
                new RecordedChatInteraction
                {
                    Sequence = 0, Legacy = true,
                    Updates = [new RecordedChatUpdate
                    {
                        Sequence = 0,
                        Value = new System.Text.Json.Nodes.JsonObject
                        {
                            ["role"] = "assistant",
                            ["contents"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
                            {
                                ["type"] = "text", ["text"] = "Synthetic fixture"
                            })
                        }
                    }]
                }
            ]
        };
        using var client = new ReplayChatClient(new ChatRecordingOptions { Recording = tape });
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
            updates.Add(update);

        Assert.Equal("Synthetic fixture", Assert.IsType<TextContent>(updates[0].Contents[0]).Text);
        client.Session.AssertFullyReplayed();
    }

    [Fact]
    public void AguiTransport_UsesEndpointBearerAndInfiniteTimeoutWithoutExposingToken()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AGUI:Endpoint"] = "https://example.test",
            ["AGUI:ApiKey"] = "very-secret",
        }).Build();
        using var client = new HttpClient();

        AguiEndpointConfiguration.Configure(client, config);

        Assert.Equal(new Uri("https://example.test/"), client.BaseAddress);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
        Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization!.Scheme);
        Assert.NotEqual("very-secret", client.ToString());
    }

    [Fact]
    public void AguiTransport_RejectsNonLoopbackCleartextOutsideDebug()
    {
        AguiEndpointConfiguration.ValidateEndpoint(new Uri("https://example.test"), allowDebugCleartext: false);
        AguiEndpointConfiguration.ValidateEndpoint(new Uri("http://10.0.2.2:5018"), allowDebugCleartext: true);
        Assert.Throws<InvalidOperationException>(() =>
            AguiEndpointConfiguration.ValidateEndpoint(new Uri("http://example.test"), allowDebugCleartext: true));
        Assert.Throws<InvalidOperationException>(() =>
            AguiEndpointConfiguration.ValidateEndpoint(new Uri("http://127.0.0.1:5018"), allowDebugCleartext: false));
    }

    [Theory]
    [InlineData("iOS")]
    [InlineData("MacCatalyst")]
    public void AppleManifests_AllowLocalNetworkingWithoutArbitraryCleartext(string platform)
    {
        var repositoryRoot = FindRepositoryRoot();
        var manifest = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "samples",
            "AIChat.ClientServer.Sample",
            "AIChat.ClientServer.Sample.Client",
            "Platforms",
            platform,
            "Info.plist"));

        Assert.Contains("<key>NSAllowsLocalNetworking</key><true/>", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("NSAllowsArbitraryLoads", manifest, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AIChat.Client.Sample")]
    [InlineData("AIChat.ClientServer.Sample/AIChat.ClientServer.Sample.Client")]
    public void IosManifests_DeclarePhoneAndTabletOrientations(string samplePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var manifest = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "samples",
            samplePath,
            "Platforms",
            "iOS",
            "Info.plist"));

        Assert.Contains("<key>UISupportedInterfaceOrientations</key>", manifest, StringComparison.Ordinal);
        Assert.Contains("<key>UISupportedInterfaceOrientations~ipad</key>", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void AguiScenarioPaths_MatchAllServerScenarios()
    {
        foreach (var scenario in ScenarioIds.All)
            Assert.Equal("/" + scenario.Id, AguiEndpointConfiguration.GetScenarioPath(scenario.Id));

        Assert.Throws<ArgumentOutOfRangeException>(() => AguiEndpointConfiguration.GetScenarioPath("not-a-scenario"));
    }

    [Fact]
    public void AguiRequestCodec_CanonicalizesTypedRawRepresentation()
    {
        var codec = new AguiRunRequestCodec();
        var options = new ChatOptions
        {
            RawRepresentationFactory = _ => new RunAgentInput
            {
                ThreadId = "volatile-thread",
                RunId = "volatile-run",
                State = JsonSerializer.SerializeToElement(new { document = new { content = "draft" } }),
            }
        };

        var canonical = codec.Write(options);

        Assert.True(codec.CanWrite(options));
        Assert.Equal("agui-run-agent-input-v1", canonical["kind"]!.GetValue<string>());
        Assert.Null(canonical["threadId"]);
        Assert.Equal(canonical.ToJsonString(), codec.Read(canonical).ToJsonString());
        Assert.Throws<InvalidDataException>(() => codec.Read(new JsonObject
        {
            ["kind"] = "agui-run-agent-input-v1",
            ["state"] = new JsonObject { ["unsafe"] = true },
        }));
    }

    [Fact]
    public void ClientAgentState_UsesGeneratedJsonMetadataForTheAguiRequestEnvelope()
    {
        var state = JsonSerializer.SerializeToElement(
            new ClientAgentState
            {
                Plan = new Plan
                {
                    Steps =
                    [
                        new PlanStep
                        {
                            Description = "Research",
                            Status = PlanStepStatus.Pending,
                        },
                    ],
                },
                Recipe = new Recipe { Title = "Soup" },
                Document = new DocumentState { Content = "Draft" },
            },
            SampleSerializerContext.Default.ClientAgentState);

        Assert.Equal("Research", state.GetProperty("plan").GetProperty("steps")[0].GetProperty("description").GetString());
        Assert.Equal("Soup", state.GetProperty("recipe").GetProperty("title").GetString());
        Assert.Equal("Draft", state.GetProperty("document").GetProperty("content").GetString());

        var proposal = JsonSerializer.SerializeToElement(
            new DocumentProposalSnapshot
            {
                Proposal = new DocumentProposal
                {
                    Document = new DocumentState { Content = "Proposed draft" },
                },
            },
            SampleSerializerContext.Default.DocumentProposalSnapshot);
        Assert.Equal(
            "Proposed draft",
            proposal.GetProperty("proposal").GetProperty("document").GetProperty("content").GetString());
    }

    [Fact]
    public void StateMapper_AppliesCanonicalPlanRecipeAndDocumentProposalShapes()
    {
        var state = new SampleUiState();
        using var snapshot = JsonDocument.Parse("""{"plan":{"steps":[{"description":"Research","status":"Pending"}]}}""");
        using var allowed = JsonDocument.Parse("""{"op":"replace","path":"/steps/0/status","value":"completed"}""");
        using var rejected = JsonDocument.Parse("""{"op":"replace","path":"/../../unsafe","value":"completed"}""");
        using var proposal = JsonDocument.Parse("""{"document":{"content":"Updated draft"}}""");
        using var recipe = JsonDocument.Parse("""{"recipe":{"title":"Soup","ingredients":[],"instructions":[]}}""");
        using var predictive = JsonDocument.Parse("""{"proposal":{"document":{"content":"Pending"}}}""");

        Assert.True(state.TryApplySnapshot(snapshot.RootElement));
        Assert.True(state.TryApplyDelta(allowed.RootElement));
        Assert.False(state.TryApplyDelta(rejected.RootElement));
        Assert.True(state.TryApplySnapshot(recipe.RootElement));
        Assert.True(state.TryApplySnapshot(predictive.RootElement));
        Assert.Equal(string.Empty, state.Document.Content);
        Assert.True(state.TryApplyDocumentProposal(proposal.RootElement));
        state.AcceptDocumentProposal();

        Assert.Equal(PlanStepStatus.Completed, state.Plan.Steps[0].Status);
        Assert.Equal("Soup", state.Recipe!.Title);
        Assert.Equal("Updated draft", state.Document.Content);
    }

    [Fact]
    public async Task RecordingNormalizer_StripsProviderRawBeforeRecordingAndReplay()
    {
        var provider = new RawUpdateChatClient();
        using var recording = new RecordingChatClient(
            new RecordingRawNormalizerChatClient(provider),
            new ChatRecordingOptions { Mode = ChatRecordingMode.Record, StrictSanitizer = true });
        _ = await recording.GetResponseAsync([new ChatMessage(ChatRole.User, "record")]);

        var update = Assert.Single(recording.Session.Recording.Interactions).Updates.Single().Value;
        Assert.Null(update["raw"]);
        Assert.Null(update["contents"]![0]!["metadata"]);

        using var replay = new ReplayChatClient(new ChatRecordingOptions
        {
            Recording = recording.Session.Recording,
            StrictSanitizer = true,
        });
        var response = await replay.GetResponseAsync([new ChatMessage(ChatRole.User, "record")]);
        Assert.Equal("provider text", response.Text);
        replay.Session.AssertFullyReplayed();
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task DirectLiveRecording_WithExplicitOptIn_StrictlyRecordsAndReplaysProviderResponse()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MAUI_AI_CHAT_LIVE_SMOKE"), "1", StringComparison.Ordinal))
            return;

        var configuration = LoadSharedAiConfiguration();
        var endpoint = configuration["AI:Endpoint"]
            ?? throw new InvalidOperationException("AI:Endpoint is required for the opted-in live smoke test.");
        var apiKey = configuration["AI:ApiKey"]
            ?? throw new InvalidOperationException("AI:ApiKey is required for the opted-in live smoke test.");
        var deployment = GetLiveDeployment(configuration);
        var fixturePath = Path.Combine(Path.GetTempPath(), $"maui-ai-chat-live-{Guid.NewGuid():N}.recording.json");
        var messages = new[] { new ChatMessage(ChatRole.User, "Reply with exactly: strict recording verified.") };

        try
        {
            var provider = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
                .GetChatClient(deployment)
                .AsIChatClient();
            using var recording = new RecordingChatClient(
                new RecordingRawNormalizerChatClient(provider),
                new ChatRecordingOptions
                {
                    Mode = ChatRecordingMode.Record,
                    FixturePath = fixturePath,
                    StrictSanitizer = true,
                    Adapter = "direct-azure-openai-live-smoke",
                });

            _ = await recording.GetResponseAsync(messages);
            ChatRecordingStore.Save(recording.Session.Recording, fixturePath);

            using var replay = new ReplayChatClient(new ChatRecordingOptions
            {
                FixturePath = fixturePath,
                StrictSanitizer = true,
            });
            var response = await replay.GetResponseAsync(messages);

            Assert.False(string.IsNullOrWhiteSpace(response.Text));
            replay.Session.AssertFullyReplayed();
        }
        finally
        {
            File.Delete(fixturePath);
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task DirectLiveReasoning_WithExplicitOptIn_UsesTheResponsesApi()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MAUI_AI_CHAT_LIVE_SMOKE"), "1", StringComparison.Ordinal))
            return;

        var configuration = LoadSharedAiConfiguration();
        var endpoint = configuration["AI:Endpoint"]
            ?? throw new InvalidOperationException("AI:Endpoint is required for the opted-in live smoke test.");
        var apiKey = configuration["AI:ApiKey"]
            ?? throw new InvalidOperationException("AI:ApiKey is required for the opted-in live smoke test.");
        using var client = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
            .GetResponsesClient()
            .AsIChatClient(GetLiveDeployment(configuration));

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Reply with exactly: reasoning path verified.")],
            new ChatOptions
            {
                Reasoning = new ReasoningOptions { Output = ReasoningOutput.Full },
            });

        Assert.Contains("reasoning path verified", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task AguiLiveClient_WithExplicitOptIn_StreamsServerResponse()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MAUI_AI_CHAT_LIVE_SMOKE"), "1", StringComparison.Ordinal))
            return;

        var endpoint = Environment.GetEnvironmentVariable("MAUI_AI_CHAT_AGUI_ENDPOINT")
            ?? "http://127.0.0.1:5018";
        var apiKey = Environment.GetEnvironmentVariable("MAUI_AI_CHAT_AGUI_API_KEY")
            ?? throw new InvalidOperationException(
                "MAUI_AI_CHAT_AGUI_API_KEY is required for the opted-in AG-UI live smoke test.");
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        using var client = new AGUIChatClient(new AGUIChatClientOptions(
            httpClient,
            "/" + ScenarioIds.AgenticChat));
        var options = new ChatOptions
        {
            RawRepresentationFactory = _ => new RunAgentInput
            {
                ThreadId = $"test-{Guid.NewGuid():N}",
                RunId = Guid.NewGuid().ToString("N"),
                State = JsonSerializer.SerializeToElement(
                    new ClientAgentState
                    {
                        Plan = new Plan(),
                        Recipe = new Recipe(),
                        Document = new DocumentState(),
                    },
                    SampleSerializerContext.Default.ClientAgentState),
            },
        };
        var text = new List<string>();

        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Reply with one short greeting.")],
            options))
        {
            text.Add(update.Text);
        }

        Assert.False(string.IsNullOrWhiteSpace(string.Concat(text)));
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task AguiLiveComposition_WithExplicitOptIn_CompletesTheAgentTurn()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MAUI_AI_CHAT_LIVE_SMOKE"), "1", StringComparison.Ordinal))
            return;

        var apiKey = Environment.GetEnvironmentVariable("MAUI_AI_CHAT_AGUI_API_KEY")
            ?? throw new InvalidOperationException(
                "MAUI_AI_CHAT_AGUI_API_KEY is required for the opted-in AG-UI live smoke test.");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Chat:Mode"] = "Live",
                ["AGUI:Endpoint"] = Environment.GetEnvironmentVariable("MAUI_AI_CHAT_AGUI_ENDPOINT")
                    ?? "http://127.0.0.1:5018",
                ["AGUI:ApiKey"] = apiKey,
            })
            .Build();
        using var composition = new AguiChatComposition(
            configuration,
            new ConfiguredHttpClientFactory(configuration),
            EmptyServiceProvider.Instance);

        await composition.Session.SendMessageAsync("Reply with one short greeting.");

        Assert.True(
            composition.Session.Status == ConversationStatus.Idle,
            composition.Session.Error?.ToString() ?? $"Unexpected status: {composition.Session.Status}");
        Assert.Contains(
            composition.Session.Turns.Single().ResponseBlocks,
            block => block is TextContentBlock text && !string.IsNullOrWhiteSpace(text.RawText));
    }

    [Fact]
    public async Task AguiRawCodec_RecordsAndReplaysActualTypedStateEvents()
    {
        var codec = new AguiStateEventRecordingCodec();
        using var recording = new RecordingChatClient(new StateEventChatClient(), new ChatRecordingOptions
        {
            Mode = ChatRecordingMode.Record,
            StrictSanitizer = true,
            RawCodecs = { codec },
        });
        await foreach (var _ in recording.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "plan")])) { }

        using var replay = new ReplayChatClient(new ChatRecordingOptions
        {
            Recording = recording.Session.Recording,
            StrictSanitizer = true,
            RawCodecs = { codec },
        });
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "plan")]))
            updates.Add(update);
        Assert.IsType<StateSnapshotEvent>(updates[0].RawRepresentation);
        Assert.IsType<StateDeltaEvent>(updates[1].RawRepresentation);
        replay.Session.AssertFullyReplayed();
    }

    [Fact]
    public async Task ScenarioFixtures_AreSelectedAndFullyConsumed()
    {
        foreach (var scenario in ScenarioIds.All)
        {
            using var replay = new ReplayChatClient(new ChatRecordingOptions
            {
                Recording = AguiScenarioReplayFixtures.Create(scenario.Id),
                StrictSanitizer = true,
                RawCodecs = { new AguiStateEventRecordingCodec() },
                RequestCodecs = { new AguiRunRequestCodec() },
            });
            await foreach (var _ in replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, scenario.Id)])) { }
            if (scenario.Id is ScenarioIds.PredictiveState or ScenarioIds.HumanInTheLoop or ScenarioIds.SelectiveApproval
                or ScenarioIds.FrontendTools or ScenarioIds.ToolBasedGenerativeUi)
                await foreach (var _ in replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "decision")])) { }
            replay.Session.AssertFullyReplayed();
        }

        foreach (var scenario in new[] { "basic", "weather", "approval", "frontend-action", "predictive", "reasoning", "attachments", "restore" })
        {
            using var replay = new ReplayChatClient(new ChatRecordingOptions
            {
                Recording = DirectScenarioReplayFixtures.Create(scenario),
                StrictSanitizer = true,
            });
            if (scenario == "restore")
            {
                await Assert.ThrowsAsync<RecordedChatException>(async () =>
                {
                    await foreach (var _ in replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, scenario)])) { }
                });
            }
            else
            {
                await foreach (var _ in replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, scenario)])) { }
            }
            if (scenario is "approval" or "frontend-action" or "predictive")
                await foreach (var _ in replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "continuation")])) { }
            if (scenario == "restore")
                await foreach (var _ in replay.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "retry")])) { }
            replay.Session.AssertFullyReplayed();
        }
    }

    [Fact]
    public async Task AguiComposition_ReplaysEachProductionScenarioAndConsumesItsOwnTape()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Chat:Mode"] = "Replay",
        }).Build();
        using var composition = new AguiChatComposition(config, new TestHttpClientFactory(), EmptyServiceProvider.Instance);
        foreach (var scenario in ScenarioIds.All)
        {
            composition.SelectedScenario = scenario;
            var send = composition.Session.SendMessageAsync($"replay {scenario.Id}");
            if (scenario.Id is ScenarioIds.PredictiveState or ScenarioIds.HumanInTheLoop or ScenarioIds.SelectiveApproval)
            {
                await WaitForAwaitingInputAsync(composition.Session);
                if (scenario.Id == ScenarioIds.PredictiveState)
                    await composition.ResolveDocumentProposalAsync(accepted: true);
                else
                    Assert.IsType<ToolApprovalBlock>(
                        composition.Session.Turns.Single().ResponseBlocks.Last()).Approve();
            }
            await send;
            Assert.Equal(ConversationStatus.Idle, composition.Session.Status);
            AssertAguiScenario(composition, scenario.Id);
            composition.AssertReplayFullyConsumed();
        }
    }

    [Fact]
    public async Task DirectComposition_ReplaysEachProductionScenarioAndConsumesItsOwnTape()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Chat:Mode"] = "Replay",
        }).Build();
        using var composition = new AIChat.Client.Sample.DirectChatComposition(config, EmptyServiceProvider.Instance);
        foreach (var scenario in composition.Scenarios)
        {
            composition.SelectedScenario = scenario;
            var send = composition.Session.SendMessageAsync($"replay {scenario.Id}");
            if (scenario.Id is "approval" or "predictive")
            {
                await WaitForAwaitingInputAsync(composition.Session);
                if (scenario.Id == "predictive")
                    await composition.ResolveDocumentProposalAsync(accepted: true);
                else
                    Assert.IsType<ToolApprovalBlock>(
                        composition.Session.Turns.Single().ResponseBlocks.Last()).Approve();
            }
            await send;
            if (scenario.Id == "restore")
            {
                Assert.Equal(ConversationStatus.Error, composition.Session.Status);
                await composition.RetryAsync();
            }

            Assert.Equal(ConversationStatus.Idle, composition.Session.Status);
            AssertDirectScenario(composition, scenario.Id);
            composition.AssertReplayFullyConsumed();
        }
    }

    [Fact]
    public async Task DirectApprovalReplay_RejectionDoesNotClaimTheMeetingWasBooked()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Chat:Mode"] = "Replay",
        }).Build();
        using var composition = new AIChat.Client.Sample.DirectChatComposition(
            config,
            EmptyServiceProvider.Instance);
        composition.SelectedScenario = composition.Scenarios.Single(scenario => scenario.Id == "approval");

        var send = composition.Session.SendMessageAsync("Book a meeting.");
        await WaitForAwaitingInputAsync(composition.Session);
        var approval = composition.Session.Turns.Single().ResponseBlocks.OfType<ToolApprovalBlock>().Single();
        approval.Reject("Not now.");
        await send;

        var responseText = string.Join(
            Environment.NewLine,
            composition.Session.Turns.Single().ResponseBlocks.OfType<TextContentBlock>().Select(block => block.RawText));
        Assert.Equal(ApprovalStatus.Rejected, approval.Status);
        Assert.Contains("decision was recorded", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("booked", responseText, StringComparison.OrdinalIgnoreCase);
        composition.AssertReplayFullyConsumed();
    }

    [Theory]
    [InlineData(ScenarioIds.HumanInTheLoop, "booked")]
    [InlineData(ScenarioIds.SelectiveApproval, "completed")]
    public async Task AguiApprovalReplay_RejectionDoesNotClaimTheOperationSucceeded(
        string scenarioId,
        string forbiddenClaim)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Chat:Mode"] = "Replay",
        }).Build();
        using var composition = new AguiChatComposition(
            config,
            new TestHttpClientFactory(),
            EmptyServiceProvider.Instance);
        composition.SelectedScenario = composition.Scenarios.Single(scenario => scenario.Id == scenarioId);

        var send = composition.Session.SendMessageAsync("Request approval.");
        await WaitForAwaitingInputAsync(composition.Session);
        var approval = composition.Session.Turns.Single().ResponseBlocks.OfType<ToolApprovalBlock>().Single();
        approval.Reject("Declined.");
        await send;

        var responseText = string.Join(
            Environment.NewLine,
            composition.Session.Turns.Single().ResponseBlocks.OfType<TextContentBlock>().Select(block => block.RawText));
        Assert.Equal(ApprovalStatus.Rejected, approval.Status);
        Assert.Contains("decision was recorded", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(forbiddenClaim, responseText, StringComparison.OrdinalIgnoreCase);
        composition.AssertReplayFullyConsumed();
    }

    [Fact]
    public async Task PredictiveRendererPredicate_MatchesOnlyThePredictiveAction()
    {
        using var host = new SampleSessionHost(
            ChatSampleMode.Replay,
            (_, _) => new ProposalChatClient(),
            (options, _) => options.RegisterUIAction(AIFunctionFactory.Create(
                (DocumentProposal proposal, bool? accepted) => $"decision:{accepted}",
                "propose_document",
                "Test proposal."),
                UIActionInvocationMode.Manual));
        var send = host.Session.SendMessageAsync("propose");
        await WaitForAwaitingInputAsync(host.Session);
        var action = Assert.IsType<UIActionBlock>(host.Session.Turns.Single().ResponseBlocks.Single());
        using var predictive = new AgentBlockContent(action, host.Session.Turns.Single(), isRequest: false);

        Assert.True(PredictiveProposal.IsPredictiveProposal(predictive));

        await host.ResolveDocumentProposalAsync(accepted: false);
        await send;
    }

    [Fact]
    public void PredictiveScenarios_DisableParallelToolCallsAcrossCompositions()
    {
        Assert.False(PredictiveToolCallPolicy.ForDirect("predictive"));
        Assert.False(PredictiveToolCallPolicy.ForAgui(ScenarioIds.PredictiveState));
        Assert.False(PredictiveToolCallPolicy.ForServerDocument());
        Assert.True(PredictiveToolCallPolicy.ForDirect("weather"));
        Assert.True(PredictiveToolCallPolicy.ForAgui(ScenarioIds.AgenticChat));
    }

    [Fact]
    public void AndroidDebugManifest_RestrictsCleartextToLoopbackAndEmulator()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "samples", "AIChat.ClientServer.Sample",
            "AIChat.ClientServer.Sample.Client", "AIChat.ClientServer.Sample.Client.csproj"));
        var releaseManifest = File.ReadAllText(Path.Combine(root, "samples", "AIChat.ClientServer.Sample",
            "AIChat.ClientServer.Sample.Client", "Platforms", "Android", "AndroidManifest.xml"));
        var debugManifest = File.ReadAllText(Path.Combine(root, "samples", "AIChat.ClientServer.Sample",
            "AIChat.ClientServer.Sample.Client", "Platforms", "Android", "AndroidManifest.Debug.xml"));
        var networkConfig = File.ReadAllText(Path.Combine(root, "samples", "AIChat.ClientServer.Sample",
            "AIChat.ClientServer.Sample.Client", "Platforms", "Android", "Resources", "xml", "network_security_config.xml"));

        Assert.Contains("<AndroidManifest Condition=\"'$(Configuration)' == 'Debug'\">Platforms\\Android\\AndroidManifest.Debug.xml</AndroidManifest>", project, StringComparison.Ordinal);
        Assert.Contains("Target Name=\"UseDebugAndroidManifest\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("AndroidNetworkSecurityConfig", project, StringComparison.Ordinal);
        Assert.DoesNotContain("networkSecurityConfig", releaseManifest, StringComparison.Ordinal);
        Assert.Contains("android:networkSecurityConfig=\"@xml/network_security_config\"", debugManifest, StringComparison.Ordinal);
        Assert.Contains(">127.0.0.1<", networkConfig, StringComparison.Ordinal);
        Assert.Contains(">10.0.2.2<", networkConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", networkConfig, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionReplacement_DisposesOldClientAndExposesOneSharedPresentationAndController()
    {
        var clients = new List<DisposableChatClient>();
        using var host = new SampleSessionHost(
            ChatSampleMode.Replay,
            (_, _) => { var client = new DisposableChatClient(); clients.Add(client); return client; });
        var oldPresentation = host.Presentation;
        var oldController = host.ComposerController;
        using var stateSnapshot = JsonDocument.Parse("""{"document":{"content":"old"}}""");
        host.State.TryApplySnapshot(stateSnapshot.RootElement);

        host.ReplaceSession(ScenarioIds.Reasoning);

        Assert.True(clients[0].Disposed);
        Assert.NotSame(oldPresentation, host.Presentation);
        Assert.NotSame(oldController, host.ComposerController);
        Assert.Same(host.Presentation, host.ComposerController.Conversation);
        Assert.Empty(host.State.Document.Content);
    }

    [Fact]
    public void SessionReplacement_FailurePreservesCurrentSessionAndState()
    {
        var call = 0;
        using var host = new SampleSessionHost(ChatSampleMode.Replay, (_, _) =>
        {
            if (++call > 1)
                throw new InvalidOperationException("candidate failed");
            return new DisposableChatClient();
        });
        using var snapshot = JsonDocument.Parse("""{"document":{"content":"preserve"}}""");
        host.State.TryApplySnapshot(snapshot.RootElement);
        var previous = host.Session;

        Assert.Throws<InvalidOperationException>(() => host.ReplaceSession(ScenarioIds.Reasoning));
        Assert.Same(previous, host.Session);
        Assert.Equal("preserve", host.State.Document.Content);
    }

    [Fact]
    public async Task SessionReplacement_WhenActivePreservesCurrentSessionStateAndRecording()
    {
        var saveCount = 0;
        using var host = new SampleSessionHost(
            ChatSampleMode.Record,
            (_, _) => new RecordingChatClient(new BlockingChatClient(), new ChatRecordingOptions { Mode = ChatRecordingMode.Record }),
            recordingCompleted: (_, _) => saveCount++,
            recordingPathFactory: _ => "outside.recording.json");
        using var snapshot = JsonDocument.Parse("""{"document":{"content":"preserve"}}""");
        host.State.TryApplySnapshot(snapshot.RootElement);
        var original = host.Session;
        var send = host.Session.SendMessageAsync("wait");
        await WaitForStreamingAsync(host.Session);

        Assert.False(host.ReplaceSession(ScenarioIds.Reasoning));

        Assert.Same(original, host.Session);
        Assert.Equal("basic", host.ScenarioId);
        Assert.Equal("preserve", host.State.Document.Content);
        Assert.Equal("Finish or cancel the active conversation before changing scenarios.", host.RecordingError);

        host.Dispose();
        await send;
        Assert.Equal(0, saveCount);
        Assert.Equal("The active conversation was canceled; its incomplete recording was not saved.", host.RecordingError);
    }

    [Fact]
    public async Task DirectScenarioSetter_WhenSessionIsActive_RevertsThePickerSelection()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Chat:Mode"] = "Replay",
        }).Build();
        var composition = new AIChat.Client.Sample.DirectChatComposition(
            config,
            EmptyServiceProvider.Instance,
            (_, _) => new BlockingChatClient());
        var original = composition.SelectedScenario;
        var changed = new List<string?>();
        composition.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        var send = composition.Session.SendMessageAsync("wait");
        await WaitForStreamingAsync(composition.Session);

        composition.SelectedScenario = composition.Scenarios[1];

        Assert.Same(original, composition.SelectedScenario);
        Assert.Contains(nameof(composition.SelectedScenario), changed);
        composition.Dispose();
        await send;
    }

    [Fact]
    public async Task AguiScenarioSetter_WhenSessionIsActive_RevertsThePickerSelection()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Chat:Mode"] = "Replay",
        }).Build();
        var composition = new AguiChatComposition(
            config,
            new TestHttpClientFactory(),
            EmptyServiceProvider.Instance,
            (_, _) => new BlockingChatClient());
        var original = composition.SelectedScenario;
        var changed = new List<string?>();
        composition.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        var send = composition.Session.SendMessageAsync("wait");
        await WaitForStreamingAsync(composition.Session);

        composition.SelectedScenario = composition.Scenarios[1];

        Assert.Same(original, composition.SelectedScenario);
        Assert.Contains(nameof(composition.SelectedScenario), changed);
        composition.Dispose();
        await send;
    }

    [Fact]
    public void SessionReplacement_PreservesRecordingSaveFailure()
    {
        using var host = new SampleSessionHost(
            ChatSampleMode.Record,
            (_, _) => new RecordingChatClient(
                new DisposableChatClient(),
                new ChatRecordingOptions { Mode = ChatRecordingMode.Record }),
            recordingCompleted: (_, _) => throw new IOException("forced failure"),
            recordingPathFactory: scenario => $"{scenario}.recording.json");

        Assert.True(host.ReplaceSession(ScenarioIds.Reasoning));
        Assert.Equal(ScenarioIds.Reasoning, host.ScenarioId);
        Assert.Equal("The recording could not be saved.", host.RecordingError);
    }

    [Fact]
    public void SaveAtomic_WhenWriterFails_PreservesExistingFixtureAndDeletesTemporaryFile()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"sample-recording-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "fixture.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "original");
        try
        {
            Assert.Throws<IOException>(() => SampleRecordingStore.SaveAtomic(
                new ChatRecording(),
                path,
                (_, temporaryPath) =>
                {
                    File.WriteAllText(temporaryPath, "incomplete");
                    throw new IOException("forced failure");
                }));

            Assert.Equal("original", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            Assert.Empty(Directory.GetDirectories(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAtomic_LargeBlobUsesFinalSidecarAndRemovesObsoleteBlobs()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"sample-recording-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "fixture.json");
        var bytes = Enumerable.Repeat((byte)0x5A, (64 * 1024) + 1).ToArray();
        Directory.CreateDirectory(directory);
        try
        {
            using var recorder = new RecordingChatClient(
                new DataUpdateChatClient(bytes),
                new ChatRecordingOptions
                {
                    Mode = ChatRecordingMode.Record,
                    InlineDataThresholdBytes = 64 * 1024,
                });
            await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "image")]).ToListAsync();

            SampleRecordingStore.SaveAtomic(recorder.Session.Recording, path);

            var blobDirectory = path + ".blobs";
            Assert.True(File.Exists(path));
            Assert.True(Directory.Exists(blobDirectory));
            Assert.Single(Directory.GetFiles(blobDirectory));
            Assert.Contains(
                $"{Path.GetFileName(path)}.blobs/",
                File.ReadAllText(path),
                StringComparison.Ordinal);
            Assert.Empty(Directory.GetDirectories(directory, "*.tmp"));

            using var replay = new ReplayChatClient(new ChatRecordingOptions { FixturePath = path });
            var replayed = await replay
                .GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "image")])
                .ToListAsync();
            Assert.Equal(
                bytes,
                Assert.IsType<DataContent>(replayed.Single().Contents.Single()).Data.ToArray());
            replay.Session.AssertFullyReplayed();

            SampleRecordingStore.SaveAtomic(new ChatRecording(), path);
            Assert.False(Directory.Exists(blobDirectory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SharedComposerController_UsesInjectedMultimodalServicesAndCanPickAttachments()
    {
        var picker = new FakeAttachmentPicker();
        var recorder = new FakeAudioRecorder();
        var recognizer = new FakeSpeechRecognizer();
        using var host = new SampleSessionHost(
            ChatSampleMode.Replay,
            (_, _) => new DisposableChatClient(),
            attachmentPicker: picker,
            audioRecorder: recorder,
            speechRecognizer: recognizer);

        var controller = host.ComposerController;
        Assert.True(controller.AllowAttachments);
        Assert.True(controller.AllowAudioCapture);
        Assert.True(controller.AllowLiveSpeech);
        Assert.Same(picker, controller.AttachmentPicker);
        Assert.Same(recorder, controller.AudioRecorder);
        Assert.Same(recognizer, controller.SpeechRecognizer);
        Assert.True(controller.CanPickAttachments);
        Assert.True(controller.CanToggleAudioCapture);
        Assert.True(controller.CanToggleLiveSpeech);

        await controller.PickAttachmentsAsync();
        Assert.Equal("fake.txt", Assert.Single(controller.Attachments).FileName);
    }

    [Fact]
    public void SessionDisposal_StillDisposesClientWhenRecordingCallbackFails()
    {
        var inner = new DisposableChatClient();
        var host = new SampleSessionHost(
            ChatSampleMode.Record,
            (_, _) => new RecordingChatClient(inner, new ChatRecordingOptions { Mode = ChatRecordingMode.Record }),
            recordingCompleted: (_, _) => throw new IOException("save failed"),
            recordingPathFactory: _ => "outside.recording.json");

        host.Dispose();

        Assert.True(inner.Disposed);
        Assert.Equal("The recording could not be saved.", host.RecordingError);
    }

    [Theory]
    [InlineData(true, "Accepted synthetic document")]
    [InlineData(false, "")]
    public async Task DocumentProposalResolution_InvokesActualManualBlockBeforeCommitting(
        bool accepted,
        string expectedDocument)
    {
        bool? invokedWith = null;
        using var host = new SampleSessionHost(
            ChatSampleMode.Replay,
            (_, _) => new ProposalChatClient(),
            (options, _) => options.RegisterUIAction(AIFunctionFactory.Create(
                (DocumentProposal proposal, bool? accepted) =>
                {
                    invokedWith = accepted;
                    return $"decision:{accepted}";
                },
                "propose_document",
                "Test proposal."),
                UIActionInvocationMode.Manual));

        var send = host.Session.SendMessageAsync("propose");
        for (var attempt = 0; attempt < 50 && host.Session.Status != ConversationStatus.AwaitingInput; attempt++)
            await Task.Delay(10);
        Assert.Equal(ConversationStatus.AwaitingInput, host.Session.Status);
        Assert.Equal("Accepted synthetic document", host.State.PendingDocument!.Document.Content);

        await host.ResolveDocumentProposalAsync(accepted);
        await send;

        Assert.Equal(accepted, invokedWith);
        Assert.Null(host.State.PendingDocument);
        Assert.Equal(expectedDocument, host.State.Document.Content);
    }

    [Fact]
    public async Task ToolApproval_UsesActualApproveApiAndResumesContinuation()
    {
        using var host = new SampleSessionHost(ChatSampleMode.Replay, (_, _) => new ApprovalChatClient());
        var send = host.Session.SendMessageAsync("book");
        for (var attempt = 0; attempt < 50 && host.Session.Status != ConversationStatus.AwaitingInput; attempt++)
            await Task.Delay(10);

        var approval = Assert.IsType<ToolApprovalBlock>(host.Session.Turns.Single().ResponseBlocks.Single());
        approval.Approve();
        await send;

        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Equal(ConversationStatus.Idle, host.Session.Status);
    }

    private static async Task WaitForAwaitingInputAsync(AgentContext session)
    {
        for (var attempt = 0; attempt < 100 && session.Status != ConversationStatus.AwaitingInput; attempt++)
            await Task.Delay(10);
        Assert.Equal(ConversationStatus.AwaitingInput, session.Status);
    }

    private static async Task WaitForStreamingAsync(AgentContext session)
    {
        for (var attempt = 0; attempt < 100 && session.Status != ConversationStatus.Streaming; attempt++)
            await Task.Delay(10);
        Assert.Equal(ConversationStatus.Streaming, session.Status);
    }

    private static void AssertAguiScenario(AguiChatComposition composition, string scenario)
    {
        var blocks = composition.Session.Turns.Single().ResponseBlocks;
        switch (scenario)
        {
            case ScenarioIds.AgenticChat:
            case ScenarioIds.Workflow:
                Assert.Contains(blocks, block => block is TextContentBlock);
                break;
            case ScenarioIds.BackendToolRendering:
                Assert.Contains(blocks, block => block is FunctionInvocationContentBlock function && function.Result is not null);
                break;
            case ScenarioIds.FrontendTools:
            case ScenarioIds.ToolBasedGenerativeUi:
                Assert.Contains(blocks, block => block is UIActionBlock action && action.IsComplete);
                break;
            case ScenarioIds.HumanInTheLoop:
                Assert.Contains(blocks, block => block is ToolApprovalBlock { Status: ApprovalStatus.Approved });
                Assert.Contains(blocks, block => block is TextContentBlock);
                break;
            case ScenarioIds.SelectiveApproval:
                Assert.Contains(blocks, block => block is FunctionInvocationContentBlock function && function.Result is not null);
                Assert.Contains(blocks, block => block is ToolApprovalBlock { Status: ApprovalStatus.Approved });
                Assert.Contains(blocks, block => block is TextContentBlock);
                break;
            case ScenarioIds.AgenticGenerativeUi:
                Assert.Equal(PlanStepStatus.Completed, Assert.Single(composition.State.Plan.Steps).Status);
                Assert.Contains(blocks, block => block is TextContentBlock);
                break;
            case ScenarioIds.SharedState:
                Assert.Equal("Synthetic soup", composition.State.Recipe!.Title);
                Assert.Contains(blocks, block => block is TextContentBlock);
                break;
            case ScenarioIds.PredictiveState:
                Assert.Equal("Synthetic proposed document.", composition.State.Document.Content);
                Assert.Null(composition.State.PendingDocument);
                Assert.Contains(blocks, block => block is UIActionBlock { IsComplete: true });
                Assert.Contains(blocks, block => block is TextContentBlock);
                break;
            case ScenarioIds.Reasoning:
                Assert.Contains(blocks, block => block is ReasoningContentBlock);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown AG-UI scenario.");
        }
    }

    private static void AssertDirectScenario(AIChat.Client.Sample.DirectChatComposition composition, string scenario)
    {
        var blocks = composition.Session.Turns.Single().ResponseBlocks;
        switch (scenario)
        {
            case "basic":
                Assert.Contains(blocks, block => block is TextContentBlock text && text.RawText.Contains("Streaming is complete.", StringComparison.Ordinal));
                break;
            case "weather":
                Assert.Contains(blocks, block => block is FunctionInvocationContentBlock function && function.Result is not null);
                break;
            case "approval":
                Assert.Contains(blocks, block => block is ToolApprovalBlock { Status: ApprovalStatus.Approved });
                break;
            case "frontend-action":
                Assert.Contains(blocks, block => block is UIActionBlock action && action.IsComplete);
                break;
            case "predictive":
                Assert.Equal("Synthetic proposed document.", composition.State.Document.Content);
                Assert.Null(composition.State.PendingDocument);
                Assert.Contains(blocks, block => block is UIActionBlock { IsComplete: true });
                Assert.Contains(blocks, block => block is TextContentBlock);
                break;
            case "reasoning":
                Assert.Contains(blocks, block => block is ReasoningContentBlock);
                break;
            case "attachments":
                Assert.Contains(blocks, block => block is MediaContentBlock media && media.Items.Count == 2);
                break;
            case "restore":
                Assert.Contains(blocks, block => block is TextContentBlock);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown direct scenario.");
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static IConfigurationRoot LoadSharedAiConfiguration()
    {
        var userSecretsPath = OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "UserSecrets",
                "ai-attributes-secrets",
                "secrets.json")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".microsoft",
                "usersecrets",
                "ai-attributes-secrets",
                "secrets.json");
        return new ConfigurationBuilder()
            .AddJsonFile(userSecretsPath, optional: false)
            .Build();
    }

    private static string GetLiveDeployment(IConfiguration configuration) =>
        Environment.GetEnvironmentVariable("MAUI_AI_CHAT_DEPLOYMENT")
        ?? configuration["AI:DeploymentName"]
        ?? "gpt-5.4-mini";

    private sealed class DisposableChatClient : IChatClient
    {
        public bool Disposed { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse());
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => Disposed = true;
    }

    private sealed class BlockingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse());

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class RawUpdateChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse { RawRepresentation = new object() });
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                RawRepresentation = new object(),
                Contents = [new TextContent("provider text") { RawRepresentation = new object() }],
            };
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class DataUpdateChatClient(byte[] bytes) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse());

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new DataContent(bytes, "application/octet-stream")],
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class StateEventChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse());
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                RawRepresentation = new StateSnapshotEvent
                {
                    Snapshot = JsonSerializer.SerializeToElement(new { plan = new { steps = Array.Empty<object>() } }),
                },
            };
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                RawRepresentation = new StateDeltaEvent
                {
                    Delta = JsonSerializer.SerializeToElement(new[] { new { op = "replace", path = "/steps/0/status", value = "completed" } }),
                },
            };
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class ProposalChatClient : IChatClient
    {
        private int _call;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse());
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            if (_call++ == 0)
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents =
                    [
                        new FunctionCallContent(
                            "proposal-1",
                            "propose_document",
                            new Dictionary<string, object?>
                            {
                                ["proposal"] = new Dictionary<string, object?>
                                {
                                    ["document"] = new Dictionary<string, object?>
                                    {
                                        ["content"] = "Accepted synthetic document",
                                    },
                                },
                            }),
                    ],
                };
            }

            else
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent("Proposal decision continued.")],
                };
            }
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class ApprovalChatClient : IChatClient
    {
        private int _call;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse());
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            if (_call++ == 0)
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents =
                    [
                        new ToolApprovalRequestContent(
                            "approve-1",
                            new FunctionCallContent("book-1", "book_meeting", new Dictionary<string, object?>())),
                    ],
                };
            }
            else
                yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent("Meeting approved.")] };
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class ConfiguredHttpClientFactory(IConfiguration configuration) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient();
            AguiEndpointConfiguration.Configure(client, configuration);
            return client;
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();
        public object? GetService(Type serviceType) => null;
    }

    private sealed class FakeAttachmentPicker : IChatAttachmentPicker
    {
        public Task<IReadOnlyList<ChatAttachment>> PickAsync(object? fileTypes, long maxBytesPerFile, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatAttachment>>([new ChatAttachment("fake.txt", "text/plain", "test"u8.ToArray())]);
    }

    private sealed class FakeAudioRecorder : IChatAudioRecorder
    {
        public bool IsSupported => true;
        public bool IsRecording { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken = default) { IsRecording = true; return Task.CompletedTask; }
        public Task<ChatAttachment?> StopAsync(long maximumBytes, CancellationToken cancellationToken = default)
        {
            IsRecording = false;
            return Task.FromResult<ChatAttachment?>(new ChatAttachment("audio.wav", "audio/wav", "a"u8.ToArray()));
        }
        public Task CancelAsync(CancellationToken cancellationToken = default) { IsRecording = false; return Task.CompletedTask; }
    }

    private sealed class FakeSpeechRecognizer : IChatSpeechRecognizer
    {
        public event EventHandler<ChatSpeechRecognitionEventArgs>? RecognitionChanged;
        public bool IsSupported => true;
        public bool IsListening { get; private set; }
        public Task<bool> RequestPermissionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task StartAsync(CultureInfo culture, bool reportPartialResults, CancellationToken cancellationToken = default)
        {
            IsListening = true;
            RecognitionChanged?.Invoke(this, new ChatSpeechRecognitionEventArgs(string.Empty, isFinal: false));
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default) { IsListening = false; return Task.CompletedTask; }
    }
}
