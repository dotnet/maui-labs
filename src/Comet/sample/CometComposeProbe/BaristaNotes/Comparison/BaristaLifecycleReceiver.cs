using System;
using Android.App;
using Android.Content;
using Android.Runtime;
#if BARISTA_PERF && COMET_RUNTIME_DIAGNOSTICS
using System.IO;
using System.Text.Json;
using Android.OS;
#endif

namespace CometComposeProbe.BaristaNotes.Comparison;

// The JNI class is unconditional. Only the diagnostic comparison manifest registers it.
[Register("com/comet/baristacomparison/BaristaLifecycleReceiver")]
#if BARISTA_PERF && COMET_RUNTIME_DIAGNOSTICS
[BroadcastReceiver(Name = "com.comet.baristacomparison.BaristaLifecycleReceiver",
    Enabled = true, Exported = true, Permission = "android.permission.DUMP")]
#endif
public sealed class BaristaLifecycleReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
#if BARISTA_PERF && COMET_RUNTIME_DIAGNOSTICS
        try
        {
            if (context?.PackageName != "com.comet.sample.perf.diag.baristanotes" || intent is null)
                throw new InvalidOperationException("Lifecycle control requires the diagnostic comparison app.");
            if (Looper.MyLooper() != Looper.MainLooper)
                throw new InvalidOperationException("Lifecycle control must run on the Android main thread.");
            var control = MainActivity.CurrentLifecycleControl();
            var result = intent.Action switch
            {
                "com.comet.baristacomparison.LIFECYCLE_STATUS" => control.Snapshot(),
                "com.comet.baristacomparison.RECREATE_ACTIVITY" => control.Request(
                    intent.GetIntExtra("pid", -1), intent.GetStringExtra("activityId"), intent.GetStringExtra("rootId")),
                _ => throw new InvalidDataException("Unknown lifecycle control action."),
            };
            SetResult(Result.Ok, JsonSerializer.Serialize(result, BaristaLifecycleJson.Default.BaristaLifecycleSnapshot), null);
        }
        catch (Exception error)
        {
            Android.Util.Log.Error("CometLifecycleControl", error.ToString());
            SetResult(Result.Canceled, "Rejected: " + error.Message, null);
        }
#else
        SetResult(Result.Canceled, "Lifecycle controls are not enabled.", null);
#endif
    }
}
