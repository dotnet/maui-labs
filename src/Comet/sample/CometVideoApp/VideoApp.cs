using Comet.Styles;

namespace CometVideoApp;

public class VideoApp : CometApp
{
	public VideoApp()
	{
		Body = CreateRootView;
	}

	public static View CreateRootView() => new VideoFeedPage();

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

#if DEBUG
		builder.UseCometSampleDebugHost(CreateRootView);
#else
		builder.UseCometApp<VideoApp>();
#endif

		builder.ConfigureFonts(fonts =>
		{
			fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			fonts.AddFont("OpenSans-SemiBold.ttf", "OpenSansSemiBold");
		});

		ThemeManager.SetTheme(Defaults.Dark);

#if DEBUG
		builder.EnableSampleRuntimeDebugging();
#endif

		return builder.Build();
	}
}
