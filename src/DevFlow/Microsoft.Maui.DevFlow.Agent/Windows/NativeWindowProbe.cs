#if WINDOWS
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.Maui.DevFlow.Agent.Core;
using SkiaSharp;

namespace Microsoft.Maui.DevFlow.Agent.Windows;

/// <summary>
/// Discovers native Windows UI Automation elements that are not reliably represented
/// in the MAUI visual tree, including modal dialogs and dialog-like popups.
/// </summary>
public sealed class NativeWindowProbe
{
    private const int DefaultMaxDepth = 10;
    private const int MaxNodesPerWindow = 256;
    private const int MaxRuntimeIdRecoveryNodes = 4096;
    // Bounds the descendant scan during dialog discovery to avoid runaway UIA tree
    // walks on large WinUI apps (where TreeScope.Descendants can return thousands of
    // nodes via cross-process COM marshaling).
    private const int MaxDialogScanNodes = 512;
    private const int MaxDialogScanDepth = 8;
    private static readonly int CurrentProcessId = Environment.ProcessId;
    private const int Srccopy = 0x00CC0020;
    private const uint PrintWindowRenderFullContent = 0x00000002;
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;

    private static readonly HashSet<string> CommonDialogButtonLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "OK", "CANCEL", "YES", "NO", "CLOSE", "DISMISS", "RETRY", "ABORT", "IGNORE", "CONTINUE", "ALLOW", "DON'T ALLOW", "DELETE", "KEEP"
    };

    public void AppendNativeWindows(
        List<ElementInfo> roots,
        Dictionary<string, object> nativeObjects,
        IEnumerable<IntPtr> knownHwnds,
        int? maxDepth = null)
    {
        var known = knownHwnds.Where(h => h != IntPtr.Zero).Distinct().ToArray();
        AppendKnownWindowDialogSubtrees(roots, nativeObjects, known, maxDepth);
        AppendForeignTopLevelWindows(roots, nativeObjects, known, maxDepth);
    }

    public List<ElementInfo> HitTest(
        Dictionary<string, object> nativeObjects,
        IReadOnlyList<IntPtr> knownHwnds,
        double x,
        double y)
    {
        var hwnd = knownHwnds.FirstOrDefault(candidate => candidate != IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            return [];

        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        var screenPoint = new NativePoint
        {
            X = (int)Math.Round(x * scale),
            Y = (int)Math.Round(y * scale)
        };
        if (!ClientToScreen(hwnd, ref screenPoint))
            return [];

        AutomationElement? currentElement;
        try
        {
            currentElement = AutomationElement.FromPoint(
                new System.Windows.Point(screenPoint.X, screenPoint.Y));
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
        {
            return HitTestByBounds(nativeObjects, knownHwnds, x, y);
        }

        var clientOrigin = new NativePoint();
        if (!ClientToScreen(hwnd, ref clientOrigin))
            return [];

        var results = new List<ElementInfo>();
        for (var depth = 0; currentElement is not null && depth < 16; depth++)
        {
            AutomationElement.AutomationElementInformation current;
            try
            {
                current = currentElement.Current;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
            {
                break;
            }

            if (current.ProcessId != CurrentProcessId)
                break;

            var runtimeId = TryGetRuntimeId(currentElement);
            var id = runtimeId is { Length: > 0 }
                ? $"native:uia-runtime:{string.Join(".", runtimeId)}"
                : BuildHitId(hwnd, screenPoint, depth);
            nativeObjects[id] = currentElement;

            var info = Map(currentElement, current, id, isRoot: false);
            NormalizeBoundsToWindow(info, clientOrigin, scale);
            results.Add(info);

            try
            {
                currentElement = TreeWalker.ControlViewWalker.GetParent(currentElement);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
            {
                break;
            }
        }

        var boundsResults = HitTestByBounds(nativeObjects, knownHwnds, x, y);
        if (boundsResults.Count == 0)
            return results;
        if (results.Count == 0)
            return boundsResults;

        var merged = new List<ElementInfo>(boundsResults.Count + results.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in boundsResults)
        {
            if (seenIds.Add(result.Id))
                merged.Add(result);
        }
        foreach (var result in results)
        {
            if (seenIds.Add(result.Id))
                merged.Add(result);
        }
        return merged;
    }

    private void AppendBoundsHits(
        List<ElementInfo> hits,
        ElementInfo element,
        double x,
        double y)
    {
        var bounds = element.WindowBounds ?? element.Bounds;
        if (element.IsVisible
            && bounds is { Width: > 0, Height: > 0 }
            && x >= bounds.X
            && x <= bounds.X + bounds.Width
            && y >= bounds.Y
            && y <= bounds.Y + bounds.Height)
        {
            hits.Add(element);
        }

        if (element.Children is null)
            return;

        foreach (var child in element.Children)
            AppendBoundsHits(hits, child, x, y);
    }

    private List<ElementInfo> HitTestByBounds(
        Dictionary<string, object> nativeObjects,
        IReadOnlyList<IntPtr> knownHwnds,
        double x,
        double y)
    {
        var roots = new List<ElementInfo>();
        AppendNativeWindows(roots, nativeObjects, knownHwnds);

        var hits = new List<ElementInfo>();
        foreach (var root in roots)
            AppendBoundsHits(hits, root, x, y);

        hits.Sort((first, second) =>
        {
            var firstBounds = first.WindowBounds ?? first.Bounds;
            var secondBounds = second.WindowBounds ?? second.Bounds;
            var firstArea = firstBounds is null ? double.MaxValue : firstBounds.Width * firstBounds.Height;
            var secondArea = secondBounds is null ? double.MaxValue : secondBounds.Width * secondBounds.Height;
            return firstArea.CompareTo(secondArea);
        });
        return hits;
    }

    public void AppendForeignTopLevelWindows(
        List<ElementInfo> roots,
        Dictionary<string, object> nativeObjects,
        IEnumerable<IntPtr> knownHwnds,
        int? maxDepth = null)
    {
        var depth = maxDepth is > 0 ? maxDepth.Value : DefaultMaxDepth;
        var knownHandles = knownHwnds.Where(handle => handle != IntPtr.Zero).ToArray();
        var known = new HashSet<long>(knownHandles.Select(h => h.ToInt64()));
        var coordinateRoot = knownHandles.FirstOrDefault();
        IReadOnlyList<AutomationElement> windows;
        try
        {
            windows = EnumerateProcessTopLevels();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ElementNotAvailableException)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] NativeWindowProbe enumeration failed: {ex.Message}");
            return;
        }

        if (coordinateRoot != IntPtr.Zero
            && !windows.Any(window =>
                TryGetNativeWindowHandle(window) == coordinateRoot))
        {
            return;
        }

        var rootIndex = roots.Count;
        foreach (var window in windows)
        {
            var hwnd = TryGetNativeWindowHandle(window);
            if (hwnd == coordinateRoot)
                break;

            if (hwnd == IntPtr.Zero || known.Contains(hwnd.ToInt64()))
                continue;

            var prefix = $"native:hwnd:0x{hwnd.ToInt64():X}";
            var info = WalkAutomationElement(window, prefix, [rootIndex++], nativeObjects, 0, depth, isRoot: true);
            if (info is null)
                continue;

            NormalizeTreeToWindow(
                info,
                coordinateRoot != IntPtr.Zero ? coordinateRoot : hwnd);
            if (coordinateRoot != IntPtr.Zero)
                ClipTreeToWindowClient(info, coordinateRoot);
            info.Traits ??= [];
            if (!info.Traits.Contains("dialog"))
                info.Traits.Add("dialog");
            roots.Add(info);
        }
    }

    private static IntPtr TryGetNativeWindowHandle(AutomationElement window)
    {
        try
        {
            // Zero-extend the int handle: AutomationElement.NativeWindowHandle is a
            // signed 32-bit value, but valid HWNDs above 0x7FFFFFFF are negative
            // when reinterpreted as int.
            return new IntPtr(unchecked((long)(uint)window.Current.NativeWindowHandle));
        }
        catch (ElementNotAvailableException)
        {
            return IntPtr.Zero;
        }
    }

    private static void ClipTreeToWindowClient(ElementInfo root, IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out var clientRect))
            return;

        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        var clipWidth = (clientRect.Right - clientRect.Left) / scale;
        var clipHeight = (clientRect.Bottom - clientRect.Top) / scale;
        ClipElementToWindowClient(root, clipWidth, clipHeight);
    }

    private static void ClipElementToWindowClient(
        ElementInfo element,
        double clipWidth,
        double clipHeight)
    {
        if (element.WindowBounds is { } bounds)
        {
            var left = Math.Max(0, bounds.X);
            var top = Math.Max(0, bounds.Y);
            var right = Math.Min(clipWidth, bounds.X + bounds.Width);
            var bottom = Math.Min(clipHeight, bounds.Y + bounds.Height);
            if (right <= left || bottom <= top)
            {
                element.IsVisible = false;
                element.WindowBounds = new BoundsInfo();
            }
            else
            {
                element.WindowBounds = new BoundsInfo
                {
                    X = left,
                    Y = top,
                    Width = right - left,
                    Height = bottom - top
                };
            }
        }

        if (element.Children is null)
            return;

        foreach (var child in element.Children)
            ClipElementToWindowClient(child, clipWidth, clipHeight);
    }

    public static AutomationElement? TryGetAutomationElement(IReadOnlyDictionary<string, object> nativeObjects, string id)
        => nativeObjects.TryGetValue(id, out var native) && native is AutomationElement element ? element : null;

    public static byte[]? CaptureElementScreenshot(AutomationElement element)
    {
        System.Windows.Rect bounds;
        try
        {
            bounds = element.Current.BoundingRectangle;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
        {
            return null;
        }

        if (bounds.IsEmpty
            || !double.IsFinite(bounds.X)
            || !double.IsFinite(bounds.Y)
            || !double.IsFinite(bounds.Width)
            || !double.IsFinite(bounds.Height))
        {
            return null;
        }

        var hwnd = TryGetTopLevelWindowHandle(element);
        if (!hwnd.HasValue || !GetWindowRect(hwnd.Value, out var windowRect))
            return null;

        var windowWidth = windowRect.Right - windowRect.Left;
        var windowHeight = windowRect.Bottom - windowRect.Top;
        var cropLeft = Math.Max(0, (int)Math.Floor(bounds.X) - windowRect.Left);
        var cropTop = Math.Max(0, (int)Math.Floor(bounds.Y) - windowRect.Top);
        var cropRight = Math.Min(windowWidth, (int)Math.Ceiling(bounds.Right) - windowRect.Left);
        var cropBottom = Math.Min(windowHeight, (int)Math.Ceiling(bounds.Bottom) - windowRect.Top);
        var width = cropRight - cropLeft;
        var height = cropBottom - cropTop;
        return CaptureWindowRegion(
            hwnd.Value,
            windowWidth,
            windowHeight,
            cropLeft,
            cropTop,
            width,
            height);
    }

    public static byte[]? CaptureWindowScreenshot(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero
            || !GetWindowRect(hwnd, out var windowRect)
            || !GetClientRect(hwnd, out var clientRect))
        {
            return null;
        }

        var clientOrigin = new NativePoint();
        if (!ClientToScreen(hwnd, ref clientOrigin))
            return null;

        var windowWidth = windowRect.Right - windowRect.Left;
        var windowHeight = windowRect.Bottom - windowRect.Top;
        var cropLeft = Math.Max(0, clientOrigin.X - windowRect.Left);
        var cropTop = Math.Max(0, clientOrigin.Y - windowRect.Top);
        var cropRight = Math.Min(
            windowWidth,
            cropLeft + clientRect.Right - clientRect.Left);
        var cropBottom = Math.Min(
            windowHeight,
            cropTop + clientRect.Bottom - clientRect.Top);
        return CaptureWindowRegion(
            hwnd,
            windowWidth,
            windowHeight,
            cropLeft,
            cropTop,
            cropRight - cropLeft,
            cropBottom - cropTop);
    }

    public static byte[]? CaptureCompositedWindowScreenshot(IntPtr mainHwnd)
    {
        var baseScreenshot = CaptureWindowScreenshot(mainHwnd);
        if (baseScreenshot is null)
            return null;

        var mainOrigin = new NativePoint();
        if (!ClientToScreen(mainHwnd, ref mainOrigin))
            return baseScreenshot;

        using var bitmap = SKBitmap.Decode(baseScreenshot);
        if (bitmap is null)
            return baseScreenshot;

        IReadOnlyList<AutomationElement> topLevels;
        try
        {
            topLevels = EnumerateProcessTopLevels();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ElementNotAvailableException)
        {
            return baseScreenshot;
        }

        var windowsAboveMain = new List<IntPtr>();
        var foundMainWindow = false;
        foreach (var topLevel in topLevels)
        {
            IntPtr hwnd;
            try
            {
                hwnd = new IntPtr(unchecked((long)(uint)topLevel.Current.NativeWindowHandle));
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }

            if (hwnd == mainHwnd)
            {
                foundMainWindow = true;
                break;
            }
            if (hwnd != IntPtr.Zero)
                windowsAboveMain.Add(hwnd);
        }

        if (!foundMainWindow)
            return baseScreenshot;

        using var canvas = new SKCanvas(bitmap);
        foreach (var hwnd in windowsAboveMain.AsEnumerable().Reverse())
        {
            if (hwnd == IntPtr.Zero)
                continue;

            var origin = new NativePoint();
            if (!ClientToScreen(hwnd, ref origin))
                continue;

            var screenshot = CaptureWindowScreenshot(hwnd);
            if (screenshot is null)
                continue;

            using var windowBitmap = SKBitmap.Decode(screenshot);
            if (windowBitmap is null)
                continue;

            canvas.DrawBitmap(
                windowBitmap,
                origin.X - mainOrigin.X,
                origin.Y - mainOrigin.Y);
        }

        canvas.Flush();
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static byte[]? CaptureWindowRegion(
        IntPtr hwnd,
        int windowWidth,
        int windowHeight,
        int cropLeft,
        int cropTop,
        int width,
        int height)
    {
        const long MaxCapturePixels = 16_777_216;
        if (windowWidth <= 0 || windowHeight <= 0 || width <= 0 || height <= 0
            || (long)windowWidth * windowHeight > MaxCapturePixels
            || (long)width * height > MaxCapturePixels)
        {
            return null;
        }

        var windowDc = GetDC(hwnd);
        if (windowDc == IntPtr.Zero)
            return null;

        var windowMemoryDc = IntPtr.Zero;
        var windowBitmap = IntPtr.Zero;
        var windowPrevious = IntPtr.Zero;
        var cropMemoryDc = IntPtr.Zero;
        var cropBitmap = IntPtr.Zero;
        var cropPrevious = IntPtr.Zero;
        try
        {
            windowMemoryDc = CreateCompatibleDC(windowDc);
            windowBitmap = CreateCompatibleBitmap(windowDc, windowWidth, windowHeight);
            if (windowMemoryDc == IntPtr.Zero || windowBitmap == IntPtr.Zero)
                return null;

            windowPrevious = SelectObject(windowMemoryDc, windowBitmap);
            if (!PrintWindow(hwnd, windowMemoryDc, PrintWindowRenderFullContent))
                return null;

            cropMemoryDc = CreateCompatibleDC(windowDc);
            cropBitmap = CreateCompatibleBitmap(windowDc, width, height);
            if (cropMemoryDc == IntPtr.Zero || cropBitmap == IntPtr.Zero)
                return null;

            cropPrevious = SelectObject(cropMemoryDc, cropBitmap);
            if (!BitBlt(
                cropMemoryDc,
                0,
                0,
                width,
                height,
                windowMemoryDc,
                cropLeft,
                cropTop,
                Srccopy))
            {
                return null;
            }

            SelectObject(cropMemoryDc, cropPrevious);
            cropPrevious = IntPtr.Zero;

            var bytes = new byte[checked(width * height * 4)];
            var bitmapInfo = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb,
                    SizeImage = (uint)bytes.Length
                }
            };
            if (GetDIBits(
                cropMemoryDc,
                cropBitmap,
                0,
                (uint)height,
                bytes,
                ref bitmapInfo,
                DibRgbColors) == 0)
            {
                return null;
            }

            using var skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            Marshal.Copy(bytes, 0, skBitmap.GetPixels(), bytes.Length);
            using var image = SKImage.FromBitmap(skBitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            return png.ToArray();
        }
        finally
        {
            if (cropPrevious != IntPtr.Zero && cropMemoryDc != IntPtr.Zero)
                SelectObject(cropMemoryDc, cropPrevious);
            if (cropBitmap != IntPtr.Zero)
                DeleteObject(cropBitmap);
            if (cropMemoryDc != IntPtr.Zero)
                DeleteDC(cropMemoryDc);
            if (windowPrevious != IntPtr.Zero && windowMemoryDc != IntPtr.Zero)
                SelectObject(windowMemoryDc, windowPrevious);
            if (windowBitmap != IntPtr.Zero)
                DeleteObject(windowBitmap);
            if (windowMemoryDc != IntPtr.Zero)
                DeleteDC(windowMemoryDc);
            ReleaseDC(hwnd, windowDc);
        }
    }

    /// <summary>
    /// Parses HWND seeds out of a DevFlow native element id of the form
    /// <c>native:hwnd:0x{HEX}[:dialog:{N}...]</c>. Returns an empty array when the
    /// id doesn't carry an embedded HWND. Used to keep ID generation stable across
    /// cache-miss re-walks (without a seed, <c>AppendKnownWindowDialogSubtrees</c>
    /// would never run and dialog-scoped ids would never be regenerated).
    /// </summary>
    public static IReadOnlyList<IntPtr> ExtractHwndsFromId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return Array.Empty<IntPtr>();

        const string prefix = "native:hwnd:0x";
        var start = id.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return Array.Empty<IntPtr>();

        var hexStart = start + prefix.Length;
        var hexEnd = hexStart;
        while (hexEnd < id.Length && IsHexDigit(id[hexEnd]))
            hexEnd++;

        if (hexEnd == hexStart)
            return Array.Empty<IntPtr>();

        var hex = id.AsSpan(hexStart, hexEnd - hexStart);
        if (!long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var hwndValue))
            return Array.Empty<IntPtr>();

        return new[] { new IntPtr(hwndValue) };

        static bool IsHexDigit(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    /// <summary>
    /// Rebuilds an <see cref="ElementInfo"/> for a previously-cached <see cref="AutomationElement"/>
    /// without performing a fresh process-wide window enumeration. Returns <c>null</c> when the cached
    /// element is no longer available (e.g. dialog closed).
    /// </summary>
    public static ElementInfo? TryBuildCachedElementInfo(
        IReadOnlyDictionary<string, object> nativeObjects,
        string id,
        int? maxDepth = null)
    {
        if (TryGetAutomationElement(nativeObjects, id) is not { } element)
            return null;

        return TryBuildElementInfo(element, id, maxDepth);
    }

    public static ElementInfo? TryBuildElementInfo(
        AutomationElement element,
        string id,
        int? maxDepth = null)
    {
        var depth = maxDepth is > 0 ? maxDepth.Value : DefaultMaxDepth;
        // Build a throwaway nativeObjects map so child walks don't leak into the
        // caller's cache. The returned ElementInfo's root id is rewritten to match
        // the supplied id so it round-trips with the request that produced it.
        var scratch = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var info = WalkAutomationElement(element, id, [0], scratch, 0, depth, isRoot: true);
        if (info is not null)
        {
            info.Id = id;
            if (TryGetTopLevelWindowHandle(element) is { } hwnd)
            {
                var clientOrigin = new NativePoint();
                if (ClientToScreen(hwnd, ref clientOrigin))
                {
                    var dpi = GetDpiForWindow(hwnd);
                    NormalizeBoundsToWindow(
                        info,
                        clientOrigin,
                        dpi > 0 ? dpi / 96d : 1d);
                }
            }
        }
        return info;
    }

    public AutomationElement? FindByRuntimeId(string id)
    {
        if (!TryParseRuntimeId(id, out var expectedRuntimeId))
            return null;

        try
        {
            var queue = new Queue<AutomationElement>(EnumerateProcessTopLevels());
            var walker = TreeWalker.RawViewWalker;
            var visited = 0;
            while (queue.Count > 0 && visited++ < MaxRuntimeIdRecoveryNodes)
            {
                var current = queue.Dequeue();
                try
                {
                    if (TryGetRuntimeId(current) is { } runtimeId
                        && runtimeId.SequenceEqual(expectedRuntimeId))
                    {
                        return current;
                    }

                    if (current.Current.ProcessId != CurrentProcessId)
                        continue;

                    for (var child = walker.GetFirstChild(current);
                        child is not null;
                        child = walker.GetNextSibling(child))
                    {
                        queue.Enqueue(child);
                    }
                }
                catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
        }

        return null;
    }

    public AutomationElement? FindByHitId(string id)
    {
        if (!TryParseHitId(
            id,
            out var expectedHwnd,
            out var screenX,
            out var screenY,
            out var depth))
            return null;

        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(screenX, screenY));
            for (var index = 0; index < depth && element is not null; index++)
                element = TreeWalker.ControlViewWalker.GetParent(element);

            return element?.Current.ProcessId == CurrentProcessId
                && TryGetTopLevelWindowHandle(element) == expectedHwnd
                    ? element
                    : null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            return null;
        }
    }

    public static bool TryInvoke(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePattern) && invokePattern is InvokePattern invoke)
            {
                invoke.Invoke();
                return true;
            }

            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var togglePattern) && togglePattern is TogglePattern toggle)
            {
                toggle.Toggle();
                return true;
            }

            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionPattern) && selectionPattern is SelectionItemPattern selection)
            {
                selection.Select();
                return true;
            }

            if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandPattern) && expandPattern is ExpandCollapsePattern expand)
            {
                if (expand.Current.ExpandCollapseState == ExpandCollapseState.Collapsed)
                    expand.Expand();
                else
                    expand.Collapse();
                return true;
            }
        }
        catch (Exception ex) when (ex is ElementNotEnabledException or ElementNotAvailableException or InvalidOperationException or COMException)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] NativeWindowProbe.TryInvoke failed: {ex.Message}");
        }

        return false;
    }

    public static bool TrySetValue(AutomationElement element, string value)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) &&
                pattern is ValuePattern valuePattern &&
                !valuePattern.Current.IsReadOnly)
            {
                valuePattern.SetValue(value);
                return true;
            }
        }
        catch (Exception ex) when (ex is ElementNotEnabledException or ElementNotAvailableException or InvalidOperationException or COMException)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] NativeWindowProbe.TrySetValue failed: {ex.Message}");
        }

        return false;
    }

    public static bool TryFocus(AutomationElement element)
    {
        try
        {
            element.SetFocus();
            return true;
        }
        catch (Exception ex) when (ex is ElementNotEnabledException or ElementNotAvailableException or InvalidOperationException or COMException)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] NativeWindowProbe.TryFocus failed: {ex.Message}");
        }

        return false;
    }

    public static bool TryScroll(AutomationElement element, double deltaX, double deltaY)
    {
        try
        {
            if ((deltaX != 0 || deltaY != 0) &&
                element.TryGetCurrentPattern(ScrollPattern.Pattern, out var scrollPattern) &&
                scrollPattern is ScrollPattern scroll)
            {
                scroll.Scroll(ToScrollAmount(deltaX), ToScrollAmount(deltaY));
                return true;
            }

            if (element.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var itemPattern) &&
                itemPattern is ScrollItemPattern item)
            {
                item.ScrollIntoView();
                return true;
            }
        }
        catch (Exception ex) when (ex is ElementNotEnabledException or ElementNotAvailableException or InvalidOperationException or COMException)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] NativeWindowProbe.TryScroll failed: {ex.Message}");
        }

        return false;
    }

    private static ScrollAmount ToScrollAmount(double delta)
    {
        if (delta > 0)
            return ScrollAmount.LargeIncrement;
        if (delta < 0)
            return ScrollAmount.LargeDecrement;
        return ScrollAmount.NoAmount;
    }

    private void AppendKnownWindowDialogSubtrees(
        List<ElementInfo> roots,
        Dictionary<string, object> nativeObjects,
        IReadOnlyList<IntPtr> knownHwnds,
        int? maxDepth)
    {
        var depth = maxDepth is > 0 ? maxDepth.Value : DefaultMaxDepth;
        var rootIndex = roots.Count;

        foreach (var hwnd in knownHwnds)
        {
            AutomationElement? root;
            try
            {
                root = AutomationElement.FromHandle(hwnd);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException or ArgumentException)
            {
                continue;
            }

            if (root is null)
                continue;

            var dialogIndex = 0;
            foreach (var candidate in FindDialogCandidates(root, hwnd))
            {
                var prefix = $"native:hwnd:0x{hwnd.ToInt64():X}:dialog:{dialogIndex++}";
                var info = WalkAutomationElement(candidate, prefix, [rootIndex++], nativeObjects, 0, depth, isRoot: true);
                if (info is null)
                    continue;

                NormalizeTreeToWindow(info, hwnd);
                info.Traits ??= [];
                if (!info.Traits.Contains("dialog"))
                    info.Traits.Add("dialog");
                roots.Add(info);
            }
        }
    }

    private static IReadOnlyList<AutomationElement> FindDialogCandidates(AutomationElement root, IntPtr rootHwnd)
    {
        // Walk the subtree breadth-first via TreeScope.Children rather than calling
        // FindAll(TreeScope.Descendants) which eagerly materializes the entire UIA
        // subtree (potentially thousands of cross-process COM marshalled nodes).
        // We cap both total nodes visited and depth to keep dialog discovery bounded.
        var candidates = new List<AutomationElement>();
        var queue = new Queue<(AutomationElement Element, int Depth)>();
        queue.Enqueue((root, 0));
        var scanned = 0;

        while (queue.Count > 0 && scanned < MaxDialogScanNodes)
        {
            var (current, depth) = queue.Dequeue();
            scanned++;

            // Skip the root window itself - only its descendants are dialog candidates.
            if (current != root && IsDialogCandidate(current, rootHwnd))
            {
                // Once we've identified a dialog candidate we don't need to keep
                // descending into its subtree.
                candidates.Add(current);
                continue;
            }

            if (depth >= MaxDialogScanDepth)
                continue;

            AutomationElementCollection children;
            try
            {
                children = current.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
            {
                continue;
            }

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child is not null)
                    queue.Enqueue((child, depth + 1));
            }
        }

        return candidates;
    }

    private static bool IsDialogCandidate(AutomationElement element, IntPtr rootHwnd)
    {
        AutomationElement.AutomationElementInformation current;
        try
        {
            current = element.Current;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }

        // Zero-extend the int NativeWindowHandle the same way EnumerateProcessTopLevels
        // does, so this comparison matches valid HWNDs above 0x7FFFFFFF on 64-bit.
        var nativeHandle = unchecked((long)(uint)current.NativeWindowHandle);
        if (nativeHandle != 0 && nativeHandle == rootHwnd.ToInt64())
            return false;

        if (TryGetIsModal(element) == true)
            return true;

        var className = current.ClassName ?? string.Empty;
        var localizedType = current.ControlType?.LocalizedControlType ?? string.Empty;
        var name = current.Name ?? string.Empty;
        var looksDialogLike =
            className.Contains("Dialog", StringComparison.OrdinalIgnoreCase) ||
            localizedType.Contains("dialog", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("dialog", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("alert", StringComparison.OrdinalIgnoreCase);

        return looksDialogLike && HasCommonDialogButton(element);
    }

    private static bool IsAncestor(AutomationElement ancestor, AutomationElement descendant)
    {
        var walker = TreeWalker.RawViewWalker;
        AutomationElement? current;
        try
        {
            current = walker.GetParent(descendant);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }

        while (current is not null)
        {
            if (SameElement(current, ancestor))
                return true;

            try
            {
                current = walker.GetParent(current);
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        }

        return false;
    }

    internal static bool SameElement(AutomationElement first, AutomationElement second)
    {
        try
        {
            return first.Equals(second) || first.GetRuntimeId().SequenceEqual(second.GetRuntimeId());
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    internal static object GetStableIdentity(AutomationElement element)
    {
        try
        {
            var runtimeId = element.GetRuntimeId();
            return runtimeId is { Length: > 0 }
                ? new AutomationRuntimeIdentity(runtimeId)
                : element;
        }
        catch (ElementNotAvailableException)
        {
            return element;
        }
    }

    internal static bool SameIdentity(object first, object second)
    {
        if (first is AutomationRuntimeIdentity firstRuntime
            && second is AutomationRuntimeIdentity secondRuntime)
        {
            return firstRuntime.RuntimeId.AsSpan().SequenceEqual(secondRuntime.RuntimeId);
        }

        if (first is AutomationElement firstElement
            && second is AutomationElement secondElement)
        {
            return SameElement(firstElement, secondElement);
        }

        return Equals(first, second);
    }

    internal static bool IsDurableIdentity(object identity)
        => identity is AutomationRuntimeIdentity;

    private sealed record AutomationRuntimeIdentity(int[] RuntimeId);

    private static bool HasCommonDialogButton(AutomationElement root)
    {
        try
        {
            var buttons = root.FindAll(
                TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            for (var i = 0; i < buttons.Count; i++)
            {
                var name = buttons[i]?.Current.Name;
                if (!string.IsNullOrWhiteSpace(name) &&
                    CommonDialogButtonLabels.Contains(NormalizeLabel(name)))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
        {
        }

        return false;
    }

    private static IReadOnlyList<AutomationElement> EnumerateProcessTopLevels()
    {
        var hwnds = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            GetWindowThreadProcessId(hwnd, out var pid);
            if ((int)pid == CurrentProcessId)
                hwnds.Add(hwnd);

            return true;
        }, IntPtr.Zero);

        var result = new List<AutomationElement>(hwnds.Count);
        foreach (var hwnd in hwnds)
        {
            AutomationElement? element;
            try
            {
                element = AutomationElement.FromHandle(hwnd);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException or ArgumentException)
            {
                continue;
            }

            if (element is not null)
                result.Add(element);
        }

        return result;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr handle);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        IntPtr destinationDc,
        int destinationX,
        int destinationY,
        int width,
        int height,
        IntPtr sourceDc,
        int sourceX,
        int sourceY,
        int rasterOperation);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc,
        IntPtr bitmap,
        uint start,
        uint lines,
        byte[] bits,
        ref BitmapInfo bitmapInfo,
        uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
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
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    private static ElementInfo? WalkAutomationElement(
        AutomationElement element,
        string prefix,
        IReadOnlyList<int> path,
        Dictionary<string, object> nativeObjects,
        int depth,
        int maxDepth,
        bool isRoot)
    {
        if (depth > maxDepth)
            return null;

        AutomationElement.AutomationElementInformation current;
        try
        {
            current = element.Current;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }

        var id = BuildId(current, prefix, path);
        // Sanitize() collapses non-alphanumerics to '_', so siblings with names that
        // sanitize identically (e.g. "Don't Allow" and "Don_t Allow") would otherwise
        // overwrite each other in nativeObjects and a later action-by-id would
        // invoke the wrong element. Disambiguate by appending the tree path.
        if (nativeObjects.ContainsKey(id))
            id = $"{id}:path:{string.Join(".", path)}";
        nativeObjects[id] = element;
        var info = Map(element, current, id, isRoot);

        AutomationElementCollection children;
        try
        {
            children = element.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
        {
            return info;
        }

        var index = 0;
        for (var i = 0; i < children.Count; i++)
        {
            if (info.Children?.Count >= MaxNodesPerWindow)
                break;

            var child = children[i];
            if (child is null)
                continue;

            var childInfo = WalkAutomationElement(child, prefix, [.. path, index++], nativeObjects, depth + 1, maxDepth, isRoot: false);
            if (childInfo is not null)
            {
                info.Children ??= [];
                info.Children.Add(childInfo);
            }
        }

        return info;
    }

    private static ElementInfo Map(AutomationElement element, AutomationElement.AutomationElementInformation current, string id, bool isRoot)
    {
        var controlType = current.ControlType;
        var type = controlType?.LocalizedControlType ?? "element";
        var isPassword = IsPassword(element);
        var traits = new List<string>();
        if (isRoot)
            traits.Add("dialog");
        if (HasActionPattern(element))
        {
            traits.Add("actionable");
            traits.Add("interactive");
        }

        if (CanFocus(current, element))
            traits.Add("focusable");
        if (CanScroll(element))
            traits.Add("scrollable");

        var properties = new Dictionary<string, string?>
        {
            ["controlType"] = controlType?.ProgrammaticName,
            ["className"] = string.IsNullOrWhiteSpace(current.ClassName) ? null : current.ClassName,
            ["nativeWindowHandle"] = current.NativeWindowHandle == 0 ? null : $"0x{current.NativeWindowHandle:X}",
            ["processId"] = current.ProcessId.ToString(),
            ["isModal"] = TryGetIsModal(element)?.ToString(),
            ["framework"] = string.IsNullOrWhiteSpace(current.FrameworkId) ? null : current.FrameworkId,
            ["isOffscreen"] = current.IsOffscreen.ToString(),
            ["hasKeyboardFocus"] = current.HasKeyboardFocus.ToString(),
            ["isPassword"] = isPassword.ToString()
        };

        return new ElementInfo
        {
            Id = id,
            IdentityToken = GetStableIdentity(element),
            Framework = "windows-native",
            Origin = "native",
            Type = NormalizeType(type),
            FullType = controlType?.ProgrammaticName ?? string.Empty,
            AutomationId = string.IsNullOrWhiteSpace(current.AutomationId) ? null : current.AutomationId,
            Text = string.IsNullOrWhiteSpace(current.Name) ? null : current.Name,
            Value = SensitiveValueRedactor.Redact(TryGetValue(element), isPassword),
            Role = controlType?.LocalizedControlType,
            Traits = traits.Count > 0 ? traits : null,
            Capabilities = BuildCapabilities(element, current),
            Bounds = MapBounds(current.BoundingRectangle),
            WindowBounds = MapBounds(current.BoundingRectangle),
            BoundsQuality = "exact",
            IsVisible = !current.IsOffscreen,
            IsEnabled = current.IsEnabled,
            IsFocused = current.HasKeyboardFocus,
            NativeType = controlType?.ProgrammaticName,
            NativeProperties = properties
        };
    }

    private static List<string> BuildCapabilities(
        AutomationElement element,
        AutomationElement.AutomationElementInformation current)
    {
        var capabilities = new List<string> { "select" };
        if (HasActionPattern(element))
            capabilities.Add("invoke");
        if (CanFocus(current, element))
            capabilities.Add("focus");
        if (SupportsValuePattern(element))
            capabilities.Add("set-value");
        if (CanScroll(element))
            capabilities.Add("scroll");
        return capabilities;
    }

    private static void NormalizeBoundsToWindow(
        ElementInfo info,
        NativePoint clientOrigin,
        double scale)
    {
        var screenBounds = info.WindowBounds ?? info.Bounds;
        if (screenBounds is not null)
        {
            var normalized = new BoundsInfo
            {
                X = (screenBounds.X - clientOrigin.X) / scale,
                Y = (screenBounds.Y - clientOrigin.Y) / scale,
                Width = screenBounds.Width / scale,
                Height = screenBounds.Height / scale
            };
            info.Bounds = normalized;
            info.WindowBounds = normalized;
            info.NativeProperties ??= new Dictionary<string, string?>();
            info.NativeProperties["coordinateSpace"] = "window-logical";
            info.NativeProperties["displayDensity"] =
                scale.ToString("R", CultureInfo.InvariantCulture);
            info.NativeProperties["screenBounds"] =
                $"{screenBounds.X},{screenBounds.Y},{screenBounds.Width},{screenBounds.Height}";
        }

        if (info.Children is null)
            return;

        foreach (var child in info.Children)
            NormalizeBoundsToWindow(child, clientOrigin, scale);
    }

    private static void NormalizeTreeToWindow(ElementInfo info, IntPtr hwnd)
    {
        var clientOrigin = new NativePoint();
        if (!ClientToScreen(hwnd, ref clientOrigin))
            return;

        var dpi = GetDpiForWindow(hwnd);
        NormalizeBoundsToWindow(
            info,
            clientOrigin,
            dpi > 0 ? dpi / 96d : 1d);
    }

    private static int[]? TryGetRuntimeId(AutomationElement element)
    {
        try
        {
            return element.GetRuntimeId();
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
        {
            return null;
        }
    }

    private static bool TryParseRuntimeId(string id, out int[] runtimeId)
    {
        const string prefix = "native:uia-runtime:";
        runtimeId = [];
        if (!id.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var parts = id[prefix.Length..].Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        runtimeId = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(
                parts[index],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out runtimeId[index]))
            {
                runtimeId = [];
                return false;
            }
        }

        return true;
    }

    private static string BuildHitId(IntPtr hwnd, NativePoint screenPoint, int depth)
        => $"native:uia-hit:0x{hwnd.ToInt64():X}:{screenPoint.X}:{screenPoint.Y}:{depth}";

    private static bool TryParseHitId(
        string id,
        out IntPtr hwnd,
        out int screenX,
        out int screenY,
        out int depth)
    {
        const string prefix = "native:uia-hit:0x";
        hwnd = IntPtr.Zero;
        screenX = 0;
        screenY = 0;
        depth = 0;
        if (!id.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var parts = id[prefix.Length..].Split(':');
        if (parts.Length != 4
            || !long.TryParse(
                parts[0],
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var hwndValue))
        {
            return false;
        }

        hwnd = new IntPtr(hwndValue);
        return hwnd != IntPtr.Zero
            && int.TryParse(
                parts[1],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out screenX)
            && int.TryParse(
                parts[2],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out screenY)
            && int.TryParse(
                parts[3],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out depth)
            && depth >= 0;
    }

    internal static IntPtr? TryGetTopLevelWindowHandle(AutomationElement element)
    {
        AutomationElement? current = element;
        IntPtr hwnd = IntPtr.Zero;
        try
        {
            while (current is not null)
            {
                var nativeHandle = current.Current.NativeWindowHandle;
                if (nativeHandle != 0)
                    hwnd = new IntPtr(unchecked((long)(uint)nativeHandle));
                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            return null;
        }

        return hwnd == IntPtr.Zero ? null : hwnd;
    }

    private static bool CanFocus(AutomationElement.AutomationElementInformation current, AutomationElement element)
    {
        if (current.IsKeyboardFocusable)
            return true;

        return HasActionPattern(element) || SupportsValuePattern(element);
    }

    private static BoundsInfo? MapBounds(System.Windows.Rect rect)
    {
        if (rect.IsEmpty || double.IsInfinity(rect.Width) || double.IsInfinity(rect.Height))
            return null;

        return new BoundsInfo
        {
            X = rect.X,
            Y = rect.Y,
            Width = Math.Max(0, rect.Width),
            Height = Math.Max(0, rect.Height)
        };
    }

    private static bool? TryGetIsModal(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern) && pattern is WindowPattern windowPattern)
                return windowPattern.Current.IsModal;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
        }

        return null;
    }

    private static string? TryGetValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern valuePattern)
                return valuePattern.Current.Value;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
        }

        return null;
    }

    private static bool SupportsValuePattern(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(ValuePattern.Pattern, out _);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return false;
        }
    }

    private static bool IsPassword(AutomationElement element)
    {
        try
        {
            return element.GetCurrentPropertyValue(
                AutomationElement.IsPasswordProperty,
                ignoreDefaultValue: true) is true;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return false;
        }
    }

    private static bool HasActionPattern(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(InvokePattern.Pattern, out _) ||
                   element.TryGetCurrentPattern(TogglePattern.Pattern, out _) ||
                   element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _) ||
                   element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return false;
        }
    }

    private static bool CanScroll(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(ScrollPattern.Pattern, out _) ||
                   element.TryGetCurrentPattern(ScrollItemPattern.Pattern, out _);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return false;
        }
    }

    private static string BuildId(AutomationElement.AutomationElementInformation current, string prefix, IReadOnlyList<int> path)
    {
        var stable = !string.IsNullOrWhiteSpace(current.AutomationId)
            ? $"automation:{Sanitize(current.AutomationId)}"
            : !string.IsNullOrWhiteSpace(current.Name)
                ? $"name:{Sanitize(current.Name)}"
                : $"path:{string.Join(".", path)}";
        return $"{prefix}:{stable}";
    }

    private static string Sanitize(string value)
        => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or ':' ? ch : '_'));

    private static string NormalizeType(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Element";

        return string.Concat(raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string NormalizeLabel(string label)
        => label
            .Trim()
            .Replace('\u2019', '\'')
            .Replace("&", string.Empty)
            .Replace("_", string.Empty)
            .ToUpperInvariant();
}
#endif
