using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using AGUI.Abstractions;
using AGUI.Client;
using AIChat.ClientServer.Sample.Shared;
using AIChat.Sample.Shared;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Presentation;
using Microsoft.Maui.AI.Chat.Recording;
using Microsoft.Maui.Chat;

namespace AIChat.ClientServer.Sample.Client;

/// <summary>One AG-UI session composition. A scenario change atomically replaces and disposes its trio.</summary>
public sealed class AguiChatComposition : INotifyPropertyChanged, IDisposable
{
    private readonly SampleSessionHost _host;
    private readonly SampleUiState _state = new();
    private readonly IHttpClientFactory _httpClients;
    private readonly ChatSampleMode _mode;
    private readonly string? _fixtureDestination;
    private ScenarioDescriptor _selectedScenario;

    public AguiChatComposition(IConfiguration configuration, IHttpClientFactory httpClients, IServiceProvider services)
        : this(configuration, httpClients, services, clientFactory: null)
    {
    }

    internal AguiChatComposition(
        IConfiguration configuration,
        IHttpClientFactory httpClients,
        IServiceProvider services,
        Func<string, ChatSampleMode, IChatClient>? clientFactory)
    {
        _httpClients = httpClients;
        _mode = ChatSampleModeParser.Parse(configuration);
        _fixtureDestination = configuration["AI:Chat:FixtureDestination"];
        _selectedScenario = ScenarioIds.All[0];
        _host = new SampleSessionHost(
            _mode,
            clientFactory ?? CreateClient,
            ConfigureAgent,
            SampleRecordingStore.SaveAtomic,
            scenario => RecordingDestination.ForScenario(_fixtureDestination, scenario),
            services.GetService<IChatAttachmentPicker>(),
            services.GetService<IChatAudioRecorder>(),
            services.GetService<IChatSpeechRecognizer>(),
            _state,
            initialScenario: _selectedScenario.Id);
        _host.Changed += OnHostChanged;
        State.Changed += OnStateChanged;
    }

    internal sealed class AguiStateEventHandler(SampleUiState state)
        : ContentBlockHandler<AguiStateEventHandler.State>
    {
        public override BlockMappingResult<State> Handle(BlockMappingContext context, State handlerState)
        {
            switch (context.Update.RawRepresentation)
            {
                case StateSnapshotEvent snapshot:
                    if (state.TryApplySnapshot(snapshot.Snapshot))
                        context.MarkUpdateHandled();
                    break;
                case StateDeltaEvent delta:
                    if (state.TryApplyDelta(delta.Delta))
                        context.MarkUpdateHandled();
                    break;
            }
            return BlockMappingResult<State>.Pass();
        }

        public sealed class State;
    }

    public ReadOnlyCollection<ScenarioDescriptor> Scenarios { get; } =
        new(ScenarioIds.All.ToList());
    public ScenarioDescriptor SelectedScenario
    {
        get => _selectedScenario;
        set
        {
            if (value is null || string.Equals(_selectedScenario.Id, value.Id, StringComparison.Ordinal))
                return;
            if (!_host.ReplaceSession(value.Id))
            {
                OnPropertyChanged(nameof(SelectedScenario));
                OnPropertyChanged(nameof(StatusText));
                return;
            }
            _selectedScenario = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Session));
            OnPropertyChanged(nameof(Presentation));
            OnPropertyChanged(nameof(ComposerController));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public AgentContext Session => _host.Session;
    public AgentChatPresentation Presentation => _host.Presentation;
    public ChatComposerController ComposerController => _host.ComposerController;
    public SampleUiState State => _state;
    public bool HasPendingDocument => State.HasPendingDocument;
    public string StatusText => $"{_mode}: {_host.Status}";
    public ChatSampleMode Mode => _mode;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Clear() => _host.Clear();
    public Task RetryAsync() => _host.RetryAsync();
    public void AssertReplayFullyConsumed() => _host.AssertReplayFullyConsumed();
    public Task ResolveDocumentProposalAsync(bool accepted) =>
        _host.ResolveDocumentProposalAsync(accepted);
    public void Dispose()
    {
        _host.Changed -= OnHostChanged;
        State.Changed -= OnStateChanged;
        _host.Dispose();
    }

