#nullable enable
#if ANDROID
using System.Collections.Generic;
using System.Linq;
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using ComposeLazyColumn = AndroidX.Compose.LazyColumn<int>;

namespace Comet.Platform.Compose
{
	/// <summary>
	/// Renders a Comet <c>ListView</c>/<c>CollectionView</c> as a Compose
	/// <c>LazyColumn</c> — genuinely virtualized: each row's template
	/// <see cref="View"/> is materialized into a backend node only when Compose
	/// composes that row (i.e. when it scrolls into view).
	/// </summary>
	/// <remarks>
	/// Single-section lists (the common case) are flattened to a flat row index.
	/// Data changes bump a version <see cref="MutableState{T}"/> via
	/// <see cref="ApplyProperty"/> so the LazyColumn recomposes against the new rows.
	/// </remarks>
	sealed class ComposeListNode : ComposeNode
	{
		readonly IListView _list;
		readonly BackendContext _context;
		readonly MutableState<int> _version = new(0);

		public ComposeListNode(IListView list, BackendContext context)
		{
			_list = list;
			_context = context;
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.List_Version)
				_version.Value++; // recompose against the latest rows
		}

		public override void Render(IComposer composer)
		{
			_ = _version.Value; // subscribe so data changes recompose the list

			// Single-section (the common case); multi-section flattening is a follow-up.
			int count = _list.Sections() > 0 ? _list.Rows(0) : 0;
			var indices = Enumerable.Range(0, count).ToList();

			// Under Yoga, lay each row out to the list's arranged width so rows render identically
			// to the rest of the tree (and to iOS). FrameWidth is 0 until the engine arranges this
			// list, so fall back to the screen width.
			bool yoga = HasFrame;
			double rowWidth = FrameWidth > 0
				? FrameWidth
				: global::Android.Content.Res.Resources.System!.DisplayMetrics!.WidthPixels / ComposeNode.Density;

			var lazy = new ComposeLazyColumn(indices, i =>
			{
				// Materialized lazily by Compose for visible rows only.
				var view = _list.ViewFor(0, i);
				var node = (ComposableNode)CometBackendBridge.Materialize(view, _context);

				// Drive the same Yoga pass the root uses, but width-pinned / height-wrapped so the
				// row's own size comes from its content — the row then carries absolute child frames
				// and renders as a self-positioning Box (see ComposeStackNode), matching iOS.
				if (yoga)
					CometBackendLayoutEngine.LayoutContent(view, rowWidth);

				return node;
			});

			// Position + size the list from its Yoga frame (offset below the top bar, sized to the
			// remaining height) so it scrolls within its slot rather than laying out at the origin.
			((ComposableNode)lazy).Modifier = BuildNodeModifier();
			lazy.Render(composer);
		}
	}
}
#endif
