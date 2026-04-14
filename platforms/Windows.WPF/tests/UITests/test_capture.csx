using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

[DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
[DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
[DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
[DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr h);
[DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr h, int w, int ht);
[DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr h, IntPtr o);
[DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int cx, int cy, IntPtr s, int x1, int y1, uint r);
[DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr h);
[DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
[StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

// Find WinUI process
Process winui = null;
foreach (var p in Process.GetProcessesByName("ControlGallery")) {
    try { if (p.MainModule.FileName.Contains("davidortinau")) { p.Refresh(); winui = p; break; } } catch {}
}
if (winui == null) { Console.WriteLine("No WinUI"); return; }
Console.WriteLine($"WinUI PID={winui.Id} Handle={winui.MainWindowHandle}");

GetWindowRect(winui.MainWindowHandle, out var rect);
int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
Console.WriteLine($"Size: {w}x{h}");

// Method 1: PrintWindow with PW_RENDERFULLCONTENT (0x2)
var bmp1 = new Bitmap(w, h, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp1)) {
    var hdc = g.GetHdc();
    bool ok = PrintWindow(winui.MainWindowHandle, hdc, 0x2);
    g.ReleaseHdc(hdc);
    Console.WriteLine($"PrintWindow(0x2): {ok}");
}
// Check if blank
int nonBlack1 = 0;
for (int x = 0; x < w; x += 10) for (int y = 0; y < h; y += 10) {
    var px = bmp1.GetPixel(x, y);
    if (px.R > 5 || px.G > 5 || px.B > 5) nonBlack1++;
}
Console.WriteLine($"  Non-black pixels (sampled): {nonBlack1}");
bmp1.Save(@"D:\repos\rmarinho\maui.wpf\tests\UITests\Comparisons\test_pw_0x2.png", ImageFormat.Png);

// Method 2: PrintWindow with 0x0
var bmp2 = new Bitmap(w, h, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp2)) {
    var hdc = g.GetHdc();
    bool ok = PrintWindow(winui.MainWindowHandle, hdc, 0x0);
    g.ReleaseHdc(hdc);
    Console.WriteLine($"PrintWindow(0x0): {ok}");
}
int nonBlack2 = 0;
for (int x = 0; x < w; x += 10) for (int y = 0; y < h; y += 10) {
    var px = bmp2.GetPixel(x, y);
    if (px.R > 5 || px.G > 5 || px.B > 5) nonBlack2++;
}
Console.WriteLine($"  Non-black pixels (sampled): {nonBlack2}");
bmp2.Save(@"D:\repos\rmarinho\maui.wpf\tests\UITests\Comparisons\test_pw_0x0.png", ImageFormat.Png);

// Method 3: PrintWindow with PW_CLIENTONLY (0x1)
var bmp3 = new Bitmap(w, h, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp3)) {
    var hdc = g.GetHdc();
    bool ok = PrintWindow(winui.MainWindowHandle, hdc, 0x1);
    g.ReleaseHdc(hdc);
    Console.WriteLine($"PrintWindow(0x1): {ok}");
}
int nonBlack3 = 0;
for (int x = 0; x < w; x += 10) for (int y = 0; y < h; y += 10) {
    var px = bmp3.GetPixel(x, y);
    if (px.R > 5 || px.G > 5 || px.B > 5) nonBlack3++;
}
Console.WriteLine($"  Non-black pixels (sampled): {nonBlack3}");
bmp3.Save(@"D:\repos\rmarinho\maui.wpf\tests\UITests\Comparisons\test_pw_0x1.png", ImageFormat.Png);

// Method 4: PrintWindow with 0x3 (RENDERFULLCONTENT | CLIENTONLY)
var bmp4 = new Bitmap(w, h, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp4)) {
    var hdc = g.GetHdc();
    bool ok = PrintWindow(winui.MainWindowHandle, hdc, 0x3);
    g.ReleaseHdc(hdc);
    Console.WriteLine($"PrintWindow(0x3): {ok}");
}
int nonBlack4 = 0;
for (int x = 0; x < w; x += 10) for (int y = 0; y < h; y += 10) {
    var px = bmp4.GetPixel(x, y);
    if (px.R > 5 || px.G > 5 || px.B > 5) nonBlack4++;
}
Console.WriteLine($"  Non-black pixels (sampled): {nonBlack4}");
bmp4.Save(@"D:\repos\rmarinho\maui.wpf\tests\UITests\Comparisons\test_pw_0x3.png", ImageFormat.Png);
