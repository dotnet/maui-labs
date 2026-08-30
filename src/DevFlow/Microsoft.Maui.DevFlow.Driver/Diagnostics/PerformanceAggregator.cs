using System.Globalization;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Turns raw profiler streams into a <see cref="PerformanceSummary"/>.
///
/// Aggregation lives here — in the shared driver — rather than in the agent so the CLI, the MCP
/// tools, and the Inspector all present identical analysis from identical inputs, and so the
/// in-app agent keeps doing nothing beyond bounded sampling.
///
/// The aggregator is pure: no I/O, no clock reads beyond the caller-supplied timestamp, and no
/// synthesis. A metric the agent cannot measure stays null and is explained in
/// <see cref="PerformanceCapability.Limitations"/>.
/// </summary>
public static class PerformanceAggregator
{
    /// <summary>Frame source values the agent may report on a sample.</summary>
    private const string NativeFrameSourcePrefix = "native.";
    private const string ExactFrameQuality = "native.exact";

    public static PerformanceSummary Aggregate(
        ProfilerCapabilities? capabilities,
        ProfilerSessionInfo? session,
        ProfilerBatch? batch,
        IReadOnlyList<ProfilerHotspot>? hotspots,
        AgentStatus? status = null,
        DateTime? generatedUtc = null)
    {
        var samples = batch?.Samples ?? [];
        var markers = batch?.Markers ?? [];
        var spans = batch?.Spans ?? [];

        var summary = new PerformanceSummary
        {
            GeneratedUtc = (generatedUtc ?? DateTime.UtcNow)
                .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            Session = BuildSession(session, batch, samples),
            Capability = BuildCapability(capabilities, status),
        };

        summary.Memory = BuildMemory(samples, capabilities);
        summary.Gc = BuildGc(samples, capabilities);
        summary.Cpu = BuildCpu(samples, capabilities);
        summary.Threads = BuildThreads(samples, capabilities);
        summary.Frames = BuildFrames(samples, capabilities);
        summary.Markers = BuildMarkerCounts(markers, spans);
        summary.Loss = BuildLoss(batch);

        if (hotspots is { Count: > 0 })
        {
            summary.Hotspots = hotspots
                .OrderByDescending(hotspot => hotspot.P95DurationMs)
                .ThenByDescending(hotspot => hotspot.MaxDurationMs)
                .ThenByDescending(hotspot => hotspot.ErrorCount)
                .ThenBy(hotspot => hotspot.Name, StringComparer.Ordinal)
                .Select(hotspot => new PerformanceHotspot
                {
                    Kind = hotspot.Kind,
                    Name = hotspot.Name,
                    Screen = hotspot.Screen,
                    Count = hotspot.Count,
                    ErrorCount = hotspot.ErrorCount,
                    AvgDurationMs = Round(hotspot.AvgDurationMs),
                    P95DurationMs = Round(hotspot.P95DurationMs),
                    MaxDurationMs = Round(hotspot.MaxDurationMs),
                })
                .ToList();
        }

        summary.Warnings = BuildWarnings(summary, samples.Count);
        return summary;
    }

    // ── sections ─────────────────────────────────────────────────────────────────────────────

    private static PerformanceSessionInfo BuildSession(
        ProfilerSessionInfo? session,
        ProfilerBatch? batch,
        IReadOnlyList<ProfilerSample> samples)
    {
        var info = new PerformanceSessionInfo
        {
            SessionId = session?.SessionId ?? (string.IsNullOrEmpty(batch?.SessionId) ? null : batch!.SessionId),
            Active = session?.IsActive ?? batch?.IsActive ?? false,
            StartedUtc = session is null || session.StartedAtUtc == default
                ? null
                : session.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            SampleIntervalMs = session?.SampleIntervalMs ?? 0,
            SampleCount = samples.Count,
            StopToken = string.IsNullOrWhiteSpace(session?.StopToken)
                ? null
                : session.StopToken,
        };

        if (samples.Count >= 2)
            info.SampledDurationMs = Round((samples[^1].TsUtc - samples[0].TsUtc).TotalMilliseconds);

        return info;
    }

