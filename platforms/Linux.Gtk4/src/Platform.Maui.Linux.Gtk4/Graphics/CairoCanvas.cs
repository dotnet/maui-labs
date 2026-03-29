using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Text;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Platform.Maui.Linux.Gtk4.Graphics;

/// <summary>
/// ICanvas implementation backed by Cairo for GTK4.
/// Handles all MAUI.Graphics drawing operations including paths with bezier curves,
/// gradient paints, text measurement, stroke properties, and transforms.
/// </summary>
internal class CairoCanvas : ICanvas
{
	private readonly Cairo.Context _cr;
	private float _strokeSize = 1;
	private Color _strokeColor = Colors.Black;
	private Color _fillColor = Colors.White;
	private Color _fontColor = Colors.Black;
	private float _fontSize = 14;
	private float _alpha = 1;
	private Paint? _currentPaint;
	private RectF _currentPaintRect;
	private SizeF _shadowOffset;
	private float _shadowBlur;
	private Color? _shadowColor;

	public CairoCanvas(Cairo.Context cr)
	{
		_cr = cr;
	}

	// --- Properties ---

	public float StrokeSize { get => _strokeSize; set => _strokeSize = value; }
	public float MiterLimit { get; set; } = 10;
	public LineCap StrokeLineCap { get; set; } = LineCap.Butt;
	public LineJoin StrokeLineJoin { get; set; } = LineJoin.Miter;
	public Color StrokeColor { get => _strokeColor; set => _strokeColor = value ?? Colors.Black; }
	public Color FillColor { get => _fillColor; set { _fillColor = value ?? Colors.Transparent; _currentPaint = null; } }
	public Color FontColor { get => _fontColor; set => _fontColor = value ?? Colors.Black; }
	public float FontSize { get => _fontSize; set => _fontSize = value; }
	public IFont Font { get; set; } = Microsoft.Maui.Graphics.Font.Default;
	public float Alpha { get => _alpha; set => _alpha = Math.Clamp(value, 0, 1); }
	public bool Antialias { get; set; } = true;
	public float DisplayScale { get; set; } = 1;
	public BlendMode BlendMode { get; set; } = BlendMode.Normal;
	public float[] StrokeDashPattern { get; set; } = [];
	public float StrokeDashOffset { get; set; }
	public bool RestrictToClipBounds { get; set; }

	// --- Basic shapes ---

	public void DrawLine(float x1, float y1, float x2, float y2)
	{
		ApplyStroke();
		_cr.MoveTo(x1, y1);
		_cr.LineTo(x2, y2);
		_cr.Stroke();
	}

	public void DrawRectangle(float x, float y, float width, float height)
	{
		ApplyStroke();
		_cr.Rectangle(x, y, width, height);
		_cr.Stroke();
	}

	public void FillRectangle(float x, float y, float width, float height)
	{
		DrawShadowFill(() => { _cr.Rectangle(x, y, width, height); _cr.Fill(); });
		ApplyFill();
		_cr.Rectangle(x, y, width, height);
		_cr.Fill();
	}

	public void DrawRoundedRectangle(float x, float y, float width, float height, float cornerRadius)
	{
		ApplyStroke();
		RoundedRectPath(x, y, width, height, cornerRadius);
		_cr.Stroke();
	}

	public void FillRoundedRectangle(float x, float y, float width, float height, float cornerRadius)
	{
		DrawShadowFill(() => { RoundedRectPath(x, y, width, height, cornerRadius); _cr.Fill(); });
		ApplyFill();
		RoundedRectPath(x, y, width, height, cornerRadius);
		_cr.Fill();
	}

	public void DrawEllipse(float x, float y, float width, float height)
	{
		ApplyStroke();
		EllipsePath(x, y, width, height);
		_cr.Stroke();
	}

	public void FillEllipse(float x, float y, float width, float height)
	{
		DrawShadowFill(() => { EllipsePath(x, y, width, height); _cr.Fill(); });
		ApplyFill();
		EllipsePath(x, y, width, height);
		_cr.Fill();
	}

