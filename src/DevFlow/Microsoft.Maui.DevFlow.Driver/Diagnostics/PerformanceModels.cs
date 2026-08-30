using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// A task-focused performance triage summary built from the agent's existing profiler streams.
///
/// This is deliberately <em>not</em> a profiler. It answers "did this interaction allocate, stall,
/// or churn?" from bounded sampling that the app is already doing, and it is explicit about
/// everything it could not measure. When a number would have to be synthesised to exist, it is
/// omitted and the reason is recorded in <see cref="Capability"/>.
/// </summary>
public class PerformanceSummary
{
    /// <summary>Contract version for this summary shape.</summary>
    public const string CurrentSchemaVersion = "1.0";

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("generatedUtc")]
    public string GeneratedUtc { get; set; } = "";

    [JsonPropertyName("session")]
    public PerformanceSessionInfo Session { get; set; } = new();

    [JsonPropertyName("memory")]
    public PerformanceMemory Memory { get; set; } = new();

    [JsonPropertyName("gc")]
    public PerformanceGc Gc { get; set; } = new();

    [JsonPropertyName("cpu")]
    public PerformanceCpu Cpu { get; set; } = new();

    [JsonPropertyName("threads")]
    public PerformanceThreads Threads { get; set; } = new();

    [JsonPropertyName("frames")]
    public PerformanceFrames Frames { get; set; } = new();

    [JsonPropertyName("hotspots")]
    public List<PerformanceHotspot> Hotspots { get; set; } = [];

    [JsonPropertyName("markers")]
    public PerformanceMarkerCounts Markers { get; set; } = new();

    /// <summary>Ring-buffer overwrite accounting. Non-zero loss invalidates start/peak deltas.</summary>
    [JsonPropertyName("loss")]
    public PerformanceLoss Loss { get; set; } = new();

    /// <summary>What the agent can measure, and what perturbs the numbers.</summary>
    [JsonPropertyName("capability")]
    public PerformanceCapability Capability { get; set; } = new();

    /// <summary>Prominent, human-readable caveats. Never empty in normal Debug builds.</summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];
}

public class PerformanceSessionInfo
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("startedUtc")]
    public string? StartedUtc { get; set; }

    [JsonPropertyName("sampleIntervalMs")]
    public int SampleIntervalMs { get; set; }

    [JsonPropertyName("sampleCount")]
    public int SampleCount { get; set; }

    /// <summary>Span between the first and last retained sample, not wall-clock session length.</summary>
    [JsonPropertyName("sampledDurationMs")]
    public double SampledDurationMs { get; set; }

    [JsonPropertyName("stopToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StopToken { get; set; }
}

public class PerformanceMemory
{
    [JsonPropertyName("managedStartBytes")]
    public long? ManagedStartBytes { get; set; }

    [JsonPropertyName("managedEndBytes")]
    public long? ManagedEndBytes { get; set; }

    [JsonPropertyName("managedPeakBytes")]
    public long? ManagedPeakBytes { get; set; }

    [JsonPropertyName("managedDeltaBytes")]
    public long? ManagedDeltaBytes { get; set; }

    [JsonPropertyName("nativeSupported")]
    public bool NativeSupported { get; set; }

    [JsonPropertyName("nativeKind")]
    public string? NativeKind { get; set; }

    [JsonPropertyName("nativeStartBytes")]
    public long? NativeStartBytes { get; set; }

    [JsonPropertyName("nativeEndBytes")]
    public long? NativeEndBytes { get; set; }

    [JsonPropertyName("nativePeakBytes")]
    public long? NativePeakBytes { get; set; }

    [JsonPropertyName("nativeDeltaBytes")]
    public long? NativeDeltaBytes { get; set; }

    [JsonPropertyName("nativeKindsMixed")]
    public bool NativeKindsMixed { get; set; }

    [JsonPropertyName("nativeUnsupportedReason")]
    public string? NativeUnsupportedReason { get; set; }

    [JsonPropertyName("processSupported")]
    public bool ProcessSupported { get; set; }

    [JsonPropertyName("processKind")]
    public string? ProcessKind { get; set; }

    [JsonPropertyName("processStartBytes")]
    public long? ProcessStartBytes { get; set; }

    [JsonPropertyName("processEndBytes")]
    public long? ProcessEndBytes { get; set; }

    [JsonPropertyName("processPeakBytes")]
    public long? ProcessPeakBytes { get; set; }

    [JsonPropertyName("processDeltaBytes")]
    public long? ProcessDeltaBytes { get; set; }

    [JsonPropertyName("processKindsMixed")]
    public bool ProcessKindsMixed { get; set; }

    [JsonPropertyName("processUnsupportedReason")]
    public string? ProcessUnsupportedReason { get; set; }
}

public class PerformanceGc
{
    [JsonPropertyName("supported")]
    public bool Supported { get; set; }

    [JsonPropertyName("gen0Delta")]
    public int? Gen0Delta { get; set; }

    [JsonPropertyName("gen1Delta")]
    public int? Gen1Delta { get; set; }

    [JsonPropertyName("gen2Delta")]
    public int? Gen2Delta { get; set; }
}

public class PerformanceCpu
{
    [JsonPropertyName("supported")]
    public bool Supported { get; set; }

    [JsonPropertyName("averagePercent")]
    public double? AveragePercent { get; set; }

    [JsonPropertyName("peakPercent")]
    public double? PeakPercent { get; set; }
}

public class PerformanceThreads
{
    [JsonPropertyName("supported")]
    public bool Supported { get; set; }

    [JsonPropertyName("peakCount")]
    public int? PeakCount { get; set; }
}

/// <summary>
/// Frame statistics. Every value stays null unless the agent reported authoritative
/// <em>native</em> frame timings for the samples in the window — an estimated or unavailable
/// frame source never produces an FPS number here.
/// </summary>
public class PerformanceFrames
{
    [JsonPropertyName("supported")]
    public bool Supported { get; set; }

    /// <summary>Why frame statistics are missing, when they are.</summary>
    [JsonPropertyName("unsupportedReason")]
    public string? UnsupportedReason { get; set; }

    /// <summary>
    /// Frame provider reported by the agent, for example
    /// <c>native.android.framemetrics</c> or <c>native.apple.cadisplaylink</c>.
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("quality")]
    public string? Quality { get; set; }

    [JsonPropertyName("averageFps")]
    public double? AverageFps { get; set; }

    [JsonPropertyName("minimumFps")]
    public double? MinimumFps { get; set; }

    [JsonPropertyName("frameTimeMsP95")]
    public double? FrameTimeMsP95 { get; set; }

    [JsonPropertyName("worstFrameTimeMs")]
    public double? WorstFrameTimeMs { get; set; }

    [JsonPropertyName("jankFrameCount")]
    public int? JankFrameCount { get; set; }

    [JsonPropertyName("uiThreadStallCount")]
    public int? UiThreadStallCount { get; set; }
}

