using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Coordinates the Agentic tab: the scenario list, the client the scenarios use, and a session per scenario, created
/// the first time it is opened so each keeps its conversation while you switch between them.
/// </summary>
public sealed partial class ScenariosViewModel : ObservableObject
{
    private const string PreferredClientId = "azure-openai-chat";

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ChatSessionService> _recordingLogger;
    private readonly Dictionary<ScenarioItem, ScenarioSessionViewModel> _sessions = [];
    private readonly string _dataDirectory;
    private readonly string _exportDirectory;

    /// <summary>Initializes the scenarios and selects the first one.</summary>
    public ScenariosViewModel(
        IEnumerable<IChatClient> clients,
        ILoggerFactory loggerFactory,
        ILogger<ChatSessionService> recordingLogger,
        DiagnosticsViewModel diagnostics)
    {
        _loggerFactory = loggerFactory;
        _recordingLogger = recordingLogger;
        Diagnostics = diagnostics;
        _dataDirectory = Path.Combine(FileSystem.AppDataDirectory, "scenarios");
        _exportDirectory = Path.Combine(FileSystem.CacheDirectory, "scenarios");

        // The scenarios choose their client apart from the chat tab, and only live clients: each scenario replays its
        // own example. A tool-capable cloud model runs every scenario, so it is preferred.
        Settings = new SettingsPaneViewModel(clients);
        LiveClients = [.. Settings.Clients.Where(static option => !option.Descriptor.IsReplay)];
        SelectedClient = LiveClients.FirstOrDefault(static option => option.Descriptor.Id == PreferredClientId)
            ?? LiveClients.FirstOrDefault();

        var scenarios = ScenarioCatalog.CreateAll(
            new ScenarioTools(TimeSpan.FromSeconds(1.5)),
            new PlanTools(TimeSpan.FromSeconds(1.2)),
            Accent,
            Path.Combine(_dataDirectory, "persistence", "conversation.json"));
        Items = [.. scenarios.Select(static (scenario, index) => new ScenarioItem(scenario, index))];
        SelectedItem = Items[0];
    }

    /// <summary>Gets the client selection the scenarios share.</summary>
    public SettingsPaneViewModel Settings { get; }

    /// <summary>Gets the diagnostics shown by the Logs button.</summary>
    public DiagnosticsViewModel Diagnostics { get; }

    /// <summary>Gets what the frontend-tools scenario recolors.</summary>
    public AccentColorController Accent { get; } = new();

    /// <summary>Gets the live clients the scenarios can use.</summary>
    public IReadOnlyList<ChatClientOption> LiveClients { get; }

    public bool HasLiveClients => LiveClients.Count > 0;

    /// <summary>Gets the scenarios, in sidebar order.</summary>
    public IReadOnlyList<ScenarioItem> Items { get; }

    /// <summary>Gets or sets the client the scenarios use.</summary>
    [ObservableProperty] private ChatClientOption? selectedClient;

    /// <summary>Gets or sets the scenario shown.</summary>
    [ObservableProperty] private ScenarioItem? selectedItem;

    /// <summary>Gets the session of the scenario shown.</summary>
    [ObservableProperty] private ScenarioSessionViewModel? selected;

    partial void OnSelectedClientChanged(ChatClientOption? value)
    {
        if (value is not null)
            Settings.SelectedClient = value.Client;
    }

    partial void OnSelectedItemChanged(ScenarioItem? oldValue, ScenarioItem? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;
        if (newValue is null)
            return;

        newValue.IsSelected = true;
        if (!_sessions.TryGetValue(newValue, out var session))
        {
            session = new ScenarioSessionViewModel(
                newValue.Scenario, Settings, _loggerFactory, _recordingLogger, _dataDirectory, _exportDirectory);
            session.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ScenarioSessionViewModel.IsBusy))
                    OnPropertyChanged(nameof(CanSelectClient));
            };
            _sessions.Add(newValue, session);
        }

        Selected = session;
        OnPropertyChanged(nameof(CanSelectClient));
    }

    /// <summary>Gets whether the client can change: not while the scenario shown is responding.</summary>
    public bool CanSelectClient => HasLiveClients && Selected?.IsBusy != true;
}

/// <summary>A scenario in the sidebar list.</summary>
public sealed partial class ScenarioItem(PlaygroundScenario scenario, int index) : ObservableObject
{
    public PlaygroundScenario Scenario { get; } = scenario;

    public string Title => Scenario.Title;

    public string Glyph => Scenario.Glyph;

    public string Summary => Scenario.Summary;

    public string AutomationId => $"Scenario{index}Item";

    /// <summary>Gets or sets whether the scenario is the one shown.</summary>
    [ObservableProperty] private bool isSelected;
}
