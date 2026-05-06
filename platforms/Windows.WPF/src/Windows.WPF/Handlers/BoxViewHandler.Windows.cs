using WBorder = System.Windows.Controls.Border;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class BoxViewHandler : WPFViewHandler<IShapeView, WBorder>
	{
		protected override WBorder CreatePlatformView()
		{
			return new WBorder
			{
				Background = System.Windows.Media.Brushes.Transparent,
			};
		}

		public override Microsoft.Maui.Graphics.Size GetDesiredSize(double widthConstraint, double heightConstraint)
		{
			var ve = VirtualView as Microsoft.Maui.Controls.VisualElement;
			if (ve != null)
			{
				var wr = ve.WidthRequest;
				var hr = ve.HeightRequest;
				if (wr > 0 && hr > 0)
				{
					(PlatformView as System.Windows.UIElement)?.Measure(new System.Windows.Size(wr, hr));
					return new Microsoft.Maui.Graphics.Size(wr, hr);
				}
			}
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

		public static void MapColor(BoxViewHandler handler, IShapeView view)
		{
			if (view.Fill is Microsoft.Maui.Graphics.SolidPaint solidPaint)
			{
				var brush = ToBrush(solidPaint.Color);
				if (brush != null)
					handler.PlatformView.Background = brush;
			}
			else if (view is Microsoft.Maui.Controls.BoxView bv && bv.Color != null)
			{
				var brush = ToBrush(bv.Color);
				if (brush != null)
					handler.PlatformView.Background = brush;
			}
		}

		public static void MapCornerRadius(BoxViewHandler handler, IShapeView view)
		{
			if (view.Shape is Microsoft.Maui.Controls.Shapes.RoundRectangle roundRect)
			{
				handler.PlatformView.CornerRadius = new System.Windows.CornerRadius(
					roundRect.CornerRadius.TopLeft,
					roundRect.CornerRadius.TopRight,
					roundRect.CornerRadius.BottomRight,
					roundRect.CornerRadius.BottomLeft);
			}
		}

		public static void MapBackground(BoxViewHandler handler, IShapeView view)
		{
			if (view.Background is SolidPaint solidPaint)
			{
				var brush = ToBrush(solidPaint.Color);
				if (brush != null)
					handler.PlatformView.Background = brush;
			}
		}
	}
}
