using System.Numerics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

/// <summary>
/// Exercises all CairoCanvas features implemented for issue #11.
/// Each GraphicsView section targets a specific feature set.
/// </summary>
public class GraphicsFeaturePage : ContentPage
{
	private Label _interactionLog;

	public GraphicsFeaturePage()
	{
		Title = "Graphics Features";

		_interactionLog = new Label
		{
			Text = "Tap, drag, or hover the interactive GraphicsView below",
			FontSize = 11,
			TextColor = Colors.Gray,
		};

		var interactiveView = new GraphicsView
		{
			HeightRequest = 120,
			Drawable = new InteractionDrawable(),
		};

		// Wire interaction events
		interactiveView.StartInteraction += (s, e) =>
			_interactionLog.Text = $"StartInteraction at ({e.Touches[0].X:F0}, {e.Touches[0].Y:F0})";
		interactiveView.EndInteraction += (s, e) =>
			_interactionLog.Text = $"EndInteraction at ({e.Touches[0].X:F0}, {e.Touches[0].Y:F0})";
		interactiveView.DragInteraction += (s, e) =>
			_interactionLog.Text = $"DragInteraction at ({e.Touches[0].X:F0}, {e.Touches[0].Y:F0})";
		interactiveView.StartHoverInteraction += (s, e) =>
			_interactionLog.Text = $"Hover at ({e.Touches[0].X:F0}, {e.Touches[0].Y:F0})";
		interactiveView.MoveHoverInteraction += (s, e) =>
			_interactionLog.Text = $"MoveHover at ({e.Touches[0].X:F0}, {e.Touches[0].Y:F0})";
		interactiveView.EndHoverInteraction += (s, e) =>
			_interactionLog.Text = "EndHoverInteraction";

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 12,
				Padding = new Thickness(24),
				Children =
				{
					new Label { Text = "Graphics Feature Tests", FontSize = 22, FontAttributes = FontAttributes.Bold },
					new Label { Text = "Each section exercises a specific CairoCanvas capability.", FontSize = 12, TextColor = Colors.Gray },
					new BoxView { HeightRequest = 2, Color = Colors.DodgerBlue },

					Section("1. Path Operations (Bezier, Quad, Arc, Close)"),
					new GraphicsView { HeightRequest = 160, Drawable = new PathOpsDrawable() },

					Section("2. Stroke Properties (Dash, Caps, Joins, Miter)"),
					new GraphicsView { HeightRequest = 160, Drawable = new StrokePropsDrawable() },

					Section("3. Gradient Paint (Linear & Radial)"),
					new GraphicsView { HeightRequest = 160, Drawable = new GradientDrawable() },

					Section("4. Text Alignment, Font Weight & Style"),
					new GraphicsView { HeightRequest = 160, Drawable = new TextFeaturesDrawable() },

					Section("5. ConcatenateTransform"),
					new GraphicsView { HeightRequest = 160, Drawable = new TransformDrawable() },

					Section("6. Shadow Rendering"),
					new GraphicsView { HeightRequest = 160, Drawable = new ShadowDrawable() },

					Section("7. Antialias & BlendMode"),
					new GraphicsView { HeightRequest = 140, Drawable = new AntialiasBlendDrawable() },

					Section("8. SubtractFromClip"),
					new GraphicsView { HeightRequest = 140, Drawable = new ClipDrawable() },

					Section("9. Interaction Events (click, drag, hover)"),
					interactiveView,
					_interactionLog,

					Section("10. Multi-line Text Wrapping (Pango)"),
					new GraphicsView { HeightRequest = 200, Drawable = new TextWrapDrawable() },
				}
			}
		};
	}

	private static Label Section(string title) => new()
	{
		Text = title,
		FontSize = 14,
		FontAttributes = FontAttributes.Bold,
		Margin = new Thickness(0, 8, 0, 0),
	};
}

// ── 1. Path Operations ──────────────────────────────────────────────────────

