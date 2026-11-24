using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.WPF;
using WpfHandlers = Microsoft.Maui.Handlers.WPF;
using WWindow = System.Windows.Window;

namespace HandlerTests;

[Collection("Window title")]
public class MultiWindowTests
{
	[Fact]
	public void OpenWindow_CreatesNativeWindow_AndCloseWindowClosesIt()
	{
		WpfApplicationHost.Run(nativeApp =>
		{
			DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
			var builder = MauiApp.CreateBuilder().UseMauiAppWPF<Application>();
			builder.Services.AddSingleton(nativeApp);
			using var mauiApp = builder.Build();
			var appContext = new WPFMauiContext(mauiApp.Services);
			var app = (Application)appContext.Services.GetRequiredService<IApplication>();
			var appHandler = new WpfHandlers.ApplicationHandler();
			appHandler.SetMauiContext(appContext);
			appHandler.SetVirtualView(app);

			var window = new Window(new ContentPage { Title = "Second page" }) { Title = "Second window" };
			app.OpenWindow(window);
			var nativeWindow = window.Handler?.PlatformView as WWindow;
			try
			{
				Assert.Contains(window, app.Windows);
				Assert.NotNull(nativeWindow);
				Assert.True(nativeWindow.IsVisible);
				Assert.Equal("Second window", nativeWindow.Title);

				app.CloseWindow(window);
				Assert.False(nativeWindow.IsVisible);
			}
			finally
			{
				nativeWindow?.Close();
			}
		});
	}

	[Fact]
	public void ReplacingWindowPage_RemovesPreviousRootFromNativeWindow()
	{
		WpfApplicationHost.Run(nativeApp =>
		{
			DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
			var builder = MauiApp.CreateBuilder().UseMauiAppWPF<Application>();
			builder.Services.AddSingleton(nativeApp);
			using var mauiApp = builder.Build();
			var appContext = new WPFMauiContext(mauiApp.Services);
			var app = (Application)appContext.Services.GetRequiredService<IApplication>();
			var appHandler = new WpfHandlers.ApplicationHandler();
			appHandler.SetMauiContext(appContext);
			appHandler.SetVirtualView(app);

			var firstPage = new ContentPage { Title = "First" };
			var window = new Window(firstPage);
			app.OpenWindow(window);
			var nativeWindow = window.Handler?.PlatformView as WWindow;
			try
			{
				Assert.NotNull(nativeWindow);
				var firstNative = firstPage.Handler?.PlatformView as System.Windows.UIElement;
				Assert.NotNull(firstNative);

				var secondPage = new ContentPage { Title = "Second" };
				window.Page = secondPage;
				var secondNative = secondPage.Handler?.PlatformView as System.Windows.UIElement;
				Assert.NotNull(secondNative);

				var root = Assert.IsAssignableFrom<System.Windows.Controls.Panel>(System.Windows.Media.VisualTreeHelper.GetParent(secondNative));
				Assert.DoesNotContain(firstNative, root.Children.Cast<System.Windows.UIElement>());
			}
			finally
			{
				nativeWindow?.Close();
			}
		});
	}
}
