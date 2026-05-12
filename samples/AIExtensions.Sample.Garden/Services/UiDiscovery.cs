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
public sealed class UiDiscovery
{
    // The generated index class name follows the pattern {SanitizedAssemblyName}UiIndex.
    // For AIExtensions.Sample.Garden → AIExtensions_Sample_GardenUiIndex
    private static UiPageIndex Index => AIExtensions_Sample_GardenUiIndex.Default;

    [ExportAIFunction("search_ui")]
    [Description(
        "Search the app's UI pages for content matching one or more search terms. " +
        "Use this to find which pages contain specific controls, labels, buttons, or features. " +
        "Returns a list of matching page names with relevant snippets. " +
        "Example: search for ['cart', 'checkout'] to find pages related to shopping.")]
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
        "Get the full semantic UI description of a specific page. " +
        "Returns the complete accessibility tree showing all controls, " +
        "their labels, bindings, commands, and conditions. " +
        "Use after search_ui to inspect a specific page in detail.")]
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
        "List all pages and views in the app with their routes. " +
        "Use this to understand the app's structure and navigation.")]
    public static string ListAppPages()
    {
        var sb = new StringBuilder();
        sb.AppendLine("App pages and views:");
        sb.AppendLine();

        foreach (var page in Index.Pages.OrderBy(p => p.Name))
        {
            var route = page.Route != null ? $" (route: {page.Route})" : "";
            var file = page.FilePath != null ? $" — {page.FilePath}" : "";
            sb.AppendLine($"- {page.Name}{route}{file}");
        }

        sb.AppendLine();
        sb.AppendLine("Use get_page_ui with a page name to see its full UI, or search_ui to find pages by content.");
        return sb.ToString();
    }
}
