using System.ClientModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AIChat.ClientServer.Sample.Shared;
using AIChat.Sample.Shared;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Presentation;
using Microsoft.Maui.AI.Chat.Recording;
using Microsoft.Maui.Chat;

namespace AIChat.Client.Sample;

/// <summary>Composition root for the one session shared by the native and Razor controls.</summary>
public sealed class DirectChatComposition : INotifyPropertyChanged, IDisposable
{
    private readonly SampleSessionHost _host;
    private readonly SampleUiState _state = new();
    private ScenarioDescriptor _selectedScenario;

    public DirectChatComposition(IConfiguration configuration, IServiceProvider services)
        : this(configuration, services, clientFactory: null)
    {
    }

    internal DirectChatComposition(
        IConfiguration configuration,
        IServiceProvider services,
        Func<string, ChatSampleMode, IChatClient>? clientFactory)
    {
        var mode = ChatSampleModeParser.Parse(configuration);
        var fixtureDestination = configuration["AI:Chat:FixtureDestination"];
        _selectedScenario = Scenarios[0];
        _host = new SampleSessionHost(
            mode,
            (scenario, selectedMode) => clientFactory?.Invoke(scenario, selectedMode) ??
                CreateClient(configuration, services, scenario, selectedMode, fixtureDestination),
            ConfigureAgent,
            SampleRecordingStore.SaveAtomic,
            scenario => RecordingDestination.ForScenario(fixtureDestination, scenario),
            services.GetService<IChatAttachmentPicker>(),
            services.GetService<IChatAudioRecorder>(),
            services.GetService<IChatSpeechRecognizer>(),
            _state);
        _host.Changed += OnHostChanged;
        _host.State.Changed += OnStateChanged;
    }

    public ReadOnlyCollection<ScenarioDescriptor> Scenarios { get; } = new(
    [
        new("basic", "Basic streaming"),
        new("weather", "Backend weather tool/card"),
        new("approval", "Meeting approval"),
        new("frontend-action", "Automatic frontend UI action"),
        new("predictive", "Manual predictive document"),
        new("reasoning", "Reasoning/activity"),
        new("attachments", "Attachments/images"),
        new("restore", "Stop/retry/thread restore"),
    ]);
    public ScenarioDescriptor SelectedScenario
    {
        get => _selectedScenario;
        set
        {
            if (value is null || string.Equals(value.Id, _selectedScenario.Id, StringComparison.Ordinal))
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
    public string StatusText => $"{_host.Mode}: {_host.Status}";
    public ChatSampleMode Mode => _host.Mode;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Clear() => _host.Clear();
    public Task RetryAsync() => _host.RetryAsync();
    public void AssertReplayFullyConsumed() => _host.AssertReplayFullyConsumed();
    public Task ResolveDocumentProposalAsync(bool accepted) =>
        _host.ResolveDocumentProposalAsync(accepted);

    public void Dispose()
    {
        _host.Changed -= OnHostChanged;
        _host.State.Changed -= OnStateChanged;
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

    private static IChatClient CreateClient(
        IConfiguration configuration,
        IServiceProvider services,
        string scenario,
        ChatSampleMode mode,
        string? fixtureDestination)
    {
        if (mode == ChatSampleMode.Replay)
            return new ReplayChatClient(new ChatRecordingOptions
            {
                Recording = DirectScenarioReplayFixtures.Create(scenario),
                StrictSanitizer = true,
            });

        var endpoint = configuration["AI:Endpoint"];
        var apiKey = configuration["AI:ApiKey"];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Direct mode requires AI:Endpoint and AI:ApiKey from user secrets or environment variables. Never add credentials to appsettings.json.");
        }

        var deployment = configuration["AI:DeploymentName"]
            ?? configuration["AI:Deployment"]
            ?? "gpt-5.4-mini";
        var azureClient = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));
        var provider = scenario == "reasoning"
            ? azureClient.GetResponsesClient().AsIChatClient(deployment)
            : azureClient.GetChatClient(deployment).AsIChatClient();
        var providerBuilder = provider.AsBuilder();
        var imageDeployment = configuration["AI:ImageDeploymentName"];
        if (!string.IsNullOrWhiteSpace(imageDeployment))
            providerBuilder.UseImageGeneration(azureClient.GetImageClient(imageDeployment).AsIImageGenerator());
        provider = providerBuilder
            .UseFunctionInvocation()
            .Build(services);

        if (mode != ChatSampleMode.Record)
            return provider;

        if (string.IsNullOrWhiteSpace(fixtureDestination))
        {
            provider.Dispose();
            throw new InvalidOperationException(
                "Record mode requires an explicit AI:Chat:FixtureDestination outside the source tree.");
        }

        return new RecordingChatClient(new RecordingRawNormalizerChatClient(provider), new ChatRecordingOptions
        {
            Mode = ChatRecordingMode.Record,
            FixturePath = fixtureDestination,
            StrictSanitizer = true,
            Adapter = "direct-azure-openai",
        });
    }

    private void ConfigureAgent(UIAgentOptions options, string scenario)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
                (string location) => $"Sunny and 20°C in {location}.",
                "get_weather",
                "Get current weather."),
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
                (string title) => $"Meeting '{title}' has been booked.",
                "book_meeting",
                "Book a meeting only after approval.")),
        };
        options.ChatOptions = new ChatOptions
        {
            Instructions = $"You are a concise MAUI demo assistant. Active scenario: {scenario}.",
            Tools = tools,
            AllowMultipleToolCalls = PredictiveToolCallPolicy.ForDirect(scenario),
            Reasoning = scenario == "reasoning"
                ? new ReasoningOptions { Output = ReasoningOutput.Full }
                : null,
        };
        options.RegisterUIAction(AIFunctionFactory.Create(
            (string conditions) =>
            {
                State.TryApplySnapshot(System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    weather = new { temperature = 20, conditions, humidity = 50, wind_speed = 10 },
                }));
                return "Weather card shown.";
            },
            "show_weather_card",
            "Show a weather card in the local client."));
        options.RegisterUIAction(AIFunctionFactory.Create(
            (DocumentProposal proposal, bool? accepted = null) =>
            {
                if (accepted is null)
                    return "Document proposal awaits a user decision.";
                return accepted.Value
                    ? "Document proposal was accepted."
                    : "Document proposal was rejected.";
            },
            "propose_document",
            "Propose a document edit for explicit user approval."),
            UIActionInvocationMode.Manual);
    }
}
