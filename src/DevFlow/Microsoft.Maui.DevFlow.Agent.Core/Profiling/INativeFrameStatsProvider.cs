namespace Microsoft.Maui.DevFlow.Agent.Core.Profiling;

public sealed class NativeFrameStatsSnapshot
{
    public DateTime TsUtc { get; set; }
    public string Source { get; set; } = "native.unknown";
    public double? Fps { get; set; }
    public double? FrameTimeMsP50 { get; set; }
    public double? FrameTimeMsP95 { get; set; }
    public double? WorstFrameTimeMs { get; set; }
    public int JankFrameCount { get; set; }
    public int UiThreadStallCount { get; set; }
    public int FrameDataLossCount { get; set; }
    public long? NativeMemoryBytes { get; set; }
    public string? NativeMemoryKind { get; set; }
    public long? ProcessMemoryBytes { get; set; }
    public string? ProcessMemoryKind { get; set; }
}

public interface INativeFrameStatsProvider : IDisposable
{
    bool IsSupported { get; }
    bool ProvidesExactFrameTimings { get; }
    string Source { get; }
    bool ProvidesNativeMemory => false;
    bool ProvidesProcessMemory => false;
    void Start();
    void Stop();
    bool TryCollect(out NativeFrameStatsSnapshot snapshot);
    bool TryReadNativeMemory(out long bytes, out string kind)
    {
        bytes = 0;
        kind = "";
        return false;
    }
    bool TryReadProcessMemory(out long bytes, out string kind)
    {
        bytes = 0;
        kind = "";
        return false;
    }
}
