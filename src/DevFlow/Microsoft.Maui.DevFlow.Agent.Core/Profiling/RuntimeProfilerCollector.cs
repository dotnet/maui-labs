using System.Diagnostics;
using Microsoft.Maui.Devices;

namespace Microsoft.Maui.DevFlow.Agent.Core.Profiling;

public class RuntimeProfilerCollector : IProfilerCollector, IDisposable
{
    private const double FallbackRefreshRate = 60d;
    private const double FallbackFrameTimeMs = 1000d / FallbackRefreshRate;

    private readonly Process _process = Process.GetCurrentProcess();
    private readonly INativeFrameStatsProvider? _nativeFrameStatsProvider;
    private readonly ProfilerCapabilities _capabilities;

    private bool _running;
    private DateTime _lastSampleTimestampUtc;
    private TimeSpan _lastCpuTime;
    private int _sampleIntervalMs = 500;
    private double _estimatedFrameTimeMs = FallbackFrameTimeMs;
    private string _estimatedFrameQuality = "estimated.default-60hz";

    public RuntimeProfilerCollector(INativeFrameStatsProvider? nativeFrameStatsProvider = null)
    {
        _nativeFrameStatsProvider = nativeFrameStatsProvider;
        _capabilities = new ProfilerCapabilities
        {
            Platform = GetPlatformName(),
            ManagedMemorySupported = true,
            NativeMemorySupported = true,
            GcSupported = true,
            CpuPercentSupported = true,
            ThreadCountSupported = true,
            FpsSupported = true,
            FrameTimingsEstimated = true,
            NativeFrameTimingsSupported = false,
            JankEventsSupported = false,
            UiThreadStallSupported = false
        };

        if (_nativeFrameStatsProvider?.IsSupported == true)
        {
            _capabilities.NativeFrameTimingsSupported = true;
            _capabilities.JankEventsSupported = true;
            _capabilities.UiThreadStallSupported = true;
            _capabilities.FrameTimingsEstimated = !_nativeFrameStatsProvider.ProvidesExactFrameTimings;
        }
    }

    public void Start(int intervalMs)
    {
        if (intervalMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(intervalMs), "Sample interval must be > 0");

        _sampleIntervalMs = intervalMs;
        _lastSampleTimestampUtc = DateTime.UtcNow;
        if (_nativeFrameStatsProvider?.IsSupported == true)
        {
            // Native providers own frame timing. Keep a safe fallback estimate in case native collection is disabled later.
            _estimatedFrameTimeMs = FallbackFrameTimeMs;
            _estimatedFrameQuality = "estimated.default-60hz";
        }
        else
        {
            (_estimatedFrameTimeMs, _estimatedFrameQuality) = ResolveFrameEstimate();
        }

        try
        {
            _process.Refresh();
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            || ex is NotSupportedException
            || ex is PlatformNotSupportedException)
        {
            _capabilities.CpuPercentSupported = false;
            _capabilities.ThreadCountSupported = false;
            _capabilities.NativeMemorySupported = false;
        }

        if (_capabilities.CpuPercentSupported)
        {
            try
            {
                _lastCpuTime = _process.TotalProcessorTime;
            }
            catch (Exception ex) when (
                ex is InvalidOperationException
                || ex is NotSupportedException
                || ex is PlatformNotSupportedException)
            {
                _capabilities.CpuPercentSupported = false;
                _lastCpuTime = TimeSpan.Zero;
            }
        }

        if (_nativeFrameStatsProvider?.IsSupported == true)
        {
            try
            {
                _nativeFrameStatsProvider.Start();
            }
            catch (Exception ex) when (IsNativeProviderAccessException(ex))
            {
                TryStopNativeProviderAfterStartupFailure();
                _capabilities.NativeFrameTimingsSupported = false;
                _capabilities.JankEventsSupported = false;
                _capabilities.UiThreadStallSupported = false;
                _capabilities.FrameTimingsEstimated = true;
            }
        }

        _running = true;
    }

    public void Stop()
    {
        _running = false;
        _nativeFrameStatsProvider?.Stop();
    }

