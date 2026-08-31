using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
    /// Returns the broker port only when an existing broker is reachable.
    /// Does not start a broker or clean up stale broker state.
    /// </summary>
    internal static async Task<int?> GetRunningBrokerPortAsync()
    {
        var port = ReadBrokerPort() ?? BrokerServer.DefaultPort;
        return await IsBrokerAliveAsync(port) ? port : null;
    }

    internal static int? GetRunningBrokerPort()
    {
        var port = ReadBrokerPort() ?? BrokerServer.DefaultPort;
        return IsBrokerAlive(port) ? port : null;
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

    public static AgentRegistration[]? ListAgents(int brokerPort)
    {
        try
        {
            var response = GetString($"http://localhost:{brokerPort}/api/agents");
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
        => (await ResolveAgentAsync(brokerPort, projectPath, tfm))?.Port;

    /// <summary>
    /// Resolves the agent registration for the current project context.
    /// Tries: broker lookup by project hash → single agent auto-select → null.
    /// </summary>
    public static async Task<AgentRegistration?> ResolveAgentAsync(int brokerPort, string? projectPath = null, string? tfm = null)
    {
        var agents = await ListAgentsAsync(brokerPort);
        if (agents == null || agents.Length == 0) return null;

        return ResolveAgent(agents, projectPath, tfm);
    }

    public static AgentRegistration? ResolveAgent(int brokerPort, string? projectPath = null, string? tfm = null)
    {
        var agents = ListAgents(brokerPort);
        if (agents == null || agents.Length == 0) return null;

        return ResolveAgent(agents, projectPath, tfm);
    }

    /// <summary>Reads or explicitly changes a broker-owned route checkpoint for a selected agent.</summary>
    public static async Task<RouteCheckpointStatus> ControlCheckpointAsync(
        int brokerPort,
        string agentId,
        string action = "status")
    {
        try
        {
            var url = $"http://localhost:{brokerPort}/api/checkpoints/{Uri.EscapeDataString(agentId)}";
            using var response = string.Equals(action, "status", StringComparison.OrdinalIgnoreCase)
                ? await _http.GetAsync(url)
                : await _http.PostAsync(
                    url,
                    new StringContent(CliJson.SerializeUntyped(new { action }, indented: false), Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();
            var result = CliJson.Deserialize<RouteCheckpointStatus>(body) ?? new RouteCheckpointStatus
            {
                Ok = false,
                Warning = "Broker returned an invalid checkpoint response."
            };
            if (!response.IsSuccessStatusCode)
                result.Ok = false;
            return result;
        }
        catch
        {
            return new RouteCheckpointStatus
            {
                Ok = false,
                Connected = false,
                Warning = "The DevFlow broker is unavailable."
            };
        }
    }

    internal static AgentRegistration? ResolveAgent(AgentRegistration[] agents, string? projectPath = null, string? tfm = null)
    {
        // If project+TFM provided, look for exact match
        if (projectPath != null && tfm != null)
        {
            var id = AgentRegistration.ComputeId(projectPath, tfm);
            var match = agents.FirstOrDefault(a => a.Id == id);
            if (match != null) return match;

            var tfmMatches = agents
                .Where(a => AgentMatchesProject(a, projectPath) && a.Tfm.Equals(tfm, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (tfmMatches.Length == 1) return tfmMatches[0];
        }

        // If project provided (no TFM), match by project path
        if (projectPath != null)
        {
            var matches = agents.Where(a => AgentMatchesProject(a, projectPath)).ToArray();
            if (matches.Length == 1) return matches[0];
        }

        // If only one agent, auto-select
        if (agents.Length == 1) return agents[0];

        return null;
    }

    static bool AgentMatchesProject(AgentRegistration agent, string projectPath)
        => agent.Project.Equals(projectPath, StringComparison.OrdinalIgnoreCase);

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

    private static bool IsBrokerAlive(int port)
    {
        try
        {
            return TryConnect(IPAddress.Loopback, port) || TryConnect(IPAddress.IPv6Loopback, port);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryConnect(IPAddress address, int port)
    {
        using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            Blocking = false
        };

        try
        {
            socket.Connect(new IPEndPoint(address, port));
            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.InProgress or SocketError.AlreadyInProgress)
        {
            if (!socket.Poll(500_000, SelectMode.SelectWrite))
                return false;

            var error = (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error)!;
            return error == 0;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static string GetString(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = _http.Send(request);
        response.EnsureSuccessStatusCode();
        using var stream = response.Content.ReadAsStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
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
    /// Indicates whether the current reachable broker has the owner-file-only native-host approval
    /// authority needed by an Inspector host. This deliberately returns only a boolean; callers
    /// must never receive the token.
    /// </summary>
    internal static bool HasNativeHostApprovalAuthority()
    {
        try
        {
            if (!File.Exists(BrokerPaths.StateFile))
                return false;
            var state = CliJson.Deserialize<BrokerState>(File.ReadAllText(BrokerPaths.StateFile));
            return state is { Port: > 0, NativeApprovalToken: { Length: > 0 } } &&
                   IsBrokerAlive(state.Port);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the owner-file-only native-host approval token so an in-process native host surface,
    /// such as the <c>maui devflow approve</c> command, can present it to the Inspector.
    /// <para>
    /// This is NOT an authorization boundary. Reading the token requires nothing more than local
    /// read access to the broker state file, which every process running as the same OS user
    /// already has — including an MCP agent. It only proves same-user locality, never that a human
    /// is at the keyboard.
    /// </para>
    /// </summary>
    internal static bool TryReadNativeApprovalToken([NotNullWhen(true)] out string? token)
    {
        token = null;
        try
        {
            if (!File.Exists(BrokerPaths.StateFile))
                return false;
            var state = CliJson.Deserialize<BrokerState>(File.ReadAllText(BrokerPaths.StateFile));
            if (state is not { Port: > 0, NativeApprovalToken: { Length: > 0 } approvalToken })
                return false;
            token = approvalToken;
            return true;
        }
        catch
        {
            token = null;
            return false;
        }
    }

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
            var agent = await ResolveAgentAsync(brokerPort, Path.GetFullPath(csproj));
            if (agent is not null) return agent.Port;
        }

        // Try auto-select (single agent)
        var autoAgent = await ResolveAgentAsync(brokerPort);
        if (autoAgent is not null) return autoAgent.Port;

        // No single match — return null so callers can handle multi-agent case
        return null;
    }

    /// <summary>
    /// High-level agent resolution: ensure broker running → resolve by project → auto-select → null.
    /// Returns the resolved broker registration when one can be selected unambiguously.
    /// </summary>
    public static async Task<AgentRegistration?> ResolveAgentForProjectAsync()
    {
        var brokerPort = ReadBrokerPort() ?? BrokerServer.DefaultPort;

        if (!await IsBrokerAliveAsync(brokerPort))
        {
            var started = await EnsureBrokerRunningAsync();
            if (started.HasValue)
                brokerPort = started.Value;
            else
                return null;
        }

        // Try project-specific resolution
        var csproj = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj").FirstOrDefault();
        if (csproj is not null)
        {
            var agent = await ResolveAgentAsync(brokerPort, Path.GetFullPath(csproj));
            if (agent is not null) return agent;
        }

        // Try auto-select (single agent)
        var autoAgent = await ResolveAgentAsync(brokerPort);
        if (autoAgent is not null) return autoAgent;

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
                using var process = Process.GetProcessById(state.Pid);
                var processPath = process.MainModule?.FileName;
                if (!process.HasExited &&
                    IsBrokerProcessIdentityMatch(
                        state,
                        process.StartTime.ToUniversalTime(),
                        processPath,
                        Environment.ProcessPath))
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

    internal static bool IsBrokerProcessIdentityMatch(
        BrokerState state,
        DateTime processStartedUtc,
        string? candidateExecutable,
        string? currentCliExecutable)
    {
        if (state.Pid <= 0 ||
            state.StartedAt == default ||
            string.IsNullOrWhiteSpace(candidateExecutable) ||
            string.IsNullOrWhiteSpace(currentCliExecutable))
        {
            return false;
        }

        var elapsed = (processStartedUtc - state.StartedAt.ToUniversalTime()).Duration();
        if (elapsed > TimeSpan.FromSeconds(30))
            return false;

        try
        {
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var candidatePath = Path.GetFullPath(candidateExecutable);
            var currentPath = Path.GetFullPath(currentCliExecutable);
            var candidateName = Path.GetFileName(candidatePath);
            if (candidateName.Equals("dotnet", comparison) ||
                candidateName.Equals("dotnet.exe", comparison))
            {
                // A generic dotnet host cannot be distinguished from an unrelated managed app
                // without inspecting its command line. Prefer leaving a hung broker behind over
                // terminating an arbitrary process after PID reuse.
                return false;
            }
            return string.Equals(candidatePath, currentPath, comparison);
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    internal static async Task<int?> StartBrokerAsync()
    {
        try
        {
            // Find the CLI executable path
            var exePath = Environment.ProcessPath;
            if (exePath == null)
            {
                Console.Error.WriteLine("[DevFlow Broker] Cannot resolve CLI executable path (Environment.ProcessPath is null)");
                return null;
            }

            string fileName;
            string arguments;

            // If running via `dotnet run` or `dotnet <dll>`, exePath is the dotnet host.
            // In that case, use `dotnet <entryDll> devflow broker start --foreground` instead.
            // Note: the `devflow` token is required because broker is a subcommand of devflow,
            // not a top-level CLI command.
            if (exePath.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase)
                || exePath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                var dllPath = ResolveManagedEntryAssemblyPath();
                if (string.IsNullOrEmpty(dllPath))
                {
                    Console.Error.WriteLine("[DevFlow Broker] Cannot resolve managed entry assembly path for daemon spawn");
                    return null;
                }
                fileName = exePath;
                arguments = $"\"{dllPath}\" devflow broker start --foreground";
            }
            else
            {
                fileName = exePath;
                arguments = "devflow broker start --foreground";
            }

            // The daemon must not inherit this invocation's stdout/stderr: a shell pipeline such as
            // `maui devflow flow run ... | Tee-Object` stays open for as long as any process holds
            // the pipe, so an inherited handle keeps the caller's pipeline blocked long after the
            // CLI has exited.
            var process = DetachedDaemonProcess.Start(fileName, arguments);
            if (process == null)
            {
                Console.Error.WriteLine("[DevFlow Broker] The daemon process could not be launched");
                return null;
            }

            try
            {
                // Poll until broker is ready
                var port = BrokerServer.DefaultPort;
                for (int i = 0; i < 25; i++) // 25 * 200ms = 5s
                {
                    await Task.Delay(200);

                    // Check if the child process has crashed during startup
                    if (process.HasExited)
                    {
                        ReportDaemonExit(process, "exited prematurely");
                        return null;
                    }

                    // Check if state file was written (may have a different port)
                    var statePort = ReadBrokerPort();
                    if (statePort.HasValue) port = statePort.Value;

                    if (await IsBrokerAliveAsync(port))
                        return port;
                }

                // Timeout — check if the child is still running or crashed
                if (process.HasExited)
                {
                    ReportDaemonExit(process, "exited before becoming ready");
                }
                else
                {
                    Console.Error.WriteLine($"[DevFlow Broker] Daemon process started (PID {process.ProcessId}) but TCP listener not reachable after 5s");
                }

                return null;
            }
            finally
            {
                process.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DevFlow Broker] Failed to start daemon: {ex.Message}");
            return null;
        }
    }

    private static void ReportDaemonExit(DetachedDaemonProcess process, string what)
    {
        var exitCode = process.ExitCode;
        Console.Error.WriteLine(exitCode.HasValue
            ? $"[DevFlow Broker] Daemon process {what} with code {exitCode.Value}"
            : $"[DevFlow Broker] Daemon process {what}");

        var stderrText = process.CapturedStandardError;
        Console.Error.WriteLine(string.IsNullOrWhiteSpace(stderrText)
            ? "[DevFlow Broker] Run 'maui devflow broker start --foreground' to see the daemon's own diagnostics"
            : $"[DevFlow Broker] stderr: {stderrText}");
    }

    private static string? ResolveManagedEntryAssemblyPath()
    {
        var assemblyName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
        if (string.IsNullOrEmpty(assemblyName))
            return null;

        var candidate = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Builds the shared guidance message shown when multiple agents are connected and no
    /// target was specified (issue #343). Used by both the CLI <c>--agent-port</c> guard and
    /// the MCP <c>McpAgentSession</c> so the two surfaces stay identical.
    /// </summary>
    /// <param name="agents">The connected agents to list (may be empty).</param>
    /// <param name="optionHint">
    /// How the caller specifies a target on this surface — <c>--agent-port</c> for the CLI,
    /// <c>agentPort</c> for MCP tools.
    /// </param>
    internal static string BuildMultiAgentTargetingMessage(AgentRegistration[] agents, string optionHint = "--agent-port")
    {
        var sb = new StringBuilder();
        sb.Append("Multiple MAUI DevFlow agents are connected and no target was specified. ");
        sb.Append($"Re-run with {optionHint} <port> to choose which app to target.");
        if (agents is { Length: > 0 })
        {
            foreach (var a in agents.OrderBy(a => a.Port))
                sb.Append($"{Environment.NewLine}  {optionHint} {a.Port}  {a.AppName} ({a.Platform} {a.Tfm})");
        }
        return sb.ToString();
    }
}
