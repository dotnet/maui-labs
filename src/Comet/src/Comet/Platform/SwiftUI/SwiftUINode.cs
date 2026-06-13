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
		ICometEventSink? _sink;

		public CometNode Native => _native;

		public SwiftUINode(string kind)
		{
			_native = CometSwiftUIHost.MakeNode(kind);
			// Route native events back through the event sink. Harmless for kinds that
			// don't raise a given event (their handler simply never fires).
			CometSwiftUIHost.SetTapHandler(_native, OnNativeTap);
			CometSwiftUIHost.SetTapGestureHandler(_native, OnNativeTapGesture);
			CometSwiftUIHost.SetStringChangeHandler(_native, OnNativeTextChanged);
			CometSwiftUIHost.SetBoolChangeHandler(_native, OnNativeToggled);
			CometSwiftUIHost.SetDoubleChangeHandler(_native, OnNativeValueChanged);
		}

		void OnNativeTap() => _sink?.OnEvent(EventIds.Clicked);
		void OnNativeTapGesture() => _sink?.OnGesture(GestureKind.Tap, new GestureData(GestureState.Ended, default));
		void OnNativeTextChanged(string s) => _sink?.OnEvent(EventIds.TextChanged, s);
		void OnNativeToggled(bool b) => _sink?.OnEvent(EventIds.Toggled, b);
		void OnNativeValueChanged(double d) => _sink?.OnEvent(EventIds.ValueChanged, d);

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Text_Value || id == PropertyIds.Button_Text || id == PropertyIds.TextField_Text)
				CometSwiftUIHost.SetString(_native, "text", value.AsString ?? string.Empty);
			else if (id == PropertyIds.TextField_Placeholder)
				CometSwiftUIHost.SetString(_native, "placeholder", value.AsString ?? string.Empty);
			else if (id == PropertyIds.Image_Source)
				CometSwiftUIHost.SetString(_native, "imageurl", value.AsString ?? string.Empty);
			else if (id == PropertyIds.Icon_Symbol)
				CometSwiftUIHost.SetString(_native, "icon", value.AsString ?? string.Empty);
			else if (id == PropertyIds.Icon_Tint && value.AsColor is { } it)
				CometSwiftUIHost.SetColor(_native, "textcolor", ToArgb(it));
			else if (id == PropertyIds.Icon_Size)
				CometSwiftUIHost.SetDouble(_native, "fontsize", value.AsDouble);
			else if (id == PropertyIds.Toggle_IsOn)
				CometSwiftUIHost.SetBool(_native, "ison", value.AsBool);
			else if (id == PropertyIds.Slider_Value)
				CometSwiftUIHost.SetDouble(_native, "value", value.AsDouble);
			else if (id == PropertyIds.HasTapGesture)
				CometSwiftUIHost.SetBool(_native, "hastapgesture", value.AsBool);
			else if (id == PropertyIds.Text_Color && value.AsColor is { } tc)
				CometSwiftUIHost.SetColor(_native, "textcolor", ToArgb(tc));
			else if (id == PropertyIds.Text_FontSize)
				CometSwiftUIHost.SetDouble(_native, "fontsize", value.AsDouble);
			else if (id == PropertyIds.Text_FontWeight)
				CometSwiftUIHost.SetDouble(_native, "fontweight", value.AsDouble);
			else if (id == PropertyIds.BackgroundColor && value.AsColor is { } c)
				CometSwiftUIHost.SetColor(_native, "background", ToArgb(c));
			else if (id == PropertyIds.Padding && value.AsObject is Microsoft.Maui.Thickness t)
				CometSwiftUIHost.SetDouble(_native, "padding", t.Left);
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
			else if (id == PropertyIds.Border && value.AsObject is BorderSpec border)
			{
				CometSwiftUIHost.SetDouble(_native, "borderwidth", border.Width);
				CometSwiftUIHost.SetColor(_native, "bordercolor", ToArgb(border.Color));
			}
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
			var size = CometSwiftUIHost.MeasureNode(_native, widthConstraint, heightConstraint);
			return new Size(size.Width, size.Height);
		}

		public void Arrange(Rect frame)
			=> CometSwiftUIHost.SetFrame(_native, frame.X, frame.Y, frame.Width, frame.Height);

		public void SetEventSink(ICometEventSink? sink) => _sink = sink;
		public void Dispose() { }

		static uint ToArgb(Color c) =>
			((uint)(c.Alpha * 255) << 24) | ((uint)(c.Red * 255) << 16) |
			((uint)(c.Green * 255) << 8) | (uint)(c.Blue * 255);
	}
}
#endif
