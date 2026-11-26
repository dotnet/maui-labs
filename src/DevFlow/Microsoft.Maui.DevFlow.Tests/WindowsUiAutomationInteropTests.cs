#if WINDOWS_BUILD
using System.Runtime.InteropServices;
using Microsoft.Maui.DevFlow.Driver.Windows;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Exercises the hand-written, source-generated UI Automation COM interfaces against the real
/// Windows UIA runtime. A wrong vtable slot or IID would otherwise only surface on a Windows
/// NativeAOT build, and the UIAutomationInterop wrappers swallow exceptions, so these tests assert
/// on returned values rather than only on "does not throw".
/// </summary>
public class WindowsUiAutomationInteropTests
{
    private const int UIA_PaneControlTypeId = 50033;
    private const int UIA_WindowControlTypeId = 50032;

    [Fact]
    public void DesktopElement_ReportsPaneWithBoundsAndStrings()
    {
        var desktop = UIAutomationInterop.ElementFromHandle(NativeMethods.GetDesktopWindow());

        Assert.NotNull(desktop);
        Assert.Equal(UIA_PaneControlTypeId, UIAutomationInterop.GetControlType(desktop!));
        Assert.False(string.IsNullOrEmpty(UIAutomationInterop.GetLocalizedControlType(desktop!)));
        Assert.NotNull(UIAutomationInterop.GetBoundingRectangle(desktop!));
        Assert.True(UIAutomationInterop.IsEnabled(desktop!));
        _ = UIAutomationInterop.GetName(desktop!);
        _ = UIAutomationInterop.GetAutomationId(desktop!);
        _ = UIAutomationInterop.GetProcessId(desktop!);
        _ = UIAutomationInterop.GetNativeWindowHandle(desktop!);
        _ = UIAutomationInterop.IsOffscreen(desktop!);
    }

    [Fact]
    public void FindTopLevelWindows_ReturnsOnlyWindowControlTypes()
    {
        var windows = UIAutomationInterop.FindTopLevelWindows();

        Assert.NotNull(windows);
        foreach (var window in windows)
        {
            Assert.Equal(UIA_WindowControlTypeId, UIAutomationInterop.GetControlType(window));
        }
    }

    [Fact]
    public void DumpTree_DesktopWithSmallDepth_ReturnsNonEmptyText()
    {
        var desktop = UIAutomationInterop.ElementFromHandle(NativeMethods.GetDesktopWindow());

        Assert.NotNull(desktop);
        var dump = UIAutomationInterop.DumpTree(desktop!, maxDepth: 2);

        Assert.False(string.IsNullOrWhiteSpace(dump));
    }

    [Fact]
    public void OwnWindow_IsDiscoverableAndSupportsPatterns()
    {
        using var host = TestWindowHost.TryCreate();
        if (host is null)
        {
            // xUnit v2 cannot skip at runtime. Locally there may be no interactive desktop, but CI must
            // run this test, because it is the only one that exercises the pattern interfaces.
            Assert.False(
                Environment.GetEnvironmentVariable("CI") is not null,
                "Could not create the test window, so the UI Automation pattern interfaces were not exercised.");
            return;
        }

        var byProcess = UIAutomationInterop.FindWindowsByProcessId(Environment.ProcessId);
        var window = Assert.Single(byProcess, w => UIAutomationInterop.GetNativeWindowHandle(w) == host.Window);

        Assert.Equal(UIA_WindowControlTypeId, UIAutomationInterop.GetControlType(window));
        Assert.Equal(Environment.ProcessId, UIAutomationInterop.GetProcessId(window));
        Assert.Equal(host.Title, UIAutomationInterop.GetName(window));
        Assert.True(UIAutomationInterop.IsEnabled(window));
        var bounds = UIAutomationInterop.GetBoundingRectangle(window);
        Assert.NotNull(bounds);
        Assert.True(bounds!.Value.Width > 0);
        Assert.True(bounds.Value.Height > 0);

        Assert.Contains(UIAutomationInterop.FindWindowsByTitle(host.Title, exact: true),
            w => UIAutomationInterop.GetNativeWindowHandle(w) == host.Window);

        var buttons = UIAutomationInterop.FindButtons(window);
        var button = Assert.Single(buttons, b => b.name == TestWindowHost.ButtonText);

        var edits = UIAutomationInterop.FindEdits(window);
        var edit = Assert.Single(edits, e => UIAutomationInterop.GetNativeWindowHandle(e) == host.Edit);

        // Walks descendants using CreateTrueCondition.
        Assert.NotNull(UIAutomationInterop.FindFirstByAutomationIdOrName([window], TestWindowHost.ButtonText));

        Assert.True(UIAutomationInterop.InvokeElement(button.element));
        Assert.True(UIAutomationInterop.SetValue(edit, "changed"));
        Assert.Equal("changed", NativeMethods.GetText(host.Edit));

        var dump = UIAutomationInterop.DumpTree(window, maxDepth: 4);
        Assert.Contains(host.Title, dump);
        Assert.Contains(TestWindowHost.ButtonText, dump);
    }

