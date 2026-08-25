namespace Microsoft.Maui.AI.Indexer;

/// <summary>
/// A semantic Markdown snapshot of the currently presented MAUI page.
/// </summary>
public sealed class CurrentPageSnapshot
{
    public CurrentPageSnapshot(string pageName, string markdown)
        : this(pageName, markdown, null)
    {
    }

    public CurrentPageSnapshot(
        string pageName,
        string markdown,
        string? pageTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageName);
        ArgumentNullException.ThrowIfNull(markdown);

        PageName = pageName;
        Markdown = markdown;
        PageTitle = string.IsNullOrWhiteSpace(pageTitle)
            ? null
            : pageTitle;
    }

    /// <summary>The runtime type name of the presented page.</summary>
    public string PageName { get; }

    /// <summary>The user-visible title of the presented page, when available.</summary>
    public string? PageTitle { get; }

    /// <summary>
    /// The currently materialized, visible controls and their live semantic state.
    /// </summary>
    public string Markdown { get; }
}
