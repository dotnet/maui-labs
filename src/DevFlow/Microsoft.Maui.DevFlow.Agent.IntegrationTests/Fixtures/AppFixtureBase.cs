using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Shared base class for platform-specific app fixtures. Handles port allocation,
/// agent readiness polling, sample app build invocation, and client setup.
/// </summary>
public abstract class AppFixtureBase : IAppFixture
{
    bool _disposed;

    public AgentClient Client { get; private set; } = null!;
    public HttpClient Http { get; private set; } = null!;
    public int AgentPort { get; private set; }
    public string AgentBaseUrl => $"http://localhost:{AgentPort}";
    public abstract string Platform { get; }

    protected AppFixtureBase()
    {
        AgentPort = int.TryParse(
            Environment.GetEnvironmentVariable("DEVFLOW_TEST_PORT"), out var port) ? port : 0;
    }

    public async Task InitializeAsync()
    {
        if (AgentPort > 0)
        {
            SetupClients();

            if (await IsAgentReadyAsync())
                return;
        }

        if (AgentPort == 0)
            AgentPort = FindFreePort();

        SetupClients();

        await InitializePlatformAsync();
        await WaitForAgentAsync(timeoutSeconds: 120);
    }

    public async Task DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        Http?.Dispose();
        Client?.Dispose();

