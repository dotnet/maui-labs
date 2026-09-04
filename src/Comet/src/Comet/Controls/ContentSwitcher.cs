#nullable enable
using System.Collections.Generic;
using Comet.Reactive;

namespace Comet
{
	/// <summary>
	/// An index-driven full-slot content swap: shows <c>Views[Index]</c>, switching
	/// reactively as the signal changes — the routing primitive for NavigationSuite content
	/// (Reply's four destinations; JetNews routes). The swap lives inside the backend node
	/// (MutableState, the NavigationSuite idiom) because body-level structure swaps don't
	/// reach the retained node tree. Unlike <see cref="SelectorPanel"/> (a collapsible input
	/// footer), every view fills the host's bounds and index 0 is an ordinary view.
	/// </summary>
	public partial class ContentSwitcher : View, IContainerView
	{
		public ContentSwitcher(Signal<int> index, IReadOnlyList<View> views)
		{
			Index = index;
			Views = views;
			foreach (var v in views)
				v.Parent = this;
		}

		public Signal<int> Index { get; }
		public IReadOnlyList<View> Views { get; }

		public IReadOnlyList<View> GetChildren() => Views;
	}
}
