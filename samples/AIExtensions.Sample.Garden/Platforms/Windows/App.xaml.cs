using Microsoft.UI.Xaml;

namespace AIExtensions.Sample.Garden.WinUI;

public partial class App : MauiWinUIApplication
{
	public App()
	{
		this.InitializeComponent();
		this.UnhandledException += OnUnhandledException;
	}

	private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
	{
		var configuredPath = System.Environment.GetEnvironmentVariable("GARDEN_CRASH_LOG");
		var fallbackPath = System.IO.Path.Combine(
			System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
			"maui_crash.log");
		foreach (var logPath in new[] { configuredPath, fallbackPath }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct())
		{
			try
			{
				var directory = System.IO.Path.GetDirectoryName(logPath);
				if (!string.IsNullOrWhiteSpace(directory))
					System.IO.Directory.CreateDirectory(directory);
				System.IO.File.AppendAllText(logPath,
					$"[{System.DateTime.Now}] UNHANDLED: {e.Exception}\n{e.Exception?.StackTrace}\n\n");
				break;
			}
			catch
			{
				// Crash logging is best-effort and must not hide the original exception.
			}
		}
		// Do NOT set e.Handled = true — let the exception propagate
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
