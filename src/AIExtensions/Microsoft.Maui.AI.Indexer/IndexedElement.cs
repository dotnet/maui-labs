namespace Microsoft.Maui.AI.Indexer;

/// <summary>Broad semantic category known from XAML or the live MAUI control.</summary>
public enum IndexedElementKind
{
    Unknown,
    Group,
    Text,
    Heading,
    Action,
    Input,
    Selection,
    Toggle,
    Range,
    Collection,
    Image,
    Visual,
    Status,
    WebContent,
}

/// <summary>Structured semantic information for one indexed UI element.</summary>
public sealed record IndexedElement(
    IndexedElementKind Kind,
    string? Text,
    string? Hint,
    string? AutomationId,
    string? Placeholder,
    bool IsDynamic,
    bool IsActionable,
    bool IsConditional,
    int Depth,
    IReadOnlyList<string> State);
