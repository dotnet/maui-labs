using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Parsing of <c>adb shell dumpsys jobscheduler</c>. Real captured output — the format is
/// load-bearing: the numeric job id and the namespace are both required by
/// <c>cmd jobscheduler run</c>, and without the namespace the job is simply not found.
/// </summary>
public class AndroidJobParsingTests
{
    private const string Package = "com.companyname.mauitodo";

    // Captured verbatim from an Android API 37 emulator.
    private const string Dumpsys = """
          JOB androidx.work.systemjobscheduler:u0a233/1: 258ed68 #SampleSyncWorker#@androidx.work.systemjobscheduler@com.companyname.mauitodo/androidx.work.impl.background.systemjob.SystemJobService
            u0a233 tag=*job*r/#SampleSyncWorker#@androidx.work.systemjobscheduler@com.companyname.mauitodo/androidx.work.impl.background.systemjob.SystemJobService
              Service: com.companyname.mauitodo/androidx.work.impl.background.systemjob.SystemJobService
            JobStatus{258ed68 androidx.work.systemjobscheduler:u0a233/1 #SampleSyncWorker#@androidx.work.systemjobscheduler@com.companyname.mauitodo/androidx.work.impl.background.systemjob.SystemJobService u=0 s=10233 TIME=+23h59m9s913ms:none satisfied:0x3600000 unsatisfied:0x80000000}: 108722023
          JOB #1000/808: cb8ff9f android/com.android.server.MountServiceIdler
          JOB androidx.work.systemjobscheduler:u0a148/160: ef63ebc #LanguagePackAutoUpdateWorker#@androidx.work.systemjobscheduler@com.google.android.as/androidx.work.impl.background.systemjob.SystemJobService
        """;

    [Fact]
    public void ParseScheduledJobs_FindsOnlyTheRequestedPackage()
    {
        var jobs = AndroidAppDriver.ParseScheduledJobs(Dumpsys, Package);

        // The Google app's WorkManager job and the system MountServiceIdler must not leak in.
        Assert.Single(jobs);
        Assert.Equal("1", jobs[0].JobId);
    }

    [Fact]
    public void ParseScheduledJobs_ExtractsNamespaceRequiredByRunCommand()
    {
        var job = AndroidAppDriver.ParseScheduledJobs(Dumpsys, Package).Single();

        Assert.Equal(AndroidAppDriver.WorkManagerNamespace, job.Namespace);
        Assert.True(job.IsWorkManager);
    }

    [Fact]
    public void ParseScheduledJobs_ExtractsWorkerNameFromDebugTag()
    {
        var job = AndroidAppDriver.ParseScheduledJobs(Dumpsys, Package).Single();

        // WorkSpec UUIDs are invisible to JobScheduler, so the worker name in the #…# debug
        // tag is what lets a caller map a WorkManager job onto a numeric job id.
        Assert.Equal("SampleSyncWorker", job.Worker);
        Assert.Contains("SystemJobService", job.Service);
    }

    [Fact]
    public void ParseScheduledJobs_DeduplicatesRepeatedEntries()
    {
        // dumpsys mentions the same job again under "Pending queue" / "Active jobs" sections.
        var doubled = Dumpsys + "\n" + Dumpsys;

        Assert.Single(AndroidAppDriver.ParseScheduledJobs(doubled, Package));
    }

    [Fact]
    public void ParseScheduledJobs_ReturnsEmptyWhenPackageHasNoJobs()
    {
        Assert.Empty(AndroidAppDriver.ParseScheduledJobs(Dumpsys, "com.example.absent"));
    }

    [Fact]
    public void ParseScheduledJobs_HandlesUnnamespacedJobs()
    {
        const string plain =
            "  JOB #1000/808: cb8ff9f com.companyname.mauitodo/com.example.PlainJobService";

        var job = AndroidAppDriver.ParseScheduledJobs(plain, Package).Single();

        Assert.Equal("808", job.JobId);
        Assert.Null(job.Namespace);
        Assert.False(job.IsWorkManager);
    }
}
