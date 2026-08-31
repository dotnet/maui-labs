using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Maui.DevFlow.Agent.Core.Profiling;
using System.Reflection;
using System.Linq;

namespace Microsoft.Maui.DevFlow.Tests;

public class ProfilerCoreTests
{
    [Fact]
    public void ProfilerBatch_SerializesAndDeserializes()
    {
        var now = DateTime.UtcNow;
        var batch = new ProfilerBatch
        {
            SessionId = "session-1",
            IsActive = true,
            SampleCursor = 2,
            MarkerCursor = 3,
            SpanCursor = 4,
            SampleMetadata = new()
            {
                OldestCursor = 1,
                LatestCursor = 4,
                LostCount = 0,
                AvailableCount = 3
            },
            Samples = new()
            {
                new ProfilerSample
                {
                    TsUtc = now,
                    Fps = 59.9,
                    FrameTimeMsP50 = 16.67,
                    FrameTimeMsP95 = 22.4,
                    WorstFrameTimeMs = 31.2,
                    ManagedBytes = 123_456,
                    NativeMemoryBytes = 654_321,
                    NativeMemoryKind = "android.native-heap-allocated",
                    Gc0 = 10,
                    Gc1 = 4,
                    Gc2 = 1,
                    CpuPercent = 33.2,
                    ThreadCount = 14,
                    JankFrameCount = 2,
                    UiThreadStallCount = 1,
                    FrameSource = "native.android.choreographer",
                    FrameQuality = "estimated"
                }
            },
            Markers = new()
            {
                new ProfilerMarker
                {
                    TsUtc = now,
                    Type = "navigation.start",
                    Name = "//native",
                    PayloadJson = """{"route":"//native"}"""
                }
            },
            Spans = new()
            {
                new ProfilerSpan
                {
                    SpanId = "span-1",
                    StartTsUtc = now,
                    EndTsUtc = now.AddMilliseconds(18),
                    DurationMs = 18,
                    Kind = "ui.operation",
                    Name = "action.tap",
                    Status = "ok"
                }
            }
        };

        var json = JsonSerializer.Serialize(batch);
        var parsed = JsonSerializer.Deserialize<ProfilerBatch>(json);

        Assert.NotNull(parsed);
        Assert.Equal("session-1", parsed.SessionId);
        Assert.True(parsed.IsActive);
        Assert.Single(parsed.Samples);
        Assert.Single(parsed.Markers);
        Assert.Single(parsed.Spans);
        Assert.Equal("navigation.start", parsed.Markers[0].Type);
        Assert.Equal(4, parsed.SpanCursor);
        Assert.Equal(1, parsed.SampleMetadata.OldestCursor);
        Assert.Equal(4, parsed.SampleMetadata.LatestCursor);
        Assert.Equal(123_456, parsed.Samples[0].ManagedBytes);
        Assert.Equal(654_321, parsed.Samples[0].NativeMemoryBytes);
        Assert.Equal("android.native-heap-allocated", parsed.Samples[0].NativeMemoryKind);
        Assert.Equal("native.android.choreographer", parsed.Samples[0].FrameSource);
        Assert.Equal(2, parsed.Samples[0].JankFrameCount);
    }

    [Fact]
    public void ProfilerRingBuffer_OverwritesOldestWhenCapacityReached()
    {
        var ring = new ProfilerRingBuffer<ProfilerMarker>(3);
        ring.Add(new ProfilerMarker { Name = "m1", Type = "t", TsUtc = DateTime.UtcNow });
        ring.Add(new ProfilerMarker { Name = "m2", Type = "t", TsUtc = DateTime.UtcNow.AddMilliseconds(1) });
        ring.Add(new ProfilerMarker { Name = "m3", Type = "t", TsUtc = DateTime.UtcNow.AddMilliseconds(2) });
        ring.Add(new ProfilerMarker { Name = "m4", Type = "t", TsUtc = DateTime.UtcNow.AddMilliseconds(3) });

        var result = ring.ReadAfter(0, 10);

        Assert.Equal(4, result.NextCursor);
        Assert.Equal(2, result.OldestCursor);
        Assert.Equal(4, result.LatestCursor);
        Assert.Equal(1, result.LostCount);
        Assert.Equal(3, result.AvailableCount);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal("m2", result.Items[0].Name);
        Assert.Equal("m3", result.Items[1].Name);
        Assert.Equal("m4", result.Items[2].Name);
    }

