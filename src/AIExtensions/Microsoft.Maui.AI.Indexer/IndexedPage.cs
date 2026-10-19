namespace Microsoft.Maui.AI.Indexer;

/// <summary>
/// Represents a single indexed XAML page with its semantic markdown content.
/// </summary>
public sealed class IndexedPage
{
    public IndexedPage(string name, string? filePath, string markdown, string? route = null)
        : this(
            name,
            filePath,
            markdown,
            route is null ? Array.Empty<string>() : [route],
            name)
    {
    }

    public IndexedPage(
        string name,
        string? filePath,
        string markdown,
        IReadOnlyList<string> routes,
        string typeName)
        : this(name, filePath, markdown, routes, typeName, null, [])
    {
    }

    public IndexedPage(
        string name,
        string? filePath,
        string markdown,
        IReadOnlyList<string> routes,
        string typeName,
        string? title,
        IReadOnlyList<IndexedElement> elements)
    {
        Name = name;
        FilePath = filePath;
        Markdown = markdown;
        Routes = routes ?? throw new ArgumentNullException(nameof(routes));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Title = title;
        Elements = elements ?? throw new ArgumentNullException(nameof(elements));
    }

    /// <summary>The page class name.</summary>
    public string Name { get; }

    /// <summary>Relative file path of the XAML source.</summary>
    public string? FilePath { get; }

    /// <summary>The semantic markdown representation of the page's UI.</summary>
    public string Markdown { get; }

    /// <summary>The page's fully qualified CLR type name.</summary>
    public string TypeName { get; }

    /// <summary>User-visible page title inferred from the first static heading.</summary>
    public string? Title { get; }

    /// <summary>Structured semantic elements emitted for this page.</summary>
    public IReadOnlyList<IndexedElement> Elements { get; }

    /// <summary>All explicit absolute ShellContent routes for this page.</summary>
    public IReadOnlyList<string> Routes { get; }

    /// <summary>The first explicit ShellContent route, when available.</summary>
    public string? Route => Routes.FirstOrDefault();
}