class PathOpsDrawable : IDrawable
{
	public void Draw(ICanvas canvas, RectF rect)
	{
		canvas.FillColor = Color.FromArgb("#f0f4f8");
		canvas.FillRectangle(rect);

		// Cubic bezier curve
		var cubic = new PathF();
		cubic.MoveTo(20, 120);
		cubic.CurveTo(60, 20, 120, 20, 160, 120);
		canvas.StrokeColor = Colors.DodgerBlue;
		canvas.StrokeSize = 3;
		canvas.DrawPath(cubic);
		canvas.FontSize = 10;
		canvas.FontColor = Colors.DodgerBlue;
		canvas.DrawString("Cubic Bezier", 50, 130, HorizontalAlignment.Left);

		// Quadratic bezier curve
		var quad = new PathF();
		quad.MoveTo(190, 120);
		quad.QuadTo(250, 20, 310, 120);
		canvas.StrokeColor = Colors.Coral;
		canvas.DrawPath(quad);
		canvas.FontColor = Colors.Coral;
		canvas.DrawString("Quad Bezier", 210, 130, HorizontalAlignment.Left);

		// Arc
		canvas.StrokeColor = Colors.MediumSeaGreen;
		canvas.DrawArc(340, 20, 100, 100, 0, 270, true, false);
		canvas.FontColor = Colors.MediumSeaGreen;
		canvas.DrawString("Arc (270°)", 350, 130, HorizontalAlignment.Left);

		// Closed path (triangle)
		var tri = new PathF();
		tri.MoveTo(500, 120);
		tri.LineTo(540, 30);
		tri.LineTo(580, 120);
		tri.Close();
		canvas.FillColor = Color.FromRgba(155, 89, 182, 128);
		canvas.FillPath(tri);
		canvas.StrokeColor = Colors.Purple;
		canvas.DrawPath(tri);
		canvas.FontColor = Colors.Purple;
		canvas.DrawString("Closed Path", 500, 130, HorizontalAlignment.Left);
	}
}

// ── 2. Stroke Properties ────────────────────────────────────────────────────

class StrokePropsDrawable : IDrawable
{
	public void Draw(ICanvas canvas, RectF rect)
	{
		canvas.FillColor = Color.FromArgb("#f0f4f8");
		canvas.FillRectangle(rect);

		float y = 30;

		// Dash pattern
		canvas.StrokeColor = Colors.DodgerBlue;
		canvas.StrokeSize = 3;
		canvas.StrokeDashPattern = [10, 5];
		canvas.DrawLine(20, y, 180, y);
		canvas.FontSize = 10;
		canvas.FontColor = Colors.DodgerBlue;
		canvas.DrawString("Dash [10,5]", 20, y + 8, HorizontalAlignment.Left);

		// Dash-dot pattern
		y += 30;
		canvas.StrokeDashPattern = [15, 5, 3, 5];
		canvas.DrawLine(20, y, 180, y);
		canvas.DrawString("Dash-dot [15,5,3,5]", 20, y + 8, HorizontalAlignment.Left);

		// Reset dash, show line caps
		canvas.StrokeDashPattern = [];
		canvas.StrokeSize = 8;

		// Butt cap
		y += 40;
		canvas.StrokeLineCap = LineCap.Butt;
		canvas.StrokeColor = Colors.Crimson;
		canvas.DrawLine(220, y, 320, y);
		canvas.FontSize = 10;
		canvas.FontColor = Colors.Crimson;
		canvas.DrawString("Butt Cap", 220, y + 14, HorizontalAlignment.Left);

		// Round cap
		canvas.StrokeLineCap = LineCap.Round;
		canvas.DrawLine(340, y, 440, y);
		canvas.DrawString("Round Cap", 340, y + 14, HorizontalAlignment.Left);

		// Square cap
		canvas.StrokeLineCap = LineCap.Square;
		canvas.DrawLine(460, y, 560, y);
		canvas.DrawString("Square Cap", 460, y + 14, HorizontalAlignment.Left);

		// Line joins
		canvas.StrokeLineCap = LineCap.Butt;
		canvas.StrokeSize = 5;
		canvas.StrokeColor = Colors.Teal;

		// Miter join
		var miter = new PathF();
		miter.MoveTo(20, 140);
		miter.LineTo(50, 110);
		miter.LineTo(80, 140);
		canvas.StrokeLineJoin = LineJoin.Miter;
		canvas.DrawPath(miter);
		canvas.FontColor = Colors.Teal;
		canvas.DrawString("Miter", 30, 145, HorizontalAlignment.Left);

		// Round join
		var round = new PathF();
		round.MoveTo(120, 140);
		round.LineTo(150, 110);
		round.LineTo(180, 140);
		canvas.StrokeLineJoin = LineJoin.Round;
		canvas.DrawPath(round);
		canvas.DrawString("Round", 130, 145, HorizontalAlignment.Left);

		// Bevel join
		var bevel = new PathF();
		bevel.MoveTo(220, 140);
		bevel.LineTo(250, 110);
		bevel.LineTo(280, 140);
		canvas.StrokeLineJoin = LineJoin.Bevel;
		canvas.DrawPath(bevel);
		canvas.DrawString("Bevel", 230, 145, HorizontalAlignment.Left);
	}
}

