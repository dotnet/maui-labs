using System.Runtime.InteropServices;

namespace Microsoft.Maui.DevFlow.Driver;

internal static class WindowsPngEncoder
{
    private const int PixelFormat32bppRgb = 0x22009;

    internal static byte[] Encode(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var stride = checked(width * 4);
        if (pixels.Length != checked(stride * height))
            throw new ArgumentException("Expected a complete, top-down 32-bit BGRA image.", nameof(pixels));
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("GDI+ PNG encoding requires Windows.");

        var file = Path.Combine(Path.GetTempPath(), $"devflow-gdi-{Environment.ProcessId}-{Guid.NewGuid():N}.png");
        var input = new StartupInput { Version = 1 };
        CheckStatus(GdiplusStartup(out var token, ref input, IntPtr.Zero), "startup");
        var bitmap = IntPtr.Zero;
        var pinned = default(GCHandle);
        try
        {
            pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            // GDI's unused alpha bytes must be ignored, not interpreted as transparency.
            CheckStatus(GdipCreateBitmapFromScan0(width, height, stride, PixelFormat32bppRgb,
                pinned.AddrOfPinnedObject(), out bitmap), "bitmap creation");
            var pngEncoder = new Guid("557cf406-1a04-11d3-9a73-0000f81ef32e");
            CheckStatus(GdipSaveImageToFile(bitmap, file, in pngEncoder, IntPtr.Zero), "PNG encoding");
            return File.ReadAllBytes(file);
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
            {
                var status = GdipDisposeImage(bitmap);
                if (status != 0)
                    Console.Error.WriteLine($"Warning: GDI+ image disposal failed (status {status}).");
            }
            if (pinned.IsAllocated)
                pinned.Free();
            GdiplusShutdown(token);
            ScreenshotImage.DeleteTemporaryFile(file);
        }
    }

    private static void CheckStatus(int status, string operation)
    {
        if (status != 0)
            throw new ExternalException($"GDI+ {operation} failed (status {status}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInput
    {
        public uint Version;
        public IntPtr DebugCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GdiplusStartup(out UIntPtr token, ref StartupInput input, IntPtr output);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void GdiplusShutdown(UIntPtr token);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GdipCreateBitmapFromScan0(
        int width, int height, int stride, int pixelFormat, IntPtr scan0, out IntPtr bitmap);

    [DllImport("gdiplus.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GdipSaveImageToFile(IntPtr image, string file, in Guid encoder, IntPtr parameters);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GdipDisposeImage(IntPtr image);
}