    [Fact]
    public void ProfilerRingBuffer_LimitedReadAdvancesCursorOnlyToLastReturnedItem()
    {
        var ring = new ProfilerRingBuffer<ProfilerMarker>(5);
        for (var i = 1; i <= 4; i++)
            ring.Add(new ProfilerMarker { Name = $"m{i}", Type = "t", TsUtc = DateTime.UtcNow });

        var firstPage = ring.ReadAfter(0, 2);
        var secondPage = ring.ReadAfter(firstPage.NextCursor, 2);

        Assert.Equal(2, firstPage.NextCursor);
        Assert.Equal(4, firstPage.LatestCursor);
        Assert.Equal(["m1", "m2"], firstPage.Items.Select(item => item.Name));
        Assert.Equal(4, secondPage.NextCursor);
        Assert.Equal(["m3", "m4"], secondPage.Items.Select(item => item.Name));
    }

    [Fact]
    public void ProfilerRingBuffer_ReadLatestAlwaysIncludesTheNewestItem()
    {
        var ring = new ProfilerRingBuffer<ProfilerMarker>(5);
        for (var i = 1; i <= 8; i++)
            ring.Add(new ProfilerMarker { Name = $"m{i}", Type = "t", TsUtc = DateTime.UtcNow });

        var latest = ring.ReadLatest(3);

        Assert.Equal(["m6", "m7", "m8"], latest.Items.Select(item => item.Name));
        Assert.Equal(8, latest.LatestCursor);
        Assert.Equal(8, latest.NextCursor);
        Assert.Equal(4, latest.OldestCursor);
        Assert.Equal(3, latest.LostCount);
        Assert.Equal(5, latest.AvailableCount);
    }

    [Fact]
    public void ProfilerSessionStore_EnforcesMonotonicMarkerTimestamps()
    {
        var store = new ProfilerSessionStore(100, 100, 100);
        store.Start(500);

        var now = DateTime.UtcNow;
        store.AddMarker(new ProfilerMarker { TsUtc = now, Type = "user.action", Name = "first" });
        store.AddMarker(new ProfilerMarker { TsUtc = now.AddMilliseconds(-100), Type = "user.action", Name = "second" });

        var batch = store.GetBatch(sampleCursor: 0, markerCursor: 0, limit: 100);

        Assert.Equal(2, batch.Markers.Count);
        Assert.True(batch.Markers[1].TsUtc > batch.Markers[0].TsUtc);
        Assert.Equal("second", batch.Markers[1].Name);
    }

    [Fact]
    public void ProfilerSessionStore_ReportsOverwriteLossForEachStream()
    {
        var store = new ProfilerSessionStore(maxSamples: 2, maxMarkers: 2, maxSpans: 2);
        store.Start(500);
        var now = DateTime.UtcNow;

        for (var i = 0; i < 3; i++)
        {
            store.AddSample(new ProfilerSample { TsUtc = now.AddMilliseconds(i) });
            store.AddMarker(new ProfilerMarker { TsUtc = now.AddMilliseconds(i), Type = "test", Name = $"m{i}" });
            store.AddSpan(new ProfilerSpan
            {
                StartTsUtc = now.AddMilliseconds(i),
                EndTsUtc = now.AddMilliseconds(i + 1),
                Name = $"s{i}"
            });
        }

        var batch = store.GetBatch(sampleCursor: 0, markerCursor: 0, spanCursor: 0, limit: 1);

        Assert.Equal(2, batch.SampleCursor);
        Assert.Equal(2, batch.MarkerCursor);
        Assert.Equal(2, batch.SpanCursor);
        Assert.Equal(1, batch.SampleMetadata.LostCount);
        Assert.Equal(1, batch.MarkerMetadata.LostCount);
        Assert.Equal(1, batch.SpanMetadata.LostCount);
        Assert.Equal(2, batch.SampleMetadata.AvailableCount);
        Assert.Equal(3, batch.SampleMetadata.LatestCursor);
    }

    [Fact]
    public void ProfilerSessionStore_HotspotsAggregateSpanDurations()
    {
        var store = new ProfilerSessionStore(100, 100, 100);
        store.Start(500);
        var now = DateTime.UtcNow;

        store.AddSpan(new ProfilerSpan
        {
            SpanId = "s1",
            StartTsUtc = now,
            EndTsUtc = now.AddMilliseconds(40),
            Kind = "ui.operation",
            Name = "action.scroll",
            Status = "ok",
            Screen = "//feed"
        });
        store.AddSpan(new ProfilerSpan
        {
            SpanId = "s2",
            StartTsUtc = now.AddMilliseconds(50),
            EndTsUtc = now.AddMilliseconds(120),
            Kind = "ui.operation",
            Name = "action.scroll",
            Status = "error",
            Screen = "//feed"
        });

        var hotspots = store.GetHotspots(limit: 5, minDurationMs: 16, kind: "ui.operation");

        Assert.Single(hotspots);
        Assert.Equal("action.scroll", hotspots[0].Name);
        Assert.Equal(2, hotspots[0].Count);
        Assert.Equal(1, hotspots[0].ErrorCount);
        Assert.True(hotspots[0].P95DurationMs >= 40);
    }

