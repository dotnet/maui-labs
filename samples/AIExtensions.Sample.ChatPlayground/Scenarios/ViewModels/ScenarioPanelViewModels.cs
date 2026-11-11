using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Shows the plan scenario's typed state, <see cref="PlanState"/>, beside the chat.</summary>
public sealed partial class PlanPanelViewModel : ObservableObject
{
    private readonly ScenarioSessionViewModel _session;
    private IDisposable? _registration;

    public PlanPanelViewModel(ScenarioSessionViewModel session)
    {
        _session = session;
        session.SessionCreated += (_, _) => Attach();
        Attach();
    }

    [ObservableProperty] private IReadOnlyList<PlanStepRow> steps = [];
    [ObservableProperty] private string progressText = string.Empty;
    [ObservableProperty] private double progress;
    [ObservableProperty] private bool hasPlan;

    private UIAgent<PlanState> Agent => ((PlanProgressScenario)_session.Scenario).PlanAgent;

    private void Attach()
    {
        _registration?.Dispose();
        _registration = Agent.State.OnChanged(() => Show(Agent.State.Value));
        Show(Agent.State.Value);
    }

    private void Show(PlanState plan)
    {
        Steps = [.. plan.Steps.Select(static (step, index) => new PlanStepRow(index + 1, step))];
        HasPlan = plan.Steps.Count > 0;
        Progress = HasPlan ? (double)plan.CompletedCount / plan.Steps.Count : 0;
        ProgressText = !HasPlan ? "No plan yet. Ask the agent to plan something."
            : plan.IsDone ? $"All {plan.Steps.Count} steps done"
            : $"{plan.CompletedCount} of {plan.Steps.Count} steps done";
    }
}

/// <summary>One step of the plan panel.</summary>
public sealed class PlanStepRow(int number, PlanStep step)
{
    public int Number { get; } = number;

    public string Description { get; } = step.Description;

    public bool IsCompleted { get; } = step.IsCompleted;

    public bool IsRunning { get; } = step.IsRunning && !step.IsCompleted;

    public bool IsPending => !IsCompleted && !IsRunning;

    public bool ShowsGlyph => !IsRunning;

    public string Glyph => IsCompleted ? "\u2713" : Number.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public string AutomationId => $"PlanPanelStep{Number}";
}

/// <summary>Edits the shared-state scenario's recipe: the agent's typed state, <see cref="RecipeState"/>.</summary>
/// <remarks>
/// Every edit becomes the agent's state at once, so the next request sends it. A change from the agent replaces the
/// fields, unless it is the edit just made.
/// </remarks>
public sealed partial class RecipeEditorViewModel : ObservableObject
{
    private const string EditedNote = "Edited by you; the next message sends it to the agent.";

    private readonly ScenarioSessionViewModel _session;
    private IDisposable? _registration;
    private bool _loading;

