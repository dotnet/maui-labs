using Microsoft.Maui.Primitives;

namespace Comet.Layout;

public class VStackLayoutManager : Microsoft.Maui.Layouts.ILayoutManager
{
	private readonly LayoutAlignment _defaultAlignment;
	private readonly double _spacing;

	public VStackLayoutManager(ContainerView layout, LayoutAlignment alignment = LayoutAlignment.Fill,
		double? spacing = null)
	{
		_defaultAlignment = alignment;
		_spacing = spacing ?? 4;
		this.layout = layout;
	}

	ContainerView layout;
	public Size ArrangeChildren(Rect rect)
	{
		var layoutRect = rect;
		double spacerHeight = (layoutRect.Height - childrenHeight) / spacerCount;
		foreach (var view in layout)
		{
			if (view is Spacer spacer)
			{
				var spacerConstraints = spacer.GetFrameConstraints();
				if (spacerConstraints?.Height > 0)
				{
					// Fixed-height spacer — advance by its explicit height.
					layoutRect.Y += spacerConstraints.Height.Value + _spacing;
				}
				else
				{
					layoutRect.Y += spacerHeight;
				}
				continue;
			}


			var size = view.MeasuredSize;
			var verticalSizing = view.GetVerticalLayoutAlignment(layout);
			if (verticalSizing == LayoutAlignment.Fill)
				size.Height = spacerHeight;

			layoutRect.Height = size.Height;
			view.SetFrameFromPlatformView(layoutRect,_defaultAlignment, LayoutAlignment.Start);
			layoutRect.Y = view.Frame.Bottom + _spacing;

		}
		return new Size(layoutRect.Left,layoutRect.Bottom);
	}

	int spacerCount;
	double childrenHeight;
	public Size Measure(double widthConstraint, double heightConstraint)
	{

		var index = 0;
		double width = 0;
		double height = 0;
		spacerCount = 0;
		childrenHeight = 0;
		bool hasExpandingChildren = false;

		foreach (var view in layout)
		{
			if (view is Spacer spacer)
			{
				var spacerConstraints = spacer.GetFrameConstraints();
				if (spacerConstraints?.Height > 0)
				{
					// Spacer with an explicit Frame(height:) acts as a
					// fixed-size gap, not a flexible spacer.
					var fixedHeight = spacerConstraints.Height.Value;
					spacer.MeasuredSize = new Size(0, fixedHeight);
					spacer.MeasurementValid = true;
					height += fixedHeight;
					childrenHeight += fixedHeight;
				}
				else
				{
					spacerCount++;
					if (!spacer.MeasurementValid)
					{
						spacer.MeasuredSize = new Size(-1, -1);
						spacer.MeasurementValid = true;
					}
				}

				if (index > 0)
					height += _spacing;
				index++;
				continue;
			}
			else
			{
				var size = view.Measure(widthConstraint, heightConstraint);

				var finalHeight = size.Height;
				var finalWidth = size.Width;

				var margin = view.GetMargin();
				finalHeight += margin.VerticalThickness;
				finalWidth += margin.HorizontalThickness;

				var constraints = view.GetFrameConstraints();
				var sizing = view.GetHorizontalLayoutAlignment(layout);
				if (sizing == LayoutAlignment.Fill && constraints?.Width is null && !double.IsInfinity(widthConstraint))
					width = widthConstraint;

				width = Math.Max(finalWidth, width);
				height += finalHeight;

				var verticalSizing = view.GetVerticalLayoutAlignment(layout);
				if (verticalSizing == LayoutAlignment.Fill)
				{
					spacerCount++;
					hasExpandingChildren = true;
				}
				else
					childrenHeight += finalHeight;
			}

			if (index > 0)
				height += _spacing;
			index++;
		}

		if (hasExpandingChildren)
			height = heightConstraint;

		return new Size(width, height);
	}
}


