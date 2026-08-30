using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Covers the run verdict in <c>eng/devflow/Measure-ReplayStability.ps1</c>. The measurement is
/// quoted as a stability number, so the verdict must come from the run's primary outcome: a pass
/// followed by a failed cleanup is a pass with a cleanup problem, not a replay failure.
/// The function is loaded from the checked-in script source, so the tests exercise the shipped
/// logic without executing a device run.
/// </summary>
public sealed class DevFlowReplayStabilityScriptTests : IDisposable
{
    private const string Probe = """
        param([string] $ScriptPath, [string] $ReportPath, [string] $CliPath)

        $tokens = $null
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref] $tokens, [ref] $errors)
        foreach ($definition in $ast.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                        $node.Name -eq 'Get-RunVerdict'
                }, $true)) {
            . ([scriptblock]::Create($definition.Extent.Text))
        }

        $report = $null
        if (-not [string]::IsNullOrWhiteSpace($ReportPath) -and (Test-Path -LiteralPath $ReportPath)) {
            $report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
        }
        $cli = $null
        if (-not [string]::IsNullOrWhiteSpace($CliPath) -and (Test-Path -LiteralPath $CliPath)) {
            $cli = Get-Content -LiteralPath $CliPath -Raw | ConvertFrom-Json
        }

        Get-RunVerdict -Report $report -CliJson $cli | ConvertTo-Json -Depth 6 -Compress
        """;

    private readonly string _repositoryRoot = FindRepositoryRoot();
    private readonly string _testRoot;

