#if BARISTA_PERF && COMET_RUNTIME_DIAGNOSTICS
using System;
using CometBaristaNotes.Data;
using CometComposeProbe.BaristaNotes.Comparison;

namespace CometComposeProbe;

public partial class MainActivity
{
    static WeakReference<MainActivity>? _lifecycleActivity;
    readonly string _lifecycleActivityId = Guid.NewGuid().ToString("N");
    BaristaLifecycleControl? _lifecycleControl;
    string? _lifecycleRootId;

    void PublishLifecycleControl()
    {
        _lifecycleRootId = _baristaHost?.Root?.Id ??
            throw new InvalidOperationException("A logical Barista root is required for lifecycle control.");
        _lifecycleControl = new BaristaLifecycleControl(CaptureLifecycleSnapshot, Recreate);
        _lifecycleActivity = new WeakReference<MainActivity>(this);
        Android.Util.Log.Info("CometLifecycleControl",
            $"Root active: pid={Android.OS.Process.MyPid()} activity={_lifecycleActivityId} root={_lifecycleRootId}");
    }

    void WithdrawLifecycleControl()
    {
        if (_lifecycleActivity?.TryGetTarget(out var current) == true && ReferenceEquals(current, this))
            _lifecycleActivity = null;
        _lifecycleControl = null;
        Android.Util.Log.Info("CometLifecycleControl",
            $"Activity destroying: pid={Android.OS.Process.MyPid()} activity={_lifecycleActivityId} root={_lifecycleRootId}");
    }

    internal static BaristaLifecycleControl CurrentLifecycleControl()
    {
        if (_lifecycleActivity is not { } reference || !reference.TryGetTarget(out var activity) ||
            !BaristaOwnership.IsOwner(activity) || activity._lifecycleControl is null)
            throw new InvalidOperationException("No current diagnostic Barista activity is available.");
        return activity._lifecycleControl;
    }

    BaristaLifecycleSnapshot CaptureLifecycleSnapshot()
    {
        var root = _baristaHost?.Root ??
            throw new InvalidOperationException("The Barista root has been released.");
        var store = root.Services.Store as SqliteDataStore ??
            throw new InvalidOperationException("The Barista root does not have a SQLite store.");
        return new BaristaLifecycleSnapshot(
            Android.OS.Process.MyPid(), _lifecycleActivityId, root.Id, store.DatabasePath,
            !IsFinishing && !IsDestroyed && HasWindowFocus && BaristaOwnership.IsOwner(this) &&
            BaristaFixtureHost.WaitForIdleAsync().IsCompletedSuccessfully,
            false);
    }
}
#endif
