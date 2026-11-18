using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices.JavaScript;
using System.Text;

namespace Microsoft.Maui.DevFlow.Agent.Web;

/// <summary>
/// JavaScript side of the web agent: viewport metrics, DOM geometry, scrolling and screenshots.
/// The module ships embedded in this assembly and is imported once from a data URL.
/// </summary>
internal static partial class WebAgentInterop
{
    private const string Module = "microsoft-maui-devflow-agent";
    private static Task? s_load;

    public static Task EnsureLoadedAsync() => s_load ??= LoadAsync();

    private static async Task LoadAsync()
    {
        await using var stream = typeof(WebAgentInterop).Assembly.GetManifestResourceStream("devflow-agent.js")
            ?? throw new InvalidOperationException("The DevFlow web agent script is missing from its assembly.");
        using var reader = new StreamReader(stream);
        var source = await reader.ReadToEndAsync();
        await JSHost.ImportAsync(Module, "data:text/javascript;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(source)));
    }

    /// <summary>Viewport width and height in CSS pixels, then the device pixel ratio.</summary>
    [JSImport("viewport", Module)]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static partial double[] Viewport();

    /// <summary>The element's viewport-relative x, y, width and height in CSS pixels.</summary>
    [JSImport("rect", Module)]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static partial double[] Rect(JSObject element);

    [JSImport("findByAutomationId", Module)]
    public static partial JSObject? FindByAutomationId(string automationId);

    /// <summary>The element's tag name, id and ARIA role.</summary>
    [JSImport("describe", Module)]
    [return: JSMarshalAs<JSType.Array<JSType.String>>]
    public static partial string[] Describe(JSObject element);

    [JSImport("scrollBy", Module)]
    public static partial bool ScrollBy(JSObject element, double deltaX, double deltaY);

    /// <summary>Renders a viewport region to a base64 PNG, or null when the browser refuses.</summary>
    [JSImport("capture", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> CaptureAsync(double x, double y, double width, double height);

    /// <summary>
    /// Finds the DOM element behind a handler's platform view. Web backends wrap the element in a
    /// public <c>Js</c> property (Platform.Maui.Web's <c>WebElement</c>); the backend's
    /// <c>data-automation-id</c> attribute is the fallback.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "DevFlow runs in Debug builds. If trimming removes the property, the automation id lookup still applies.")]
    public static JSObject? FindElement(object? platformView, string? automationId)
    {
        if (platformView is JSObject element)
            return element;
        if (platformView?.GetType().GetProperty("Js")?.GetValue(platformView) is JSObject wrapped)
            return wrapped;
        return string.IsNullOrEmpty(automationId) ? null : FindByAutomationId(automationId);
    }

    public static async Task<byte[]?> CaptureRegionAsync(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
            return null;
        var base64 = await CaptureAsync(x, y, width, height);
        return base64 is null ? null : Convert.FromBase64String(base64);
    }
}
