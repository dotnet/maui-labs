using System.ComponentModel;
using System.Text;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Maui.AI.Indexer;

namespace AIExtensions.Sample.Garden.Services;

/// <summary>
/// AI tools for searching and discovering the app's UI structure.
/// Uses the compile-time generated UI index to answer questions like
/// "which page has the list of products?" or "where do I go to checkout?".
/// </summary>
public sealed class PageDiscovery
{
    // The generated index class name follows the pattern {SanitizedAssemblyName}IndexedPageCatalog.
    // For AIExtensions.Sample.Garden → AIExtensions_Sample_GardenIndexedPageCatalog
    private static IndexedPageCatalog Index => AIExtensions_Sample_GardenIndexedPageCatalog.Default;

    [ExportAIFunction("search_ui")]
    [Description(
        "ALWAYS use this first to answer any question about how to use the app, how to do a " +
        "task, where a feature or screen is, or to walk the user through something (for example " +
        "'how do I write a review?', 'walk me through checking out', 'where do I see prices?'). " +
        "Searches the real app screens for content matching one or more terms and returns the " +
        "matching screen names with snippets of their actual controls. Never answer such " +
        "questions from your own knowledge — search here and then read the screens with " +
        "get_page_ui. Example: search for ['review'] to find where reviews are written.")]
    public static string SearchUi(
        [Description("One or more search terms to look for across all pages. Each term is matched independently.")]
        string[] searchTerms)
    {
        if (searchTerms == null || searchTerms.Length == 0)
            return "No search terms provided.";

        var sb = new StringBuilder();
        var matchedPages = new Dictionary<string, List<string>>();

        foreach (var page in Index.Pages)
        {
            var matchedTerms = new List<string>();
            foreach (var term in searchTerms)
            {
                if (string.IsNullOrWhiteSpace(term))
                    continue;

                if (page.Markdown.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || page.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    matchedTerms.Add(term);
                }
            }

            if (matchedTerms.Count > 0)
                matchedPages[page.Name] = matchedTerms;
        }

        if (matchedPages.Count == 0)
            return $"No pages found matching: {string.Join(", ", searchTerms)}";

        var home = Index.EntryPageName;
        if (!string.IsNullOrEmpty(home))
        {
            sb.AppendLine($"HOME screen (the app opens here — every user starts on this screen): {home}");
            sb.AppendLine($"A walkthrough MUST start here. First call get_page_ui(\"{home}\") to read the home screen, then follow its buttons screen by screen to reach the matching page(s) below.");
            sb.AppendLine();
        }

        sb.AppendLine($"Found {matchedPages.Count} page(s) matching your search:");
        sb.AppendLine();

        foreach (var kv in matchedPages.OrderByDescending(x => x.Value.Count))
        {
            var page = Index.FindByName(kv.Key);
            if (page == null) continue;

            sb.AppendLine($"## {kv.Key}");
            sb.AppendLine($"Matched terms: {string.Join(", ", kv.Value)}");

            var lines = page.Markdown.Split('\n');
            var relevantLines = new List<string>();
            foreach (var line in lines)
            {
                foreach (var term in kv.Value)
                {
                    if (line.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        relevantLines.Add(line.TrimStart());
                        break;
                    }
                }
            }

            if (relevantLines.Count > 0)
            {
                sb.AppendLine("Relevant content:");
                foreach (var line in relevantLines.Take(10))
                    sb.AppendLine($"  {line}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Use get_page_ui with a page name to see its full UI structure.");
        return sb.ToString();
    }

    [ExportAIFunction("get_page_ui")]
    [Description(
        "Read one real app screen in full. Returns the exact controls on that screen — the " +
        "verbatim button labels, field labels, headings, and navigation hints. Call this on " +
        "every screen along the path (starting from the home screen) before you explain how to " +
        "do anything, so you can name the exact on-screen text the user must tap or type. Never " +
        "describe a screen you have not read with this tool. Use after search_ui or " +
        "list_app_pages.")]
    public static string GetPageUi(
        [Description("The name of the page to retrieve, e.g. 'MainPage', 'CatalogView', 'ProductDetailPage'")]
        string pageName)
    {
        if (string.IsNullOrWhiteSpace(pageName))
            return "Please provide a page name.";

        var page = Index.FindByName(pageName);
        if (page != null)
            return page.Markdown;

        // Try fuzzy matching
        var candidates = Index.Pages
            .Where(p => p.Name.Contains(pageName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (candidates.Length == 1)
            return candidates[0].Markdown;

        if (candidates.Length > 1)
            return $"Multiple pages match '{pageName}': {string.Join(", ", candidates.Select(c => c.Name))}. Please be more specific.";

        return $"Page '{pageName}' not found. Available pages: {string.Join(", ", Index.Pages.Select(p => p.Name))}";
    }

    [ExportAIFunction("list_app_pages")]
    [Description(
        "List every real screen in the app with its route, and identify the HOME screen (where " +
        "the app opens and every user starts). Call this when you need the full set of screens " +
        "or the starting point for a walkthrough. Read individual screens with get_page_ui.")]
    public static string ListAppPages()
    {
        var sb = new StringBuilder();
        var home = Index.EntryPageName;

        if (!string.IsNullOrEmpty(home))
        {
            sb.AppendLine($"HOME screen (the app opens here — start every walkthrough from this screen): {home}");
            sb.AppendLine();
        }

        sb.AppendLine("App pages and views:");
        sb.AppendLine();

        foreach (var page in Index.Pages.OrderBy(p => p.Name))
        {
            var route = page.Route != null ? $" (route: {page.Route})" : "";
            var file = page.FilePath != null ? $" — {page.FilePath}" : "";
            var isHome = string.Equals(page.Name, home, StringComparison.OrdinalIgnoreCase) ? "  [HOME]" : "";
            sb.AppendLine($"- {page.Name}{route}{file}{isHome}");
        }

        sb.AppendLine();
        sb.AppendLine("Use get_page_ui with a page name to see its full UI, or search_ui to find pages by content.");
        return sb.ToString();
    }
}
