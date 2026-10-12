using System;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;

namespace MauiWpfApp;

public class WpfApp : MauiWPFApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new WpfApp();
        app.Run();
    }
}
