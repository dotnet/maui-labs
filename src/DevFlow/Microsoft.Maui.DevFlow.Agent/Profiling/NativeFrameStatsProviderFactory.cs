using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.DevFlow.Agent.Core.Profiling;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
#if ANDROID
using global::Android.OS;
using global::Android.Views;
using Microsoft.Maui.Devices;
#endif
#if IOS || MACCATALYST || TVOS
using CoreAnimation;
using Foundation;
using Microsoft.Maui.Devices;
using System.Runtime.InteropServices;
#endif
#if WINDOWS
using Microsoft.Maui.Devices;
using Microsoft.UI.Xaml.Media;
#endif

namespace Microsoft.Maui.DevFlow.Agent.Profiling;

internal static class NativeFrameStatsProviderFactory
{
    public static INativeFrameStatsProvider? Create()
    {
#if ANDROID
        return AndroidFrameMetricsStatsProvider.IsApiSupported
            ? new AndroidFrameMetricsStatsProvider()
            : new AndroidChoreographerFrameStatsProvider();
#elif IOS || MACCATALYST || TVOS
        return new AppleDisplayLinkFrameStatsProvider();
#elif WINDOWS
        return new WindowsCompositionFrameStatsProvider();
#else
        return null;
#endif
    }
}

internal sealed class FrameStatsAccumulator
{
    private readonly object _gate = new();
    private readonly Queue<double> _durationsMs = new();
    private readonly double _jankThresholdMs;
    private readonly double _stallThresholdMs;
    private readonly int _maxBufferedFrames;

    public FrameStatsAccumulator(double frameBudgetMs, int maxBufferedFrames = 720)
    {
        _jankThresholdMs = Math.Max(16d, frameBudgetMs * 1.5d);
        _stallThresholdMs = 150d;
        _maxBufferedFrames = Math.Max(120, maxBufferedFrames);
    }

    public void Record(double durationMs)
    {
        if (durationMs <= 0d || double.IsNaN(durationMs) || double.IsInfinity(durationMs))
            return;

        lock (_gate)
        {
            _durationsMs.Enqueue(durationMs);
            if (_durationsMs.Count > _maxBufferedFrames)
                _durationsMs.Dequeue();
        }
    }

    public bool TryCreateSnapshot(string source, out NativeFrameStatsSnapshot snapshot)
    {
        List<double> data;
        lock (_gate)
        {
            if (_durationsMs.Count == 0)
            {
                snapshot = new NativeFrameStatsSnapshot();
                return false;
            }

            data = _durationsMs.ToList();
            _durationsMs.Clear();
        }

        data.Sort();
        var avg = data.Average();
        var p50 = Percentile(data, 0.50);
        var p95 = Percentile(data, 0.95);
        var worst = data[^1];

        snapshot = new NativeFrameStatsSnapshot
        {
            TsUtc = DateTime.UtcNow,
            Source = source,
            Fps = avg > 0d ? 1000d / avg : null,
            FrameTimeMsP50 = p50,
            FrameTimeMsP95 = p95,
            WorstFrameTimeMs = worst,
            JankFrameCount = data.Count(frame => frame >= _jankThresholdMs),
            UiThreadStallCount = data.Count(frame => frame >= _stallThresholdMs)
        };
        return true;
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
            return 0d;

        var clamped = Math.Clamp(percentile, 0d, 1d);
        var index = (int)Math.Ceiling(sorted.Count * clamped) - 1;
        index = Math.Clamp(index, 0, sorted.Count - 1);
        return sorted[index];
    }
}

#if ANDROID
internal sealed class AndroidFrameMetricsStatsProvider : Java.Lang.Object, INativeFrameStatsProvider, global::Android.Views.Window.IOnFrameMetricsAvailableListener
{
    private readonly FrameStatsAccumulator _accumulator;
    private WeakReference<global::Android.Views.Window>? _windowRef;
    private Handler? _frameMetricsHandler;
    private bool _running;

    public AndroidFrameMetricsStatsProvider()
    {
        var frameBudgetMs = ResolveFrameBudgetMs();
        _accumulator = new FrameStatsAccumulator(frameBudgetMs);
    }

    public static bool IsApiSupported => Build.VERSION.SdkInt >= BuildVersionCodes.N;
    public bool IsSupported => IsApiSupported;
    public bool ProvidesExactFrameTimings => true;
    public string Source => "native.android.framemetrics";

