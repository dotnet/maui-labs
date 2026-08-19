using System.Runtime.InteropServices;

namespace Microsoft.Maui.DevFlow.Driver.Mac;

/// <summary>
/// P/Invoke declarations for macOS Accessibility (AXUIElement) and CoreFoundation APIs.
/// Only usable on macOS — guard all calls with OperatingSystem.IsMacOS().
/// </summary>
internal static class MacAccessibility
{
    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundationLib =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    internal const int kCFStringEncodingUTF8 = 0x08000100;

    // AXError values
    internal const int kAXErrorSuccess = 0;
    internal const int kAXErrorAttributeUnsupported = -25205;
    internal const int kAXErrorNoValue = -25212;
    internal const int kAXErrorCannotComplete = -25204;

    // --- AXUIElement ---

    [DllImport(ApplicationServices)]
    internal static extern nint AXUIElementCreateApplication(int pid);

    [DllImport(ApplicationServices)]
    internal static extern nint AXUIElementCreateSystemWide();

    [DllImport(ApplicationServices)]
    internal static extern int AXUIElementCopyAttributeValue(nint element, nint attribute, out nint value);

    [DllImport(ApplicationServices)]
    internal static extern int AXUIElementCopyAttributeNames(nint element, out nint names);

    [DllImport(ApplicationServices)]
    internal static extern int AXUIElementPerformAction(nint element, nint action);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool AXIsProcessTrusted();

    // --- CoreFoundation ---

    [DllImport(CoreFoundationLib)]
    internal static extern nint CFStringCreateWithCString(nint alloc, string cStr, int encoding);

    [DllImport(CoreFoundationLib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool CFStringGetCString(nint theString, nint buffer, long bufferSize, int encoding);

    [DllImport(CoreFoundationLib)]
    internal static extern long CFStringGetLength(nint theString);

    [DllImport(CoreFoundationLib)]
    internal static extern long CFArrayGetCount(nint theArray);

    [DllImport(CoreFoundationLib)]
    internal static extern nint CFArrayGetValueAtIndex(nint theArray, long idx);

    [DllImport(CoreFoundationLib)]
    internal static extern nint CFDictionaryGetValue(nint theDictionary, nint key);

    [DllImport(CoreFoundationLib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool CFNumberGetValue(nint number, int numberType, out int value);

    [DllImport(CoreFoundationLib)]
    internal static extern void CFRelease(nint cf);

    [DllImport(CoreFoundationLib)]
    internal static extern nint CFRetain(nint cf);

    [DllImport(CoreFoundationLib)]
    internal static extern uint CFGetTypeID(nint cf);

    [DllImport(CoreFoundationLib)]
    internal static extern uint CFStringGetTypeID();

    [DllImport(CoreFoundationLib)]
    internal static extern uint CFArrayGetTypeID();

    [DllImport(CoreFoundationLib)]
    internal static extern uint CFBooleanGetTypeID();

    [DllImport(CoreFoundationLib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool CFBooleanGetValue(nint boolean);

    [DllImport(CoreFoundationLib)]
    internal static extern uint CFNumberGetTypeID();
}
