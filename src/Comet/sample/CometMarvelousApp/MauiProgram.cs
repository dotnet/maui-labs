using CometMarvelousApp.Pages;
using Microsoft.Maui.Controls.Hosting;

namespace CometMarvelousApp;

public class MarvelousShell : MauiControls.Shell
{
	public MarvelousShell()
	{
		MauiControls.Shell.SetNavBarIsVisible(this, false);

		var shellContent = new MauiControls.ShellContent
		{
			ContentTemplate = new MauiControls.DataTemplate(() =>
			{
				var page = new MauiControls.ContentPage();
				page.SafeAreaEdges = SafeAreaEdges.None;
				var container = new MauiControls.ContentView();
				page.Content = container;
				page.Loaded += (s, e) =>
				{
					if (page.Handler?.MauiContext == null) return;
					var mainPage = new MainPage();
					try
					{
						container.Content = new CometHost(mainPage);
					}
					catch (Exception ex)
					{
						System.Diagnostics.Debug.WriteLine($"[EmbedCometView] Failed: {ex.Message}");
					}
				};
				MauiControls.Shell.SetNavBarIsVisible(page, false);
				return page;
			}),
			Route = "main"
		};

		Items.Add(shellContent);
	}
}

public class MarvelousApp : MauiControls.Application
{
	protected override MauiControls.Window CreateWindow(IActivationState? activationState)
	{
		return new MauiControls.Window(new MarvelousShell());
	}
}

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<MarvelousApp>();
		builder.UseCometHandlers();
		builder.ConfigureFonts(fonts =>
		{
			fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			fonts.AddFont("B612Mono-Regular.ttf", "Mono");
			fonts.AddFont("CinzelDecorative-Black.ttf", "CinzelDecorativeBlack");
			fonts.AddFont("CinzelDecorative-Bold.ttf", "CinzelDecorativeBold");
			fonts.AddFont("CinzelDecorative-Regular.ttf", "CinzelDecorativeRegular");
			fonts.AddFont("Raleway-Bold.ttf", "RalewayBold");
			fonts.AddFont("Raleway-BoldItalic.ttf", "RalewayBoldItalic");
			fonts.AddFont("Raleway-ExtraBold.ttf", "RalewayExtraBold");
			fonts.AddFont("Raleway-ExtraBoldItalic.ttf", "RalewayExtraBoldItalic");
			fonts.AddFont("Raleway-Italic.ttf", "RalewayItalic");
			fonts.AddFont("Raleway-Medium.ttf", "RalewayMedium");
			fonts.AddFont("Raleway-MediumItalic.ttf", "RalewayMediumItalic");
			fonts.AddFont("Raleway-Regular.ttf", "RalewayRegular");
			fonts.AddFont("TenorSans-Regular.ttf", "TenorSans");
			fonts.AddFont("YesevaOne-Regular.ttf", "YesevaOne");
		});

#if DEBUG
		builder.EnableSampleRuntimeDebugging();
#endif

		return builder.Build();
	}
}
