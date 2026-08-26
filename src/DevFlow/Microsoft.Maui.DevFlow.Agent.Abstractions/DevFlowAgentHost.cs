using System.Reflection;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Shared host bootstrap logic for reading DevFlow build metadata and registering agents with the broker.
/// </summary>
public static class DevFlowAgentHost
{
    private const string PortMetadataKey = "Microsoft.Maui.DevFlowPort";
    private const string ProjectMetadataKey = "Microsoft.Maui.DevFlowProject";
    private const string TfmMetadataKey = "Microsoft.Maui.DevFlowTfm";
    private const string SessionIdMetadataKey = "Microsoft.Maui.DevFlowSessionId";

    /// <summary>
    /// Reads DevFlow assembly metadata from the host app assembly.
    /// </summary>
    public static DevFlowAgentAssemblyMetadata ReadAssemblyMetadata()
    {
        var portValue = ReadAssemblyMetadata(PortMetadataKey);
        return new DevFlowAgentAssemblyMetadata(
            Port: portValue != null && int.TryParse(portValue, out var port) ? port : null,
            Project: ReadAssemblyMetadata(ProjectMetadataKey),
            Tfm: ReadAssemblyMetadata(TfmMetadataKey),
            SessionId: ReadAssemblyMetadata(SessionIdMetadataKey));
    }

    /// <summary>
    /// Prepares broker registration and applies broker/metadata port selection to <paramref name="options"/>.
    /// </summary>
    public static DevFlowAgentHostContext Configure(
        AgentOptions options,
        Func<(string Platform, string AppName)>? hostIdentityProvider = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        log ??= static message => Console.WriteLine(message);

        var metadata = ReadAssemblyMetadata();
        var project = metadata.Project ?? "unknown";
        var tfm = metadata.Tfm ?? "unknown";
        var sessionId = metadata.SessionId;

        // Always register with the broker for discoverability. When a custom port is set we tell the
        // broker our port so it uses that instead of assigning from the pool; the agent stays visible
        // to `maui devflow list` regardless of how the port was configured.
        BrokerRegistration? brokerRegistration = null;
        var hasCustomPort = options.Port != AgentOptions.DefaultPort;
        try
        {
            var (platform, appName) = hostIdentityProvider?.Invoke() ?? GetDefaultHostIdentity();
            brokerRegistration = new BrokerRegistration(project, tfm, platform, appName, sessionId);

            if (hasCustomPort)
                brokerRegistration.CurrentPort = options.Port;

            // Task.Run avoids a deadlock: TryRegisterAsync awaits internally, and callers typically
            // bootstrap on the main thread, whose SynchronizationContext would deadlock if we blocked
            // on it directly. Do not "simplify" this to a direct GetAwaiter().GetResult().
            var assignedPort = Task.Run(() => brokerRegistration.TryRegisterAsync(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult();
            if (assignedPort.HasValue)
            {
                options.Port = assignedPort.Value;
                log($"[Microsoft.Maui.DevFlow] Broker assigned port {assignedPort.Value}");
            }
        }
        catch (Exception ex)
        {
            log($"[Microsoft.Maui.DevFlow] Broker registration failed: {ex.Message}");
            brokerRegistration?.Dispose();
            brokerRegistration = null;
        }

        // Fall back to the build-injected metadata port only when the broker didn't assign one.
        if (!hasCustomPort && brokerRegistration?.AssignedPort == null && metadata.Port.HasValue)
            options.Port = metadata.Port.Value;

        return new DevFlowAgentHostContext(metadata, brokerRegistration);
    }

    /// <summary>
    /// Reads one metadata value from the entry/app assembly, falling back to loaded assemblies on platforms
    /// where <see cref="Assembly.GetEntryAssembly"/> is unavailable.
    /// </summary>
    public static string? ReadAssemblyMetadata(string key)
    {
        try
        {
            var entry = Assembly.GetEntryAssembly();
            if (entry != null)
            {
                var value = FindMetadataInAssembly(entry, key);
                if (value != null)
                    return value;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                    continue;

                var value = FindMetadataInAssembly(assembly, key);
                if (value != null)
                    return value;
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? FindMetadataInAssembly(Assembly assembly, string key)
    {
        try
        {
            var attributes = assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false);
            foreach (AssemblyMetadataAttribute attribute in attributes)
            {
                if (attribute.Key == key)
                    return attribute.Value;
            }
        }
        catch
        {
        }

        return null;
    }

    private static (string Platform, string AppName) GetDefaultHostIdentity()
        => (DetectPlatformName(), Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown");

    private static string DetectPlatformName() => DevFlowRuntimePlatform.DetectName();
}

/// <summary>
/// DevFlow build metadata injected into the host app assembly.
/// </summary>
public sealed record DevFlowAgentAssemblyMetadata(
    int? Port,
    string? Project,
    string? Tfm,
    string? SessionId);

/// <summary>
/// Result of shared DevFlow host bootstrap work.
/// </summary>
public sealed class DevFlowAgentHostContext
{
    internal DevFlowAgentHostContext(DevFlowAgentAssemblyMetadata metadata, BrokerRegistration? brokerRegistration)
    {
        Metadata = metadata;
        BrokerRegistration = brokerRegistration;
    }

    /// <summary>Metadata read from the host app assembly.</summary>
    public DevFlowAgentAssemblyMetadata Metadata { get; }

    /// <summary>Broker registration to attach to the agent service, or <c>null</c> when registration failed.</summary>
    public BrokerRegistration? BrokerRegistration { get; }

    /// <summary>
    /// Applies the shared session identity and broker registration to a service after its final port is known.
    /// </summary>
    public void AttachTo(DevFlowAgentService service, AgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(options);

        service.SetSessionId(Metadata.SessionId);
        if (BrokerRegistration != null)
        {
            // Record the port we actually landed on so late reconnections (broker started after the
            // app) register the correct port rather than the pre-assignment default.
            BrokerRegistration.CurrentPort = options.Port;
            service.SetBrokerRegistration(BrokerRegistration);
        }
    }
}
