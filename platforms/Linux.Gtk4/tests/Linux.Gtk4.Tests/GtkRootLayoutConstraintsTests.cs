using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public class GtkRootLayoutConstraintsTests
{
	[Theory]
	[InlineData(null, false, false)]
	[InlineData(ScrollOrientation.Neither, false, false)]
	[InlineData(ScrollOrientation.Vertical, false, true)]
	[InlineData(ScrollOrientation.Horizontal, true, false)]
	[InlineData(ScrollOrientation.Both, true, true)]
	public void Viewport_ConstrainsOnlyNonScrollingAxes(ScrollOrientation? orientation, bool horizontal, bool vertical)
	{
		var constraints = new GtkRootLayoutConstraints(320, 240, orientation);
		Assert.Equal(new Size(horizontal ? double.PositiveInfinity : 320, vertical ? double.PositiveInfinity : 240),
			constraints.MeasureConstraints);
		Assert.Equal(new Rect(0, 0, horizontal ? 1200 : 320, vertical ? 900 : 240),
			constraints.ArrangeBounds(new Size(1200, 900)));
		Assert.Equal(new Rect(0, 0, 320, 240), constraints.ArrangeBounds(new Size(20, 30)));
	}

	[Fact]
	public void OrientationChange_InvalidatesOtherwiseIdenticalConstraints()
	{
		var vertical = new GtkRootLayoutConstraints(320, 240, ScrollOrientation.Vertical);
		Assert.NotEqual(vertical, vertical with { Orientation = ScrollOrientation.Horizontal });
		Assert.NotEqual(vertical, vertical with { Orientation = null });
	}
}
