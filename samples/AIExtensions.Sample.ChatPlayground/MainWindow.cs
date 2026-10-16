namespace AIExtensions.Sample.ChatPlayground;

public sealed class MainWindow : Window
{
    public MainWindow(IEnumerable<Page> pages)
        : base(CreateShell(pages))
    {
    }

    private static Shell CreateShell(IEnumerable<Page> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var shell = new Shell
        {
            FlyoutBehavior = FlyoutBehavior.Disabled,
        };
        var tabBar = new TabBar
        {
            Route = "playground",
        };
        foreach (var page in pages)
        {
            var title = string.IsNullOrWhiteSpace(page.Title)
                ? page.GetType().Name
                : page.Title;
            var route = CreateRoute(title);
            var tab = new Tab
            {
                Route = route,
                Title = title,
            };
            tab.Items.Add(new ShellContent
            {
                Title = title,
                Content = page,
            });
            tabBar.Items.Add(tab);
        }

        if (tabBar.Items.Count == 0)
            throw new InvalidOperationException("At least one playground page must be registered.");

        shell.Items.Add(tabBar);
        return shell;
    }

    private static string CreateRoute(string title)
        => string.Concat(title
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant));
}