    private void OnHostChanged() => OnPropertyChanged(nameof(StatusText));
    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(HasPendingDocument));
    }
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private IChatClient CreateClient(string scenarioId, ChatSampleMode mode)
    {
        if (mode == ChatSampleMode.Replay)
        {
            var replayOptions = new ChatRecordingOptions
            {
                Recording = AguiScenarioReplayFixtures.Create(scenarioId),
                StrictSanitizer = true,
                AllowAguiThreadId = true,
            };
            replayOptions.RequestCodecs.Add(new AguiRunRequestCodec());
            replayOptions.RawCodecs.Add(new AguiStateEventRecordingCodec());
            return new ReplayChatClient(replayOptions);
        }

        IChatClient client = new AGUIChatClient(new AGUIChatClientOptions(
            _httpClients.CreateClient(AguiEndpointConfiguration.HttpClientName),
            AguiEndpointConfiguration.GetScenarioPath(scenarioId)));
        if (mode != ChatSampleMode.Record)
            return client;

        if (string.IsNullOrWhiteSpace(_fixtureDestination))
        {
            client.Dispose();
            throw new InvalidOperationException(
                "Record mode requires an explicit AI:Chat:FixtureDestination outside the source tree.");
        }

        var options = new ChatRecordingOptions
        {
            Mode = ChatRecordingMode.Record,
            StrictSanitizer = true,
            Adapter = "agui",
            AllowAguiThreadId = true,
        };
        options.RequestCodecs.Add(new AguiRunRequestCodec());
        options.RawCodecs.Add(new AguiStateEventRecordingCodec());
        return new RecordingChatClient(client, options);
    }

    private void ConfigureAgent(UIAgentOptions options, string scenario)
    {
        var threadId = $"maui-{Guid.NewGuid():N}";
        options.ChatOptions = new ChatOptions
        {
            AllowMultipleToolCalls = PredictiveToolCallPolicy.ForAgui(scenario),
            RawRepresentationFactory = _ => new RunAgentInput
            {
                ThreadId = threadId,
                RunId = Guid.NewGuid().ToString("N"),
                State = JsonSerializer.SerializeToElement(
                    new ClientAgentState
                    {
                        Plan = State.Plan,
                        Recipe = State.Recipe,
                        Document = State.Document,
                    },
                    SampleSerializerContext.Default.ClientAgentState),
            },
        };
        options.AddBlockHandler(new AguiStateEventHandler(State));
        options.RegisterUIAction(AIFunctionFactory.Create(
            (string conditions) =>
            {
                State.TryApplySnapshot(JsonSerializer.SerializeToElement(new
                {
                    weather = new { temperature = 20, conditions, humidity = 50, wind_speed = 10 },
                }));
                return "Weather card shown.";
            },
            "show_weather_card",
            "Show a weather card in the client."));
        options.RegisterUIAction(AIFunctionFactory.Create(
            (DocumentProposal proposal, bool? accepted = null) =>
            {
                if (accepted is null)
                    return "Document proposal awaits a user decision.";
                return accepted.Value ? "Document proposal accepted." : "Document proposal rejected.";
            },
            "propose_document",
            "Present a document proposal for explicit client approval."),
            UIActionInvocationMode.Manual);
        options.RegisterUIAction(AIFunctionFactory.Create(
            (Plan plan) =>
            {
                State.TryApplySnapshot(JsonSerializer.SerializeToElement(new { plan }));
                return "Plan rendered.";
            },
            "render_plan",
            "Render plan progress in the client."));
    }
}

/// <summary>Canonical recording seam for AG-UI raw requests; it never serializes arbitrary objects.</summary>
public sealed class AguiRunRequestCodec : IChatRecordingRequestCodec
{
    public bool CanWrite(ChatOptions options) =>
        options.RawRepresentationFactory?.Invoke(SentinelChatClient.Instance) is RunAgentInput;

    public JsonObject Write(ChatOptions options)
    {
        var input = options.RawRepresentationFactory?.Invoke(SentinelChatClient.Instance) as RunAgentInput
            ?? throw new InvalidOperationException("The AG-UI request factory did not return RunAgentInput.");
        return new JsonObject
        {
            ["kind"] = "agui-run-agent-input-v1",
            ["state"] = CanonicalState(input.State),
        };
    }

    public bool CanRead(JsonObject extension) =>
        extension["kind"]?.GetValue<string>() == "agui-run-agent-input-v1";

    public JsonObject Read(JsonObject extension)
    {
        if (!CanRead(extension) || extension["state"] is not JsonNode state)
            throw new InvalidDataException("Invalid AG-UI recording request extension.");
        return new JsonObject { ["kind"] = "agui-run-agent-input-v1", ["state"] = CanonicalState(JsonSerializer.SerializeToElement(state)) };
    }