    public DevFlowReplayStabilityScriptTests()
    {
        _testRoot = Path.Combine(
            _repositoryRoot,
            "artifacts",
            "TestResults",
            "devflow-replay-stability-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public async Task PrimaryOutcome_DecidesThePassAndCleanupIsReportedSeparately()
    {
        var report = WriteJson(
            "passed-with-cleanup.json",
            """
            {
              "schema": 1,
              "outcome": { "status": "passed", "verified": true },
              "secondaryFailures": [
                { "phase": "cleanup", "code": "artifact-cleanup-failed" }
              ]
            }
            """);

        var verdict = await RunProbeAsync(report, cliPath: null);

        Assert.Equal("passed", verdict.GetProperty("status").GetString());
        Assert.True(verdict.GetProperty("passed").GetBoolean());
        Assert.True(verdict.GetProperty("verified").GetBoolean());
        Assert.Equal("outcome.status", verdict.GetProperty("source").GetString());
        Assert.Equal(1, verdict.GetProperty("secondaryFailures").GetInt32());
        Assert.True(verdict.GetProperty("cleanupFailed").GetBoolean());
    }

    [Fact]
    public async Task FailedPrimaryOutcome_IsNotRescuedByAPassingCliEnvelope()
    {
        var report = WriteJson(
            "failed.json",
            """
            {
              "schema": 1,
              "outcome": { "status": "failed", "verified": false },
              "failure": { "class": "assertion-failed" }
            }
            """);
        var cli = WriteJson("cli-pass.json", """{ "ok": true, "exitCategory": "pass" }""");

        var verdict = await RunProbeAsync(report, cli);

        Assert.Equal("failed", verdict.GetProperty("status").GetString());
        Assert.False(verdict.GetProperty("passed").GetBoolean());
        Assert.Equal("outcome.status", verdict.GetProperty("source").GetString());
    }

    [Fact]
    public async Task PassingUiReplayWithoutItsOracle_IsReportedAsUnverified()
    {
        var report = WriteJson(
            "passed-unverified.json",
            """{ "schema": 1, "outcome": { "status": "passed", "verified": false } }""");

        var verdict = await RunProbeAsync(report, cliPath: null);

        Assert.True(verdict.GetProperty("passed").GetBoolean());
        Assert.False(verdict.GetProperty("verified").GetBoolean());
    }

    [Fact]
    public async Task ArtifactWithoutAStructuredOutcome_StillReadsTheLegacyEnvelope()
    {
        var report = WriteJson("legacy.json", """{ "schema": 1, "verified": true }""");
        var cli = WriteJson("cli-legacy.json", """{ "ok": true, "exitCategory": "pass" }""");

        var verdict = await RunProbeAsync(report, cli);

        Assert.Equal("pass", verdict.GetProperty("status").GetString());
        Assert.True(verdict.GetProperty("passed").GetBoolean());
        Assert.Equal("legacy:exitCategory", verdict.GetProperty("source").GetString());
    }

    [Fact]
    public async Task NoEvidenceAtAll_IsNeverCountedAsAPass()
    {
        var verdict = await RunProbeAsync(reportPath: null, cliPath: null);

        Assert.False(verdict.GetProperty("passed").GetBoolean());
        Assert.Equal("none", verdict.GetProperty("source").GetString());
    }

    [Fact]
    public void ReplayStability_DeclaresItsMinimumPowerShellVersionAndPwshInvocation()
    {
        var script = File.ReadAllText(ScriptPath);
        Assert.StartsWith("#Requires -Version 7.3", script, StringComparison.Ordinal);

        var readme = File.ReadAllText(Path.Combine(
            _repositoryRoot, "docs", "DevFlow", "evidence", "replay-stability", "README.md"));
        Assert.Contains("pwsh .\\eng\\devflow\\Measure-ReplayStability.ps1", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// The measured number is quoted as a stability result, so a missing build must not be reported
    /// as a replay loop that failed every run.
    /// </summary>
    [Fact]
    public async Task MissingCli_FailsClosedInsteadOfReportingZeroPasses()
    {
        var result = await RunScriptAsync(
            "-Runs", "2",
            "-CliPath", Path.Combine(_testRoot, "does-not-exist", "maui.exe"),
            "-AdbPath", Path.Combine(_testRoot, "does-not-exist", "adb.exe"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("was not found", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("REPLAY STABILITY", result.StandardOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// adb is what returns every run to the same starting state. Without it the second run measures
    /// an app the first one left installed and populated, which is not the first-attempt replay the
    /// number is quoted as - and the script would report a complete measurement anyway.
    /// </summary>
    [Fact]
    public async Task MissingAdb_FailsClosedInsteadOfMeasuringADirtyDevice()
    {
        var result = await RunScriptAsync(
            "-Runs", "2",
            "-CliPath", CreateStub("cli-adb-guard.ps1", StubCli),
            "-AdbPath", Path.Combine(_testRoot, "does-not-exist", "adb.exe"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("adb was not found", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("REPLAY STABILITY", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonPositiveRunCount_IsRefused()
    {
        var result = await RunScriptAsync(
            "-Runs", "0",
            "-CliPath", CreateStub("cli-refused.ps1", StubCli),
            "-AdbPath", CreateStub("adb-refused.ps1", StubAdb));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("-Runs", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// A full measurement against a stub CLI: the output directory has to be created, the summary
    /// has to land in it, and <c>passSource</c> has to describe the verdicts these runs really used.
    /// </summary>
    [Fact]
    public async Task Measurement_CreatesItsOutputDirectoryAndReportsTheVerdictSourceItUsed()
    {
        var result = await RunScriptAsync(
            "-Runs", "2",
            "-CliPath", CreateStub("cli-structured.ps1", StubCli),
            "-AdbPath", CreateStub("adb-structured.ps1", StubAdb));

        Assert.Equal(0, result.ExitCode);
        var summaryPath = SummaryPath(result.StandardOutput);
        Assert.True(File.Exists(summaryPath), $"No summary at '{summaryPath}'. stdout={result.StandardOutput}");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(summaryPath));
            var root = document.RootElement;
            Assert.Equal("outcome.status", root.GetProperty("passSource").GetString());
            Assert.Equal(2, root.GetProperty("runs").GetInt32());
            Assert.Equal(2, root.GetProperty("firstAttemptPasses").GetInt32());
            Assert.Equal(1, root.GetProperty("value").GetDouble());
        }
        finally
        {
            CleanUpMeasurement(summaryPath);
        }
    }

    [Fact]
    public async Task MixedEvidence_ReportsEveryVerdictSourceItRead()
    {
        var result = await RunScriptAsync(
            "-Runs", "2",
            "-CliPath", CreateStub("cli-mixed.ps1", StubMixedCli),
            "-AdbPath", CreateStub("adb-mixed.ps1", StubAdb));

        Assert.Equal(0, result.ExitCode);
        var summaryPath = SummaryPath(result.StandardOutput);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(summaryPath));
            // A run decided by the old CLI envelope may never be described as a structured outcome.
            Assert.Equal(
                "legacy:exitCategory+outcome.status",
                document.RootElement.GetProperty("passSource").GetString());
        }
        finally
        {
            CleanUpMeasurement(summaryPath);
        }
    }

    private const string StubAdb = """
        param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Arguments)
        exit 0
        """;

    private const string StubCli = """
        param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Arguments)

        $output = $null
        for ($index = 0; $index -lt $Arguments.Count; $index++) {
            if ($Arguments[$index] -eq '--output') { $output = $Arguments[$index + 1] }
        }
        [void] (New-Item -ItemType Directory -Force -Path $output)
        Set-Content -LiteralPath (Join-Path $output 'flow-run.json') -Value '{"schema":1,"outcome":{"status":"passed","verified":true}}'
        Write-Output '{"ok":true,"exitCategory":"pass"}'
        exit 0
        """;

    private const string StubMixedCli = """
        param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Arguments)

        $output = $null
        for ($index = 0; $index -lt $Arguments.Count; $index++) {
            if ($Arguments[$index] -eq '--output') { $output = $Arguments[$index + 1] }
        }
        [void] (New-Item -ItemType Directory -Force -Path $output)
        # The first run writes a structured outcome; the second leaves only the legacy envelope.
        if ($output.EndsWith('run1')) {
            Set-Content -LiteralPath (Join-Path $output 'flow-run.json') -Value '{"schema":1,"outcome":{"status":"passed","verified":true}}'
        }
        Write-Output '{"ok":true,"exitCategory":"pass"}'
        exit 0
        """;

    private string CreateStub(string name, string body)
    {
        var path = Path.Combine(_testRoot, name);
        File.WriteAllText(path, body, new UTF8Encoding(false));
        return path;
    }

    private string SummaryPath(string standardOutput)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            standardOutput,
            @"summary\s+:\s+(?<path>.+replay-stability\.json)");
        Assert.True(match.Success, $"No summary path in output: {standardOutput}");
        return Path.Combine(_repositoryRoot, match.Groups["path"].Value.Trim());
    }

    private static void CleanUpMeasurement(string summaryPath)
    {
        var directory = Path.GetDirectoryName(summaryPath);
        if (directory is not null && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private async Task<(int ExitCode, string StandardOutput, string StandardError)> RunScriptAsync(
        params string[] arguments)
    {
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
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-File", ScriptPath }.Concat(arguments))
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    private string ScriptPath =>
        Path.Combine(_repositoryRoot, "eng", "devflow", "Measure-ReplayStability.ps1");

    private string WriteJson(string name, string content)
    {
        var path = Path.Combine(_testRoot, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private async Task<JsonElement> RunProbeAsync(string? reportPath, string? cliPath)
    {
        var probePath = Path.Combine(_testRoot, $"probe-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(probePath, Probe, new UTF8Encoding(false));

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
        foreach (var argument in new[]
                 {
                     "-NoLogo",
                     "-NoProfile",
                     "-File",
                     probePath,
                     "-ScriptPath",
                     ScriptPath,
                     "-ReportPath",
                     reportPath ?? string.Empty,
                     "-CliPath",
                     cliPath ?? string.Empty,
                 })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"Probe failed. stdout={stdout}; stderr={stderr}");

        var jsonLine = stdout.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(static line => line.StartsWith("{", StringComparison.Ordinal));
        Assert.True(jsonLine is not null, $"No JSON verdict. stdout={stdout}; stderr={stderr}");
        using var document = JsonDocument.Parse(jsonLine!);
        return document.RootElement.Clone();
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
