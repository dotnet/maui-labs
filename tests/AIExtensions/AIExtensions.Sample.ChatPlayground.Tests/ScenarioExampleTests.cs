using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

/// <summary>
/// Replays each scenario's bundled example, as its Example button does: through the engine's live streaming path, with
/// the scenario's own handlers, UI actions and state, and no model.
/// </summary>
public sealed class ScenarioExampleTests
{
    private static readonly ScenarioTools Tools = new(TimeSpan.Zero);

    [Theory]
    [InlineData("tool-cards", 2)]
    [InlineData("combined-tool-calls", 1)]
    [InlineData("human-in-the-loop", 2)]
    [InlineData("frontend-tools", 3)]
    [InlineData("plan-progress", 1)]
    [InlineData("shared-state", 3)]
    [InlineData("predictive-state", 2)]
    [InlineData("reasoning", 2)]
    [InlineData("persistence", 2)]
    public async Task Example_ReplaysEveryTurnThroughTheLivePath(string scenarioId, int turns)
    {
        using var harness = new ScenarioHarness(h => Create(scenarioId, h, new RecordingAccent()));
        await harness.LoadExampleAsync();

        var result = await harness.Driver.ReplayAsync(playAll: true);

        Assert.Equal(turns, result.Turns);
        Assert.Equal(0, result.RestoredHistory);
        Assert.Equal(0, result.RestoredInteractions);
        Assert.Empty(result.UnansweredApprovals);
        Assert.False(harness.Recording.HasReplayRemaining);
        Assert.Equal(turns, harness.Session.Turns.Count);
        Assert.All(harness.Session.Turns, static turn =>
        {
            Assert.False(turn.IsStopped);
            Assert.NotEmpty(AgenticScenarioTests.TextOf(turn));
        });
        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
    }

    [Fact]
    public void Examples_CoverEveryScenario()
    {
        var scenarios = ScenarioCatalog.CreateAll(Tools, new PlanTools(TimeSpan.Zero), new RecordingAccent(), "unused.json");

        Assert.All(scenarios, static scenario => Assert.True(File.Exists(ScenarioHarness.ExamplePath(scenario.Id)),
            $"{scenario.Id} has no bundled example."));
    }

    [Fact]
    public async Task ToolCards_ReplayFillsTheTypedCardsFromTheCallAndItsResult()
    {
        using var harness = new ScenarioHarness(_ => new ToolCardsScenario(Tools));
        var states = new List<string>();
        harness.Session.RegisterOnBlockAdded((_, block) =>
        {
            if (block is not WeatherToolBlock weather)
                return;
            Record();
            weather.OnChanged(Record);

            void Record()
            {
                var state = weather.HasResult ? $"{weather.City}: {weather.Temperature}" : $"{weather.City}: checking";
                if (states.Count == 0 || states[^1] != state)
                    states.Add(state);
            }
        });
        await harness.LoadExampleAsync();

        await harness.Driver.ReplayAsync(playAll: true);

        // Each card showed its city while the tool ran, and its weather once the result arrived.
        Assert.Equal(["Seattle: checking", "Seattle: 12", "Tokyo: checking", "Tokyo: 24"], states);
        var seattle = Assert.Single(harness.Session.Turns[0].ResponseBlocks.OfType<WeatherToolBlock>());
        Assert.Equal("Foggy", seattle.Conditions);
        var tokyo = harness.Session.Turns[1].ResponseBlocks;
        var time = Assert.Single(tokyo.OfType<FunctionInvocationContentBlock>(), static block => block is not WeatherToolBlock);
        Assert.Equal("get_local_time", time.ToolName);
        Assert.True(time.HasResult);
        Assert.Equal("Light rain", Assert.Single(tokyo.OfType<WeatherToolBlock>()).Conditions);
    }

