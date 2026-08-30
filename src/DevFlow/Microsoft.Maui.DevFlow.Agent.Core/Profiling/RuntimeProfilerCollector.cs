using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.Core.Profiling;

public class RuntimeProfilerCollector : IProfilerCollector, IDisposable
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly INativeFrameStatsProvider? _nativeFrameStatsProvider;
    private readonly ProfilerCapabilities _capabilities;

    private bool _running;
    private bool _nativeFrameProviderActive;
    private DateTime _lastSampleTimestampUtc;
    private TimeSpan _lastCpuTime;

    public RuntimeProfilerCollector(INativeFrameStatsProvider? nativeFrameStatsProvider = null)
    {
        _nativeFrameStatsProvider = nativeFrameStatsProvider;
        _capabilities = new ProfilerCapabilities
        {
            Platform = GetPlatformName(),
            ManagedMemorySupported = true,
            NativeMemorySupported = _nativeFrameStatsProvider?.ProvidesNativeMemory == true,
            ProcessMemorySupported = true,
            GcSupported = true,
            CpuPercentSupported = true,
            ThreadCountSupported = true,
            FpsSupported = false,
            FrameTimingsEstimated = false,
            NativeFrameTimingsSupported = false,
            JankEventsSupported = false,
            UiThreadStallSupported = false
        };

        if (_nativeFrameStatsProvider?.IsSupported == true)
        {
            _capabilities.FpsSupported = true;
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

        _lastSampleTimestampUtc = DateTime.UtcNow;
        _nativeFrameProviderActive = false;

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
            _capabilities.ProcessMemorySupported = false;
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
                _nativeFrameProviderActive = true;
            }
            catch (Exception ex) when (IsNativeProviderAccessException(ex))
            {
                TryStopNativeProviderAfterStartupFailure();
                _nativeFrameProviderActive = false;
                _capabilities.FpsSupported = false;
                _capabilities.NativeFrameTimingsSupported = false;
                _capabilities.JankEventsSupported = false;
                _capabilities.UiThreadStallSupported = false;
                _capabilities.FrameTimingsEstimated = false;
            }
        }

        _running = true;
    }

    public void Stop()
    {
        _running = false;
        _nativeFrameProviderActive = false;
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
        if (_nativeFrameProviderActive &&
            _nativeFrameStatsProvider?.TryReadNativeMemory(out var providerBytes, out var providerKind) == true)
        {
            sample.NativeMemoryBytes = providerBytes;
            sample.NativeMemoryKind = providerKind;
        }
        if (_nativeFrameProviderActive &&
            _nativeFrameStatsProvider?.TryReadProcessMemory(
                out var processBytes,
                out var processKind) == true)
        {
            sample.ProcessMemoryBytes = processBytes;
            sample.ProcessMemoryKind = processKind;
        }
        else if (!sample.ProcessMemoryBytes.HasValue)
        {
            var processMemory = TryReadProcessMemory(processSnapshotAvailable);
            sample.ProcessMemoryBytes = processMemory.Bytes;
            sample.ProcessMemoryKind = processMemory.Kind;
        }
        sample.CpuPercent = cpuPercent;
        sample.ThreadCount = threadCount;

        _lastSampleTimestampUtc = now;
        return true;
    }

    public ProfilerCapabilities GetCapabilities() => _capabilities;

    private ProfilerSample BuildFrameSample(DateTime now)
    {
        if (_nativeFrameProviderActive && _nativeFrameStatsProvider is not null)
        {
            try
            {
                if (_nativeFrameStatsProvider.TryCollect(out var nativeSnapshot))
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
                        FrameDataLossCount = nativeSnapshot.FrameDataLossCount,
                        NativeMemoryBytes = nativeSnapshot.NativeMemoryBytes,
                        NativeMemoryKind = nativeSnapshot.NativeMemoryKind,
                        ProcessMemoryBytes = nativeSnapshot.ProcessMemoryBytes,
                        ProcessMemoryKind = nativeSnapshot.ProcessMemoryKind,
                        FrameSource = nativeSnapshot.Source,
                        FrameQuality = _nativeFrameStatsProvider.ProvidesExactFrameTimings
                            ? "native.exact"
                            : "native.cadence"
                    };
                }
            }
            catch (Exception ex) when (IsNativeProviderAccessException(ex))
            {
                _nativeFrameProviderActive = false;
                _capabilities.FpsSupported = false;
                _capabilities.FrameTimingsEstimated = false;
                _capabilities.NativeFrameTimingsSupported = false;
                _capabilities.JankEventsSupported = false;
                _capabilities.UiThreadStallSupported = false;
            }
        }

        return new ProfilerSample
        {
            TsUtc = now,
            FrameSource = "unavailable",
            FrameQuality = "unavailable"
        };
    }

    private bool TryRefreshProcessSnapshot()
    {
        if (!_capabilities.CpuPercentSupported
            && !_capabilities.ThreadCountSupported
            && !_capabilities.ProcessMemorySupported)
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
            _capabilities.ProcessMemorySupported = false;
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

    private (long? Bytes, string? Kind) TryReadProcessMemory(bool processSnapshotAvailable)
    {
        if (!_capabilities.ProcessMemorySupported || !processSnapshotAvailable)
            return (null, null);

        try
        {
            var workingSetBytes = _process.WorkingSet64;
            if (workingSetBytes <= 0)
                return (null, null);

            return (workingSetBytes, "process.working-set");
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            || ex is NotSupportedException
            || ex is PlatformNotSupportedException)
        {
            _capabilities.ProcessMemorySupported = false;
            return (null, null);
        }
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
