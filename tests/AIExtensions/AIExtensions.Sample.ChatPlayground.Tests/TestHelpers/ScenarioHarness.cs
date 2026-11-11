using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

/// <summary>
/// One agentic scenario without MAUI: the router client over a live client (with function invocation, as the app's
/// clients have) and the replay client, the scenario's agent and session, its recording, and the replay driver, wired
/// the way the scenarios tab wires them.
/// </summary>
internal sealed class ScenarioHarness : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"chat-playground-scenarios-{Guid.NewGuid():N}");

    /// <param name="scenario">The scenario to run.</param>
    /// <param name="model">The model behind the live client, or <see langword="null"/> for only Replay.</param>
    /// <param name="capabilities">What the live client can do; by default it calls tools and reasons.</param>
    /// <param name="functionInvocation">Whether the live client invokes tools, as the app's clients do.</param>
    public ScenarioHarness(
        Func<ScenarioHarness, PlaygroundScenario> scenario,
        IChatClient? model = null,
        ScenarioClientCapabilities? capabilities = null,
        bool functionInvocation = true)
    {
        Directory.CreateDirectory(_directory);
        Scenario = scenario(this);
        Capabilities = capabilities ?? new ScenarioClientCapabilities("test-model", SupportsToolCalling: true, SupportsReasoning: true);
        Recording = new ChatSessionService(
            NullLogger<ChatSessionService>.Instance,
            Path.Combine(_directory, "data"),
            Path.Combine(_directory, "cache"),
            "scenario-" + Scenario.Id);
        ReplayRequests = new CapturingChatClient(new ReplayChatClient(Recording));
        Replay = new DescribedChatClient(ReplayRequests, PlaygroundChatHarness.ReplayDescriptor);
        if (model is not null)
            Live = new DescribedChatClient(
                functionInvocation ? new FunctionInvokingChatClient(model) : model,
                PlaygroundChatHarness.LiveDescriptor);

        Settings = new SettingsPaneViewModel(Live is null ? [Replay] : [Live, Replay]);
        Router = new PlaygroundChatClient(
            () => Settings.SelectedClient,
            Replay,
            Settings,
            engineOptions => Scenario.CreateRequestOptions(engineOptions, Capabilities),
            Recording,
            Scenario.PrepareMessages);
        Session = new AgentContext(Scenario.CreateAgent(Router, NullLoggerFactory.Instance));
        Driver = new ChatReplayDriver(Session, Router, Recording);
    }

    /// <summary>Gets a folder that is deleted with the harness.</summary>
    public string TempDirectory => _directory;

    public PlaygroundScenario Scenario { get; }

    public ScenarioClientCapabilities Capabilities { get; }

    public ChatSessionService Recording { get; }

    public CapturingChatClient ReplayRequests { get; }

    public IChatClient Replay { get; }

    public IChatClient? Live { get; }

    public SettingsPaneViewModel Settings { get; }

    public PlaygroundChatClient Router { get; }

    public AgentContext Session { get; private set; }

    public ChatReplayDriver Driver { get; private set; }

    public ConversationTurn LastTurn => Session.Turns[^1];

    /// <summary>Gets the path of a scenario's bundled example in the test output.</summary>
    public static string ExamplePath(string scenarioId) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "scenarios", scenarioId + ".json");

    /// <summary>Starts over with a new agent and session, as reopening the app does.</summary>
    public void Reopen()
    {
        Session.Dispose();
        Session = new AgentContext(Scenario.CreateAgent(Router, NullLoggerFactory.Instance));
        Driver = new ChatReplayDriver(Session, Router, Recording);
    }

    /// <summary>Makes a scenario's bundled example the current recording, as Play example does.</summary>
    public async Task LoadExampleAsync()
    {
        await using var source = File.OpenRead(ExamplePath(Scenario.Id));
        await Recording.LoadFileAsync(source);
    }

    public void Dispose()
    {
        Session.Dispose();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
