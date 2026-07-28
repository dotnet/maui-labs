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
