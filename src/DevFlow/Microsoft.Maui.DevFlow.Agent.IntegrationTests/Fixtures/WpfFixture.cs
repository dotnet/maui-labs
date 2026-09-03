using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches the MAUI WPF DevFlow sample.
/// </summary>
public sealed class WpfFixture : AppFixtureBase
{
    const string TargetFramework = "net10.0-windows";

    Process? _appProcess;

    public override string Platform => "wpf";

    protected override async Task InitializePlatformAsync()
    {
        if (TestFramework.IsNative)
        {
            throw new InvalidOperationException(
                "DEVFLOW_TEST_PLATFORM=wpf only supports DEVFLOW_TEST_FRAMEWORK=maui.");
        }

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The WPF integration fixture requires Windows.");

        await WithBuildLockAsync(async () =>
        {
            await BuildSampleAsync(GetSampleProjectPath("wpf"), TargetFramework);
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
        var outputDirectory = Path.Combine(GetSampleBuildOutputRoot("wpf"), TargetFramework);
        var executable = Directory
            .GetFiles(outputDirectory, "DevFlow.Sample.WPF.exe", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (executable is null)
            throw new InvalidOperationException($"WPF sample executable not found under: {outputDirectory}");

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
