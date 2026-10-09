// LEGACY (parked): this scaffolds the pre-Phase-5 MAUI-hosted Comet model
// (App : CometApp + UseCometApp), which no longer exists in current Comet and
// will NOT compile. See ../../LEGACY-TEMPLATE.md. Use tag comet-pre-phase5-delete.
using Microsoft.Maui.Hosting;

namespace CometApp1;

public class App : CometApp
{
	public App()
	{
		Body = () => new MainPage();
	}

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseCometApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		return builder.Build();
	}
}
