using Microsoft.Extensions.Configuration;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class MainWindow : Window
{
    public MainWindow(IEnumerable<Page> pages, IConfiguration configuration)
        : base(CreateTabs(pages, configuration["page"]))
    {
    }

    private static TabbedPage CreateTabs(IEnumerable<Page> pages, string? initialPage)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var tabs = new TabbedPage();
        foreach (var page in pages)
            tabs.Children.Add(page);

        if (tabs.Children.Count == 0)
            throw new InvalidOperationException("At least one playground page must be registered.");
        if (!string.IsNullOrWhiteSpace(initialPage))
            tabs.CurrentPage = tabs.Children.FirstOrDefault(page => string.Equals(page.Title, initialPage, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown playground page '{initialPage}'.", nameof(initialPage));

        return tabs;
    }
}