    [Fact]
    public void RuntimeProfilerCollector_CollectsRuntimeMetrics()
    {
        var collector = new RuntimeProfilerCollector();
        collector.Start(250);
        Thread.Sleep(150);

        var first = collector.TryCollect(out var sample1);
        Thread.Sleep(150);
        var second = collector.TryCollect(out var sample2);
        collector.Stop();

        Assert.True(first);
        Assert.True(second);
        Assert.True(sample1.ManagedBytes >= 0);
        Assert.True(sample1.Gc0 >= 0);
        Assert.Equal("unavailable", sample1.FrameSource);
        Assert.Equal("unavailable", sample1.FrameQuality);
        Assert.Null(sample1.Fps);
        Assert.Null(sample1.FrameTimeMsP50);
        Assert.Null(sample1.FrameTimeMsP95);
        Assert.Null(sample1.WorstFrameTimeMs);
        Assert.Equal(0, sample1.JankFrameCount);
        Assert.Equal(0, sample1.UiThreadStallCount);
        var capabilities = collector.GetCapabilities();
        Assert.False(capabilities.FpsSupported);
        Assert.False(capabilities.FrameTimingsEstimated);
        Assert.False(capabilities.NativeFrameTimingsSupported);
        Assert.False(capabilities.JankEventsSupported);
        Assert.False(capabilities.UiThreadStallSupported);
        Assert.False(capabilities.NativeMemorySupported);
        Assert.True(capabilities.ProcessMemorySupported);
        Assert.Null(sample1.NativeMemoryBytes);
        Assert.Null(sample1.NativeMemoryKind);
        if (sample1.ProcessMemoryBytes.HasValue)
            Assert.Equal("process.working-set", sample1.ProcessMemoryKind);
        else
            Assert.Null(sample1.ProcessMemoryKind);
        Assert.True(sample2.TsUtc > sample1.TsUtc);
    }

    [Fact]
    public void ProfilerSessionStore_IsActiveReflectsLifecycle()
    {
        var store = new ProfilerSessionStore(10, 10, 10);
        Assert.False(store.IsActive);

        store.Start(250);
        Assert.True(store.IsActive);

        store.Stop();
        Assert.False(store.IsActive);
    }

    [Fact]
    public void RuntimeProfilerCollector_WhenNativeProviderStartFails_ReportsFrameMetricsAsUnavailable()
    {
        var provider = new ThrowingNativeProvider();
        var collector = new RuntimeProfilerCollector(provider);

        collector.Start(100);
        Thread.Sleep(120);
        var collected = collector.TryCollect(out var sample);
        var capabilities = collector.GetCapabilities();
        collector.Stop();

        Assert.Equal(1, provider.StartCalls);
        Assert.True(provider.StopCalls >= 1);
        Assert.True(collected);
        Assert.Equal("unavailable", sample.FrameSource);
        Assert.Equal("unavailable", sample.FrameQuality);
        Assert.Null(sample.Fps);
        Assert.Null(sample.FrameTimeMsP95);
        Assert.Equal(0, sample.JankFrameCount);
        Assert.Equal(0, sample.UiThreadStallCount);
        Assert.False(capabilities.FpsSupported);
        Assert.False(capabilities.FrameTimingsEstimated);
        Assert.False(capabilities.NativeFrameTimingsSupported);
        Assert.False(capabilities.JankEventsSupported);
        Assert.False(capabilities.UiThreadStallSupported);
    }

    [Fact]
    public void RuntimeProfilerCollector_PropagatesNativeMemoryKindFromProvider()
    {
        var provider = new SnapshotNativeProvider(new NativeFrameStatsSnapshot
        {
            Source = "native.test",
            Fps = 60,
            FrameTimeMsP50 = 16.7,
            FrameTimeMsP95 = 20.5,
            WorstFrameTimeMs = 24.1,
            NativeMemoryBytes = 42_000,
            NativeMemoryKind = "android.native-heap-allocated"
        });
        var collector = new RuntimeProfilerCollector(provider);

        collector.Start(100);
        var collected = collector.TryCollect(out var sample);
        collector.Stop();

        Assert.True(collected);
        Assert.Equal(42_000, sample.NativeMemoryBytes);
        Assert.Equal("android.native-heap-allocated", sample.NativeMemoryKind);
    }

