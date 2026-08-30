using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Pins the performance triage contract.
///
/// The aggregator's job is to be honest: never synthesise a frame rate, never hide buffer loss,
/// and always say when the build itself perturbs the numbers. These tests exist because a triage
/// surface that quietly invents plausible values is worse than no surface at all.
/// </summary>
public class PerformanceTriageTests
{
    private static readonly DateTime Generated = new(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Aggregate_ComputesMemoryGcCpuAndThreadWindows()
    {
        var summary = PerformanceAggregator.Aggregate(
            Capabilities(),
            Session(),
            Batch(
                Sample(0, managed: 1_000_000, gc0: 1, cpu: 10, threads: 20),
                Sample(1, managed: 3_000_000, gc0: 4, gc1: 1, cpu: 60, threads: 33),
                Sample(2, managed: 2_000_000, gc0: 6, gc1: 2, gc2: 1, cpu: 30, threads: 28)),
            hotspots: null,
            status: Status("profile", readOnly: true),
            generatedUtc: Generated);

        Assert.Equal(1_000_000, summary.Memory.ManagedStartBytes);
        Assert.Equal(2_000_000, summary.Memory.ManagedEndBytes);
        Assert.Equal(3_000_000, summary.Memory.ManagedPeakBytes);
        Assert.Equal(1_000_000, summary.Memory.ManagedDeltaBytes);
        Assert.Equal(5, summary.Gc.Gen0Delta);
        Assert.Equal(2, summary.Gc.Gen1Delta);
        Assert.Equal(1, summary.Gc.Gen2Delta);
        Assert.Equal(33.33, summary.Cpu.AveragePercent);
        Assert.Equal(60, summary.Cpu.PeakPercent);
        Assert.Equal(33, summary.Threads.PeakCount);
        Assert.Equal(3, summary.Session.SampleCount);
        Assert.Equal(2000, summary.Session.SampledDurationMs);
        Assert.Equal(PerformanceSummary.CurrentSchemaVersion, summary.SchemaVersion);
    }

    [Fact]
    public void Aggregate_ReportsNativeMemoryOnlyWhenSamplesCarryIt()
    {
        var withoutNative = PerformanceAggregator.Aggregate(
            Capabilities(), Session(), Batch(Sample(0, managed: 10)), null, Status(), Generated);
        Assert.False(withoutNative.Memory.NativeSupported);
        Assert.Null(withoutNative.Memory.NativeEndBytes);

        var native0 = Sample(0, managed: 10);
        native0.NativeMemoryBytes = 500;
        native0.NativeMemoryKind = "resident";
        var native1 = Sample(1, managed: 10);
        native1.NativeMemoryBytes = 900;
        native1.NativeMemoryKind = "resident";

        var withNative = PerformanceAggregator.Aggregate(
            Capabilities(nativeMemory: true), Session(), Batch(native0, native1), null, Status(), Generated);

        Assert.True(withNative.Memory.NativeSupported);
        Assert.Equal(500, withNative.Memory.NativeStartBytes);
        Assert.Equal(900, withNative.Memory.NativeEndBytes);
        Assert.Equal(900, withNative.Memory.NativePeakBytes);
        Assert.Equal(400, withNative.Memory.NativeDeltaBytes);
        Assert.Equal("resident", withNative.Memory.NativeKind);
    }

    [Fact]
    public void Aggregate_RefusesToSubtractIncompatibleNativeMemoryKinds()
    {
        var first = Sample(0, managed: 10);
        first.NativeMemoryBytes = 1_000;
        first.NativeMemoryKind = "process.working-set-minus-managed";
        var second = Sample(1, managed: 20);
        second.NativeMemoryBytes = 2_000;
        second.NativeMemoryKind = "android.native-heap-allocated";

        var summary = PerformanceAggregator.Aggregate(
            Capabilities(nativeMemory: true),
            Session(),
            Batch(first, second),
            null,
            Status(),
            Generated);

        Assert.False(summary.Memory.NativeSupported);
        Assert.True(summary.Memory.NativeKindsMixed);
        Assert.Null(summary.Memory.NativeDeltaBytes);
        Assert.Contains(summary.Warnings, warning =>
            warning.Contains("incompatible", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Aggregate_ReportsProcessFootprintSeparatelyFromNativeHeap()
    {
        var first = Sample(0, managed: 10);
        first.ProcessMemoryBytes = 1_000;
        first.ProcessMemoryKind = "windows.working-set";
        var second = Sample(1, managed: 20);
        second.ProcessMemoryBytes = 1_500;
        second.ProcessMemoryKind = "windows.working-set";

        var summary = PerformanceAggregator.Aggregate(
            Capabilities(), Session(), Batch(first, second), null, Status(), Generated);

        Assert.True(summary.Memory.ProcessSupported);
        Assert.Equal(500, summary.Memory.ProcessDeltaBytes);
        Assert.Equal("process.working-set", summary.Memory.ProcessKind);
        Assert.False(summary.Memory.NativeSupported);
        Assert.Null(summary.Memory.NativeDeltaBytes);
    }

    [Fact]
    public void Aggregate_NormalizesEquivalentProcessMemoryKinds()
    {
        var first = Sample(0, managed: 10);
        first.ProcessMemoryBytes = 1_000;
        first.ProcessMemoryKind = "windows.working-set";
        var second = Sample(1, managed: 20);
        second.ProcessMemoryBytes = 1_500;
        second.ProcessMemoryKind = "process.working-set";

        var summary = PerformanceAggregator.Aggregate(
            Capabilities(), Session(), Batch(first, second), null, Status(), Generated);

        Assert.True(summary.Memory.ProcessSupported);
        Assert.False(summary.Memory.ProcessKindsMixed);
        Assert.Equal("process.working-set", summary.Memory.ProcessKind);
        Assert.Equal(500, summary.Memory.ProcessDeltaBytes);
    }

    [Fact]
    public void Aggregate_WithholdsIncompatibleProcessMemoryKindsWithAWarning()
    {
        var first = Sample(0, managed: 10);
        first.ProcessMemoryBytes = 1_000;
        first.ProcessMemoryKind = "apple.phys-footprint";
        var second = Sample(1, managed: 20);
        second.ProcessMemoryBytes = 1_500;
        second.ProcessMemoryKind = "process.working-set";

        var summary = PerformanceAggregator.Aggregate(
            Capabilities(), Session(), Batch(first, second), null, Status(), Generated);

        Assert.False(summary.Memory.ProcessSupported);
        Assert.True(summary.Memory.ProcessKindsMixed);
        Assert.Null(summary.Memory.ProcessDeltaBytes);
        Assert.Contains(summary.Warnings, warning =>
            warning.Contains("incompatible", StringComparison.OrdinalIgnoreCase));
    }

    // ── frames: never synthesise ─────────────────────────────────────────────────────────────

    [Fact]
    public void Aggregate_WithholdsFrameRateWhenTimingsAreOnlyEstimated()
    {
        var sample = Sample(0, managed: 10);
        sample.FrameSource = "estimated";
        sample.Fps = 58;
        sample.WorstFrameTimeMs = 120;
        sample.JankFrameCount = 4;

        var summary = PerformanceAggregator.Aggregate(
            Capabilities(frameTimingsEstimated: true), Session(), Batch(sample), null, Status(), Generated);

        Assert.False(summary.Frames.Supported);
        Assert.Null(summary.Frames.AverageFps);
        Assert.Null(summary.Frames.WorstFrameTimeMs);
        Assert.Null(summary.Frames.JankFrameCount);
        Assert.Contains("estimate", summary.Frames.UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(summary.Warnings, warning => warning.Contains("estimate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Aggregate_WithholdsFrameRateWhenNoFrameSourceExists()
    {
        var summary = PerformanceAggregator.Aggregate(
            Capabilities(), Session(), Batch(Sample(0, managed: 10)), null, Status(), Generated);

        Assert.False(summary.Frames.Supported);
        Assert.Null(summary.Frames.AverageFps);
        Assert.Contains("native frame timing", summary.Frames.UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Aggregate_ReportsNativeFrameStatisticsWhenTheyAreAuthoritative()
    {
        var first = Sample(0, managed: 10);
        first.FrameSource = "native.android.framemetrics";
        first.FrameQuality = "native.exact";
        first.Fps = 60;
        first.FrameTimeMsP95 = 17;
        first.WorstFrameTimeMs = 22;
        first.JankFrameCount = 1;
        first.UiThreadStallCount = 0;

        var second = Sample(1, managed: 10);
        second.FrameSource = "native.android.framemetrics";
        second.FrameQuality = "native.exact";
        second.Fps = 42;
        second.FrameTimeMsP95 = 31;
        second.WorstFrameTimeMs = 96;
        second.JankFrameCount = 5;
        second.UiThreadStallCount = 2;

        var summary = PerformanceAggregator.Aggregate(
            Capabilities(nativeFrames: true, jank: true, stalls: true),
            Session(), Batch(first, second), null, Status(), Generated);

        Assert.True(summary.Frames.Supported);
        Assert.Equal("native.android.framemetrics", summary.Frames.Source);
        Assert.Equal("native.exact", summary.Frames.Quality);
        Assert.Equal(51, summary.Frames.AverageFps);
        Assert.Equal(42, summary.Frames.MinimumFps);
        Assert.Equal(31, summary.Frames.FrameTimeMsP95);
        Assert.Equal(96, summary.Frames.WorstFrameTimeMs);
        Assert.Equal(6, summary.Frames.JankFrameCount);
        Assert.Equal(2, summary.Frames.UiThreadStallCount);
    }

    [Fact]
    public void Aggregate_WithholdsCadenceDerivedFrameStatistics()
    {
        var sample = Sample(0, managed: 10);
        sample.FrameSource = "native.android.choreographer";
        sample.FrameQuality = "native.cadence";
        sample.Fps = 60;
        sample.FrameTimeMsP95 = 18;
        sample.WorstFrameTimeMs = 80;
        sample.JankFrameCount = 4;

        var summary = PerformanceAggregator.Aggregate(
            Capabilities(frameTimingsEstimated: true, jank: true),
            Session(), Batch(sample), null, Status(), Generated);

        Assert.False(summary.Frames.Supported);
        Assert.Equal("native.android.choreographer", summary.Frames.Source);
        Assert.Equal("native.cadence", summary.Frames.Quality);
        Assert.Null(summary.Frames.AverageFps);
        Assert.Null(summary.Frames.FrameTimeMsP95);
        Assert.Null(summary.Frames.JankFrameCount);
        Assert.Contains("cadence", summary.Frames.UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
    }

    // ── loss and taint ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Aggregate_SurfacesBufferLossProminently()
    {
        var batch = Batch(Sample(0, managed: 10), Sample(1, managed: 20));
        batch.SampleMetadata = new ProfilerStreamReadMetadata { OldestCursor = 40, LatestCursor = 42, LostCount = 40 };
        batch.MarkerMetadata = new ProfilerStreamReadMetadata { LostCount = 3 };
        batch.SpanMetadata = new ProfilerStreamReadMetadata { LostCount = 1 };

        var summary = PerformanceAggregator.Aggregate(
            Capabilities(), Session(), batch, null, Status("profile", readOnly: true), Generated);

        Assert.True(summary.Loss.AnyLoss);
        Assert.Equal(40, summary.Loss.SamplesLost);
        Assert.Equal(3, summary.Loss.MarkersLost);
        Assert.Equal(1, summary.Loss.SpansLost);
        Assert.Equal(40, summary.Loss.OldestSampleCursor);
        Assert.Contains(summary.Warnings, warning =>
            warning.Contains("overwrote", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Aggregate_SurfacesDataOmittedByTheSummaryLimit()
    {
        var batch = Batch(Sample(0, managed: 10), Sample(1, managed: 20));
        batch.SampleMetadata.AvailableCount = 12;
        batch.MarkerMetadata.AvailableCount = 4;
        batch.SpanMetadata.AvailableCount = 3;
        batch.Markers = [new ProfilerMarker()];
        batch.Spans = [new ProfilerSpan(), new ProfilerSpan()];

        var summary = PerformanceAggregator.Aggregate(
            Capabilities(), Session(), batch, null, Status("profile", readOnly: true), Generated);

        Assert.True(summary.Loss.AnyOmitted);
        Assert.Equal(10, summary.Loss.SamplesOmitted);
        Assert.Equal(3, summary.Loss.MarkersOmitted);
        Assert.Equal(1, summary.Loss.SpansOmitted);
        Assert.Contains(summary.Warnings, warning =>
            warning.Contains("summary limit omitted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Aggregate_SurfacesNativeFrameProviderLoss()
    {
        var sample = Sample(0, managed: 10);
        sample.FrameDataLossCount = 7;

        var summary = PerformanceAggregator.Aggregate(
            Capabilities(), Session(), Batch(sample), null, Status("profile", readOnly: true), Generated);

        Assert.True(summary.Loss.AnyLoss);
        Assert.Equal(7, summary.Loss.NativeFrameEventsLost);
        Assert.Contains(summary.Warnings, warning =>
            warning.Contains("lost frame event", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Aggregate_WarnsThatDebugModePerturbsMeasurements()
    {
        var summary = PerformanceAggregator.Aggregate(
            Capabilities(), Session(), Batch(Sample(0, managed: 10)), null, Status("debug"), Generated);

        Assert.False(summary.Capability.LowPerturbation);
        Assert.Equal("debug", summary.Capability.Mode);
        Assert.Contains(summary.Warnings, warning =>
            warning.Contains("Hot Reload", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(summary.Capability.Limitations, limitation =>
            limitation.Contains("profile mode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Aggregate_MarksAnExplicitProfileBuildAsLowPerturbation()
    {
        var summary = PerformanceAggregator.Aggregate(
            Capabilities(), Session(), Batch(Sample(0, managed: 1), Sample(1, managed: 2), Sample(2, managed: 3)),
            null, Status("profile", readOnly: true), Generated);

        Assert.True(summary.Capability.LowPerturbation);
        Assert.DoesNotContain(summary.Warnings, warning =>
            warning.Contains("Hot Reload", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Aggregate_ReportsAnUnavailableProfilerWithoutInventingMetrics()
    {
        var summary = PerformanceAggregator.Aggregate(
            new ProfilerCapabilities { Available = false, FeatureEnabled = false },
            session: null, batch: null, hotspots: null, status: Status(), generatedUtc: Generated);

        Assert.False(summary.Capability.Available);
        Assert.Equal(0, summary.Session.SampleCount);
        Assert.Null(summary.Memory.ManagedEndBytes);
        Assert.Null(summary.Cpu.AveragePercent);
        Assert.Single(summary.Warnings);
        Assert.Contains("disabled", summary.Warnings[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Aggregate_WarnsWhenTheRetainedWindowIsTooSmallToTrust()
    {
        var summary = PerformanceAggregator.Aggregate(
            Capabilities(), Session(), Batch(Sample(0, managed: 10)), null, Status("profile", readOnly: true), Generated);

        Assert.Contains(summary.Warnings, warning =>
            warning.Contains("three samples", StringComparison.OrdinalIgnoreCase));
    }

    // ── hotspots and markers ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Aggregate_SortsHotspotsByP95ThenMax()
    {
        var summary = PerformanceAggregator.Aggregate(
            Capabilities(), Session(), Batch(Sample(0, managed: 10)),
            [
                new ProfilerHotspot { Kind = "ui.operation", Name = "b", Count = 1, P95DurationMs = 10, MaxDurationMs = 12 },
                new ProfilerHotspot { Kind = "ui.operation", Name = "a", Count = 2, P95DurationMs = 90, MaxDurationMs = 95, ErrorCount = 1 },
            ],
            Status(), Generated);

        Assert.Equal(["ui.operation/a", "ui.operation/b"],
            summary.Hotspots.Select(hotspot => $"{hotspot.Kind}/{hotspot.Name}"));
        Assert.Equal(1, summary.Hotspots[0].ErrorCount);
    }

    [Fact]
    public void Aggregate_BucketsMarkersByType()
    {
        var batch = Batch(Sample(0, managed: 10));
        batch.Markers =
        [
            new ProfilerMarker { Type = "navigation.push", Name = "n" },
            new ProfilerMarker { Type = "network.request", Name = "r" },
            new ProfilerMarker { Type = "user.action", Name = "t" },
            new ProfilerMarker { Type = "ui.layout", Name = "l" },
            new ProfilerMarker { Type = "custom", Name = "c" },
        ];

        var summary = PerformanceAggregator.Aggregate(Capabilities(), Session(), batch, null, Status(), Generated);

        Assert.Equal(5, summary.Markers.Total);
        Assert.Equal(1, summary.Markers.Navigation);
        Assert.Equal(1, summary.Markers.Network);
        Assert.Equal(2, summary.Markers.Ui);
        Assert.Equal(1, summary.Markers.Other);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static ProfilerCapabilities Capabilities(
        bool nativeMemory = false,
        bool nativeFrames = false,
        bool frameTimingsEstimated = false,
        bool jank = false,
        bool stalls = false)
        => new()
        {
            Available = true,
            FeatureEnabled = true,
            Platform = "Windows",
            ManagedMemorySupported = true,
            NativeMemorySupported = nativeMemory,
            ProcessMemorySupported = true,
            GcSupported = true,
            CpuPercentSupported = true,
            ThreadCountSupported = true,
            NativeFrameTimingsSupported = nativeFrames,
            FrameTimingsEstimated = frameTimingsEstimated,
            JankEventsSupported = jank,
            UiThreadStallSupported = stalls,
        };

    private static ProfilerSessionInfo Session() => new()
    {
        SessionId = "session-1",
        StartedAtUtc = Generated,
        SampleIntervalMs = 250,
        IsActive = true,
    };

    private static AgentStatus Status(string mode = "debug", bool readOnly = false) => new()
    {
        Agent = new AgentDescriptor { Mode = mode, ReadOnly = readOnly },
        Device = new DeviceDescriptor { Platform = "Windows" },
    };

    private static ProfilerBatch Batch(params ProfilerSample[] samples) => new()
    {
        SessionId = "session-1",
        IsActive = true,
        Samples = [.. samples],
    };

    private static ProfilerSample Sample(
        int second,
        long managed,
        int gc0 = 0,
        int gc1 = 0,
        int gc2 = 0,
        double? cpu = null,
        int? threads = null)
        => new()
        {
            TsUtc = Generated.AddSeconds(second),
            ManagedBytes = managed,
            Gc0 = gc0,
            Gc1 = gc1,
            Gc2 = gc2,
            CpuPercent = cpu,
            ThreadCount = threads,
            FrameSource = "unavailable",
            FrameQuality = "unavailable",
        };
}
