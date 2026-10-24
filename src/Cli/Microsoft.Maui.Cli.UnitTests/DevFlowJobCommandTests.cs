using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Jobs;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class DevFlowJobCommandTests : IDisposable
{
    private const string OldId = "11111111-1111-1111-1111-111111111111";
    private const string RequestedId = "22222222-2222-2222-2222-222222222222";
    private const string OtherId = "33333333-3333-3333-3333-333333333333";
    private readonly Func<AndroidAppDriver> _originalFactory = DevFlowCommands.JobDriverFactory;
    private readonly TestAndroidDriver _driver = new();

    public DevFlowJobCommandTests() => DevFlowCommands.JobDriverFactory = () => _driver;
    public void Dispose() => DevFlowCommands.JobDriverFactory = _originalFactory;

    [Theory]
    [InlineData("SUCCEEDED", 0)]
    [InlineData("FAILED", 1)]
    [InlineData("CANCELLED", 1)]
    public async Task Run_ExplicitUuid_TriggersAndReportsOnlyThatRequest(string outcome, int exitCode)
    {
        await using var server = new MockAgentServer(supportMutationLease: true)
        {
            JobsListResponse = () => Jobs(_driver.Triggered ? outcome : "ENQUEUED")
        };
        await server.StartAsync();

        var result = await new CliTestHarness(server.Port).InvokeAsync(
            "devflow", "ui", "jobs", "run", RequestedId, "--serial", "test-device", "--json");

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(("com.example.testapp", "10", AndroidAppDriver.WorkManagerNamespace, "test-device"),
            Assert.Single(_driver.Runs));
        var json = result.ParseJsonOutput();
        Assert.Equal(RequestedId, json.GetProperty("identifier").GetString());
        Assert.Equal("ENQUEUED", json.GetProperty("stateBefore").GetString());
        Assert.Equal(outcome, json.GetProperty("state").GetString());
        Assert.Equal(2, json.GetProperty("runAttemptCount").GetInt32());
    }

    [Theory]
    [InlineData("SampleSyncWorker")]
    [InlineData("com.example.SampleSyncWorker")]
    [InlineData("shared-tag")]
    public async Task Run_AmbiguousWorkerOrTag_RejectsWithoutTriggering(string identifier)
    {
        await using var server = new MockAgentServer(supportMutationLease: true) { JobsListResponse = () => Jobs("ENQUEUED") };
        await server.StartAsync();
        var result = await new CliTestHarness(server.Port).InvokeAsync(
            "devflow", "ui", "jobs", "run", identifier, "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ambiguous", result.StdErr);
        Assert.Empty(_driver.Runs);
    }

    [Fact]
    public void Resolve_NumericId_ResolvesToTheExactUuid()
    {
        using var payload = JsonDocument.Parse(Jobs("ENQUEUED"));
        var target = AndroidJobRunner.ResolveJob(payload.RootElement, _driver.Scheduled, "10");
        Assert.Equal("10", target.Job.JobId);
        Assert.Equal(RequestedId, target.WorkSpecId);
    }

    [Fact]
    public void Resolve_UnknownUuid_DoesNotFallBackToAnotherWorker()
    {
        using var payload = JsonDocument.Parse(Jobs("ENQUEUED"));
        var error = Assert.Throws<InvalidOperationException>(() =>
            AndroidJobRunner.ResolveJob(payload.RootElement, _driver.Scheduled, OldId));
        Assert.Contains("No scheduled", error.Message);
    }

    [Fact]
    public void Resolve_OlderAgentWithoutMappings_RejectsRatherThanGuessing()
    {
        using var payload = JsonDocument.Parse("""{"supported":true,"jobs":[]}""");
        var error = Assert.Throws<InvalidOperationException>(() =>
            AndroidJobRunner.ResolveJob(payload.RootElement, _driver.Scheduled, RequestedId));
        Assert.Contains("Update the agent", error.Message);
    }

    [Fact]
    public void ReadState_MissingRequestedUuid_DoesNotReturnAnotherRequestsSuccess()
    {
        using var jobs = JsonDocument.Parse(Jobs("ENQUEUED"));
        var error = Assert.Throws<InvalidOperationException>(() =>
            AndroidJobRunner.ReadState(jobs.RootElement, "44444444-4444-4444-4444-444444444444", 0));
        Assert.Contains("disappeared", error.Message);
    }

    [Fact]
    public async Task Run_QueryFailure_DoesNotTreatAnOldSuccessAsTheOutcome()
    {
        await using var server = new MockAgentServer(supportMutationLease: true)
        {
            JobsListResponse = () => _driver.Triggered
                ? """{"platform":"Android","supported":true,"jobs":[],"error":"database unavailable"}"""
                : Jobs("ENQUEUED")
        };
        await server.StartAsync();
        var result = await new CliTestHarness(server.Port).InvokeAsync(
            "devflow", "ui", "jobs", "run", RequestedId, "--json");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("database unavailable", result.StdErr);
        Assert.Single(_driver.Runs);
    }

    [Fact]
    public async Task Run_TerminalRequestedWork_RejectsWithoutTriggering()
    {
        await using var server = new MockAgentServer(supportMutationLease: true) { JobsListResponse = () => Jobs("SUCCEEDED") };
        await server.StartAsync();
        var result = await new CliTestHarness(server.Port).InvokeAsync(
            "devflow", "ui", "jobs", "run", RequestedId, "--json");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("only enqueued work", result.StdErr);
        Assert.Empty(_driver.Runs);
    }

    [Fact]
    public async Task Run_WorkRemainsPending_ReportsTimeoutRatherThanAnotherRequestsSuccess()
    {
        await using var server = new MockAgentServer(supportMutationLease: true) { JobsListResponse = () => Jobs("ENQUEUED") };
        await server.StartAsync();
        var result = await new CliTestHarness(server.Port).InvokeAsync(
            "devflow", "ui", "jobs", "run", RequestedId, "--timeout", "1", "--json");
        Assert.Equal(1, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.True(json.GetProperty("timedOut").GetBoolean());
        Assert.Equal("ENQUEUED", json.GetProperty("state").GetString());
        Assert.Single(_driver.Runs);
    }

    [Fact]
    public async Task Run_LeaseHeldByAnotherHost_DoesNotForceWork()
    {
        await using var server = new MockAgentServer(supportMutationLease: true, mutationLeaseHeldByOther: true)
        {
            JobsListResponse = () => Jobs("ENQUEUED")
        };
        await server.StartAsync();
        var result = await new CliTestHarness(server.Port).InvokeAsync(
            "devflow", "ui", "jobs", "run", RequestedId, "--json");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Another DevFlow session", result.StdErr);
        Assert.Empty(_driver.Runs);
        Assert.Equal(new[] { "claim" }, LeaseActions(server));
    }

    [Fact]
    public async Task Run_DifferentAgentOnSelectedDevice_RefusesBeforeClaimingOrTriggering()
    {
        _driver.DeviceJobs = Jobs("ENQUEUED").Replace(
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        await using var server = new MockAgentServer(supportMutationLease: true) { JobsListResponse = () => Jobs("ENQUEUED") };
        await server.StartAsync();
        var result = await new CliTestHarness(server.Port).InvokeAsync(
            "devflow", "ui", "jobs", "run", RequestedId, "--serial", "different-device", "--json");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("does not host the connected agent", result.StdErr);
        Assert.Empty(_driver.Runs);
        Assert.Empty(LeaseActions(server));
    }

    [Fact]
    public async Task Runner_LongAdbCall_RenewsTransactionAndReleasesItAfterCompletion()
    {
        _driver.RunDelay = token => Task.Delay(1500, token);
        await using var server = new MockAgentServer(supportMutationLease: true)
        {
            JobsListResponse = () => Jobs(_driver.Triggered ? "SUCCEEDED" : "ENQUEUED")
        };
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var result = await new AndroidJobRunner(client, _driver).RunAsync(
            RequestedId, null, TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.True(result.Success);
        var actions = LeaseActions(server);
        Assert.Equal("claim", actions[0]);
        Assert.True(actions.Count(a => a == "begin") >= 2);
        Assert.Equal(new[] { "end", "release" }, actions.TakeLast(2));
    }

    [Fact]
    public async Task Runner_LeaseRenewalFails_CancelsAdbAndFailsExplicitly()
    {
        _driver.RunDelay = token => Task.Delay(Timeout.Infinite, token);
        await using var server = new MockAgentServer(supportMutationLease: true)
        {
            JobsListResponse = () => Jobs("ENQUEUED"),
            RejectMutationLeaseAction = action => action == "begin" && _driver.Triggered
        };
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AndroidJobRunner(client, _driver).RunAsync(
                RequestedId, null, TimeSpan.FromSeconds(10), CancellationToken.None));
        Assert.Contains("lease was lost", error.Message);
        Assert.Equal(new[] { "end", "release" }, LeaseActions(server).TakeLast(2));
    }

    [Fact]
    public async Task Runner_CancelledDuringAdb_ReleasesTheTransaction()
    {
        using var cancel = new CancellationTokenSource();
        _driver.OnRun = cancel.Cancel;
        _driver.RunDelay = token => Task.Delay(Timeout.Infinite, token);
        await using var server = new MockAgentServer(supportMutationLease: true) { JobsListResponse = () => Jobs("ENQUEUED") };
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AndroidJobRunner(client, _driver).RunAsync(
                RequestedId, null, TimeSpan.FromSeconds(10), cancel.Token));
        Assert.Equal(new[] { "claim", "begin", "end", "release" }, LeaseActions(server));
    }

    [Fact]
    public void Resolve_Api23DuplicateBackingJobs_AcceptsTheExactWorkSpec()
    {
        var payload = JsonNode.Parse(Jobs("ENQUEUED"))!;
        var mapping = payload["scheduledJobs"]![1]!.DeepClone();
        mapping["jobId"] = "11";
        payload["scheduledJobs"]!.AsArray().Add(mapping);
        var scheduled = _driver.Scheduled.Concat(new[]
        {
            new AndroidScheduledJob { JobId = "11", Namespace = AndroidAppDriver.WorkManagerNamespace, IsWorkManager = true }
        }).ToArray();
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var target = AndroidJobRunner.ResolveJob(document.RootElement, scheduled, RequestedId);
        Assert.Equal(RequestedId, target.WorkSpecId);
        Assert.Equal("10", target.Job.JobId);
    }

    [Fact]
    public void Resolve_StaleSchedulerGeneration_DoesNotTriggerIt()
    {
        var payload = JsonNode.Parse(Jobs("ENQUEUED"))!;
        payload["jobs"]![2]!["generation"] = 1;
        using var document = JsonDocument.Parse(payload.ToJsonString());
        Assert.Throws<InvalidOperationException>(() =>
            AndroidJobRunner.ResolveJob(document.RootElement, _driver.Scheduled, RequestedId));
        Assert.Throws<InvalidOperationException>(() =>
            AndroidJobRunner.ReadState(document.RootElement, RequestedId, 0));
    }

    private static string[] LeaseActions(MockAgentServer server) => server.RecordedRequests
        .Where(r => r.Path == "/api/v1/agent/lease")
        .Select(r => JsonNode.Parse(r.Body!)!["action"]!.GetValue<string>()).ToArray();

    [Fact]
    public async Task Run_IosLaunchAcknowledgement_IsNotPresentedAsTaskCompletion()
    {
        await using var server = new MockAgentServer
        {
            JobsListResponse = () => """{"platform":"iOS","supported":true,"runSupported":true,"jobs":[{"identifier":"com.example.sync","type":"processing"}]}""",
            JobRunResponse = () => """
                {"success":true,"identifier":"com.example.sync","launchRequested":true,
                 "executionVerified":false,"completionVerified":false,
                 "message":"Launch requested for BGTask 'com.example.sync'.",
                 "note":"Verify the handler's recorded outcome."}
                """
        };
        await server.StartAsync();
        var result = await new CliTestHarness(server.Port).InvokeAsync(
            "devflow", "ui", "jobs", "run", "com.example.sync", "--no-json");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("LAUNCH REQUESTED:", result.StdOut);
        Assert.DoesNotContain("SUCCESS:", result.StdOut);
        Assert.Contains("handler's recorded outcome", result.StdOut);
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/device/jobs");
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/device/jobs/com.example.sync/run");
        Assert.Empty(_driver.Runs);
    }

    private static string Jobs(string requestedState) => $$"""
        {
          "platform":"Android","supported":true,"runSupported":false,
          "agentInstanceId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","agentPort":9223,
          "jobs":[
            {"identifier":"{{OldId}}","generation":0,"tags":["com.example.SampleSyncWorker","shared-tag"],"state":"SUCCEEDED","runAttemptCount":9},
            {"identifier":"{{OtherId}}","generation":0,"tags":["com.example.SampleSyncWorker","shared-tag"],"state":"ENQUEUED","runAttemptCount":0},
            {"identifier":"{{RequestedId}}","generation":0,"tags":["com.example.SampleSyncWorker","shared-tag"],"state":"{{requestedState}}","runAttemptCount":{{(requestedState == "ENQUEUED" ? 0 : 2)}}}
          ],
          "scheduledJobs":[
            {"workSpecId":"{{OtherId}}","generation":0,"jobId":"9","namespace":"androidx.work.systemjobscheduler"},
            {"workSpecId":"{{RequestedId}}","generation":0,"jobId":"10","namespace":"androidx.work.systemjobscheduler"}
          ]
        }
        """;

    private sealed class TestAndroidDriver : AndroidAppDriver
    {
        public bool Triggered { get; private set; }
        public string DeviceJobs { get; set; } = Jobs("ENQUEUED");
        public Action? OnRun { get; set; }
        public Func<CancellationToken, Task>? RunDelay { get; set; }
        public List<(string Package, string JobId, string? Namespace, string? Serial)> Runs { get; } = [];
        public IReadOnlyList<AndroidScheduledJob> Scheduled { get; } =
        [
            new() { JobId = "9", Namespace = WorkManagerNamespace, Worker = "SampleSyncWorker", IsWorkManager = true },
            new() { JobId = "10", Namespace = WorkManagerNamespace, Worker = "SampleSyncWorker", IsWorkManager = true }
        ];
        public override Task<System.Text.Json.JsonElement> ReadJobsOnDeviceAsync(int port, CancellationToken cancellationToken = default)
        {
            Assert.Equal(9223, port);
            using var document = JsonDocument.Parse(DeviceJobs);
            return Task.FromResult(document.RootElement.Clone());
        }
        public override Task<IReadOnlyList<AndroidScheduledJob>> ListScheduledJobsAsync(string packageName, CancellationToken cancellationToken = default) => Task.FromResult(Scheduled);
        public override async Task<string> RunScheduledJobAsync(string packageName, string jobId, string? jobNamespace = WorkManagerNamespace, bool force = true, CancellationToken cancellationToken = default)
        {
            Runs.Add((packageName, jobId, jobNamespace, Serial));
            Triggered = true;
            OnRun?.Invoke();
            if (RunDelay is not null) await RunDelay(cancellationToken);
            return "Running job [FORCED]";
        }
    }
}