	public void DrawCircle(float centerX, float centerY, float radius)
		=> DrawEllipse(centerX - radius, centerY - radius, radius * 2, radius * 2);

	public void FillCircle(float centerX, float centerY, float radius)
		=> FillEllipse(centerX - radius, centerY - radius, radius * 2, radius * 2);

	// --- Arcs ---

	public void DrawArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise, bool closed)
	{
		ApplyStroke();
		ArcPath(x, y, width, height, startAngle, endAngle, clockwise);
		if (closed) _cr.ClosePath();
		_cr.Stroke();
	}

	public void FillArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise)
	{
		ApplyFill();
		ArcPath(x, y, width, height, startAngle, endAngle, clockwise);
		_cr.ClosePath();
		_cr.Fill();
	}

	// --- Text ---

	public void DrawString(string value, float x, float y, HorizontalAlignment horizontalAlignment)
	{
		if (string.IsNullOrEmpty(value)) return;

		ApplyFontColor();
		var layout = CreatePangoLayout(value);

		layout.SetAlignment(horizontalAlignment switch
		{
			HorizontalAlignment.Center => Pango.Alignment.Center,
			HorizontalAlignment.Right => Pango.Alignment.Right,
			_ => Pango.Alignment.Left,
		});

		layout.GetPixelSize(out int textW, out int textH);

		double drawX = horizontalAlignment switch
		{
			HorizontalAlignment.Center => x - textW / 2.0,
			HorizontalAlignment.Right => x - textW,
			_ => x,
		};

		_cr.MoveTo(drawX, y);
		PangoCairo.Functions.ShowLayout(_cr, layout);
	}

	public void DrawString(string value, float x, float y, float width, float height,
		HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment,
		TextFlow textFlow = TextFlow.ClipBounds, float lineSpacingAdjustment = 0)
	{
		if (string.IsNullOrEmpty(value)) return;

		ApplyFontColor();
		var layout = CreatePangoLayout(value);

		// Enable word wrapping when we have a width constraint
		layout.SetWidth((int)(width * Pango.Constants.SCALE));
		layout.SetWrap(Pango.WrapMode.Word);

		layout.SetAlignment(horizontalAlignment switch
		{
			HorizontalAlignment.Center => Pango.Alignment.Center,
			HorizontalAlignment.Right => Pango.Alignment.Right,
			_ => Pango.Alignment.Left,
		});

		if (lineSpacingAdjustment != 0)
			layout.SetSpacing((int)(lineSpacingAdjustment * Pango.Constants.SCALE));

		layout.GetPixelSize(out int textW, out int textH);

		double drawY = verticalAlignment switch
		{
			VerticalAlignment.Center => y + (height - textH) / 2.0,
			VerticalAlignment.Bottom => y + height - textH,
			_ => y,
		};

		if (textFlow == TextFlow.ClipBounds)
		{
			_cr.Save();
			_cr.Rectangle(x, y, width, height);
			_cr.Clip();
		}

		_cr.MoveTo(x, drawY);
		PangoCairo.Functions.ShowLayout(_cr, layout);

		if (textFlow == TextFlow.ClipBounds)
		{
			_cr.Restore();
		}
	}

	public void DrawText(IAttributedText value, float x, float y, float width, float height)
	{
		if (value == null || string.IsNullOrEmpty(value.Text)) return;

		ApplyFontColor();
		var layout = CreatePangoLayout(value.Text);
		layout.SetWidth((int)(width * Pango.Constants.SCALE));
		layout.SetWrap(Pango.WrapMode.Word);

		// Apply text attributes (bold, italic, color, etc.)
		var attrList = BuildPangoAttrList(value);
		if (attrList != IntPtr.Zero)
		{
			pango_layout_set_attributes(layout.Handle.DangerousGetHandle(), attrList);
			// attrList ownership transfers to layout
		}

		_cr.MoveTo(x, y);
		PangoCairo.Functions.ShowLayout(_cr, layout);
	}

	public SizeF GetStringSize(string value, IFont font, float fontSize)
	{
		if (string.IsNullOrEmpty(value))
			return SizeF.Zero;

		var layout = CreatePangoLayout(value, font, fontSize);
		layout.GetPixelSize(out int textW, out int textH);
		return new SizeF(textW, textH);
	}

	public SizeF GetStringSize(string value, IFont font, float fontSize, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment)
	{
		return GetStringSize(value, font, fontSize);
	}

	// --- Paths ---

	public void DrawPath(PathF path)
	{
		ApplyStroke();
		DrawPathInternal(path);
		_cr.Stroke();
	}

	public void FillPath(PathF path, WindingMode windingMode = WindingMode.NonZero)
	{
		var rule = windingMode == WindingMode.NonZero ? Cairo.FillRule.Winding : Cairo.FillRule.EvenOdd;
		DrawShadowFill(() => { DrawPathInternal(path); _cr.FillRule = rule; _cr.Fill(); });
		ApplyFill();
		DrawPathInternal(path);
		_cr.FillRule = rule;
		_cr.Fill();
	}

	public void ClipPath(PathF path, WindingMode windingMode = WindingMode.NonZero)
	{
		DrawPathInternal(path);
		_cr.FillRule = windingMode == WindingMode.NonZero ? Cairo.FillRule.Winding : Cairo.FillRule.EvenOdd;
		_cr.Clip();
	}

	public void ClipRectangle(float x, float y, float width, float height)
	{
		_cr.Rectangle(x, y, width, height);
		_cr.Clip();
	}

	public void SubtractFromClip(float x, float y, float width, float height)
	{
		// Cairo doesn't support direct clip subtraction. We use even-odd fill rule
		// with a large outer rect and the subtracted inner rect.
		var savedRule = _cr.FillRule;
		_cr.FillRule = Cairo.FillRule.EvenOdd;

		// Outer rect covering effectively infinite area
		_cr.Rectangle(-1e6, -1e6, 2e6, 2e6);
		// Inner rect to subtract
		_cr.Rectangle(x, y, width, height);
		_cr.Clip();

		_cr.FillRule = savedRule;
	}

	// --- Image ---

	public void DrawImage(IImage image, float x, float y, float width, float height)
	{
		if (image is not CairoPlatformImage cairoImage)
			return;

		var surface = cairoImage.Surface;
		if (surface == null)
			return;

		_cr.Save();

		double scaleX = width / cairo_image_surface_get_width(surface.Handle.DangerousGetHandle());
		double scaleY = height / cairo_image_surface_get_height(surface.Handle.DangerousGetHandle());
		_cr.Translate(x, y);
		_cr.Scale(scaleX, scaleY);

		_cr.SetSourceSurface(surface, 0, 0);
		_cr.PaintWithAlpha(_alpha);

		_cr.Restore();
	}

	// --- Transforms ---

	public void Rotate(float degrees, float x, float y)
	{
		_cr.Translate(x, y);
		_cr.Rotate(degrees * Math.PI / 180);
		_cr.Translate(-x, -y);
	}

	public void Rotate(float degrees) => Rotate(degrees, 0, 0);
	public void Scale(float sx, float sy) => _cr.Scale(sx, sy);
	public void Translate(float tx, float ty) => _cr.Translate(tx, ty);

	public void ConcatenateTransform(Matrix3x2 transform)
	{
		// Matrix3x2 layout: M11 M12 / M21 M22 / M31 M32 (translation)
		// Cairo matrix Init: xx, yx, xy, yy, x0, y0
		var matrix = new Cairo.Matrix();
		matrix.Init(transform.M11, transform.M12, transform.M21, transform.M22, transform.M31, transform.M32);
		_cr.Transform(matrix);
	}

	// --- State ---

	public void SaveState() => _cr.Save();

	public bool RestoreState()
	{
		_cr.Restore();
		return true;
	}

	public void ResetState()
	{
		_strokeSize = 1;
		_strokeColor = Colors.Black;
		_fillColor = Colors.White;
		_fontColor = Colors.Black;
		_fontSize = 14;
		_alpha = 1;
		_currentPaint = null;
		_shadowColor = null;
		StrokeLineCap = LineCap.Butt;
		StrokeLineJoin = LineJoin.Miter;
		MiterLimit = 10;
		StrokeDashPattern = [];
		StrokeDashOffset = 0;
		BlendMode = BlendMode.Normal;
		Antialias = true;
		Font = Microsoft.Maui.Graphics.Font.Default;
	}

	// --- Shadow ---

	public void SetShadow(SizeF offset, float blur, Color color)
	{
		_shadowOffset = offset;
		_shadowBlur = blur;
		_shadowColor = color;
	}

	private bool HasShadow => _shadowColor != null && (_shadowOffset.Width != 0 || _shadowOffset.Height != 0 || _shadowBlur > 0);

	/// <summary>
	/// Draws a shadow version of the current path/shape before the actual draw.
	/// Uses offset copies with decreasing alpha to approximate blur.
	/// </summary>
	private void DrawShadowFill(Action drawShape)
	{
		if (!HasShadow || _shadowColor == null)
		{
			drawShape();
			return;
		}

		_cr.Save();

		// Approximate blur with multiple offset passes
		int passes = _shadowBlur > 0 ? Math.Max(1, (int)(_shadowBlur / 2)) : 1;
		passes = Math.Min(passes, 5); // Cap at 5 for performance
		float baseAlpha = _shadowColor.Alpha * _alpha;

		for (int i = passes; i >= 1; i--)
		{
			float spread = _shadowBlur > 0 ? _shadowBlur * i / passes : 0;
			float passAlpha = baseAlpha / (passes + 1);

			_cr.Save();
			_cr.Translate(_shadowOffset.Width, _shadowOffset.Height);

			if (spread > 0)
			{
				// Slight scale to simulate spread
				_cr.Translate(spread / 2, spread / 2);
			}

			_cr.SetSourceRgba(_shadowColor.Red, _shadowColor.Green, _shadowColor.Blue, passAlpha);
			drawShape();
			_cr.Restore();
		}

		_cr.Restore();
	}

	// --- Paint ---

	public void SetFillPaint(Paint paint, RectF rectangle)
	{
		_currentPaint = paint;
		_currentPaintRect = rectangle;

		if (paint is SolidPaint solidPaint && solidPaint.Color != null)
			_fillColor = solidPaint.Color;
	}

	// --- Private helpers ---

	private void ApplyStroke()
	{
		ApplyOperator();
		ApplyAntialias();

		_cr.SetSourceRgba(_strokeColor.Red, _strokeColor.Green, _strokeColor.Blue, _strokeColor.Alpha * _alpha);
		_cr.LineWidth = _strokeSize;

		_cr.LineCap = StrokeLineCap switch
		{
			LineCap.Round => Cairo.LineCap.Round,
			LineCap.Square => Cairo.LineCap.Square,
			_ => Cairo.LineCap.Butt
		};

		_cr.LineJoin = StrokeLineJoin switch
		{
			LineJoin.Round => Cairo.LineJoin.Round,
			LineJoin.Bevel => Cairo.LineJoin.Bevel,
			_ => Cairo.LineJoin.Miter
		};

		_cr.MiterLimit = MiterLimit;

		if (StrokeDashPattern is { Length: > 0 })
		{
			var dashes = new double[StrokeDashPattern.Length];
			for (int i = 0; i < StrokeDashPattern.Length; i++)
				dashes[i] = StrokeDashPattern[i] * _strokeSize;
			_cr.SetDash(dashes, StrokeDashOffset * _strokeSize);
		}
		else
		{
			_cr.SetDash([], 0);
		}
	}

	private void ApplyFill()
	{
		ApplyOperator();
		ApplyAntialias();

		if (_currentPaint is LinearGradientPaint linear)
		{
			ApplyLinearGradient(linear, _currentPaintRect);
			return;
		}

		if (_currentPaint is RadialGradientPaint radial)
		{
			ApplyRadialGradient(radial, _currentPaintRect);
			return;
		}

		_cr.SetSourceRgba(_fillColor.Red, _fillColor.Green, _fillColor.Blue, _fillColor.Alpha * _alpha);
	}

	private void ApplyLinearGradient(LinearGradientPaint paint, RectF rect)
	{
		double x0 = rect.X + paint.StartPoint.X * rect.Width;
		double y0 = rect.Y + paint.StartPoint.Y * rect.Height;
		double x1 = rect.X + paint.EndPoint.X * rect.Width;
		double y1 = rect.Y + paint.EndPoint.Y * rect.Height;

		var patternHandle = cairo_pattern_create_linear(x0, y0, x1, y1);
		try
		{
			AddGradientStops(patternHandle, paint.GradientStops);
			cairo_set_source(_cr.Handle.DangerousGetHandle(), patternHandle);
		}
		finally
		{
			cairo_pattern_destroy(patternHandle);
		}
	}

	private void ApplyRadialGradient(RadialGradientPaint paint, RectF rect)
	{
		double cx = rect.X + paint.Center.X * rect.Width;
		double cy = rect.Y + paint.Center.Y * rect.Height;
		double radius = Math.Max(rect.Width, rect.Height) * paint.Radius;

		var patternHandle = cairo_pattern_create_radial(cx, cy, 0, cx, cy, radius);
		try
		{
			AddGradientStops(patternHandle, paint.GradientStops);
			cairo_set_source(_cr.Handle.DangerousGetHandle(), patternHandle);
		}
		finally
		{
			cairo_pattern_destroy(patternHandle);
		}
	}

	private void AddGradientStops(nint pattern, PaintGradientStop[]? stops)
	{
		if (stops == null) return;

		foreach (var stop in stops)
		{
			var color = stop.Color;
			cairo_pattern_add_color_stop_rgba(pattern, stop.Offset,
				color.Red, color.Green, color.Blue, color.Alpha * _alpha);
		}
	}

	private void ApplyFontColor()
	{
		_cr.SetSourceRgba(_fontColor.Red, _fontColor.Green, _fontColor.Blue, _fontColor.Alpha * _alpha);
	}

	/// <summary>
	/// Creates a Pango layout configured with the current font settings.
	/// </summary>
	private Pango.Layout CreatePangoLayout(string text, IFont? font = null, float? fontSize = null)
	{
		var layout = PangoCairo.Functions.CreateLayout(_cr);
		var fontDesc = Pango.FontDescription.New();

		var f = font ?? Font;
		var size = fontSize ?? _fontSize;

		fontDesc.SetFamily(f?.Name ?? "Sans");
		fontDesc.SetAbsoluteSize(size * Pango.Constants.SCALE);

		if (f != null)
		{
			fontDesc.SetWeight(f.Weight >= 600 ? Pango.Weight.Bold : Pango.Weight.Normal);
			fontDesc.SetStyle(f.StyleType switch
			{
				FontStyleType.Italic => Pango.Style.Italic,
				FontStyleType.Oblique => Pango.Style.Oblique,
				_ => Pango.Style.Normal,
			});
		}

		layout.SetFontDescription(fontDesc);
		layout.SetText(text, -1);
		return layout;
	}

	/// <summary>
	/// Builds a PangoAttrList from IAttributedText runs.
	/// Returns IntPtr.Zero if no attributes to apply.
	/// </summary>
	private static IntPtr BuildPangoAttrList(IAttributedText attributedText)
	{
		if (attributedText.Runs == null || attributedText.Runs.Count == 0)
			return IntPtr.Zero;

		var attrList = pango_attr_list_new();
		var text = attributedText.Text;

		foreach (var run in attributedText.Runs)
		{
			// Convert character indices to byte indices (Pango uses UTF-8 byte offsets)
			int byteStart = System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(0, Math.Min(run.Start, text.Length)));
			int byteEnd = System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(0, Math.Min(run.Start + run.Length, text.Length)));

			var attrs = run.Attributes;
			if (attrs == null) continue;

			if (attrs.ContainsKey(TextAttribute.Bold))
				InsertPangoAttr(attrList, pango_attr_weight_new(700), byteStart, byteEnd);

			if (attrs.ContainsKey(TextAttribute.Italic))
				InsertPangoAttr(attrList, pango_attr_style_new(2), byteStart, byteEnd);

			if (attrs.ContainsKey(TextAttribute.Underline))
				InsertPangoAttr(attrList, pango_attr_underline_new(1), byteStart, byteEnd);

			if (attrs.ContainsKey(TextAttribute.Strikethrough))
				InsertPangoAttr(attrList, pango_attr_strikethrough_new(true), byteStart, byteEnd);

			if (attrs.TryGetValue(TextAttribute.FontSize, out var fontSizeStr)
				&& float.TryParse(fontSizeStr, out float attrFontSize))
			{
				InsertPangoAttr(attrList, pango_attr_size_new((int)(attrFontSize * Pango.Constants.SCALE)), byteStart, byteEnd);
			}

			if (attrs.TryGetValue(TextAttribute.Color, out var colorStr))
			{
				if (Color.TryParse(colorStr, out var color))
				{
					ushort r = (ushort)(color.Red * 65535);
					ushort g = (ushort)(color.Green * 65535);
					ushort b = (ushort)(color.Blue * 65535);
					InsertPangoAttr(attrList, pango_attr_foreground_new(r, g, b), byteStart, byteEnd);
				}
			}
		}

		return attrList;
	}

	/// <summary>
	/// Sets start/end byte indices on a PangoAttribute and inserts it into the list.
	/// PangoAttribute struct layout: klass (ptr), start_index (uint), end_index (uint)
	/// </summary>
	private static void InsertPangoAttr(IntPtr attrList, IntPtr attr, int byteStart, int byteEnd)
	{
		if (attr == IntPtr.Zero) return;
		Marshal.WriteInt32(attr, IntPtr.Size, byteStart);
		Marshal.WriteInt32(attr, IntPtr.Size + 4, byteEnd);
		pango_attr_list_insert(attrList, attr);
	}

	private void ApplyOperator()
	{
		_cr.Operator = BlendMode switch
		{
			BlendMode.Clear => Cairo.Operator.Clear,
			BlendMode.Copy => Cairo.Operator.Source,
			BlendMode.SourceIn => Cairo.Operator.In,
			BlendMode.SourceOut => Cairo.Operator.Out,
			BlendMode.SourceAtop => Cairo.Operator.Atop,
			BlendMode.DestinationOver => Cairo.Operator.DestOver,
			BlendMode.DestinationIn => Cairo.Operator.DestIn,
			BlendMode.DestinationOut => Cairo.Operator.DestOut,
			BlendMode.DestinationAtop => Cairo.Operator.DestAtop,
			BlendMode.Xor => Cairo.Operator.Xor,
			_ => Cairo.Operator.Over,
		};
	}

	private void ApplyAntialias()
	{
		_cr.Antialias = Antialias ? Cairo.Antialias.Default : Cairo.Antialias.None;
	}

	private void ArcPath(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise)
	{
		double cx = x + width / 2, cy = y + height / 2;
		double rx = width / 2, ry = height / 2;
		_cr.Save();
		_cr.Translate(cx, cy);
		_cr.Scale(rx, ry);
		if (clockwise)
			_cr.Arc(0, 0, 1, startAngle * Math.PI / 180, endAngle * Math.PI / 180);
		else
			_cr.ArcNegative(0, 0, 1, startAngle * Math.PI / 180, endAngle * Math.PI / 180);
		_cr.Restore();
	}

	private void EllipsePath(float x, float y, float width, float height)
	{
		_cr.Save();
		_cr.Translate(x + width / 2, y + height / 2);
		_cr.Scale(width / 2, height / 2);
		_cr.Arc(0, 0, 1, 0, 2 * Math.PI);
		_cr.Restore();
	}

	private void RoundedRectPath(float x, float y, float w, float h, float r)
	{
		r = Math.Min(r, Math.Min(w / 2, h / 2));
		_cr.NewPath();
		_cr.Arc(x + w - r, y + r, r, -Math.PI / 2, 0);
		_cr.Arc(x + w - r, y + h - r, r, 0, Math.PI / 2);
		_cr.Arc(x + r, y + h - r, r, Math.PI / 2, Math.PI);
		_cr.Arc(x + r, y + r, r, Math.PI, 3 * Math.PI / 2);
		_cr.ClosePath();
	}

	/// <summary>
	/// Renders a PathF by iterating segment types (Move, Line, Cubic, Quad, Arc, Close).
	/// </summary>
	private void DrawPathInternal(PathF path)
	{
		_cr.NewPath();
		if (path.OperationCount == 0)
			return;

		for (int i = 0; i < path.OperationCount; i++)
		{
			var type = path.GetSegmentType(i);
			var points = path.GetPointsForSegment(i);

			switch (type)
			{
				case PathOperation.Move:
					if (points.Length > 0)
						_cr.MoveTo(points[0].X, points[0].Y);
					break;

				case PathOperation.Line:
					if (points.Length > 0)
						_cr.LineTo(points[0].X, points[0].Y);
					break;

				case PathOperation.Cubic:
					if (points.Length >= 3)
						_cr.CurveTo(
							points[0].X, points[0].Y,
							points[1].X, points[1].Y,
							points[2].X, points[2].Y);
					break;

				case PathOperation.Quad:
					if (points.Length >= 2)
					{
						_cr.GetCurrentPoint(out var cx, out var cy);
						var c1x = cx + 2.0 / 3.0 * (points[0].X - cx);
						var c1y = cy + 2.0 / 3.0 * (points[0].Y - cy);
						var c2x = points[1].X + 2.0 / 3.0 * (points[0].X - points[1].X);
						var c2y = points[1].Y + 2.0 / 3.0 * (points[0].Y - points[1].Y);
						_cr.CurveTo(c1x, c1y, c2x, c2y, points[1].X, points[1].Y);
					}
					break;

				case PathOperation.Arc:
					if (points.Length > 0)
						_cr.LineTo(points[0].X, points[0].Y);
					break;

				case PathOperation.Close:
					_cr.ClosePath();
					break;
			}
		}
	}

	// --- Cairo P/Invoke for gradient patterns ---

	[DllImport("libcairo.so.2")]
	private static extern nint cairo_pattern_create_linear(double x0, double y0, double x1, double y1);

	[DllImport("libcairo.so.2")]
	private static extern nint cairo_pattern_create_radial(double cx0, double cy0, double radius0, double cx1, double cy1, double radius1);

	[DllImport("libcairo.so.2")]
	private static extern void cairo_pattern_add_color_stop_rgba(nint pattern, double offset, double red, double green, double blue, double alpha);

	[DllImport("libcairo.so.2")]
	private static extern void cairo_pattern_destroy(nint pattern);

	[DllImport("libcairo.so.2")]
	private static extern void cairo_set_source(nint cr, nint pattern);

	[DllImport("libcairo.so.2")]
	private static extern int cairo_image_surface_get_width(nint surface);

	[DllImport("libcairo.so.2")]
	private static extern int cairo_image_surface_get_height(nint surface);

	// --- Pango P/Invoke for attributed text ---

	[DllImport("libpango-1.0.so.0")]
	private static extern IntPtr pango_attr_list_new();

	[DllImport("libpango-1.0.so.0")]
	private static extern void pango_attr_list_insert(IntPtr list, IntPtr attr);

	[DllImport("libpango-1.0.so.0")]
	private static extern void pango_layout_set_attributes(IntPtr layout, IntPtr attrs);

	[DllImport("libpango-1.0.so.0")]
	private static extern IntPtr pango_attr_weight_new(int weight);

	[DllImport("libpango-1.0.so.0")]
	private static extern IntPtr pango_attr_style_new(int style);

	[DllImport("libpango-1.0.so.0")]
	private static extern IntPtr pango_attr_underline_new(int underline);

	[DllImport("libpango-1.0.so.0")]
	private static extern IntPtr pango_attr_strikethrough_new(bool strikethrough);

	[DllImport("libpango-1.0.so.0")]
	private static extern IntPtr pango_attr_size_new(int size);

	[DllImport("libpango-1.0.so.0")]
	private static extern IntPtr pango_attr_foreground_new(ushort red, ushort green, ushort blue);
}
