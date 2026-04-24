using CometWeather.Pages;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using MauiPage = Microsoft.Maui.Controls.ContentPage;
using MauiShell = Microsoft.Maui.Controls.Shell;

namespace CometWeather;

public class WeatherShell : MauiShell
{
    public WeatherShell()
    {
        MauiShell.SetNavBarIsVisible(this, false);

        var tabBar = new TabBar();

        tabBar.Items.Add(new Microsoft.Maui.Controls.ShellContent
        {
            Title = "Home",
            Icon = "tab_home.png",
            ContentTemplate = new DataTemplate(() => MakeCometPage(new HomePage(), "Home")),
            Route = "home"
        });

        tabBar.Items.Add(new Microsoft.Maui.Controls.ShellContent
        {
            Title = "Favorites",
            Icon = "tab_favorites.png",
            ContentTemplate = new DataTemplate(() => MakeCometPage(new FavoritesPage(), "Favorites")),
            Route = "favorites"
        });

        tabBar.Items.Add(new Microsoft.Maui.Controls.ShellContent
        {
            Title = "Settings",
            Icon = "tab_settings.png",
            ContentTemplate = new DataTemplate(() => MakeCometPage(new SettingsPage(), "Settings")),
            Route = "settings"
        });

        Items.Add(tabBar);

        // Apply initial Shell chrome colors
        ApplyThemeToShell();

        // Re-apply when theme changes
        WeatherPreferences.SettingsChanged += () => ApplyThemeToShell();
    }

    void ApplyThemeToShell()
    {
        var bg = WeatherPreferences.Background;
        MauiShell.SetBackgroundColor(this, bg);
        Shell.SetTabBarBackgroundColor(this, bg);
        Shell.SetTabBarUnselectedColor(this, WeatherPreferences.TextSecondary);
        Shell.SetTabBarTitleColor(this, WeatherPreferences.Accent);
        Shell.SetTabBarForegroundColor(this, WeatherPreferences.Accent);
    }

    static MauiPage MakeCometPage(Comet.View cometView, string title)
    {
        var page = new MauiPage
        {
            Title = title,
            BackgroundColor = WeatherPreferences.Background,
        };

        var container = new Microsoft.Maui.Controls.ContentView
        {
            BackgroundColor = WeatherPreferences.Background,
        };

        page.Content = container;

        page.Loaded += (s, e) =>
        {
            if (page.Handler?.MauiContext == null) return;
            EmbedCometView(container, cometView, page.Handler.MauiContext);
        };

        // Update page background when theme changes
        WeatherPreferences.SettingsChanged += () =>
        {
            page.BackgroundColor = WeatherPreferences.Background;
            container.BackgroundColor = WeatherPreferences.Background;
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
        return new Window(new WeatherShell());
    }
}

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<ShellMauiApp>();
        builder.UseCometHandlers();
        builder.ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-SemiBold.ttf", "OpenSansSemibold");
            fonts.AddFont("fa-solid-900.ttf", "FASolid");
        });

#if DEBUG
        builder.EnableSampleRuntimeDebugging();
#endif

        return builder.Build();
    }
}