    [Fact]
    public void RuntimeProfilerCollector_ReadsProviderMemoryWithoutAFrameBatch()
    {
        var collector = new RuntimeProfilerCollector(new MemoryOnlyNativeProvider());

        collector.Start(100);
        var collected = collector.TryCollect(out var sample);
        collector.Stop();

        Assert.True(collected);
        Assert.Equal(64_000, sample.NativeMemoryBytes);
        Assert.Equal("native.test-memory", sample.NativeMemoryKind);
        Assert.Equal("unavailable", sample.FrameSource);
    }

    [Fact]
    public void RuntimeProfilerCollector_SeparatesProcessFootprintFromNativeHeap()
    {
        var provider = new SnapshotNativeProvider(new NativeFrameStatsSnapshot
        {
            Source = "native.test",
            ProcessMemoryBytes = 128_000,
            ProcessMemoryKind = "windows.working-set"
        });
        var collector = new RuntimeProfilerCollector(provider);

        collector.Start(100);
        Assert.True(collector.TryCollect(out var sample));
        collector.Stop();

        Assert.Equal(128_000, sample.ProcessMemoryBytes);
        Assert.Equal("windows.working-set", sample.ProcessMemoryKind);
        Assert.Null(sample.NativeMemoryBytes);
        Assert.False(collector.GetCapabilities().NativeMemorySupported);
        Assert.True(collector.GetCapabilities().ProcessMemorySupported);
    }

    [Fact]
    public void ProfilerContractModels_StayAlignedWithDriverModels()
    {
        AssertCorePropertiesExistInDriver<ProfilerSessionInfo, Microsoft.Maui.DevFlow.Driver.ProfilerSessionInfo>();
        AssertCorePropertiesExistInDriver<ProfilerSample, Microsoft.Maui.DevFlow.Driver.ProfilerSample>();
        AssertCorePropertiesExistInDriver<ProfilerMarker, Microsoft.Maui.DevFlow.Driver.ProfilerMarker>();
        AssertCorePropertiesExistInDriver<ProfilerSpan, Microsoft.Maui.DevFlow.Driver.ProfilerSpan>();
        AssertCorePropertiesExistInDriver<ProfilerBatch, Microsoft.Maui.DevFlow.Driver.ProfilerBatch>();
        AssertCorePropertiesExistInDriver<ProfilerStreamReadMetadata, Microsoft.Maui.DevFlow.Driver.ProfilerStreamReadMetadata>();
        AssertCorePropertiesExistInDriver<ProfilerHotspot, Microsoft.Maui.DevFlow.Driver.ProfilerHotspot>();
        AssertCorePropertiesExistInDriver<ProfilerCapabilities, Microsoft.Maui.DevFlow.Driver.ProfilerCapabilities>();
    }

    [Fact]
    public void AppleTaskInfo_PhysFootprint_StructLayoutIsCorrect()
    {
        // This test validates that the P/Invoke struct layout for mach task_info
        // is correct on the current platform. It runs on macOS (same Mach kernel as iOS).
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsMacCatalyst() && !OperatingSystem.IsIOS())
        {
            // Skip on non-Apple platforms — the P/Invoke is Apple-only.
            return;
        }

        var info = new MachTaskVmInfoRev1();
        int count = Marshal.SizeOf<MachTaskVmInfoRev1>() / sizeof(int);
        int result = mach_task_info(mach_task_self(), 22, ref info, ref count);

        // Verify the syscall succeeded (KERN_SUCCESS = 0)
        Assert.Equal(0, result);

        // PhysFootprint must be > 0 for any running process
        Assert.True(info.PhysFootprint > 0, $"PhysFootprint was {info.PhysFootprint}, expected > 0");

