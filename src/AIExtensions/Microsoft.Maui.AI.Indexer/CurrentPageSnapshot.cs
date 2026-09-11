namespace Microsoft.Maui.AI.Indexer;

/// <summary>
/// A semantic Markdown snapshot of the currently presented MAUI page.
/// </summary>
public sealed class CurrentPageSnapshot
{
    public CurrentPageSnapshot(string pageName, string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageName);
        ArgumentNullException.ThrowIfNull(markdown);

        PageName = pageName;
        Markdown = markdown;
    }

    /// <summary>The runtime type name of the presented page.</summary>
    public string PageName { get; }

    /// <summary>
    /// The currently materialized, visible controls and their live semantic state.
    /// </summary>
    public string Markdown { get; }
}
