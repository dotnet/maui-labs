using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Maui.Handlers;
using WImage = System.Windows.Controls.Image;
using WBorder = System.Windows.Controls.Border;
using GHorizontalAlignment = Microsoft.Maui.Graphics.HorizontalAlignment;
using GVerticalAlignment = Microsoft.Maui.Graphics.VerticalAlignment;

namespace Microsoft.Maui.Handlers.WPF
{
	/// <summary>
	/// GraphicsView handler — renders IDrawable on a WPF Image via WriteableBitmap + WPF DrawingContext.
	/// Uses a simplified canvas adapter for basic drawing operations.
	/// </summary>
	public partial class GraphicsViewHandler : WPFViewHandler<IGraphicsView, WBorder>
	{
		WImage? _image;

		public static readonly PropertyMapper<IGraphicsView, GraphicsViewHandler> Mapper =
			new(ViewMapper)
			{
				[nameof(IGraphicsView.Drawable)] = MapDrawable,
			};

		public static readonly CommandMapper<IGraphicsView, GraphicsViewHandler> CommandMapper =
			new(ViewCommandMapper)
			{
				["Invalidate"] = MapInvalidate,
			};

		public GraphicsViewHandler() : base(Mapper, CommandMapper) { }

		protected override WBorder CreatePlatformView()
		{
			var WBorder = new WBorder();
			_image = new WImage { Stretch = System.Windows.Media.Stretch.None };
			WBorder.Child = _image;
			WBorder.SizeChanged += OnSizeChanged;

			// Wire mouse events for touch/interaction
			WBorder.MouseLeftButtonDown += (s, e) =>
			{
				var pos = e.GetPosition(WBorder);
				try
				{
					VirtualView?.StartInteraction(new[] { new Microsoft.Maui.Graphics.PointF((float)pos.X, (float)pos.Y) });
				}
				catch { }
			};
			WBorder.MouseMove += (s, e) =>
			{
				if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
				{
					var pos = e.GetPosition(WBorder);
					try
					{
						VirtualView?.DragInteraction(new[] { new Microsoft.Maui.Graphics.PointF((float)pos.X, (float)pos.Y) });
					}
					catch { }
				}
				else
				{
					var pos = e.GetPosition(WBorder);
					try
					{
						VirtualView?.MoveHoverInteraction(new[] { new Microsoft.Maui.Graphics.PointF((float)pos.X, (float)pos.Y) });
					}
					catch { }
				}
			};
			WBorder.MouseLeftButtonUp += (s, e) =>
			{
				var pos = e.GetPosition(WBorder);
				try
				{
					VirtualView?.EndInteraction(new[] { new Microsoft.Maui.Graphics.PointF((float)pos.X, (float)pos.Y) }, true);
				}
				catch { }
			};

			return WBorder;
		}

		void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

		void Redraw()
		{
			try
			{
				if (VirtualView?.Drawable == null || _image == null || PlatformView == null) return;

				int width = (int)PlatformView.ActualWidth;
				int height = (int)PlatformView.ActualHeight;
				if (width <= 0 || height <= 0) return;

				var drawingVisual = new DrawingVisual();
				using (var dc = drawingVisual.RenderOpen())
				{
					var canvas = new WpfCanvas(dc, width, height);
					VirtualView.Drawable.Draw(canvas, new Microsoft.Maui.Graphics.RectF(0, 0, width, height));
				}

				var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
				bitmap.Render(drawingVisual);
				bitmap.Freeze();
				_image.Source = bitmap;
			}
			catch { }
		}

		static void MapDrawable(GraphicsViewHandler handler, IGraphicsView view) => handler.Redraw();
		static void MapInvalidate(GraphicsViewHandler handler, IGraphicsView view, object? args) => handler.Redraw();

		protected override void DisconnectHandler(WBorder platformView)
		{
			platformView.SizeChanged -= OnSizeChanged;
			base.DisconnectHandler(platformView);
		}
	}

	/// <summary>
	/// WPF ICanvas implementation bridging MAUI Graphics to WPF DrawingContext.
	/// </summary>
	internal class WpfCanvas : Microsoft.Maui.Graphics.ICanvas
	{
		readonly DrawingContext _dc;
		readonly int _w, _h;
		System.Windows.Media.Color _stroke = System.Windows.Media.Colors.Black;
		System.Windows.Media.Color _fill = System.Windows.Media.Colors.Transparent;
		System.Windows.Media.Color _fontColor = System.Windows.Media.Colors.Black;
		double _strokeSize = 1;
		float _fontSize = 14;
		string _fontName = "Segoe UI";
		bool _fontBold, _fontItalic;
		float _alpha = 1;

