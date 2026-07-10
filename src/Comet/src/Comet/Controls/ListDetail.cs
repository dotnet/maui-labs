#nullable enable
using System.Collections.Generic;
using Comet.Reactive;

namespace Comet
{
	/// <summary>
	/// The adaptive list-detail primitive (docs/adaptive-primitives-design.md): two panes at
	/// expanded widths (the gold's accompanist <c>TwoPane</c> — 50/50 split, 16dp gap, ≥840dp),
	/// a single pane below that where <see cref="IsDetailOpen"/> swaps list ↔ detail (the gold's
	/// full-screen detail with a <c>BackHandler</c> that closes it — the node raises the
	/// back-press as an event and this control writes the signal back to false). The pane swap
	/// lives inside the backend node (MutableState — the <see cref="NavigationSuite"/> idiom)
	/// because body-level structure swaps don't reach the retained node tree.
	/// </summary>
	public partial class ListDetail : View, IContainerView
	{
		public ListDetail(Signal<bool> isDetailOpen, View list, View detail, double listFraction = 0.5)
		{
			IsDetailOpen = isDetailOpen;
			List = list;
			Detail = detail;
			ListFraction = listFraction;
			list.Parent = this;
			detail.Parent = this;
		}

		/// <summary>Compact: true shows the detail pane full-screen (back writes it false).
		/// Expanded: both panes always show — the signal only tracks what the detail displays.</summary>
		public Signal<bool> IsDetailOpen { get; }
		public View List { get; }
		public View Detail { get; }

		/// <summary>Two-pane list share of the width. Reply: 0.5 (50/50 TwoPane); JetNews'
		/// ListDetailScene keeps the list ≈ a third of an expanded window.</summary>
		public double ListFraction { get; }

		/// <summary>The gold two-pane threshold: WindowWidthSizeClass.Expanded (≥ 840dp) —
		/// ReplyApp.kt:76-88 (folding postures out of scope). Pure for host tests + every backend.</summary>
		public static bool TwoPaneFor(double widthDp) => widthDp >= 840;

		/// <summary>The gold split: 50/50 with a 16dp gap (ReplyListContent.kt:98).</summary>
		public const double GapDp = 16;

		public IReadOnlyList<View> GetChildren() => new[] { List, Detail };
	}
}
