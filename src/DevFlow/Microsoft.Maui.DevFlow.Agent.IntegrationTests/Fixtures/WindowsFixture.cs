using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches the DevFlow sample app on Windows.
/// </summary>
public sealed class WindowsFixture : AppFixtureBase
{
    Process? _appProcess;

    public override string Platform => "windows";

    protected override async Task InitializePlatformAsync()
    {
        await WithBuildLockAsync(async () =>
        {
            var projectPath = GetSampleProjectPath();
            await BuildSampleAsync(projectPath, "net10.0-windows10.0.19041.0");

            var exePath = FindExecutable();
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            psi.Environment["DEVFLOW_TEST_PORT"] = AgentPort.ToString();

            _appProcess = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to launch {exePath}");
        });
    }

    protected override async Task DisposePlatformAsync()
    {
        if (_appProcess is { HasExited: false })
        {
            _appProcess.Kill(entireProcessTree: true);
            try { await _appProcess.WaitForExitAsync(new CancellationTokenSource(5000).Token); } catch { }
        }

        _appProcess?.Dispose();
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
