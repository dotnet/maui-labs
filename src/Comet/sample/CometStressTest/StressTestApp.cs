using CometStressTest.Pages;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using MauiPage = Microsoft.Maui.Controls.ContentPage;
using MauiShell = Microsoft.Maui.Controls.Shell;

namespace CometStressTest;

public class StressTestShell : MauiShell
{
	public StressTestShell()
	{
		MauiShell.SetNavBarIsVisible(this, false);

		var tabBar = new TabBar();

		tabBar.Items.Add(new Microsoft.Maui.Controls.ShellContent
		{
			Title = "Lists",
			Icon = "tab_lists.png",
			ContentTemplate = new DataTemplate(() => MakeCometPage(new ListTestPage(), "Lists")),
			Route = "lists"
		});

		tabBar.Items.Add(new Microsoft.Maui.Controls.ShellContent
		{
			Title = "Collections",
			Icon = "tab_collections.png",
			ContentTemplate = new DataTemplate(() => MakeCometPage(new CollectionTestPage(), "Collections")),
			Route = "collections"
		});

		tabBar.Items.Add(new Microsoft.Maui.Controls.ShellContent
		{
			Title = "Layouts",
			Icon = "tab_layouts.png",
			ContentTemplate = new DataTemplate(() => MakeCometPage(new LayoutTestPage(), "Layouts")),
			Route = "layouts"
		});

		tabBar.Items.Add(new Microsoft.Maui.Controls.ShellContent
		{
			Title = "Controls",
			Icon = "tab_controls.png",
			ContentTemplate = new DataTemplate(() => MakeCometPage(new ControlTestPage(), "Controls")),
			Route = "controls"
		});

		tabBar.Items.Add(new Microsoft.Maui.Controls.ShellContent
		{
			Title = "State",
			Icon = "tab_state.png",
			ContentTemplate = new DataTemplate(() => MakeCometPage(new StateTestPage(), "State")),
			Route = "state"
		});

		tabBar.Items.Add(new Microsoft.Maui.Controls.ShellContent
		{
			Title = "Swipe",
			Icon = "tab_swipe.png",
			ContentTemplate = new DataTemplate(() => MakeCometPage(new SwipeTestPage(), "Swipe")),
			Route = "swipe"
		});

		Items.Add(tabBar);
	}

	static MauiPage MakeCometPage(Comet.View cometView, string title)
	{
		var page = new MauiPage
		{
			Title = title,
			BackgroundColor = Colors.White,
		};

		var container = new Microsoft.Maui.Controls.ContentView();

		page.Content = container;

		page.Loaded += (s, e) =>
		{
			if (page.Handler?.MauiContext == null) return;
			EmbedCometView(container, cometView, page.Handler.MauiContext);
		};

		MauiShell.SetNavBarIsVisible(page, false);
		return page;
	}

	internal static void EmbedCometView(
		Microsoft.Maui.Controls.ContentView container,
		Comet.View cometView,
		IMauiContext mauiContext)
	{
		try
		{
			var renderView = cometView.GetView();
			IView viewToRender = (renderView != null && renderView != cometView) ? renderView : cometView;

			if (viewToRender is MauiViewHost mvh)
			{
				var hostedView = mvh.HostedView;
				if (hostedView is Microsoft.Maui.Controls.View mauiView)
				{
					container.Content = mauiView;
					return;
				}
			}

			container.Content = new CometHost(cometView);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[EmbedCometView] Failed: {ex.Message}");
		}
	}
}

public class ShellMauiApp : Application
{
	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new StressTestShell());
	}
}

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<ShellMauiApp>();
		builder.UseCometHandlers();
#if DEBUG
		builder.EnableSampleRuntimeDebugging();
#endif
		return builder.Build();
	}
}