// ── 3. Gradient Paint ───────────────────────────────────────────────────────

class GradientDrawable : IDrawable
{
	public void Draw(ICanvas canvas, RectF rect)
	{
		canvas.FillColor = Color.FromArgb("#f0f4f8");
		canvas.FillRectangle(rect);

		// Linear gradient rectangle
		var linearRect = new RectF(20, 20, 200, 100);
		var linearPaint = new LinearGradientPaint
		{
			StartPoint = new Point(0, 0),
			EndPoint = new Point(1, 1),
			GradientStops =
			[
				new PaintGradientStop(0, Colors.DodgerBlue),
				new PaintGradientStop(0.5f, Colors.MediumPurple),
				new PaintGradientStop(1, Colors.Coral),
			]
		};
		canvas.SetFillPaint(linearPaint, linearRect);
		canvas.FillRoundedRectangle(linearRect, 12);

		canvas.FontSize = 11;
		canvas.FontColor = Colors.DarkSlateGray;
		canvas.DrawString("Linear Gradient", 60, 130, HorizontalAlignment.Left);

		// Radial gradient circle
		var radialRect = new RectF(280, 15, 120, 120);
		var radialPaint = new RadialGradientPaint
		{
			Center = new Point(0.5, 0.5),
			Radius = 0.5,
			GradientStops =
			[
				new PaintGradientStop(0, Colors.White),
				new PaintGradientStop(0.5f, Colors.Gold),
				new PaintGradientStop(1, Colors.OrangeRed),
			]
		};
		canvas.SetFillPaint(radialPaint, radialRect);
		canvas.FillEllipse(radialRect);
		canvas.DrawString("Radial Gradient", 290, 140, HorizontalAlignment.Left);

		// Gradient on path
		var pathRect = new RectF(460, 20, 120, 100);
		var pathPaint = new LinearGradientPaint
		{
			StartPoint = new Point(0, 0),
			EndPoint = new Point(0, 1),
			GradientStops =
			[
				new PaintGradientStop(0, Colors.LimeGreen),
				new PaintGradientStop(1, Colors.DarkGreen),
			]
		};
		canvas.SetFillPaint(pathPaint, pathRect);

		var star = new PathF();
		float cx = 520, cy = 70, r = 45;
		for (int i = 0; i < 5; i++)
		{
			float angle = (float)(i * 4 * Math.PI / 5 - Math.PI / 2);
			float px = cx + r * (float)Math.Cos(angle);
			float py = cy + r * (float)Math.Sin(angle);
			if (i == 0) star.MoveTo(px, py);
			else star.LineTo(px, py);
		}
		star.Close();
		canvas.FillPath(star);

		canvas.FontColor = Colors.DarkSlateGray;
		canvas.DrawString("Gradient Path", 470, 130, HorizontalAlignment.Left);
	}
}

// ── 4. Text Features ────────────────────────────────────────────────────────

