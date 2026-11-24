using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Agent.Web;

/// <summary>
/// Visual tree walker for MAUI apps in the browser. Reads element geometry from the DOM, which
/// accounts for scrolling and transforms that MAUI's own frames do not.
/// </summary>
public class WebVisualTreeWalker : VisualTreeWalker
{
    protected override BoundsInfo? ResolveWindowBounds(VisualElement ve)
    {
        try
        {
            // Never dispose the element: it is usually the backend's own handle to the DOM node.
            if (WebAgentInterop.FindElement(ve.Handler?.PlatformView, ve.AutomationId) is not { } element)
                return null;

            var rect = WebAgentInterop.Rect(element);
            return new BoundsInfo { X = rect[0], Y = rect[1], Width = rect[2], Height = rect[3] };
        }
        catch
        {
            return null;
        }
    }

    protected override void PopulateNativeInfo(ElementInfo info, VisualElement ve)
    {
        try
        {
            var platformView = ve.Handler?.PlatformView;
            if (platformView is null)
                return;

            info.NativeType = platformView.GetType().FullName;
            if (WebAgentInterop.FindElement(platformView, ve.AutomationId) is not { } element)
                return;

            var description = WebAgentInterop.Describe(element);
            var properties = new Dictionary<string, string?> { ["tagName"] = description[0] };
            if (!string.IsNullOrEmpty(description[1]))
                properties["id"] = description[1];
            if (!string.IsNullOrEmpty(description[2]))
                properties["role"] = description[2];
            info.NativeProperties = properties;
        }
        catch
        {
            // Native info is best-effort; don't fail the tree walk.
        }
    }
}
