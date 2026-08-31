using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core.Profiling;

public class ProfilerSessionInfo
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";
    [JsonPropertyName("startedAtUtc")]
    public DateTime StartedAtUtc { get; set; }
    [JsonPropertyName("sampleIntervalMs")]
    public int SampleIntervalMs { get; set; }
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
    [JsonIgnore]
    public string StopToken { get; set; } = "";
}

public class ProfilerSample
{
    [JsonPropertyName("tsUtc")]
    public DateTime TsUtc { get; set; }
    [JsonPropertyName("fps")]
    public double? Fps { get; set; }
    [JsonPropertyName("frameTimeMsP50")]
    public double? FrameTimeMsP50 { get; set; }
    [JsonPropertyName("frameTimeMsP95")]
    public double? FrameTimeMsP95 { get; set; }
    [JsonPropertyName("worstFrameTimeMs")]
    public double? WorstFrameTimeMs { get; set; }
    [JsonPropertyName("managedBytes")]
    public long ManagedBytes { get; set; }
    [JsonPropertyName("gc0")]
    public int Gc0 { get; set; }
    [JsonPropertyName("gc1")]
    public int Gc1 { get; set; }
    [JsonPropertyName("gc2")]
    public int Gc2 { get; set; }
    [JsonPropertyName("nativeMemoryBytes")]
    public long? NativeMemoryBytes { get; set; }
    [JsonPropertyName("nativeMemoryKind")]
    public string? NativeMemoryKind { get; set; }
    [JsonPropertyName("processMemoryBytes")]
    public long? ProcessMemoryBytes { get; set; }
    [JsonPropertyName("processMemoryKind")]
    public string? ProcessMemoryKind { get; set; }
    [JsonPropertyName("cpuPercent")]
    public double? CpuPercent { get; set; }
    [JsonPropertyName("threadCount")]
    public int? ThreadCount { get; set; }
    [JsonPropertyName("jankFrameCount")]
    public int JankFrameCount { get; set; }
    [JsonPropertyName("uiThreadStallCount")]
    public int UiThreadStallCount { get; set; }
    [JsonPropertyName("frameDataLossCount")]
    public int FrameDataLossCount { get; set; }
    [JsonPropertyName("frameSource")]
    public string FrameSource { get; set; } = "unavailable";
    [JsonPropertyName("frameQuality")]
    public string FrameQuality { get; set; } = "unavailable";
}

public class ProfilerMarker
{
    [JsonPropertyName("tsUtc")]
    public DateTime TsUtc { get; set; }
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("payloadJson")]
    public string? PayloadJson { get; set; }
}

public class ProfilerBatch
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";
    [JsonPropertyName("samples")]
    public List<ProfilerSample> Samples { get; set; } = new();
    [JsonPropertyName("markers")]
    public List<ProfilerMarker> Markers { get; set; } = new();
    [JsonPropertyName("spans")]
    public List<ProfilerSpan> Spans { get; set; } = new();
    [JsonPropertyName("sampleCursor")]
    public long SampleCursor { get; set; }
    [JsonPropertyName("markerCursor")]
    public long MarkerCursor { get; set; }
    [JsonPropertyName("spanCursor")]
    public long SpanCursor { get; set; }
    [JsonPropertyName("sampleMetadata")]
    public ProfilerStreamReadMetadata SampleMetadata { get; set; } = new();
    [JsonPropertyName("markerMetadata")]
    public ProfilerStreamReadMetadata MarkerMetadata { get; set; } = new();
    [JsonPropertyName("spanMetadata")]
    public ProfilerStreamReadMetadata SpanMetadata { get; set; } = new();
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
}

/// <summary>
/// Describes the retained range and overwrite loss for one profiler stream.
/// Existing cursor fields on <see cref="ProfilerBatch"/> remain the next cursors.
/// </summary>
public class ProfilerStreamReadMetadata
{
    [JsonPropertyName("oldestCursor")]
    public long OldestCursor { get; set; }
    [JsonPropertyName("latestCursor")]
    public long LatestCursor { get; set; }
    [JsonPropertyName("lostCount")]
    public long LostCount { get; set; }
    [JsonPropertyName("availableCount")]
    public int AvailableCount { get; set; }
}

public class ProfilerSpan
{
    [JsonPropertyName("spanId")]
    public string SpanId { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("parentSpanId")]
    public string? ParentSpanId { get; set; }
    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }
    [JsonPropertyName("startTsUtc")]
    public DateTime StartTsUtc { get; set; }
    [JsonPropertyName("endTsUtc")]
    public DateTime EndTsUtc { get; set; }
    [JsonPropertyName("durationMs")]
    public double DurationMs { get; set; }
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "ui.operation";
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("status")]
    public string Status { get; set; } = "ok";
    [JsonPropertyName("threadId")]
    public int? ThreadId { get; set; }
    [JsonPropertyName("screen")]
    public string? Screen { get; set; }
    [JsonPropertyName("elementPath")]
    public string? ElementPath { get; set; }
    [JsonPropertyName("tagsJson")]
    public string? TagsJson { get; set; }
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class ProfilerHotspot
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

public class PublishProfilerSpanRequest
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public string? Status { get; set; }
    public string? ParentSpanId { get; set; }
    public string? TraceId { get; set; }
    public DateTime? StartTsUtc { get; set; }
    public DateTime? EndTsUtc { get; set; }
    public int? ThreadId { get; set; }
    public string? Screen { get; set; }
    public string? ElementPath { get; set; }
    public string? TagsJson { get; set; }
    public string? Error { get; set; }
}

public class ProfilerCapabilities
{
    [JsonPropertyName("available")]
    public bool Available { get; set; }
    [JsonPropertyName("supportedInBuild")]
    public bool SupportedInBuild { get; set; }
    [JsonPropertyName("featureEnabled")]
    public bool FeatureEnabled { get; set; }
    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "unknown";
    [JsonPropertyName("managedMemorySupported")]
    public bool ManagedMemorySupported { get; set; }
    [JsonPropertyName("nativeMemorySupported")]
    public bool NativeMemorySupported { get; set; }
    [JsonPropertyName("processMemorySupported")]
    public bool ProcessMemorySupported { get; set; }
    [JsonPropertyName("gcSupported")]
    public bool GcSupported { get; set; }
    [JsonPropertyName("cpuPercentSupported")]
    public bool CpuPercentSupported { get; set; }
    [JsonPropertyName("fpsSupported")]
    public bool FpsSupported { get; set; }
    [JsonPropertyName("frameTimingsEstimated")]
    public bool FrameTimingsEstimated { get; set; }
    [JsonPropertyName("nativeFrameTimingsSupported")]
    public bool NativeFrameTimingsSupported { get; set; }
    [JsonPropertyName("jankEventsSupported")]
    public bool JankEventsSupported { get; set; }
    [JsonPropertyName("uiThreadStallSupported")]
    public bool UiThreadStallSupported { get; set; }
    [JsonPropertyName("threadCountSupported")]
    public bool ThreadCountSupported { get; set; }
}

public class StartProfilerRequest
{
    public int? SampleIntervalMs { get; set; }
}

public class PublishProfilerMarkerRequest
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? PayloadJson { get; set; }
}
