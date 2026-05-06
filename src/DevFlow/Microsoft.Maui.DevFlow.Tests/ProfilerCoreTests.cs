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

        var items = ring.ReadAfter(0, 10, out var latestCursor);

        Assert.Equal(4, latestCursor);
        Assert.Equal(3, items.Count);
        Assert.Equal("m2", items[0].Name);
        Assert.Equal("m3", items[1].Name);
        Assert.Equal("m4", items[2].Name);
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
        Assert.StartsWith("managed.", sample1.FrameSource);
        Assert.StartsWith("estimated", sample1.FrameQuality);
        Assert.True(sample1.Fps > 0);
        Assert.True(sample1.FrameTimeMsP95 > 0);
        if (sample1.NativeMemoryBytes.HasValue)
            Assert.Equal("process.working-set-minus-managed", sample1.NativeMemoryKind);
        else
            Assert.Null(sample1.NativeMemoryKind);
        Assert.True(sample2.TsUtc > sample1.TsUtc);
    }

    [Fact]
    public void RuntimeProfilerCollector_InvalidFrameEstimate_UsesDefaultFrameEstimate()
    {
        var collector = new RuntimeProfilerCollector();
        collector.Start(100);
        SetPrivateField(collector, "_estimatedFrameTimeMs", 0d);

        var collected = collector.TryCollect(out var sample);
        collector.Stop();

        Assert.True(collected);
        Assert.True(sample.Fps > 0, $"Fps was {sample.Fps}, expected > 0");
        Assert.True(sample.FrameTimeMsP95 > 0, $"FrameTimeMsP95 was {sample.FrameTimeMsP95}, expected > 0");
        Assert.StartsWith("estimated.default-60hz", sample.FrameQuality);
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
    public void RuntimeProfilerCollector_WhenNativeProviderStartFails_CleansUpAndFallsBackToEstimated()
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
        Assert.StartsWith("managed.", sample.FrameSource);
        Assert.True(capabilities.FrameTimingsEstimated);
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
            NativeMemoryKind = "apple.phys-footprint"
        });
        var collector = new RuntimeProfilerCollector(provider);

        collector.Start(100);
        var collected = collector.TryCollect(out var sample);
        collector.Stop();

        Assert.True(collected);
        Assert.Equal(42_000, sample.NativeMemoryBytes);
        Assert.Equal("apple.phys-footprint", sample.NativeMemoryKind);
    }

    [Fact]
    public void ProfilerContractModels_StayAlignedWithDriverModels()
    {
        AssertCorePropertiesExistInDriver<ProfilerSessionInfo, Microsoft.Maui.DevFlow.Driver.ProfilerSessionInfo>();
        AssertCorePropertiesExistInDriver<ProfilerSample, Microsoft.Maui.DevFlow.Driver.ProfilerSample>();
        AssertCorePropertiesExistInDriver<ProfilerMarker, Microsoft.Maui.DevFlow.Driver.ProfilerMarker>();
        AssertCorePropertiesExistInDriver<ProfilerSpan, Microsoft.Maui.DevFlow.Driver.ProfilerSpan>();
        AssertCorePropertiesExistInDriver<ProfilerBatch, Microsoft.Maui.DevFlow.Driver.ProfilerBatch>();
        AssertCorePropertiesExistInDriver<ProfilerHotspot, Microsoft.Maui.DevFlow.Driver.ProfilerHotspot>();
        AssertCorePropertiesExistInDriver<ProfilerCapabilities, Microsoft.Maui.DevFlow.Driver.ProfilerCapabilities>("Available");
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

    private static void SetPrivateField<TValue>(object instance, string fieldName, TValue value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(instance, value);
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
                NativeMemoryBytes = snapshotToReturn.NativeMemoryBytes,
                NativeMemoryKind = snapshotToReturn.NativeMemoryKind
            };
            return true;
        }

        public void Dispose()
        {
        }
    }
}
