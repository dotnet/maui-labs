namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// View model for a single tool shown in the empty-state placeholder.
/// </summary>
public sealed record ToolInfoViewModel(
    string Name,
    string Description);
