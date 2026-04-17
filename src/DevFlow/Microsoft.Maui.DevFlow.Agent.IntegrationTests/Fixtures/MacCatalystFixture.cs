using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches the DevFlow sample app as a Mac Catalyst app.
/// </summary>
public sealed class MacCatalystFixture : AppFixtureBase
{
    Process? _appProcess;
    bool _weOwnTheProcess;

    public override string Platform => "maccatalyst";

    protected override async Task InitializePlatformAsync()
    {
        await WithBuildLockAsync(async () =>
        {
            var projectPath = GetSampleProjectPath();
            await BuildSampleAsync(projectPath, "net10.0-maccatalyst");

            var appPath = FindAppBundle();
            LaunchApp(appPath);
            _weOwnTheProcess = true;
        });
    }

    protected override async Task DisposePlatformAsync()
    {
        if (_weOwnTheProcess && _appProcess is { HasExited: false })
        {
            _appProcess.Kill(entireProcessTree: true);
            try { await _appProcess.WaitForExitAsync(new CancellationTokenSource(5000).Token); } catch { }
        }

        _appProcess?.Dispose();
    }

    static string FindAppBundle()
    {
        var sampleBinDir = Path.Combine(GetSampleBuildOutputRoot(), "net10.0-maccatalyst");

        if (!Directory.Exists(sampleBinDir))
            throw new InvalidOperationException($"Build output directory not found: {sampleBinDir}");

        var appBundles = Directory.GetDirectories(sampleBinDir, "*.app", SearchOption.AllDirectories);

        if (appBundles.Length == 0)
            throw new InvalidOperationException($"No .app bundle found under {sampleBinDir}");

        return appBundles[0];
    }

    void LaunchApp(string appBundlePath)
    {
        var macosDir = Path.Combine(appBundlePath, "Contents", "MacOS");

        if (!Directory.Exists(macosDir))
            throw new InvalidOperationException($"MacOS directory not found at: {macosDir}");

        var executables = Directory.GetFiles(macosDir)
            .Where(f => !Path.GetFileName(f).StartsWith('.'))
            .ToArray();

        if (executables.Length == 0)
            throw new InvalidOperationException($"No executables found in {macosDir}");

        var executablePath = executables[0];

        var psi = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        psi.Environment["DEVFLOW_TEST_PORT"] = AgentPort.ToString();

        _appProcess = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to launch {executablePath}");
    }
}
