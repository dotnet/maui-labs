using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches the MAUI GTK4 sample under the current display server.
/// CI provides that display with Xvfb.
/// </summary>
public sealed class GtkFixture : AppFixtureBase
{
    const string TargetFramework = "net10.0";

    Process? _appProcess;

    public override string Platform => "gtk";

    protected override async Task InitializePlatformAsync()
    {
        if (TestFramework.IsNative)
        {
            throw new InvalidOperationException(
                "DEVFLOW_TEST_PLATFORM=gtk only supports DEVFLOW_TEST_FRAMEWORK=maui.");
        }

        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("The GTK integration fixture requires Linux.");

        await WithBuildLockAsync(async () =>
        {
            await BuildSampleAsync(GetSampleProjectPath("gtk"), TargetFramework);
            LaunchApp(FindExecutable());
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
        var outputDirectory = Path.Combine(GetSampleBuildOutputRoot("gtk"), TargetFramework);
        var executable = Directory
            .GetFiles(outputDirectory, "DevFlow.Sample.Linux", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (executable is null)
            throw new InvalidOperationException($"GTK sample executable not found under: {outputDirectory}");

        return executable;
    }

    void LaunchApp(string executablePath)
    {
        var psi = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
        };
        psi.Environment["DEVFLOW_TEST_PORT"] = AgentPort.ToString();

        _appProcess = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to launch {executablePath}");
    }
}