    private static JsonObject CanonicalState(JsonElement? state)
    {
        if (state is not { ValueKind: JsonValueKind.Object })
            throw new InvalidDataException("AG-UI recording state must be a JSON object.");

        var source = state.Value;
        var result = new JsonObject();
        foreach (var name in new[] { "plan", "recipe", "document" })
        {
            if (source.TryGetProperty(name, out var value))
                result[name] = JsonNode.Parse(value.GetRawText());
        }

        if (source.EnumerateObject().Any(property => property.Name is not ("plan" or "recipe" or "document")))
            throw new InvalidDataException("AG-UI recording state contains an unsupported property.");
        return result;
    }
}

/// <summary>Strict JSON-only persistence for AG-UI state events consumed by the sample state handler.</summary>
public sealed class AguiStateEventRecordingCodec : IChatRecordingRawCodec
{
    private static readonly string SnapshotType = typeof(StateSnapshotEvent).FullName!;
    private static readonly string DeltaType = typeof(StateDeltaEvent).FullName!;

    public bool CanWrite(object value) => value is StateSnapshotEvent or StateDeltaEvent;

    public JsonNode? Write(object value) => value switch
    {
        StateSnapshotEvent snapshot => new JsonObject
        {
            ["kind"] = "agui-state-snapshot-v1",
            ["snapshot"] = JsonNode.Parse(snapshot.Snapshot.GetRawText()),
        },
        StateDeltaEvent delta => new JsonObject
        {
            ["kind"] = "agui-state-delta-v1",
            ["delta"] = JsonNode.Parse(delta.Delta.GetRawText()),
        },
        _ => throw new InvalidDataException("Unsupported AG-UI raw event."),
    };

    public bool CanRead(string type) => type is not null && (type == SnapshotType || type == DeltaType);

    public object? Read(string type, JsonNode? value)
    {
        var envelope = value as JsonObject ?? throw new InvalidDataException("AG-UI raw event must be an object.");
        return type switch
        {
            var snapshotType when snapshotType == SnapshotType
                && envelope["kind"]?.GetValue<string>() == "agui-state-snapshot-v1"
                && envelope["snapshot"] is JsonNode snapshot =>
                new StateSnapshotEvent { Snapshot = ToElement(snapshot) },
            var deltaType when deltaType == DeltaType
                && envelope["kind"]?.GetValue<string>() == "agui-state-delta-v1"
                && envelope["delta"] is JsonNode delta =>
                new StateDeltaEvent { Delta = ToElement(delta) },
            _ => throw new InvalidDataException("Invalid AG-UI raw event envelope."),
        };
    }

    private static JsonElement ToElement(JsonNode node) =>
        JsonSerializer.SerializeToElement(node);
}

