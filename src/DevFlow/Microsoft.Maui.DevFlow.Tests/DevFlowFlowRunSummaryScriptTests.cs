using System.Diagnostics;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class DevFlowFlowRunSummaryScriptTests : IDisposable
{
    private const string WorkflowPath = ".github/workflows/devflow-integration.yml";
    private static readonly string[] FlowLanes =
    [
        "android-flow-pilot",
        "ios-flow-qa",
        "maccatalyst-flow-qa",
        "macos-appkit-flow-qa",
        "windows-flow-qa",
    ];

    private readonly string _repositoryRoot = FindRepositoryRoot();
    private readonly string _testRoot;

    public DevFlowFlowRunSummaryScriptTests()
    {
        _testRoot = Path.Combine(
            _repositoryRoot,
            "artifacts",
            "TestResults",
            "devflow-flow-run-summary-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public async Task PassingReport_RendersStructuredRow()
    {
        var root = CreateResultsRoot("passing");
        WriteReport(root, "modal-roundtrip-attempt-1", PassingReport());

        var result = await RunAsync(root, "android-flow-pilot");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("summarized", result.Json.GetProperty("status").GetString());
        Assert.Equal(1, result.Json.GetProperty("reports").GetInt32());
        Assert.Equal(1, result.Json.GetProperty("passed").GetInt32());
        Assert.Equal(0, result.Json.GetProperty("verified").GetInt32());
        Assert.True(result.Json.GetProperty("summaryWritten").GetBoolean());
        Assert.Contains("## DevFlow flow run — android-flow-pilot", result.Summary, StringComparison.Ordinal);
        Assert.Contains(
            "| modal-roundtrip.md | android | passed | no | ok | unverified | - | - | 2 | ",
            result.Summary,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A run whose flow passed and whose owned cleanup failed is not a green pass. The renderer
    /// counted `outcome.status` alone, so the one artifact that proves the lane exited non-zero
    /// was summarised as success.
    /// </summary>
    [Fact]
    public async Task PassedFlowWithFailedCleanup_IsNotCountedAsAPass()
    {
        var root = CreateResultsRoot("cleanup-failed");
        WriteReport(root, "cleanup-failed-attempt-1", PassedWithCleanupFailureReport());

        var result = await RunAsync(root, "android-flow-pilot");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.Json.GetProperty("reports").GetInt32());
        Assert.Equal(0, result.Json.GetProperty("passed").GetInt32());
        Assert.Equal(1, result.Json.GetProperty("notPassed").GetInt32());
        Assert.Equal(1, result.Json.GetProperty("cleanupFailed").GetInt32());
        Assert.Contains("| passed | no | failed (1) | infrastructure-failure |", result.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// The count, not the exit category, has to be what makes a cleanup-failed run non-green. With
    /// a `test-failure` primary the exit category is unchanged, so `secondaryFailures` is the only
    /// evidence there is.
    /// </summary>
    [Fact]
    public async Task FailedFlowWithFailedCleanup_IsCountedFromTheSecondaryFailuresAlone()
    {
        var root = CreateResultsRoot("cleanup-failed-test-failure");
        WriteReport(root, "cleanup-failed-attempt-2", FailedWithCleanupFailureReport());

        var result = await RunAsync(root, "android-flow-pilot");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.Json.GetProperty("notPassed").GetInt32());
        Assert.Equal(1, result.Json.GetProperty("cleanupFailed").GetInt32());
        Assert.Contains("| failed | no | failed (1) | test-failure |", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PassedFlowWithCleanCleanup_IsStillCountedAsAPass()
    {
        var root = CreateResultsRoot("cleanup-ok");
        WriteReport(root, "clean-attempt-1", PassingReport());

        var result = await RunAsync(root, "android-flow-pilot");

        Assert.Equal(1, result.Json.GetProperty("passed").GetInt32());
        Assert.Equal(0, result.Json.GetProperty("notPassed").GetInt32());
        Assert.Equal(0, result.Json.GetProperty("cleanupFailed").GetInt32());
    }

    [Fact]
    public async Task NoReports_StatesTheLaneProducedNoEvidence()
    {
        var root = CreateResultsRoot("empty");

        var result = await RunAsync(root, "windows-flow-qa");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, result.Json.GetProperty("reports").GetInt32());
        Assert.Contains("No `flow-run.json` report was produced", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingResultsRoot_IsReportedWithoutFailing()
    {
        var result = await RunAsync(Path.Combine(_testRoot, "does-not-exist"), "ios-flow-qa");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, result.Json.GetProperty("reports").GetInt32());
        Assert.Contains("No `flow-run.json` report was produced", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedReport_IsCountedUnreadableWithoutThrowing()
    {
        var root = CreateResultsRoot("malformed");
        WriteReport(root, "broken-attempt-1", "{ this is not json");

        var result = await RunAsync(root, "android-flow-pilot");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.Json.GetProperty("reports").GetInt32());
        Assert.Equal(1, result.Json.GetProperty("unreadable").GetInt32());
        Assert.Contains("unreadable", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizedReport_IsSkippedWithoutBeingRead()
    {
        var root = CreateResultsRoot("oversized");
        var sentinel = "OVERSIZED-SENTINEL-MUST-NOT-LEAK";
        WriteReport(root, "huge-attempt-1", $"{{\"legacyFlowIdentity\":\"{sentinel}\",\"padding\":\"{new string('a', 4096)}\"}}");

        var result = await RunAsync(root, "android-flow-pilot", maximumReportBytes: 1024);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.Json.GetProperty("unreadable").GetInt32());
        Assert.Contains("oversized", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreeTextOutcomeAndFailureMessages_AreNeverRendered()
    {
        var root = CreateResultsRoot("free-text");
        WriteReport(root, "failed-attempt-1", FailedReport());

        var result = await RunAsync(root, "android-flow-pilot");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.Json.GetProperty("notPassed").GetInt32());
        Assert.Contains("app-build-failed", result.Summary, StringComparison.Ordinal);
        Assert.Contains("resolve-artifact", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("MESSAGE-MUST-NOT-LEAK", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("SUMMARY-MUST-NOT-LEAK", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostileReportFields_AreSanitizedIntoASingleTableRow()
    {
        var root = CreateResultsRoot("hostile");
        WriteReport(root, "hostile-attempt-1", HostileReport());

        var result = await RunAsync(root, "android-flow-pilot");

        Assert.Equal(0, result.ExitCode);
        var row = Assert.Single(
            result.Summary.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static line => line.StartsWith("| ", StringComparison.Ordinal)
                    && !line.StartsWith("| ---", StringComparison.Ordinal)
                    && !line.StartsWith("| Flow", StringComparison.Ordinal)));
        Assert.Equal(12, row.Split('|').Length);
        Assert.DoesNotContain("<img", row, StringComparison.Ordinal);
        Assert.DoesNotContain("](", row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParsableReportWithoutOutcomeStatus_CountsAsNotPassed()
    {
        var root = CreateResultsRoot("no-status");
        WriteReport(root, "truncated-attempt-1", """{ "schema": 1, "runId": "run-4" }""");

        var result = await RunAsync(root, "android-flow-pilot");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.Json.GetProperty("reports").GetInt32());
        Assert.Equal(0, result.Json.GetProperty("passed").GetInt32());
        Assert.Equal(1, result.Json.GetProperty("notPassed").GetInt32());
    }

    [Fact]
    public async Task ScalarJsonReport_CountsAsUnreadable()
    {
        var root = CreateResultsRoot("scalar");
        WriteReport(root, "scalar-attempt-1", "\"just a string\"");

        var result = await RunAsync(root, "android-flow-pilot");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.Json.GetProperty("unreadable").GetInt32());
        Assert.Equal(0, result.Json.GetProperty("passed").GetInt32());
        Assert.Contains("unreadable", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFlowLaneRendersASummaryAndUploadsEvidence()
    {
        var workflow = File.ReadAllText(Path.Combine(_repositoryRoot, WorkflowPath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Equal(
            FlowLanes.Length,
            CountOccurrences(workflow, "eng/devflow/Write-DevFlowFlowRunSummary.ps1")
                + CountOccurrences(workflow, "eng\\devflow\\Write-DevFlowFlowRunSummary.ps1"));

        foreach (var lane in FlowLanes)
            Assert.Contains($"-Lane {lane}", workflow, StringComparison.Ordinal);

        Assert.Equal(FlowLanes.Length, CountOccurrences(workflow, "name: devflow-flow-evidence-"));
        Assert.Contains("**/flow-run.json", workflow, StringComparison.Ordinal);
        Assert.Contains("**/*.mauitrace", workflow, StringComparison.Ordinal);
        Assert.Contains("**\\flow-run.json", workflow, StringComparison.Ordinal);
        Assert.Contains("**\\*.mauitrace", workflow, StringComparison.Ordinal);

        // A lane that ran and produced no evidence has to fail loudly, but a lane that never got
        // as far as running must not report an artifact error instead of its real failure.
        Assert.Equal(
            FlowLanes.Length,
            CountOccurrences(workflow, "if: ${{ always() && steps.flow-run.conclusion != 'skipped' }}"));
    }

    [Fact]
    public void WindowsFlowQaLane_IsNoLongerAdvisory()
    {
        var workflow = File.ReadAllText(Path.Combine(_repositoryRoot, WorkflowPath.Replace('/', Path.DirectorySeparatorChar)));
        var start = workflow.IndexOf("\n  windows-flow-qa:", StringComparison.Ordinal);
        Assert.True(start >= 0, "The windows-flow-qa lane is missing.");

        var end = workflow.IndexOf("\n  windows:", start, StringComparison.Ordinal);
        var lane = end >= 0 ? workflow[start..end] : workflow[start..];
        var laneHeader = lane[..lane.IndexOf("    steps:", StringComparison.Ordinal)];

        Assert.DoesNotContain("continue-on-error", laneHeader, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
                Directory.Delete(_testRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private string CreateResultsRoot(string name)
    {
        var root = Path.Combine(_testRoot, name);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteReport(string root, string runDirectory, string json)
    {
        var directory = Path.Combine(root, runDirectory);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "flow-run.json"), json);
    }

    private static string PassingReport()
        => """
        {
          "schema": 1,
          "runId": "run-1",
          "flowId": "sha256:3b4edbacc43a360b301f8e55eaa621d5997bd5edeedb0007d81e6264c51cd13a",
          "legacyFlowIdentity": "modal-roundtrip.md",
          "startedAt": "2026-08-15T20:25:23.0000000+02:00",
          "endedAt": "2026-08-15T20:25:26.5000000+02:00",
          "exitCategory": "unverified",
          "target": { "platform": "android" },
          "steps": [ { "stepId": "1" }, { "stepId": "2" } ],
          "outcome": { "status": "passed", "verified": false, "summary": "Flow replay passed." }
        }
        """;

    private static string PassedWithCleanupFailureReport()
        => """
        {
          "schema": 1,
          "runId": "run-cleanup",
          "legacyFlowIdentity": "modal-roundtrip.md",
          "startedAt": "2026-08-15T20:25:23.0000000+02:00",
          "endedAt": "2026-08-15T20:25:26.5000000+02:00",
          "exitCategory": "infrastructure-failure",
          "target": { "platform": "android" },
          "steps": [ { "stepId": "1" } ],
          "outcome": { "status": "passed", "verified": false, "summary": "Flow replay passed." },
          "secondaryFailures": [
            { "phase": "cleanup", "code": "cleanup-exception", "class": "infrastructure", "retryable": true }
          ]
        }
        """;

    private static string FailedWithCleanupFailureReport()
        => """
        {
          "schema": 1,
          "runId": "run-cleanup-2",
          "legacyFlowIdentity": "modal-roundtrip.md",
          "startedAt": "2026-08-15T20:25:23.0000000+02:00",
          "endedAt": "2026-08-15T20:25:26.5000000+02:00",
          "exitCategory": "test-failure",
          "target": { "platform": "android" },
          "steps": [ { "stepId": "1" } ],
          "outcome": { "status": "failed", "verified": false },
          "failure": { "class": "assertion-failed", "code": "assertion-failed", "phase": "assertion" },
          "secondaryFailures": [
            { "phase": "cleanup", "code": "cleanup-exception", "class": "infrastructure", "retryable": true }
          ]
        }
        """;

    private static string FailedReport()
        => """
        {
          "schema": 1,
          "runId": "run-2",
          "legacyFlowIdentity": "native-baseline.md",
          "startedAt": "2026-08-15T20:25:23.0000000+02:00",
          "endedAt": "2026-08-15T20:25:50.0000000+02:00",
          "exitCategory": "infrastructure-failure",
          "target": { "platform": "android" },
          "steps": [],
          "outcome": { "status": "infrastructure-error", "verified": false, "summary": "SUMMARY-MUST-NOT-LEAK" },
          "failure": {
            "class": "infrastructure",
            "code": "app-build-failed",
            "phase": "resolve-artifact",
            "message": "MESSAGE-MUST-NOT-LEAK"
          }
        }
        """;

    private static string HostileReport()
        => """
        {
          "schema": 1,
          "runId": "run-3",
          "legacyFlowIdentity": "evil | row\nbreak <img src=x onerror=alert(1)> [link](https://example.invalid)",
          "startedAt": "2026-08-15T20:25:23.0000000+02:00",
          "endedAt": "2026-08-15T20:25:24.0000000+02:00",
          "exitCategory": "failed`|`",
          "target": { "platform": "android | windows" },
          "steps": [],
          "outcome": { "status": "failed", "verified": false }
        }
        """;

    private async Task<(int ExitCode, string Summary, JsonElement Json)> RunAsync(
        string resultsRoot,
        string lane,
        int? maximumReportBytes = null)
    {
        var summaryPath = Path.Combine(_testRoot, $"summary-{Guid.NewGuid():N}.md");
        var script = Path.Combine(_repositoryRoot, "eng", "devflow", "Write-DevFlowFlowRunSummary.ps1");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh")
            {
                WorkingDirectory = _repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        AddArgument("-NoLogo");
        AddArgument("-NoProfile");
        AddArgument("-File");
        AddArgument(script);
        AddArgument("-ResultsRoot");
        AddArgument(resultsRoot);
        AddArgument("-Lane");
        AddArgument(lane);
        AddArgument("-SummaryPath");
        AddArgument(summaryPath);
        if (maximumReportBytes is not null)
        {
            AddArgument("-MaximumReportBytes");
            AddArgument(maximumReportBytes.Value.ToString());
        }

        process.Start();
        // Both pipes are drained at once. Reading one to the end first deadlocks as soon as the
        // other fills its buffer, which is exactly what a chatty failure does.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var jsonLine = stdout.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(static line => line.StartsWith("{", StringComparison.Ordinal));
        Assert.True(jsonLine is not null, $"No JSON result. stdout={stdout}; stderr={stderr}");

        using var document = JsonDocument.Parse(jsonLine);
        var summary = File.Exists(summaryPath) ? File.ReadAllText(summaryPath) : string.Empty;
        return (process.ExitCode, summary, document.RootElement.Clone());

        void AddArgument(string value) => process.StartInfo.ArgumentList.Add(value);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the maui-labs repository root.");
    }
}