public class PerformanceHotspot
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("screen")]
    public string? Screen { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("avgDurationMs")]
    public double AvgDurationMs { get; set; }

    [JsonPropertyName("p95DurationMs")]
    public double P95DurationMs { get; set; }

    [JsonPropertyName("maxDurationMs")]
    public double MaxDurationMs { get; set; }
}

public class PerformanceMarkerCounts
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("ui")]
    public int Ui { get; set; }

    [JsonPropertyName("network")]
    public int Network { get; set; }

    [JsonPropertyName("navigation")]
    public int Navigation { get; set; }

    [JsonPropertyName("other")]
    public int Other { get; set; }

    [JsonPropertyName("spanCount")]
    public int SpanCount { get; set; }
}

public class PerformanceLoss
{
    [JsonPropertyName("anyLoss")]
    public bool AnyLoss { get; set; }

    [JsonPropertyName("samplesLost")]
    public long SamplesLost { get; set; }

    [JsonPropertyName("markersLost")]
    public long MarkersLost { get; set; }

    [JsonPropertyName("spansLost")]
    public long SpansLost { get; set; }

    [JsonPropertyName("nativeFrameEventsLost")]
    public long NativeFrameEventsLost { get; set; }

    /// <summary>True when the requested summary limit omitted retained data.</summary>
    [JsonPropertyName("anyOmitted")]
    public bool AnyOmitted { get; set; }

    [JsonPropertyName("samplesOmitted")]
    public int SamplesOmitted { get; set; }

    [JsonPropertyName("markersOmitted")]
    public int MarkersOmitted { get; set; }

    [JsonPropertyName("spansOmitted")]
    public int SpansOmitted { get; set; }

    [JsonPropertyName("oldestSampleCursor")]
    public long OldestSampleCursor { get; set; }

    [JsonPropertyName("latestSampleCursor")]
    public long LatestSampleCursor { get; set; }
}

/// <summary>What the agent supports, and what taints the numbers in this run.</summary>
public class PerformanceCapability
{
    [JsonPropertyName("available")]
    public bool Available { get; set; }

    [JsonPropertyName("featureEnabled")]
    public bool FeatureEnabled { get; set; }

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "unknown";

    /// <summary>Agent build mode: <c>debug</c>, <c>profile</c>, or a custom label.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "unknown";

    [JsonPropertyName("readOnly")]
    public bool ReadOnly { get; set; }

    /// <summary>True in a low-perturbation, explicitly profile-mode agent build.</summary>
    [JsonPropertyName("lowPerturbation")]
    public bool LowPerturbation { get; set; }

    [JsonPropertyName("managedMemorySupported")]
    public bool ManagedMemorySupported { get; set; }

    [JsonPropertyName("nativeMemorySupported")]
    public bool NativeMemorySupported { get; set; }

    [JsonPropertyName("processMemorySupported")]
    public bool ProcessMemorySupported { get; set; }

    [JsonPropertyName("gcSupported")]
    public bool GcSupported { get; set; }

    [JsonPropertyName("cpuSupported")]
    public bool CpuSupported { get; set; }

    [JsonPropertyName("threadCountSupported")]
    public bool ThreadCountSupported { get; set; }

    [JsonPropertyName("nativeFrameTimingsSupported")]
    public bool NativeFrameTimingsSupported { get; set; }

    [JsonPropertyName("frameTimingsEstimated")]
    public bool FrameTimingsEstimated { get; set; }

    [JsonPropertyName("jankEventsSupported")]
    public bool JankEventsSupported { get; set; }

    [JsonPropertyName("uiThreadStallSupported")]
    public bool UiThreadStallSupported { get; set; }

    [JsonPropertyName("limitations")]
    public List<string> Limitations { get; set; } = [];
}