		public WpfCanvas(DrawingContext dc, int w, int h) { _dc = dc; _w = w; _h = h; }

		System.Windows.Media.Pen Pen() => new(new System.Windows.Media.SolidColorBrush(A(_stroke)), _strokeSize);
		System.Windows.Media.Brush Fill() => new System.Windows.Media.SolidColorBrush(A(_fill));
		System.Windows.Media.Color A(System.Windows.Media.Color c) =>
			System.Windows.Media.Color.FromArgb((byte)(c.A * _alpha), c.R, c.G, c.B);
		static System.Windows.Media.Color C(Microsoft.Maui.Graphics.Color? c) =>
			c != null ? System.Windows.Media.Color.FromArgb((byte)(c.Alpha * 255), (byte)(c.Red * 255), (byte)(c.Green * 255), (byte)(c.Blue * 255)) : System.Windows.Media.Colors.Black;

		// Properties
		public float DisplayScale { get; set; } = 1;
		public float StrokeSize { get => (float)_strokeSize; set => _strokeSize = value; }
		public float MiterLimit { get; set; } = 10;
		public Microsoft.Maui.Graphics.LineCap StrokeLineCap { get; set; }
		public Microsoft.Maui.Graphics.LineJoin StrokeLineJoin { get; set; }
		public float[] StrokeDashPattern { get; set; } = Array.Empty<float>();
		public float StrokeDashOffset { get; set; }
		public Microsoft.Maui.Graphics.Color StrokeColor { set => _stroke = C(value); }
		public Microsoft.Maui.Graphics.Color FillColor { set => _fill = C(value); }
		public Microsoft.Maui.Graphics.Color FontColor { set => _fontColor = C(value); }
		public Microsoft.Maui.Graphics.IFont? Font
		{
			set
			{
				if (value != null)
				{
					_fontName = value.Name ?? "Segoe UI";
					_fontBold = value.Weight >= 600;
					_fontItalic = value.StyleType == Microsoft.Maui.Graphics.FontStyleType.Italic;
				}
			}
		}
		public float FontSize { get => _fontSize; set => _fontSize = value; }
		public float Alpha { get => _alpha; set => _alpha = value; }
		public bool Antialias { get; set; } = true;
		public Microsoft.Maui.Graphics.BlendMode BlendMode { get; set; }

		// Colors (method variants)
		public void SetStrokeColor(Microsoft.Maui.Graphics.Color color) => _stroke = C(color);
		public void SetFillColor(Microsoft.Maui.Graphics.Color color) => _fill = C(color);
		public void SetFont(Microsoft.Maui.Graphics.IFont font) => Font = font;
		public void SetFillPaint(Microsoft.Maui.Graphics.Paint paint, Microsoft.Maui.Graphics.RectF rect)
		{
			if (paint is Microsoft.Maui.Graphics.SolidPaint sp && sp.Color != null)
				_fill = C(sp.Color);
		}
		public void SetShadow(Microsoft.Maui.Graphics.SizeF offset, float blur, Microsoft.Maui.Graphics.Color color) { }

		// Drawing
		public void DrawLine(float x1, float y1, float x2, float y2) =>
			_dc.DrawLine(Pen(), new System.Windows.Point(x1, y1), new System.Windows.Point(x2, y2));
		public void DrawRectangle(float x, float y, float w, float h) =>
			_dc.DrawRectangle(null, Pen(), new System.Windows.Rect(x, y, w, h));
		public void FillRectangle(float x, float y, float w, float h) =>
			_dc.DrawRectangle(Fill(), null, new System.Windows.Rect(x, y, w, h));
		public void DrawRoundedRectangle(float x, float y, float w, float h, float rx, float ry) =>
			_dc.DrawRoundedRectangle(null, Pen(), new System.Windows.Rect(x, y, w, h), rx, ry);
		public void FillRoundedRectangle(float x, float y, float w, float h, float rx, float ry) =>
			_dc.DrawRoundedRectangle(Fill(), null, new System.Windows.Rect(x, y, w, h), rx, ry);
		public void DrawRoundedRectangle(float x, float y, float w, float h, float r) =>
			DrawRoundedRectangle(x, y, w, h, r, r);
		public void FillRoundedRectangle(float x, float y, float w, float h, float r) =>
			FillRoundedRectangle(x, y, w, h, r, r);
		public void DrawEllipse(float x, float y, float w, float h) =>
			_dc.DrawEllipse(null, Pen(), new System.Windows.Point(x + w / 2, y + h / 2), w / 2, h / 2);
		public void FillEllipse(float x, float y, float w, float h) =>
			_dc.DrawEllipse(Fill(), null, new System.Windows.Point(x + w / 2, y + h / 2), w / 2, h / 2);
		public void DrawArc(float x, float y, float w, float h, float startAngle, float endAngle, bool clockwise, bool closed) { }
		public void FillArc(float x, float y, float w, float h, float startAngle, float endAngle, bool clockwise) { }

