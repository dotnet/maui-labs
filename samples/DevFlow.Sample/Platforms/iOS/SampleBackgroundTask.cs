using System.Runtime.InteropServices;
using BackgroundTasks;
using Foundation;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Storage;
using ObjCRuntime;

namespace DevFlow.Sample;

/// <summary>
/// A real BGTask so DevFlow's background-job surface has something to list, trigger and
/// assert against on iOS — the counterpart to <see cref="SampleSyncWorker"/> on Android.
///
/// <para>
/// Unlike Android, there is no host-side way to force a BGTask: simctl has no equivalent of
/// <c>adb shell cmd jobscheduler run</c>. <c>BGTaskScheduler.Submit</c> only *schedules*; iOS
/// decides if and when to launch, and organically that essentially never happens on a Simulator.
/// The only trigger is the private <c>_simulateLaunchForTaskWithIdentifier:</c> selector that
/// Apple documents for use from the debugger.
/// </para>
/// </summary>
public static class SampleBackgroundTask
{
    public const string SyncTaskIdentifier = "com.companyname.mauitodo.sync";

    public const string RunCountPreference = "bgtask-run-count";
    public const string LastRunPreference = "bgtask-last-run-utc";

    /// <summary>
    /// Registers the launch handler. Must be called before didFinishLaunchingWithOptions returns,
    /// or BGTaskScheduler throws when the identifier is later submitted.
    /// </summary>
    public static void Register()
    {
        BGTaskScheduler.Shared.Register(SyncTaskIdentifier, null, task =>
        {
            Preferences.Set(RunCountPreference, Preferences.Get(RunCountPreference, 0) + 1);
            Preferences.Set(LastRunPreference, DateTime.UtcNow.ToString("O"));
            Console.WriteLine($"[DevFlow.Sample] BGTask '{SyncTaskIdentifier}' executed.");
            task.SetTaskCompleted(true);
        });
    }

    /// <summary>Submits the task with a far-future begin date so it stays pending until forced.</summary>
    public static string Schedule()
    {
        var request = new BGProcessingTaskRequest(SyncTaskIdentifier)
        {
            EarliestBeginDate = (NSDate)DateTime.Now.AddDays(1),
            RequiresNetworkConnectivity = false,
            RequiresExternalPower = false
        };

        BGTaskScheduler.Shared.Submit(request, out var error);
        return error != null ? $"submit failed: {error.LocalizedDescription}" : "submitted";
    }

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendSimulateLaunch(IntPtr receiver, IntPtr selector, IntPtr identifier);

    /// <summary>
    /// Invokes the private SPI that Apple documents for triggering a BGTask from LLDB, but from
    /// inside the process. This is the only thing that actually runs a BGTask on demand.
    /// Probes with RespondsToSelector first so an OS that drops the SPI degrades to a clear
    /// message instead of crashing.
    /// </summary>
    [DevFlowAction("bgtask-simulate-launch", Description = "Force a registered BGTask to run now (debug only; uses a private selector)")]
    public static string SimulateLaunch(
        [System.ComponentModel.Description("BGTask identifier to launch")] string identifier = SyncTaskIdentifier)
    {
        var selector = new Selector("_simulateLaunchForTaskWithIdentifier:");
        var scheduler = BGTaskScheduler.Shared;

        if (!scheduler.RespondsToSelector(selector))
            return "unavailable: BGTaskScheduler does not respond to _simulateLaunchForTaskWithIdentifier:";

        using var nsIdentifier = new NSString(identifier);
        SendSimulateLaunch(scheduler.Handle, selector.Handle, nsIdentifier.Handle);
        return $"invoked _simulateLaunchForTaskWithIdentifier: for '{identifier}'";
    }

    [DevFlowAction("bgtask-schedule", Description = "Submit the sample BGTask request so it becomes pending")]
    public static string ScheduleAction() => Schedule();

    [DevFlowAction("bgtask-run-count", Description = "How many times the sample BGTask handler has executed")]
    public static string RunCount() =>
        $"{Preferences.Get(RunCountPreference, 0)} run(s), last at {Preferences.Get(LastRunPreference, "never")}";
}
