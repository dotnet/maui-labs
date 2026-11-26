#if WINDOWS_BUILD
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Microsoft.Maui.DevFlow.Driver.Windows;

// Minimal source-generated COM declarations for the subset of Windows UI Automation (UIA) the Driver
// uses. They replace the TLB-generated Interop.UIAutomationClient assembly, whose [ComImport] types
// need built-in COM interop and therefore do not work under NativeAOT.
//
// IMPORTANT: [GeneratedComInterface] assigns vtable slots strictly in declaration order (after the
// three implicit IUnknown slots), so every interface declares ALL methods up to the last one that is
// called, in the exact order of the native vtable. Methods that are never called are declared only to
// occupy their slot (as PreserveSig/no-arg placeholders) and must not be invoked or reordered.
// The order and IIDs were taken from the Interop.UIAutomationClient 10.19041.0 metadata (tlbimp keeps
// vtable order) and cross-checked against the Windows SDK uiautomationclient.h.

internal static class UIAutomationClsids
{
    // CLSID_CUIAutomation
    public static readonly Guid CUIAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");

    // IID_IUIAutomation (kept in sync with the [Guid] on IUIAutomation)
    public static readonly Guid IUIAutomation = new("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE");
}

[Flags]
internal enum TreeScope
{
    TreeScope_None = 0,
    TreeScope_Element = 1,
    TreeScope_Children = 2,
    TreeScope_Descendants = 4,
    TreeScope_Parent = 8,
    TreeScope_Ancestors = 16,
    TreeScope_Subtree = 7,
}

internal enum ScrollAmount
{
    ScrollAmount_LargeDecrement = 0,
    ScrollAmount_SmallDecrement = 1,
    ScrollAmount_NoAmount = 2,
    ScrollAmount_LargeIncrement = 3,
    ScrollAmount_SmallIncrement = 4,
}

// Win32 RECT.
[StructLayout(LayoutKind.Sequential)]
internal struct UiaNativeRect
{
    public int left;
    public int top;
    public int right;
    public int bottom;
}

// Blittable VARIANT. The native union contains BRECORD (two pointers), so VARIANT is 24 bytes on
// 64-bit and 16 on 32-bit; the trailing IntPtr reproduces that size and the by-value calling
// convention. Only VT_I4 is ever produced.
[StructLayout(LayoutKind.Sequential)]
internal struct UiaVariant
{
    private const ushort VT_I4 = 3;

    private ushort vt;
    private ushort wReserved1;
    private ushort wReserved2;
    private ushort wReserved3;
    private int lVal;
    private IntPtr recordPadding;

    public static UiaVariant FromInt32(int value) => new() { vt = VT_I4, lVal = value };
}

