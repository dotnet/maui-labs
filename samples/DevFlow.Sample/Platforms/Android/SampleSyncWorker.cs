using Android.Content;
using AndroidX.Work;
using Java.Util.Concurrent;
using Microsoft.Maui.Storage;

namespace DevFlow.Sample;

/// <summary>
/// A real WorkManager Worker so DevFlow's background-job surface has something to inspect,
/// force-run and assert against. Without this the jobs API can only ever return an empty list,
/// which is indistinguishable from a broken query.
///
/// Every run records its outcome in Preferences, which DevFlow can read back over HTTP — that is
/// what turns "the job was triggered" into "the job actually executed and reported success".
/// </summary>
public class SampleSyncWorker : Worker
{
    /// <summary>Unique work name; also used as the tag callers filter on.</summary>
    public const string WorkName = "devflow-sample-sync";

    /// <summary>Input key that makes the worker report failure, for testing the failure path.</summary>
    public const string ShouldFailKey = "shouldFail";

    public const string RunCountPreference = "worker-run-count";
    public const string LastResultPreference = "worker-last-result";
    public const string LastRunPreference = "worker-last-run-utc";

    public SampleSyncWorker(Context context, WorkerParameters workerParams)
        : base(context, workerParams)
    {
    }

    public override Result DoWork()
    {
        var shouldFail = InputData?.GetBoolean(ShouldFailKey, false) ?? false;

        Preferences.Set(RunCountPreference, Preferences.Get(RunCountPreference, 0) + 1);
        Preferences.Set(LastRunPreference, DateTime.UtcNow.ToString("O"));
        Preferences.Set(LastResultPreference, shouldFail ? "failure" : "success");

        return (shouldFail ? Result.InvokeFailure() : Result.InvokeSuccess())!;
    }

    /// <summary>
    /// Enqueues the sample job with a long initial delay so it sits in JobScheduler as pending
    /// rather than running on its own. That is deliberate: it gives
    /// <c>adb shell cmd jobscheduler run -f</c> something to force, and keeps the job from
    /// firing spontaneously in the middle of an unrelated test.
    /// </summary>
    public static void Enqueue(Context context, bool shouldFail = false)
    {
        var data = new Data.Builder()
            .PutBoolean(ShouldFailKey, shouldFail)
            .Build()!;

        // Builder.Build() is typed as the WorkRequest base in the AndroidX binding,
        // but EnqueueUniqueWork requires the OneTimeWorkRequest it actually returns.
        var request = (OneTimeWorkRequest)new OneTimeWorkRequest.Builder(Java.Lang.Class.FromType(typeof(SampleSyncWorker)))
            .SetInputData(data)
            .SetInitialDelay(1, TimeUnit.Days!)
            .AddTag(WorkName)
            .Build()!;

        WorkManager.GetInstance(context)
            .EnqueueUniqueWork(WorkName, ExistingWorkPolicy.Replace, request);
    }
}
