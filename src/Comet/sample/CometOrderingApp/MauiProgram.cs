namespace CometOrderingApp;

public class OrderingApp : CometApp
{
	public OrderingApp()
	{
		Body = CreateRootView;
	}

	public static View CreateRootView() => new Pages.MainPage();

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

#if DEBUG
		builder.UseCometSampleDebugHost(CreateRootView);
#else
		builder.UseCometApp<OrderingApp>();
#endif

		builder.ConfigureFonts(fonts =>
		{
			fonts.AddFont("Mulish-Regular.ttf", "MulishRegular");
			fonts.AddFont("Mulish-SemiBold.ttf", "MulishSemiBold");
			fonts.AddFont("Mulish-Bold.ttf", "MulishBold");
		});

#if DEBUG
		builder.EnableSampleRuntimeDebugging();
#endif

		return builder.Build();
	}
}
