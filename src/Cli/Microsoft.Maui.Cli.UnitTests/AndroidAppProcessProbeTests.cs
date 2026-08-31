using Microsoft.Maui.Cli.DevFlow.Execution;
using Microsoft.Maui.DevFlow.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Pins the Android exit-record and crash-buffer parsing against output captured verbatim from a
/// real API 35 emulator, so a format the parser has never actually seen cannot be mistaken for a
/// format it handles.
/// </summary>
public class AndroidAppProcessProbeTests
{
    /// <summary>
    /// Verbatim <c>adb shell dumpsys activity exit-info &lt;package&gt;</c> output from an API 35
    /// emulator. Record #0 is a genuine crash induced with <c>am crash</c>; record #1 is a
    /// deliberate <c>am force-stop</c>. Only the package name is substituted.
    /// </summary>
    private const string RealDeviceDump = """
        ACTIVITY MANAGER PROCESS EXIT INFO (dumpsys activity exit-info)
        Last Timestamp of Persistence Into Persistent Storage: 2026-08-17 14:37:14.486
          package: com.contoso.app
            Historical Process Exit for uid=10212
                ApplicationExitInfo #0:
                  timestamp=2026-08-17 14:42:36.633 pid=14277 realUid=10212 packageUid=10212 definingUid=10212 user=0
                  process=com.contoso.app reason=4 (APP CRASH(EXCEPTION)) subreason=0 (UNKNOWN) status=0
                  importance=100 pss=0.00 rss=0.00 description=crash state=empty trace=null
                ApplicationExitInfo #1:
                  timestamp=2026-08-17 14:32:16.385 pid=11897 realUid=10212 packageUid=10212 definingUid=10212 user=0
                  process=com.contoso.app reason=10 (USER REQUESTED) subreason=21 (FORCE STOP) status=0
                  importance=100 pss=0.00 rss=268MB description=stop com.contoso.app due to from pid 12124 state=empty trace=null
        """;

    /// <summary>
    /// Verbatim <c>adb logcat -b crash --pid=14277</c> output for the same crash, after the probe's
    /// banner filtering.
    /// </summary>
    private static readonly string[] RealCrashLog =
    [
        "08-17 14:42:36.522 14277 14277 E AndroidRuntime: FATAL EXCEPTION: main",
        "08-17 14:42:36.522 14277 14277 E AndroidRuntime: Process: com.contoso.app, PID: 14277",
        "08-17 14:42:36.522 14277 14277 E AndroidRuntime: android.app.RemoteServiceException$CrashedByAdbException: shell-induced crash",
        "08-17 14:42:36.522 14277 14277 E AndroidRuntime: \tat android.app.ActivityThread.throwRemoteServiceException(ActivityThread.java:2257)",
        "08-17 14:42:36.522 14277 14277 E AndroidRuntime: \tat android.os.Looper.loop(Looper.java:317)",
    ];

    [Fact]
    public void ParseExitReason_RealCrashRecord_IsReadAsACrash()
        => Assert.Equal(MauiFlowAppExitReasons.Crash, AndroidAppProcessProbe.ParseExitReason(RealDeviceDump, 14277));

    [Fact]
    public void ParseExitReason_RealForceStopRecord_IsNotACrash()
    {
        var reason = AndroidAppProcessProbe.ParseExitReason(RealDeviceDump, 11897);

        Assert.Equal(MauiFlowAppExitReasons.UserRequested, reason);
        Assert.False(MauiFlowFailureClassifier.ProvesAppCrash(new MauiFlowFailureFacts
        {
            AppProcessExited = true,
            AppExitReason = reason,
        }));
    }

    /// <summary>
    /// <c>subreason=</c> uses a completely different code space; reading it as the exit reason would
    /// invent crashes. Here subreason 4 would map to "crash" if the parser were sloppy.
    /// </summary>
    [Fact]
    public void ParseExitReason_SubreasonIsNeverReadAsTheReason()
    {
        const string dump = """
            ApplicationExitInfo #0:
              timestamp=2026-08-17 14:32:16.385 pid=900 realUid=10212 user=0
              process=com.contoso.app reason=10 (USER REQUESTED) subreason=4 (SOMETHING) status=0
            """;

        Assert.Equal(MauiFlowAppExitReasons.UserRequested, AndroidAppProcessProbe.ParseExitReason(dump, 900));
    }

