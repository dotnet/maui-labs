using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Profiling;

namespace Microsoft.Maui.DevFlow.Agent.Gtk;

/// <summary>
/// GTK-specific agent service with native tap and screenshot support for Linux/GTK.
/// </summary>
public class GtkAgentService : MauiDevFlowAgentService
{
    public GtkAgentService(AgentOptions? options = null) : base(options) { }

    internal GtkAgentService(
        AgentOptions? options,
        RegisteredNativeElementRegistry nativeElementRegistry,
        IDisposable nativeElementSubscription)
        : base(options, nativeElementRegistry, nativeElementSubscription)
    {
    }

    protected override VisualTreeWalker CreateTreeWalker()
        => NativeElementRegistry is null
            ? new GtkVisualTreeWalker()
            : new GtkVisualTreeWalker(NativeElementRegistry);
    protected override IProfilerCollector CreateProfilerCollector() => new RuntimeProfilerCollector();

    protected override string PlatformName => "Linux";
    protected override string DeviceTypeName => "Virtual";
    protected override string IdiomName => "Desktop";
    protected override bool SupportsNativeElementScreenshots => true;

    protected override double GetWindowDisplayDensity(IWindow? window)
    {
        try
        {
            // GTK4: get the scale factor from the native Gtk.Window's display/surface
            if (window?.Handler?.PlatformView is global::Gtk.Window gtkWindow)
            {
                var surface = gtkWindow.GetSurface();
                if (surface != null)
                    return surface.GetScaleFactor();
            }

            // Fallback: walk widget hierarchy to find the Gtk.Window
            if (window is Microsoft.Maui.Controls.Window mauiWindow)
            {
                if (mauiWindow.Page is Shell shell && shell.CurrentPage?.Handler?.PlatformView is global::Gtk.Widget cpWidget)
                {
                    var root = cpWidget.GetRoot();
                    if (root is global::Gtk.Widget rootWidget)
                        return rootWidget.GetScaleFactor();
                }
                if (mauiWindow.Page?.Handler?.PlatformView is global::Gtk.Widget pageWidget)
                    return pageWidget.GetScaleFactor();
            }
        }
        catch { }
        return 1.0;
    }

    protected override (double width, double height) GetNativeWindowSize(IWindow window)
    {
        try
        {
            if (window.Handler?.PlatformView is global::Gtk.Window gtkWindow)
                return (gtkWindow.GetWidth(), gtkWindow.GetHeight());

            // MAUI Window doesn't have a handler on GTK; find Gtk.Window via widget hierarchy
            if (window is Microsoft.Maui.Controls.Window mauiWindow)
            {
                // Try Shell's current page first
                if (mauiWindow.Page is Shell shell && shell.CurrentPage?.Handler?.PlatformView is global::Gtk.Widget cpWidget)
                {
                    var root = cpWidget.GetRoot();
                    if (root is global::Gtk.Window rootWin)
                        return (rootWin.GetWidth(), rootWin.GetHeight());
                }

                // Try page directly
                if (mauiWindow.Page?.Handler?.PlatformView is global::Gtk.Widget pageWidget)
                {
                    var root = pageWidget.GetRoot();
                    if (root is global::Gtk.Window rootWin)
                        return (rootWin.GetWidth(), rootWin.GetHeight());
                }
            }
        }
        catch { }
        return base.GetNativeWindowSize(window);
    }

    protected override Task<bool> TryNativeScroll(VisualElement element, double deltaX, double deltaY)
    {
        try
        {
            var target = element;
            while (target != null)
            {
                if (target.Handler?.PlatformView is global::Gtk.Widget widget)
                {
                    // Walk up GTK widget hierarchy looking for ScrolledWindow
                    var current = widget;
                    while (current != null)
                    {
                        if (current is global::Gtk.ScrolledWindow scrolledWindow)
                        {
                            var hAdj = scrolledWindow.GetHadjustment();
                            var vAdj = scrolledWindow.GetVadjustment();
                            if (hAdj != null && deltaX != 0)
                                hAdj.SetValue(Math.Max(hAdj.GetLower(), Math.Min(hAdj.GetValue() + deltaX, hAdj.GetUpper() - hAdj.GetPageSize())));
                            if (vAdj != null && deltaY != 0)
                                vAdj.SetValue(Math.Max(vAdj.GetLower(), Math.Min(vAdj.GetValue() - deltaY, vAdj.GetUpper() - vAdj.GetPageSize())));
                            return Task.FromResult(true);
                        }
                        current = current.GetParent() as global::Gtk.Widget;
                    }
                }
                target = target.Parent as VisualElement;
            }
        }
        catch { }
        return Task.FromResult(false);
    }

    protected override bool TryNativeTap(VisualElement ve)
    {
        try
        {
            var platformView = ve.Handler?.PlatformView;
            if (platformView == null) return false;

            if (platformView is global::Gtk.Button button)
            {
                button.Activate();
                return true;
            }

            if (platformView is global::Gtk.Widget widget)
            {
                widget.Activate();
                return true;
            }
        }
        catch { }
        return false;
    }

    protected override async Task<byte[]?> CaptureElementScreenshotAsync(VisualElement element)
    {
        // Try the standard MAUI API first
        try
        {
            var result = await VisualDiagnostics.CaptureAsPngAsync(element);
            if (result != null) return result;
        }
        catch { }

        // GTK4-specific fallback: capture the specific widget via WidgetPaintable
        if (element.Handler?.PlatformView is global::Gtk.Widget widget)
        {
            return await CaptureGtkWidgetAsync(widget);
        }

        return null;
    }

