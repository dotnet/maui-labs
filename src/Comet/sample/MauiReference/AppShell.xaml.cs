using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Font = Microsoft.Maui.Font;
namespace MauiReference;

public partial class AppShell : Shell
{
	public static string? ForcePage { get; set; }

	public AppShell()
	{
		InitializeComponent();
		var currentTheme = Application.Current!.RequestedTheme;		
		ThemeSegmentedControl.SelectedIndex = currentTheme == AppTheme.Light ? 0 : 1;

		// Navigate to forced page after shell is loaded
		if (ForcePage != null)
		{
			Dispatcher.DispatchAsync(async () =>
			{
				await Task.Delay(1000);
				if (ForcePage == "projectdetail")
					await GoToAsync($"//main/project?id=1");
				else if (ForcePage == "taskdetail")
					await GoToAsync($"//main/task?id=1");
				else
					await GoToAsync($"//{ForcePage}");
			});
		}
	}
	public static async Task DisplaySnackbarAsync(string message)
	{
		CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

		var snackbarOptions = new SnackbarOptions
		{
			BackgroundColor = Color.FromArgb("#FF3300"),
			TextColor = Colors.White,
			ActionButtonTextColor = Colors.Yellow,
			CornerRadius = new CornerRadius(0),
			Font = Font.SystemFontOfSize(18),
			ActionButtonFont = Font.SystemFontOfSize(14)
		};

		var snackbar = Snackbar.Make(message, visualOptions: snackbarOptions);

		await snackbar.Show(cancellationTokenSource.Token);
	}

	public static async Task DisplayToastAsync(string message)
	{
		// Toast is currently not working in MCT on Windows
		if (OperatingSystem.IsWindows())
			return;

		var toast = Toast.Make(message, textSize: 18);

		var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		await toast.Show(cts.Token);
	}

	private void SfSegmentedControl_SelectionChanged(object sender, Syncfusion.Maui.Toolkit.SegmentedControl.SelectionChangedEventArgs e)
    {
		Application.Current!.UserAppTheme = e.NewIndex == 0 ? AppTheme.Light : AppTheme.Dark;
    }
}
