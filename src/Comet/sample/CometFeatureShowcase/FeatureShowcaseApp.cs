using CometFeatureShowcase.Pages;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using MauiPage = Microsoft.Maui.Controls.ContentPage;
using MauiShell = Microsoft.Maui.Controls.Shell;

namespace CometFeatureShowcase;

public class FeatureShowcaseShell : MauiShell
{
    public FeatureShowcaseShell()
    {
        MauiShell.SetNavBarIsVisible(this, false);

        var tabBar = new TabBar();

        tabBar.Items.Add(new Microsoft.Maui.Controls.ShellContent
        {
            Title = "BindableLayout",
            ContentTemplate = new DataTemplate(() => MakeCometPage(new HomePage(), "BindableLayout")),
            Route = "home"
        });

        tabBar.Items.Add(new Microsoft.Maui.Controls.ShellContent
        {
            Title = "Converters",
            ContentTemplate = new DataTemplate(() => MakeCometPage(new DataPage(), "Converters")),
            Route = "data"
        });

        tabBar.Items.Add(new Microsoft.Maui.Controls.ShellContent
        {
            Title = "Animation",
            ContentTemplate = new DataTemplate(() => MakeCometPage(new AnimationPage(), "Animation")),
            Route = "animation"
        });

        tabBar.Items.Add(new Microsoft.Maui.Controls.ShellContent
        {
            Title = "TabView",
            ContentTemplate = new DataTemplate(() => MakeCometPage(new TabDemoPage(), "TabView")),
            Route = "tabdemo"
        });

        tabBar.Items.Add(new Microsoft.Maui.Controls.ShellContent
        {
            Title = "Scroll",
            ContentTemplate = new DataTemplate(() => MakeCometPage(new ScrollPage(), "Scroll")),
            Route = "scroll"
        });

        Items.Add(tabBar);
    }

    static MauiPage MakeCometPage(Comet.View cometView, string title)
    {
        var page = new MauiPage
        {
            Title = title,
            BackgroundColor = Color.FromArgb("#F5F5F5"),
        };

        var container = new Microsoft.Maui.Controls.ContentView
        {
            BackgroundColor = Color.FromArgb("#F5F5F5"),
        };

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

public class FeatureShowcaseMauiApp : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new FeatureShowcaseShell());
    }
}

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<FeatureShowcaseMauiApp>();
        builder.UseCometHandlers();
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