    public bool TryCollect(out ProfilerSample sample)
    {
        sample = new ProfilerSample();
        if (!_running)
            return false;

        var now = DateTime.UtcNow;
        var elapsedMs = Math.Max(1d, (now - _lastSampleTimestampUtc).TotalMilliseconds);
        var processSnapshotAvailable = TryRefreshProcessSnapshot();
        var cpuPercent = TryReadCpuPercent(elapsedMs, processSnapshotAvailable);
        var threadCount = TryReadThreadCount(processSnapshotAvailable);

        sample = BuildFrameSample(now);
        sample.ManagedBytes = GC.GetTotalMemory(false);
        sample.Gc0 = GC.CollectionCount(0);
        sample.Gc1 = GC.CollectionCount(1);
        sample.Gc2 = GC.CollectionCount(2);
        if (!sample.NativeMemoryBytes.HasValue)
        {
            var nativeMemory = TryReadNativeMemory(processSnapshotAvailable, sample.ManagedBytes);
            sample.NativeMemoryBytes = nativeMemory.Bytes;
            sample.NativeMemoryKind = nativeMemory.Kind;
        }
        sample.CpuPercent = cpuPercent;
        sample.ThreadCount = threadCount;

        _lastSampleTimestampUtc = now;
        return true;
    }

    public ProfilerCapabilities GetCapabilities() => _capabilities;

    private ProfilerSample BuildFrameSample(DateTime now)
    {
        if (_nativeFrameStatsProvider?.IsSupported == true
            && _nativeFrameStatsProvider.TryCollect(out var nativeSnapshot))
        {
            return new ProfilerSample
            {
                TsUtc = now,
                Fps = nativeSnapshot.Fps,
                FrameTimeMsP50 = nativeSnapshot.FrameTimeMsP50,
                FrameTimeMsP95 = nativeSnapshot.FrameTimeMsP95,
                WorstFrameTimeMs = nativeSnapshot.WorstFrameTimeMs,
                JankFrameCount = nativeSnapshot.JankFrameCount,
                UiThreadStallCount = nativeSnapshot.UiThreadStallCount,
                NativeMemoryBytes = nativeSnapshot.NativeMemoryBytes,
                NativeMemoryKind = nativeSnapshot.NativeMemoryKind,
                FrameSource = nativeSnapshot.Source,
                FrameQuality = _nativeFrameStatsProvider.ProvidesExactFrameTimings
                    ? "native.exact"
                    : "native.cadence"
            };
        }

        var elapsedMs = Math.Max(1d, (now - _lastSampleTimestampUtc).TotalMilliseconds);
        var effectiveElapsedMs = Math.Max(_sampleIntervalMs, elapsedMs);
        var lagRatio = effectiveElapsedMs / _sampleIntervalMs;
        var estimatedFrameTimeMs = _estimatedFrameTimeMs * lagRatio;
        var estimatedFrameQuality = _estimatedFrameQuality;
        if (!IsPositiveFinite(estimatedFrameTimeMs))
        {
            estimatedFrameTimeMs = FallbackFrameTimeMs;
            estimatedFrameQuality = "estimated.default-60hz";
        }

        var estimatedFps = 1000d / estimatedFrameTimeMs;

        return new ProfilerSample
        {
            TsUtc = now,
            Fps = estimatedFps,
            FrameTimeMsP50 = estimatedFrameTimeMs,
            FrameTimeMsP95 = estimatedFrameTimeMs,
            WorstFrameTimeMs = estimatedFrameTimeMs,
            JankFrameCount = estimatedFrameTimeMs >= 24d ? 1 : 0,
            UiThreadStallCount = estimatedFrameTimeMs >= 150d ? 1 : 0,
            FrameSource = "managed.estimated",
            FrameQuality = $"{estimatedFrameQuality}.sampling-lag"
        };
    }

    private bool TryRefreshProcessSnapshot()
    {
        if (!_capabilities.CpuPercentSupported
            && !_capabilities.ThreadCountSupported
            && !_capabilities.NativeMemorySupported)
            return false;

        try
        {
            _process.Refresh();
            return true;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            || ex is NotSupportedException
            || ex is PlatformNotSupportedException)
        {
            _capabilities.CpuPercentSupported = false;
            _capabilities.ThreadCountSupported = false;
            _capabilities.NativeMemorySupported = false;
            return false;
        }
    }