    /// <summary>
    /// The real dump's free-text <c>description=</c> mentions an unrelated process id. Binding the
    /// pid to the timestamp line keeps that from being treated as an exit record of its own.
    /// </summary>
    [Fact]
    public void ParseExitReason_ProcessIdMentionedInFreeText_IsNotTreatedAsARecord()
        => Assert.Null(AndroidAppProcessProbe.ParseExitReason(RealDeviceDump, 12124));

    [Fact]
    public void ParseExitReason_UnknownProcess_ReportsNothingRatherThanGuessing()
        => Assert.Null(AndroidAppProcessProbe.ParseExitReason(RealDeviceDump, 9999));

    [Fact]
    public void ParseExitReason_EmptyOrUnrecognisedOutput_ReportsNothing()
    {
        Assert.Null(AndroidAppProcessProbe.ParseExitReason(null, 14277));
        Assert.Null(AndroidAppProcessProbe.ParseExitReason("   ", 14277));
        Assert.Null(AndroidAppProcessProbe.ParseExitReason("ACTIVITY MANAGER PROCESS EXIT INFO", 14277));
    }

    [Fact]
    public void ParseExitReason_LastRecordInTheDump_IsStillRead()
    {
        const string dump = """
            ApplicationExitInfo #0:
              timestamp=2026-08-17 14:42:36.633 pid=77 realUid=10212 user=0
              process=com.contoso.app reason=6 (ANR) subreason=0 (UNKNOWN) status=0
            """;

        Assert.Equal(MauiFlowAppExitReasons.Anr, AndroidAppProcessProbe.ParseExitReason(dump, 77));
    }

    [Theory]
    [InlineData(1, MauiFlowAppExitReasons.ExitSelf)]
    [InlineData(2, MauiFlowAppExitReasons.Signaled)]
    [InlineData(3, MauiFlowAppExitReasons.LowMemory)]
    [InlineData(4, MauiFlowAppExitReasons.Crash)]
    [InlineData(5, MauiFlowAppExitReasons.CrashNative)]
    [InlineData(6, MauiFlowAppExitReasons.Anr)]
    [InlineData(10, MauiFlowAppExitReasons.UserRequested)]
    [InlineData(11, MauiFlowAppExitReasons.UserRequested)]
    [InlineData(9, MauiFlowAppExitReasons.Unknown)]
    [InlineData(13, MauiFlowAppExitReasons.Unknown)]
    [InlineData(99, MauiFlowAppExitReasons.Unknown)]
    public void ParseExitReason_MapsDocumentedReasonCodesOntoTheNeutralVocabulary(int code, string expected)
    {
        var dump = $"""
            ApplicationExitInfo #0:
              timestamp=2026-08-17 14:42:36.633 pid=55 realUid=10212 user=0
              process=com.contoso.app reason={code} (WHATEVER THE PLATFORM CALLS IT) subreason=0 (UNKNOWN) status=0
            """;

        Assert.Equal(expected, AndroidAppProcessProbe.ParseExitReason(dump, 55));
    }

    /// <summary>
    /// Only the crash reasons are proof. Every other way a process can end leaves the classifier
    /// unconvinced, which is what keeps a stopped app from being reported as a regression.
    /// </summary>
    [Theory]
    [InlineData(MauiFlowAppExitReasons.Crash, true)]
    [InlineData(MauiFlowAppExitReasons.CrashNative, true)]
    [InlineData(MauiFlowAppExitReasons.Anr, true)]
    [InlineData(MauiFlowAppExitReasons.UserRequested, false)]
    [InlineData(MauiFlowAppExitReasons.Signaled, false)]
    [InlineData(MauiFlowAppExitReasons.ExitSelf, false)]
    [InlineData(MauiFlowAppExitReasons.LowMemory, false)]
    [InlineData(MauiFlowAppExitReasons.Unknown, false)]
    public void ExitReasonVocabulary_OnlyCrashReasonsAreProof(string reason, bool proves)
        => Assert.Equal(proves, MauiFlowFailureClassifier.ProvesAppCrash(new MauiFlowFailureFacts
        {
            AppProcessExited = true,
            AppExitReason = reason,
        }));

    [Fact]
    public void SelectSignature_RealCrashLog_PicksTheExceptionRatherThanTheBanner()
        => Assert.Equal(
            "android.app.RemoteServiceException$CrashedByAdbException: shell-induced crash",
            AndroidAppProcessProbe.SelectSignature(RealCrashLog));

