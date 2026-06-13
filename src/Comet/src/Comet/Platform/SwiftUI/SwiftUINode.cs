#nullable enable
#if IOS
using System.Collections.Generic;
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.SwiftUI
{
	/// <summary>
	/// A retained backend node that bridges Comet's diff to a Swift <see cref="CometNode"/>
	/// in the SwiftUI tree (the iOS counterpart of the Compose <c>ComposeNode</c>). Unlike
	/// the Compose backend — which needs a distinct class per control — the SwiftUI shim is
	/// kind-driven, so one node type parameterized by a kind string suffices.
	/// </summary>
	sealed class SwiftUINode : ICometBackendNode
	{
		readonly CometNode _native;
		readonly List<SwiftUINode> _children = new();
		ICometEventSink? _sink;

		public CometNode Native => _native;

		public SwiftUINode(string kind)
		{
			_native = CometSwiftUIHost.MakeNode(kind);
			// Route native taps (e.g. a SwiftUI Button) back through the event sink.
			// Harmless for non-interactive kinds (their handler never fires).
			CometSwiftUIHost.SetTapHandler(_native, OnNativeTap);
		}

		void OnNativeTap() => _sink?.OnEvent(EventIds.Clicked);

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Text_Value || id == PropertyIds.Button_Text)
				CometSwiftUIHost.SetString(_native, "text", value.AsString ?? string.Empty);
			else if (id == PropertyIds.BackgroundColor && value.AsColor is { } c)
				CometSwiftUIHost.SetColor(_native, "background", ToArgb(c));
			else if (id == PropertyIds.Padding && value.AsObject is Microsoft.Maui.Thickness t)
				CometSwiftUIHost.SetDouble(_native, "padding", t.Left);
		}

		public void InsertChild(int index, ICometBackendNode child)
		{
			var c = (SwiftUINode)child;
			_children.Insert(index, c);
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
			CometSwiftUIHost.InsertChild(_native, toIndex, c.Native);
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
