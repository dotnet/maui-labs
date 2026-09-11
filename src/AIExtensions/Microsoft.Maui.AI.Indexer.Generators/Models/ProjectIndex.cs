using System.Collections.Generic;

namespace Microsoft.Maui.AI.Indexer.Generators.Models;

/// <summary>Aggregate project index across all pages.</summary>
internal sealed class ProjectIndex
{
    public List<PageModel> Pages { get; set; } = new();

    /// <summary>Class name of the app's home/entry screen (first ShellContent), or null.</summary>
    public string? EntryPageName { get; set; }
}
