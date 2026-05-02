using System;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Comet.Layout
{
	public class AbsoluteLayoutManager : ILayoutManager
	{
		private readonly ILayout layout;

		public AbsoluteLayoutManager(ILayout layout) => this.layout = layout;

		public Size Measure(double widthConstraint, double heightConstraint)
		{
			double maxWidth = 0;
			double maxHeight = 0;

			foreach (var view in layout)
			{
				if (view is not View cometView)
					continue;

				var bounds = cometView.GetLayoutBounds();
				var flags = cometView.GetLayoutFlags();

				// Measure the child
				var size = view.Measure(widthConstraint, heightConstraint);

				// Calculate absolute bounds
				var x = (flags & AbsoluteLayoutFlags.XProportional) != 0 ? bounds.X * widthConstraint : bounds.X;
				var y = (flags & AbsoluteLayoutFlags.YProportional) != 0 ? bounds.Y * heightConstraint : bounds.Y;
				var width = (flags & AbsoluteLayoutFlags.WidthProportional) != 0 ? bounds.Width * widthConstraint : bounds.Width;
				var height = (flags & AbsoluteLayoutFlags.HeightProportional) != 0 ? bounds.Height * heightConstraint : bounds.Height;

				// Use measured size if bounds specify -1 (AutoSize)
				if (width < 0)
					width = size.Width;
				if (height < 0)
					height = size.Height;

				maxWidth = Math.Max(maxWidth, x + width);
				maxHeight = Math.Max(maxHeight, y + height);
			}

			return new Size(
				double.IsInfinity(widthConstraint) ? maxWidth : widthConstraint,
				double.IsInfinity(heightConstraint) ? maxHeight : heightConstraint
			);
		}

		public Size ArrangeChildren(Rect bounds)
		{
			foreach (var view in layout)
			{
				if (view is not View cometView)
					continue;

				var childBounds = cometView.GetLayoutBounds();
				var flags = cometView.GetLayoutFlags();

				var x = (flags & AbsoluteLayoutFlags.XProportional) != 0 ? childBounds.X * bounds.Width : childBounds.X;
				var y = (flags & AbsoluteLayoutFlags.YProportional) != 0 ? childBounds.Y * bounds.Height : childBounds.Y;
				var width = (flags & AbsoluteLayoutFlags.WidthProportional) != 0 ? childBounds.Width * bounds.Width : childBounds.Width;
				var height = (flags & AbsoluteLayoutFlags.HeightProportional) != 0 ? childBounds.Height * bounds.Height : childBounds.Height;

				// Use measured size if bounds specify -1 (AutoSize)
				if (width < 0)
					width = cometView.MeasuredSize.Width;
				if (height < 0)
					height = cometView.MeasuredSize.Height;

				var finalBounds = new Rect(bounds.X + x, bounds.Y + y, width, height);

				if (view is View cv)
					cv.LayoutSubviews(finalBounds);
				else
					view.Arrange(finalBounds);
			}

			return bounds.Size;
		}
	}
}
