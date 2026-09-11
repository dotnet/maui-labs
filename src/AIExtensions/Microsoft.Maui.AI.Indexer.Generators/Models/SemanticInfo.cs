namespace Microsoft.Maui.AI.Indexer.Generators.Models;

/// <summary>Semantic information extracted from SemanticProperties attached properties.</summary>
internal sealed class SemanticInfo
{
    public string? Description { get; set; }
    public string? Hint { get; set; }
    public int? HeadingLevel { get; set; }
}
