#nullable enable
using System;
using System.IO;
using System.Text.Json.Serialization;

namespace CometComposeProbe.BaristaNotes.Comparison;

internal sealed record BaristaLifecycleSnapshot(
    int ProcessId, string ActivityId, string RootId, string DatabasePath,
    bool CanRecreate, bool RecreateRequested);

internal sealed class BaristaLifecycleControl(Func<BaristaLifecycleSnapshot> capture, Action recreate)
{
    bool _requested;

    internal BaristaLifecycleSnapshot Snapshot()
    {
        var current = capture();
        return current with { CanRecreate = current.CanRecreate && !_requested, RecreateRequested = _requested };
    }

    internal BaristaLifecycleSnapshot Request(int processId, string? activityId, string? rootId)
    {
        var current = Snapshot();
        if (processId <= 0 || processId != current.ProcessId ||
            string.IsNullOrEmpty(activityId) || activityId != current.ActivityId ||
            string.IsNullOrEmpty(rootId) || rootId != current.RootId)
            throw new InvalidDataException("The request does not match the current process, activity and root.");
        if (!current.CanRecreate)
            throw new InvalidOperationException("The current activity is not ready for recreation.");

        // A failed native call is surfaced, but never retried by this activity's control.
        _requested = true;
        recreate();
        return current with { CanRecreate = false, RecreateRequested = true };
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BaristaLifecycleSnapshot))]
internal partial class BaristaLifecycleJson : JsonSerializerContext;
