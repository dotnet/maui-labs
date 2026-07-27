using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Profiling;
using Microsoft.Maui.DevFlow.Agent.Windows;

namespace Microsoft.Maui.DevFlow.Agent.WPF;

/// <summary>
/// WPF-specific agent service with native tap, screenshot, and scroll support for MAUI WPF apps.
/// </summary>
public class WpfAgentService : MauiDevFlowAgentService
{
    public WpfAgentService(AgentOptions? options = null) : base(options) { }

    internal WpfAgentService(
        AgentOptions? options,
        NativeElementRegistrationRegistry nativeElementRegistry,
        IDisposable nativeElementSubscription)
        : base(options, nativeElementRegistry, nativeElementSubscription)
    {
    }

    protected override VisualTreeWalker CreateTreeWalker()
        => NativeElementRegistry is null
            ? new WpfVisualTreeWalker()
            : new WpfVisualTreeWalker(NativeElementRegistry);
    protected override IProfilerCollector CreateProfilerCollector() => new RuntimeProfilerCollector();

    protected override string PlatformName => "WPF";
    protected override string DeviceTypeName => "Virtual";
    protected override string IdiomName => "Desktop";
    protected override bool SupportsNativeElementScreenshots => true;

    protected override double GetWindowDisplayDensity(IWindow? window)
    {
        try
        {
            if (window?.Handler?.PlatformView is System.Windows.Window wpfWindow)
            {
                var source = PresentationSource.FromVisual(wpfWindow);
                if (source?.CompositionTarget != null)
                    return source.CompositionTarget.TransformToDevice.M11;
            }
        }
        catch { }
        return 1.0;
    }

    protected override (double width, double height) GetNativeWindowSize(IWindow window)
    {
        try
        {
            if (window.Handler?.PlatformView is System.Windows.Window wpfWindow)
                return (wpfWindow.ActualWidth, wpfWindow.ActualHeight);
        }
        catch { }
        return base.GetNativeWindowSize(window);
    }

