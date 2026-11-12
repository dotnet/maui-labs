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

        // This is a display label only; it cannot uniquely identify a WorkSpec.
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

    [Fact]
    public void ParseScheduledJobs_DoesNotMatchAPackageNameSuffix()
    {
        const string dump = "JOB #u0a233/5: abcd other.com.companyname.mauitodo/com.example.Service";
        Assert.Empty(AndroidAppDriver.ParseScheduledJobs(dump, Package));
    }

    [Fact]
    public void ParseScheduledJobs_SameWorkerWithDifferentIds_KeepsBothRequests()
    {
        var dump = Dumpsys + "\n" +
            $"JOB androidx.work.systemjobscheduler:u0a233/2: abcd #SampleSyncWorker#@androidx.work.systemjobscheduler@{Package}/androidx.work.impl.background.systemjob.SystemJobService";
        var jobs = AndroidAppDriver.ParseScheduledJobs(dump, Package);
        Assert.Equal(new[] { "1", "2" }, jobs.Select(j => j.JobId));
        Assert.All(jobs, j => Assert.Equal("SampleSyncWorker", j.Worker));
    }

    [Fact]
    public void RunArguments_KeepAdbCommandsAndFlagsAsSeparateArguments()
    {
        var arguments = AndroidAppDriver.BuildJobSchedulerArguments(
            "run", Package, "10", AndroidAppDriver.WorkManagerNamespace, force: true);
        Assert.Equal(new[]
        {
            "shell", "cmd", "jobscheduler", "run", "-f", "-n",
            AndroidAppDriver.WorkManagerNamespace, Package, "10"
        }, arguments);
        var startInfo = AndroidAppDriver.CreateAdbProcessStartInfo("adb",
            AndroidAppDriver.BuildAdbArguments("emulator-5588", arguments));
        Assert.Equal(new[] { "-s", "emulator-5588" }.Concat(arguments), startInfo.ArgumentList);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("get-job-state")]
    [InlineData("stop")]
    public void SchedulerArguments_UnnamespacedJob_OmitsNamespaceAndForce(string action)
    {
        Assert.Equal(new[] { "shell", "cmd", "jobscheduler", action, Package, "10" },
            AndroidAppDriver.BuildJobSchedulerArguments(action, Package, "10", null));
    }
}
