using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

internal static class GtkTestWindow
{
	internal static void Resize(Gtk.Window window, int width, int height)
	{
		if (!OperatingSystem.IsWindows())
		{
			window.SetDefaultSize(width, height);
			return;
		}

		// Exercise real compositor allocation on Win32, as a user resize does.
		// GTK's Win32 SetDefaultSize does not resize this mapped runtime window.
		var surface = window.GetSurface()!;
		var hwnd = gdk_win32_surface_get_handle(surface.Handle.DangerousGetHandle());
		if (!GetWindowRect(hwnd, out var rect))
			throw new Win32Exception(Marshal.GetLastWin32Error());
		var scale = surface.GetScaleFactor();
		var nativeWidth = rect.Right - rect.Left + (width - window.GetAllocatedWidth()) * scale;
		var nativeHeight = rect.Bottom - rect.Top + (height - window.GetAllocatedHeight()) * scale;
		if (!SetWindowPos(hwnd, IntPtr.Zero, 0, 0, nativeWidth, nativeHeight, 0x0002 | 0x0004 | 0x0010))
			throw new Win32Exception(Marshal.GetLastWin32Error());
	}

	[DllImport("libgtk-4-1.dll")]
	static extern IntPtr gdk_win32_surface_get_handle(IntPtr surface);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

	[StructLayout(LayoutKind.Sequential)]
	struct NativeRect
	{
		public int Left, Top, Right, Bottom;
	}
}
