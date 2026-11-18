using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.DevFlow.Broker;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds the web (WebAssembly) sample, serves it with <c>dotnet run</c>, and opens it in
/// a headless Chromium browser. A browser page cannot listen on a port, so the broker serves the
/// agent's port and relays requests over the agent's WebSocket; when no broker is running, the
/// fixture hosts one in-process for the duration of the run.
/// </summary>
/// <remarks>
/// Set <c>DEVFLOW_TEST_BROWSER</c> to a Chrome, Edge or Chromium executable to choose the browser.
/// The sample uses the community Platform.Maui.Web backend; set <c>DEVFLOW_TEST_WEB_BACKEND_PROJECT</c>
/// to a mauiplatforms checkout's <c>src/Platform.Maui.Web/Platform.Maui.Web.csproj</c> to build
/// against it instead of the nuget.org package.
/// </remarks>
public sealed partial class WebFixture : AppFixtureBase
{
    const string TargetFramework = "net10.0";

    Process? _server;
    Process? _browser;
    BrokerServer? _broker;
    CancellationTokenSource? _brokerCts;
    Task? _brokerTask;
    string? _browserProfile;

    public override string Platform => "web";

    protected override async Task InitializePlatformAsync()
    {
        if (TestFramework.IsNative)
        {
            throw new InvalidOperationException(
                "DEVFLOW_TEST_PLATFORM=web only supports DEVFLOW_TEST_FRAMEWORK=maui.");
        }

        var browser = FindBrowser()
            ?? throw new InvalidOperationException(
                "No Chromium browser found. Install Chrome, Edge or Chromium, or set DEVFLOW_TEST_BROWSER.");

        await EnsureBrokerAsync();

        var projectPath = GetSampleProjectPath(Platform);
        await WithBuildLockAsync(() => BuildSampleAsync(
            projectPath,
            TargetFramework,
            CreateBuildArguments(Environment.GetEnvironmentVariable("DEVFLOW_TEST_WEB_BACKEND_PROJECT"))));

        var appUrl = await StartServerAsync(projectPath);
        _browserProfile = Directory.CreateTempSubdirectory("devflow-web-").FullName;
        _browser = Process.Start(new ProcessStartInfo(browser)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList =
            {
                "--headless=new",
                "--no-first-run",
                "--no-default-browser-check",
                "--window-size=1280,800",
                $"--user-data-dir={_browserProfile}",
                CreatePageUrl(appUrl, AgentPort)
            }
        }) ?? throw new InvalidOperationException($"Failed to launch {browser}");
        DrainOutput(_browser);
    }

    protected override async Task DisposePlatformAsync()
    {
        foreach (var process in new[] { _browser, _server }.OfType<Process>())
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(new CancellationTokenSource(5000).Token);
                }
            }
            catch
            {
            }
            process.Dispose();
        }

        if (_broker is not null)
        {
            _brokerCts!.Cancel();
            try { await _brokerTask!.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
            _broker.Dispose();
            _brokerCts.Dispose();
        }

        if (_browserProfile is not null)
        {
            try { Directory.Delete(_browserProfile, recursive: true); } catch { }
        }
    }

    internal static string CreateBuildArguments(string? backendProject)
        => string.IsNullOrWhiteSpace(backendProject)
            ? ""
            // The backend checkout defaults to its own (newer) .NET; build it for .NET 10 with the
            // matching MAUI. These must be global properties: restore ignores per-reference ones.
            : $"\"-p:PlatformMauiWebProject={Path.GetFullPath(backendProject)}\" -p:BaseTargetFramework=net10.0 -p:MauiVersion=10.0.120";

    internal static string CreatePageUrl(string appUrl, int agentPort)
        => $"{appUrl.TrimEnd('/')}/?devflowPort={agentPort}";

    /// <summary>Reads the address the WebAssembly dev server prints when it starts.</summary>
    internal static string? ParseAppUrl(string line)
    {
        var match = AppUrlPattern().Match(line);
        return match.Success ? match.Groups["url"].Value : null;
    }

    async Task EnsureBrokerAsync()
    {
        if (await BrokerClient.GetRunningBrokerPortAsync() is not null)
            return;

        _brokerCts = new CancellationTokenSource();
        _broker = new BrokerServer(idleTimeout: TimeSpan.FromHours(1));
        _brokerTask = _broker.RunAsync(_brokerCts.Token);
        for (var attempt = 0; attempt < 100 && !_broker.IsRunning; attempt++)
            await Task.Delay(50);
        if (!_broker.IsRunning)
            throw new InvalidOperationException($"Could not start a DevFlow broker on port {BrokerServer.DefaultPort}.");
    }

    async Task<string> StartServerAsync(string projectPath)
    {
        var appUrl = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _server = new Process
        {
            StartInfo = new ProcessStartInfo(ResolveDotnetPath())
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(projectPath)!,
                ArgumentList = { "run", "--project", projectPath, "--no-build", "-c", "Debug", "-f", TargetFramework }
            }
        };
        _server.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null && ParseAppUrl(e.Data) is { } url)
                appUrl.TrySetResult(url);
        };
        _server.ErrorDataReceived += (_, _) => { };
        _server.Start();
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();

        return await appUrl.Task.WaitAsync(TimeSpan.FromSeconds(120));
    }

    static void DrainOutput(Process process)
    {
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    static string? FindBrowser()
    {
        var configured = Environment.GetEnvironmentVariable("DEVFLOW_TEST_BROWSER");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string[] candidates =
        [
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
            "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
            "/Applications/Chromium.app/Contents/MacOS/Chromium",
            "/usr/bin/google-chrome",
            "/usr/bin/chromium",
            "/usr/bin/chromium-browser",
            "/usr/bin/microsoft-edge",
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    [GeneratedRegex(@"App url:\s*(?<url>http://[^\s/]+/)")]
    private static partial Regex AppUrlPattern();
}