    [Fact]
    public void SelectSignature_NoExceptionLineAfterTheBanner_FallsBackToTheBanner()
        => Assert.Equal(
            "FATAL EXCEPTION: main",
            AndroidAppProcessProbe.SelectSignature(
            [
                "08-17 14:42:36.522 14277 14277 E AndroidRuntime: FATAL EXCEPTION: main",
                "08-17 14:42:36.522 14277 14277 E AndroidRuntime: Process: com.contoso.app, PID: 14277",
            ]));

    [Fact]
    public void SelectSignature_NoFatalExceptionAtAll_UsesTheFirstLine()
        => Assert.Equal(
            "some other crash buffer content",
            AndroidAppProcessProbe.SelectSignature(
                ["08-17 14:42:36.522 14277 14277 E AndroidRuntime: some other crash buffer content"]));

    [Fact]
    public void SelectSignature_EmptyLog_HasNoSignature()
        => Assert.Null(AndroidAppProcessProbe.SelectSignature([]));

    /// <summary>
    /// Exit records outlive the run that produced them and Linux recycles pids, so a pid match on
    /// its own is not proof the record belongs to this run. Attributing a previous run's crash to
    /// this one would be exactly the false accusation the evidence rule exists to prevent.
    /// </summary>
    [Fact]
    public void ParseExitReason_RecordOlderThanTheRun_IsNotAttributedToIt()
    {
        var deviceNow = AndroidAppProcessProbe.ParseDeviceTimestamp("2026-08-17 18:00:00");
        Assert.Null(AndroidAppProcessProbe.ParseExitReason(
            RealDeviceDump,
            14277,
            deviceNow,
            TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void ParseExitReason_RecordInsideTheRunWindow_IsStillReadAsACrash()
    {
        var deviceNow = AndroidAppProcessProbe.ParseDeviceTimestamp("2026-08-17 14:43:10");
        Assert.Equal(
            MauiFlowAppExitReasons.Crash,
            AndroidAppProcessProbe.ParseExitReason(RealDeviceDump, 14277, deviceNow, TimeSpan.FromMinutes(10)));
    }

    /// <summary>
    /// A record with no readable timestamp cannot be shown to belong to this run, so it is
    /// discarded rather than assumed current.
    /// </summary>
    [Fact]
    public void ParseExitReason_RecordWithoutAReadableTimestamp_IsDiscardedWhenAgeMatters()
    {
        const string dump = """
            ACTIVITY MANAGER PROCESS EXIT INFO (dumpsys activity exit-info)
              package: com.contoso.app
                  ApplicationExitInfo #0:
                    timestamp=not-a-date pid=14277 realUid=10212 user=0
                    process=com.contoso.app reason=4 (APP CRASH(EXCEPTION)) subreason=0 (UNKNOWN) status=0
            """;
        var deviceNow = AndroidAppProcessProbe.ParseDeviceTimestamp("2026-08-17 14:43:10");
        Assert.Null(AndroidAppProcessProbe.ParseExitReason(dump, 14277, deviceNow, TimeSpan.FromMinutes(10)));
    }

    [Theory]
    [InlineData("2026-08-17 14:42:36.633")]
    [InlineData("2026-08-17 16:36:10")]
    [InlineData("2026-08-17T17:13:30")]
    public void ParseDeviceTimestamp_RealDeviceFormats_AreUnderstood(string value)
        => Assert.NotNull(AndroidAppProcessProbe.ParseDeviceTimestamp(value));

    /// <summary>
    /// <c>adb shell</c> re-splits its arguments on the device, so a <c>date</c> format containing a
    /// space arrives as two arguments and the command fails with <c>date: Max 1 argument</c>. That
    /// silently disabled the whole staleness guard on a real emulator, so the format is pinned, as
    /// is the parser's ability to read what it produces.
    /// </summary>
    [Fact]
    public void DeviceClockFormat_ContainsNoSpaceAndItsOutputIsParsed()
    {
        Assert.DoesNotContain(' ', AndroidAppProcessProbe.DeviceClockFormat);
        Assert.Equal("+%Y-%m-%dT%H:%M:%S", AndroidAppProcessProbe.DeviceClockFormat);
        Assert.NotNull(AndroidAppProcessProbe.ParseDeviceTimestamp("2026-08-17T17:13:30"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("17/08/2026 14:42")]
    [InlineData("adb: device not found")]
    public void ParseDeviceTimestamp_UnusableValue_IsRejected(string? value)
        => Assert.Null(AndroidAppProcessProbe.ParseDeviceTimestamp(value));
}
