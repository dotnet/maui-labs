using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches the plain .NET macOS (AppKit) sample head.
///
/// This platform is native-only: MAUI has no in-box AppKit backend, so <c>DevFlow.Sample</c> has no
/// macOS head to drive. Without this fixture the AppKit UI backend had no automated coverage at all
/// — every other native backend is exercised through Mac Catalyst, iOS or Android.
/// </summary>
public sealed class MacOSFixture : AppFixtureBase
{
    const string TargetFramework = "net10.0-macos";

    Process? _appProcess;

    public override string Platform => "macos";

    protected override async Task InitializePlatformAsync()
    {
        if (!TestFramework.IsNative)
        {
            throw new InvalidOperationException(
                "DEVFLOW_TEST_PLATFORM=macos is only valid with DEVFLOW_TEST_FRAMEWORK=native: the MAUI " +
                "sample has no AppKit head. Use maccatalyst to drive MAUI on a Mac.");
        }

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
        // lipo'd, a universal one at the TFM root. Prefer the deepest path: the RID-specific bundle
        // is always present, whereas the universal one only appears for some build configurations.
        return appBundles.OrderByDescending(static path => path.Length).First();
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
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // The sample reads this to pick its agent port; see samples/DevFlow.Sample.Native/Shared/SampleAgentOptions.cs.
        psi.Environment["DEVFLOW_TEST_PORT"] = AgentPort.ToString();

        _appProcess = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to launch {executablePath}");
    }
}
