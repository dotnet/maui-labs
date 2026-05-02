namespace EssentialsAISample;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}

	protected override void OnStart()
	{
		base.OnStart();

		// TEMP: test if command-line args reach a packaged MSIX app via dotnet run
		// Run with: dotnet run -f net10.0-windows10.0.19041.0 -p:WinAppLaunchArgs="hello-world"
		// Delay so the Window's XamlRoot is set before showing a dialog (Windows requirement)
		MainThread.InvokeOnMainThreadAsync(async () =>
		{
			await Task.Delay(1000);

			var cliArgs = Environment.GetCommandLineArgs();
			var argText = cliArgs.Length > 1
				? string.Join(" | ", cliArgs[1..])
				: "(no args)";

#if WINDOWS
			try
			{
				var activatedArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
				if (activatedArgs?.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launchArgs
					&& !string.IsNullOrEmpty(launchArgs.Arguments))
				{
					argText += $"\nWinRT activation args: {launchArgs.Arguments}";
				}
			}
			catch { /* not available in all scenarios */ }
#endif

			var page = Windows.Count > 0 ? Windows[0].Page : null;
			if (page is not null)
				await page.DisplayAlertAsync("Launch Args Test", argText, "OK");
		});
	}
}