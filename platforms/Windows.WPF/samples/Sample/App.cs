using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Sample;

public class App : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var tabbedPage = new TabbedPage();

        var controlsPage = new MainPage { Title = "Controls" };
        controlsPage.ToolbarItems.Add(new ToolbarItem("Refresh", null, async () => await controlsPage.DisplayAlertAsync("Toolbar", "Hi From Controls toolbar", "OK")));
        controlsPage.ToolbarItems.Add(new ToolbarItem("Add", null, async () => await controlsPage.DisplayAlertAsync("Toolbar", "Hi From Controls toolbar", "OK")));

        var essentialsPage = new EssentialsPage { Title = "Essentials" };
        essentialsPage.ToolbarItems.Add(new ToolbarItem("Info", null, async () => await essentialsPage.DisplayAlertAsync("Toolbar", "Hi From Essentials toolbar", "OK")));

        var storagePage = new StoragePage { Title = "Storage" };
        storagePage.ToolbarItems.Add(new ToolbarItem("Clear", null, async () => await storagePage.DisplayAlertAsync("Toolbar", "Hi From Storage toolbar", "OK")));
        storagePage.ToolbarItems.Add(new ToolbarItem("Export", null, async () => await storagePage.DisplayAlertAsync("Toolbar", "Hi From Storage toolbar", "OK")));

        tabbedPage.Children.Add(new NavigationPage(controlsPage) { Title = "Controls" });
        tabbedPage.Children.Add(new NavigationPage(essentialsPage) { Title = "Essentials" });
        tabbedPage.Children.Add(new NavigationPage(storagePage) { Title = "Storage" });
        tabbedPage.Children.Add(new NavigationPage(new Sample.Pages.MapPage { Title = "Map" }) { Title = "Map" });
#if WPFAPP
        tabbedPage.Children.Add(new NavigationPage(new FlyoutDemoPage { Title = "FlyoutPage" }) { Title = "FlyoutPage" });
#endif
        var window = new Window(tabbedPage);
        window.Created += (s, e) => Console.WriteLine("[Lifecycle] Window Created");
        window.Activated += (s, e) => Console.WriteLine("[Lifecycle] Window Activated");
        window.Deactivated += (s, e) => Console.WriteLine("[Lifecycle] Window Deactivated");
        window.Stopped += (s, e) => Console.WriteLine("[Lifecycle] Window Stopped");
        window.Resumed += (s, e) => Console.WriteLine("[Lifecycle] Window Resumed");
        window.Destroying += (s, e) => Console.WriteLine("[Lifecycle] Window Destroying");
        window.Backgrounding += (s, e) => Console.WriteLine("[Lifecycle] Window Backgrounding");
        return window;
    }
}