    public RecipeEditorViewModel(ScenarioSessionViewModel session)
    {
        _session = session;
        Preferences = [.. RecipeState.Preferences.Select(name => new RecipePreference(name, Commit))];
        session.SessionCreated += (_, _) => Attach();
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ScenarioSessionViewModel.IsBusy))
                return;
            OnPropertyChanged(nameof(IsEditable));
            if (_session.IsBusy && LastChange == EditedNote)
                LastChange = "Your edits went to the agent with your message.";
        };
        Attach();
    }

    public IReadOnlyList<string> SkillLevels => RecipeState.SkillLevels;

    public IReadOnlyList<string> CookingTimes => RecipeState.CookingTimes;

    public IReadOnlyList<RecipePreference> Preferences { get; }

    public ObservableCollection<RecipeIngredientRow> Ingredients { get; } = [];

    /// <summary>Gets whether the recipe can be edited: not while the agent responds.</summary>
    public bool IsEditable => !_session.IsBusy;

    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string? skillLevel;
    [ObservableProperty] private string? cookingTime;
    [ObservableProperty] private string instructions = string.Empty;
    [ObservableProperty] private string lastChange = "The recipe the conversation starts with.";

    private UIAgent<RecipeState> Agent => ((SharedStateScenario)_session.Scenario).RecipeAgent;

    public IRelayCommand AddIngredientAction => AddIngredientCommand;
    public IRelayCommand<RecipeIngredientRow?> RemoveIngredientAction => RemoveIngredientCommand;

    [RelayCommand]
    private void AddIngredient()
    {
        Ingredients.Add(new RecipeIngredientRow(new RecipeIngredient { Icon = "\U0001F944" }, Commit));
        Commit();
    }

    [RelayCommand]
    private void RemoveIngredient(RecipeIngredientRow? row)
    {
        if (row is not null && Ingredients.Remove(row))
            Commit();
    }

    partial void OnTitleChanged(string value) => Commit();

    partial void OnSkillLevelChanged(string? value) => Commit();

    partial void OnCookingTimeChanged(string? value) => Commit();

    partial void OnInstructionsChanged(string value) => Commit();

    private void Attach()
    {
        _registration?.Dispose();
        _registration = Agent.State.OnChanged(() =>
        {
            if (_loading)
                return;
            Load(Agent.State.Value);
            LastChange = _session.IsBusy ? "Updated by the agent." : "Reset to the recipe the conversation starts with.";
        });
        Load(Agent.State.Value);
    }

    private void Load(RecipeState recipe)
    {
        _loading = true;
        try
        {
            Title = recipe.Title;
            SkillLevel = recipe.SkillLevel;
            CookingTime = recipe.CookingTime;
            Instructions = string.Join('\n', recipe.Instructions);
            foreach (var preference in Preferences)
            {
                preference.IsSelected = recipe.SpecialPreferences.Contains(preference.Name, StringComparer.OrdinalIgnoreCase);
            }
            Ingredients.Clear();
            foreach (var ingredient in recipe.Ingredients)
                Ingredients.Add(new RecipeIngredientRow(ingredient, Commit));
        }
        finally
        {
            _loading = false;
        }
    }

    private void Commit()
    {
        if (_loading)
            return;

        _loading = true;
        try
        {
            Agent.State.Value = new RecipeState
            {
                Title = Title.Trim(),
                SkillLevel = SkillLevel ?? RecipeState.SkillLevels[0],
                CookingTime = CookingTime ?? RecipeState.CookingTimes[1],
                SpecialPreferences = [.. Preferences.Where(static preference => preference.IsSelected)
                    .Select(static preference => preference.Name)],
                Ingredients = [.. Ingredients.Select(static row => row.ToIngredient())
                    .Where(static ingredient => ingredient.Name.Length > 0 || ingredient.Amount.Length > 0)],
                Instructions = [.. Instructions.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            };
            LastChange = EditedNote;
        }
        finally
        {
            _loading = false;
        }
    }
}

/// <summary>A dietary preference in the recipe editor.</summary>
public sealed partial class RecipePreference(string name, Action changed) : ObservableObject
{
    public string Name { get; } = name;

    public string AutomationId => "RecipePreference" + Name.Replace("-", string.Empty, StringComparison.Ordinal);

    [ObservableProperty] private bool isSelected;

    partial void OnIsSelectedChanged(bool value) => changed();
}

/// <summary>An ingredient in the recipe editor.</summary>
public sealed partial class RecipeIngredientRow : ObservableObject
{
    private readonly Action _changed;

    public RecipeIngredientRow(RecipeIngredient ingredient, Action changed)
    {
        icon = ingredient.Icon;
        name = ingredient.Name;
        amount = ingredient.Amount;
        _changed = changed;
    }

    [ObservableProperty] private string icon;
    [ObservableProperty] private string name;
    [ObservableProperty] private string amount;

    public RecipeIngredient ToIngredient() => new() { Icon = Icon.Trim(), Name = Name.Trim(), Amount = Amount.Trim() };

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        _changed();
    }
}

/// <summary>
/// Shows the predictive-state scenario's document: editable while the agent is idle, and while a proposal waits for
/// review, the proposal with its changes marked against the document the turn started with.
/// </summary>
public sealed partial class DocumentPanelViewModel : ObservableObject
{
    private readonly ScenarioSessionViewModel _session;
    private IDisposable? _registration;

    // The document before the proposal: a proposal is a predictive state, so the last document that was not one.
    private string _baseline = string.Empty;
    private bool _loading;

