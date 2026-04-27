using System.Runtime.InteropServices;
using Foundation;
using AppKit;
using ObjCRuntime;
using Microsoft.Maui.Devices;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class DeviceInfoImplementation : IDeviceInfo
{
    [DllImport(Constants.SystemConfigurationLibrary)]
    static extern IntPtr SCDynamicStoreCopyComputerName(IntPtr store, IntPtr encoding);

    [DllImport(Constants.CoreFoundationLibrary)]
    static extern void CFRelease(IntPtr cf);

    public string Model
    {
        get
        {
            try
            {
                var pLen = IntPtr.Zero;
                var pStr = IntPtr.Zero;

                NativeMethods.sysctlbyname("hw.model", IntPtr.Zero, ref pLen, IntPtr.Zero, 0);
                if (pLen == IntPtr.Zero)
                    return string.Empty;

                pStr = Marshal.AllocHGlobal(pLen);
                NativeMethods.sysctlbyname("hw.model", pStr, ref pLen, IntPtr.Zero, 0);
                return Marshal.PtrToStringAnsi(pStr) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public string Manufacturer => "Apple";

    public string Name
    {
        get
        {
            var handle = SCDynamicStoreCopyComputerName(IntPtr.Zero, IntPtr.Zero);
            if (handle == IntPtr.Zero)
                return string.Empty;

            try
            {
#pragma warning disable CS0618
                return NSString.FromHandle(handle) ?? string.Empty;
#pragma warning restore CS0618
            }
            finally
            {
                CFRelease(handle);
            }
        }
    }

    public string VersionString
    {
        get
        {
            using var info = new NSProcessInfo();
            return info.OperatingSystemVersion.ToString();
        }
    }

    public Version Version => Utils.ParseVersion(VersionString);

    public DevicePlatform Platform => DevicePlatform.macOS;

    public DeviceIdiom Idiom => DeviceIdiom.Desktop;

    public DeviceType DeviceType => DeviceType.Physical;

    static class NativeMethods
    {
        [DllImport("libc")]
        public static extern int sysctlbyname(string property, IntPtr output, ref IntPtr oldLen, IntPtr newp, uint newlen);
    }
}
