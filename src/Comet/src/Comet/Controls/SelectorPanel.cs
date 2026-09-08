#nullable enable
using System.Collections.Generic;
using Comet.Reactive;

namespace Comet
{
	/// <summary>
	/// An inline, reactive content-swap panel: shows one of <see cref="Panels"/> chosen by an integer
	/// <see cref="Selector"/> signal. Index <c>0</c> (and any null / out-of-range slot) shows nothing,
	/// collapsing the panel to zero height. Maps to the platform's expandable input panel — on Android
	/// the gold-standard Jetchat's <c>SelectorExpanded</c> (a <c>Surface</c> that swaps an emoji table /
	/// "not available" pane by <c>InputSelector</c>); iOS gets a no-op twin.
	/// <para>The active panel's measured height feeds the shared Yoga engine, so opening it grows this
	/// view (and shrinks a sibling flex slot, e.g. the message list) — exactly the gold's bottom-anchored
	/// <c>Surface(tonalElevation)</c> growth, reflowed after each reactive flush. The backend node
	/// intercepts the system back press to dismiss, writing <see cref="Selector"/> back to <c>0</c>.</para>
	/// </summary>
	public partial class SelectorPanel : View, IContainerView
	{
		public SelectorPanel(Signal<int> selector, IReadOnlyList<View?> panels)
		{
			Selector = selector;
			Panels = panels;
			foreach (var p in panels)
				if (p is not null)
					p.Parent = this;
		}

		/// <summary>The active panel index. <c>0</c> (and any null slot) collapses the panel; index
		/// <c>k</c> shows <c>Panels[k]</c>. The node writes it back to <c>0</c> on a back-press dismiss.</summary>
		public Signal<int> Selector { get; }

		/// <summary>Candidate panels, indexed by <see cref="Selector"/> value. Null slots render nothing
		/// (e.g. the collapsed state, or a selector handled by a separate dialog).</summary>
		public IReadOnlyList<View?> Panels { get; }

		public IReadOnlyList<View> GetChildren()
		{
			var children = new List<View>();
			foreach (var p in Panels)
				if (p is not null)
					children.Add(p);
			return children;
		}
	}
}