    protected override Task<byte[]?> CaptureNativeElementScreenshotAsync(
        object nativeElement,
        ElementInfo? elementInfo)
        => DispatchAsync<byte[]>(() => nativeElement is global::Gtk.Widget widget
            ? CaptureGtkWidgetAsync(widget)
            : Task.FromResult<byte[]?>(null));

    protected override async Task<byte[]?> CaptureScreenshotAsync(VisualElement rootElement)
    {
        // Try the standard MAUI API first
        try
        {
            var result = await VisualDiagnostics.CaptureAsPngAsync(rootElement);
            if (result != null) return result;
        }
        catch { }

        // GTK4-specific fallback: capture the rootElement's native widget directly
        if (rootElement.Handler?.PlatformView is global::Gtk.Widget widget)
        {
            var pngBytes = await CaptureGtkWidgetAsync(widget);
            if (pngBytes != null) return pngBytes;
        }

        // Final fallback: capture the main GTK window
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is global::Gtk.Window gtkWindow)
        {
            return await CaptureGtkWidgetAsync(gtkWindow);
        }

        return null;
    }

    private static async Task<byte[]?> CaptureGtkWidgetAsync(global::Gtk.Widget widget)
    {
        using var paintable = global::Gtk.WidgetPaintable.New(widget);
        try
        {
            var width = paintable.GetIntrinsicWidth();
            var height = paintable.GetIntrinsicHeight();

            if (width <= 0 || height <= 0) return null;

            Gsk.RenderNode? node = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                using (var snapshot = global::Gtk.Snapshot.New())
                {
                    paintable.Snapshot(snapshot, width, height);
                    node = snapshot.ToNode();
                }
                if (node != null)
                    break;

                // A newly observed widget may have no render node until GTK draws a frame
                // and delivers its deferred image update. Keep this same paintable attached.
                if (attempt == 4 || !await WaitForGtkFrameAsync(widget))
                    return null;
                width = paintable.GetIntrinsicWidth();
                height = paintable.GetIntrinsicHeight();
                if (width <= 0 || height <= 0)
                    return null;
            }
            if (node == null) return null;

            try
            {
                var renderer = widget.GetNative()?.GetRenderer();
                if (renderer == null) return null;

                using var texture = renderer.RenderTexture(node, null);
                if (texture == null) return null;

                using var png = texture.SaveToPngBytes();
                return png.GetRegionSpan<byte>(0, png.GetSize()).ToArray();
            }
            finally
            {
                node.Unref();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Microsoft.Maui.DevFlow] GTK screenshot failed: {ex}");
            throw;
        }
        finally
        {
            // GTK queues paintable image updates with an unowned pointer. Detach while the
            // wrapper is still live to cancel that idle before releasing the toggle reference.
            paintable.SetWidget(null);
        }
    }

    private static async Task<bool> WaitForGtkFrameAsync(global::Gtk.Widget widget)
    {
        var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tickId = widget.AddTickCallback((_, _) =>
        {
            frame.TrySetResult();
            return true;
        });
        try
        {
            widget.QueueDraw();
            await Task.WhenAny(frame.Task, Task.Delay(100));
            return frame.Task.IsCompletedSuccessfully;
        }
        finally
        {
            widget.RemoveTickCallback(tickId);
        }
    }

    protected override void TryNativeResize(IWindow window, int width, int height)
    {
        if (window.Handler?.PlatformView is global::Gtk.Window gtkWindow)
        {
            gtkWindow.SetDefaultSize(width, height);
        }
        else
        {
            base.TryNativeResize(window, width, height);
        }
    }

    protected override async Task<byte[]?> CaptureFullScreenAsync(int? windowIndex = null)
    {
        // Use XDG Desktop Portal Screenshot via DBUS to capture the full screen
        // including all windows (modal dialogs, popups, etc.)
        try
        {
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "gdbus";
            process.StartInfo.Arguments = "call --session --dest org.freedesktop.portal.Desktop " +
                "--object-path /org/freedesktop/portal/desktop " +
                "--method org.freedesktop.portal.Screenshot.Screenshot \"\" \"{'interactive': <false>}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.Start();

            await process.WaitForExitAsync();
            if (process.ExitCode != 0) return null;

            // Wait briefly for the screenshot file to be written
            await Task.Delay(500);

            // Find the most recent screenshot in ~/Pictures/
            var picturesDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            if (!System.IO.Directory.Exists(picturesDir))
                picturesDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");

            if (!System.IO.Directory.Exists(picturesDir)) return null;

            var screenshots = System.IO.Directory.GetFiles(picturesDir, "Screenshot*.png")
                .OrderByDescending(f => System.IO.File.GetLastWriteTimeUtc(f))
                .FirstOrDefault();

            if (screenshots == null) return null;

            // Only use if it was created very recently (within last 5 seconds)
            var fileTime = System.IO.File.GetLastWriteTimeUtc(screenshots);
            if ((DateTime.UtcNow - fileTime).TotalSeconds > 5) return null;

            return await System.IO.File.ReadAllBytesAsync(screenshots);
        }
        catch
        {
            return null;
        }
    }
}