		// Text
		FormattedText MakeFormattedText(string value) =>
			new(value, System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
				new Typeface(new System.Windows.Media.FontFamily(_fontName),
					_fontItalic ? FontStyles.Italic : FontStyles.Normal,
					_fontBold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal, FontStretches.Normal),
				_fontSize, new System.Windows.Media.SolidColorBrush(A(_fontColor)), 96);

		public void DrawString(string value, float x, float y, GHorizontalAlignment ha) =>
			_dc.DrawText(MakeFormattedText(value), new System.Windows.Point(x, y));
		public void DrawString(string value, float x, float y, float w, float h, GHorizontalAlignment ha, GVerticalAlignment va, Microsoft.Maui.Graphics.TextFlow tf = default, float lsh = 0) =>
			_dc.DrawText(MakeFormattedText(value), new System.Windows.Point(x, y));
		public void DrawText(Microsoft.Maui.Graphics.Text.IAttributedText text, float x, float y, float w, float h) =>
			_dc.DrawText(MakeFormattedText(text?.Text ?? ""), new System.Windows.Point(x, y));

		// Path
		public void DrawPath(Microsoft.Maui.Graphics.PathF path)
		{
			try
			{
				var s = path.ToDefinitionString();
				if (!string.IsNullOrEmpty(s))
					_dc.DrawGeometry(null, Pen(), Geometry.Parse(s));
			}
			catch { }
		}
		public void FillPath(Microsoft.Maui.Graphics.PathF path, Microsoft.Maui.Graphics.WindingMode winding)
		{
			try
			{
				var s = path.ToDefinitionString();
				if (!string.IsNullOrEmpty(s))
					_dc.DrawGeometry(Fill(), null, Geometry.Parse(s));
			}
			catch { }
		}

		// Transforms
		public void Translate(float tx, float ty) => _dc.PushTransform(new TranslateTransform(tx, ty));
		public void Scale(float sx, float sy) => _dc.PushTransform(new ScaleTransform(sx, sy));
		public void Rotate(float degrees) => _dc.PushTransform(new RotateTransform(degrees));
		public void Rotate(float degrees, float cx, float cy) => _dc.PushTransform(new RotateTransform(degrees, cx, cy));
		public void ConcatenateTransform(System.Numerics.Matrix3x2 transform) { }

		// State
		public void SaveState() { }
		public bool RestoreState() { return true; }
		public void ResetState() { }

		// Clip
		public void ClipRectangle(float x, float y, float w, float h) =>
			_dc.PushClip(new RectangleGeometry(new System.Windows.Rect(x, y, w, h)));
		public void ClipPath(Microsoft.Maui.Graphics.PathF path, Microsoft.Maui.Graphics.WindingMode winding) { }
		public void SubtractFromClip(float x, float y, float w, float h) { }

		// Image
		public void DrawImage(Microsoft.Maui.Graphics.IImage image, float x, float y, float w, float h) { }

		// Measure
		public Microsoft.Maui.Graphics.SizeF GetStringSize(string value, Microsoft.Maui.Graphics.IFont font, float fontSize)
		{
			var ft = new FormattedText(value, System.Globalization.CultureInfo.CurrentCulture,
				System.Windows.FlowDirection.LeftToRight, new Typeface(font?.Name ?? "Segoe UI"),
				fontSize, Brushes.Black, 96);
			return new Microsoft.Maui.Graphics.SizeF((float)ft.Width, (float)ft.Height);
		}
		public Microsoft.Maui.Graphics.SizeF GetStringSize(string value, Microsoft.Maui.Graphics.IFont font, float fontSize, GHorizontalAlignment ha, GVerticalAlignment va)
			=> GetStringSize(value, font, fontSize);
	}
}



