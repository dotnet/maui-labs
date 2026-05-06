#if MACOS
using Microsoft.Maui.Platforms.MacOS.Controls;
#endif

namespace DevFlow.Sample;

public partial class BlazorTodoPage : ContentPage
{
    public BlazorTodoPage()
    {
        InitializeComponent();
#if MACOS
        var macWebView = new MacOSBlazorWebView
        {
            HostPage = "wwwroot/index.html",
            AutomationId = "BlazorWebView",
        };
        macWebView.RootComponents.Add(new BlazorRootComponent
        {
            Selector = "#app",
            ComponentType = typeof(DevFlow.Sample.Components.Routes),
        });
        Content = macWebView;
#endif
    }
}