        await DisposePlatformAsync();
    }

    protected abstract Task InitializePlatformAsync();
    protected abstract Task DisposePlatformAsync();

    void SetupClients()
    {
        Http?.Dispose();
        Client?.Dispose();

        Http = new HttpClient
        {
            BaseAddress = new Uri(AgentBaseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };
        Client = new AgentClient("localhost", AgentPort)
        {
            TransientFailureRetryCount = Platform == "android" ? 8 : 0,
            TransientFailureRetryDelay = TimeSpan.FromMilliseconds(250),
        };
    }

    protected async Task<bool> IsAgentReadyAsync()
    {
        try
        {
            var status = await Client.GetStatusAsync();
            return status != null;
        }
        catch
        {
            return false;
        }
    }

    protected async Task WaitForAgentAsync(int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var status = await Client.GetStatusAsync();
                if (status != null)
                    return;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException(
            $"Agent did not become ready within {timeoutSeconds}s on port {AgentPort}. " +
            $"Last error: {lastException?.GetType().Name}: {lastException?.Message}");
    }

    protected static async Task BuildSampleAsync(string projectPath, string targetFramework, string? extraArgs = null)
    {
        CleanBuildOutputs(projectPath, targetFramework);

        var args = $"build \"{projectPath}\" -f {targetFramework} -c Debug --nologo -v q";
        if (!string.IsNullOrEmpty(extraArgs))
            args += $" {extraArgs}";

        var psi = new ProcessStartInfo(ResolveDotnetPath(), args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet build");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet build failed (exit code {process.ExitCode}).\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }
    }

    /// <summary>
    /// Finds the dotnet muxer to build the sample with. This has to be the same install that is
    /// running the tests: side-by-side installs are common on developer machines, and only the one
    /// selected by the repo's global.json has an SDK new enough to build the samples.
    /// </summary>
    private static string ResolveDotnetPath()
    {
        // The SDK sets this for any process it launches, including the test host.
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(hostPath) && File.Exists(hostPath))
            return hostPath;

        // Otherwise the process running us is usually the muxer itself.
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) &&
            Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        if (File.Exists("/usr/local/share/dotnet/dotnet"))
            return "/usr/local/share/dotnet/dotnet";

        return "dotnet";
    }

    protected static async Task WithBuildLockAsync(Func<Task> action, TimeSpan? timeout = null)
    {
        var repoRoot = FindRepoRoot();
        var lockFilePath = Path.Combine(repoRoot, "artifacts", ".devflow-integration-build.lock");
        await using var buildLock = await AcquireBuildLockAsync(lockFilePath, timeout ?? TimeSpan.FromMinutes(10));
        await action();
    }

    private static async Task<FileStream> AcquireBuildLockAsync(string lockFilePath, TimeSpan timeout)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lockFilePath)!);
        var deadline = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                return new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(250);
            }
        }

        throw new TimeoutException($"Timed out waiting for integration build lock: {lockFilePath}");
    }

    private static void CleanBuildOutputs(string projectPath, string targetFramework)
    {
        var projectDir = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Could not determine project directory for '{projectPath}'.");
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var repoRoot = FindRepoRoot();

        var paths = new[]
        {
            Path.Combine(repoRoot, "artifacts", "bin", projectName, "Debug", targetFramework),
            Path.Combine(repoRoot, "artifacts", "obj", projectName, "Debug", targetFramework),
            Path.Combine(projectDir, "bin", "Debug", targetFramework),
            Path.Combine(projectDir, "obj", "Debug", targetFramework),
        };

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    protected static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "MauiLabs.slnx")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not find repository root (looked for MauiLabs.slnx). " +
            "Ensure tests are run from within the repository.");
    }

    protected static string GetSampleProjectPath()
    {
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(repoRoot, "samples", "DevFlow.Sample", "DevFlow.Sample.csproj");
        if (!File.Exists(path))
            throw new InvalidOperationException($"Sample project not found at: {path}");

        return path;
    }

    /// <summary>
    /// Resolves the sample project for the framework under test: the MAUI sample by default, or
    /// the plain .NET head matching <paramref name="platform"/> when DEVFLOW_TEST_FRAMEWORK=native.
    /// </summary>
    protected static string GetSampleProjectPath(string platform)
    {
        if (!TestFramework.IsNative)
            return GetSampleProjectPath();

        var head = TestFramework.NativeHeadFor(platform);
        var path = Path.Combine(
            FindRepoRoot(), "samples", "DevFlow.Sample.Native", head, $"{NativeSampleName(platform)}.csproj");

        if (!File.Exists(path))
            throw new InvalidOperationException($"Native sample project not found at: {path}");

        return path;
    }

    /// <summary>Assembly/project name of the native sample head for a fixture platform.</summary>
    protected static string NativeSampleName(string platform)
        => $"DevFlow.Sample.Native.{TestFramework.NativeHeadFor(platform)}";

    protected static string GetSampleBuildOutputRoot()
    {
        var repoRoot = FindRepoRoot();
        var artifactsPath = Path.Combine(repoRoot, "artifacts", "bin", "DevFlow.Sample", "Debug");
        if (Directory.Exists(artifactsPath))
            return artifactsPath;

        var projectBinPath = Path.Combine(repoRoot, "samples", "DevFlow.Sample", "bin", "Debug");
        if (Directory.Exists(projectBinPath))
            return projectBinPath;

        throw new InvalidOperationException(
            $"Could not locate DevFlow.Sample build output. Checked '{artifactsPath}' and '{projectBinPath}'.");
    }

    /// <summary>
    /// Resolves the build output root for the framework under test. Mirrors
    /// <see cref="GetSampleProjectPath(string)"/>.
    /// </summary>
    protected static string GetSampleBuildOutputRoot(string platform)
    {
        if (!TestFramework.IsNative)
            return GetSampleBuildOutputRoot();

        var repoRoot = FindRepoRoot();
        var name = NativeSampleName(platform);

        var artifactsPath = Path.Combine(repoRoot, "artifacts", "bin", name, "Debug");
        if (Directory.Exists(artifactsPath))
            return artifactsPath;

        var projectBinPath = Path.Combine(
            repoRoot, "samples", "DevFlow.Sample.Native", TestFramework.NativeHeadFor(platform), "bin", "Debug");
        if (Directory.Exists(projectBinPath))
            return projectBinPath;

        throw new InvalidOperationException(
            $"Could not locate {name} build output. Checked '{artifactsPath}' and '{projectBinPath}'.");
    }

    /// <summary>
    /// Picks the <c>.app</c> bundle built for this machine's architecture.
    /// </summary>
    /// <remarks>
    /// A macOS or Mac Catalyst build emits one bundle per RuntimeIdentifier, and Release also
    /// lipo's a universal one at the target-framework root. Choosing between them by enumeration
    /// order or path length is only ever accidentally right, and choosing wrong launches the x64
    /// slice under Rosetta on Apple Silicon — the app still runs, so nothing fails, it just is not
    /// the architecture under test. Prefer the host RID, fall back to the universal bundle, and
    /// refuse to launch a foreign-architecture one.
    /// </remarks>
    internal static string SelectHostArchitectureAppBundle(IReadOnlyList<string> bundles, string ridPrefix)
    {
        var hostArch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var hostRid = $"{ridPrefix}-{hostArch}";

        static bool HasSegment(string path, Func<string, bool> predicate) =>
            path.Split(Path.DirectorySeparatorChar).Any(predicate);

        var hostBundle = bundles.FirstOrDefault(b => HasSegment(b, segment => segment == hostRid));
        if (hostBundle is not null)
            return hostBundle;

        // The universal bundle is the one that does not sit under any RID directory.
        var universal = bundles.FirstOrDefault(
            b => !HasSegment(b, segment => segment.StartsWith($"{ridPrefix}-", StringComparison.Ordinal)));
        if (universal is not null)
            return universal;

        throw new InvalidOperationException(
            $"No .app bundle was built for {hostRid}. Found only: {string.Join(", ", bundles)}. " +
            "Refusing to launch a foreign-architecture bundle, which would run under Rosetta.");
    }

    protected static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    protected static async Task<(string Stdout, string Stderr, int ExitCode)> RunProcessAsync(
        string fileName,
        string arguments,
        IDictionary<string, string>? envVars = null,
        int timeoutSeconds = 300)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (envVars != null)
        {
            foreach (var (key, value) in envVars)
                psi.Environment[key] = value;
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start: {fileName} {arguments}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cts.Token));
        }
        catch (OperationCanceledException ex)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException(
                $"Process timed out after {timeoutSeconds}s: {fileName} {arguments}", ex);
        }

        return (stdoutTask.Result, stderrTask.Result, process.ExitCode);
    }

    protected static async Task<string> RunProcessCheckedAsync(
        string fileName,
        string arguments,
        IDictionary<string, string>? envVars = null,
        int timeoutSeconds = 300)
    {
        var (stdout, stderr, exitCode) = await RunProcessAsync(fileName, arguments, envVars, timeoutSeconds);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process failed: {fileName} {arguments} (exit code {exitCode})\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }

        return stdout;
    }
}