    public DocumentPanelViewModel(ScenarioSessionViewModel session)
    {
        _session = session;
        session.SessionCreated += (_, _) => Attach();
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScenarioSessionViewModel.IsBusy))
            {
                OnPropertyChanged(nameof(IsEditable));
                OnPropertyChanged(nameof(IsReadOnly));
            }
        };
        Attach();
    }

    [ObservableProperty] private string documentText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable), nameof(IsReadOnly), nameof(IsEditorVisible))]
    private bool isPending;

    [ObservableProperty] private IReadOnlyList<DiffSegment> diff = [];
    [ObservableProperty] private string changeSummary = string.Empty;
    [ObservableProperty] private string wordCount = "Empty";

    /// <summary>Gets whether the document can be edited: not while the agent responds or a proposal waits.</summary>
    public bool IsEditable => !_session.IsBusy && !IsPending;

    public bool IsReadOnly => !IsEditable;

    /// <summary>Gets whether the editor shows, rather than the proposal with its changes.</summary>
    public bool IsEditorVisible => !IsPending;

    private UIAgent<DocumentState> Agent => ((PredictiveStateScenario)_session.Scenario).DocumentAgent;

    partial void OnDocumentTextChanged(string value)
    {
        WordCount = CountWords(value);
        if (_loading || IsPending)
            return;

        // The user's edit becomes the agent's state, so the next request sends it.
        _loading = true;
        try
        {
            Agent.State.Value = new DocumentState { Document = value };
        }
        finally
        {
            _loading = false;
        }
    }

    private void Attach()
    {
        _registration?.Dispose();
        _registration = Agent.State.OnChanged(Show);
        _baseline = Agent.State.Value.Document;
        Show();
    }

    private void Show()
    {
        if (_loading)
            return;

        var document = Agent.State.Value.Document;
        IsPending = Agent.State.HasPendingPredictiveState;
        _loading = true;
        try
        {
            DocumentText = document;
        }
        finally
        {
            _loading = false;
        }

        if (!IsPending)
        {
            _baseline = document;
            Diff = [];
            ChangeSummary = string.Empty;
            return;
        }

        Diff = DocumentDiff.Compute(_baseline, document);
        var added = CountWords(Diff.Where(static segment => segment.Kind == DiffKind.Added));
        var removed = CountWords(Diff.Where(static segment => segment.Kind == DiffKind.Removed));
        ChangeSummary = $"Proposed: +{added} {(added == 1 ? "word" : "words")}, \u2212{removed} " +
            $"{(removed == 1 ? "word" : "words")}. Accept or reject it in the chat.";
    }

    private static int CountWords(IEnumerable<DiffSegment> segments) =>
        segments.Sum(static segment => segment.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);

    private static string CountWords(string text)
    {
        var count = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return count == 0 ? "Empty" : $"{count} {(count == 1 ? "word" : "words")}";
    }
}

/// <summary>Shows where the persistence scenario saves its conversation, and reads it again.</summary>
public sealed partial class PersistencePanelViewModel : ObservableObject
{
    private readonly ScenarioSessionViewModel _session;
    private FileConversationThread? _thread;

    public PersistencePanelViewModel(ScenarioSessionViewModel session)
    {
        _session = session;
        session.SessionCreated += (_, _) => Attach();
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScenarioSessionViewModel.IsBusy))
                ReloadCommand.NotifyCanExecuteChanged();
        };
        Attach();
    }

    [ObservableProperty] private string filePath = string.Empty;
    [ObservableProperty] private string summary = string.Empty;

    private PersistenceScenario Scenario => (PersistenceScenario)_session.Scenario;

    private bool CanReload() => !_session.IsBusy;

    public IAsyncRelayCommand ReloadAction => ReloadCommand;

    [RelayCommand(CanExecute = nameof(CanReload))]
    private Task ReloadAsync() => _session.ReloadAsync();

    private void Attach()
    {
        if (_thread is not null)
            _thread.Saved -= ThreadSaved;
        _thread = Scenario.Thread;
        if (_thread is not null)
            _thread.Saved += ThreadSaved;
        FilePath = DescribePath(Scenario.ThreadPath);
        Refresh();
    }

    private void ThreadSaved(object? sender, EventArgs e) => Refresh();

    // The app's data folder is a long, per-install path; the part inside it is what tells the file apart.
    private static string DescribePath(string path)
    {
        var relative = Path.GetRelativePath(FileSystem.AppDataDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? path
            : $"App data \u203A {relative.Replace(Path.DirectorySeparatorChar, '/')}";
    }

    private void Refresh()
    {
        if (_thread is not { } thread || thread.SavedAt is not { } savedAt)
        {
            Summary = "Nothing saved yet.";
            return;
        }

        var turns = thread.TurnCount;
        Summary = $"{turns} {(turns == 1 ? "turn" : "turns")} \u00B7 {thread.FileSize / 1024.0:0.#} KB \u00B7 " +
            $"saved {savedAt.ToLocalTime():t}";
    }
}
