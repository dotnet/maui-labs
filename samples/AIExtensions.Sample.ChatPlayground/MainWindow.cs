namespace AIExtensions.Sample.ChatPlayground;

public sealed class MainWindow : Window
{
    public MainWindow(IEnumerable<Page> pages)
        : base(CreateTabs(pages))
    {
    }

    private static TabbedPage CreateTabs(IEnumerable<Page> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var tabs = new TabbedPage();
        foreach (var page in pages)
            tabs.Children.Add(page);

        if (tabs.Children.Count == 0)
            throw new InvalidOperationException("At least one playground page must be registered.");

        return tabs;
    }
}
