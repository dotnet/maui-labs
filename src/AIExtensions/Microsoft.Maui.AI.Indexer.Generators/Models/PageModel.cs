using System.Collections.Generic;

namespace Microsoft.Maui.AI.Indexer.Generators.Models;

/// <summary>Parsed XAML document with semantic tree.</summary>
internal sealed class PageModel
{
    public string ClassName { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string RootType { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string? Route { get; set; }
    public List<SemanticNode> Elements { get; set; } = new();
}
