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
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
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
