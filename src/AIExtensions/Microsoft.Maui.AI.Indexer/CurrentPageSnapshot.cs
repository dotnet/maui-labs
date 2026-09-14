namespace Microsoft.Maui.AI.Indexer;

/// <summary>
/// A semantic Markdown snapshot of the currently presented MAUI page.
/// </summary>
public sealed class CurrentPageSnapshot
{
    public CurrentPageSnapshot(string pageName, string markdown)
        : this(pageName, markdown, null, [], [])
    {
    }

    public CurrentPageSnapshot(
        string pageName,
        string markdown,
        string? pageTitle)
        : this(pageName, markdown, pageTitle, [], [])
    {
    }

    public CurrentPageSnapshot(
        string pageName,
        string markdown,
        string? pageTitle,
        IReadOnlyList<string> automationIds)
        : this(pageName, markdown, pageTitle, automationIds, [])
    {
    }

    public CurrentPageSnapshot(
        string pageName,
        string markdown,
        string? pageTitle,
        IReadOnlyList<string> automationIds,
        IReadOnlyList<IndexedElement> elements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageName);
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(automationIds);
        ArgumentNullException.ThrowIfNull(elements);

        PageName = pageName;
        Markdown = markdown;
        PageTitle = string.IsNullOrWhiteSpace(pageTitle)
            ? null
            : pageTitle;
        AutomationIds = automationIds;
        Elements = elements;
    }

    /// <summary>The runtime type name of the presented page.</summary>
    public string PageName { get; }

    /// <summary>The user-visible title of the presented page, when available.</summary>
    public string? PageTitle { get; }

    /// <summary>
    /// Automation identifiers for semantic controls included in this snapshot.
    /// </summary>
    public IReadOnlyList<string> AutomationIds { get; }

    /// <summary>Structured semantic elements currently visible on the page.</summary>
    public IReadOnlyList<IndexedElement> Elements { get; }

    /// <summary>
    /// The currently materialized, visible controls and their live semantic state.
    /// </summary>
    public string Markdown { get; }
}
