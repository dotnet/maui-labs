namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Configuration options for the Microsoft.Maui.DevFlow Agent.
/// </summary>
public class AgentOptions
{
    /// <summary>Default port when none is specified via code or MSBuild property.</summary>
    public const int DefaultPort = 9223;

    /// <summary>
    /// Port for the HTTP API server. Default: 9223.
    /// Override at build time with -p:MauiDevFlowPort=XXXX.
    /// </summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>
    /// Whether the agent is enabled. Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Build/runtime mode reported by the agent: debug, profile, or custom.</summary>
    public string Mode { get; set; } = "debug";

    /// <summary>
    /// When true, every mutating endpoint is rejected before lease coordination. Used by explicit
    /// optimized diagnostics builds. Default: false.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Maximum tree walk depth. 0 = unlimited. Default: 0.
    /// </summary>
    public int MaxTreeDepth { get; set; } = 0;

    /// <summary>
    /// Whether to capture ILogger output to rotating log files. Default: true.
    /// </summary>
    public bool EnableFileLogging { get; set; } = true;

    /// <summary>
    /// Whether to register the FileLogProvider as an ILoggerProvider so that
    /// ILogger output is written to the rotating log files. Default: true.
    /// Requires <see cref="EnableFileLogging"/> to be true.
    /// </summary>
    public bool CaptureILogger { get; set; } = true;

    /// <summary>
    /// Maximum size of each log file in bytes before rotation. Default: 1MB.
    /// </summary>
    public long MaxLogFileSize { get; set; } = 1_048_576;

    /// <summary>
    /// Maximum number of rotated log files to keep. Default: 5.
    /// </summary>
    public int MaxLogFiles { get; set; } = 5;

    /// <summary>
    /// Whether to capture Console.Out and Console.Error output into the file log pipeline.
    /// Output is tee'd — original streams still receive everything. Default: true.
    /// Requires <see cref="EnableFileLogging"/> to be true.
    /// </summary>
    public bool CaptureConsole { get; set; } = true;

    /// <summary>
    /// Whether to capture Trace/Debug output into the file log pipeline. Default: true.
    /// Requires <see cref="EnableFileLogging"/> to be true.
    /// </summary>
    public bool CaptureTrace { get; set; } = true;

    /// <summary>
    /// Whether to intercept HttpClient requests for network monitoring. Default: true.
    /// When enabled, all IHttpClientFactory-created HttpClients are automatically monitored.
    /// </summary>
    public bool EnableNetworkMonitoring { get; set; } = true;

    /// <summary>
    /// Maximum size of request/response bodies to capture, in bytes. Default: 256KB.
    /// Bodies larger than this are truncated. Set to 0 to disable body capture.
    /// </summary>
    public int MaxNetworkBodySize { get; set; } = 256 * 1024;

    /// <summary>
    /// Maximum number of network requests to keep in the ring buffer. Default: 500.
    /// </summary>
    public int MaxNetworkBufferSize { get; set; } = 500;

    /// <summary>
    /// Enables runtime profiling endpoints and sampling. Default: false.
    /// </summary>
    public bool EnableProfiler { get; set; } = false;

    /// <summary>
    /// Default profiler sampling interval in milliseconds. Default: 500ms.
    /// </summary>
    public int ProfilerSampleIntervalMs { get; set; } = 500;

    /// <summary>
    /// Maximum number of profiler samples to keep in memory. Default: 20,000.
    /// Uses overwrite-on-full ring buffer behavior.
    /// </summary>
    public int MaxProfilerSamples { get; set; } = 20_000;

    /// <summary>
    /// Maximum number of profiler markers to keep in memory. Default: 20,000.
    /// Uses overwrite-on-full ring buffer behavior.
    /// </summary>
    public int MaxProfilerMarkers { get; set; } = 20_000;

    /// <summary>
    /// Maximum number of profiler spans to keep in memory. Default: 20,000.
    /// Uses overwrite-on-full ring buffer behavior.
    /// </summary>
    public int MaxProfilerSpans { get; set; } = 20_000;

    /// <summary>
    /// Maximum pending UI events retained for each WebSocket subscription. When full, the oldest
    /// events are discarded and the subscriber receives a loss notification. Default: 256.
    /// </summary>
    public int MaxUiEventSubscriptionQueueSize { get; set; } = 256;

    /// <summary>
    /// Enables high-level MAUI UI correlation hooks (navigation/page/scroll markers).
    /// Default: true.
    /// </summary>
    public bool EnableHighLevelUiHooks { get; set; } = true;

    /// <summary>
    /// Enables detailed per-control MAUI hooks (button/entry/toggle/picker/tap).
    /// Default: false to avoid broad attachment overhead.
    /// </summary>
    public bool EnableDetailedUiHooks { get; set; } = false;

    /// <summary>
    /// Whether DevFlow may use runtime reflection to resolve BindableProperty metadata for
    /// application-defined controls. Built-in MAUI properties use a trim-safe registry first.
    /// Disable this in optimized diagnostic builds. Default: true.
    /// </summary>
    public bool AllowPropertyReflection { get; set; } = true;