class TextFeaturesDrawable : IDrawable
{
	public void Draw(ICanvas canvas, RectF rect)
	{
		canvas.FillColor = Color.FromArgb("#f0f4f8");
		canvas.FillRectangle(rect);

		// Horizontal alignment
		canvas.FontColor = Colors.DodgerBlue;
		canvas.FontSize = 12;

		float midX = rect.Width / 4;

		// Guide line
		canvas.StrokeColor = Color.FromRgba(100, 100, 100, 60);
		canvas.StrokeSize = 1;
		canvas.StrokeDashPattern = [3, 3];
		canvas.DrawLine(midX, 10, midX, 90);
		canvas.StrokeDashPattern = [];

		canvas.FontColor = Colors.DodgerBlue;
		canvas.DrawString("Left aligned", midX, 12, HorizontalAlignment.Left);
		canvas.DrawString("Center aligned", midX, 32, HorizontalAlignment.Center);
		canvas.DrawString("Right aligned", midX, 52, HorizontalAlignment.Right);

		// Vertical alignment in box
		float boxX = rect.Width / 2 + 20;
		float boxW = 140, boxH = 30;

		canvas.StrokeColor = Colors.Gray;
		canvas.StrokeSize = 1;
		canvas.DrawRectangle(boxX, 10, boxW, boxH);
		canvas.FontColor = Colors.Crimson;
		canvas.DrawString("VTop", boxX, 10, boxW, boxH, HorizontalAlignment.Center, VerticalAlignment.Top);

		canvas.DrawRectangle(boxX, 50, boxW, boxH);
		canvas.DrawString("VCenter", boxX, 50, boxW, boxH, HorizontalAlignment.Center, VerticalAlignment.Center);

		canvas.DrawRectangle(boxX, 90, boxW, boxH);
		canvas.DrawString("VBottom", boxX, 90, boxW, boxH, HorizontalAlignment.Center, VerticalAlignment.Bottom);

		// Font weight
		canvas.FontSize = 14;
		canvas.FontColor = Colors.DarkSlateGray;
		canvas.Font = new Microsoft.Maui.Graphics.Font("Sans", 800); // Bold
		canvas.DrawString("Bold (weight 800)", 20, 100, HorizontalAlignment.Left);

		// Font style
		canvas.Font = new Microsoft.Maui.Graphics.Font("Sans", 400, FontStyleType.Italic);
		canvas.DrawString("Italic (style)", 20, 120, HorizontalAlignment.Left);

		canvas.Font = new Microsoft.Maui.Graphics.Font("Sans", 800, FontStyleType.Italic);
		canvas.DrawString("Bold + Italic", 20, 140, HorizontalAlignment.Left);

		// GetStringSize
		var measureFont = Microsoft.Maui.Graphics.Font.Default;
		float measureFontSize = 12;
		canvas.Font = measureFont;
		canvas.FontSize = measureFontSize;
		string measured = "Measured Text";
		var size = canvas.GetStringSize(measured, measureFont, measureFontSize);
		canvas.FillColor = Color.FromRgba(52, 152, 219, 40);
		canvas.FillRectangle(boxX, 130, size.Width, size.Height);
		canvas.FontColor = Colors.DodgerBlue;
		canvas.DrawString(measured, boxX, 130, HorizontalAlignment.Left);
		canvas.FontSize = 9;
		canvas.FontColor = Colors.Gray;
		canvas.DrawString($"({size.Width:F0}×{size.Height:F0}px)", boxX + size.Width + 4, 132, HorizontalAlignment.Left);
	}
}

// ── 5. ConcatenateTransform ─────────────────────────────────────────────────

