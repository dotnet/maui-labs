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

namespace Microsoft.Maui.DevFlow.Agent.WPF;

/// <summary>
/// WPF-specific agent service with native tap, screenshot, and scroll support for MAUI WPF apps.
/// </summary>
public class WpfAgentService : DevFlowAgentService
{
    public WpfAgentService(AgentOptions? options = null) : base(options) { }

    protected override VisualTreeWalker CreateTreeWalker() => new WpfVisualTreeWalker();
    protected override IProfilerCollector CreateProfilerCollector() => new RuntimeProfilerCollector();

    protected override string PlatformName => "WPF";
    protected override string DeviceTypeName => "Virtual";
    protected override string IdiomName => "Desktop";

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

    protected override async Task<byte[]?> CaptureFullScreenAsync()
    {
        try
        {
            byte[]? bytes = null;
            var app = System.Windows.Application.Current;
            if (app?.Dispatcher != null)
            {
                await app.Dispatcher.InvokeAsync(() =>
                {
                    if (app.MainWindow != null)
                        bytes = CaptureFrameworkElement(app.MainWindow);
                });
            }
            return bytes;
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
