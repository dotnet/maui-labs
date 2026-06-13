#nullable enable
#if IOS
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.SwiftUI
{
	/// <summary>
	/// Renders a Comet <c>ListView</c> as a SwiftUI <c>List</c> (the iOS counterpart of
	/// <c>ComposeListNode</c>). Owns its rows (so it implements
	/// <see cref="IBackendManagesOwnContent"/>): on a data-version change it materializes
	/// each row's template view into a child node of the native "list" node, which the
	/// shim renders via <c>List { ForEach … }</c> (SwiftUI lazily realizes row views).
	/// </summary>
	sealed class SwiftUIListNode : ICometBackendNode, IBackendManagesOwnContent, ISwiftUINativeNode
	{
		readonly IListView _list;
		readonly BackendContext _context;
		readonly CometNode _native;

		public CometNode Native => _native;

		public SwiftUIListNode(IListView list, BackendContext context)
		{
			_list = list;
			_context = context;
			_native = CometSwiftUIHost.MakeNode("list");
		}

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.List_Version)
				Rebuild();
		}

		void Rebuild()
		{
			// Drop the previous rows from the dev tree (they register under the ListView).
			if (_list is View listView)
				Comet.DevTools.CometDevRegistry.UnregisterSubtree(listView, includeRoot: false);

			CometSwiftUIHost.ClearChildren(_native);
			int count = _list.Sections() > 0 ? _list.Rows(0) : 0;
			for (int i = 0; i < count; i++)
			{
				var view = _list.ViewFor(0, i);
				var node = (ISwiftUINativeNode)CometBackendBridge.Materialize(view, _context, _list as View);
				CometSwiftUIHost.InsertChild(_native, i, node.Native);
			}
		}

		// The node manages its own rows; the generic child API is unused.
		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }
		public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;
		public void Arrange(Rect frame) { }
		public void SetEventSink(ICometEventSink? sink) { }
		public void Dispose() { }
	}
}
#endif
