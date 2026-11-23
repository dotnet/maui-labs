namespace Microsoft.Maui.DevFlow.Extensibility;

/// <summary>Behaviour hints for a plugin tool, passed through to MCP clients.</summary>
public sealed class DevFlowToolAnnotations
{
    /// <summary>The tool only reads state. Read-only tools skip the mutation lease.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>Calling the tool again with the same parameters has no further effect.</summary>
    public bool Idempotent { get; init; }

    /// <summary>The tool can change device or app state in a way that is hard to undo.</summary>
    public bool Destructive { get; init; }

    /// <summary>Optional grouping shown by tools that list plugin tools.</summary>
    public string? Category { get; init; }
}