/// <summary>Creates scenario-selected synthetic schema-v1 AG-UI recordings for deterministic replay.</summary>
public static class AguiScenarioReplayFixtures
{
    public static ChatRecording Create(string scenarioId)
    {
        _ = AguiEndpointConfiguration.GetScenarioPath(scenarioId);
        var recording = new ChatRecording();
        recording.Metadata["fixture"] = "synthetic-agui-semantic-v1";
        recording.Metadata["scenario"] = scenarioId;
        recording.Metadata["description"] = "Deterministic semantic AG-UI replay fixture; it does not represent a service response.";

        switch (scenarioId)
        {
            case ScenarioIds.AgenticChat:
                Add(recording, scenarioId, Text("Hello"), Text(" from AG-UI."), Text(" Streaming is complete."));
                break;
            case ScenarioIds.BackendToolRendering:
                Add(recording, scenarioId,
                    FunctionCall("get_weather", """{"location":"Seattle"}"""),
                    FunctionResult("get_weather-1", "Sunny in Seattle"),
                    Text("Seattle is sunny."));
                break;
            case ScenarioIds.FrontendTools:
                Add(recording, scenarioId, FunctionCall("show_weather_card", """{"conditions":"Sunny"}"""));
                Add(recording, $"{scenarioId}-continuation", Text("The weather card is displayed."));
                break;
            case ScenarioIds.HumanInTheLoop:
                Add(recording, scenarioId, ApprovalRequest("meeting-1", "book_meeting"));
                Add(recording, $"{scenarioId}-continuation", Text("The meeting approval decision was recorded."));
                break;
            case ScenarioIds.ToolBasedGenerativeUi:
                Add(recording, scenarioId, FunctionCall("render_plan", """{"plan":{"steps":[{"description":"Draft card","status":"Pending"}]}}"""));
                Add(recording, $"{scenarioId}-continuation", Text("The generative UI card is rendered."));
                break;
            case ScenarioIds.AgenticGenerativeUi:
                Add(recording, scenarioId,
                    StateSnapshot("""{"plan":{"steps":[{"description":"Research","status":"Pending"}]}}"""),
                    StateDelta("""[{"op":"replace","path":"/steps/0/status","value":"completed"}]"""),
                    Text("The plan is complete."));
                break;
            case ScenarioIds.SharedState:
                Add(recording, scenarioId,
                    StateSnapshot("""{"recipe":{"title":"Synthetic soup","ingredients":[],"instructions":[]}}"""),
                    Text("The shared recipe state is ready."));
                break;
            case ScenarioIds.PredictiveState:
                Add(recording, scenarioId,
                    StateSnapshot("""{"proposal":{"document":{"content":"Synthetic proposed document."}}}"""),
                    FunctionCall("propose_document", """{"proposal":{"document":{"content":"Synthetic proposed document."}}}"""));
                Add(recording, $"{scenarioId}-continuation", Text("The document decision has been applied."));
                break;
            case ScenarioIds.Reasoning:
                Add(recording, scenarioId, Reasoning("I compared the available options."), Text("Here is the concise answer."));
                break;
            case ScenarioIds.Workflow:
                Add(recording, scenarioId, Text("Author selected a topic."), Text("Researcher found the evidence."), Text("Reporter summarized the result."));
                break;
            case ScenarioIds.SelectiveApproval:
                Add(recording, scenarioId,
                    FunctionCall("get_account_balance", "{}"),
                    FunctionResult("get_account_balance-1", "$1,250.00"),
                    ApprovalRequest("transfer-1", "transfer_funds"));
                Add(recording, $"{scenarioId}-continuation", Text("The transfer approval decision was recorded."));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenarioId), scenarioId, "Unknown AG-UI replay scenario.");
        }

        return recording;
    }

    private static void Add(ChatRecording recording, string name, params JsonObject[] updates)
    {
        recording.Interactions.Add(new RecordedChatInteraction
        {
            Sequence = recording.Interactions.Count,
            Name = name,
            Legacy = true,
            Request = new JsonObject { ["fixture"] = "synthetic-agui-semantic-v1" },
            Updates = updates.Select((value, sequence) => new RecordedChatUpdate
            {
                Sequence = sequence,
                Value = value,
            }).ToList(),
        });
    }

    private static JsonObject Text(string text) => Update(new JsonObject
    {
        ["type"] = "text",
        ["text"] = text,
    });

    private static JsonObject Reasoning(string text) => Update(new JsonObject
    {
        ["type"] = "reasoning",
        ["text"] = text,
    });

    private static JsonObject FunctionCall(string name, string arguments) => Update(new JsonObject
    {
        ["type"] = "functionCall",
        ["callId"] = $"{name}-1",
        ["name"] = name,
        ["arguments"] = JsonNode.Parse(arguments),
        ["informationalOnly"] = false,
    });

    private static JsonObject FunctionResult(string callId, object result) => Update(new JsonObject
    {
        ["type"] = "functionResult",
        ["callId"] = callId,
        ["result"] = JsonSerializer.SerializeToNode(result),
    });

    private static JsonObject ApprovalRequest(string callId, string name) => Update(new JsonObject
    {
        ["type"] = "toolApprovalRequest",
        ["requestId"] = $"{callId}-approval",
        ["toolCall"] = new JsonObject
        {
            ["type"] = "functionCall",
            ["callId"] = callId,
            ["name"] = name,
            ["arguments"] = name == "transfer_funds"
                ? JsonNode.Parse("""{"toAccount":"Savings","amount":25}""")
                : JsonNode.Parse("""{"title":"Synthetic meeting","time":"tomorrow"}"""),
            ["informationalOnly"] = false,
        },
    });

    private static JsonObject StateSnapshot(string snapshot) =>
        Update(content: null, raw: Raw(typeof(StateSnapshotEvent).FullName!, "agui-state-snapshot-v1", "snapshot", snapshot));

    private static JsonObject StateDelta(string delta) =>
        Update(content: null, raw: Raw(typeof(StateDeltaEvent).FullName!, "agui-state-delta-v1", "delta", delta));

    private static JsonObject Raw(string type, string kind, string property, string value) => new()
    {
        ["type"] = type,
        ["value"] = new JsonObject
        {
            ["kind"] = kind,
            [property] = JsonNode.Parse(value),
        },
    };

    private static JsonObject Update(JsonObject? content = null, JsonObject? raw = null) => new()
    {
        ["role"] = "assistant",
        ["contents"] = content is null ? new JsonArray() : new JsonArray(content),
        ["raw"] = raw,
    };
}

internal sealed class SentinelChatClient : IChatClient
{
    public static SentinelChatClient Instance { get; } = new();
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
