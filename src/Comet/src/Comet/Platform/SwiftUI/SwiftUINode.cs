#nullable enable
#if IOS
using System.Collections.Generic;
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.SwiftUI
{
	/// <summary>A backend node backed by a Swift <see cref="CometNode"/>. Lets nodes of
	/// different C# types (generic nodes, the list node) nest and expose their native handle.</summary>
	interface ISwiftUINativeNode
	{
		CometNode Native { get; }
	}

	/// <summary>
	/// A retained backend node that bridges Comet's diff to a Swift <see cref="CometNode"/>
	/// in the SwiftUI tree (the iOS counterpart of the Compose <c>ComposeNode</c>). Unlike
	/// the Compose backend — which needs a distinct class per control — the SwiftUI shim is
	/// kind-driven, so one node type parameterized by a kind string suffices.
	/// </summary>
	sealed class SwiftUINode : ICometBackendNode, ISwiftUINativeNode
	{
		readonly CometNode _native;
		readonly List<ICometBackendNode> _children = new();
		readonly string _kind;
		ICometEventSink? _sink;

		// Tracked so MeasureBaseline can compute the first-baseline offset from UIFont
		// metrics (the iOS counterpart of ComposeTextNode's TextMeasurer baseline).
		double? _fontSize;
		string? _fontFamily;
		int _fontWeight;
		bool _fontItalic;
		string _fontImageGlyph = string.Empty;
		string _fontImageFamily = string.Empty;
		double _fontImageSize;
		int _fontImageWeight;
		bool _fontImageItalic;
		bool _fontImageAutoScaling;
		Color? _fontImageColor;
		Size _fontImageMeasuredSize;
		SwiftUIFontImageSpec? _renderedFontImage;

		public CometNode Native => _native;

		public SwiftUINode(string kind)
		{
			_kind = kind;
			_native = CometSwiftUIHost.MakeNode(kind);
			// Route native events back through the event sink. Harmless for kinds that
			// don't raise a given event (their handler simply never fires).
			CometSwiftUIHost.SetTapHandler(_native, OnNativeTap);
			CometSwiftUIHost.SetTapGestureHandler(_native, OnNativeTapGesture);
			CometSwiftUIHost.SetLongPressGestureHandler(_native, OnNativeLongPress);
			CometSwiftUIHost.SetRecordGestureHandler(_native, OnNativeRecord);
			CometSwiftUIHost.SetStringChangeHandler(_native, OnNativeTextChanged);
			CometSwiftUIHost.SetBoolChangeHandler(_native, OnNativeToggled);
			CometSwiftUIHost.SetDoubleChangeHandler(_native, OnNativeValueChanged);
			CometSwiftUIHost.SetFocusHandler(_native, OnNativeFocused);
			CometSwiftUIHost.SetCompletedHandler(_native, OnNativeCompleted);
			CometSwiftUIHost.SetRefreshHandler(_native, OnNativeRefresh);
			CometSwiftUIHost.SetRefreshEndedHandler(_native, OnNativeRefreshEnded);
		}

		void OnNativeTap() => _sink?.OnEvent(EventIds.Clicked);
		void OnNativeTapGesture() => _sink?.OnGesture(GestureKind.Tap, new GestureData(GestureState.Ended, default));
		void OnNativeLongPress() => _sink?.OnGesture(GestureKind.LongPress, new GestureData(GestureState.Ended, default));
		void OnNativeRecord(double state, double deltaX, double deltaY)
			=> _sink?.OnGesture(GestureKind.Pan, new GestureData(
				(GestureState)(int)state, default, new Point(deltaX, deltaY)));
		void OnNativeTextChanged(string s) => _sink?.OnEvent(EventIds.TextChanged, s);
		// TextField gained focus (gold onTextFieldFocused) → e.g. the composer closes an open selector panel.
		void OnNativeFocused() => _sink?.OnEvent(EventIds.Focused);
		void OnNativeCompleted() => _sink?.OnEvent(EventIds.Completed);
		void OnNativeToggled(bool b) => _sink?.OnEvent(EventIds.Toggled, b);
		void OnNativeValueChanged(double d) => _sink?.OnEvent(EventIds.ValueChanged, d);
		void OnNativeRefresh() => _sink?.OnEvent(EventIds.RefreshRequested);
		void OnNativeRefreshEnded() => _sink?.OnEvent(EventIds.RefreshEnded);

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (ApplyCommonProperty(_native, id, in value))
				return;

			if (id == PropertyIds.Text_Value || id == PropertyIds.Button_Text || id == PropertyIds.TextField_Text)
				CometSwiftUIHost.SetString(_native, "text", value.AsString ?? string.Empty);
			else if (id == PropertyIds.Text_Runs && value.AsObject is System.Collections.Generic.IReadOnlyList<TextRun> runs)
			{
				// FormattedText: push the styled runs so the shim renders per-run colour/monospace/
				// underline; also set the flattened text as a measure baseline + fallback if runs are empty.
				CometSwiftUIHost.SetString(_native, "text", string.Concat(System.Linq.Enumerable.Select(runs, r => r.Text)));
				CometSwiftUIHost.ClearTextRuns(_native);
				foreach (var r in runs)
					CometSwiftUIHost.AddTextRun(_native, r.Text ?? string.Empty,
						r.Color is { } c ? ToArgb(c) : 0, r.Color is not null,
						r.Monospace,
						r.Background is { } bg ? ToArgb(bg) : 0, r.Background is not null,
						r.Underline);
			}
			else if (id == PropertyIds.TextField_Placeholder)
				CometSwiftUIHost.SetString(_native, "placeholder", value.AsString ?? string.Empty);
			else if (id == PropertyIds.Image_Source)
				CometSwiftUIHost.SetString(_native, "imageurl", value.AsString ?? string.Empty);
			else if (id == PropertyIds.Image_Data && value.AsObject is byte[] imageData)
			{
				// Font-image declarations emit an empty byte payload before their typed font
				// parameters. Keep the already-rasterized image until the final glyph property
				// either replaces it or explicitly clears it.
				if (imageData.Length == 0 && !string.IsNullOrEmpty(_fontImageGlyph))
					return;
				using var data = Foundation.NSData.FromArray(imageData);
				CometSwiftUIHost.SetData(_native, data);
			}
			else if (id == PropertyIds.Image_FontFamily)
				_fontImageFamily = value.AsString ?? string.Empty;
			else if (id == PropertyIds.Image_FontSize)
				_fontImageSize = value.AsDouble;
			else if (id == PropertyIds.Image_FontWeight)
				_fontImageWeight = value.AsInt;
			else if (id == PropertyIds.Image_FontItalic)
				_fontImageItalic = value.AsBool;
			else if (id == PropertyIds.Image_FontColor)
				_fontImageColor = value.AsColor;
			else if (id == PropertyIds.Image_FontAutoScaling)
				_fontImageAutoScaling = value.AsBool;
			else if (id == PropertyIds.Image_FontGlyph)
			{
				_fontImageGlyph = value.AsString ?? string.Empty;
				UpdateFontImage();
			}
			else if (id == PropertyIds.Icon_Symbol)
				CometSwiftUIHost.SetString(_native, "icon", value.AsString ?? string.Empty);
			else if (id == PropertyIds.Icon_Glyph)
				CometSwiftUIHost.SetString(_native, "iconglyph", value.AsString ?? string.Empty);
			else if (id == PropertyIds.Icon_FontFamily)
				CometSwiftUIHost.SetString(_native, "iconfontfamily", value.AsString ?? string.Empty);
			else if (id == PropertyIds.Icon_Tint)
				CometSwiftUIHost.SetColor(
					_native,
					"textcolor",
					value.AsColor is { } it ? ToArgb(it) : 0);
			else if (id == PropertyIds.Icon_Size)
				CometSwiftUIHost.SetDouble(_native, "fontsize", value.AsDouble);
			else if (id == PropertyIds.Icon_FillFrame)
				CometSwiftUIHost.SetBool(_native, "iconfillframe", value.AsBool);
			else if (id == PropertyIds.Text_Italic)
			{
				_fontItalic = value.AsBool;
				CometSwiftUIHost.SetBool(_native, "fontitalic", value.AsBool);
			}
			else if (id == PropertyIds.Container_Card)
				// The iOS twin has no Card widget: give the hand-composed card the M3
				// resting elevation the real Android Card self-themes (was a Shadow token
				// before the AsCard promotion — this keeps the iOS render elevated).
				CometSwiftUIHost.SetDouble(_native, "elevation", value.AsBool ? 1 : 0);
			else if (id == PropertyIds.GradientBackground)
			{
				CometSwiftUIHost.ClearGradientStops(_native);
				if (value.AsObject is GradientSpec spec)
				{
					foreach (var stop in spec.Stops)
						CometSwiftUIHost.AddGradientStop(_native, ToArgb(stop));
					// 0 horizontal / 1 vertical / 2 diagonal — extent/offset/mirror (the
					// parallax fields) are an Android-first capability, documented deviation here.
					CometSwiftUIHost.SetDouble(_native, "gradientdirection", (double)spec.Direction);
				}
			}
			else if (id == PropertyIds.GradientBorder)
			{
				CometSwiftUIHost.ClearBorderGradientStops(_native);
				if (value.AsObject is GradientSpec borderSpec)
				{
					foreach (var stop in borderSpec.Stops)
						CometSwiftUIHost.AddBorderGradientStop(_native, ToArgb(stop));
					// Same 0/1/2/3 mapping as the background — Android honors the spec's
					// direction for borders via BuildGradientBrush, so the shim must too.
					CometSwiftUIHost.SetDouble(_native, "bordergradientdirection", (double)borderSpec.Direction);
				}
			}
			else if (id == PropertyIds.Toggle_IsOn)
				CometSwiftUIHost.SetBool(_native, "ison", value.AsBool);
			else if (id == PropertyIds.Refresh_IsRefreshing)
				CometSwiftUIHost.SetBool(_native, "refreshing", value.AsBool);
			else if (id == PropertyIds.Slider_Value)
				CometSwiftUIHost.SetDouble(_native, "value", value.AsDouble);
			else if (id == PropertyIds.Slider_Minimum)
				CometSwiftUIHost.SetDouble(_native, "slidermin", value.AsDouble);
			else if (id == PropertyIds.Slider_Maximum)
				CometSwiftUIHost.SetDouble(_native, "slidermax", value.AsDouble);
			else if (id == PropertyIds.HasTapGesture)
				CometSwiftUIHost.SetBool(_native, "hastapgesture", value.AsBool);
			else if (id == PropertyIds.HasLongPressGesture)
				CometSwiftUIHost.SetBool(_native, "haslongpressgesture", value.AsBool);
			else if (id == PropertyIds.HasRecordGesture)
				CometSwiftUIHost.SetBool(_native, "hasrecordgesture", value.AsBool);
			else if (id == PropertyIds.ClipShape)
				CometSwiftUIHost.SetBool(_native, "clipcircle", value.AsBool);
			else if (id == PropertyIds.Opacity)
				CometSwiftUIHost.SetDouble(_native, "opacity", value.AsDouble);
			else if (id == PropertyIds.IsVisible)
				CometSwiftUIHost.SetBool(_native, "isvisible", value.AsBool);
			else if (id == PropertyIds.TextField_Borderless)
				CometSwiftUIHost.SetBool(_native, "borderless", value.AsBool);
			else if (id == PropertyIds.TextField_FocusRequested && value.AsBool)
				CometSwiftUIHost.SetBool(_native, "focusrequested", true);
			else if (id == PropertyIds.TextField_Keyboard)
				CometSwiftUIHost.SetDouble(_native, "keyboardtype", value.AsInt);
			else if (id == PropertyIds.Button_Outlined)
				CometSwiftUIHost.SetBool(_native, "outlined", value.AsBool);
			else if (id == PropertyIds.Button_HasExplicitPadding)
				CometSwiftUIHost.SetBool(_native, "buttonhasexplicitpadding", value.AsBool);
			else if (id == PropertyIds.Text_Color ||
				id == PropertyIds.Button_TextColor ||
				id == PropertyIds.TextField_TextColor)
				CometSwiftUIHost.SetColor(
					_native,
					"textcolor",
					value.AsColor is { } tc ? ToArgb(tc) : 0);
			else if (id == PropertyIds.Text_FontSize)
			{
				_fontSize = value.AsDouble;
				CometSwiftUIHost.SetDouble(_native, "fontsize", value.AsDouble);
			}
			else if (id == PropertyIds.Text_MaxLines)
				CometSwiftUIHost.SetDouble(_native, "maxlines", value.AsInt);
			else if (id == PropertyIds.Text_CharacterSpacing)
				CometSwiftUIHost.SetDouble(_native, "characterspacing", value.AsDouble);
			else if (id == PropertyIds.Text_LineBreakMode)
				CometSwiftUIHost.SetDouble(_native, "linebreakmode", value.AsInt);
			else if (id == PropertyIds.Text_FontWeight)
			{
				_fontWeight = value.AsInt;
				CometSwiftUIHost.SetDouble(_native, "fontweight", value.AsInt);   // Int-kind, not Double
			}
			else if (id == PropertyIds.Text_FontFamily)
			{
				_fontFamily = value.AsString;
				CometSwiftUIHost.SetString(_native, "fontfamily", value.AsString ?? string.Empty);
			}
			else if (id == PropertyIds.BackgroundColor)
				CometSwiftUIHost.SetColor(
					_native,
					"background",
					value.AsColor is { } c ? ToArgb(c) : 0);
			else if (id == PropertyIds.Padding && value.AsObject is Microsoft.Maui.Thickness t)
			{
				CometSwiftUIHost.SetDouble(_native, "padding", t.Left);   // uniform value used by stacks
				CometSwiftUIHost.SetDouble(_native, "pad.l", t.Left);
				CometSwiftUIHost.SetDouble(_native, "pad.t", t.Top);
				CometSwiftUIHost.SetDouble(_native, "pad.r", t.Right);
				CometSwiftUIHost.SetDouble(_native, "pad.b", t.Bottom);
			}
			else if (id == PropertyIds.CornerRadius && value.AsObject is CornerRadii corners)
			{
				// Four SetDouble calls (reusing the bound host fn) carry per-corner radii.
				CometSwiftUIHost.SetDouble(_native, "corner.tl", corners.TopLeft);
				CometSwiftUIHost.SetDouble(_native, "corner.tr", corners.TopRight);
				CometSwiftUIHost.SetDouble(_native, "corner.br", corners.BottomRight);
				CometSwiftUIHost.SetDouble(_native, "corner.bl", corners.BottomLeft);
			}
			else if (id == PropertyIds.Shadow)
				CometSwiftUIHost.SetDouble(_native, "elevation", value.AsDouble);
			else if (id == PropertyIds.Border)
			{
				if (value.AsObject is BorderSpec border)
				{
					CometSwiftUIHost.SetDouble(_native, "borderwidth", border.Width);
					CometSwiftUIHost.SetColor(_native, "bordercolor", ToArgb(border.Color));
				}
				else
				{
					CometSwiftUIHost.SetDouble(_native, "borderwidth", 0);
					CometSwiftUIHost.SetColor(_native, "bordercolor", 0);
				}
			}
		}

		internal static bool ApplyCommonProperty(
			CometNode native,
			PropertyId id,
			in PropertyValue value)
		{
			if (id != PropertyIds.AutomationId)
				return false;
			CometSwiftUIHost.SetString(native, "automationid", value.AsString ?? string.Empty);
			return true;
		}

		public void InsertChild(int index, ICometBackendNode child)
		{
			var c = (ISwiftUINativeNode)child;
			_children.Insert(index, (ICometBackendNode)c);
			CometSwiftUIHost.InsertChild(_native, index, c.Native);
		}

		public void RemoveChildAt(int index)
		{
			_children.RemoveAt(index);
			CometSwiftUIHost.RemoveChild(_native, index);
		}

		public void MoveChild(int fromIndex, int toIndex)
		{
			var c = _children[fromIndex];
			_children.RemoveAt(fromIndex);
			CometSwiftUIHost.RemoveChild(_native, fromIndex);
			_children.Insert(toIndex, c);
			CometSwiftUIHost.InsertChild(_native, toIndex, ((ISwiftUINativeNode)c).Native);
		}

		// Yoga layout: a leaf's intrinsic size comes from SwiftUI (sizeThatFits), and the
		// Yoga-computed parent-relative frame is pushed back so the shim positions it absolutely.
		public Size Measure(double widthConstraint, double heightConstraint)
		{
			if (_kind == "image" && !string.IsNullOrEmpty(_fontImageGlyph))
				return _fontImageMeasuredSize;
			var size = CometSwiftUIHost.MeasureNode(_native, widthConstraint, heightConstraint);
			return new Size(size.Width, size.Height);
		}

		public void Arrange(Rect frame)
			=> CometSwiftUIHost.SetFrame(_native, frame.X, frame.Y, frame.Width, frame.Height);

		// First-baseline offset from UIFont metrics: the ascender is the distance from the
		// top of the line box to the baseline, which is where SwiftUI draws a single-line
		// Text's first baseline. Replaces the old null→height fallback so .AlignBaseline()
		// and .BaselineHeight() rows line up on iOS too (Compose measures its own engine for
		// exactness; UIFont metrics are the correct iOS analog).
		public double? MeasureBaseline(double width, double height)
		{
			if (_kind != "text")
				return null;
			using var font = SwiftUIFontResolver.Resolve(
				_fontFamily,
				_fontSize ?? 17,
				_fontWeight,
				_fontItalic);
			return (double)font.Ascender;
		}

		// Baseline-height inset (gold baselineHeight): pad the leaf content down so its first baseline
		// lands at the requested offset — the iOS counterpart of ComposeNode's _contentTopInset.
		public void SetContentTopInset(double dp) => CometSwiftUIHost.SetDouble(_native, "contenttopinset", dp);

		public void SetEventSink(ICometEventSink? sink) => _sink = sink;
		public void Dispose() { }

		void UpdateFontImage()
		{
			if (_kind != "image")
				return;

			if (string.IsNullOrEmpty(_fontImageGlyph))
			{
				_renderedFontImage = null;
				_fontImageMeasuredSize = Size.Zero;
				using var empty = Foundation.NSData.FromArray(System.Array.Empty<byte>());
				CometSwiftUIHost.SetData(_native, empty);
				return;
			}

			var color = _fontImageColor ?? Microsoft.Maui.Graphics.Colors.White;
			var argb = ((uint)(color.Alpha * 255) << 24) |
				((uint)(color.Red * 255) << 16) |
				((uint)(color.Green * 255) << 8) |
				(uint)(color.Blue * 255);
			var spec = new SwiftUIFontImageSpec(
				_fontImageGlyph,
				_fontImageFamily,
				_fontImageSize,
				_fontImageWeight,
				_fontImageItalic,
				_fontImageAutoScaling,
				argb);
			if (_renderedFontImage is { } rendered && rendered.Equals(spec))
				return;

			_renderedFontImage = spec;
			using var data = SwiftUIFontImageRasterizer.Render(spec, out _fontImageMeasuredSize);
			if (data is not null)
				CometSwiftUIHost.SetData(_native, data);
			else
			{
				using var empty = Foundation.NSData.FromArray(System.Array.Empty<byte>());
				CometSwiftUIHost.SetData(_native, empty);
			}
		}

		static uint ToArgb(Color c) =>
			((uint)(c.Alpha * 255) << 24) | ((uint)(c.Red * 255) << 16) |
			((uint)(c.Green * 255) << 8) | (uint)(c.Blue * 255);
	}
}
#endif
