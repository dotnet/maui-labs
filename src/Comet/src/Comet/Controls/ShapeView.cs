using System;
using Comet.Graphics;
using Comet.Reactive;
using Microsoft.Maui.Graphics;

namespace Comet
{
	/// <summary>
	/// A view that displays a shape.
	/// </summary>
	public class ShapeView : View, IDrawable
	{
		public ShapeView(Shape value)
		{
			Shape = new PropertySubscription<Shape>(value);
		}
		public ShapeView(Func<Shape> value)
		{
			Shape = PropertySubscription<Shape>.FromFunc(value);
		}

		PropertySubscription<Shape> _shape;
		public PropertySubscription<Shape> Shape
		{
			get => _shape;
			private set => this.SetPropertySubscription(ref _shape, value);
		}

		void IDrawable.Draw(ICanvas canvas, RectF dirtyRect) {
			var padding = this.GetPadding();
			dirtyRect = dirtyRect.ApplyPadding(padding);

			var shape = Shape.CurrentValue;
			var drawingStyle = shape.GetDrawingStyle(this, DrawingStyle.StrokeFill);
			var strokeColor = shape.GetStrokeColor(this, Colors.Black);
			var strokeWidth = shape.GetLineWidth(this, 1);
			var fill = shape.GetFill(this);

			// Apply dash pattern if set on the shape
			var dashPattern = shape.GetEnvironment<float[]>(this, "StrokeDashPattern");
			if (dashPattern is not null && dashPattern.Length > 0)
				canvas.StrokeDashPattern = dashPattern;

			canvas.DrawShape(shape, dirtyRect, drawingStyle, strokeWidth, strokeColor, fill);
		}
	}
}
