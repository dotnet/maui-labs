using System;
using AppKit;
using CoreGraphics;
using Comet.Graphics;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;

namespace Comet.MacOS
{
	public class CUIShapeNSView : NSView
	{
		private static readonly CGColorSpace ColorSpace = CGColorSpace.CreateDeviceRGB();

		private Shape _shape;

		public CUIShapeNSView()
		{
			WantsLayer = true;
		}

		public Shape Shape
		{
			get => _shape;
			set
			{
				_shape = value;
				NeedsDisplay = true;
			}
		}

		public View View { get; set; }

		public override bool IsFlipped => true;

		public override void DrawRect(CGRect dirtyRect)
		{
			var context = NSGraphicsContext.CurrentContext?.CGContext;
			if (context is null || Shape is null)
				return;

			var drawingStyle = Shape.GetDrawingStyle(View, DrawingStyle.StrokeFill);

			var lineWidth = Shape.GetLineWidth(View, 1);
			var strokeColor = Colors.Black;
			object fill = null;

			if (drawingStyle == DrawingStyle.Fill || drawingStyle == DrawingStyle.StrokeFill)
			{
				fill = Shape.GetFill(View);
			}

			var shapeBounds = new RectF(
				(float)dirtyRect.X + (lineWidth / 2),
				(float)dirtyRect.Y + (lineWidth / 2),
				(float)dirtyRect.Width - lineWidth,
				(float)dirtyRect.Height - lineWidth);

			var path = Shape.PathForBounds(shapeBounds).AsCGPath();

			if (fill is not null)
			{
				if (fill is Color color)
				{
					context.SetFillColor(ColorToCG(color));
					context.AddPath(path);
					context.FillPath();
				}
				else if (fill is Gradient gradient)
				{
					context.SaveState();
					context.AddPath(path);
					context.Clip();

					var gradientColors = new nfloat[gradient.Stops.Length * 4];
					var offsets = new nfloat[gradient.Stops.Length];

					int g = 0;
					for (int i = 0; i < gradient.Stops.Length; i++)
					{
						var stopColor = gradient.Stops[i].Color;
						offsets[i] = gradient.Stops[i].Offset;

						if (stopColor is null) stopColor = Colors.White;

						gradientColors[g++] = stopColor.Red;
						gradientColors[g++] = stopColor.Green;
						gradientColors[g++] = stopColor.Blue;
						gradientColors[g++] = stopColor.Alpha;
					}

					var cgGradient = new CGGradient(CGColorSpace.CreateDeviceRGB(), gradientColors, offsets);

					if (gradient is LinearGradient linearGradient)
					{
						var gradientStart = new CGPoint(
							(float)dirtyRect.X + dirtyRect.Width * linearGradient.StartPoint.X,
							(float)dirtyRect.Y + dirtyRect.Height * linearGradient.StartPoint.Y);

						var gradientEnd = new CGPoint(
							(float)dirtyRect.X + dirtyRect.Width * linearGradient.EndPoint.X,
							(float)dirtyRect.Y + dirtyRect.Height * linearGradient.EndPoint.Y);

						context.DrawLinearGradient(
							cgGradient,
							gradientStart,
							gradientEnd,
							CGGradientDrawingOptions.DrawsAfterEndLocation | CGGradientDrawingOptions.DrawsBeforeStartLocation);
					}
					else if (gradient is RadialGradient radialGradient)
					{
						var radialFocalPoint = new CGPoint(
							(float)dirtyRect.X + dirtyRect.Width * radialGradient.Center.X,
							(float)dirtyRect.Y + dirtyRect.Height * radialGradient.Center.Y);

						context.DrawRadialGradient(
							cgGradient,
							radialFocalPoint,
							radialGradient.StartRadius,
							radialFocalPoint,
							radialGradient.EndRadius,
							CGGradientDrawingOptions.DrawsBeforeStartLocation | CGGradientDrawingOptions.DrawsAfterEndLocation);
					}

					cgGradient.Dispose();
					context.RestoreState();
				}
			}

			if (drawingStyle == DrawingStyle.Stroke || drawingStyle == DrawingStyle.StrokeFill)
			{
				strokeColor = Shape.GetStrokeColor(View, Colors.Black);

				context.SetLineWidth(lineWidth);
				context.SetStrokeColor(ColorToCG(strokeColor));

				context.AddPath(path);
				context.StrokePath();
			}
		}
		static CGColor ColorToCG(Color color)
			=> CGColor.CreateSrgb(
				(nfloat)color.Red, (nfloat)color.Green,
				(nfloat)color.Blue, (nfloat)color.Alpha);
	}
}
