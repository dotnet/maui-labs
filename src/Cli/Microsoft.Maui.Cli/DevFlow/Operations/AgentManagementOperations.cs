using Microsoft.Maui.Cli.DevFlow.Android;
using Microsoft.Maui.Cli.DevFlow.Broker;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed record AndroidForwardingCheck(AndroidDevFlowForwardingReport? Report, string? Error = null);

internal sealed record AgentDiscoveryResult(int? BrokerPort, AgentRegistration[]? Agents,
    string[] Projects, AndroidDevFlowForwardingReport? AndroidForwarding = null);

internal sealed class AgentManagementOperations(
    Func<Task<int?>> resolveRunningBroker,
    Func<int, Task<AgentRegistration[]?>> listAgents,
    Func<AndroidDevFlowPortForwarder> createForwarder,
    Func<bool> isAdbAvailable,
    Func<Task<int?>>? ensureBrokerRunning = null)
{
    internal async Task<AgentDiscoveryResult> ListAsync(string? deviceId,
        Action<AndroidForwardingCheck> forwardingChecked, CancellationToken cancellationToken)
    {
        var port = await (ensureBrokerRunning ?? BrokerClient.EnsureBrokerRunningAsync)();
        if (port is null)
            return new(null, null, []);

        var agents = await listAgents(port.Value);
        if (agents is null || agents.Length == 0)
            return new(port, agents, ScanForProjects());

        var check = await EnsureForwardingForAgentsAsync(agents, deviceId, true, cancellationToken, port);
        forwardingChecked(check);

        return new(port, agents, [], check.Report);
    }

    internal async Task<AgentDiscoveryResult> DiagnoseAsync(string? deviceId, CancellationToken cancellationToken)
    {
        var port = await resolveRunningBroker();
        var agents = port.HasValue ? await listAgents(port.Value) : null;
        var ports = agents?
            .Where(IsAndroidAgent)
            .Select(static a => a.Port)
            .Distinct()
            .ToArray() ?? [];

        AndroidForwardingCheck? check = null;
        if (ports.Length > 0 || !string.IsNullOrWhiteSpace(deviceId))
            check = await EnsureForwardingForPortsAsync(ports, true, deviceId, false, cancellationToken, port);

        return new(port, agents, ScanForProjects(), check?.Report);
    }

    internal async Task<(bool BrokerAvailable, AgentRegistration? Agent)> WaitAsync(
        int timeoutSeconds, string? projectFilter, string? platformFilter, string? deviceId,
        Action<AndroidForwardingCheck> forwardingChecked, CancellationToken cancellationToken)
    {
        var port = await (ensureBrokerRunning ?? BrokerClient.EnsureBrokerRunningAsync)();
        if (port is null)
            return (false, null);

        if (string.IsNullOrWhiteSpace(platformFilter) || platformFilter.Contains("Android", StringComparison.OrdinalIgnoreCase))
            forwardingChecked(await EnsureForwardingForPortsAsync([], true, deviceId, true, cancellationToken, port));

        var project = projectFilter is null ? null : Path.GetFullPath(projectFilter);

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        AgentRegistration? matched = null;

        while (DateTime.UtcNow < deadline)
        {
            var agents = await listAgents(port.Value);
            if (agents is { Length: > 0 })
            {
                matched = FindMatchingAgent(agents, project, platformFilter);
                if (matched != null)
                    break;
            }

            await Task.Delay(500, cancellationToken);
        }

        if (matched is not null && IsAndroidAgent(matched))
            forwardingChecked(await EnsureForwardingForAgentsAsync([matched], deviceId, true, cancellationToken, port));

        return (true, matched);
    }

    internal Task<AndroidForwardingCheck> EnsureForwardingForAgentsAsync(IEnumerable<AgentRegistration> agents,
        string? deviceId, bool repair, CancellationToken cancellationToken, int? brokerPort = null)
    {
        var ports = agents
            .Where(IsAndroidAgent)
            .Select(static a => a.Port)
            .Distinct()
            .ToArray();

        if (ports.Length == 0)
            return Task.FromResult(new AndroidForwardingCheck(null));

        return EnsureForwardingForPortsAsync(ports, true, deviceId, repair, cancellationToken, brokerPort);
    }

    internal async Task<AndroidForwardingCheck> EnsureForwardingForPortsAsync(int[] ports,
        bool ensureBrokerReverse, string? deviceId, bool repair, CancellationToken cancellationToken, int? brokerPort = null)
    {
        if (!isAdbAvailable())
            return new(null);

        try
        {
            return new(await createForwarder().EnsureAsync(new AndroidDevFlowForwardingRequest
            {
                AgentPorts = ports,
                EnsureBrokerReverse = ensureBrokerReverse,
                BrokerPort = brokerPort ?? BrokerClient.ReadBrokerPortPublic() ?? BrokerServer.DefaultPort,
                Repair = repair,
                DeviceSerial = deviceId
            }, cancellationToken));
        }
        catch (Exception ex)
        {
            return new(null, ex.Message);
        }
    }

    internal static bool IsAndroidAgent(AgentRegistration agent) =>
        agent.Platform.Contains("Android", StringComparison.OrdinalIgnoreCase)
            || agent.Tfm.Contains("-android", StringComparison.OrdinalIgnoreCase);

    internal static AgentRegistration? FindMatchingAgent(AgentRegistration[] agents, string? project, string? platform)
    {
        foreach (var agent in agents)
        {
            if (project != null && !string.Equals(agent.Project, project, StringComparison.OrdinalIgnoreCase))
                continue;

            if (platform != null && !agent.Platform.Contains(platform, StringComparison.OrdinalIgnoreCase))
                continue;

            return agent;
        }

        return null;
    }

    private static readonly HashSet<string> ScanExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".idea", "packages"
    };

    internal static string[] ScanForProjects()
    {
        var cwd = Directory.GetCurrentDirectory();
        var projects = new List<string>();

        try
        {
            var pending = new Queue<string>();
            pending.Enqueue(cwd);

            while (pending.Count > 0)
            {
                var dir = pending.Dequeue();

                try
                {
                    foreach (var project in Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var content = File.ReadAllText(project);
                            if (content.Contains("Redth.MauiDevFlow.Agent") ||
                                content.Contains("Microsoft.Maui.DevFlow.Agent"))
                            {
                                projects.Add(Path.GetRelativePath(cwd, project));
                            }
                        }
                        catch
                        {
                            // Skip files we can't read.
                        }
                    }
                }
                catch
                {
                    // Skip directories we can't enumerate.
                    continue;
                }

                try
                {
                    foreach (var subDir in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                    {
                        var name = Path.GetFileName(subDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (!string.IsNullOrEmpty(name) && !ScanExcludedDirs.Contains(name))
                            pending.Enqueue(subDir);
                    }
                }
                catch
                {
                    // Skip directories we can't enumerate.
                }
            }
        }
        catch
        {
            // Preserve the best-effort project scan.
        }

        projects.Sort(StringComparer.OrdinalIgnoreCase);
        return projects.ToArray();
    }
}
