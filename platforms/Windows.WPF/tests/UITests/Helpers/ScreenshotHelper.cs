using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace UITests.Helpers;

/// <summary>
/// Screenshot capture and comparison utilities.
/// Uses PrintWindow Win32 API for reliable window capture regardless of z-order.
/// </summary>
public static class ScreenshotHelper
{
    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy,
        IntPtr hdcSrc, int x1, int y1, uint rop);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    const uint PW_RENDERFULLCONTENT = 0x2;
    const uint SRCCOPY = 0x00CC0020;

    /// <summary>
    /// Capture a screenshot of a process's main window using PrintWindow.
    /// Works even when the window is partially obscured by other windows.
    /// Falls back to BitBlt for DirectComposition/WinUI windows.
    /// </summary>
    public static Bitmap CaptureWindow(Process proc)
    {
        proc.Refresh();
        var hwnd = proc.MainWindowHandle;
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("No main window handle");

        // Restore minimized windows first — they have off-screen coordinates
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
            Thread.Sleep(800);
            SetForegroundWindow(hwnd);
            Thread.Sleep(400);
        }

        if (!GetWindowRect(hwnd, out var rect))
            throw new InvalidOperationException("GetWindowRect failed");

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        // If window rect is unreasonably small or off-screen, try restore again
        if (width < 200 || height < 200 || rect.Left < -10000)
        {
            ShowWindow(hwnd, SW_RESTORE);
            Thread.Sleep(500);
            SetForegroundWindow(hwnd);
            Thread.Sleep(500);
            GetWindowRect(hwnd, out rect);
            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
        }

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"Window has invalid size: {width}x{height}");

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        var hdc = g.GetHdc();
        try
        {
            if (!PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT))
                PrintWindow(hwnd, hdc, 0);
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }

        // Check if capture is blank (all black) - happens with DirectComposition/WinUI windows
        if (IsCaptureBlank(bmp))
        {
            bmp.Dispose();
            return CaptureWindowFallback(hwnd, rect);
        }

        return bmp;
    }

    /// <summary>
    /// Check if a captured bitmap is blank (all black/transparent).
    /// </summary>
    static bool IsCaptureBlank(Bitmap bmp)
    {
        int nonBlank = 0;
        for (int x = 10; x < bmp.Width - 10; x += bmp.Width / 20 + 1)
        {
            for (int y = 10; y < bmp.Height - 10; y += bmp.Height / 20 + 1)
            {
                var px = bmp.GetPixel(x, y);
                if (px.A > 10 && (px.R > 5 || px.G > 5 || px.B > 5))
                    nonBlank++;
            }
        }
        return nonBlank < 5;
    }

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("dwmapi.dll")]
    static extern int DwmFlush();

    const int SW_MINIMIZE = 6;
    const int SW_RESTORE = 9;

    /// <summary>
    /// Fallback capture: bring window to foreground, wait for render, then BitBlt from screen.
    /// Used when PrintWindow returns blank (DirectComposition/WinUI windows).
    /// Minimizes other overlapping windows first to get a clean capture,
    /// but skips other ControlGallery windows to avoid side effects.
    /// </summary>
    static Bitmap CaptureWindowFallback(IntPtr hwnd, RECT rect)
    {
        // Bring target to foreground first (ensures it's restored and visible)
        SetForegroundWindow(hwnd);
        Thread.Sleep(400);

        // Re-read position now that window is in foreground
        GetWindowRect(hwnd, out rect);
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width < 200 || height < 200)
        {
            ShowWindow(hwnd, SW_RESTORE);
            Thread.Sleep(600);
            SetForegroundWindow(hwnd);
            Thread.Sleep(400);
            GetWindowRect(hwnd, out rect);
            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
        }

        // Minimize other overlapping non-ControlGallery windows
        var otherWindows = new List<IntPtr>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero && p.MainWindowHandle != hwnd)
                {
                    // Never minimize ControlGallery windows (WPF or WinUI)
                    if (p.ProcessName.Equals("ControlGallery", StringComparison.OrdinalIgnoreCase))
                        continue;

                    RECT other;
                    if (GetWindowRect(p.MainWindowHandle, out other))
                    {
                        // Check if it overlaps
                        if (other.Left < rect.Right && other.Right > rect.Left &&
                            other.Top < rect.Bottom && other.Bottom > rect.Top)
                        {
                            ShowWindow(p.MainWindowHandle, SW_MINIMIZE);
                            otherWindows.Add(p.MainWindowHandle);
                        }
                    }
                }
            }
            catch { }
        }

        // Bring target to foreground again after minimizing others
        SetForegroundWindow(hwnd);
        Thread.Sleep(400);
        try { DwmFlush(); } catch { }
        Thread.Sleep(200);

        // Final position read
        GetWindowRect(hwnd, out rect);
        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;

        // BitBlt from screen DC (window is now unoccluded)
        var screenDC = GetDC(IntPtr.Zero);
        var memDC = CreateCompatibleDC(screenDC);
        var hBitmap = CreateCompatibleBitmap(screenDC, width, height);
        var oldBitmap = SelectObject(memDC, hBitmap);

        BitBlt(memDC, 0, 0, width, height, screenDC, rect.Left, rect.Top, SRCCOPY);

        SelectObject(memDC, oldBitmap);
        var result = Image.FromHbitmap(hBitmap);
        DeleteObject(hBitmap);
        DeleteDC(memDC);
        ReleaseDC(IntPtr.Zero, screenDC);

        // Restore minimized windows
        foreach (var h in otherWindows)
        {
            try { ShowWindow(h, SW_RESTORE); } catch { }
        }

        return result;
    }

    /// <summary>
    /// Capture a window by bringing it to foreground and using BitBlt from screen DC.
    /// Required for WinUI apps where PrintWindow returns black (DirectComposition rendering).
    /// </summary>
    public static Bitmap CaptureWindowBitBlt(Process proc)
    {
        proc.Refresh();
        var hwnd = proc.MainWindowHandle;
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("No main window handle");

        SetForegroundWindow(hwnd);
        Thread.Sleep(500);

        if (!GetWindowRect(hwnd, out var rect))
            throw new InvalidOperationException("GetWindowRect failed");

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"Window has invalid size: {width}x{height}");

        var screenDC = GetDC(IntPtr.Zero);
        var memDC = CreateCompatibleDC(screenDC);
        var hBitmap = CreateCompatibleBitmap(screenDC, width, height);
        var oldBitmap = SelectObject(memDC, hBitmap);

        BitBlt(memDC, 0, 0, width, height, screenDC, rect.Left, rect.Top, SRCCOPY);

        SelectObject(memDC, oldBitmap);
        var bmp = Image.FromHbitmap(hBitmap);
        DeleteObject(hBitmap);
        DeleteDC(memDC);
        ReleaseDC(IntPtr.Zero, screenDC);

        return bmp;
    }

    /// <summary>
    /// Save a screenshot to disk.
    /// </summary>
    public static string SaveScreenshot(Bitmap bmp, string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{name}.png");
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    /// <summary>
    /// Capture and save a screenshot of a process's main window.
    /// </summary>
    public static string CaptureAndSave(Process proc, string directory, string name)
    {
        using var bmp = CaptureWindow(proc);
        return SaveScreenshot(bmp, directory, name);
    }

    /// <summary>
    /// Get the average color of a region (useful for verifying backgrounds).
    /// </summary>
    public static Color GetAverageColor(Bitmap bmp, Rectangle region)
    {
        long r = 0, g = 0, b = 0;
        int count = 0;

        var clampedRegion = Rectangle.Intersect(region, new Rectangle(0, 0, bmp.Width, bmp.Height));
        if (clampedRegion.Width <= 0 || clampedRegion.Height <= 0)
            return Color.Empty;

        for (int x = clampedRegion.Left; x < clampedRegion.Right; x += 2)
        {
            for (int y = clampedRegion.Top; y < clampedRegion.Bottom; y += 2)
            {
                var pixel = bmp.GetPixel(x, y);
                r += pixel.R;
                g += pixel.G;
                b += pixel.B;
                count++;
            }
        }

        if (count == 0) return Color.Empty;
        return Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
    }

    /// <summary>
    /// Check if a color is "dark" (brightness &lt; 128).
    /// </summary>
    public static bool IsDark(Color color)
    {
        return (color.R * 0.299 + color.G * 0.587 + color.B * 0.114) < 128;
    }

    /// <summary>
    /// Check if a color is "light" (brightness >= 128).
    /// </summary>
    public static bool IsLight(Color color)
    {
        return !IsDark(color);
    }

    /// <summary>
    /// Compare two screenshots and return the percentage of pixels that differ
    /// beyond a tolerance threshold.
    /// </summary>
    public static double CompareScreenshots(Bitmap a, Bitmap b, int tolerance = 30)
    {
        var width = Math.Min(a.Width, b.Width);
        var height = Math.Min(a.Height, b.Height);
        if (width <= 0 || height <= 0) return 1.0;

        int diffPixels = 0;
        int totalPixels = 0;

        for (int x = 0; x < width; x += 3)
        {
            for (int y = 0; y < height; y += 3)
            {
                var pa = a.GetPixel(x, y);
                var pb = b.GetPixel(x, y);
                var diff = Math.Abs(pa.R - pb.R) + Math.Abs(pa.G - pb.G) + Math.Abs(pa.B - pb.B);
                if (diff > tolerance)
                    diffPixels++;
                totalPixels++;
            }
        }

        return (double)diffPixels / totalPixels;
    }

    /// <summary>
    /// Sample the content area background color.
    /// With PrintWindow, coordinates are in window-local space.
    /// The flyout is ~320 DIP wide. Content starts to the right of it.
    /// Sample in the center of the content area to avoid headers/footers.
    /// </summary>
    public static Color SampleContentBackground(Bitmap bmp, int flyoutWidth = 480)
    {
        // Content area starts after the flyout. Sample its center.
        var contentX = flyoutWidth + (bmp.Width - flyoutWidth) / 2;
        var contentY = bmp.Height / 2;
        contentX = Math.Max(flyoutWidth + 50, Math.Min(contentX, bmp.Width - 100));
        contentY = Math.Max(100, Math.Min(contentY, bmp.Height - 100));
        var region = new Rectangle(contentX, contentY, Math.Min(100, bmp.Width - contentX), Math.Min(100, bmp.Height - contentY));
        return GetAverageColor(bmp, region);
    }
}
