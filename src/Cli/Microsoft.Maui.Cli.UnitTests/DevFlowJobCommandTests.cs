using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;
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
        await using var server = new MockAgentServer
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
        await using var server = new MockAgentServer { JobsListResponse = () => Jobs("ENQUEUED") };
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
        var (target, id) = DevFlowCommands.ResolveAndroidJob(payload.RootElement, _driver.Scheduled, "10");
        Assert.Equal("10", target.JobId);
        Assert.Equal(RequestedId, id);
    }

    [Fact]
    public void Resolve_UnknownUuid_DoesNotFallBackToAnotherWorker()
    {
        using var payload = JsonDocument.Parse(Jobs("ENQUEUED"));
        var error = Assert.Throws<InvalidOperationException>(() =>
            DevFlowCommands.ResolveAndroidJob(payload.RootElement, _driver.Scheduled, OldId));
        Assert.Contains("No scheduled", error.Message);
    }

    [Fact]
    public void Resolve_OlderAgentWithoutMappings_RejectsRatherThanGuessing()
    {
        using var payload = JsonDocument.Parse("""{"supported":true,"jobs":[]}""");
        var error = Assert.Throws<InvalidOperationException>(() =>
            DevFlowCommands.ResolveAndroidJob(payload.RootElement, _driver.Scheduled, RequestedId));
        Assert.Contains("Update the agent", error.Message);
    }

    [Fact]
    public async Task ReadState_MissingRequestedUuid_DoesNotReturnAnotherRequestsSuccess()
    {
        await using var server = new MockAgentServer { JobsListResponse = () => Jobs("ENQUEUED") };
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DevFlowCommands.ReadWorkStateAsync(client, "44444444-4444-4444-4444-444444444444"));
        Assert.Contains("disappeared", error.Message);
    }

    [Fact]
    public async Task Run_QueryFailure_DoesNotTreatAnOldSuccessAsTheOutcome()
    {
        await using var server = new MockAgentServer
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
        await using var server = new MockAgentServer { JobsListResponse = () => Jobs("SUCCEEDED") };
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
        await using var server = new MockAgentServer { JobsListResponse = () => Jobs("ENQUEUED") };
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
          "jobs":[
            {"identifier":"{{OldId}}","tags":["com.example.SampleSyncWorker","shared-tag"],"state":"SUCCEEDED","runAttemptCount":9},
            {"identifier":"{{OtherId}}","tags":["com.example.SampleSyncWorker","shared-tag"],"state":"ENQUEUED","runAttemptCount":0},
            {"identifier":"{{RequestedId}}","tags":["com.example.SampleSyncWorker","shared-tag"],"state":"{{requestedState}}","runAttemptCount":{{(requestedState == "ENQUEUED" ? 0 : 2)}}}
          ],
          "scheduledJobs":[
            {"workSpecId":"{{OtherId}}","jobId":"9","namespace":"androidx.work.systemjobscheduler"},
            {"workSpecId":"{{RequestedId}}","jobId":"10","namespace":"androidx.work.systemjobscheduler"}
          ]
        }
        """;

    private sealed class TestAndroidDriver : AndroidAppDriver
    {
        public bool Triggered { get; private set; }
        public List<(string Package, string JobId, string? Namespace, string? Serial)> Runs { get; } = [];
        public IReadOnlyList<AndroidScheduledJob> Scheduled { get; } =
        [
            new() { JobId = "9", Namespace = WorkManagerNamespace, Worker = "SampleSyncWorker", IsWorkManager = true },
            new() { JobId = "10", Namespace = WorkManagerNamespace, Worker = "SampleSyncWorker", IsWorkManager = true }
        ];
        public override Task<IReadOnlyList<AndroidScheduledJob>> ListScheduledJobsAsync(string packageName) => Task.FromResult(Scheduled);
        public override Task<string> RunScheduledJobAsync(string packageName, string jobId, string? jobNamespace = WorkManagerNamespace, bool force = true)
        {
            Runs.Add((packageName, jobId, jobNamespace, Serial));
            Triggered = true;
            return Task.FromResult("Running job [FORCED]");
        }
    }
}
