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
		var logPath = System.IO.Path.Combine(
			System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
			"maui_crash.log");
		System.IO.File.AppendAllText(logPath,
			$"[{System.DateTime.Now}] UNHANDLED: {e.Exception}\n{e.Exception?.StackTrace}\n\n");
		// Do NOT set e.Handled = true — let the exception propagate
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

