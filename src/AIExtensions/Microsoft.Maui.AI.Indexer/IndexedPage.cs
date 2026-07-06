namespace Microsoft.Maui.AI.Indexer;

/// <summary>
/// Represents a single indexed XAML page with its semantic markdown content.
/// </summary>
public sealed class IndexedPage
{
    public IndexedPage(string name, string? route, string? filePath, string markdown)
    {
        Name = name;
        Route = route;
        FilePath = filePath;
        Markdown = markdown;
    }

    /// <summary>The page class name.</summary>
    public string Name { get; }

    /// <summary>The Shell route, if any.</summary>
    public string? Route { get; }

    /// <summary>Relative file path of the XAML source.</summary>
    public string? FilePath { get; }

    /// <summary>The semantic markdown representation of the page's UI.</summary>
    public string Markdown { get; }
}