        // Allocate 20MB of native memory and touch it to ensure it's paged in
        var allocSize = 20 * 1024 * 1024;
        IntPtr nativeAlloc = Marshal.AllocHGlobal(allocSize);
        try
        {
            for (int i = 0; i < allocSize; i += 4096)
                Marshal.WriteByte(nativeAlloc + i, 1);

            var info2 = new MachTaskVmInfoRev1();
            int count2 = Marshal.SizeOf<MachTaskVmInfoRev1>() / sizeof(int);
            int result2 = mach_task_info(mach_task_self(), 22, ref info2, ref count2);

            Assert.Equal(0, result2);

            // PhysFootprint should have grown by at least ~15MB (some overhead variance)
            var deltaBytes = (long)info2.PhysFootprint - (long)info.PhysFootprint;
            Assert.True(deltaBytes >= 15 * 1024 * 1024,
                $"PhysFootprint delta was {deltaBytes / 1024.0 / 1024.0:F1} MB after 20MB allocation, expected >= 15MB");
        }
        finally
        {
            Marshal.FreeHGlobal(nativeAlloc);
        }
    }

    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "mach_task_self")]
    static extern IntPtr mach_task_self();

    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "task_info")]
    static extern int mach_task_info(IntPtr targetTask, uint flavor, ref MachTaskVmInfoRev1 info, ref int count);

    [StructLayout(LayoutKind.Sequential)]
    struct MachTaskVmInfoRev1
    {
        public ulong VirtualSize;
        public int RegionCount;
        public int PageSize;
        public ulong ResidentSize;
        public ulong ResidentSizePeak;
        public ulong Device;
        public ulong DevicePeak;
        public ulong Internal;
        public ulong InternalPeak;
        public ulong External;
        public ulong ExternalPeak;
        public ulong Reusable;
        public ulong ReusablePeak;
        public ulong PurgeableVolatilePmap;
        public ulong PurgeableVolatileResident;
        public ulong PurgeableVolatileVirtual;
        public ulong Compressed;
        public ulong CompressedPeak;
        public ulong CompressedLifetime;
        public ulong PhysFootprint;
    }

    private static void AssertCorePropertiesExistInDriver<TCore, TDriver>(params string[] extraDriverProperties)
    {
        var coreProperties = typeof(TCore)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var driverProperties = typeof(TDriver)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Concat(extraDriverProperties)
            .ToHashSet(StringComparer.Ordinal);

        var missingInDriver = coreProperties
            .Where(coreProperty => !driverProperties.Contains(coreProperty))
            .OrderBy(name => name)
            .ToArray();

        Assert.True(
            missingInDriver.Length == 0,
            $"Driver contract {typeof(TDriver).Name} is missing properties: {string.Join(", ", missingInDriver)}");
    }

    private sealed class ThrowingNativeProvider : INativeFrameStatsProvider
    {
        public bool IsSupported => true;
        public bool ProvidesExactFrameTimings => true;
        public string Source => "native.test";
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public void Start()
        {
            StartCalls++;
            throw new InvalidOperationException("start failed");
        }

        public void Stop() => StopCalls++;

        public bool TryCollect(out NativeFrameStatsSnapshot snapshot)
        {
            snapshot = new NativeFrameStatsSnapshot();
            return false;
        }

        public void Dispose()
        {
        }
    }

    private sealed class SnapshotNativeProvider(NativeFrameStatsSnapshot snapshotToReturn) : INativeFrameStatsProvider
    {
        public bool IsSupported => true;
        public bool ProvidesExactFrameTimings => true;
        public bool ProvidesNativeMemory => snapshotToReturn.NativeMemoryBytes.HasValue;
        public bool ProvidesProcessMemory => snapshotToReturn.ProcessMemoryBytes.HasValue;
        public string Source => snapshotToReturn.Source;

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public bool TryCollect(out NativeFrameStatsSnapshot snapshot)
        {
            snapshot = new NativeFrameStatsSnapshot
            {
                Source = snapshotToReturn.Source,
                Fps = snapshotToReturn.Fps,
                FrameTimeMsP50 = snapshotToReturn.FrameTimeMsP50,
                FrameTimeMsP95 = snapshotToReturn.FrameTimeMsP95,
                WorstFrameTimeMs = snapshotToReturn.WorstFrameTimeMs,
                JankFrameCount = snapshotToReturn.JankFrameCount,
                UiThreadStallCount = snapshotToReturn.UiThreadStallCount,
                FrameDataLossCount = snapshotToReturn.FrameDataLossCount,
                NativeMemoryBytes = snapshotToReturn.NativeMemoryBytes,
                NativeMemoryKind = snapshotToReturn.NativeMemoryKind,
                ProcessMemoryBytes = snapshotToReturn.ProcessMemoryBytes,
                ProcessMemoryKind = snapshotToReturn.ProcessMemoryKind
            };
            return true;
        }

        public void Dispose()
        {
        }
    }

    private sealed class MemoryOnlyNativeProvider : INativeFrameStatsProvider
    {
        public bool IsSupported => true;
        public bool ProvidesExactFrameTimings => true;
        public bool ProvidesNativeMemory => true;
        public string Source => "native.test";
        public void Start() { }
        public void Stop() { }
        public bool TryCollect(out NativeFrameStatsSnapshot snapshot)
        {
            snapshot = new NativeFrameStatsSnapshot();
            return false;
        }
        public bool TryReadNativeMemory(out long bytes, out string kind)
        {
            bytes = 64_000;
            kind = "native.test-memory";
            return true;
        }
        public void Dispose() { }
    }
}