    protected override Task<bool> TryNativeScroll(VisualElement element, double deltaX, double deltaY)
    {
        try
        {
            var target = element;
            while (target != null)
            {
                if (target.Handler?.PlatformView is DependencyObject d)
                {
                    var scroll = FindAncestor<ScrollViewer>(d);
                    if (scroll != null)
                    {
                        if (deltaX != 0)
                            scroll.ScrollToHorizontalOffset(Math.Max(0, scroll.HorizontalOffset + deltaX));
                        if (deltaY != 0)
                            scroll.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset - deltaY));
                        return Task.FromResult(true);
                    }
                }
                target = target.Parent as VisualElement;
            }
        }
        catch { }
        return Task.FromResult(false);
    }

    protected override bool TryNativeTap(VisualElement ve)
    {
        try
        {
            var platformView = ve.Handler?.PlatformView;
            if (platformView == null) return false;

            if (platformView is ButtonBase buttonBase)
            {
                // Try to get an existing peer (some controls register one during template apply);
                // otherwise fall back to the generic ButtonBase peer which works for Button,
                // CheckBox, RadioButton, ToggleButton, RepeatButton, etc. without an unsafe cast.
                var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(buttonBase)
                    ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(buttonBase);

                if (peer is null)
                {
                    // Last resort: simulate the click directly.
                    buttonBase.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent, buttonBase));
                    return true;
                }

                if (peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)
                    is System.Windows.Automation.Provider.IInvokeProvider invoke)
                {
                    invoke.Invoke();
                    return true;
                }
                if (peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)
                    is System.Windows.Automation.Provider.IToggleProvider toggle)
                {
                    toggle.Toggle();
                    return true;
                }
            }

            if (platformView is System.Windows.Controls.CheckBox checkBox)
            {
                checkBox.IsChecked = !(checkBox.IsChecked ?? false);
                return true;
            }

            if (platformView is ToggleButton toggleButton)
            {
                toggleButton.IsChecked = !(toggleButton.IsChecked ?? false);
                return true;
            }

            if (platformView is UIElement ui)
            {
                ui.Focus();
                return true;
            }
        }
        catch { }
        return false;
    }

    protected override async Task<bool> TryNativeTapFirstAsync(VisualElement ve)
    {
        try
        {
            if (ve.Handler?.PlatformView is not ButtonBase buttonBase)
                return false;

            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(buttonBase)
                ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(buttonBase);

            async Task<bool> DispatchInvocationAsync(Action invocation, string actionName)
            {
                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var invocationState = 0;
                _ = buttonBase.Dispatcher.BeginInvoke(() =>
                {
                    if (Interlocked.CompareExchange(ref invocationState, 1, 0) != 0)
                    {
                        completion.TrySetResult(false);
                        return;
                    }

                    try
                    {
                        _ = buttonBase.Dispatcher.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Background,
                            new Action(() => completion.TrySetResult(true)));
                        invocation();
                        completion.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Microsoft.Maui.DevFlow] WPF native {actionName} skipped: {ex.GetBaseException().Message}");
                        completion.TrySetResult(false);
                    }
                });
                var winner = await Task.WhenAny(
                    completion.Task,
                    Task.Delay(TimeSpan.FromSeconds(5)));
                if (winner == completion.Task)
                    return await completion.Task;

                if (Interlocked.CompareExchange(ref invocationState, 2, 0) == 0)
                {
                    completion.TrySetResult(false);
                    return false;
                }

                // The native invocation already started. Treat it as handled so the
                // caller does not also fire the managed fallback and execute twice.
                return true;
            }

            if (peer?.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)
                is System.Windows.Automation.Provider.IInvokeProvider invoke)
            {
                return await DispatchInvocationAsync(invoke.Invoke, "invoke");
            }

            if (peer?.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)
                is System.Windows.Automation.Provider.IToggleProvider toggle)
            {
                return await DispatchInvocationAsync(toggle.Toggle, "toggle");
            }

            return await DispatchInvocationAsync(
                () => buttonBase.RaiseEvent(
                    new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent, buttonBase)),
                "raise-event");
        }
        catch { }

        return false;
    }

    protected override async Task<byte[]?> CaptureElementScreenshotAsync(VisualElement element)
    {
        try
        {
            var result = await VisualDiagnostics.CaptureAsPngAsync(element);
            if (result != null) return result;
        }
        catch { }

        try
        {
            if (element.Handler?.PlatformView is FrameworkElement fe)
            {
                var bytes = CaptureFrameworkElement(fe);
                if (bytes != null) return bytes;
            }
        }
        catch { }

        return null;
    }

    protected override Task<byte[]?> CaptureNativeElementScreenshotAsync(
        object nativeElement,
        ElementInfo? elementInfo)
        => Task.FromResult(nativeElement switch
        {
            FrameworkElement frameworkElement => CaptureFrameworkElement(frameworkElement),
            _ => WpfVisualTreeWalker.CaptureNativeElementScreenshot(nativeElement)
        });

    protected override async Task<byte[]?> CaptureScreenshotAsync(VisualElement rootElement)
    {
        try
        {
            var result = await VisualDiagnostics.CaptureAsPngAsync(rootElement);
            if (result != null) return result;
        }
        catch { }

        try
        {
            if (rootElement.Handler?.PlatformView is FrameworkElement fe)
            {
                var bytes = CaptureFrameworkElement(fe);
                if (bytes != null) return bytes;
            }
        }
        catch { }

        try
        {
            var app = System.Windows.Application.Current;
            var window = app?.MainWindow;
            if (window != null)
                return CaptureFrameworkElement(window);
        }
        catch { }

        return null;
    }

    protected override async Task<byte[]?> CaptureFullScreenAsync(int? windowIndex = null)
    {
        try
        {
            var index = windowIndex ?? 0;
            var mauiWindow = Microsoft.Maui.Controls.Application.Current?
                .Windows.ElementAtOrDefault(index);
            if (mauiWindow?.Handler?.PlatformView is System.Windows.Window window)
            {
                var hwnd = await window.Dispatcher.InvokeAsync(
                    () => new System.Windows.Interop.WindowInteropHelper(window).Handle);
                return hwnd == IntPtr.Zero
                    ? null
                    : await Task.Run(
                        () => NativeWindowProbe.CaptureCompositedWindowScreenshot(hwnd));
            }
        }
        catch { }
        return null;
    }

    protected override void TryNativeResize(IWindow window, int width, int height)
    {
        try
        {
            if (window.Handler?.PlatformView is System.Windows.Window wpfWindow)
            {
                wpfWindow.Width = width;
                wpfWindow.Height = height;
                return;
            }
        }
        catch { }
        base.TryNativeResize(window, width, height);
    }

    private static byte[]? CaptureFrameworkElement(FrameworkElement element)
    {
        try
        {
            var width = (int)Math.Ceiling(element.ActualWidth);
            var height = (int)Math.Ceiling(element.ActualHeight);
            if (width <= 0 || height <= 0) return null;

            var dpi = 96.0;
            var source = PresentationSource.FromVisual(element);
            if (source?.CompositionTarget != null)
                dpi = 96.0 * source.CompositionTarget.TransformToDevice.M11;

            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(width * dpi / 96.0),
                (int)Math.Ceiling(height * dpi / 96.0),
                dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(element);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        var current = start;
        while (current != null)
        {
            if (current is T t) return t;
            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
