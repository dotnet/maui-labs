namespace Microsoft.Maui.AI.Indexer.Generators.Models;

/// <summary>Information about a data binding expression.</summary>
internal sealed class BindingInfo
{
    public string? Path { get; set; }
    public string? Mode { get; set; }
    public string? Converter { get; set; }
    public string? StringFormat { get; set; }
    public string? Raw { get; set; }

    public bool IsBound => Path != null;

    public string ToDisplayString()
    {
        if (Path != null) return "{" + Path + "}";
        return Raw ?? "";
    }
}
