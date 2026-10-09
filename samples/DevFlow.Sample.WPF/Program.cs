using DevFlow.Sample;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;

namespace DevFlow.Sample.WPF;

public sealed class WpfApplication : MauiWPFApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new WpfApplication();
        app.Run();
    }
}
