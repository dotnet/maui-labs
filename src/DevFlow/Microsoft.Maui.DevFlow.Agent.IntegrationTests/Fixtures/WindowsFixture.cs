using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches the DevFlow sample app on Windows.
/// </summary>
public sealed class WindowsFixture : AppFixtureBase
{
    Process? _appProcess;
    Process? _launcherProcess;
    readonly List<Task> _outputPumps = [];
    readonly List<Task> _exitObservations = [];
    readonly object _diagnosticsGate = new();
    string _diagnosticsPath = "";
    volatile bool _stopping;

    public override string Platform => "windows";

    protected override async Task InitializePlatformAsync()
    {
        if (TestFramework.IsNative)
        {
            throw new InvalidOperationException(
                "There is no plain .NET (native) sample head for Windows. " +
                "Run the native suite on android, ios, maccatalyst or macos, " +
                "or unset DEVFLOW_TEST_FRAMEWORK to test the MAUI sample.");
        }

        await WithBuildLockAsync(async () =>
        {
            var projectPath = GetSampleProjectPath();
            var applicationId = $"com.microsoft.maui.devflow.integration{AgentPort}";
            await BuildSampleAsync(
                projectPath,
                "net10.0-windows10.0.19041.0",
                $"-p:ApplicationId={applicationId}");

            var exePath = FindExecutable();
            var diagnosticsDirectory = Path.Combine(
                FindRepoRoot(), "artifacts", "TestResults", "devflow-integration", "windows", "diagnostics");
            Directory.CreateDirectory(diagnosticsDirectory);
            _diagnosticsPath = Path.Combine(diagnosticsDirectory, $"sample-{AgentPort}.log");
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            psi.Environment["DEVFLOW_TEST_PORT"] = AgentPort.ToString();
            psi.Environment["DEVFLOW_TEST_DIAGNOSTICS_PATH"] =
                Path.Combine(diagnosticsDirectory, $"sample-{AgentPort}-unhandled.log");

            _launcherProcess = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to launch {exePath}");
            _appProcess = _launcherProcess;
            _exitObservations.Add(ObserveExitAsync(_launcherProcess, "launcher"));
            _outputPumps.Add(DrainAsync(_launcherProcess.StandardOutput, "stdout"));
            _outputPumps.Add(DrainAsync(_launcherProcess.StandardError, "stderr"));
            WriteDiagnostic($"Launched pid={_launcherProcess.Id}, port={AgentPort}, executable={exePath}.");

            await WaitForAgentAsync(timeoutSeconds: 120);
            var status = await Client.GetStatusAsync();
            var agentPid = status?.App?.ProcessId
                ?? throw new InvalidOperationException("The Windows agent did not report its process ID.");
            if (agentPid != _launcherProcess.Id)
            {
                var actualProcess = Process.GetProcessById(agentPid);
                if (!string.Equals(actualProcess.MainModule?.FileName, exePath, StringComparison.OrdinalIgnoreCase)
                    || actualProcess.StartTime.ToUniversalTime() < _launcherProcess.StartTime.ToUniversalTime())
                {
                    actualProcess.Dispose();
                    throw new InvalidOperationException($"Agent PID {agentPid} is not the owned sample process.");
                }
                _appProcess = actualProcess;
                _exitObservations.Add(ObserveExitAsync(actualProcess, "agent"));
            }
            WriteDiagnostic($"Connected to actual agent pid={agentPid}, launcher pid={_launcherProcess.Id}, port={AgentPort}.");
            Console.WriteLine($"[DevFlow Windows sample] Agent pid={agentPid}; diagnostics: {_diagnosticsPath}");
        });
    }

    async Task ObserveExitAsync(Process process, string role)
    {
        await process.WaitForExitAsync();
        WriteDiagnostic($"{role} pid={process.Id} exited: code={process.ExitCode} " +
            $"(0x{process.ExitCode:X8}), fixtureStopping={_stopping}.");
    }

    async Task DrainAsync(StreamReader reader, string source)
    {
        while (await reader.ReadLineAsync() is { } line)
            WriteDiagnostic($"{source}: {line}");
    }

    void WriteDiagnostic(string message)
    {
        lock (_diagnosticsGate)
            File.AppendAllText(_diagnosticsPath, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
    }

    protected override async Task DisposePlatformAsync()
    {
        _stopping = true;
        try
        {
            foreach (var process in new[] { _appProcess, _launcherProcess }.OfType<Process>().DistinctBy(p => p.Id))
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            await Task.WhenAll(_outputPumps.Concat(_exitObservations)).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (!ReferenceEquals(_appProcess, _launcherProcess))
                _launcherProcess?.Dispose();
            _appProcess?.Dispose();
        }
    }

    static string FindExecutable()
    {
        var binDir = GetSampleBuildOutputRoot();
        var exes = Directory.GetFiles(binDir, "DevFlow.Sample.exe", SearchOption.AllDirectories);

        if (exes.Length == 0)
            throw new InvalidOperationException($"No DevFlow.Sample.exe found under {binDir}");

        return exes[0];
    }
}