    private static PerformanceMemory BuildMemory(
        IReadOnlyList<ProfilerSample> samples,
        ProfilerCapabilities? capabilities)
    {
        var memory = new PerformanceMemory
        {
            NativeSupported = capabilities?.NativeMemorySupported ?? false,
            ProcessSupported = capabilities?.ProcessMemorySupported ?? false,
        };

        if (samples.Count == 0)
            return memory;

        memory.ManagedStartBytes = samples[0].ManagedBytes;
        memory.ManagedEndBytes = samples[^1].ManagedBytes;
        memory.ManagedPeakBytes = samples.Max(sample => sample.ManagedBytes);
        memory.ManagedDeltaBytes = memory.ManagedEndBytes - memory.ManagedStartBytes;

        var process = samples.Where(sample => sample.ProcessMemoryBytes.HasValue).ToList();
        if (process.Count > 0)
        {
            var processKinds = process
                .Select(sample => NormalizeProcessMemoryKind(sample.ProcessMemoryKind))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (processKinds.Length == 1)
            {
                memory.ProcessSupported = true;
                memory.ProcessKind = processKinds[0];
                memory.ProcessStartBytes = process[0].ProcessMemoryBytes;
                memory.ProcessEndBytes = process[^1].ProcessMemoryBytes;
                memory.ProcessPeakBytes = process.Max(sample => sample.ProcessMemoryBytes!.Value);
                memory.ProcessDeltaBytes = memory.ProcessEndBytes - memory.ProcessStartBytes;
            }
            else
            {
                memory.ProcessSupported = false;
                memory.ProcessKindsMixed = true;
                memory.ProcessUnsupportedReason =
                    "Process-memory samples used incompatible measurement kinds, so no start/end/peak/delta was calculated.";
            }
        }

        var native = samples.Where(sample => sample.NativeMemoryBytes.HasValue).ToList();
        if (native.Count == 0)
            return memory;

        var kinds = native
            .Select(sample => sample.NativeMemoryKind ?? "unknown")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (kinds.Length != 1)
        {
            memory.NativeSupported = false;
            memory.NativeKindsMixed = true;
            memory.NativeUnsupportedReason =
                "Native-heap samples used incompatible measurement kinds, so no start/end/peak/delta was calculated.";
            return memory;
        }

        memory.NativeSupported = true;
        memory.NativeKind = kinds[0];
        memory.NativeStartBytes = native[0].NativeMemoryBytes;
        memory.NativeEndBytes = native[^1].NativeMemoryBytes;
        memory.NativePeakBytes = native.Max(sample => sample.NativeMemoryBytes!.Value);
        memory.NativeDeltaBytes = memory.NativeEndBytes - memory.NativeStartBytes;
        return memory;
    }

    private static string NormalizeProcessMemoryKind(string? kind)
    {
        var normalized = string.IsNullOrWhiteSpace(kind)
            ? "unknown"
            : kind.Trim().ToLowerInvariant();
        return normalized is "windows.working-set" or "process.working-set"
            ? "process.working-set"
            : normalized;
    }

    private static PerformanceGc BuildGc(
        IReadOnlyList<ProfilerSample> samples,
        ProfilerCapabilities? capabilities)
    {
        var gc = new PerformanceGc { Supported = capabilities?.GcSupported ?? false };
        if (samples.Count == 0 || !gc.Supported)
            return gc;

        gc.Gen0Delta = Math.Max(0, samples[^1].Gc0 - samples[0].Gc0);
        gc.Gen1Delta = Math.Max(0, samples[^1].Gc1 - samples[0].Gc1);
        gc.Gen2Delta = Math.Max(0, samples[^1].Gc2 - samples[0].Gc2);
        return gc;
    }

