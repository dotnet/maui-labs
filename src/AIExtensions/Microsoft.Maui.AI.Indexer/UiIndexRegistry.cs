using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Microsoft.Maui.AI.Indexer;

/// <summary>
/// Runtime registry that discovers all generated UI page indexes
/// and provides search and lookup capabilities.
/// </summary>
public sealed class UiIndexRegistry
{
    private static UiIndexRegistry? _instance;

    /// <summary>
    /// The shared registry instance. Call <see cref="CreateFromAssembly"/> first.
    /// </summary>
    public static UiIndexRegistry Instance => _instance
        ?? throw new InvalidOperationException("Call UiIndexRegistry.CreateFromAssembly() at app startup.");

    private readonly PageInfo[] _pages;

    /// <summary>All indexed pages.</summary>
    public IReadOnlyList<PageInfo> Pages => _pages;

    private UiIndexRegistry(PageInfo[] pages)
    {
        _pages = pages;
    }

    /// <summary>
    /// Register pages directly. Called by the generated UiIndex class.
    /// </summary>
    public static UiIndexRegistry Register(params PageInfo[] pages)
    {
        _instance = new UiIndexRegistry(pages);
        return _instance;
    }

    /// <summary>
    /// Scan an assembly for all types marked with [UiPageIndex] and build the registry.
    /// Call this once at app startup.
    /// </summary>
    [RequiresUnreferencedCode("Uses reflection to discover [UiPageIndex] types. Prefer Register() for trimmed apps.")]
    public static UiIndexRegistry CreateFromAssembly(Assembly assembly)
    {
        var pages = new List<PageInfo>();

        foreach (var type in assembly.GetTypes())
        {
            var attr = type.GetCustomAttribute<UiPageIndexAttribute>();
            if (attr == null)
                continue;

            var markdownField = type.GetField("Markdown", BindingFlags.Public | BindingFlags.Static);
            if (markdownField == null)
                continue;

            var markdown = markdownField.GetValue(null) as string ?? "";

            pages.Add(new PageInfo(attr.PageName, attr.Route, attr.FilePath, markdown));
        }

        _instance = new UiIndexRegistry(pages.OrderBy(p => p.Name).ToArray());
        return _instance;
    }

    /// <summary>
    /// Search across all page indexes for pages containing the given text.
    /// </summary>
    public PageInfo[] Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _pages;

        return _pages
            .Where(p => p.Markdown.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || (p.Route != null && p.Route.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    /// <summary>Find a page by its Shell route.</summary>
    public PageInfo? FindByRoute(string route)
        => _pages.FirstOrDefault(p => string.Equals(p.Route, route, StringComparison.OrdinalIgnoreCase));

    /// <summary>Find a page by its class name.</summary>
    public PageInfo? FindByName(string name)
        => _pages.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Represents an indexed page.</summary>
    public sealed class PageInfo
    {
        public PageInfo(string name, string? route, string? filePath, string markdown)
        {
            Name = name;
            Route = route;
            FilePath = filePath;
            Markdown = markdown;
        }

        public string Name { get; }
        public string? Route { get; }
        public string? FilePath { get; }
        public string Markdown { get; }
    }
}