    public void Start()
    {
        if (_running)
            return;
        if (!IsSupported)
            throw new PlatformNotSupportedException("FrameMetrics requires Android API 24+.");

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                var window = activity?.Window;
                if (window == null)
                    return;

                var looper = Looper.MyLooper() ?? Looper.MainLooper;
                if (looper == null)
                    return;

                _frameMetricsHandler ??= new Handler(looper);
                _windowRef = new WeakReference<global::Android.Views.Window>(window);
                window.AddOnFrameMetricsAvailableListener(this, _frameMetricsHandler);
                _running = true;
            }
            catch
            {
            }
        });
    }

    public void Stop()
    {
        _running = false;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_windowRef?.TryGetTarget(out var window) == true)
                window.RemoveOnFrameMetricsAvailableListener(this);
            _windowRef = null;
            _frameMetricsHandler = null;
        });
    }

    public bool TryCollect(out NativeFrameStatsSnapshot snapshot)
    {
        if (!_accumulator.TryCreateSnapshot(Source, out snapshot))
            return false;

        snapshot.NativeMemoryBytes = TryReadAndroidNativeMemoryBytes();
        snapshot.NativeMemoryKind = snapshot.NativeMemoryBytes.HasValue
            ? "android.native-heap-allocated"
            : null;
        return true;
    }

    public void OnFrameMetricsAvailable(global::Android.Views.Window? window, FrameMetrics? frameMetrics, int dropCountSinceLastInvocation)
    {
        if (!_running || frameMetrics == null)
            return;

        var durationNs = frameMetrics.GetMetric((int)FrameMetricsId.TotalDuration);
        if (durationNs <= 0)
            return;

        _accumulator.Record(durationNs / 1_000_000d);
    }

    public new void Dispose()
    {
        Stop();
        base.Dispose();
    }

    private static long? TryReadAndroidNativeMemoryBytes()
    {
        try
        {
            return global::Android.OS.Debug.NativeHeapAllocatedSize;
        }
        catch
        {
            return null;
        }
    }

    private static double ResolveFrameBudgetMs()
    {
        try
        {
            var refreshRate = DeviceDisplay.Current.MainDisplayInfo.RefreshRate;
            if (refreshRate > 1d && !double.IsInfinity(refreshRate) && !double.IsNaN(refreshRate))
                return 1000d / refreshRate;
        }
        catch
        {
        }

        return 1000d / 60d;
    }
}

internal sealed class AndroidChoreographerFrameStatsProvider : Java.Lang.Object, INativeFrameStatsProvider, Choreographer.IFrameCallback
{
    private readonly FrameStatsAccumulator _accumulator;
    private bool _running;
    private long _lastFrameTimeNanos;

    public AndroidChoreographerFrameStatsProvider()
    {
        var frameBudgetMs = ResolveFrameBudgetMs();
        _accumulator = new FrameStatsAccumulator(frameBudgetMs);
    }

    public bool IsSupported => true;
    public bool ProvidesExactFrameTimings => false;
    public string Source => "native.android.choreographer";

    public void Start()
    {
        if (_running)
            return;

        _lastFrameTimeNanos = 0;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                Choreographer.Instance!.PostFrameCallback(this);
                _running = true;
            }
            catch
            {
            }
        });
    }

    public void Stop()
    {
        _running = false;
        MainThread.BeginInvokeOnMainThread(() => Choreographer.Instance!.RemoveFrameCallback(this));
    }

    public bool TryCollect(out NativeFrameStatsSnapshot snapshot)
    {
        if (!_accumulator.TryCreateSnapshot(Source, out snapshot))
            return false;

        snapshot.NativeMemoryBytes = TryReadAndroidNativeMemoryBytes();
        snapshot.NativeMemoryKind = snapshot.NativeMemoryBytes.HasValue
            ? "android.native-heap-allocated"
            : null;
        return true;
    }

    public void DoFrame(long frameTimeNanos)
    {
        if (!_running)
            return;

        if (_lastFrameTimeNanos > 0)
        {
            var durationMs = (frameTimeNanos - _lastFrameTimeNanos) / 1_000_000d;
            _accumulator.Record(durationMs);
        }

        _lastFrameTimeNanos = frameTimeNanos;
        Choreographer.Instance!.PostFrameCallback(this);
    }

    public new void Dispose()
    {
        Stop();
        base.Dispose();
    }

    private static double ResolveFrameBudgetMs()
    {
        try
        {
            var refreshRate = DeviceDisplay.Current.MainDisplayInfo.RefreshRate;
            if (refreshRate > 1d && !double.IsInfinity(refreshRate) && !double.IsNaN(refreshRate))
                return 1000d / refreshRate;
        }
        catch
        {
        }

        return 1000d / 60d;
    }

    private static long? TryReadAndroidNativeMemoryBytes()
    {
        try
        {
            return global::Android.OS.Debug.NativeHeapAllocatedSize;
        }
        catch
        {
            return null;
        }
    }
}
#endif

#if IOS || MACCATALYST || TVOS
internal sealed class AppleDisplayLinkFrameStatsProvider : INativeFrameStatsProvider
{
    private readonly FrameStatsAccumulator _accumulator;
    private CADisplayLink? _displayLink;
    private bool _running;
    private double _lastTimestampSeconds;

    public AppleDisplayLinkFrameStatsProvider()
    {
        var frameBudgetMs = ResolveFrameBudgetMs();
        _accumulator = new FrameStatsAccumulator(frameBudgetMs);
    }

    public bool IsSupported => true;
    public bool ProvidesExactFrameTimings => false;
    public string Source => "native.apple.cadisplaylink";

