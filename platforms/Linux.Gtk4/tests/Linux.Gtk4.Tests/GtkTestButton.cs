using System.Runtime.InteropServices;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

static class GtkTestButton
{
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	delegate void EmitSignal(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal);

	public static void Click(Gtk.Button button)
	{
		var library = NativeLibrary.Load(OperatingSystem.IsWindows()
			? "libgobject-2.0-0.dll"
			: OperatingSystem.IsMacOS() ? "libgobject-2.0.0.dylib" : "libgobject-2.0.so.0");
		try
		{
			var emit = Marshal.GetDelegateForFunctionPointer<EmitSignal>(
				NativeLibrary.GetExport(library, "g_signal_emit_by_name"));
			emit(button.Handle.DangerousGetHandle(), "clicked");
		}
		finally
		{
			NativeLibrary.Free(library);
		}
	}
}
