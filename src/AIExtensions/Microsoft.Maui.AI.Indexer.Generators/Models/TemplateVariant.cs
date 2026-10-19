using System.Collections.Generic;

namespace Microsoft.Maui.AI.Indexer.Generators.Models;

/// <summary>A named template variant from a DataTemplateSelector.</summary>
internal sealed class TemplateVariant
{
    public string Name { get; set; } = "";
    public List<SemanticNode> Elements { get; set; } = new();
}