class TransformDrawable : IDrawable
{
	public void Draw(ICanvas canvas, RectF rect)
	{
		canvas.FillColor = Color.FromArgb("#f0f4f8");
		canvas.FillRectangle(rect);

		// Original rectangle
		canvas.StrokeColor = Colors.Gray;
		canvas.StrokeSize = 1;
		canvas.StrokeDashPattern = [4, 4];
		canvas.DrawRectangle(50, 30, 80, 50);
		canvas.FontSize = 10;
		canvas.FontColor = Colors.Gray;
		canvas.DrawString("Original", 55, 85, HorizontalAlignment.Left);
		canvas.StrokeDashPattern = [];

		// Rotate via ConcatenateTransform
		canvas.SaveState();
		float cx = 250, cy = 55;
		var rotation = Matrix3x2.CreateRotation((float)(15 * Math.PI / 180), new Vector2(cx, cy));
		canvas.ConcatenateTransform(rotation);
		canvas.StrokeColor = Colors.DodgerBlue;
		canvas.StrokeSize = 2;
		canvas.DrawRectangle(210, 30, 80, 50);
		canvas.FontColor = Colors.DodgerBlue;
		canvas.DrawString("Rotated 15°", 215, 85, HorizontalAlignment.Left);
		canvas.RestoreState();

		// Scale via ConcatenateTransform
		canvas.SaveState();
		cx = 410; cy = 55;
		var scale = Matrix3x2.CreateScale(1.3f, 0.7f, new Vector2(cx, cy));
		canvas.ConcatenateTransform(scale);
		canvas.StrokeColor = Colors.Coral;
		canvas.StrokeSize = 2;
		canvas.DrawRectangle(370, 30, 80, 50);
		canvas.FontColor = Colors.Coral;
		canvas.DrawString("Scaled 1.3×0.7", 370, 85, HorizontalAlignment.Left);
		canvas.RestoreState();

		// Skew via ConcatenateTransform
		canvas.SaveState();
		var skew = Matrix3x2.CreateSkew(0.3f, 0, new Vector2(90, 130));
		canvas.ConcatenateTransform(skew);
		canvas.FillColor = Color.FromRgba(46, 204, 113, 100);
		canvas.FillRectangle(50, 105, 80, 40);
		canvas.FontColor = Colors.MediumSeaGreen;
		canvas.DrawString("Skewed", 55, 148, HorizontalAlignment.Left);
		canvas.RestoreState();

		// Composite: translate + rotate
		canvas.SaveState();
		var composite = Matrix3x2.CreateRotation((float)(-10 * Math.PI / 180), new Vector2(300, 130));
		composite *= Matrix3x2.CreateTranslation(20, 0);
		canvas.ConcatenateTransform(composite);
		canvas.FillColor = Color.FromRgba(155, 89, 182, 120);
		canvas.FillRoundedRectangle(260, 105, 100, 40, 8);
		canvas.FontColor = Colors.Purple;
		canvas.DrawString("Composite", 270, 148, HorizontalAlignment.Left);
		canvas.RestoreState();
	}
}

// ── 6. Shadow Rendering ─────────────────────────────────────────────────────

class ShadowDrawable : IDrawable
{
	public void Draw(ICanvas canvas, RectF rect)
	{
		canvas.FillColor = Color.FromArgb("#f8f9fa");
		canvas.FillRectangle(rect);

		// Rectangle with shadow
		canvas.SetShadow(new SizeF(4, 4), 8, Color.FromRgba(0, 0, 0, 80));
		canvas.FillColor = Colors.DodgerBlue;
		canvas.FillRectangle(30, 30, 100, 70);
		canvas.FontSize = 10;
		canvas.FontColor = Colors.DarkSlateGray;
		canvas.SetShadow(SizeF.Zero, 0, null);
		canvas.DrawString("Rect + Shadow", 30, 110, HorizontalAlignment.Left);

		// Rounded rect with shadow
		canvas.SetShadow(new SizeF(5, 5), 12, Color.FromRgba(0, 0, 0, 100));
		canvas.FillColor = Colors.Coral;
		canvas.FillRoundedRectangle(180, 30, 100, 70, 14);
		canvas.SetShadow(SizeF.Zero, 0, null);
		canvas.DrawString("Rounded + Shadow", 180, 110, HorizontalAlignment.Left);

		// Ellipse with shadow
		canvas.SetShadow(new SizeF(3, 6), 10, Color.FromRgba(100, 0, 150, 100));
		canvas.FillColor = Colors.MediumPurple;
		canvas.FillEllipse(330, 25, 110, 80);
		canvas.SetShadow(SizeF.Zero, 0, null);
		canvas.DrawString("Ellipse + Shadow", 345, 110, HorizontalAlignment.Left);

		// Path (star) with shadow
		canvas.SetShadow(new SizeF(4, 4), 6, Color.FromRgba(0, 0, 0, 80));
		canvas.FillColor = Colors.Gold;
		var star = new PathF();
		float sx = 520, sy = 65, sr = 35;
		for (int i = 0; i < 5; i++)
		{
			float angle = (float)(i * 4 * Math.PI / 5 - Math.PI / 2);
			float px = sx + sr * (float)Math.Cos(angle);
			float py = sy + sr * (float)Math.Sin(angle);
			if (i == 0) star.MoveTo(px, py);
			else star.LineTo(px, py);
		}
		star.Close();
		canvas.FillPath(star);
		canvas.SetShadow(SizeF.Zero, 0, null);
		canvas.DrawString("Path + Shadow", 490, 110, HorizontalAlignment.Left);
	}
}

