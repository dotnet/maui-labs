#nullable enable
#if IOS
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.SwiftUI
{
	/// <summary>Renders a Comet <see cref="Comet.TabView"/> as a REAL native SwiftUI
	/// <c>TabView(selection:)</c> with <c>.tabItem</c> on each child — the actual iOS
	/// <c>UITabBar</c> rendered by SwiftUI, not a hand-composed look-alike. Each tab's
	/// <c>text</c> and <c>iconName</c> properties on the child <c>CometNode</c> drive the
	/// native <c>.tabItem { Image(systemName:); Text() }</c> slots. Selection routes
	/// through <see cref="TabView.SelectItem"/> via the <c>onSelectionChanged</c> callback.</summary>
	sealed class SwiftUITabViewNode : ICometBackendNode, IBackendManagesOwnContent, ISwiftUINativeNode
	{
		readonly CometNode _native;
		readonly BackendContext _context;
		ICometEventSink? _sink;
		TabView _tabView;
		bool _built;
		OwnedContentGeneration? _generation;

		public CometNode Native => _native;

		public SwiftUITabViewNode(TabView tabView, BackendContext context)
		{
			_tabView = tabView;
			_context = context;
			_native = CometSwiftUIHost.MakeNode("tabview");
			CometSwiftUIHost.SetSelectionChangedHandler(_native, OnNativeSelectionChanged);
		}

		void OnNativeSelectionChanged(double index)
		{
			int idx = (int)index;
			_tabView.SelectItem(idx);
		}

		void EnsureChildren()
		{
			if (_built) return;
			_built = true;

			var tabs = _tabView.Tabs;
			var generation = new OwnedContentGeneration(_tabView, _context);
			try
			{
				for (int i = 0; i < tabs.Count; i++)
				{
					var tab = tabs[i];
					// Each child CometNode's text and iconName properties drive the native
					// .tabItem { Image(systemName:); Text() } slots in the Swift shim.
					var childNode = CometSwiftUIHost.MakeNode("vstack");
					CometSwiftUIHost.SetString(childNode, "text", tab.Title ?? string.Empty);
					if (tab.Icon is { Length: > 0 } icon)
						CometSwiftUIHost.SetString(childNode, "icon", icon);

					if (tab.Content is { } content)
					{
						var contentNode = (ISwiftUINativeNode)generation.Materialize(content);
						CometSwiftUIHost.InsertChild(childNode, 0, contentNode.Native);
					}
					CometSwiftUIHost.InsertChild(_native, i, childNode);
				}
				_generation = generation;
			}
			catch
			{
				generation.Dispose();
				throw;
			}
		}

		void DisposeGeneration()
		{
			if (_generation is not { } generation)
				return;
			_generation = null;
			generation.Dispose();
		}

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Nav_SelectedIndex)
				CometSwiftUIHost.SetDouble(_native, "selectedindex", value.AsInt);
			else if (id == PropertyIds.BackgroundColor)
				CometSwiftUIHost.SetColor(_native, "background",
					value.AsColor is { } c
						? ((uint)(c.Alpha * 255) << 24) | ((uint)(c.Red * 255) << 16) |
							((uint)(c.Green * 255) << 8) | (uint)(c.Blue * 255)
						: 0);
		}

		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }

		public Size Measure(double widthConstraint, double heightConstraint)
		{
			var b = UIKit.UIScreen.MainScreen.Bounds;
			double w = double.IsFinite(widthConstraint) && widthConstraint > 0 ? widthConstraint : b.Width;
			double h = double.IsFinite(heightConstraint) && heightConstraint > 0 ? heightConstraint : b.Height;
			return new Size(w, h);
		}

		public void Arrange(Rect frame)
		{
			CometSwiftUIHost.SetFrame(_native, frame.X, frame.Y, frame.Width, frame.Height);
			EnsureChildren();
		}

		public void SetEventSink(ICometEventSink? sink) => _sink = sink;

		public void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is TabView tv)
			{
				_tabView = tv;
				// Always rebuild: a theme/root rebuild replaces the TabView instance with
				// new tab content Views. The old native children reference stale content.
				// Selection index is preserved (the SwiftUI TabView binding reads
				// node.selectedIndex which C# re-pushes via ApplyAllSetProperties).
				_built = false;
				DisposeGeneration();
				CometSwiftUIHost.ClearChildren(_native);
				EnsureChildren();
			}
		}

		public void Dispose()
		{
			DisposeGeneration();
		}
	}
}
#endif