    private double? TryReadCpuPercent(double elapsedMs, bool processSnapshotAvailable)
    {
        if (!_capabilities.CpuPercentSupported || !processSnapshotAvailable)
            return null;

        try
        {
            var cpuTime = _process.TotalProcessorTime;
            var cpuDeltaMs = (cpuTime - _lastCpuTime).TotalMilliseconds;
            _lastCpuTime = cpuTime;

            if (cpuDeltaMs < 0)
                return null;

            var normalized = (cpuDeltaMs / (elapsedMs * Environment.ProcessorCount)) * 100d;
            return Math.Round(Math.Max(0d, normalized), 2);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            || ex is NotSupportedException
            || ex is PlatformNotSupportedException)
        {
            _capabilities.CpuPercentSupported = false;
            return null;
        }
    }

    private int? TryReadThreadCount(bool processSnapshotAvailable)
    {
        if (!_capabilities.ThreadCountSupported || !processSnapshotAvailable)
            return null;

        try
        {
            return _process.Threads.Count;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            || ex is NotSupportedException
            || ex is PlatformNotSupportedException)
        {
            _capabilities.ThreadCountSupported = false;
            return null;
        }
    }

    private (long? Bytes, string? Kind) TryReadNativeMemory(bool processSnapshotAvailable, long managedBytes)
    {
        if (!_capabilities.NativeMemorySupported || !processSnapshotAvailable)
            return (null, null);

        try
        {
            var workingSetBytes = _process.WorkingSet64;
            if (workingSetBytes <= 0)
                return (null, null);

            return (Math.Max(0L, workingSetBytes - managedBytes), "process.working-set-minus-managed");
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            || ex is NotSupportedException
            || ex is PlatformNotSupportedException)
        {
            _capabilities.NativeMemorySupported = false;
            return (null, null);
        }
    }

    private static (double FrameTimeMs, string Quality) ResolveFrameEstimate()
    {
        var refreshRate = TryReadDisplayRefreshRate();

        if (refreshRate.HasValue)
        {
            var frameTimeMs = 1000d / refreshRate.Value;
            if (IsPositiveFinite(frameTimeMs))
                return (frameTimeMs, "estimated.display-refresh");
        }

        return (FallbackFrameTimeMs, "estimated.default-60hz");
    }

    private static double? TryReadDisplayRefreshRate()
    {
        try
        {
            var refreshRate = DeviceDisplay.Current.MainDisplayInfo.RefreshRate;
            if (!IsPositiveFinite(refreshRate) || refreshRate <= 1d)
                return null;

            return refreshRate;
        }
        catch (Exception ex) when (IsDisplayInfoAccessException(ex))
        {
            return null;
        }
    }

    private static bool IsPositiveFinite(double value)
    {
        return !double.IsNaN(value)
            && !double.IsInfinity(value)
            && value > 0d;
    }

    private static bool IsDisplayInfoAccessException(Exception ex)
    {
        return ex is InvalidOperationException
            || ex is NotSupportedException
            || ex is PlatformNotSupportedException
            || ex.GetType().Name.Equals("UIKitThreadAccessException", StringComparison.Ordinal);
    }

    private void TryStopNativeProviderAfterStartupFailure()
    {
        if (_nativeFrameStatsProvider is null)
            return;

        try
        {
            _nativeFrameStatsProvider.Stop();
        }
        catch (Exception ex) when (IsNativeProviderAccessException(ex))
        {
        }
    }

    private static bool IsNativeProviderAccessException(Exception ex)
    {
        return ex is InvalidOperationException
            || ex is NotSupportedException
            || ex is PlatformNotSupportedException
            || ex is ObjectDisposedException;
    }

    private static string GetPlatformName()
    {
        if (OperatingSystem.IsAndroid()) return "Android";
        if (OperatingSystem.IsIOS()) return "iOS";
        if (OperatingSystem.IsMacCatalyst()) return "MacCatalyst";
        if (OperatingSystem.IsMacOS()) return "macOS";
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsLinux()) return "Linux";
        return "Unknown";
    }

    public void Dispose()
    {
        Stop();
        _nativeFrameStatsProvider?.Dispose();
    }
}
