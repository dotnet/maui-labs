using System;
using System.Windows;

namespace MauiWpfApp;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();
        var mauiApp = MauiProgram.CreateMauiApp();
        // WPF backend bootstraps from the MauiApp
        app.Run();
    }
}
