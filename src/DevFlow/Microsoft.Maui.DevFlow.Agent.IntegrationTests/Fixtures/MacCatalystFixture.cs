using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches the DevFlow sample app as a Mac Catalyst app.
/// </summary>
public sealed class MacCatalystFixture : AppFixtureBase
{
    readonly object _outputLock = new();
    readonly List<string> _output = [];
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

        return FormatProcessExit(process, "before the DevFlow agent became ready");
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

        var psi = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        psi.Environment["DEVFLOW_TEST_PORT"] = AgentPort.ToString();

        var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true,
        };
        process.OutputDataReceived += (_, e) => CaptureOutput("stdout", e.Data);
        process.ErrorDataReceived += (_, e) => CaptureOutput("stderr", e.Data);
        process.Exited += (_, _) =>
        {
            if (!_disposing)
                Console.WriteLine(FormatProcessExit(process, "while the integration tests were running"));
        };

        _appProcess = process;
        if (!process.Start())
            throw new InvalidOperationException($"Failed to launch {executablePath}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    void CaptureOutput(string stream, string? line)
    {
        if (line == null)
            return;

        lock (_outputLock)
        {
            _output.Add($"[{stream}] {line}");
            if (_output.Count > 200)
                _output.RemoveAt(0);
        }
    }

    string FormatCapturedOutput()
    {
        lock (_outputLock)
        {
            return _output.Count == 0
                ? "\nThe app produced no captured output."
                : $"\nCaptured app output:\n{string.Join(Environment.NewLine, _output)}";
        }
    }

    string FormatProcessExit(Process process, string context)
        => $"Mac Catalyst app exited with code {process.ExitCode} {context}." + FormatCapturedOutput();
}