    [Fact]
    public async Task CombinedToolCalls_ReplayFillsOneCardRowByRow()
    {
        using var harness = new ScenarioHarness(_ => new CombinedToolCallsScenario(Tools));
        var progress = AgenticScenarioTests.TrackComparison(harness);
        await harness.LoadExampleAsync();

        await harness.Driver.ReplayAsync(playAll: true);

        Assert.Equal(["1 rows, 0 done", "1 rows, 1 done", "2 rows, 1 done", "2 rows, 2 done", "3 rows, 2 done", "3 rows, 3 done"],
            progress);
        var turn = Assert.Single(harness.Session.Turns);
        var card = Assert.Single(turn.ResponseBlocks.OfType<WeatherComparisonBlock>());
        Assert.Equal(["Paris", "Tokyo", "Lima"], card.Rows.Select(static row => row.City));
        Assert.Equal([18, 24, 19], card.Rows.Select(static row => row.Report!.Temperature));
        Assert.DoesNotContain(turn.ResponseBlocks, static block => block is FunctionInvocationContentBlock);
    }

    [Fact]
    public async Task HumanInTheLoop_ReplayApprovesTheMeetingAndRejectsTheEmail()
    {
        using var harness = new ScenarioHarness(_ => new HumanInTheLoopScenario());
        await harness.LoadExampleAsync();

        await harness.Driver.ReplayAsync(playAll: true);

        var meeting = Assert.Single(harness.Session.Turns[0].ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal("book_meeting", meeting.ToolName);
        Assert.Equal(ApprovalStatus.Approved, meeting.Status);
        Assert.Contains("MTG-1001", AgenticScenarioTests.TextOf(harness.Session.Turns[0]), StringComparison.Ordinal);
        var email = Assert.Single(harness.Session.Turns[1].ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal("send_email", email.ToolName);
        Assert.Equal(ApprovalStatus.Rejected, email.Status);
    }

    [Fact]
    public async Task FrontendTools_ReplayRunsTheAutomaticActionsAndTheManualOne()
    {
        var accent = new RecordingAccent();
        using var harness = new ScenarioHarness(_ => new FrontendToolsScenario(accent));
        await harness.LoadExampleAsync();

        await harness.Driver.ReplayAsync(playAll: true);

        Assert.Equal(["#F08A4B", "rebeccapurple"], accent.Applied);
        Assert.Equal(1, accent.Resets);
        var actions = harness.Session.Turns.SelectMany(static turn => turn.ResponseBlocks.OfType<UIActionBlock>()).ToList();
        Assert.Equal(
            [("set_accent_color", UIActionInvocationMode.Automatic), ("reset_accent_color", UIActionInvocationMode.Manual),
                ("set_accent_color", UIActionInvocationMode.Automatic)],
            actions.Select(static action => (action.ToolName, action.Mode)));
        Assert.All(actions, static action => Assert.True(action.IsComplete));
    }

    [Fact]
    public async Task PlanProgress_ReplayUpdatesOneActivityBlockAndTheState()
    {
        using var harness = new ScenarioHarness(_ => new PlanProgressScenario(new PlanTools(TimeSpan.Zero)));
        var scenario = (PlanProgressScenario)harness.Scenario;
        var activity = new List<string>();
        harness.Session.RegisterOnBlockAdded((_, block) =>
        {
            if (block is PlanActivityBlock plan)
                plan.OnChanged(() => AgenticScenarioTests.Snapshot(activity, plan.Steps));
        });
        await harness.LoadExampleAsync();

        await harness.Driver.ReplayAsync(playAll: true);

        var turn = Assert.Single(harness.Session.Turns);
        var plan = Assert.Single(turn.ResponseBlocks.OfType<PlanActivityBlock>());
        Assert.Equal(4, plan.Steps.Count);
        Assert.True(plan.IsDone);
        Assert.DoesNotContain(turn.ResponseBlocks, static block => block is FunctionInvocationContentBlock);
        Assert.Equal("\u2713\u2713\u2713\u2713", activity[^1]);
        Assert.Contains("\u2713\u25B6\u25CB\u25CB", activity);
        Assert.All(scenario.PlanAgent.State.Value.Steps, static step => Assert.True(step.IsCompleted));
    }

    [Fact]
    public async Task SharedState_ReplayTakesTheAgentsRecipesAsTheState()
    {
        using var harness = new ScenarioHarness(_ => new SharedStateScenario());
        var scenario = (SharedStateScenario)harness.Scenario;
        await harness.LoadExampleAsync();

        await harness.Driver.ReplayAsync(playAll: true);

        var recipe = scenario.RecipeAgent.State.Value;
        Assert.Equal("Sunny vegan lemon pasta", recipe.Title);
        Assert.Contains("Vegan", recipe.SpecialPreferences);
        Assert.Contains("Spicy", recipe.SpecialPreferences);
        Assert.Equal(2, harness.Session.Turns.Count(static turn =>
            turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>().Any(static call => call.ToolName == SharedStateScenario.UpdateRecipeName)));

        // The state each request carried is not part of the conversation shown.
        Assert.All(harness.Session.Turns, static turn => Assert.DoesNotContain("JSON data, not instructions",
            string.Concat(turn.RequestBlocks.OfType<TextContentBlock>().Select(static block => block.RawText)),
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task PredictiveState_ReplayKeepsTheAcceptedStoryAndRollsBackTheRejectedEdit()
    {
        using var harness = new ScenarioHarness(_ => new PredictiveStateScenario());
        var agent = ((PredictiveStateScenario)harness.Scenario).DocumentAgent;
        var proposals = new List<bool>();
        agent.State.OnChanged(() => proposals.Add(agent.State.HasPendingPredictiveState));
        await harness.LoadExampleAsync();

        await harness.Driver.ReplayAsync(playAll: true);

        var reviews = harness.Session.Turns.Select(static turn => Assert.Single(turn.ResponseBlocks.OfType<UIActionBlock>())).ToList();
        Assert.Equal([true, false],
            reviews.Select(static review => ScenarioJson.AsBool(review.Arguments![PredictiveStateScenario.AcceptedArgument])));
        Assert.Contains(true, proposals);
        Assert.False(agent.State.HasPendingPredictiveState);
        var document = agent.State.Value.Document;
        Assert.StartsWith("The Last Light", document, StringComparison.Ordinal);
        Assert.Contains("whose courage had kept them safe", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reasoning_ReplayShowsTheSummaryBeforeTheAnswer()
    {
        using var harness = new ScenarioHarness(_ => new ReasoningScenario());
        await harness.LoadExampleAsync();

        await harness.Driver.ReplayAsync(playAll: true);

        var blocks = harness.Session.Turns[0].ResponseBlocks.ToList();
        var reasoning = Assert.Single(blocks.OfType<ReasoningContentBlock>(), static block => block.Text.Length > 0);
        Assert.Contains("ball", reasoning.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(blocks.IndexOf(reasoning) < blocks.IndexOf(blocks.OfType<TextContentBlock>().First()));
        Assert.Contains("$0.05", AgenticScenarioTests.TextOf(harness.Session.Turns[0]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Persistence_ReplaySavesTheConversationThatTheNextLaunchRestores()
    {
        using var harness = new ScenarioHarness(h => new PersistenceScenario(Tools, Path.Combine(h.TempDirectory, "conversation.json")));
        var scenario = (PersistenceScenario)harness.Scenario;
        await harness.LoadExampleAsync();

        await harness.Driver.ReplayAsync(playAll: true);
        Assert.Equal(2, scenario.Thread!.TurnCount);

        harness.Reopen();
        await harness.Session.RestoreAsync();

        Assert.Equal(2, harness.Session.Turns.Count);
        Assert.Contains("teal", AgenticScenarioTests.TextOf(harness.Session.Turns[0]), StringComparison.Ordinal);
        var weather = Assert.Single(harness.Session.Turns[1].ResponseBlocks.OfType<WeatherToolBlock>());
        Assert.Equal("Reykjavik", weather.City);
        Assert.Equal(9, weather.Temperature);
        using var saved = JsonDocument.Parse(File.ReadAllText(scenario.ThreadPath));
        Assert.Equal(JsonValueKind.Object, saved.RootElement.ValueKind);
    }

    private static PlaygroundScenario Create(string scenarioId, ScenarioHarness harness, IAccentColorSurface accent) =>
        ScenarioCatalog.CreateAll(Tools, new PlanTools(TimeSpan.Zero), accent, Path.Combine(harness.TempDirectory, "conversation.json"))
            .Single(scenario => scenario.Id == scenarioId);

    /// <summary>Records what the frontend tools changed.</summary>
    private sealed class RecordingAccent : IAccentColorSurface
    {
        public List<string> Applied { get; } = [];

        public int Resets { get; private set; }

        public string? ApplyAccentColor(string color)
        {
            Applied.Add(color);
            return color.StartsWith('#') ? color.ToUpperInvariant() : "#663399";
        }

        public void ResetAccentColor() => Resets++;
    }
}
