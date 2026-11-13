using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches the DevFlow sample app as a Mac Catalyst app.
/// </summary>
public sealed class MacCatalystFixture : AppFixtureBase
{
    Process? _appProcess;
    bool _disposing;
    bool _weOwnTheProcess;

    public override string Platform => "maccatalyst";

    protected override async Task InitializePlatformAsync()
    {
        await WithBuildLockAsync(async () =>
        {
            var projectPath = GetSampleProjectPath("maccatalyst");
            await BuildSampleAsync(projectPath, "net11.0-maccatalyst");

            var appPath = FindAppBundle();
            LaunchApp(appPath);
            _weOwnTheProcess = true;
        });
    }

    protected override async Task DisposePlatformAsync()
    {
        _disposing = true;

        if (_weOwnTheProcess && _appProcess is { HasExited: false })
        {
            _appProcess.Kill(entireProcessTree: true);
            try { await _appProcess.WaitForExitAsync(new CancellationTokenSource(5000).Token); } catch { }
        }

        _appProcess?.Dispose();
    }

    protected override string? GetPlatformStartupFailure()
    {
        if (_appProcess is not { HasExited: true } process)
            return null;

        return FormatProcessExit(process.Id, "before the DevFlow agent became ready");
    }

    static string FindAppBundle()
    {
        var sampleBinDir = Path.Combine(GetSampleBuildOutputRoot("maccatalyst"), "net11.0-maccatalyst");

        if (!Directory.Exists(sampleBinDir))
            throw new InvalidOperationException($"Build output directory not found: {sampleBinDir}");

        var appBundles = Directory.GetDirectories(sampleBinDir, "*.app", SearchOption.AllDirectories);

        if (appBundles.Length == 0)
            throw new InvalidOperationException($"No .app bundle found under {sampleBinDir}");

        return SelectHostArchitectureAppBundle(appBundles, "maccatalyst");
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

        var launch = new ProcessStartInfo("/usr/bin/open")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        launch.ArgumentList.Add("--env");
        launch.ArgumentList.Add($"DEVFLOW_TEST_PORT={AgentPort}");
        launch.ArgumentList.Add("-n");
        launch.ArgumentList.Add(appBundlePath);

        using var launcher = Process.Start(launch)
            ?? throw new InvalidOperationException($"Failed to launch {appBundlePath} with LaunchServices.");
        var stdout = launcher.StandardOutput.ReadToEnd();
        var stderr = launcher.StandardError.ReadToEnd();
        launcher.WaitForExit();
        if (launcher.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"LaunchServices failed to launch {appBundlePath} with code {launcher.ExitCode}.{Environment.NewLine}" +
                $"STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}" +
                $"STDERR:{Environment.NewLine}{stderr}");
        }

        var deadline = DateTime.UtcNow.AddSeconds(15);
        Process? process = null;
        while (process == null && DateTime.UtcNow < deadline)
        {
            process = FindProcess(executablePath);

            if (process == null)
                Thread.Sleep(100);
        }

        if (process == null)
            throw new InvalidOperationException($"LaunchServices started {appBundlePath}, but its process was not found.");

        var processId = process.Id;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            if (!_disposing)
            {
                Console.WriteLine(FormatProcessExit(processId, "while the integration tests were running"));
                try
                {
                    Console.WriteLine(CaptureProcessDiagnostics(processId));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unable to capture Mac Catalyst process diagnostics: {ex.Message}");
                }
            }
        };

        _appProcess = process;
    }

    static Process? FindProcess(string executablePath)
    {
        var candidates = Process.GetProcessesByName(Path.GetFileName(executablePath));
        var process = candidates.FirstOrDefault();
        foreach (var extra in candidates.Skip(1))
            extra.Dispose();
        return process;
    }

    static string CaptureProcessDiagnostics(int processId)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/log")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("show");
        startInfo.ArgumentList.Add("--last");
        startInfo.ArgumentList.Add("2m");
        startInfo.ArgumentList.Add("--style");
        startInfo.ArgumentList.Add("compact");
        startInfo.ArgumentList.Add("--predicate");
        startInfo.ArgumentList.Add($"processIdentifier == {processId}");

        using var diagnostics = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the macOS unified log query.");
        var stdout = diagnostics.StandardOutput.ReadToEnd();
        var stderr = diagnostics.StandardError.ReadToEnd();
        diagnostics.WaitForExit();
        return $"Unified log for process {processId} (exit {diagnostics.ExitCode}):{Environment.NewLine}" +
            stdout + stderr;
    }

    static string FormatProcessExit(int processId, string context)
        => $"Mac Catalyst app process {processId} exited {context}.";
}
