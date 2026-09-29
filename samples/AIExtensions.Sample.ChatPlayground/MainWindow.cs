using Microsoft.Extensions.Configuration;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class MainWindow : Window
{
    /// <summary>Creates the Chat, Embeddings, Images, and Documents tabs in DI registration order.</summary>
    public MainWindow(
        IEnumerable<Page> pages,
        IConfiguration configuration)
        : base(CreateTabs(pages, configuration))
    {
    }

    private static TabbedPage CreateTabs(
        IEnumerable<Page> pages,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(configuration);

        var tabs = new TabbedPage();
        foreach (var page in pages)
            tabs.Children.Add(page);

        if (tabs.Children.Count == 0)
            throw new InvalidOperationException("At least one playground page must be registered.");

        if (configuration["page"] is { } pageName &&
            tabs.Children.FirstOrDefault(page =>
                string.Equals(page.Title, pageName, StringComparison.OrdinalIgnoreCase)) is { } selectedPage)
        {
            tabs.CurrentPage = selectedPage;
        }

        return tabs;
    }
}