    /// <summary>
    /// Enables the MAUI diagnostics feature switch when the DevFlow agent is registered.
    /// Defaults to true for the explicitly opted-in development agent.
    /// </summary>
    public bool EnableMauiDiagnostics { get; set; } = true;

    /// <summary>
    /// Captures bounded, metadata-only binding failures for the Problems surface. Default: true.
    /// </summary>
    public bool EnableBindingProblems { get; set; } = true;

    /// <summary>
    /// Maximum number of deduplicated diagnostic problems retained in memory. Default: 256.
    /// </summary>
    public int MaxDiagnosticProblems { get; set; } = 256;

    /// <summary>
    /// Whether mutating HTTP endpoints require a valid DevFlow mutation lease.
    /// The lease is broker-authoritative when connected and falls back to the local agent when
    /// no broker is available. Default: true.
    /// </summary>
    public bool RequireMutationLease { get; set; } = true;

    /// <summary>
    /// How long a mutation lease remains active without a successful mutation or heartbeat.
    /// Default: 10 seconds.
    /// </summary>
    public int MutationLeaseTimeoutMs { get; set; } = 10_000;

    /// <summary>
    /// Maximum broker-owned workflow command ledgers that may be active in this app process.
    /// Active ledgers are never evicted to make room for a new run. Default: 8.
    /// </summary>
    public int MaxActiveWorkflowRunLedgers { get; set; } = 8;

    /// <summary>
    /// Maximum fenced mutating commands accepted by one workflow run. Default: 2000.
    /// </summary>
    public int MaxWorkflowCommandsPerRun { get; set; } = 2_000;

    /// <summary>
    /// Maximum terminal workflow ledgers retained for diagnostics. Default: 32.
    /// </summary>
    public int MaxRetainedWorkflowRunLedgers { get; set; } = 32;

    /// <summary>
    /// Number of seconds terminal workflow ledgers remain available for diagnostics. Default: 900.
    /// </summary>
    public int WorkflowRunLedgerRetentionSeconds { get; set; } = 15 * 60;

    /// <summary>
    /// Maximum response bytes retained as a workflow command receipt. Responses over this limit
    /// are replaced by an explicit failure receipt rather than silently truncated. Default: 64KB.
    /// </summary>
    public int MaxWorkflowCommandResponseBytes { get; set; } = 64 * 1024;

    internal void ApplyProfileDefaults()
    {
        Mode = "profile";
        ReadOnly = true;
        EnableProfiler = true;
        EnableFileLogging = false;
        CaptureILogger = false;
        CaptureConsole = false;
        CaptureTrace = false;
        EnableNetworkMonitoring = false;
        MaxNetworkBodySize = 0;
        EnableMauiDiagnostics = false;
        EnableBindingProblems = false;
        AllowPropertyReflection = false;
        EnableHighLevelUiHooks = false;
        EnableDetailedUiHooks = false;
    }

    internal void ApplyBuildMetadata(string? enabledMetadata, string? buildMode)
    {
        if (string.Equals(enabledMetadata, bool.FalseString, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "DevFlow Agent registration is disabled for this build. Remove AddMauiDevFlowAgent() "
                + "or set MauiDevFlowEnabled=true in a Debug build. For optimized diagnostics, also set MauiDevFlowProfileMode=true.");
        }

        if (string.Equals(buildMode, "profile", StringComparison.OrdinalIgnoreCase))
            ApplyProfileDefaults();
        else if (!string.IsNullOrWhiteSpace(buildMode))
            Mode = buildMode;
    }

    internal void ValidateForRegistration()
    {
        if (Port is < 1 or > 65_535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Port),
                Port,
                "DevFlow Agent port must be between 1 and 65535.");
        }
    }

    internal void ApplyPortMetadata(int? metadataPort)
    {
        if (Port == DefaultPort && metadataPort.HasValue)
            Port = metadataPort.Value;
    }

    internal static int? ParsePortMetadata(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!int.TryParse(value, out var port) || port is < 1 or > 65_535)
        {
            throw new InvalidOperationException(
                "Microsoft.Maui.DevFlowPort assembly metadata must be an integer between 1 and 65535.");
        }
        return port;
    }

    /// <summary>
    /// Custom routes registered under /api/v1/ext/{namespace}/...
    /// </summary>
    public IList<AgentExtension> Extensions { get; } = new List<AgentExtension>();

    public AgentExtension RegisterExtension(
        string @namespace,
        string description,
        int version = 1,
        IEnumerable<string>? features = null)
        => RegisterExtension(@namespace, description, $"{version}.0.0", features);

    public AgentExtension RegisterExtension(
        string @namespace,
        string description,
        string version,
        IEnumerable<string>? features = null)
    {
        var extension = new AgentExtension(@namespace, description, version, features);
        Extensions.Add(extension);
        return extension;
    }
}
