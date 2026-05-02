using Comet;
using CometRecipeApp.Pages;
using Microsoft.Maui.Hosting;

namespace CometRecipeApp;

public class RecipeApp : CometApp
{
	public RecipeApp()
	{
		Body = CreateRootView;
	}

	public static View CreateRootView() => new MainPage();

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

#if DEBUG
		builder.UseCometSampleDebugHost(CreateRootView);
#else
		builder.UseCometApp<RecipeApp>();
#endif

		builder.ConfigureFonts(fonts =>
		{
			fonts.AddFont("Rubik-Bold.ttf", "RubikBold");
			fonts.AddFont("Rubik-Light.ttf", "RubikLight");
			fonts.AddFont("Rubik-Medium.ttf", "RubikMedium");
			fonts.AddFont("Rubik-Regular.ttf", "RubikRegular");
		});

#if DEBUG
		builder.EnableSampleRuntimeDebugging();
#endif

		return builder.Build();
	}
}