    private sealed class TestWindowHost : IDisposable
    {
        public const string ButtonText = "DevFlow OK";

        private const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CHILD = 0x40000000;
        private const int WS_BORDER = 0x00800000;
        private const uint WM_QUIT = 0x0012;

        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private uint _threadId;

        public IntPtr Window { get; private set; }
        public IntPtr Edit { get; private set; }
        public string Title { get; } = $"DevFlow UIA test {Guid.NewGuid():N}";

        private TestWindowHost()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "DevFlow UIA test window" };
        }

        public static TestWindowHost? TryCreate()
        {
            var host = new TestWindowHost();
            host._thread.Start();
            if (!host._ready.Wait(TimeSpan.FromSeconds(10)) || host.Window == IntPtr.Zero || host.Edit == IntPtr.Zero)
            {
                host.Dispose();
                return null;
            }

            return host;
        }

        private void Run()
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            try
            {
                var user32 = NativeLibrary.Load("user32.dll");
                var instance = NativeMethods.GetModuleHandleW(null);
                var className = "DevFlowUiaTest" + Guid.NewGuid().ToString("N");
                var wndClass = new NativeMethods.WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                    lpfnWndProc = NativeLibrary.GetExport(user32, "DefWindowProcW"),
                    hInstance = instance,
                    lpszClassName = className,
                };

                if (NativeMethods.RegisterClassExW(ref wndClass) == 0)
                    return;

                var window = NativeMethods.CreateWindowExW(0, className, Title, WS_OVERLAPPEDWINDOW | WS_VISIBLE,
                    50, 50, 400, 300, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
                if (window == IntPtr.Zero)
                    return;

                NativeMethods.CreateWindowExW(0, "BUTTON", ButtonText, WS_CHILD | WS_VISIBLE,
                    10, 10, 120, 30, window, IntPtr.Zero, instance, IntPtr.Zero);
                var edit = NativeMethods.CreateWindowExW(0, "EDIT", "initial", WS_CHILD | WS_VISIBLE | WS_BORDER,
                    10, 60, 200, 24, window, IntPtr.Zero, instance, IntPtr.Zero);

                Edit = edit;
                Window = window;
            }
            finally
            {
                _ready.Set();
            }

            if (Window == IntPtr.Zero)
                return;

            while (NativeMethods.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessageW(ref msg);
            }

            NativeMethods.DestroyWindow(Window);
        }

        public void Dispose()
        {
            if (_threadId != 0)
                NativeMethods.PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

            // The readiness event is intentionally not disposed: the worker may still signal it if it
            // outlives the bounded join.
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string? lpWindowName, int dwStyle,
            int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessageW(ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, char[] lpString, int nMaxCount);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandleW(string? lpModuleName);

        public static string GetText(IntPtr hwnd)
        {
            var buffer = new char[256];
            var length = GetWindowTextW(hwnd, buffer, buffer.Length);
            return new string(buffer, 0, length);
        }
    }
}
#endif
