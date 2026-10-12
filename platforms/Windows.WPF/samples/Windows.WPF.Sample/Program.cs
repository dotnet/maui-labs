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
    public static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--test-scenario") >= 0)
        {
#if WPF_TEST_SCENARIOS
            return TestScenarios.TestScenarioRunner.Run(args);
#else
            Console.Error.WriteLine("Test scenarios require building with -p:WpfTestScenarios=true.");
            return 2;
#endif
        }

        var app = new App();
        return app.Run();
    }
}
