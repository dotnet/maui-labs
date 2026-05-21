using System.Diagnostics;
using System.Runtime.InteropServices;
#if WINDOWS_BUILD
using Interop.UIAutomationClient;
using Microsoft.Maui.DevFlow.Driver.Windows;
using SkiaSharp;
#endif

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Driver for Windows MAUI apps (WinUI3).
/// Direct localhost connection, no special setup needed.
/// Uses Windows UI Automation (UIA) via COM interop to detect and dismiss native dialogs.
/// </summary>
public class WindowsAppDriver : AppDriverBase
{
    public override string Platform => "Windows";

    public int? ProcessId { get; set; }
    public string? AppName { get; set; }
    public string? WindowTitle { get; set; }
    public IntPtr WindowHandle { get; set; }

#if WINDOWS_BUILD
    public override async Task<bool> TapAsync(string elementId)
    {
        if (await TryAgentActionAsync(() => base.TapAsync(elementId)))
            return true;

        EnsureWindows();
        var element = FindTargetElement(elementId);
        if (element is null)
            return false;

        UIAutomationInterop.ScrollIntoView(element);
        return UIAutomationInterop.InvokeElement(element) || ClickElementCenter(element);
    }

    public override async Task<bool> FillAsync(string elementId, string text)
    {
        if (await TryAgentActionAsync(() => base.FillAsync(elementId, text)))
            return true;

        EnsureWindows();
        var element = FindTargetElement(elementId);
        if (element is null)
            return false;

        UIAutomationInterop.ScrollIntoView(element);
        return UIAutomationInterop.SetValue(element, text);
    }

    public override async Task<bool> ClearAsync(string elementId)
    {
        if (await TryAgentActionAsync(() => base.ClearAsync(elementId)))
            return true;

        EnsureWindows();
        var element = FindTargetElement(elementId);
        return element is not null && UIAutomationInterop.SetValue(element, string.Empty);
    }

    public override async Task<byte[]?> ScreenshotAsync()
    {
        if (Client is not null)
        {
            try
            {
                var data = await base.ScreenshotAsync();
                if (data is { Length: > 0 })
                    return data;
            }
            catch { }
        }

        EnsureWindows();
        return TryCaptureTargetScreenshot();
    }

    public async Task<bool> FocusAsync(string elementId)
    {
        if (Client is not null)
        {
            try
            {
                if (await Client.FocusAsync(elementId))
                    return true;
            }
            catch { }
        }

        EnsureWindows();
        var element = FindTargetElement(elementId);
        return element is not null && UIAutomationInterop.SetFocus(element);
    }

    public Task<bool> ScrollIntoViewAsync(string elementId)
    {
        EnsureWindows();
        var element = FindTargetElement(elementId);
        return Task.FromResult(element is not null && UIAutomationInterop.ScrollIntoView(element));
    }

    public async Task<bool> ScrollAsync(string? elementId = null, double deltaX = 0, double deltaY = 0)
    {
        if (Client is not null)
        {
            try
            {
                if (await Client.ScrollAsync(elementId, deltaX, deltaY))
                    return true;
            }
            catch { }
        }

        EnsureWindows();
        var element = elementId is null
            ? ResolveTargetWindows(throwIfMissing: false).FirstOrDefault()
            : FindTargetElement(elementId);
        if (element is null)
            return false;

        return UIAutomationInterop.ScrollElement(element, ToScrollAmount(deltaX), ToScrollAmount(deltaY));
    }
#endif

#if WINDOWS_BUILD
    public override Task BackAsync() => PressKeyAsync("ESCAPE");

    public override Task PressKeyAsync(string key)
    {
        EnsureWindows();
        var vk = MapKeyToVirtualKey(key);
        SendKeyPress(vk);
        return Task.CompletedTask;
    }
#else
    public override Task BackAsync() => throw new PlatformNotSupportedException("Windows operations require Windows.");
    public override Task PressKeyAsync(string key) => throw new PlatformNotSupportedException("Windows operations require Windows.");
#endif

