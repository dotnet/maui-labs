#if MACOS
using Microsoft.Maui.Platforms.MacOS.Controls;
#endif

namespace DevFlow.Sample;

public partial class MultiBlazorPage : ContentPage
{
    public MultiBlazorPage()
    {
        InitializeComponent();
#if MACOS
        // macOS (AppKit) needs MacOSBlazorWebView instead of standard BlazorWebView
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 4,
            Padding = new Thickness(4),
        };

        var left = new MacOSBlazorWebView
        {
            HostPage = "wwwroot/index.html",
            AutomationId = "BlazorLeft",
        };
        left.RootComponents.Add(new BlazorRootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.Routes),
        });
        Grid.SetColumn(left, 0);
        grid.Add(left);

        var right = new MacOSBlazorWebView
        {
            HostPage = "wwwroot/index.html",
            AutomationId = "BlazorRight",
        };
        right.RootComponents.Add(new BlazorRootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.Routes),
        });
        Grid.SetColumn(right, 1);
        grid.Add(right);

        Content = grid;
#endif
    }
}