[GeneratedComInterface]
[Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE")]
internal partial interface IUIAutomation
{
    // 3
    [PreserveSig] int CompareElements();
    // 4
    [PreserveSig] int CompareRuntimeIds();
    // 5
    IUIAutomationElement GetRootElement();
    // 6
    IUIAutomationElement ElementFromHandle(nint hwnd);
    // 7-15
    [PreserveSig] int ElementFromPoint();
    [PreserveSig] int GetFocusedElement();
    [PreserveSig] int GetRootElementBuildCache();
    [PreserveSig] int ElementFromHandleBuildCache();
    [PreserveSig] int ElementFromPointBuildCache();
    [PreserveSig] int GetFocusedElementBuildCache();
    [PreserveSig] int CreateTreeWalker();
    [PreserveSig] int GetControlViewWalker();
    [PreserveSig] int GetContentViewWalker();
    // 16
    IUIAutomationTreeWalker GetRawViewWalker();
    // 17-20
    [PreserveSig] int GetRawViewCondition();
    [PreserveSig] int GetControlViewCondition();
    [PreserveSig] int GetContentViewCondition();
    [PreserveSig] int CreateCacheRequest();
    // 21
    IUIAutomationCondition CreateTrueCondition();
    // 22
    [PreserveSig] int CreateFalseCondition();
    // 23
    IUIAutomationCondition CreatePropertyCondition(int propertyId, UiaVariant value);
}

[GeneratedComInterface]
[Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E")]
internal partial interface IUIAutomationElement
{
    // 3
    void SetFocus();
    // 4-5
    [PreserveSig] int GetRuntimeId();
    [PreserveSig] int FindFirst();
    // 6
    IUIAutomationElementArray FindAll(TreeScope scope, IUIAutomationCondition condition);
    // 7-15
    [PreserveSig] int FindFirstBuildCache();
    [PreserveSig] int FindAllBuildCache();
    [PreserveSig] int BuildUpdatedCache();
    [PreserveSig] int GetCurrentPropertyValue();
    [PreserveSig] int GetCurrentPropertyValueEx();
    [PreserveSig] int GetCachedPropertyValue();
    [PreserveSig] int GetCachedPropertyValueEx();
    [PreserveSig] int GetCurrentPatternAs();
    [PreserveSig] int GetCachedPatternAs();
    // 16: returns an IUnknown* (the pattern object, or null when unsupported); callers cast it
    // to the requested pattern interface.
    nint GetCurrentPattern(int patternId);
    // 17-19
    [PreserveSig] int GetCachedPattern();
    [PreserveSig] int GetCachedParent();
    [PreserveSig] int GetCachedChildren();
    // 20
    int GetCurrentProcessId();
    // 21
    int GetCurrentControlType();
    // 22
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetCurrentLocalizedControlType();
    // 23
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetCurrentName();
    // 24-27
    [PreserveSig] int GetCurrentAcceleratorKey();
    [PreserveSig] int GetCurrentAccessKey();
    [PreserveSig] int GetCurrentHasKeyboardFocus();
    [PreserveSig] int GetCurrentIsKeyboardFocusable();
    // 28
    int GetCurrentIsEnabled();
    // 29
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetCurrentAutomationId();
    // 30-35
    [PreserveSig] int GetCurrentClassName();
    [PreserveSig] int GetCurrentHelpText();
    [PreserveSig] int GetCurrentCulture();
    [PreserveSig] int GetCurrentIsControlElement();
    [PreserveSig] int GetCurrentIsContentElement();
    [PreserveSig] int GetCurrentIsPassword();
    // 36
    nint GetCurrentNativeWindowHandle();
    // 37
    [PreserveSig] int GetCurrentItemType();
    // 38
    int GetCurrentIsOffscreen();
    // 39-42
    [PreserveSig] int GetCurrentOrientation();
    [PreserveSig] int GetCurrentFrameworkId();
    [PreserveSig] int GetCurrentIsRequiredForForm();
    [PreserveSig] int GetCurrentItemStatus();
    // 43
    UiaNativeRect GetCurrentBoundingRectangle();
}

[GeneratedComInterface]
[Guid("14314595-B4BC-4055-95F2-58F2E42C9855")]
internal partial interface IUIAutomationElementArray
{
    // 3
    int GetLength();
    // 4
    IUIAutomationElement GetElement(int index);
}

[GeneratedComInterface]
[Guid("352FFBA8-0973-437C-A61F-F64CAFD81DF9")]
internal partial interface IUIAutomationCondition
{
}

[GeneratedComInterface]
[Guid("4042C624-389C-4AFC-A630-9DF854A541FC")]
internal partial interface IUIAutomationTreeWalker
{
    // 3
    [PreserveSig] int GetParentElement();
    // 4
    IUIAutomationElement? GetFirstChildElement(IUIAutomationElement element);
    // 5
    [PreserveSig] int GetLastChildElement();
    // 6
    IUIAutomationElement? GetNextSiblingElement(IUIAutomationElement element);
}

[GeneratedComInterface]
[Guid("FB377FBE-8EA6-46D5-9C73-6499642D3059")]
internal partial interface IUIAutomationInvokePattern
{
    // 3
    void Invoke();
}

[GeneratedComInterface]
[Guid("A94CD8B1-0844-4CD6-9D2D-640537AB39E9")]
internal partial interface IUIAutomationValuePattern
{
    // 3
    void SetValue([MarshalAs(UnmanagedType.BStr)] string value);
    // 4
    [PreserveSig] int GetCurrentValue();
    // 5
    int GetCurrentIsReadOnly();
}

[GeneratedComInterface]
[Guid("88F4D42A-E881-459D-A77C-73BBBB7E02DC")]
internal partial interface IUIAutomationScrollPattern
{
    // 3
    void Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount);
}

[GeneratedComInterface]
[Guid("B488300F-D015-4F19-9C29-BB595E3645EF")]
internal partial interface IUIAutomationScrollItemPattern
{
    // 3
    void ScrollIntoView();
}
#endif
