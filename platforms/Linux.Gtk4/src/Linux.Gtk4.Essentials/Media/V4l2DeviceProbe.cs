using System.Runtime.InteropServices;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Media;

internal static class V4l2DeviceProbe
{
	internal static bool CanCapture(string path)
	{
		// Match v4l2src's read/write access without blocking or inheriting the descriptor.
		var descriptor = open(path, 2 | 0x800 | 0x80000); // O_RDWR | O_NONBLOCK | O_CLOEXEC
		if (descriptor < 0)
			return false;
		try
		{
			return ioctl(descriptor, 0x80685600, out var capability) == 0 // VIDIOC_QUERYCAP
				&& HasCaptureCapability(capability.Capabilities, capability.DeviceCapabilities);
		}
		finally
		{
			close(descriptor);
		}
	}

	internal static bool HasCaptureCapability(uint capabilities, uint deviceCapabilities)
	{
		const uint deviceCaps = 0x80000000;
		const uint videoCapture = 0x00000001;
		const uint videoCaptureMplane = 0x00001000;
		const uint streaming = 0x04000000;
		const uint readWrite = 0x01000000;
		var effective = (capabilities & deviceCaps) != 0 ? deviceCapabilities : capabilities;
		return (effective & (videoCapture | videoCaptureMplane)) != 0
			&& (effective & (streaming | readWrite)) != 0;
	}

	// Linux uapi/linux/videodev2.h: v4l2_capability is 104 bytes on x64 and arm64.
	[StructLayout(LayoutKind.Explicit, Size = 104)]
	struct Capability
	{
		[FieldOffset(84)] internal uint Capabilities;
		[FieldOffset(88)] internal uint DeviceCapabilities;
	}

	[DllImport("libc", SetLastError = true)]
	static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

	[DllImport("libc", SetLastError = true)]
	static extern int ioctl(int descriptor, nuint request, out Capability capability);

	[DllImport("libc")]
	static extern int close(int descriptor);
}
