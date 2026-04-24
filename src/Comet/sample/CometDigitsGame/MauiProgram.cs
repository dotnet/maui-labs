namespace CometDigitsGame;

public class DigitsGameApp : CometApp
{
	public DigitsGameApp()
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
		builder.UseCometApp<DigitsGameApp>();
#endif

		builder.ConfigureFonts(fonts =>
		{
			fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			fonts.AddFont("OpenSans-SemiBold.ttf", "OpenSansSemibold");
		});

#if DEBUG
		builder.EnableSampleRuntimeDebugging();
#endif

		return builder.Build();
	}
}
