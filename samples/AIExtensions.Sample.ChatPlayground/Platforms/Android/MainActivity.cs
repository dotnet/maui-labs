using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Views;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Android activity entry point.</summary>
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnResume()
    {
        base.OnResume();
        UpdateStatusBarIcons();
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        UpdateStatusBarIcons();
    }

    private void UpdateStatusBarIcons()
    {
        // Android draws the status bar over the page, so its icons must follow the page's theme.
        var configuration = Resources?.Configuration
            ?? throw new InvalidOperationException("Android configuration is unavailable.");
        var window = Window ?? throw new InvalidOperationException("Android window is unavailable.");
        var isLight = (configuration.UiMode & UiMode.NightMask) == UiMode.NightNo;
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && window.InsetsController is { } controller)
        {
            var lightStatusBar = (int)WindowInsetsControllerAppearance.LightStatusBars;
            controller.SetSystemBarsAppearance(isLight ? lightStatusBar : 0, lightStatusBar);
        }
        else
        {
            var decorView = window.DecorView
                ?? throw new InvalidOperationException("Android decor view is unavailable.");
#pragma warning disable CS0618 // SystemUiVisibility is required before WindowInsetsController (API 30).
            var flags = (SystemUiFlags)decorView.SystemUiVisibility;
            decorView.SystemUiVisibility = (StatusBarVisibility)(isLight
                ? flags | SystemUiFlags.LightStatusBar
                : flags & ~SystemUiFlags.LightStatusBar);
#pragma warning restore CS0618
        }
    }
}