// ── 7. Antialias & BlendMode ────────────────────────────────────────────────

class AntialiasBlendDrawable : IDrawable
{
	public void Draw(ICanvas canvas, RectF rect)
	{
		canvas.FillColor = Color.FromArgb("#f0f4f8");
		canvas.FillRectangle(rect);

		// Antialiased circle
		canvas.Antialias = true;
		canvas.FillColor = Colors.DodgerBlue;
		canvas.FillCircle(60, 60, 40);
		canvas.FontSize = 10;
		canvas.FontColor = Colors.DarkSlateGray;
		canvas.DrawString("Antialias ON", 20, 110, HorizontalAlignment.Left);

		// Non-antialiased circle
		canvas.Antialias = false;
		canvas.FillColor = Colors.DodgerBlue;
		canvas.FillCircle(170, 60, 40);
		canvas.Antialias = true; // restore for text
		canvas.DrawString("Antialias OFF", 130, 110, HorizontalAlignment.Left);

		// BlendMode demo: overlapping shapes
		canvas.FillColor = Colors.Red;
		canvas.FillCircle(310, 50, 35);

		canvas.SaveState();
		canvas.BlendMode = BlendMode.Xor;
		canvas.FillColor = Colors.Blue;
		canvas.FillCircle(340, 50, 35);
		canvas.RestoreState();

		canvas.FontColor = Colors.DarkSlateGray;
		canvas.DrawString("Xor Blend", 290, 110, HorizontalAlignment.Left);

		// SourceAtop blend
		canvas.FillColor = Colors.MediumSeaGreen;
		canvas.FillRoundedRectangle(410, 20, 70, 70, 8);

		canvas.SaveState();
		canvas.BlendMode = BlendMode.DestinationOver;
		canvas.FillColor = Colors.Orange;
		canvas.FillCircle(470, 60, 35);
		canvas.RestoreState();

		canvas.DrawString("DestOver", 420, 110, HorizontalAlignment.Left);
	}
}

// ── 8. SubtractFromClip ─────────────────────────────────────────────────────

class ClipDrawable : IDrawable
{
	public void Draw(ICanvas canvas, RectF rect)
	{
		canvas.FillColor = Color.FromArgb("#f0f4f8");
		canvas.FillRectangle(rect);

		// ClipRectangle demo
		canvas.SaveState();
		canvas.ClipRectangle(20, 10, 150, 100);
		canvas.FillColor = Colors.DodgerBlue;
		canvas.FillCircle(95, 60, 80); // Clipped to rectangle bounds
		canvas.RestoreState();
		canvas.FontSize = 10;
		canvas.FontColor = Colors.DarkSlateGray;
		canvas.DrawString("ClipRectangle", 40, 115, HorizontalAlignment.Left);

		// SubtractFromClip demo: draw a filled rect with a hole cut out
		canvas.SaveState();
		canvas.ClipRectangle(220, 10, 160, 100);
		canvas.SubtractFromClip(260, 30, 80, 50);

		// Fill the entire clipped area — the subtracted region will show through
		canvas.FillColor = Colors.Coral;
		canvas.FillRectangle(220, 10, 160, 100);
		canvas.RestoreState();

		canvas.DrawString("SubtractFromClip", 240, 115, HorizontalAlignment.Left);

		// ClipPath demo
		canvas.SaveState();
		var clipPath = new PathF();
		float cx = 490, cy = 55;
		for (int i = 0; i < 6; i++)
		{
			float angle = (float)(i * Math.PI / 3 - Math.PI / 2);
			float px = cx + 50 * (float)Math.Cos(angle);
			float py = cy + 50 * (float)Math.Sin(angle);
			if (i == 0) clipPath.MoveTo(px, py);
			else clipPath.LineTo(px, py);
		}
		clipPath.Close();
		canvas.ClipPath(clipPath);

		// Fill stripes inside the hexagonal clip
		for (float x = 430; x < 560; x += 15)
		{
			canvas.FillColor = ((int)(x / 15) % 2 == 0) ? Colors.MediumPurple : Colors.Gold;
			canvas.FillRectangle(x, 0, 15, 130);
		}
		canvas.RestoreState();

		canvas.DrawString("ClipPath (hex)", 455, 115, HorizontalAlignment.Left);
	}
}

