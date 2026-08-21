namespace GenerativeUI.Sample.Garden.Components;

public enum GardenGenerationMode
{
    ComponentComposer,
    BaselineFullGeneration,
}

public sealed record GardenGenerationModeOption(
    GardenGenerationMode Mode,
    string Label,
    string Description)
{
    public override string ToString() => Label;
}

public static class GardenGenerationModes
{
    public static IReadOnlyList<GardenGenerationModeOption> Options { get; } =
    [
        new(
            GardenGenerationMode.ComponentComposer,
            "Component Composer",
            "The model selects and prioritizes tested native product components."),
        new(
            GardenGenerationMode.BaselineFullGeneration,
            "Baseline Full Generation",
            "Research baseline: the model authors the full primitive UI tree."),
    ];

    private static readonly HashSet<string> s_composerTools = new(StringComparer.Ordinal)
    {
        "list_endpoints",
        "describe_endpoint",
        "describe_model",
        "read_api",
        "compose_product_detail",
    };

    public static bool IncludesTool(GardenGenerationMode mode, string toolName)
        => mode switch
        {
            GardenGenerationMode.ComponentComposer => s_composerTools.Contains(toolName),
            GardenGenerationMode.BaselineFullGeneration =>
                !string.Equals(toolName, "compose_product_detail", StringComparison.Ordinal),
            _ => false,
        };
}
