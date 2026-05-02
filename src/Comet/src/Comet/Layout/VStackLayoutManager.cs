using Microsoft.Maui;
using Microsoft.Maui.Primitives;

using YogaFlexDirection = Comet.Layout.Yoga.FlexDirection;

namespace Comet.Layout;

/// <summary>
/// Column-oriented stack layout. Configuration preset over <see cref="YogaStackLayoutManager"/>.
/// Constructor signature kept compatible with the existing VStack control so callers aren't touched.
/// </summary>
public class VStackLayoutManager : YogaStackLayoutManager
{
public VStackLayoutManager(
ContainerView layout,
LayoutAlignment alignment = LayoutAlignment.Fill,
double? spacing = null)
: base(layout, alignment, spacing ?? 4, YogaFlexDirection.Column, absolute: false)
{
}
}
