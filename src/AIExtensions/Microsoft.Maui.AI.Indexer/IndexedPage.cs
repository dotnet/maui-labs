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
    {
        Name = name;
        FilePath = filePath;
        Markdown = markdown;
        Routes = routes ?? throw new ArgumentNullException(nameof(routes));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
    }

    /// <summary>The page class name.</summary>
    public string Name { get; }

    /// <summary>Relative file path of the XAML source.</summary>
    public string? FilePath { get; }

    /// <summary>The semantic markdown representation of the page's UI.</summary>
    public string Markdown { get; }

    /// <summary>The page's fully qualified CLR type name.</summary>
    public string TypeName { get; }

    /// <summary>All explicit absolute ShellContent routes for this page.</summary>
    public IReadOnlyList<string> Routes { get; }

    /// <summary>The first explicit ShellContent route, when available.</summary>
    public string? Route => Routes.FirstOrDefault();
}
