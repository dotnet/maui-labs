using System;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;

namespace Microsoft.Maui.Platforms.Windows.WPF.Sample;

// WPF Application shell — hosts MAUI via MauiWPFApplication.
// Named "App" (WPF convention); MAUI's Application subclass is MainApp.
public class App : MauiWPFApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new App();
        app.Run();
    }
}
