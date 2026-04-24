using Microsoft.Maui;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Primitives;

using YogaFlexDirection = Comet.Layout.Yoga.FlexDirection;

namespace Comet.Layout;

/// <summary>
/// Overlay stack. All children occupy the same rect; per-child LayoutAlignment positions them
/// within the container bounds. Backed by a Yoga root configured for absolute positioning.
/// </summary>
public class ZStackLayoutManager : YogaStackLayoutManager
{
public ZStackLayoutManager(ILayout layout)
: base((ContainerView)layout, LayoutAlignment.Fill, spacing: 0, YogaFlexDirection.Column, absolute: true)
{
}
}
