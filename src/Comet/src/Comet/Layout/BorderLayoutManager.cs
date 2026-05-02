using System;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Comet.Layout
{
	public class BorderLayoutManager : ILayoutManager
	{
		readonly ILayout layout;

		public BorderLayoutManager(ILayout layout) => this.layout = layout;

		public Size Measure(double widthConstraint, double heightConstraint)
		{
			Size measuredSize = new();
			foreach (var child in layout)
			{
				var s = child.Measure(widthConstraint, heightConstraint);
				measuredSize.Width = Math.Max(measuredSize.Width, s.Width);
				measuredSize.Height = Math.Max(measuredSize.Height, s.Height);
			}
			return measuredSize;
		}

		public Size ArrangeChildren(Rect bounds)
		{
			foreach (var child in layout)
			{
				if (child is View view)
					view.LayoutSubviews(bounds);
				else
					child.Arrange(bounds);
			}
			return bounds.Size;
		}
	}
}