// ── 9. Interaction Events ───────────────────────────────────────────────────

class InteractionDrawable : IDrawable
{
	public void Draw(ICanvas canvas, RectF rect)
	{
		canvas.FillColor = Color.FromArgb("#e8f4fd");
		canvas.FillRectangle(rect);

		canvas.StrokeColor = Colors.DodgerBlue;
		canvas.StrokeSize = 2;
		canvas.StrokeDashPattern = [6, 4];
		canvas.DrawRoundedRectangle(10, 10, rect.Width - 20, rect.Height - 20, 10);
		canvas.StrokeDashPattern = [];

		canvas.FontSize = 14;
		canvas.FontColor = Colors.DodgerBlue;
		canvas.DrawString("Interactive Area — Click, Drag, or Hover here",
			rect.Width / 2, rect.Height / 2 - 10, HorizontalAlignment.Center);

		canvas.FontSize = 11;
		canvas.FontColor = Colors.Gray;
		canvas.DrawString("Events are logged below",
			rect.Width / 2, rect.Height / 2 + 10, HorizontalAlignment.Center);
	}
}

// ── 10. Multi-line Text Wrapping (Pango) ────────────────────────────────────

class TextWrapDrawable : IDrawable
{
	public void Draw(ICanvas canvas, RectF rect)
	{
		canvas.FillColor = Color.FromArgb("#f0f4f8");
		canvas.FillRectangle(rect);

		string longText = "This is a long paragraph that should automatically wrap " +
			"to multiple lines when drawn inside a bounded rectangle. " +
			"Pango handles word wrapping, line breaking, and text shaping.";

		// Left-aligned wrapped text
		float boxW = (rect.Width - 60) / 3;

		canvas.StrokeColor = Colors.DodgerBlue;
		canvas.StrokeSize = 1;
		canvas.StrokeDashPattern = [3, 3];
		canvas.DrawRectangle(10, 10, boxW, 150);
		canvas.StrokeDashPattern = [];

		canvas.FontSize = 11;
		canvas.FontColor = Colors.DarkSlateGray;
		canvas.DrawString(longText, 10, 10, boxW, 150,
			HorizontalAlignment.Left, VerticalAlignment.Top);

		canvas.FontSize = 9;
		canvas.FontColor = Colors.DodgerBlue;
		canvas.DrawString("Left / Top", 10, 165, HorizontalAlignment.Left);

		// Center-aligned wrapped text
		float x2 = 20 + boxW;
		canvas.StrokeColor = Colors.Coral;
		canvas.StrokeSize = 1;
		canvas.StrokeDashPattern = [3, 3];
		canvas.DrawRectangle(x2, 10, boxW, 150);
		canvas.StrokeDashPattern = [];

		canvas.FontSize = 11;
		canvas.FontColor = Colors.DarkSlateGray;
		canvas.DrawString(longText, x2, 10, boxW, 150,
			HorizontalAlignment.Center, VerticalAlignment.Center);

		canvas.FontSize = 9;
		canvas.FontColor = Colors.Coral;
		canvas.DrawString("Center / Center", x2, 165, HorizontalAlignment.Left);

		// Right-aligned wrapped text
		float x3 = 30 + boxW * 2;
		canvas.StrokeColor = Colors.MediumPurple;
		canvas.StrokeSize = 1;
		canvas.StrokeDashPattern = [3, 3];
		canvas.DrawRectangle(x3, 10, boxW, 150);
		canvas.StrokeDashPattern = [];

		canvas.FontSize = 11;
		canvas.FontColor = Colors.DarkSlateGray;
		canvas.DrawString(longText, x3, 10, boxW, 150,
			HorizontalAlignment.Right, VerticalAlignment.Bottom);

		canvas.FontSize = 9;
		canvas.FontColor = Colors.MediumPurple;
		canvas.DrawString("Right / Bottom", x3, 165, HorizontalAlignment.Left);
	}
}
