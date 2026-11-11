namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Creates the playground's agentic scenarios, in the order the sidebar lists them.</summary>
public static class ScenarioCatalog
{
    /// <summary>Creates one instance of every scenario.</summary>
    /// <param name="tools">The weather and clock tools.</param>
    /// <param name="planTools">The planning tools.</param>
    /// <param name="accent">What the frontend tools recolor.</param>
    /// <param name="persistencePath">The file the persistence scenario saves its conversation in.</param>
    public static IReadOnlyList<PlaygroundScenario> CreateAll(
        ScenarioTools tools, PlanTools planTools, IAccentColorSurface accent, string persistencePath)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(planTools);
        ArgumentNullException.ThrowIfNull(accent);
        ArgumentException.ThrowIfNullOrWhiteSpace(persistencePath);
        return
        [
            new ToolCardsScenario(tools),
            new CombinedToolCallsScenario(tools),
            new HumanInTheLoopScenario(),
            new FrontendToolsScenario(accent),
            new PlanProgressScenario(planTools),
            new SharedStateScenario(),
            new PredictiveStateScenario(),
            new ReasoningScenario(),
            new PersistenceScenario(tools, persistencePath),
        ];
    }

    /// <summary>Gets the bundled example recording of a scenario, relative to the app package.</summary>
    public static string ExamplePath(string scenarioId) => $"scenarios/{scenarioId}.json";
}
