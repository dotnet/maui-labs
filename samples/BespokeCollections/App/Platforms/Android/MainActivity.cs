using Android.App;
using Android.Content.PM;
using Android.OS;

namespace BespokeCollections.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        PrototypeLaunch.RunProof = Intent?.GetBooleanExtra("run-proof", false) == true;
        PrototypeLaunch.CaptureScrollWorkload = Intent?.GetBooleanExtra("scroll-workload", false) == true;
        base.OnCreate(savedInstanceState);
    }
}
