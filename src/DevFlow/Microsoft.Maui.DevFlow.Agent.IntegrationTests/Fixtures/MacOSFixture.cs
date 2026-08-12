using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches either the plain .NET AppKit sample or the
/// MAUI-on-AppKit sample head.
/// </summary>
public sealed class MacOSFixture : AppFixtureBase
{
    const string TargetFramework = "net10.0-macos";

    Process? _appProcess;

    public override string Platform => "macos";

    protected override async Task InitializePlatformAsync()
    {
        await WithBuildLockAsync(async () =>
        {
            await BuildSampleAsync(GetSampleProjectPath("macos"), TargetFramework);
            LaunchApp(FindAppBundle());
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

    static string FindAppBundle()
    {
        var sampleBinDir = Path.Combine(GetSampleBuildOutputRoot("macos"), TargetFramework);

        if (!Directory.Exists(sampleBinDir))
            throw new InvalidOperationException($"Build output directory not found: {sampleBinDir}");

        var appBundles = Directory.GetDirectories(sampleBinDir, "*.app", SearchOption.AllDirectories);

        if (appBundles.Length == 0)
            throw new InvalidOperationException($"No .app bundle found under {sampleBinDir}");

        // A macos build emits both a runtime-identifier-specific bundle and, once it has been
        // lipo'd, a universal one at the TFM root.
        return SelectHostArchitectureAppBundle(appBundles, "osx");
    }

    void LaunchApp(string appBundlePath)
    {
        var macosDir = Path.Combine(appBundlePath, "Contents", "MacOS");

        if (!Directory.Exists(macosDir))
            throw new InvalidOperationException($"MacOS directory not found at: {macosDir}");

        var executablePath = Directory.GetFiles(macosDir)
            .FirstOrDefault(f => !Path.GetFileName(f).StartsWith('.'))
            ?? throw new InvalidOperationException($"No executables found in {macosDir}");

        var psi = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
        };

        // Both AppKit sample heads read this to pick the agent port.
        psi.Environment["DEVFLOW_TEST_PORT"] = AgentPort.ToString();

        _appProcess = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to launch {executablePath}");
    }
}