    public void Start()
    {
        if (_running)
            return;

        _lastTimestampSeconds = 0d;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                _displayLink = CADisplayLink.Create(OnTick);
                _displayLink.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
                _running = true;
            }
            catch
            {
            }
        });
    }

    public void Stop()
    {
        _running = false;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _displayLink?.Invalidate();
            _displayLink?.Dispose();
            _displayLink = null;
        });
    }

    public bool TryCollect(out NativeFrameStatsSnapshot snapshot)
    {
        if (!_accumulator.TryCreateSnapshot(Source, out snapshot))
            return false;

        snapshot.NativeMemoryBytes = TryReadPhysFootprint();
        snapshot.NativeMemoryKind = snapshot.NativeMemoryBytes.HasValue
            ? "apple.phys-footprint"
            : null;
        return true;
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnTick()
    {
        if (!_running || _displayLink == null)
            return;

        var ts = _displayLink.Timestamp;
        if (_lastTimestampSeconds > 0d)
        {
            var durationMs = (ts - _lastTimestampSeconds) * 1000d;
            _accumulator.Record(durationMs);
        }

        _lastTimestampSeconds = ts;
    }

    private static double ResolveFrameBudgetMs()
    {
        try
        {
            var refreshRate = DeviceDisplay.Current.MainDisplayInfo.RefreshRate;
            if (refreshRate > 1d && !double.IsInfinity(refreshRate) && !double.IsNaN(refreshRate))
                return 1000d / refreshRate;
        }
        catch
        {
        }

        return 1000d / 60d;
    }

    private static long? TryReadPhysFootprint()
    {
        try
        {
            var info = new MachTaskVmInfoRev1();
            int count = Marshal.SizeOf<MachTaskVmInfoRev1>() / sizeof(int);
            int result = mach_task_info(mach_task_self(), TASK_VM_INFO, ref info, ref count);
            if (result != 0 || info.PhysFootprint <= 0)
                return null;

            return (long)info.PhysFootprint;
        }
        catch
        {
            return null;
        }
    }

    const uint TASK_VM_INFO = 22;

    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "mach_task_self")]
    static extern IntPtr mach_task_self();

    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "task_info")]
    static extern int mach_task_info(IntPtr targetTask, uint flavor, ref MachTaskVmInfoRev1 info, ref int count);

    // Only fields up through phys_footprint (rev1). The kernel fills only what fits based on count,
    // so we don't need the full rev7 struct. This layout has been stable since iOS 7 / OS X 10.9.
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
}
#endif

#if WINDOWS
internal sealed class WindowsCompositionFrameStatsProvider : INativeFrameStatsProvider
{
    private readonly FrameStatsAccumulator _accumulator;
    private bool _running;
    private TimeSpan? _lastRenderingTime;

    public WindowsCompositionFrameStatsProvider()
    {
        var frameBudgetMs = ResolveFrameBudgetMs();
        _accumulator = new FrameStatsAccumulator(frameBudgetMs);
    }

    public bool IsSupported => true;
    public bool ProvidesExactFrameTimings => false;
    public string Source => "native.windows.compositiontarget";

    public void Start()
    {
        if (_running)
            return;

        _lastRenderingTime = null;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                CompositionTarget.Rendering += OnRendering;
                _running = true;
            }
            catch
            {
            }
        });
    }

    public void Stop()
    {
        _running = false;
        MainThread.BeginInvokeOnMainThread(() => CompositionTarget.Rendering -= OnRendering);
    }

    public bool TryCollect(out NativeFrameStatsSnapshot snapshot)
    {
        if (!_accumulator.TryCreateSnapshot(Source, out snapshot))
            return false;

        snapshot.NativeMemoryBytes = TryReadResidentMemoryBytes();
        snapshot.NativeMemoryKind = snapshot.NativeMemoryBytes.HasValue
            ? "windows.working-set"
            : null;
        return true;
    }

    public void Dispose() => Stop();

    private void OnRendering(object? sender, object args)
    {
        if (!_running || args is not RenderingEventArgs renderingArgs)
            return;

        if (_lastRenderingTime.HasValue)
        {
            var durationMs = (renderingArgs.RenderingTime - _lastRenderingTime.Value).TotalMilliseconds;
            _accumulator.Record(durationMs);
        }

        _lastRenderingTime = renderingArgs.RenderingTime;
    }

    private static long? TryReadResidentMemoryBytes()
    {
        try
        {
            return Process.GetCurrentProcess().WorkingSet64;
        }
        catch
        {
            return null;
        }
    }

    private static double ResolveFrameBudgetMs()
    {
        try
        {
            var refreshRate = DeviceDisplay.Current.MainDisplayInfo.RefreshRate;
            if (refreshRate > 1d && !double.IsInfinity(refreshRate) && !double.IsNaN(refreshRate))
                return 1000d / refreshRate;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            || ex is NotSupportedException
            || ex is PlatformNotSupportedException)
        {
        }

        return 1000d / 60d;
    }
}
#endif
