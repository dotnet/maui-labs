using AppKit;

namespace MacOS.RuntimeTests;

public sealed record RuntimeScenario(
    string Name,
    int ExpectedCases,
    Func<RuntimeTestContext, NSApplicationDelegate>? CreateDelegate = null,
    Action<RuntimeTestContext>? RunManaged = null,
    int? ExpectedAssertions = null,
    int TimeoutSeconds = 90);

public static class ScenarioRegistry
{
    static readonly Dictionary<string, RuntimeScenario> Scenarios = new(StringComparer.Ordinal);

    public static void Register(RuntimeScenario scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario.Name) ||
            !char.IsAsciiLetterLower(scenario.Name[0]) ||
            scenario.Name.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')) ||
            scenario.ExpectedCases <= 0 || scenario.ExpectedAssertions is <= 0 ||
            scenario.TimeoutSeconds <= 0 ||
            (scenario.CreateDelegate == null) == (scenario.RunManaged == null))
            throw new ArgumentException("A scenario needs a valid id, positive counts/timeout and exactly one entry point.");
        if (!Scenarios.TryAdd(scenario.Name, scenario))
            throw new InvalidOperationException($"Duplicate scenario: {scenario.Name}");
    }

    public static RuntimeScenario Get(string name) =>
        Scenarios.TryGetValue(name, out var scenario)
            ? scenario
            : throw new ArgumentException($"Unknown scenario: {name}");

    public static IEnumerable<RuntimeScenario> All => Scenarios.Values.OrderBy(s => s.Name, StringComparer.Ordinal);
}
