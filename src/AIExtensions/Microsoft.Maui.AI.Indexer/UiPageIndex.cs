namespace Microsoft.Maui.AI.Indexer;

/// <summary>
/// Represents a single indexed XAML page with its semantic markdown content.
/// </summary>
public sealed class UiPageEntry
{
    public UiPageEntry(string name, string? route, string? filePath, string markdown)
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

/// <summary>
/// Base class for source-generated UI page indexes. Subclasses are generated
/// by the XAML indexer source generator — one per assembly.
/// </summary>
/// <remarks>
/// This follows the same pattern as <see cref="AI.Attributes.AIToolContext"/>:
/// the source generator emits a partial class with a <c>Default</c> singleton
/// and a <see cref="Pages"/> override — no runtime reflection needed.
/// </remarks>
public abstract class UiPageIndex
{
    /// <summary>All indexed pages in this assembly.</summary>
    public abstract IReadOnlyList<UiPageEntry> Pages { get; }

    /// <summary>
    /// The class name of the app's home/entry screen — the screen shown when the
    /// app launches (the first <c>ShellContent</c> in the Shell). <c>null</c> when
    /// the assembly has no Shell or the entry screen could not be resolved.
    /// </summary>
    public virtual string? EntryPageName => null;

    /// <summary>
    /// The app's home/entry screen, or <c>null</c> when it cannot be resolved.
    /// This is where a user starts, so any "how do I get to X" walkthrough should
    /// begin here.
    /// </summary>
    public UiPageEntry? Home
        => EntryPageName is null ? null : FindByName(EntryPageName);

    /// <summary>Find a page by its class name.</summary>
    public UiPageEntry? FindByName(string name)
        => Pages.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Find a page by its Shell route.</summary>
    public UiPageEntry? FindByRoute(string route)
        => Pages.FirstOrDefault(p => string.Equals(p.Route, route, StringComparison.OrdinalIgnoreCase));
}
