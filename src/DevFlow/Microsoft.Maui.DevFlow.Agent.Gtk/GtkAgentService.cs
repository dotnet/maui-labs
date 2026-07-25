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

    protected override VisualTreeWalker CreateTreeWalker() => new GtkVisualTreeWalker();
    protected override IProfilerCollector CreateProfilerCollector() => new RuntimeProfilerCollector();

    protected override string PlatformName => "Linux";
    protected override string DeviceTypeName => "Virtual";
    protected override string IdiomName => "Desktop";

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

    protected override Task<byte[]?> CaptureElementScreenshotAsync(VisualElement element)
    {
        // Try the standard MAUI API first
        try
        {
            var result = VisualDiagnostics.CaptureAsPngAsync(element).GetAwaiter().GetResult();
            if (result != null) return Task.FromResult<byte[]?>(result);
        }
        catch { }

        // GTK4-specific fallback: capture the specific widget via WidgetPaintable
        try
        {
            if (element.Handler?.PlatformView is global::Gtk.Widget widget)
            {
                var pngBytes = CaptureGtkWidget(widget);
                if (pngBytes != null)
                    return Task.FromResult<byte[]?>(pngBytes);
            }
        }
        catch { }

        return Task.FromResult<byte[]?>(null);
    }

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
        try
        {
            if (rootElement.Handler?.PlatformView is global::Gtk.Widget widget)
            {
                var pngBytes = CaptureGtkWidget(widget);
                if (pngBytes != null) return pngBytes;
            }
        }
        catch { }

        // Final fallback: capture the main GTK window
        try
        {
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Handler?.PlatformView is global::Gtk.Window gtkWindow)
            {
                return CaptureGtkWindow(gtkWindow);
            }
        }
        catch { }

        return null;
    }

    private static byte[]? CaptureGtkWidget(global::Gtk.Widget widget)
    {
        try
        {
            var paintable = global::Gtk.WidgetPaintable.New(widget);
            var width = paintable.GetIntrinsicWidth();
            var height = paintable.GetIntrinsicHeight();

            if (width <= 0 || height <= 0) return null;

            var snapshot = global::Gtk.Snapshot.New();
            paintable.Snapshot(snapshot, width, height);
            var node = snapshot.ToNode();
            if (node == null) return null;

            var renderer = widget.GetNative()?.GetRenderer();
            if (renderer == null) return null;

            var texture = renderer.RenderTexture(node, null);
            if (texture == null) return null;

            var tmpPath = System.IO.Path.GetTempFileName() + ".png";
            try
            {
                texture.SaveToPng(tmpPath);
                return System.IO.File.ReadAllBytes(tmpPath);
            }
            finally
            {
                try { System.IO.File.Delete(tmpPath); } catch { }
            }
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? CaptureGtkWindow(global::Gtk.Window window)
    {
        return CaptureGtkWidget(window);
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

    protected override async Task<byte[]?> CaptureFullScreenAsync()
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
