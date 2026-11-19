using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.WPF;
using WWindow = System.Windows.Window;

namespace HandlerTests;

[CollectionDefinition("Window title", DisableParallelization = true)]
public class WindowTitleCollection;

[Collection("Window title")]
public class WindowTitleTests
{
	[Fact]
	public void WindowTitle_IsOwnedByEachWindow_NotPagesOrNavigation()
	{
		WpfApplicationHost.Run(nativeApp =>
		{
			DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
			var nativeMain = new WWindow();
			try
			{
				nativeApp.MainWindow = nativeMain;
				using var mainApp = CreateApp(nativeMain);
				var mainContext = new WPFMauiContext(mainApp.Services);
				var home = new ContentPage { Title = "Home" };
				var navigation = new NavigationPage(home);
				var main = new Window(navigation) { Title = "Application title" };
				main.ToHandler(mainContext);
				try
				{
					Assert.Equal(main.Title, nativeMain.Title);
					var originalHandler = home.Handler;
					Assert.NotNull(originalHandler);
					var detail = new ContentPage { Title = "Details" };
					navigation.PushAsync(detail, false).GetAwaiter().GetResult();
					Assert.Same(detail, navigation.CurrentPage);
					Assert.Equal(main.Title, nativeMain.Title);

					detail.Title = "Renamed details";
					Assert.Equal(main.Title, nativeMain.Title);
					navigation.PopAsync(false).GetAwaiter().GetResult();
					Assert.Same(home, navigation.CurrentPage);
					Assert.Same(originalHandler, home.Handler);
					Assert.Equal(main.Title, nativeMain.Title);
					home.Title = "Renamed home";
					Assert.Equal(main.Title, nativeMain.Title);
					home.Title = null;
					Assert.Equal(main.Title, nativeMain.Title);
					home.Title = string.Empty;
					Assert.Equal(main.Title, nativeMain.Title);

					main.Title = "Updated application title";
					Assert.Equal(main.Title, nativeMain.Title);

					var nativeSecond = new WWindow();
					try
					{
						using var secondApp = CreateApp(nativeSecond);
						var secondPage = new ContentPage { Title = "Second page" };
						var second = new Window(secondPage) { Title = "Second window" };
						second.ToHandler(new WPFMauiContext(secondApp.Services));
						try
						{
							secondPage.Title = "Updated second page";
							Assert.Equal(main.Title, nativeMain.Title);
							Assert.Equal(second.Title, nativeSecond.Title);
							second.Title = "Updated second window";
							Assert.Equal(second.Title, nativeSecond.Title);
							Assert.Equal(main.Title, nativeMain.Title);

							foreach (var emptyTitle in new string?[] { "", null })
							{
								second.Title = emptyTitle;
								secondPage.Title = $"Page with {emptyTitle ?? "null"} window title";
								Assert.Equal(string.Empty, nativeSecond.Title);
								Assert.Equal(main.Title, nativeMain.Title);
							}
						}
						finally
						{
							DrainDispatcher();
							secondPage.Handler?.DisconnectHandler();
							second.Handler?.DisconnectHandler();
						}
					}
					finally
					{
						nativeSecond.Close();
					}
				}
				finally
				{
					DrainDispatcher();
					foreach (var page in navigation.Navigation.NavigationStack)
						page.Handler?.DisconnectHandler();
					navigation.Handler?.DisconnectHandler();
					main.Handler?.DisconnectHandler();
				}
			}
			finally
			{
				nativeMain.Close();
			}
		});

		WpfApplicationHost.Run(_ =>
		{
			var window = new WWindow
			{
				Width = 100,
				Height = 100,
				Left = -10000,
				Top = -10000,
				ShowActivated = false,
				ShowInTaskbar = false,
			};
			try
			{
				window.Show();
				DrainDispatcher();
				Assert.True(window.IsVisible, "Title test teardown must not disable later native windows.");
				Assert.True(window.ActualWidth > 0);
			}
			finally
			{
				window.Close();
			}
		});
	}

	static void DrainDispatcher()
	{
		var frame = new System.Windows.Threading.DispatcherFrame();
		System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
			System.Windows.Threading.DispatcherPriority.ContextIdle,
			new Action(() => frame.Continue = false));
		System.Windows.Threading.Dispatcher.PushFrame(frame);
	}

	static MauiApp CreateApp(WWindow window)
	{
		var builder = MauiApp.CreateBuilder().UseMauiAppWPF<Application>();
		builder.Services.AddSingleton(window);
		return builder.Build();
	}
}
