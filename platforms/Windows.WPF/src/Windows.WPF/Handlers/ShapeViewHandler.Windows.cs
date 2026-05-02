using WBorder = System.Windows.Controls.Border;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ShapeViewHandler : WPFViewHandler<IShapeView, WBorder>
	{
		protected override WBorder CreatePlatformView()
		{
			return new WBorder();
		}

		public override Microsoft.Maui.Graphics.Size GetDesiredSize(double widthConstraint, double heightConstraint)
		{
			var ve = VirtualView as Microsoft.Maui.Controls.VisualElement;
			if (ve != null && ve.WidthRequest > 0 && ve.HeightRequest > 0)
				return new Microsoft.Maui.Graphics.Size(ve.WidthRequest, ve.HeightRequest);
			return base.GetDesiredSize(widthConstraint, heightConstraint);
		}

		public override void PlatformArrange(Microsoft.Maui.Graphics.Rect rect)
		{
			if ((rect.Width <= 0 || rect.Height <= 0) && VirtualView is Microsoft.Maui.Controls.VisualElement ve)
			{
				var w = ve.WidthRequest > 0 ? ve.WidthRequest : rect.Width;
				var h = ve.HeightRequest > 0 ? ve.HeightRequest : rect.Height;
				if (w > 0 && h > 0)
					rect = new Microsoft.Maui.Graphics.Rect(rect.X, rect.Y, w, h);
			}
			base.PlatformArrange(rect);
		}

		static System.Windows.Media.SolidColorBrush? ToBrush(Microsoft.Maui.Graphics.Color? color)
		{
			if (color == null) return null;
			return new System.Windows.Media.SolidColorBrush(
				System.Windows.Media.Color.FromArgb(
					(byte)(color.Alpha * 255), (byte)(color.Red * 255),
					(byte)(color.Green * 255), (byte)(color.Blue * 255)));
		}

		static System.Windows.Media.Brush? ToPaintBrush(Microsoft.Maui.Graphics.Paint? paint)
		{
			if (paint is Microsoft.Maui.Graphics.SolidPaint sp && sp.Color != null)
				return ToBrush(sp.Color);
			return null;
		}

		static void ApplyShapeSize(System.Windows.FrameworkElement wpfShape, IShapeView shapeView)
		{
			// Set explicit Width/Height on child shapes to prevent them stretching
			// to fill the parent during LayoutPanel.MeasureOverride, which causes layout loops.
			var ve = shapeView as Microsoft.Maui.Controls.VisualElement;
			if (ve != null)
			{
				if (ve.WidthRequest > 0)
					wpfShape.Width = ve.WidthRequest;
				if (ve.HeightRequest > 0)
					wpfShape.Height = ve.HeightRequest;
			}
		}

		public static void MapShape(ShapeViewHandler handler, IShapeView shapeView)
		{
			if (shapeView is Microsoft.Maui.Controls.Shapes.Ellipse)
			{
				var wpfEllipse = new System.Windows.Shapes.Ellipse();
				wpfEllipse.Fill = ToPaintBrush(shapeView.Fill);
				wpfEllipse.Stroke = ToPaintBrush(shapeView.Stroke);
				wpfEllipse.StrokeThickness = shapeView.StrokeThickness;
				ApplyShapeSize(wpfEllipse, shapeView);
				handler.PlatformView.Child = wpfEllipse;
			}
			else if (shapeView is Microsoft.Maui.Controls.Shapes.Rectangle)
			{
				var wpfRect = new System.Windows.Shapes.Rectangle();
				wpfRect.Fill = ToPaintBrush(shapeView.Fill);
				wpfRect.Stroke = ToPaintBrush(shapeView.Stroke);
				wpfRect.StrokeThickness = shapeView.StrokeThickness;
				ApplyShapeSize(wpfRect, shapeView);
				handler.PlatformView.Child = wpfRect;
			}
			else if (shapeView is Microsoft.Maui.Controls.Shapes.RoundRectangle rr)
			{
				var wpfRect = new System.Windows.Shapes.Rectangle();
				wpfRect.Fill = ToPaintBrush(shapeView.Fill);
				wpfRect.Stroke = ToPaintBrush(shapeView.Stroke);
				wpfRect.StrokeThickness = shapeView.StrokeThickness;
				wpfRect.RadiusX = rr.CornerRadius.TopLeft;
				wpfRect.RadiusY = rr.CornerRadius.TopLeft;
				ApplyShapeSize(wpfRect, shapeView);
				handler.PlatformView.Child = wpfRect;
			}
			else if (shapeView is Microsoft.Maui.Controls.Shapes.Line line)
			{
				var wpfLine = new System.Windows.Shapes.Line
				{
					X1 = line.X1, Y1 = line.Y1,
					X2 = line.X2, Y2 = line.Y2,
					StrokeThickness = shapeView.StrokeThickness > 0 ? shapeView.StrokeThickness : 1,
				};
				if (shapeView.Stroke is Microsoft.Maui.Graphics.SolidPaint sp && sp.Color != null)
					wpfLine.Stroke = ToBrush(sp.Color);
				handler.PlatformView.Child = wpfLine;
			}
			else if (shapeView is Microsoft.Maui.Controls.Shapes.Path pathShape)
			{
				try
				{
					var wpfPath = new System.Windows.Shapes.Path();
					if (pathShape.Data != null)
					{
						var geoStr = pathShape.Data.ToString();
						if (!string.IsNullOrEmpty(geoStr))
							wpfPath.Data = System.Windows.Media.Geometry.Parse(geoStr);
					}
					wpfPath.StrokeThickness = shapeView.StrokeThickness > 0 ? shapeView.StrokeThickness : 1;
					wpfPath.Stroke = ToPaintBrush(shapeView.Stroke);
					wpfPath.Fill = ToPaintBrush(shapeView.Fill);
					wpfPath.Stretch = System.Windows.Media.Stretch.Uniform;
					ApplyShapeSize(wpfPath, shapeView);
					handler.PlatformView.Child = wpfPath;
				}
				catch { }
			}
			else if (shapeView is Microsoft.Maui.Controls.Shapes.Polygon polygon)
			{
				var wpfPolygon = new System.Windows.Shapes.Polygon();
				if (polygon.Points != null)
				{
					foreach (var pt in polygon.Points)
						wpfPolygon.Points.Add(new System.Windows.Point(pt.X, pt.Y));
				}
				wpfPolygon.StrokeThickness = shapeView.StrokeThickness > 0 ? shapeView.StrokeThickness : 1;
				wpfPolygon.Stroke = ToPaintBrush(shapeView.Stroke);
				wpfPolygon.Fill = ToPaintBrush(shapeView.Fill);
				wpfPolygon.Stretch = System.Windows.Media.Stretch.Uniform;
				ApplyShapeSize(wpfPolygon, shapeView);
				handler.PlatformView.Child = wpfPolygon;
			}
			else if (shapeView is Microsoft.Maui.Controls.Shapes.Polyline polyline)
			{
				var wpfPolyline = new System.Windows.Shapes.Polyline();
				if (polyline.Points != null)
				{
					foreach (var pt in polyline.Points)
						wpfPolyline.Points.Add(new System.Windows.Point(pt.X, pt.Y));
				}
				wpfPolyline.StrokeThickness = shapeView.StrokeThickness > 0 ? shapeView.StrokeThickness : 1;
				wpfPolyline.Stroke = ToPaintBrush(shapeView.Stroke);
				wpfPolyline.Fill = ToPaintBrush(shapeView.Fill);
				wpfPolyline.Stretch = System.Windows.Media.Stretch.Uniform;
				ApplyShapeSize(wpfPolyline, shapeView);
				handler.PlatformView.Child = wpfPolyline;
			}
		}

		public static void MapFill(ShapeViewHandler handler, IShapeView shapeView)
		{
			if (shapeView is Microsoft.Maui.Controls.Shapes.Line)
				return;

			// For shapes that use WPF Shape children, update the child's fill
			if (handler.PlatformView.Child is System.Windows.Shapes.Shape wpfShape)
			{
				wpfShape.Fill = ToPaintBrush(shapeView.Fill);
				return;
			}

			if (shapeView.Fill is Microsoft.Maui.Graphics.SolidPaint solidPaint && solidPaint.Color != null)
			{
				handler.PlatformView.Background = ToBrush(solidPaint.Color);
			}
		}

		public static void MapStroke(ShapeViewHandler handler, IShapeView shapeView)
		{
			if (handler.PlatformView.Child is System.Windows.Shapes.Shape wpfShape)
			{
				wpfShape.Stroke = ToPaintBrush(shapeView.Stroke);
				return;
			}

			if (shapeView.Stroke is Microsoft.Maui.Graphics.SolidPaint solidPaint && solidPaint.Color != null)
			{
				handler.PlatformView.BorderBrush = ToBrush(solidPaint.Color);
			}
		}

		public static void MapStrokeThickness(ShapeViewHandler handler, IShapeView shapeView)
		{
			if (handler.PlatformView.Child is System.Windows.Shapes.Shape wpfShape)
			{
				wpfShape.StrokeThickness = shapeView.StrokeThickness;
				return;
			}

			handler.PlatformView.BorderThickness = new System.Windows.Thickness(shapeView.StrokeThickness);
		}
	}
}
