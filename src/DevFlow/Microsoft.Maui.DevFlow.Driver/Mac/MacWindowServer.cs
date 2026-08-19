using System.Runtime.InteropServices;
using static Microsoft.Maui.DevFlow.Driver.Mac.MacAccessibility;

namespace Microsoft.Maui.DevFlow.Driver.Mac;

internal static class MacWindowServer
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    private const uint WindowListOptionOnScreenOnly = 1;
    private const int CFNumberSInt32Type = 3;

    internal static int? TryGetWindowId(int processId)
    {
        var windows = CGWindowListCopyWindowInfo(WindowListOptionOnScreenOnly, 0);
        if (windows == 0)
            return null;

        var ownerPidKey = AXElement.CreateCFString("kCGWindowOwnerPID");
        var windowNumberKey = AXElement.CreateCFString("kCGWindowNumber");
        var windowLayerKey = AXElement.CreateCFString("kCGWindowLayer");

        try
        {
            var count = CFArrayGetCount(windows);
            for (long index = 0; index < count; index++)
            {
                var window = CFArrayGetValueAtIndex(windows, index);
                if (window == 0 ||
                    !TryGetInt(window, ownerPidKey, out var ownerPid) ||
                    ownerPid != processId ||
                    !TryGetInt(window, windowLayerKey, out var layer) ||
                    layer != 0 ||
                    !TryGetInt(window, windowNumberKey, out var windowId))
                {
                    continue;
                }

                return windowId;
            }

            return null;
        }
        finally
        {
            CFRelease(windowLayerKey);
            CFRelease(windowNumberKey);
            CFRelease(ownerPidKey);
            CFRelease(windows);
        }
    }

    private static bool TryGetInt(nint dictionary, nint key, out int value)
    {
        value = 0;
        var number = CFDictionaryGetValue(dictionary, key);
        return number != 0 && CFNumberGetValue(number, CFNumberSInt32Type, out value);
    }

    [DllImport(CoreGraphics)]
    private static extern nint CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
}
