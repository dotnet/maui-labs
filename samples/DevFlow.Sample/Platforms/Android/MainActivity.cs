using Android.App;
using Android.Content.PM;
using Android.OS;

namespace DevFlow.Sample;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Queue a real background job on every launch so the DevFlow jobs API always has
        // something to list and force-run. Enqueued as unique/Replace, so relaunching does
        // not pile up duplicates.
        try
        {
            SampleSyncWorker.Enqueue(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DevFlow.Sample] Failed to enqueue {SampleSyncWorker.WorkName}: {ex}");
        }
    }
}