    private static PerformanceCpu BuildCpu(
        IReadOnlyList<ProfilerSample> samples,
        ProfilerCapabilities? capabilities)
    {
        var cpu = new PerformanceCpu { Supported = capabilities?.CpuPercentSupported ?? false };
        var values = samples
            .Where(sample => sample.CpuPercent is { } value && double.IsFinite(value))
            .Select(sample => sample.CpuPercent!.Value)
            .ToList();
        if (values.Count == 0)
            return cpu;

        cpu.Supported = true;
        cpu.AveragePercent = Round(values.Average());
        cpu.PeakPercent = Round(values.Max());
        return cpu;
    }

    private static PerformanceThreads BuildThreads(
        IReadOnlyList<ProfilerSample> samples,
        ProfilerCapabilities? capabilities)
    {
        var threads = new PerformanceThreads { Supported = capabilities?.ThreadCountSupported ?? false };
        var values = samples.Where(sample => sample.ThreadCount.HasValue).Select(sample => sample.ThreadCount!.Value).ToList();
        if (values.Count == 0)
            return threads;

        threads.Supported = true;
        threads.PeakCount = values.Max();
        return threads;
    }

    /// <summary>
    /// Frame statistics are reported only from samples backed by exact native render timings.
    /// Display-cadence callbacks are useful stall signals but are not rendered frames, so their
    /// modelled FPS and frame-time values are deliberately withheld.
    /// </summary>
    private static PerformanceFrames BuildFrames(
        IReadOnlyList<ProfilerSample> samples,
        ProfilerCapabilities? capabilities)
    {
        var frames = new PerformanceFrames();
        var nativeSamples = samples
            .Where(sample => sample.FrameSource?.StartsWith(NativeFrameSourcePrefix, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        var exactSamples = nativeSamples
            .Where(sample => string.Equals(sample.FrameQuality, ExactFrameQuality, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exactSamples.Count == 0 || capabilities?.FrameTimingsEstimated == true)
        {
            frames.Supported = false;
            frames.Source = nativeSamples.Count > 0
                ? nativeSamples[^1].FrameSource
                : samples.Count == 0
                ? null
                : samples[^1].FrameSource;
            frames.Quality = nativeSamples.Count > 0
                ? nativeSamples[^1].FrameQuality
                : samples.Count == 0 ? null : samples[^1].FrameQuality;
            frames.UnsupportedReason = capabilities?.FrameTimingsEstimated == true ||
                                       nativeSamples.Any(sample =>
                                           string.Equals(sample.FrameQuality, "native.cadence", StringComparison.OrdinalIgnoreCase))
                ? "This provider observes display cadence rather than exact rendered frames. Cadence-derived estimates of frame rate and timing are not reported — use a native profiler for frame analysis."
                : "No native frame timing source is available on this platform or in this build.";
            return frames;
        }

        frames.Supported = true;
        frames.Source = exactSamples[^1].FrameSource;
        frames.Quality = exactSamples[^1].FrameQuality;

        var fps = exactSamples.Where(s => s.Fps is { } value && double.IsFinite(value)).Select(s => s.Fps!.Value).ToList();
        if (fps.Count > 0)
        {
            frames.AverageFps = Round(fps.Average());
            frames.MinimumFps = Round(fps.Min());
        }

        var p95 = exactSamples.Where(s => s.FrameTimeMsP95 is { } value && double.IsFinite(value)).Select(s => s.FrameTimeMsP95!.Value).ToList();
        if (p95.Count > 0)
            frames.FrameTimeMsP95 = Round(p95.Max());

        var worst = exactSamples.Where(s => s.WorstFrameTimeMs is { } value && double.IsFinite(value)).Select(s => s.WorstFrameTimeMs!.Value).ToList();
        if (worst.Count > 0)
            frames.WorstFrameTimeMs = Round(worst.Max());

        if (capabilities?.JankEventsSupported ?? false)
            frames.JankFrameCount = exactSamples.Sum(sample => sample.JankFrameCount);
        if (capabilities?.UiThreadStallSupported ?? false)
            frames.UiThreadStallCount = exactSamples.Sum(sample => sample.UiThreadStallCount);

        return frames;
    }

    private static PerformanceMarkerCounts BuildMarkerCounts(
        IReadOnlyList<ProfilerMarker> markers,
        IReadOnlyList<ProfilerSpan> spans)
    {
        var counts = new PerformanceMarkerCounts
        {
            Total = markers.Count,
            SpanCount = spans.Count,
        };

        foreach (var marker in markers)
        {
            var type = marker.Type ?? "";
            if (type.StartsWith("navigation", StringComparison.OrdinalIgnoreCase)) counts.Navigation++;
            else if (type.StartsWith("network", StringComparison.OrdinalIgnoreCase) || type.StartsWith("http", StringComparison.OrdinalIgnoreCase)) counts.Network++;
            else if (type.StartsWith("ui", StringComparison.OrdinalIgnoreCase) || type.StartsWith("user", StringComparison.OrdinalIgnoreCase)) counts.Ui++;
            else counts.Other++;
        }

        return counts;
    }

    private static PerformanceLoss BuildLoss(ProfilerBatch? batch)
    {
        var loss = new PerformanceLoss();
        if (batch is null)
            return loss;

        loss.SamplesLost = batch.SampleMetadata?.LostCount ?? 0;
        loss.MarkersLost = batch.MarkerMetadata?.LostCount ?? 0;
        loss.SpansLost = batch.SpanMetadata?.LostCount ?? 0;
        loss.NativeFrameEventsLost = batch.Samples.Sum(sample => (long)Math.Max(0, sample.FrameDataLossCount));
        loss.SamplesOmitted = Math.Max(0, (batch.SampleMetadata?.AvailableCount ?? 0) - batch.Samples.Count);
        loss.MarkersOmitted = Math.Max(0, (batch.MarkerMetadata?.AvailableCount ?? 0) - batch.Markers.Count);
        loss.SpansOmitted = Math.Max(0, (batch.SpanMetadata?.AvailableCount ?? 0) - batch.Spans.Count);
        loss.OldestSampleCursor = batch.SampleMetadata?.OldestCursor ?? 0;
        loss.LatestSampleCursor = batch.SampleMetadata?.LatestCursor ?? 0;
        loss.AnyLoss = loss.SamplesLost > 0 || loss.MarkersLost > 0 ||
            loss.SpansLost > 0 || loss.NativeFrameEventsLost > 0;
        loss.AnyOmitted = loss.SamplesOmitted > 0 || loss.MarkersOmitted > 0 || loss.SpansOmitted > 0;
        return loss;
    }

    private static PerformanceCapability BuildCapability(ProfilerCapabilities? capabilities, AgentStatus? status)
    {
        var mode = status?.Agent?.Mode;
        var capability = new PerformanceCapability
        {
            Available = capabilities?.Available ?? false,
            FeatureEnabled = capabilities?.FeatureEnabled ?? false,
            Platform = capabilities?.Platform ?? status?.Device?.Platform ?? "unknown",
            Mode = string.IsNullOrWhiteSpace(mode) ? "unknown" : mode!,
            ReadOnly = status?.Agent?.ReadOnly ?? false,
            ManagedMemorySupported = capabilities?.ManagedMemorySupported ?? false,
            NativeMemorySupported = capabilities?.NativeMemorySupported ?? false,
            ProcessMemorySupported = capabilities?.ProcessMemorySupported ?? false,
            GcSupported = capabilities?.GcSupported ?? false,
            CpuSupported = capabilities?.CpuPercentSupported ?? false,
            ThreadCountSupported = capabilities?.ThreadCountSupported ?? false,
            NativeFrameTimingsSupported = capabilities?.NativeFrameTimingsSupported ?? false,
            FrameTimingsEstimated = capabilities?.FrameTimingsEstimated ?? false,
            JankEventsSupported = capabilities?.JankEventsSupported ?? false,
            UiThreadStallSupported = capabilities?.UiThreadStallSupported ?? false,
        };

        capability.LowPerturbation =
            string.Equals(capability.Mode, "profile", StringComparison.OrdinalIgnoreCase) && capability.ReadOnly;

        capability.Limitations.Add(
            "Triage only: these numbers come from bounded in-app sampling, not from a native profiler. Use a native profiler to attribute cost to call stacks.");
        if (!capability.NativeFrameTimingsSupported)
            capability.Limitations.Add("Native frame timings are unavailable, so frame rate and jank are not reported.");
        if (capability.FrameTimingsEstimated)
            capability.Limitations.Add("This agent estimates frame timings; estimated frame rates are deliberately withheld.");
        if (!capability.NativeMemorySupported)
            capability.Limitations.Add("A native-heap-specific counter is not observable on this platform.");
        if (!capability.ProcessMemorySupported)
            capability.Limitations.Add("Process resident/physical memory is not observable on this platform.");
        if (!capability.CpuSupported)
            capability.Limitations.Add("Process CPU percentage is not observable on this platform.");
        if (!capability.LowPerturbation)
        {
            capability.Limitations.Add(
                "The agent is not running in explicit profile mode, so DevFlow hooks, Hot Reload, and the debugger perturb the measurements.");
        }

        return capability;
    }

    private static List<string> BuildWarnings(PerformanceSummary summary, int sampleCount)
    {
        var warnings = new List<string>();

        if (!summary.Capability.Available || !summary.Capability.FeatureEnabled)
        {
            warnings.Add("The profiler is disabled on this agent. Enable it with AgentOptions.EnableProfiler = true.");
            return warnings;
        }

        if (summary.Loss.SamplesLost > 0 ||
            summary.Loss.MarkersLost > 0 ||
            summary.Loss.SpansLost > 0)
        {
            warnings.Add(
                $"Buffer loss: profiler buffers overwrote data before it was read ({summary.Loss.SamplesLost} sample(s), " +
                $"{summary.Loss.MarkersLost} marker(s), {summary.Loss.SpansLost} span(s)). Start values, peaks, and " +
                "deltas describe only the retained window.");
        }
        if (summary.Loss.NativeFrameEventsLost > 0)
        {
            warnings.Add(
                $"The native frame provider reported {summary.Loss.NativeFrameEventsLost} lost frame event(s); " +
                "frame percentiles, jank counts, and FPS describe only the observed frames.");
        }

        if (summary.Loss.AnyOmitted)
        {
            warnings.Add(
                $"The requested summary limit omitted retained data ({summary.Loss.SamplesOmitted} sample(s), " +
                $"{summary.Loss.MarkersOmitted} marker(s), {summary.Loss.SpansOmitted} span(s)). Metrics describe " +
                "only the returned prefix of the retained window; increase the sample limit for a longer session.");
        }

        if (sampleCount == 0)
            warnings.Add("No samples were retained for this window, so every metric is unavailable.");
        else if (sampleCount < 3)
            warnings.Add("Fewer than three samples were retained; start/end deltas are not meaningful at this resolution.");

        if (!summary.Frames.Supported && summary.Frames.UnsupportedReason is { } reason)
            warnings.Add(reason);

        if (summary.Memory.NativeKindsMixed && summary.Memory.NativeUnsupportedReason is { } memoryReason)
            warnings.Add(memoryReason);
        if (summary.Memory.ProcessKindsMixed && summary.Memory.ProcessUnsupportedReason is { } processMemoryReason)
            warnings.Add(processMemoryReason);

        if (!summary.Capability.LowPerturbation)
        {
            warnings.Add(
                "Measured in a non-profile build: Hot Reload, the debugger, and DevFlow's own diagnostics inflate CPU, " +
                "allocations, and frame times. Compare runs, do not trust absolute values.");
        }

        return warnings;
    }

    private static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