    public override async Task StartRecordingAsync(string outputFile, int timeoutSeconds = 30)
    {
        EnsureNotRecording();
        EnsureFfmpeg();

        var fullPath = Path.GetFullPath(outputFile);
        if (!fullPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            fullPath = Path.ChangeExtension(fullPath, ".mp4");

        var psi = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("gdigrab");
        psi.ArgumentList.Add("-framerate");
        psi.ArgumentList.Add("30");
        psi.ArgumentList.Add("-t");
        psi.ArgumentList.Add(timeoutSeconds.ToString());
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(ResolveGdiGrabInput());
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add(fullPath);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg");

        await Task.Delay(500);

        RecordingStateManager.Save(new RecordingState
        {
            RecordingPid = process.Id,
            OutputFile = fullPath,
            Platform = "windows",
            StartedAt = DateTimeOffset.UtcNow,
            TimeoutSeconds = timeoutSeconds
        });
    }

    public override async Task<string> StopRecordingAsync()
    {
        var state = RecordingStateManager.Load()
            ?? throw new InvalidOperationException("No active recording found.");

        if (state.Platform != "windows")
            throw new InvalidOperationException($"Active recording is on {state.Platform}, not Windows.");

        try
        {
            var proc = Process.GetProcessById(state.RecordingPid);
            if (!proc.HasExited)
            {
                proc.StandardInput.Write("q");
                proc.StandardInput.Flush();
                await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        catch
        {
            SendInterrupt(state.RecordingPid);
        }

        RecordingStateManager.Delete();
        return state.OutputFile;
    }

    private static void EnsureFfmpeg()
    {
        try
        {
            var psi = new ProcessStartInfo("ffmpeg", "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            if (proc?.ExitCode != 0)
                throw new Exception();
        }
        catch
        {
            throw new InvalidOperationException(
                "ffmpeg is required for screen recording on Windows but was not found on PATH. " +
                "Install it from https://ffmpeg.org/download.html or via 'winget install ffmpeg'.");
        }
    }

#if WINDOWS_BUILD
    public Task<AlertInfo?> DetectAlertAsync()
    {
        EnsureWindows();
        var windows = ResolveTargetWindows();
        return Task.FromResult(DetectDialog(windows));
    }

    public Task DismissAlertAsync(string? buttonLabel = null)
    {
        EnsureWindows();
        var windows = ResolveTargetWindows();
        var buttons = FindDialogButtonsCore(windows);
        if (buttons.Count == 0)
            throw new InvalidOperationException("No alert detected to dismiss.");

        var target = PickButton(buttons, buttonLabel);
        if (!UIAutomationInterop.InvokeElement(target.element) && !ClickElementCenter(target.element))
            throw new InvalidOperationException("UIA Invoke action failed.");

        return Task.CompletedTask;
    }

    public Task<AlertInfo?> HandleAlertIfPresentAsync(string? buttonLabel = null)
    {
        EnsureWindows();
        var windows = ResolveTargetWindows();
        var buttons = FindDialogButtonsCore(windows);
        if (buttons.Count == 0)
            return Task.FromResult<AlertInfo?>(null);

        var alertButtons = buttons.Select(ToAlertButton).ToList();
        var texts = FindDialogTextsCore(windows);
        var info = new AlertInfo(texts.FirstOrDefault(), alertButtons);

        var target = PickButton(buttons, buttonLabel);
        if (!UIAutomationInterop.InvokeElement(target.element))
            ClickElementCenter(target.element);

        return Task.FromResult<AlertInfo?>(info);
    }

    public Task<string> GetAccessibilityTreeAsync()
    {
        EnsureWindows();
        var windows = ResolveTargetWindows();
        var result = string.Empty;
        foreach (var window in windows)
            result += UIAutomationInterop.DumpTree(window);
        return Task.FromResult(result);
    }

    private static AlertInfo? DetectDialog(IReadOnlyList<IUIAutomationElement> windows)
    {
        var buttons = FindDialogButtonsCore(windows);
        if (buttons.Count == 0)
            return null;

        var alertButtons = buttons.Select(ToAlertButton).ToList();
        var texts = FindDialogTextsCore(windows);
        return new AlertInfo(texts.FirstOrDefault(), alertButtons);
    }

    private static List<(IUIAutomationElement element, string name)> FindDialogButtonsCore(IReadOnlyList<IUIAutomationElement> windows)
    {
        var candidate = FindDialogCandidate(windows);
        return candidate?.Buttons ?? new();
    }

    private static List<string> FindDialogTextsCore(IReadOnlyList<IUIAutomationElement> windows)
    {
        var candidate = FindDialogCandidate(windows);
        return candidate?.Texts ?? new();
    }

    private static DialogCandidate? FindDialogCandidate(IReadOnlyList<IUIAutomationElement> windows)
    {
        foreach (var window in windows)
        {
            var childWindows = UIAutomationInterop.FindChildWindows(window);
            foreach (var childWindow in childWindows)
            {
                var buttons = UIAutomationInterop.FindButtons(childWindow);
                if (buttons.Count > 0)
                {
                    var texts = UIAutomationInterop.FindTexts(childWindow);
                    if (texts.Count > 0)
                        return new DialogCandidate(buttons, texts);
                }
            }
        }

        foreach (var window in windows)
        {
            var buttons = UIAutomationInterop.FindNamedButtons(window, CommonDialogButtonLabels);
            if (buttons.Count > 0)
            {
                var texts = UIAutomationInterop.FindTexts(window);
                if (texts.Count > 0)
                    return new DialogCandidate(buttons, texts);
            }
        }

        return null;
    }

    private static AlertButton ToAlertButton((IUIAutomationElement element, string name) button)
    {
        var rect = UIAutomationInterop.GetBoundingRectangle(button.element);
        return rect is null
            ? new AlertButton(button.name, 0, 0, 0, 0)
            : new AlertButton(button.name, rect.Value.X, rect.Value.Y, rect.Value.Width, rect.Value.Height);
    }

    private sealed record DialogCandidate(
        List<(IUIAutomationElement element, string name)> Buttons,
        List<string> Texts);

    private static readonly HashSet<string> CommonDialogButtonLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "OK", "CANCEL", "YES", "NO", "CLOSE", "DISMISS", "RETRY", "ABORT", "IGNORE", "CONTINUE", "ALLOW", "DON'T ALLOW"
    };

    private static (IUIAutomationElement element, string name) PickButton(
        List<(IUIAutomationElement element, string name)> buttons, string? buttonLabel)
    {
        if (buttons.Count == 0)
            throw new InvalidOperationException("No buttons found in dialog.");

        if (buttonLabel is not null)
        {
            var normalized = UIAutomationInterop.NormalizeLabel(buttonLabel);
            var match = buttons.FirstOrDefault(b => UIAutomationInterop.NormalizeLabel(b.name) == normalized);
            if (match.element is null)
            {
                var available = string.Join(", ", buttons.Select(b => b.name));
                throw new InvalidOperationException($"Button '{buttonLabel}' not found. Available: {available}");
            }

            return match;
        }

        return buttons[0];
    }
#else
    public Task<AlertInfo?> DetectAlertAsync() => throw new PlatformNotSupportedException("Windows operations require Windows.");
    public Task DismissAlertAsync(string? buttonLabel = null) => throw new PlatformNotSupportedException("Windows operations require Windows.");
    public Task<AlertInfo?> HandleAlertIfPresentAsync(string? buttonLabel = null) => throw new PlatformNotSupportedException("Windows operations require Windows.");
    public Task<string> GetAccessibilityTreeAsync() => throw new PlatformNotSupportedException("Windows operations require Windows.");
#endif

    private int ResolveProcessId()
    {
        if (ProcessId.HasValue)
            return ProcessId.Value;

#if WINDOWS_BUILD
        if (WindowHandle != IntPtr.Zero)
        {
            var pid = GetWindowProcessId(WindowHandle);
            if (pid > 0)
            {
                ProcessId = pid;
                return pid;
            }
        }

        if (!string.IsNullOrWhiteSpace(WindowTitle))
        {
            var window = UIAutomationInterop.FindWindowsByTitle(WindowTitle).FirstOrDefault();
            var pid = window is null ? 0 : UIAutomationInterop.GetProcessId(window);
            if (pid > 0)
            {
                ProcessId = pid;
                return pid;
            }
        }
#endif

        if (!string.IsNullOrEmpty(AppName))
        {
            var processes = Process.GetProcessesByName(AppName);
            if (processes.Length > 0)
            {
                ProcessId = processes[0].Id;
                return ProcessId.Value;
            }

            var all = Process.GetProcesses();
            var match = all.FirstOrDefault(p =>
            {
                try
                {
                    return p.ProcessName.Contains(AppName, StringComparison.OrdinalIgnoreCase)
                        || p.MainWindowTitle.Contains(AppName, StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            });
            if (match != null)
            {
                ProcessId = match.Id;
                return ProcessId.Value;
            }
        }

        throw new InvalidOperationException("ProcessId, WindowHandle, WindowTitle, or AppName must be set for Windows operations.");
    }

    private string ResolveGdiGrabInput()
    {
        var title = WindowTitle;
#if WINDOWS_BUILD
        title ??= TryResolveWindowTitle();
#endif
        title ??= AppName;

        return string.IsNullOrWhiteSpace(title) ? "desktop" : $"title={title}";
    }

#if WINDOWS_BUILD
    private async Task<bool> TryAgentActionAsync(Func<Task<bool>> action)
    {
        if (Client is null)
            return false;

        try { return await action(); }
        catch { return false; }
    }

    private static ScrollAmount ToScrollAmount(double delta)
    {
        if (delta > 0)
            return ScrollAmount.ScrollAmount_LargeIncrement;
        if (delta < 0)
            return ScrollAmount.ScrollAmount_LargeDecrement;
        return ScrollAmount.ScrollAmount_NoAmount;
    }

    private IUIAutomationElement? FindTargetElement(string idOrName)
    {
        var windows = ResolveTargetWindows(throwIfMissing: false);
        return windows.Count == 0
            ? null
            : UIAutomationInterop.FindFirstByAutomationIdOrName(windows, idOrName);
    }

    private List<IUIAutomationElement> ResolveTargetWindows(bool throwIfMissing = true)
    {
        var windows = new List<IUIAutomationElement>();

        if (WindowHandle != IntPtr.Zero)
        {
            var window = UIAutomationInterop.ElementFromHandle(WindowHandle);
            if (window is not null)
                windows.Add(window);
        }

        if (ProcessId.HasValue)
            windows.AddRange(UIAutomationInterop.FindWindowsByProcessId(ProcessId.Value));
        else if (!string.IsNullOrWhiteSpace(AppName))
        {
            try
            {
                var pid = ResolveProcessId();
                windows.AddRange(UIAutomationInterop.FindWindowsByProcessId(pid));
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(WindowTitle))
        {
            var titleMatches = FilterWindowsByTitle(windows, WindowTitle);
            if (titleMatches.Count > 0)
                windows = titleMatches;
            else
                windows.AddRange(UIAutomationInterop.FindWindowsByTitle(WindowTitle));
        }

        windows = DeduplicateWindows(windows);
        if (windows.Count == 0 && throwIfMissing)
            throw new InvalidOperationException("No Windows UIAutomation windows found for the configured ProcessId, WindowHandle, WindowTitle, or AppName.");

        return windows;
    }

    private static List<IUIAutomationElement> FilterWindowsByTitle(IEnumerable<IUIAutomationElement> windows, string title)
    {
        return windows
            .Where(w => UIAutomationInterop.GetName(w)?.Contains(title, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
    }

    private static List<IUIAutomationElement> DeduplicateWindows(IEnumerable<IUIAutomationElement> windows)
    {
        var results = new List<IUIAutomationElement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var window in windows)
        {
            var handle = UIAutomationInterop.GetNativeWindowHandle(window);
            var key = handle != IntPtr.Zero
                ? $"hwnd:{handle.ToInt64()}"
                : $"uia:{UIAutomationInterop.GetProcessId(window)}:{UIAutomationInterop.GetName(window)}";
            if (seen.Add(key))
                results.Add(window);
        }

        return results;
    }

    private string? TryResolveWindowTitle()
    {
        if (!string.IsNullOrWhiteSpace(WindowTitle))
            return WindowTitle;

        if (WindowHandle != IntPtr.Zero)
        {
            var window = UIAutomationInterop.ElementFromHandle(WindowHandle);
            var name = window is null ? null : UIAutomationInterop.GetName(window);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        foreach (var window in ResolveTargetWindows(throwIfMissing: false))
        {
            var name = UIAutomationInterop.GetName(window);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        try
        {
            var process = Process.GetProcessById(ResolveProcessId());
            if (!string.IsNullOrWhiteSpace(process.MainWindowTitle))
                return process.MainWindowTitle;
        }
        catch { }

        return null;
    }

    private byte[]? TryCaptureTargetScreenshot()
    {
        try
        {
            return TryResolveTargetRectangle(out var rect)
                ? CaptureScreenRectangle(rect)
                : null;
        }
        catch { return null; }
    }

    private bool TryResolveTargetRectangle(out CaptureRect rect)
    {
        if (WindowHandle != IntPtr.Zero && TryGetWindowRectangle(WindowHandle, out rect))
            return true;

        foreach (var window in ResolveTargetWindows(throwIfMissing: false))
        {
            var handle = UIAutomationInterop.GetNativeWindowHandle(window);
            if (handle != IntPtr.Zero && TryGetWindowRectangle(handle, out rect))
                return true;

            var bounds = UIAutomationInterop.GetBoundingRectangle(window);
            if (bounds is { Width: > 0, Height: > 0 })
            {
                rect = CaptureRect.FromBounds(bounds.Value);
                return true;
            }
        }

        try
        {
            var process = Process.GetProcessById(ResolveProcessId());
            if (process.MainWindowHandle != IntPtr.Zero && TryGetWindowRectangle(process.MainWindowHandle, out rect))
                return true;
        }
        catch { }

        rect = default;
        return false;
    }

    private static bool TryGetWindowRectangle(IntPtr hwnd, out CaptureRect rect)
    {
        if (GetWindowRect(hwnd, out var nativeRect))
        {
            rect = new CaptureRect(
                nativeRect.Left,
                nativeRect.Top,
                Math.Max(0, nativeRect.Right - nativeRect.Left),
                Math.Max(0, nativeRect.Bottom - nativeRect.Top));
            return rect.Width > 0 && rect.Height > 0;
        }

        rect = default;
        return false;
    }

    private static byte[]? CaptureScreenRectangle(CaptureRect rect)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            return null;

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, rect.Width, rect.Height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
                return null;

            previous = SelectObject(memoryDc, bitmap);
            if (!BitBlt(memoryDc, 0, 0, rect.Width, rect.Height, screenDc, rect.X, rect.Y, SRCCOPY | CAPTUREBLT))
                return null;

            var bytes = new byte[rect.Width * rect.Height * 4];
            var info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = rect.Width,
                    biHeight = -rect.Height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB,
                    biSizeImage = (uint)bytes.Length
                }
            };

            var scanLines = GetDIBits(memoryDc, bitmap, 0, (uint)rect.Height, bytes, ref info, DIB_RGB_COLORS);
            if (scanLines == 0)
                return null;

            using var skBitmap = new SKBitmap(rect.Width, rect.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            Marshal.Copy(bytes, 0, skBitmap.GetPixels(), bytes.Length);
            using var image = SKImage.FromBitmap(skBitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            return png.ToArray();
        }
        finally
        {
            if (previous != IntPtr.Zero && memoryDc != IntPtr.Zero)
                SelectObject(memoryDc, previous);
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero)
                DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
#endif

#if WINDOWS_BUILD
    private static int GetWindowProcessId(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var processId);
        return (int)processId;
    }

    private static bool ClickElementCenter(IUIAutomationElement element)
    {
        var rect = UIAutomationInterop.GetBoundingRectangle(element);
        if (rect is not { Width: > 0, Height: > 0 })
            return false;

        var x = (int)Math.Round(rect.Value.X + rect.Value.Width / 2);
        var y = (int)Math.Round(rect.Value.Y + rect.Value.Height / 2);
        return ClickPoint(x, y);
    }

    private static bool ClickPoint(int x, int y)
    {
        // Save the user's current cursor position so we can restore it after the
        // synthetic click — keeping the cursor permanently relocated to the click
        // target would interfere with concurrent manual testing on the same machine.
        var hadOriginal = GetCursorPos(out var originalCursor);

        if (!SetCursorPos(x, y))
            return false;

        var inputs = new INPUT[]
        {
            new() { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } },
            new() { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } }
        };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == (uint)inputs.Length;

        if (hadOriginal)
        {
            // Restoring the cursor is best-effort; ignore failures.
            _ = SetCursorPos(originalCursor.X, originalCursor.Y);
        }

        return sent;
    }

    private static ushort MapKeyToVirtualKey(string key) => key.ToUpperInvariant() switch
    {
        "ENTER" or "RETURN" => 0x0D,
        "BACK" or "ESCAPE" or "ESC" => 0x1B,
        "TAB" => 0x09,
        "DELETE" or "BACKSPACE" => 0x08,
        "HOME" => 0x24,
        "END" => 0x23,
        "LEFT" => 0x25,
        "UP" => 0x26,
        "RIGHT" => 0x27,
        "DOWN" => 0x28,
        "SPACE" => 0x20,
        _ => (ushort)(key.Length == 1 ? char.ToUpper(key[0]) : 0)
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, int rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, byte[] lpvBits, ref BITMAPINFO lpbmi, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    private readonly record struct CaptureRect(int X, int Y, int Width, int Height)
    {
        public static CaptureRect FromBounds(UIAutomationInterop.UiaRect bounds)
            => new(
                (int)Math.Floor(bounds.X),
                (int)Math.Floor(bounds.Y),
                (int)Math.Ceiling(bounds.Width),
                (int)Math.Ceiling(bounds.Height));
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    private static void SendKeyPress(ushort vk)
    {
        if (vk == 0) return;
        var inputs = new INPUT[]
        {
            new() { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk } } },
            new() { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } } }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows dialog handling requires Windows.");
    }
#else
    private static void EnsureWindows() => throw new PlatformNotSupportedException("Windows dialog handling requires Windows.");
#endif
}
