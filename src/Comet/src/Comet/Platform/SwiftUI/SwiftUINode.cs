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
			CometSwiftUIHost.SetStringChangeHandler(_native, OnNativeTextChanged);
			CometSwiftUIHost.SetBoolChangeHandler(_native, OnNativeToggled);
			CometSwiftUIHost.SetDoubleChangeHandler(_native, OnNativeValueChanged);
		}

		void OnNativeTap() => _sink?.OnEvent(EventIds.Clicked);
		void OnNativeTextChanged(string s) => _sink?.OnEvent(EventIds.TextChanged, s);
		void OnNativeToggled(bool b) => _sink?.OnEvent(EventIds.Toggled, b);
		void OnNativeValueChanged(double d) => _sink?.OnEvent(EventIds.ValueChanged, d);

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Text_Value || id == PropertyIds.Button_Text || id == PropertyIds.TextField_Text)
				CometSwiftUIHost.SetString(_native, "text", value.AsString ?? string.Empty);
			else if (id == PropertyIds.TextField_Placeholder)
				CometSwiftUIHost.SetString(_native, "placeholder", value.AsString ?? string.Empty);
			else if (id == PropertyIds.Toggle_IsOn)
				CometSwiftUIHost.SetBool(_native, "ison", value.AsBool);
			else if (id == PropertyIds.Slider_Value)
				CometSwiftUIHost.SetDouble(_native, "value", value.AsDouble);
			else if (id == PropertyIds.BackgroundColor && value.AsColor is { } c)
				CometSwiftUIHost.SetColor(_native, "background", ToArgb(c));
			else if (id == PropertyIds.Padding && value.AsObject is Microsoft.Maui.Thickness t)
				CometSwiftUIHost.SetDouble(_native, "padding", t.Left);
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

		// SwiftUI lays out its own tree (as Compose does today); Yoga positioning is a
		// later, cross-backend step.
		public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;
		public void Arrange(Rect frame) { }

		public void SetEventSink(ICometEventSink? sink) => _sink = sink;
		public void Dispose() { }

		static uint ToArgb(Color c) =>
			((uint)(c.Alpha * 255) << 24) | ((uint)(c.Red * 255) << 16) |
			((uint)(c.Green * 255) << 8) | (uint)(c.Blue * 255);
	}
}
#endif
