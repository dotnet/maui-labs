#nullable enable
using System;
using System.IO;
using System.Text.Json;
using CometComposeProbe.BaristaNotes.Comparison;
using Xunit;

namespace Comet.Tests.BaristaFixtures;

public sealed class BaristaLifecycleControlTests
{
    static BaristaLifecycleSnapshot Active(string activity = "activity-1", string root = "root-1") =>
        new(123, activity, root, "/selected/barista_notes.db", true, false);

    [Fact]
    public void MatchingIdentity_RequestsExactlyOnce_AndDoesNotClaimCompletion()
    {
        var calls = 0;
        var control = new BaristaLifecycleControl(() => Active(), () => calls++);
        Assert.True(control.Snapshot().CanRecreate);
        var result = control.Request(123, "activity-1", "root-1");
        Assert.Equal(1, calls);
        Assert.Equal("activity-1", result.ActivityId);
        Assert.Equal("root-1", result.RootId);
        Assert.True(result.RecreateRequested);
        Assert.False(result.CanRecreate);
        Assert.Throws<InvalidOperationException>(() => control.Request(123, "activity-1", "root-1"));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(-1, "activity-1", "root-1")]
    [InlineData(999, "activity-1", "root-1")]
    [InlineData(123, null, "root-1")]
    [InlineData(123, "", "root-1")]
    [InlineData(123, "old-activity", "root-1")]
    [InlineData(123, "activity-1", null)]
    [InlineData(123, "activity-1", "")]
    [InlineData(123, "activity-1", "old-root")]
    public void StaleOrMissingIdentity_DoesNotInvokeRecreate(int pid, string? activity, string? root)
    {
        var calls = 0;
        var control = new BaristaLifecycleControl(() => Active(), () => calls++);
        Assert.Throws<InvalidDataException>(() => control.Request(pid, activity, root));
        Assert.Equal(0, calls);
        Assert.False(control.Snapshot().RecreateRequested);
    }

    [Fact]
    public void BusyOrInactiveTarget_DoesNotInvokeRecreate()
    {
        var control = new BaristaLifecycleControl(() => Active() with { CanRecreate = false },
            () => throw new Exception("Must not execute."));
        Assert.Throws<InvalidOperationException>(() => control.Request(123, "activity-1", "root-1"));
        Assert.False(control.Snapshot().RecreateRequested);
    }

    [Fact]
    public void NativeFailure_Propagates_AndIsNotRetried()
    {
        var failure = new IOException("Native failure");
        var calls = 0;
        var control = new BaristaLifecycleControl(() => Active(), () => { calls++; throw failure; });
        Assert.Same(failure, Assert.Throws<IOException>(() => control.Request(123, "activity-1", "root-1")));
        Assert.Throws<InvalidOperationException>(() => control.Request(123, "activity-1", "root-1"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ReplacementInSameProcess_ReportsNewIdentities_AndRejectsOldRequest()
    {
        var next = new BaristaLifecycleControl(() => Active("activity-2", "root-2"),
            () => throw new Exception("Old request must not reach the replacement."));
        var status = next.Snapshot();
        Assert.Equal(123, status.ProcessId);
        Assert.Equal("/selected/barista_notes.db", status.DatabasePath);
        Assert.Equal("activity-2", status.ActivityId);
        Assert.Equal("root-2", status.RootId);
        Assert.Throws<InvalidDataException>(() => next.Request(123, "activity-1", "root-1"));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(status, BaristaLifecycleJson.Default.BaristaLifecycleSnapshot);
        Assert.Equal(status, JsonSerializer.Deserialize(bytes, BaristaLifecycleJson.Default.BaristaLifecycleSnapshot));
    }
}
