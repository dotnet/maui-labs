using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Agent.Web;

/// <summary>
/// Agent service for MAUI apps running on WebAssembly in the browser. The broker relays the agent
/// API over the agent's WebSocket (see <see cref="AgentOptions.UseBrokerTunnel"/>), and screenshots
/// are rendered from the live DOM.
/// </summary>
public class WebAgentService : MauiDevFlowAgentService
{
    public WebAgentService(AgentOptions? options = null) : base(options) { }

    protected override VisualTreeWalker CreateTreeWalker() => new WebVisualTreeWalker();

    protected override string PlatformName => "Browser";
    protected override string DeviceTypeName => "Virtual";
    protected override string IdiomName => "Desktop";
    protected override bool SupportsNativeElementScreenshots => false;

    // Captures are rendered at CSS pixel size, so there is no device density to scale away.
    protected override double GetWindowDisplayDensity(IWindow? window) => 1.0;

    protected override (double width, double height) GetNativeWindowSize(IWindow window)
    {
        try
        {
            var viewport = WebAgentInterop.Viewport();
            return (viewport[0], viewport[1]);
        }
        catch
        {
            return base.GetNativeWindowSize(window);
        }
    }

    protected override Task<byte[]?> CaptureScreenshotAsync(VisualElement rootElement) => CaptureViewportAsync();

    protected override Task<byte[]?> CaptureFullScreenAsync(int? windowIndex = null) => CaptureViewportAsync();

    protected override async Task<byte[]?> CaptureElementScreenshotAsync(VisualElement element)
    {
        if (WebAgentInterop.FindElement(element.Handler?.PlatformView, element.AutomationId) is not { } node)
            return null;

        var rect = WebAgentInterop.Rect(node);
        return await WebAgentInterop.CaptureRegionAsync(rect[0], rect[1], rect[2], rect[3]);
    }

    protected override Task<bool> TryNativeScroll(VisualElement element, double deltaX, double deltaY)
        => Task.FromResult(WebAgentInterop.FindElement(element.Handler?.PlatformView, element.AutomationId) is { } node
            && WebAgentInterop.ScrollBy(node, deltaX, deltaY));

    protected override bool TryNativeScrollOnPlatformView(object platformView, double deltaX, double deltaY)
        => WebAgentInterop.FindElement(platformView, automationId: null) is { } node
            && WebAgentInterop.ScrollBy(node, deltaX, deltaY);

    private static Task<byte[]?> CaptureViewportAsync()
    {
        var viewport = WebAgentInterop.Viewport();
        return WebAgentInterop.CaptureRegionAsync(0, 0, viewport[0], viewport[1]);
    }
}
