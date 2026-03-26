using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Client-side logic for ensuring the broker is running and querying it.
/// Used by CLI commands and (in future) by agents.
/// </summary>
public static class BrokerClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>
    /// Ensures the broker daemon is running. Starts it if needed.
    /// Returns the broker port, or null if broker could not be started.
    /// </summary>
    public static async Task<int?> EnsureBrokerRunningAsync()
    {
        // 1. Determine port to try
        var port = ReadBrokerPort() ?? BrokerServer.DefaultPort;

        // 2. TCP connect to check liveness
        if (await IsBrokerAliveAsync(port))
            return port;

        // 3. Not running — clean up stale state and start new broker
        CleanupStaleBroker();
        return await StartBrokerAsync();
    }

    /// <summary>
    /// Lists all agents registered with the broker.
    /// </summary>
    public static async Task<AgentRegistration[]?> ListAgentsAsync(int brokerPort)
    {
        try
        {
            var response = await _http.GetStringAsync($"http://localhost:{brokerPort}/api/agents");
            return CliJson.Deserialize<AgentRegistration[]>(response);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds an agent by project path and TFM.
    /// </summary>
    public static async Task<AgentRegistration?> FindAgentAsync(int brokerPort, string project, string tfm)
    {
        var agents = await ListAgentsAsync(brokerPort);
        if (agents == null) return null;

        var id = AgentRegistration.ComputeId(project, tfm);
        return agents.FirstOrDefault(a => a.Id == id);
    }

    /// <summary>
    /// Resolves the agent port for the current project context.
    /// Tries: broker lookup by project hash → single agent auto-select → null.
    /// </summary>
    public static async Task<int?> ResolveAgentPortAsync(int brokerPort, string? projectPath = null, string? tfm = null)
    {
        var agents = await ListAgentsAsync(brokerPort);
        if (agents == null || agents.Length == 0) return null;

        // If project+TFM provided, look for exact match
        if (projectPath != null && tfm != null)
        {
            var id = AgentRegistration.ComputeId(projectPath, tfm);
            var match = agents.FirstOrDefault(a => a.Id == id);
            if (match != null) return match.Port;
        }

        // If project provided (no TFM), match by project path
        if (projectPath != null)
        {
            var matches = agents.Where(a => a.Project == projectPath).ToArray();
            if (matches.Length == 1) return matches[0].Port;
        }

        // If only one agent, auto-select
        if (agents.Length == 1) return agents[0].Port;

        return null;
    }

    /// <summary>
    /// Sends a shutdown request to the broker.
    /// </summary>
    public static async Task<bool> ShutdownBrokerAsync(int? port = null)
    {
        port ??= ReadBrokerPort() ?? BrokerServer.DefaultPort;
        try
        {
            await _http.PostAsync($"http://localhost:{port}/api/shutdown", new StringContent(""));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> IsBrokerAliveAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("localhost", port).WaitAsync(TimeSpan.FromMilliseconds(500));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int? ReadBrokerPort()
    {
        try
        {
            if (!File.Exists(BrokerPaths.StateFile)) return null;
            var json = File.ReadAllText(BrokerPaths.StateFile);
            var state = CliJson.Deserialize<BrokerState>(json);
            return state?.Port;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Public accessor for reading the broker port from the state file.
    /// </summary>
    public static int? ReadBrokerPortPublic() => ReadBrokerPort();

    /// <summary>
    /// High-level port resolution: ensure broker running → resolve by project → auto-select → config fallback → default.
    /// Returns the resolved agent port.
    /// </summary>
    public static async Task<int?> ResolveAgentPortForProjectAsync()
    {
        var brokerPort = ReadBrokerPort() ?? BrokerServer.DefaultPort;

        if (!await IsBrokerAliveAsync(brokerPort))
        {
            var started = await EnsureBrokerRunningAsync();
            if (started.HasValue)
                brokerPort = started.Value;
            else
                return ReadConfigPort() ?? 9223;
        }

        // Try project-specific resolution
        var csproj = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj").FirstOrDefault();
        if (csproj is not null)
        {
            var port = await ResolveAgentPortAsync(brokerPort, Path.GetFullPath(csproj));
            if (port.HasValue) return port.Value;
        }

        // Try auto-select (single agent)
        var autoPort = await ResolveAgentPortAsync(brokerPort);
        if (autoPort.HasValue) return autoPort.Value;

        // No single match — return null so callers can handle multi-agent case
        return null;
    }

    /// <summary>
    /// Read port from .mauidevflow config file in the current directory.
    /// </summary>
    public static int? ReadConfigPort()
    {
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), ".mauidevflow");
        if (!File.Exists(configPath)) return null;
        try
        {
            var json = CliJson.ParseElement(File.ReadAllText(configPath));
            if (json.TryGetProperty("port", out var portEl) && portEl.TryGetInt32(out var p))
                return p;
        }
        catch { }
        return null;
    }

    private static void CleanupStaleBroker()
    {
        try
        {
            if (!File.Exists(BrokerPaths.StateFile)) return;
            var json = File.ReadAllText(BrokerPaths.StateFile);
            var state = CliJson.Deserialize<BrokerState>(json);
            if (state == null) return;

            // Try to kill hung process
            try
            {
                var process = Process.GetProcessById(state.Pid);
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
            }
            catch { /* process already dead */ }

            File.Delete(BrokerPaths.StateFile);
        }
        catch { }
    }

    internal static async Task<int?> StartBrokerAsync()
    {
        try
        {
            // Find the CLI executable path
            var exePath = Environment.ProcessPath;
            if (exePath == null) return null;

            string fileName;
            string arguments;

            // If running via `dotnet run` or `dotnet <dll>`, exePath is the dotnet host.
            // In that case, use `dotnet <entryDll> broker start --foreground` instead.
            if (exePath.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase)
                || exePath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                var dllPath = ResolveManagedEntryAssemblyPath();
                if (string.IsNullOrEmpty(dllPath)) return null;
                fileName = exePath;
                arguments = $"\"{dllPath}\" broker start --foreground";
            }
            else
            {
                fileName = exePath;
                arguments = "broker start --foreground";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            };

            var process = Process.Start(startInfo);
            if (process == null) return null;

            // Close inherited handles so the child doesn't block on broken pipes
            process.StandardOutput.Close();
            process.StandardError.Close();
            process.StandardInput.Close();

            // Poll until broker is ready
            var port = BrokerServer.DefaultPort;
            for (int i = 0; i < 25; i++) // 25 * 200ms = 5s
            {
                await Task.Delay(200);

                // Check if state file was written (may have a different port)
                var statePort = ReadBrokerPort();
                if (statePort.HasValue) port = statePort.Value;

                if (await IsBrokerAliveAsync(port))
                    return port;
            }

            return null; // Timeout
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveManagedEntryAssemblyPath()
    {
        var assemblyName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
        if (string.IsNullOrEmpty(assemblyName))
            return null;

        var candidate = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
        return File.Exists(candidate) ? candidate : null;
    }
}
