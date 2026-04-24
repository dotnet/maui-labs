using Microsoft.Maui;
using Microsoft.Maui.Primitives;

using YogaFlexDirection = Comet.Layout.Yoga.FlexDirection;

namespace Comet.Layout;

/// <summary>
/// Row-oriented stack layout. Configuration preset over <see cref="YogaStackLayoutManager"/>.
/// Constructor signature kept compatible with the existing HStack control.
/// </summary>
public class HStackLayoutManager : YogaStackLayoutManager
{
public HStackLayoutManager(
ContainerView layout,
LayoutAlignment alignment = LayoutAlignment.Fill,
float? spacing = null)
: base(layout, alignment, spacing ?? 4, YogaFlexDirection.Row, absolute: false)
{
}
}
