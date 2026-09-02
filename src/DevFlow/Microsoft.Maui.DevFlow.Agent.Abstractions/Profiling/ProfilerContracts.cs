namespace Microsoft.Maui.DevFlow.Agent.Core.Profiling;

public class ProfilerSessionInfo
{
    public string SessionId { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public int SampleIntervalMs { get; set; }
    public bool IsActive { get; set; }
}

public class ProfilerSample
{
    public DateTime TsUtc { get; set; }
    public double? Fps { get; set; }
    public double? FrameTimeMsP50 { get; set; }
    public double? FrameTimeMsP95 { get; set; }
    public double? WorstFrameTimeMs { get; set; }
    public long ManagedBytes { get; set; }
    public int Gc0 { get; set; }
    public int Gc1 { get; set; }
    public int Gc2 { get; set; }
    public long? NativeMemoryBytes { get; set; }
    public string? NativeMemoryKind { get; set; }
    public double? CpuPercent { get; set; }
    public int? ThreadCount { get; set; }
    public int JankFrameCount { get; set; }
    public int UiThreadStallCount { get; set; }
    public string FrameSource { get; set; } = "managed.estimated";
    public string FrameQuality { get; set; } = "estimated";
}

public class ProfilerMarker
{
    public DateTime TsUtc { get; set; }
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string? PayloadJson { get; set; }
}

public class ProfilerBatch
{
    public string SessionId { get; set; } = "";
    public List<ProfilerSample> Samples { get; set; } = new();
    public List<ProfilerMarker> Markers { get; set; } = new();
    public List<ProfilerSpan> Spans { get; set; } = new();
    public long SampleCursor { get; set; }
    public long MarkerCursor { get; set; }
    public long SpanCursor { get; set; }
    public bool IsActive { get; set; }
}

public class ProfilerSpan
{
    public string SpanId { get; set; } = Guid.NewGuid().ToString("N");
    public string? ParentSpanId { get; set; }
    public string? TraceId { get; set; }
    public DateTime StartTsUtc { get; set; }
    public DateTime EndTsUtc { get; set; }
    public double DurationMs { get; set; }
    public string Kind { get; set; } = "ui.operation";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "ok";
    public int? ThreadId { get; set; }
    public string? Screen { get; set; }
    public string? ElementPath { get; set; }
    public string? TagsJson { get; set; }
    public string? Error { get; set; }
}

public class ProfilerHotspot
{
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Screen { get; set; }
    public int Count { get; set; }
    public int ErrorCount { get; set; }
    public double AvgDurationMs { get; set; }
    public double P95DurationMs { get; set; }
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
    public bool SupportedInBuild { get; set; }
    public bool FeatureEnabled { get; set; }
    public string Platform { get; set; } = "unknown";
    public bool ManagedMemorySupported { get; set; }
    public bool NativeMemorySupported { get; set; }
    public bool GcSupported { get; set; }
    public bool CpuPercentSupported { get; set; }
    public bool FpsSupported { get; set; }
    public bool FrameTimingsEstimated { get; set; }
    public bool NativeFrameTimingsSupported { get; set; }
    public bool JankEventsSupported { get; set; }
    public bool UiThreadStallSupported { get; set; }
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
